using Cheetah.Networking;
using ProtoBuf;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Cheetah.LaserTools
{
    public abstract partial class LaserToolBase : MyGameLogicComponent
    {
        protected IMyShipToolBase Tool { get; private set; }
        bool IsWelder => Tool is IMyShipWelder;
        bool IsGrinder => Tool is IMyShipGrinder;
        bool IsDrill => Tool is IMyShipDrill;
        bool NeedsInventoryCasheRefresh = true;
        int ConnectedGrids = 0;
        protected float WorkCoefficient => MyShipGrinderConstants.GRINDER_COOLDOWN_IN_MILISECONDS * 0.001f;
        protected float GrinderSpeed => MyAPIGateway.Session.GrinderSpeedMultiplier * MyShipGrinderConstants.GRINDER_AMOUNT_PER_SECOND * WorkCoefficient / 4;
        protected float WelderSpeed => MyAPIGateway.Session.WelderSpeedMultiplier * 2 * WorkCoefficient / 4; // 2 is WELDER_AMOUNT_PER_SECOND from MyShipWelder.cs
        protected float WelderBoneRepairSpeed => 0.6f * WorkCoefficient; // 0.6f is WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED from MyShipWelder.cs
        protected IMyInventory ToolCargo { get; private set; }
        protected HashSet<IMyCubeBlock> OnboardInventoryOwners = new HashSet<IMyCubeBlock>();
        protected IMyCubeGrid ToolGrid => Tool.CubeGrid;
        protected IMyGridTerminalSystem Term => ToolGrid.GetTerminalSystem();
        MyResourceSinkComponent MyPowerSink;
        float SuppliedPowerRatio => Tool.ResourceSink.SuppliedRatioByType(Electricity);
        public static MyDefinitionId Electricity { get; } = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        /// <summary>
        /// Grid block size, in meters.
        /// </summary>
        protected float GridBlockSize => ToolGrid.GridSize;
        protected Vector3I BlockDimensions => (Tool.SlimBlock.BlockDefinition as MyCubeBlockDefinition).Size;
        protected Vector3D BlockPosition => Tool.GetPosition();
        AutoSet<bool> SyncDistanceMode;
        public bool DistanceMode
        {
            get { return SyncDistanceMode.Get(); }
            set { SyncDistanceMode.Set(value); }
        }
        AutoSet<float> SyncBeamLength;
        public int BeamLength
        {
            get
            {
                if (IsDrill) return 10;
                return (int)SyncBeamLength.Get();
            }
            set { if(!IsDrill) SyncBeamLength.Set(value); }
        }
        AutoSet<float> SyncSpeedMultiplier;
        public int SpeedMultiplier
        {
            get { return (int)SyncSpeedMultiplier.Get(); }
            set { SyncSpeedMultiplier.Set(value); }
        }
        public int MinBeamLengthBlocks => 1;
        public int MaxBeamLengthBlocks => ToolGrid.GridSizeEnum == MyCubeSize.Small ? 30 : 8;
        public float MinBeamLengthM => MinBeamLengthBlocks * GridBlockSize;
        public float MaxBeamLengthM => MaxBeamLengthBlocks * GridBlockSize;
        Vector3D BlockForwardEnd => Tool.WorldMatrix.Forward * GridBlockSize * (BlockDimensions.Z) / 2;
        Vector3 LaserEmitterPosition
        {
            get
            {
                var EmitterDummy = Tool.Model.GetDummy("Laser_Emitter");
                return EmitterDummy != null ? EmitterDummy.Matrix.Translation : (Vector3)BlockForwardEnd;
            }
        }
        protected Vector3D BeamStart => BlockPosition + LaserEmitterPosition;
        protected Vector3D BeamEnd => BeamStart + Tool.WorldMatrix.Forward * BeamLength * GridBlockSize * SuppliedPowerRatio;
        Color InternalBeamColor { get; } = Color.WhiteSmoke;
        Color ExternalWeldBeamColor { get; } = Color.DeepSkyBlue;
        Color ExternalGrindBeamColor { get; } = Color.IndianRed;
        Color ExternalDrillBeamColor { get; } = Color.Gold;
        protected IMyHudNotification DebugNote;
        System.Diagnostics.Stopwatch Watch = new System.Diagnostics.Stopwatch();
        /// <summary>
        /// In milliseconds
        /// </summary>
        Queue<float> LastRunTimes = new Queue<float>();
        const float RunTimeCacheSize = 120;
        bool RunTimesAvailable => LastRunTimes.Count > 0;
        float AvgRunTime => LastRunTimes.Average();
        float MaxRunTime => LastRunTimes.Max();
        ushort Ticks = 0;
        ushort InventoryUpdateTicks = 0;

        protected HashSet<IMySlimBlock> UnbuiltBlocks = new HashSet<IMySlimBlock>();
        protected float GridAvailablePower { get; private set; }
        protected bool HasEnoughPower => GridAvailablePower > PowerConsumptionFunc(true);
        protected IMyPlayer Owner { get; private set; }


        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            Tool = Entity as IMyShipToolBase;
            //Tool.ResourceSink.SetRequiredInputFuncByType(Electricity, () => PowerConsumptionFunc());
            try
            {
                if (!Tool.HasComponent<MyModStorageComponent>())
                {
                    Tool.Storage = new MyModStorageComponent();
                    Tool.Components.Add(Tool.Storage);
                    SessionCore.DebugWrite($"{Tool.CustomName}.Init()", "Block doesn't have a Storage component!", IsExcessive: false);
                }
            }
            catch { }
        }

        #region Loading stuff
        [ProtoContract]
        public struct Persistent
        {
            [ProtoMember(1)]
            public float BeamLength;
            [ProtoMember(2)]
            public bool DistanceBased;
            [ProtoMember(3)]
            public float SpeedMultiplier;
        }

        public void Load()
        {
            try
            {
                if (MyAPIGateway.Multiplayer.MultiplayerActive && !MyAPIGateway.Multiplayer.IsServer)
                {
                    SyncBeamLength.Ask();
                    SyncDistanceMode.Ask();
                    SyncSpeedMultiplier.Ask();
                    return;
                }
                string Storage = "";
                //if (Tool.Storage.ContainsKey(SessionCore.StorageGuid))
                if (Tool.Storage?.TryGetValue(SessionCore.StorageGuid, out Storage) == true ||
                    MyAPIGateway.Utilities.GetVariable($"settings_{Tool.EntityId}", out Storage))
                {
                    try
                    {
                        Persistent persistent = MyAPIGateway.Utilities.SerializeFromBinary<Persistent>(Convert.FromBase64String(Storage));
                        SyncBeamLength.Set(persistent.BeamLength);
                        SyncDistanceMode.Set(persistent.DistanceBased);
                        SyncSpeedMultiplier.Set(persistent.SpeedMultiplier);
                        SessionCore.DebugWrite($"{Tool.CustomName}.Load()", $"Loaded from storage. Persistent Beamlength: {persistent.BeamLength}; Sync Beamlength: {SyncBeamLength.Get()}");
                    }
                    catch (Exception Scrap)
                    {
                        SessionCore.LogError($"{Tool.CustomName}.Load()", Scrap);
                    }
                }
                else
                {
                    SessionCore.DebugWrite($"{Tool.CustomName}.Load()", "Storage access failed.");
                }
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Tool.CustomName}.Load().AccessStorage", Scrap);
            }
        }

        public void Save()
        {
            try
            {
                Persistent persistent;
                persistent.BeamLength = SyncBeamLength.Get();
                persistent.DistanceBased = SyncDistanceMode.Get();
                persistent.SpeedMultiplier = SyncSpeedMultiplier.Get();
                string Raw = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(persistent));
                MyAPIGateway.Utilities.SetVariable($"settings_{Tool.EntityId}", Raw);
                Tool.Storage?.AddOrUpdate(SessionCore.StorageGuid, Raw);
                SessionCore.DebugWrite($"{Tool.CustomName}.Load()", $"Set settings to storage. Saved beamlength: {persistent.BeamLength}");
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Tool.CustomName}.Save()", Scrap);
            }
        }

        public override void Close()
        {
            try
            {
                if (SessionCore.Debug)
                {
                    DebugNote.Hide();
                    DebugNote.AliveTime = 0;
                    DebugNote = null;
                }
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Tool.CustomName}.Close().DebugClose", Scrap);
            }
            try
            {
                SessionCore.SaveUnregister(Save);
                SyncBeamLength.GotValueFromServer -= Tool.UpdateVisual;
                SyncDistanceMode.GotValueFromServer -= Tool.UpdateVisual;
                SyncSpeedMultiplier.GotValueFromServer -= Tool.UpdateVisual;

                Tool.CubeGrid.OnBlockAdded -= gridBlockChanged;
                Tool.CubeGrid.OnBlockRemoved -= gridBlockChanged;
                Tool.CubeGrid.OnBlockIntegrityChanged -= gridBlockChanged;
                Tool.CubeGrid.OnBlockOwnershipChanged -= gridOwnershipChanged;

                SyncBeamLength.Close();
                SyncDistanceMode.Close();
                SyncSpeedMultiplier.Close();
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Tool.CustomName}.Close()", Scrap);
            }
        }
        #endregion

        private void gridBlockChanged(IMySlimBlock trash)
        {
            NeedsInventoryCasheRefresh = true;
        }

        private void gridOwnershipChanged(IMyEntity trash)
        {
            NeedsInventoryCasheRefresh = true;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                if (Tool.CubeGrid.Physics?.Enabled != true || !Networker.Inited)
                {
                    NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                    if (!Networker.Inited) Networker.Init(SessionCore.ModID);
                    return;
                }
                ToolCargo = Tool.GetInventory();
                Tool.CubeGrid.OnBlockAdded += gridBlockChanged;
                Tool.CubeGrid.OnBlockRemoved += gridBlockChanged;
                Tool.CubeGrid.OnBlockIntegrityChanged += gridBlockChanged;
                Tool.CubeGrid.OnBlockOwnershipChanged += gridOwnershipChanged;

                SyncBeamLength = new AutoSet<float>(Tool, "BeamLength", 1, Checker: val => val >= MinBeamLengthBlocks && val <= MaxBeamLengthBlocks);
                SyncDistanceMode = new AutoSet<bool>(Tool, "DistanceBasedMode");
                SyncSpeedMultiplier = new AutoSet<float>(Tool, "SpeedMultiplier", 1, Checker: val => val >= 1 && val <= 4);
                SyncBeamLength.GotValueFromServer += Tool.UpdateVisual;
                SyncDistanceMode.GotValueFromServer += Tool.UpdateVisual;
                SyncSpeedMultiplier.GotValueFromServer += Tool.UpdateVisual;

                CheckInitControls();
                Load();

                Tool.AppendingCustomInfo += Tool_AppendingCustomInfo;
                SessionCore.SaveRegister(Save);
                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
                DebugNote = MyAPIGateway.Utilities.CreateNotification($"{Tool.CustomName}", int.MaxValue, (IsWelder ? "Blue" : "Red"));
                Owner = MyAPIGateway.Players.GetPlayer(Tool.OwnerId);
                if (SessionCore.Debug) DebugNote.Show();
            }
            catch { }
        }

        private void Tool_AppendingCustomInfo(IMyTerminalBlock trash, StringBuilder Info)
        {
            Info.Clear();
            //Info.AppendLine($"Current Input: {Math.Round(Tool.ResourceSink.RequiredInputByType(Electricity), 2)} MW");
            Info.AppendLine($"Max Required Input: {Math.Round(PowerConsumptionFunc(true), 2)} MW");
            Info.AppendLine($"Performance impact: {(RunTimesAvailable ? Math.Round(AvgRunTime, 4).ToString() : "--")}/{(RunTimesAvailable ? Math.Round(MaxRunTime, 4).ToString() : "--")} ms (avg/max)");
            if (Tool is IMyShipWelder)
                Info.AppendLine($"Support inventories: {OnboardInventoryOwners.Count}");
        }

        void CheckInitControls()
        {
            string Message = "Attention! Due to a bug in the game itself, you might not be able to work with these tools via mouse-click.\nIf you run into this issue, you have to use the Toggle switch in terminal or on/off switch on toolbar.\nSorry for inconvenience.";
            if (IsWelder)
            {
                if (!SessionCore.InitedWelderControls)
                {
                    SessionCore.InitWelderControls();
                    MyAPIGateway.Utilities.ShowMessage("Laser Welders", Message);
                }
            }
            else if (IsGrinder)
            {
                if (!SessionCore.InitedGrinderControls)
                {
                    SessionCore.InitGrinderControls();
                    MyAPIGateway.Utilities.ShowMessage("Laser Grinders", Message);
                }
            }
            else if (IsDrill)
            {

            }
        }

        void DrawBeam()
        {
            if (MyAPIGateway.Session.Player == null) return;
            var Internal = InternalBeamColor.ToVector4();
            var External = Vector4.Zero;
            if (IsWelder) External = ExternalWeldBeamColor.ToVector4();
            if (IsGrinder) External = ExternalGrindBeamColor.ToVector4();
            if (IsDrill) External = ExternalDrillBeamColor.ToVector4();
            var BeamStart = this.BeamStart;
            var BeamEnd = this.BeamEnd;
            MySimpleObjectDraw.DrawLine(BeamStart, BeamEnd, MyStringId.GetOrCompute("WeaponLaser"), ref Internal, 0.1f);
            MySimpleObjectDraw.DrawLine(BeamStart, BeamEnd, MyStringId.GetOrCompute("WeaponLaser"), ref External, 0.2f);
        }

        public override void UpdatingStopped()
        {

        }

        float PowerConsumptionFunc(bool Test = false)
        {
            try
            {
                if (!Test && !Tool.IsToolWorking()) return 0;
                if (IsDrill)
                {
                    return 10;
                }
                else
                {
                    if (SpeedMultiplier <= 1)
                        return (float)Math.Pow(1.2, BeamLength * GridBlockSize);
                    else
                        return (float)Math.Pow(1.2, BeamLength * GridBlockSize) + ((float)Math.Pow(1.2, BeamLength * GridBlockSize) * SpeedMultiplier - 1 * 0.8f);
                }
            }
            catch
            {
                return 0;
            }
        }
    }

    public static class GridRayCast
    {
        public static void GetBlocksOnRay(this IMyCubeGrid Grid, ICollection<LineD> Rays, ICollection<IMySlimBlock> Blocks, Func<IMySlimBlock, bool> collect = null)
        {
            foreach (LineD Ray in Rays)
            {
                List<Vector3I> BlockPositions = new List<Vector3I>();
                Grid.RayCastCells(Ray.From, Ray.To, BlockPositions);
                foreach (Vector3I Position in BlockPositions)
                {
                    IMySlimBlock Block = Grid.GetCubeBlock(Position);
                    if (Block == null) continue;
                    if (collect == null || collect(Block)) Blocks.Add(Block);
                }
            }
        }

        public static void GetBlocksOnRay(this IMyCubeGrid Grid, LineD Ray, ICollection<IMySlimBlock> Blocks, Func<IMySlimBlock, bool> collect = null)
        {
            GetBlocksOnRay(Grid, new LineD[] { Ray }, Blocks, collect);
        }
    }
}
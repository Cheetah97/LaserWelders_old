using Cheetah.Networking;
using ProtoBuf;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Cheetah.LaserTools
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Drill), false, "LargeShipLaserDrill", "SmallShipLaserDrill")]
    public class LaserDrill : MyGameLogicComponent
    {
        protected IMyShipDrill Drill => Entity as IMyShipDrill;
        protected IMyCubeGrid DrillGrid => Drill.CubeGrid;
        protected IMyGridTerminalSystem Term => DrillGrid.GetTerminalSystem();
        MyResourceSinkComponent MyPowerSink;
        float SuppliedPowerRatio => Drill.ResourceSink.SuppliedRatioByType(Electricity);
        public static MyDefinitionId Electricity { get; } = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        /// <summary>
        /// Grid block size, in meters.
        /// </summary>
        protected float GridBlockSize => DrillGrid.GridSize;
        protected Vector3I BlockDimensions => (Drill.SlimBlock.BlockDefinition as MyCubeBlockDefinition).Size;
        protected Vector3D BlockPosition => Drill.GetPosition();
        AutoSet<float> SyncHarvestEfficiency;
        public float HarvestEfficiency
        {
            get { return SyncHarvestEfficiency.Get(); }
            set { SyncHarvestEfficiency.Set(value); }
        }
        Vector3D BlockForwardEnd => Drill.WorldMatrix.Forward * GridBlockSize * (BlockDimensions.Z) / 2;
        Vector3 LaserEmitterPosition
        {
            get
            {
                var EmitterDummy = Drill.Model.GetDummy("Laser_Emitter");
                return EmitterDummy != null ? EmitterDummy.Matrix.Translation : (Vector3)BlockForwardEnd;
            }
        }
        protected Vector3D BeamStart => BlockPosition + LaserEmitterPosition;
        float SensorOffset => (Drill.SlimBlock.BlockDefinition as MyShipDrillDefinition).SensorOffset;
        float CutoutOffset => (Drill.SlimBlock.BlockDefinition as MyShipDrillDefinition).CutOutOffset;
        float CutoutRadius => (Drill.SlimBlock.BlockDefinition as MyShipDrillDefinition).CutOutRadius;
        protected Vector3D BeamEnd => BeamStart + Drill.WorldMatrix.Forward * SensorOffset + Drill.WorldMatrix.Forward * CutoutOffset + Drill.WorldMatrix.Forward * CutoutRadius;
        Color InternalBeamColor { get; } = Color.WhiteSmoke;
        Color ExternalDrillBeamColor { get; } = Color.Gold;
        protected IMyHudNotification DebugNote;

        protected IMyPlayer Owner { get; private set; }


        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            //Tool.ResourceSink.SetRequiredInputFuncByType(Electricity, () => PowerConsumptionFunc());
            try
            {
                if (!Drill.HasComponent<MyModStorageComponent>())
                {
                    Drill.Storage = new MyModStorageComponent();
                    Drill.Components.Add(Drill.Storage);
                    SessionCore.DebugWrite($"{Drill.CustomName}.Init()", "Block doesn't have a Storage component!", IsExcessive: false);
                }
            }
            catch { }
        }

        #region Loading stuff
        [ProtoContract]
        public struct Persistent
        {
            [ProtoMember(1)]
            public float HarvestMultiplier;
        }

        public void Load()
        {
            try
            {
                if (MyAPIGateway.Multiplayer.MultiplayerActive && !MyAPIGateway.Multiplayer.IsServer)
                {
                    SyncHarvestEfficiency.Ask();
                    return;
                }
                string Storage = "";
                //if (Tool.Storage.ContainsKey(SessionCore.StorageGuid))
                if (Drill.Storage?.TryGetValue(SessionCore.StorageGuid, out Storage) == true ||
                    MyAPIGateway.Utilities.GetVariable($"settings_{Drill.EntityId}", out Storage))
                {
                    try
                    {
                        Persistent persistent = MyAPIGateway.Utilities.SerializeFromBinary<Persistent>(Convert.FromBase64String(Storage));
                        SyncHarvestEfficiency.Set(persistent.HarvestMultiplier);
                    }
                    catch (Exception Scrap)
                    {
                        SessionCore.LogError($"{Drill.CustomName}.Load()", Scrap);
                    }
                }
                else
                {
                    SessionCore.DebugWrite($"{Drill.CustomName}.Load()", "Storage access failed.");
                }
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Drill.CustomName}.Load().AccessStorage", Scrap);
            }
        }

        public void Save()
        {
            try
            {
                Persistent persistent;
                persistent.HarvestMultiplier = SyncHarvestEfficiency.Get();
                string Raw = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(persistent));
                MyAPIGateway.Utilities.SetVariable($"settings_{Drill.EntityId}", Raw);
                Drill.Storage?.AddOrUpdate(SessionCore.StorageGuid, Raw);
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Drill.CustomName}.Save()", Scrap);
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
                SessionCore.LogError($"{Drill?.CustomName}.Close().DebugClose", Scrap);
            }
            try
            {
                SessionCore.SaveUnregister(Save);
                SyncHarvestEfficiency.GotValueFromServer -= Drill.UpdateVisual;
                SyncHarvestEfficiency.Close();
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Drill?.CustomName}.Close()", Scrap);
            }
        }
        #endregion

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                if (Drill.CubeGrid.Physics?.Enabled != true || !Networker.Inited)
                {
                    NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                    if (!Networker.Inited) Networker.Init(SessionCore.ModID);
                    return;
                }
                SyncHarvestEfficiency = new AutoSet<float>(Drill, "HarvestEfficiency", 1, Checker: val => val >= 1 && val <= 4);
                SyncHarvestEfficiency.GotValueFromServer += Drill.UpdateVisual;
                Drill.PowerConsumptionMultiplier = PowerConsumptionMultiplier();

                CheckInitControls();
                Load();

                SessionCore.SaveRegister(Save);
                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
                DebugNote = MyAPIGateway.Utilities.CreateNotification($"{Drill?.CustomName}", int.MaxValue, "Blue");
                Owner = MyAPIGateway.Players.GetPlayer(Drill.OwnerId);
                if (SessionCore.Debug) DebugNote.Show();
            }
            catch { }
        }

        public override void UpdateBeforeSimulation()
        {
            if (Drill.IsWorking) DrawBeam();
            if (Drill.PowerConsumptionMultiplier != PowerConsumptionMultiplier())
            {
                Drill.PowerConsumptionMultiplier = PowerConsumptionMultiplier();
                Drill.DrillHarvestMultiplier = HarvestEfficiency;
            }
            DebugNote.Text = $"{Drill?.CustomName}: Working={Drill.IsWorking}; PowerMultiplier={Math.Round(PowerConsumptionMultiplier(), 2)}";
        }

        float PowerConsumptionMultiplier()
        {
            return 50 * (float)Math.Pow(HarvestEfficiency, 2) * (DrillGrid.GridSizeEnum == MyCubeSize.Large ? 1.6f : 1);
        }

        void CheckInitControls()
        {
            if (!SessionCore.InitedDrillControls) SessionCore.InitDrillControls();
        }

        void DrawBeam()
        {
            if (MyAPIGateway.Session.Player == null) return;
            var Internal = InternalBeamColor.ToVector4();
            var External = ExternalDrillBeamColor.ToVector4();
            var BeamStart = this.BeamStart;
            var BeamEnd = this.BeamEnd;
            MySimpleObjectDraw.DrawLine(BeamStart, BeamEnd, MyStringId.GetOrCompute("WeaponLaser"), ref Internal, 0.1f);
            MySimpleObjectDraw.DrawLine(BeamStart, BeamEnd, MyStringId.GetOrCompute("WeaponLaser"), ref External, 0.4f);
        }
    }
}
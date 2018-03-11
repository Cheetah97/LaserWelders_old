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
        void Work(int ticks = 1)
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            if (IsDrill) return;

            LineD WeldRay = new LineD(BeamStart, BeamEnd);
            List<MyLineSegmentOverlapResult<MyEntity>> Overlaps = new List<MyLineSegmentOverlapResult<MyEntity>>();
            MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref WeldRay, Overlaps);

            HashSet<IMyCubeGrid> Grids = new HashSet<IMyCubeGrid>();
            HashSet<IMyCharacter> Characters = new HashSet<IMyCharacter>();
            HashSet<IMyFloatingObject> Flobjes = new HashSet<IMyFloatingObject>();
            Overlaps.Select(x => x.Element as IMyEntity).SortByType(Grids, Characters, Flobjes);
            Grids.Remove(ToolGrid);

            if (SessionCore.Debug && Vector3D.Distance(BlockPosition, MyAPIGateway.Session.LocalHumanPlayer.GetPosition()) <= 200)
            {
                string GridNames = "";
                foreach (var grid in Grids)
                {
                    GridNames += $"{grid.DisplayName};";
                }
                DebugNote.Text = $"{Tool.CustomName}: processing {Grids.Count} entities: {GridNames}";
                GridNames = null;
            }

            foreach (IMyCubeGrid Grid in Grids)
            {
                if (Grid.EntityId == ToolGrid.EntityId) continue;
                try
                {
                    ProcessGrid(Grid, ticks);
                }
                catch (Exception Scrap)
                {
                    SessionCore.LogError(Grid.DisplayName, Scrap);
                }
            }

            foreach (IMyCharacter Char in Characters)
            {
                if (Char.WorldAABB.Intersects(ref WeldRay))
                    Char.DoDamage(GrinderSpeed * ticks / 2, MyDamageType.Grind, true, null, Tool.EntityId);
            }

            foreach (IMyFloatingObject Flobj in Flobjes)
            {
                ToolCargo.PickupItem(Flobj);
            }

            /*if (Tool is IMyShipDrill)
            {
                List<MyLineSegmentOverlapResult<MyVoxelBase>> VoxelOverlaps = new List<MyLineSegmentOverlapResult<MyVoxelBase>>();
                MyGamePruningStructure.GetVoxelMapsOverlappingRay(ref WeldRay, VoxelOverlaps);
                MyVoxelBase Voxel = VoxelOverlaps.OrderBy(x => Vector3D.DistanceSquared(BlockPosition, x.Element.PositionComp.GetPosition())).First().Element;
                ProcessVoxel(Voxel, ticks);
            }*/
        }

        protected abstract void ProcessGrid(IMyCubeGrid TargetGrid, int ticks);
        protected virtual void ProcessVoxel(MyVoxelBase Voxel, int ticks) { }

        void Main(int ticks)
        {
            try
            {
                Tool.ResourceSink.SetRequiredInputByType(Electricity, PowerConsumptionFunc());
                if (Tool.IsToolWorking() && HasEnoughPower)
                {
                    Work(ticks);
                    DrawBeam();
                }
                else
                {
                    DebugNote.Text = $"{Tool.CustomName}: idle";
                    UnbuiltBlocks.Clear();
                }
                Tool.RefreshCustomInfo();
                //if (SessionCore.Debug) DebugNote.Text = $"{Tool.CustomName} perf. impact: {(RunTimesAvailable ? Math.Round(AvgRunTime, 5).ToString() : "--")}/{(RunTimesAvailable ? Math.Round(MaxRunTime, 5).ToString() : "--")} ms (avg/max)";
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError(Tool.CustomName, Scrap);
            }
        }

        void Aux(int ticks)
        {
            if (Tool.IsToolWorking() && HasEnoughPower)
            {
                DrawBeam();
            }
        }

        public override void UpdateBeforeSimulation()
        {
            Watch.Start();
            Aux(ticks: 1);
            Ticks += 1;
            InventoryUpdateTicks += 1;
            if (Ticks >= SessionCore.WorkSkipTicks)
            {
                Ticks = 0;
                Main(ticks: SessionCore.WorkSkipTicks);
            }
            if (Tool is IMyShipWelder && InventoryUpdateTicks >= SessionCore.InventoryRebuildSkipTicks)
            {
                int count = MyAPIGateway.GridGroups.GetGroup(Tool.CubeGrid, GridLinkTypeEnum.Physical).Count;

                if (count != ConnectedGrids)
                {
                    NeedsInventoryCasheRefresh = true;
                    ConnectedGrids = count;
                }

                InventoryUpdateTicks = 0;
            }

            BuildInventoryCache();
            Watch.Stop();
            if (LastRunTimes.Count >= RunTimeCacheSize) LastRunTimes.Dequeue();
            LastRunTimes.Enqueue(1000 * (Watch.ElapsedTicks / System.Diagnostics.Stopwatch.Frequency));
            Watch.Reset();
        }

        public override void UpdateBeforeSimulation100()
        {
            GridAvailablePower = ToolGrid.GetMaxPowerOutput();

            if (!Tool.Enabled) return;
            //if (RunTimesAvailable && MaxRunTime >= 0.25f)
            //    Text.AppendLine($"Performance impact: {(RunTimesAvailable ? Math.Round(AvgRunTime, 4).ToString() : "--")}/{(RunTimesAvailable ? Math.Round(MaxRunTime, 4).ToString() : "--")} ms (avg/max)");

            if (UnbuiltBlocks.Count > 0)
            {
                Dictionary<string, int> TotalMissingList = new Dictionary<string, int>();
                Dictionary<IMySlimBlock, Dictionary<string, int>> MissingPerBlock = new Dictionary<IMySlimBlock, Dictionary<string, int>>();
                UnbuiltBlocks.ReadMissingComponents(TotalMissingList, MissingPerBlock);
                if (!ToolCargo.PullAny(OnboardInventoryOwners, TotalMissingList))
                {
                    ComplainMissing(MissingPerBlock);
                }
                UnbuiltBlocks.Clear();
            }
        }

        /// <summary>
        /// Builds cache of accessible inventories on this ship.
        /// </summary>
        void BuildInventoryCache()
        {
            if (Tool.IsToolWorking() && Tool.UseConveyorSystem && NeedsInventoryCasheRefresh)
            {
                NeedsInventoryCasheRefresh = false;
                OnboardInventoryOwners.Clear();
                OnboardInventoryOwners.Add(Tool);
                Func<IMyTerminalBlock, bool> Puller = (Block) =>
                {
                    if (Block == null) return false;
                    if (!Block.HasPlayerAccess(Tool.OwnerId)) return false;
                    if (!Block.HasInventory) return false;
                    if (Block == Tool) return false;
                    if (Block is IMyCargoContainer || Block is IMyAssembler || Block is IMyShipConnector || Block is IMyCollector)
                    {
                        List<IMyInventory> inventoryList = Block.GetInventories();
                        if (inventoryList.Count > 0 && inventoryList[0].IsConnectedTo(ToolCargo))
                        {
                            OnboardInventoryOwners.Add(Block);
                        }
                    }

                    return false;
                };

                Term.GetBlocksOfType(new List<IMyTerminalBlock>(), Puller);
            }
        }

        void ComplainMissing(Dictionary<IMySlimBlock, Dictionary<string, int>> MissingPerBlock)
        {
            var Player = MyAPIGateway.Session.Player;
            if (Player == null) return;
            if (Player.IdentityId != Tool.OwnerId) return;
            if (Player.GetPosition().DistanceTo(Tool.GetPosition()) > 200) return;

            StringBuilder Text = new StringBuilder();
            
            if (MissingPerBlock.Count == 1)
            {
                IMySlimBlock Block = MissingPerBlock.Keys.First();
                var Missing = MissingPerBlock[Block];
                bool IsProjected = Block.IsProjectable();
                if (Missing != null && Missing.Count > 0)
                {
                    Text.AppendLine($"{Tool.CustomName}: can't proceed to {(!IsProjected ? "build" : "place")} {Block.BlockDefinition.DisplayNameText}, missing:\n");
                    foreach (var ItemPair in Missing)
                    {
                        Text.AppendLine($"{ItemPair.Key}: {(!IsProjected ? 1 : ItemPair.Value)}");
                        if (IsProjected) break;
                    }
                }
            }
            else if (UnbuiltBlocks.Count > 1 && MissingPerBlock.Values.Any(x => x.Count > 0))
            {
                Text.AppendLine($"{Tool.CustomName}: can't proceed to build {MissingPerBlock.Count} blocks:\n");
                foreach (IMySlimBlock Block in MissingPerBlock.Keys)
                {
                    var Missing = MissingPerBlock[Block];
                    if (Missing.Count == 0) continue;
                    Text.AppendLine($"{Block.BlockDefinition.DisplayNameText}: missing:");
                    foreach (var ItemPair in Missing)
                    {
                        Text.AppendLine($"{ItemPair.Key}: {ItemPair.Value}");
                    }
                    Text.AppendLine();
                }
            }
            Text.RemoveTrailingNewlines();
            IMyHudNotification hud = MyAPIGateway.Utilities.CreateNotification(Text.ToString(), (int)Math.Ceiling(SessionCore.TickLengthMs * 101), "Red"); // Adding 1 excess tick is needed, otherwise notification can flicker
            hud.Show();
        }
    }
}
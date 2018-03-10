using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace Cheetah.LaserTools
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), false, "LargeShipLaserWelder", "SmallShipLaserWelder")]
    public class LaserWelder : LaserToolBase
    {
        IMyShipWelder Welder => Tool as IMyShipWelder;

        protected override void ProcessGrid(IMyCubeGrid TargetGrid, int ticks)
        {
            List<IMySlimBlock> Blocks = new List<IMySlimBlock>();
            List<LineD> RayGrid;
            if (!DistanceMode)
            {
                Vector3D UpOffset = Vector3D.Normalize(Tool.WorldMatrix.Up) * 0.5;
                Vector3D RightOffset = Vector3D.Normalize(Tool.WorldMatrix.Right) * 0.5;
                RayGrid = VectorExtensions.BuildLineGrid(BeamStart, BeamEnd, UpOffset, RightOffset, 2, 2);
                TargetGrid.GetBlocksOnRay(RayGrid, Blocks, x => x.IsProjectable() || x.IsWeldable());
                Weld(Blocks, ticks);
            }
            else
            {
                RayGrid = new List<LineD> { new LineD(BeamStart, BeamEnd) };
                TargetGrid.GetBlocksOnRay(RayGrid, Blocks, x => x.IsProjectable() || x.IsWeldable());
                WeldDistanceMode(Blocks, ticks);
            }
        }

        void Weld(ICollection<IMySlimBlock> Blocks, int ticks = 1)
        {
            //UnbuiltBlocks.Clear();
            if (Blocks.Count == 0) return;
            float SpeedRatio = (WelderSpeed / Blocks.Count) * ticks * SpeedMultiplier;
            float BoneFixSpeed = WelderBoneRepairSpeed * ticks;
            var UniqueBlocks = Blocks.CollapseDuplicates();
            HashSet<IMySlimBlock> unbuilt = new HashSet<IMySlimBlock>();

            foreach (IMySlimBlock Block in UniqueBlocks.Keys)
            {
                float blockRatio = SpeedRatio * UniqueBlocks.GetData(Block);
                if (Block.CubeGrid.Physics?.Enabled == true)
                {
                    if (!Weld(Block, blockRatio, BoneFixSpeed)) unbuilt.Add(Block);
                }
                else
                {
                    Place(Block);
                }
            }

            if (unbuilt.Count > 0)
            {
                Dictionary<string, int> Missing = new Dictionary<string, int>();
                unbuilt.ReadMissingComponents(Missing);
                if (!ToolCargo.PullAny(OnboardInventoryOwners, Missing))
                    UnbuiltBlocks.UnionWith(unbuilt);
            }
        }

        void WeldDistanceMode(ICollection<IMySlimBlock> Blocks, int ticks = 1)
        {
            //UnbuiltBlocks.Clear();
            if (Blocks.Count == 0) return;
            Blocks = Blocks.OrderByDescending(x => Vector3D.DistanceSquared(x.GetPosition(), Tool.GetPosition())).ToList();
            float SpeedRatio = WelderSpeed * ticks * SpeedMultiplier;
            float BoneFixSpeed = WelderBoneRepairSpeed * ticks;
            
            foreach (IMySlimBlock Block in Blocks)
            {
                if (Block.CubeGrid.Physics?.Enabled == true)
                {
                    bool welded = Weld(Block, SpeedRatio, BoneFixSpeed);
                    if (!welded)
                    {
                        var missing = Block.ReadMissingComponents();
                        if (!ToolCargo.PullAny(OnboardInventoryOwners, missing))
                            UnbuiltBlocks.Add(Block);
                    }
                    else break;
                }
                else
                {
                    Place(Block);
                    break;
                }
            }
        }

        bool Weld(IMySlimBlock Block, float SpeedRatio, float BoneFixSpeed)
        {
            //if (Block.IsFullIntegrity && !Block.HasDeformation) return;
            if (Block.CanContinueBuild(ToolCargo) || MyAPIGateway.Session.CreativeMode)
            {
				Block.IncreaseMountLevel(SpeedRatio, Welder.OwnerId, ToolCargo, BoneFixSpeed, false);
                Block.MoveItemsToConstructionStockpile(ToolCargo);
                return true;
            }
            else if (Block.HasDeformation)
            {
                Block.IncreaseMountLevel(SpeedRatio, Welder.OwnerId, ToolCargo, BoneFixSpeed, false);
                return true;
            }
            else return false;
        }

        void Place(IMySlimBlock Block)
        {
            if (!Block.IsProjectable()) return;
            var FirstItem = ((MyCubeBlockDefinition)Block.BlockDefinition).Components[0].Definition.Id;
            if (MyAPIGateway.Session.CreativeMode || ToolCargo.PullAny(OnboardInventoryOwners, FirstItem.SubtypeName, 1))
            {
                var Projector = ((Block.CubeGrid as MyCubeGrid).Projector as IMyProjector);
                Projector.Build(Block, 0, Tool.EntityId, false);
                ToolCargo.RemoveItemsOfType(1, FirstItem);
            }
            else
            {
                UnbuiltBlocks.Add(Block);
                SessionCore.DebugWrite($"{Tool.CustomName}.Place()", $"Tool can't pull the component {FirstItem.SubtypeName}!", IsExcessive: false);
            }
        }
    }
}
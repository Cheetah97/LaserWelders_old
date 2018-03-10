using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Cheetah.LaserTools
{
    public static class OwnershipTools
    {
        public static long PirateID
        {
            get
            {
                return MyVisualScriptLogicProvider.GetPirateId();
            }
        }

        public static bool IsOwnedByPirates(this IMyTerminalBlock Block)
        {
            return Block.OwnerId == PirateID;
        }

        /*public static bool IsOwnedByNPC(this IMyTerminalBlock Block)
        {
            if (Block.IsOwnedByPirates()) return true;
            return AISessionCore.NPCIDs.Contains(Block.OwnerId);
        }*/

        public static bool IsPirate(this IMyCubeGrid Grid)
        {
            return Grid.BigOwners.Contains(PirateID);
        }

        /*public static bool IsNPC(this IMyCubeGrid Grid)
        {
            if (Grid.IsPirate()) return true;
            if (Grid.BigOwners.Count == 0) return false;
            return AISessionCore.NPCIDs.Contains(Grid.BigOwners.First());
        }*/
    }

    public static class TerminalExtensions
    {
        public static IMyGridTerminalSystem GetTerminalSystem(this IMyCubeGrid Grid)
        {
            return MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(Grid);
        }

        public static List<T> GetBlocksOfType<T>(this IMyGridTerminalSystem Term, Func<T, bool> collect = null) where T : class, Sandbox.ModAPI.Ingame.IMyTerminalBlock
        {
            if (Term == null) throw new Exception("GridTerminalSystem is null!");
            List<T> TermBlocks = new List<T>();
            Term.GetBlocksOfType(TermBlocks, collect);
            return TermBlocks;
        }

        public static List<T> GetWorkingBlocks<T>(this IMyCubeGrid Grid, bool OverrideEnabledCheck = false, Func<T, bool> collect = null) where T : class, IMyTerminalBlock
        {
            try
            {
                List<IMySlimBlock> slimBlocks = new List<IMySlimBlock>();
                List<T> Blocks = new List<T>();
                Grid.GetBlocks(slimBlocks, (x) => x != null && x is T && (!OverrideEnabledCheck ? (x as IMyTerminalBlock).IsWorking : (x as IMyTerminalBlock).IsFunctional));

                if (slimBlocks.Count == 0) return new List<T>();
                foreach (var _block in slimBlocks)
                    if (collect == null || collect(_block as T)) Blocks.Add(_block as T);

                return Blocks;
            }
            catch (Exception Scrap)
            {
                Grid.LogError("GridExtensions.GetWorkingBlocks", Scrap);
                return new List<T>();
            }
        }

        public static Dictionary<string, int> CalculateMissingComponents(this IMyCubeGrid Grid)
        {
            if (Grid == null) return new Dictionary<string, int>();
            try
            {
                Dictionary<string, int> MissingComponents = new Dictionary<string, int>();
                List<IMySlimBlock> Blocks = new List<IMySlimBlock>();
                Grid.GetBlocks(Blocks);

                foreach (IMySlimBlock Block in Blocks)
                {
                    try
                    {
                        Block.ReadMissingComponents(MissingComponents);
                    }
                    catch (Exception Scrap)
                    {
                        SessionCore.LogError($"CalculateMissing[{Grid.CustomName}].Iterate", Scrap, DebugPrefix: "LaserWelders.");
                    }
                }
                return MissingComponents;
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"CalculateMissing[{Grid.CustomName}]", Scrap, DebugPrefix: "LaserWelders.");
                return new Dictionary<string, int>();
            }
        }

        public static void Trigger(this IMyTimerBlock Timer)
        {
            Timer.GetActionWithName("TriggerNow").Apply(Timer);
        }
    }

    public static class VectorExtensions
    {
        public static float DistanceTo(this Vector3D From, Vector3D To)
        {
            return (float)(To - From).Length();
        }

        public static bool IsNullEmptyOrWhiteSpace(this string str)
        {
            return string.IsNullOrWhiteSpace(str);
        }

        public static Vector3D LineTowards(this Vector3D From, Vector3D To, double Length)
        {
            return From + (Vector3D.Normalize(To - From) * Length);
        }

        public static Vector3D InverseVectorTo(this Vector3D From, Vector3D To, double Length)
        {
            return From + (Vector3D.Normalize(From - To) * Length);
        }

        /// <summary>
        /// Builds a grid of vectors.
        /// </summary>
        /// <param name="HalfHeight">Half-height of the resulting grid, in offsets. E.g. for grid of 5 points this is 4/2=2.</param>
        /// <param name="HalfWidth">Half-width of the resulting grid, in offsets. E.g. for grid of 5 points this is 4/2=2.</param>
        /// <returns></returns>
        public static List<Vector3D> BuildGrid(Vector3D Center, Vector3D UpOffset, Vector3D RightOffset, int HalfHeight, int HalfWidth)
        {
            List<Vector3D> Grid = new List<Vector3D>(((HalfHeight * 2) + 1) * ((HalfWidth * 2) + 1));

            Vector3D LeftBottomCorner = Center + (RightOffset * -1 * HalfWidth) + (UpOffset * -1 * HalfHeight);

            for (int width = 0; width <= HalfWidth * 2; width++)
            {
                for (int height = 0; height <= HalfHeight * 2; height++)
                {
                    Grid.Add(LeftBottomCorner + (UpOffset * height) + (RightOffset * width));
                }
            }

            return Grid;
        }

        /// <summary>
        /// Builds a grid of vectors. Note that this function assumes normalized, 1m length vectors.
        /// </summary>
        /// <param name="HalfHeight">Half-height of the resulting grid, in offsets. E.g. for grid of 5 points this is 4/2=2.</param>
        /// <param name="HalfWidth">Half-width of the resulting grid, in offsets. E.g. for grid of 5 points this is 4/2=2.</param>
        /// <returns></returns>
        public static List<LineD> BuildLineGrid(Vector3D Center, Vector3D ForwardOffset, Vector3D UpOffset, Vector3D RightOffset, float LineLength, int HalfHeight, int HalfWidth)
        {
            List<LineD> Grid = new List<LineD>(((HalfHeight * 2) + 1) * ((HalfWidth * 2) + 1));

            Vector3D LeftBottomCorner = Center + (RightOffset * -1 * HalfWidth) + (UpOffset * -1 * HalfHeight);

            for (int width = 0; width <= HalfWidth * 2; width++)
            {
                for (int height = 0; height <= HalfHeight * 2; height++)
                {
                    Vector3D Point1 = LeftBottomCorner + (UpOffset * height) + (RightOffset * width);
                    Vector3D Point2 = Point1 + (ForwardOffset * LineLength);
                    Grid.Add(new LineD(Point1, Point2));
                }
            }

            return Grid;
        }

        /// <summary>
        /// Builds a grid of lines.
        /// </summary>
        /// <param name="HalfHeight">Half-height of the resulting grid, in offsets. E.g. for grid of 5 points this is 4/2=2.</param>
        /// <param name="HalfWidth">Half-width of the resulting grid, in offsets. E.g. for grid of 5 points this is 4/2=2.</param>
        /// <returns></returns>
        public static List<LineD> BuildLineGrid(Vector3D CenterStart, Vector3D CenterEnd, Vector3D UpOffset, Vector3D RightOffset, int HalfHeight, int HalfWidth)
        {
            List<LineD> Grid = new List<LineD>(((HalfHeight * 2) + 1) * ((HalfWidth * 2) + 1));

            Vector3D LeftBottomCornerStart = CenterStart + (RightOffset * -1 * HalfWidth) + (UpOffset * -1 * HalfHeight);
            Vector3D LeftBottomCornerEnd = CenterEnd + (RightOffset * -1 * HalfWidth) + (UpOffset * -1 * HalfHeight);

            for (int width = 0; width <= HalfWidth * 2; width++)
            {
                for (int height = 0; height <= HalfHeight * 2; height++)
                {
                    Vector3D Point1 = LeftBottomCornerStart + (UpOffset * height) + (RightOffset * width);
                    Vector3D Point2 = LeftBottomCornerEnd + (UpOffset * height) + (RightOffset * width);
                    Grid.Add(new LineD(Point1, Point2));
                }
            }

            return Grid;
        }
    }

    public static class GamelogicHelpers
    {
        public static bool IsToolWorking(this IMyShipToolBase Tool)
        {
            if (!Tool.IsFunctional) return false;
            return Tool.Enabled || (Tool as IMyGunObject<Sandbox.Game.Weapons.MyToolBase>).IsShooting;
        }

        public static bool IsWeldable(this IMySlimBlock Block)
        {
            if (Block.CubeGrid.Physics == null || Block.CubeGrid.Physics.Enabled == false) return false;
            if (Block.IsDestroyed || Block.IsFullyDismounted) return false;
            return !Block.IsFullIntegrity || Block.BuildLevelRatio < 1 || Block.CurrentDamage > 0.1f || Block.HasDeformation;
        }

        public static bool IsProjectable(this IMySlimBlock Block, bool CheckPlacement = true)
        {
            if (!SessionCore.Debug)
            {
                MyCubeGrid Grid = Block.CubeGrid as MyCubeGrid;
                if (!CheckPlacement) return Grid.Projector != null;
                return Grid.Projector != null && (Grid.Projector as IMyProjector).CanBuild(Block, true) == BuildCheckResult.OK;
            }
            else
            {
                MyCubeGrid Grid = Block.CubeGrid as MyCubeGrid;
                if (Grid.Projector == null)
                {
                    SessionCore.DebugWrite($"IsProjectable()", "Grid doesn't have a Projector!", IsExcessive: false);
                    return false;
                }

                var CheckResult = (Grid.Projector as IMyProjector).CanBuild(Block, true);
                if (CheckResult != BuildCheckResult.OK)
                {
                    SessionCore.DebugWrite($"IsProjectable()", $"Block cannot be built. Check result: {CheckResult.ToString()}", IsExcessive: false);
                    return false;
                }
                return true;
            }
        }

        public static bool IsGrindable(this IMySlimBlock Block)
        {
            MyCubeGrid Grid = Block.CubeGrid as MyCubeGrid;
            if (!Grid.Editable) return false;
            if (Grid.Physics?.Enabled != true) return false;
            return true;
        }

        public static long BuiltBy(this IMyCubeBlock Block)
        {
            return (Block as MyCubeBlock).BuiltBy;
        }

        public static long BuiltBy(this IMySlimBlock Block)
        {
            return Block.GetObjectBuilder().BuiltBy;
        }

        public static ComponentType GetComponent<ComponentType>(this IMyEntity Entity) where ComponentType : MyEntityComponentBase
        {
            if (Entity == null || Entity.Components == null) return null;
            return Entity.Components.Has<ComponentType>() ? Entity.Components.Get<ComponentType>() : Entity.GameLogic.GetAs<ComponentType>();
        }

        public static bool TryGetComponent<ComponentType>(this IMyEntity Entity, out ComponentType Component) where ComponentType : MyEntityComponentBase
        {
            Component = GetComponent<ComponentType>(Entity);
            return Component != null;
        }

        public static bool HasComponent<ComponentType>(this IMyEntity Entity) where ComponentType : MyEntityComponentBase
        {
            var Component = GetComponent<ComponentType>(Entity);
            return Component != null;
        }

        public static List<IMyInventory> GetInventories(this IMyEntity Entity)
        {
            List<IMyInventory> Inventories = new List<IMyInventory>();

            for (int i = 0; i < Entity.InventoryCount; ++i)
            {
                var blockInventory = Entity.GetInventory(i) as MyInventory;
                if (blockInventory != null) Inventories.Add(blockInventory);
            }

            return Inventories;
        }

        public static IMyModelDummy GetDummy(this IMyModel Model, string DummyName)
        {
            Dictionary<string, IMyModelDummy> Dummies = new Dictionary<string, IMyModelDummy>();
            Model.GetDummies(Dummies);
            return Dummies.ContainsKey(DummyName) ? Dummies[DummyName] : null;
        }

        /// <summary>
        /// (c) Phoera
        /// </summary>
        public static T EnsureComponent<T>(this IMyEntity entity) where T : MyEntityComponentBase, new()
        {
            return EnsureComponent(entity, () => new T());
        }
        /// <summary>
        /// (c) Phoera
        /// </summary>
        public static T EnsureComponent<T>(this IMyEntity entity, Func<T> factory) where T : MyEntityComponentBase
        {
            T res;
            if (entity.TryGetComponent(out res))
                return res;
            res = factory();
            if (res is MyGameLogicComponent)
            {
                if (entity.GameLogic?.GetAs<T>() == null)
                {
                    //"Added as game logic".ShowNotification();
                    entity.AddGameLogic(res as MyGameLogicComponent);
                    (res as MyGameLogicComponent).Init((MyObjectBuilder_EntityBase)null);
                }
            }
            else
            {
                //"Added as component".ShowNotification();
                entity.Components.Add(res);
                res.Init(null);
            }
            return res;
        }
        public static void AddGameLogic(this IMyEntity entity, MyGameLogicComponent logic)
        {
            var comp = entity.GameLogic as MyCompositeGameLogicComponent;
            if (comp != null)
            {
                entity.GameLogic = MyCompositeGameLogicComponent.Create(new List<MyGameLogicComponent>(2) { comp, logic }, entity as MyEntity);
            }
            else if (entity.GameLogic != null)
            {
                entity.GameLogic = MyCompositeGameLogicComponent.Create(new List<MyGameLogicComponent>(2) { entity.GameLogic as MyGameLogicComponent, logic }, entity as MyEntity);
            }
            else
            {
                entity.GameLogic = logic;
            }
        }
    }

    public static class GridExtensions
    {
        /// <summary>
        /// Removes "dead" block references from a block list.
        /// </summary>
        /// <param name="StrictCheck">Performs x.IsLive(Strict == true). Generates 2 object builders per every block in list.</param>
        public static void Purge<T>(this IList<T> Enum, bool StrictCheck = false) where T: IMySlimBlock
        {
            Enum = Enum.Where(x => x.IsLive(StrictCheck)).ToList();
        }

        /// <summary>
        /// Removes "dead" block references from a block list.
        /// </summary>
        /// <param name="StrictCheck">Performs x.IsLive(Strict == true). Generates 2 object builders per every block in list.</param>
        public static void PurgeInvalid<T>(this IList<T> Enum, bool StrictCheck = false) where T : IMyCubeBlock
        {
            Enum = Enum.Where(x => x.IsLive(StrictCheck)).ToList();
        }

        public static Dictionary<string, int> ReadMissingComponents(this IMySlimBlock Block)
        {
            Dictionary<string, int> MissingList = new Dictionary<string, int>();
            Block.ReadMissingComponents(MissingList);
            return MissingList;
        }

        public static void ReadMissingComponents(this IMySlimBlock Block, Dictionary<string, int> MissingList, bool ClearDictionary = false)
        {
            if (ClearDictionary) MissingList.Clear();
            if (Block.BuildIntegrity == Block.MaxIntegrity && Block.Integrity == Block.MaxIntegrity) return;
            if (Block.StockpileAllocated)
                Block.GetMissingComponents(MissingList);
            else
            {
                foreach (var Component in (Block.BlockDefinition as MyCubeBlockDefinition).Components)
                {
                    string Name = Component.Definition.Id.SubtypeName;
                    if (MissingList.ContainsKey(Name)) MissingList[Name] += Component.Count;
                    else MissingList.Add(Name, Component.Count);
                }
            }
        }

        public static Dictionary<string, int> ReadMissingComponents(this ICollection<IMySlimBlock> Blocks)
        {
            var Missing = new Dictionary<string, int>();
            foreach (IMySlimBlock Block in Blocks)
            {
                Block.ReadMissingComponents(Missing);
            }
            return Missing;
        }

        public static void ReadMissingComponents(this ICollection<IMySlimBlock> Blocks, Dictionary<string, int> MissingList, bool ClearDictionary = false)
        {
            if (ClearDictionary) MissingList.Clear();
            foreach (IMySlimBlock Block in Blocks)
            {
                Block.ReadMissingComponents(MissingList);
            }
        }

        public static void ReadMissingComponents(this ICollection<IMySlimBlock> Blocks, Dictionary<string, int> TotalMissingList, Dictionary<IMySlimBlock, Dictionary<string, int>> MissingPerBlock, bool ClearDictionary = false)
        {
            if (ClearDictionary) TotalMissingList.Clear();
            if (ClearDictionary) MissingPerBlock.Clear();
            foreach (IMySlimBlock Block in Blocks)
            {
                var Missing = Block.ReadMissingComponents();
                if (!MissingPerBlock.ContainsKey(Block)) MissingPerBlock.Add(Block, Missing);
                else MissingPerBlock[Block] = Missing;

                foreach (var kvp in Missing)
                {
                    if (TotalMissingList.ContainsKey(kvp.Key)) TotalMissingList[kvp.Key] += kvp.Value;
                    else TotalMissingList.Add(kvp.Key, kvp.Value);
                }
            }
        }

        public static void GetBlocks(this IMyCubeGrid Grid, HashSet<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null)
        {
            List<IMySlimBlock> cubes = new List<IMySlimBlock>();
            if (blocks == null) blocks = new HashSet<IMySlimBlock>(); else blocks.Clear();
            Grid.GetBlocks(cubes, collect);
            foreach (var block in cubes)
                blocks.Add(block);
        }

        /// <summary>
        /// Check if the given block is a "live" existing block, or a "zombie" reference left after a dead and removed block.
        /// </summary>
        /// <param name="StrictCheck">Performs strict check (checks if block in same place is of same typeid+subtypeid). Generates 2 object builders.</param>
        public static bool IsLive(this IMySlimBlock Block, bool StrictCheck = false)
        {
            if (Block == null) return false;
            if (Block.FatBlock != null && Block.FatBlock.Closed) return false;
            if (Block.IsDestroyed) return false;
            var ThereBlock = Block.CubeGrid.GetCubeBlock(Block.Position);
            if (ThereBlock == null) return false;
            var Builder = Block.GetObjectBuilder();
            var ThereBuilder = ThereBlock.GetObjectBuilder();
            return Builder.TypeId == ThereBuilder.TypeId && Builder.SubtypeId == ThereBuilder.SubtypeId;
        }

        /// <summary>
        /// Check if the given block is a "live" existing block, or a "zombie" reference left after a dead and removed block.
        /// </summary>
        /// <param name="StrictCheck">Performs strict check (checks if block in same place is of same typeid+subtypeid). Generates 2 object builders.</param>
        public static bool IsLive(this IMyCubeBlock Block, bool StrictCheck = false)
        {
            if (Block == null) return false;
            if (Block.Closed) return false;
            if (Block.SlimBlock?.IsDestroyed != false) return false;
            var ThereBlock = Block.CubeGrid.GetCubeBlock(Block.Position);
            if (ThereBlock == null) return false;
            var Builder = Block.GetObjectBuilder();
            var ThereBuilder = ThereBlock.GetObjectBuilder();
            return Builder.TypeId == ThereBuilder.TypeId && Builder.SubtypeId == ThereBuilder.SubtypeId;
        }

        public static float BuildPercent(this IMySlimBlock block)
        {
            return block.Integrity / block.MaxIntegrity;
        }

        public static float BuildPercent(this IMyCubeBlock block)
        {
            return block.SlimBlock.BuildPercent();
        }

        public static Dictionary<string, int> GetMissingComponents(this IMySlimBlock Block)
        {
            var Dict = new Dictionary<string, int>();
            Block.GetMissingComponents(Dict);
            return Dict;
        }

        public static Vector3D GetPosition(this IMySlimBlock block)
        {
            return block.CubeGrid.GridIntegerToWorld(block.Position);
        }

        public static float GetBaseMass(this IMyCubeGrid Grid)
        {
            float baseMass, totalMass;
            (Grid as MyCubeGrid).GetCurrentMass(out baseMass, out totalMass);
            return baseMass;
        }

        public static int GetTotalMass(this IMyCubeGrid Grid)
        {
            return (Grid as MyCubeGrid).GetCurrentMass();
        }

        public static bool HasPower(this IMyCubeGrid Grid)
        {
            foreach (IMySlimBlock Reactor in Grid.GetWorkingBlocks<IMyReactor>())
            {
                if (Reactor != null && Reactor.FatBlock.IsWorking) return true;
            }
            foreach (IMySlimBlock Battery in Grid.GetWorkingBlocks<IMyBatteryBlock>())
            {
                if ((Battery as IMyBatteryBlock).CurrentStoredPower > 0f) return true;
            }

            return false;
        }

        public static float GetCurrentReactorPowerOutput(this IMyCubeGrid Grid)
        {
            List<IMyReactor> Reactors = new List<IMyReactor>();
            Grid.GetTerminalSystem().GetBlocksOfType(Reactors, x => x.IsWorking);
            if (Reactors.Count == 0) return 0;

            float SummarizedOutput = 0;
            foreach (var Reactor in Reactors)
                SummarizedOutput += Reactor.CurrentOutput;

            return SummarizedOutput;
        }

        public static float GetMaxReactorPowerOutput(this IMyCubeGrid Grid)
        {
            List<IMyReactor> Reactors = new List<IMyReactor>();
            Grid.GetTerminalSystem().GetBlocksOfType(Reactors, x => x.IsWorking);
            if (Reactors.Count == 0) return 0;

            float SummarizedOutput = 0;
            foreach (var Reactor in Reactors)
                SummarizedOutput += Reactor.MaxOutput;

            return SummarizedOutput;
        }

        public static float GetMaxBatteryPowerOutput(this IMyCubeGrid Grid)
        {
            List<IMyBatteryBlock> Batteries = new List<IMyBatteryBlock>();
            Grid.GetTerminalSystem().GetBlocksOfType(Batteries, x => x.IsWorking && x.HasCapacityRemaining);
            if (Batteries.Count == 0) return 0;

            float SummarizedOutput = 0;
            foreach (var Battery in Batteries)
                SummarizedOutput += Battery.MaxOutput();

            return SummarizedOutput;
        }

        public static float MaxOutput(this IMyBatteryBlock Battery)
        {
            return (MyDefinitionManager.Static.GetCubeBlockDefinition(Battery.BlockDefinition) as MyBatteryBlockDefinition).MaxPowerOutput;
        }

        public static bool HasCockpit(this IMyCubeGrid Grid)
        {
            return Grid.GetWorkingBlocks<IMyCockpit>().Count > 0;
        }

        public static bool HasGyros(this IMyCubeGrid Grid)
        {
            return Grid.GetWorkingBlocks<IMyGyro>().Count > 0;
        }

        public static bool IsGrid(this Sandbox.ModAPI.Ingame.MyDetectedEntityInfo Info)
        {
            return Info.Type == Sandbox.ModAPI.Ingame.MyDetectedEntityType.SmallGrid || Info.Type == Sandbox.ModAPI.Ingame.MyDetectedEntityType.LargeGrid;
        }

        public static HashSet<IMyVoxelMap> GetNearbyRoids(this IMyCubeGrid Grid, float Radius = 3000)
        {
            BoundingSphereD Sphere = new BoundingSphereD(Grid.GetPosition(), Radius);
            HashSet<IMyVoxelMap> Roids = new HashSet<IMyVoxelMap>();
            foreach(var entity in MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref Sphere))
            {
                if (entity is IMyVoxelMap && !(entity is MyPlanet)) Roids.Add(entity as IMyVoxelMap);
            }
            return Roids;
        }

        public static void DisableGyroOverride(this IMyCubeGrid Grid)
        {
            foreach (IMyGyro Gyro in Grid.GetWorkingBlocks<IMyGyro>())
            {
                Gyro.SetValueBool("Override", false);
            }
        }

        public static void SetPowerConsumptionMultiplier(this IEnumerable<IMyThrust> Thrusters, float Multiplier)
        {
            foreach (var Thruster in Thrusters)
                Thruster.PowerConsumptionMultiplier = Multiplier;
        }

        public static void SetThrustMultiplier(this IEnumerable<IMyThrust> Thrusters, float Multiplier)
        {
            foreach (var Thruster in Thrusters)
                Thruster.ThrustMultiplier = Multiplier;
        }

        public static bool HasThrustersInEveryDirection(this IMyCubeGrid Grid, IMyCockpit _cockpit = null)
        {
            IMyCockpit Cockpit = _cockpit != null ? _cockpit : GetFirstCockpit(Grid);
            if (Cockpit == null) return false;
            List<IMyThrust> Thrusters = Grid.GetWorkingBlocks<IMyThrust>();
            if (Thrusters.Count < 6) return false; // There physically can't be a thruster in every direction

            bool HasForwardThrust = false;
            bool HasBackwardThrust = false;
            bool HasUpThrust = false;
            bool HasDownThrust = false;
            bool HasLeftThrust = false;
            bool HasRightThrust = false;

            foreach (IMyThrust Thruster in Grid.GetWorkingBlocks<IMyThrust>())
            {
                if (Thruster.WorldMatrix.Forward == Cockpit.WorldMatrix.Forward) HasForwardThrust = true;
                else if (Thruster.WorldMatrix.Forward == Cockpit.WorldMatrix.Backward) HasBackwardThrust = true;
                else if (Thruster.WorldMatrix.Forward == Cockpit.WorldMatrix.Up) HasUpThrust = true;
                else if (Thruster.WorldMatrix.Forward == Cockpit.WorldMatrix.Down) HasDownThrust = true;
                else if (Thruster.WorldMatrix.Forward == Cockpit.WorldMatrix.Left) HasLeftThrust = true;
                else if (Thruster.WorldMatrix.Forward == Cockpit.WorldMatrix.Right) HasRightThrust = true;
            }

            return HasForwardThrust && HasBackwardThrust && HasUpThrust && HasDownThrust && HasLeftThrust && HasRightThrust;
        }

        /*public static List<IMySlimBlock> GetBlocksOnRay(this IMyCubeGrid Grid, Vector3D From, Vector3D To, Func<IMySlimBlock, bool> collect = null)
        {
            List<IMySlimBlock> Blocks = new List<IMySlimBlock>();
            List<Vector3I> BlockPositions = new List<Vector3I>();
            Grid.RayCastCells(From, To, BlockPositions);
            foreach (Vector3I Position in BlockPositions)
            {
                IMySlimBlock Block = Grid.GetCubeBlock(Position);
                if (Block == null) continue;
                if (collect == null || collect(Block)) Blocks.Add(Block);
            }
            return Blocks;
        }*/

        public static IMyCockpit GetFirstCockpit(this IMyCubeGrid Grid)
        {
            return Grid.GetWorkingBlocks<IMyCockpit>()[0];
        }

        public static bool Has<T>(this IMyCubeGrid Grid) where T : class, IMyTerminalBlock
        {
            return Grid.GetWorkingBlocks<T>().Count > 0;
        }
    }

    public static class GeneralExtensions
    {
        public static VRage.MyFixedPoint ComputeAmountThatFits(this IMyInventory Inventory, IMyInventoryItem Item, VRage.MyFixedPoint? Amount = null)
        {
            var Id = Item.Content.GetId();
            var ActualAmount = Amount.HasValue ? Amount.Value : Item.Amount;
            var FittingAmount = (Inventory as MyInventory).ComputeAmountThatFits(Id);
            return FittingAmount > ActualAmount ? ActualAmount : FittingAmount;
        }

        public static void PickupItem(this IMyInventory Inventory, IMyFloatingObject FloatingObject)
        {
            (Inventory as MyInventory).TakeFloatingObject(FloatingObject as MyFloatingObject);
        }

        public static VRage.MyFixedPoint GetAmount(this IMyFloatingObject FloatingObject)
        {
            return (FloatingObject as MyFloatingObject).Amount;
        }

        public static Vector3 GetGravity(this MyPlanet Planet, Vector3D Position)
        {
            var GravGen = Planet.Components.Get<MyGravityProviderComponent>();
            return GravGen.GetWorldGravity(Position);
        }

        public static bool IsInGravity(this MyPlanet Planet, Vector3D Position)
        {
            var GravGen = Planet.Components.Get<MyGravityProviderComponent>();
            return GravGen.IsPositionInRange(Position);
        }

        public static bool IsAllied(this Sandbox.ModAPI.Ingame.MyDetectedEntityInfo Info)
        {
            return Info.Relationship == MyRelationsBetweenPlayerAndBlock.Owner || Info.Relationship == MyRelationsBetweenPlayerAndBlock.FactionShare;
        }

        public static float GetMaxPowerOutput(this IMyCubeGrid Grid)
        {
            return Grid.GetMaxReactorPowerOutput() + Grid.GetMaxBatteryPowerOutput();
        }

        public static IMyPlayer GetPlayer(this IMyPlayerCollection Players, long ID)
        {
            List<IMyPlayer> player = new List<IMyPlayer>(1);
            Players.GetPlayers(player, x => x.IdentityId == ID);
            return player.FirstOrDefault();
        }

        public static Color GetRelationshipColor(this Sandbox.ModAPI.Ingame.MyDetectedEntityInfo Info)
        {
            Color retval = Color.Black;
            switch (Info.Relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Owner:
                    retval = Color.LightBlue;
                    break;

                case MyRelationsBetweenPlayerAndBlock.Neutral:
                    retval = Color.White;
                    break;

                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    retval = Color.DarkGreen;
                    break;

                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    retval = Color.Red;
                    break;

                case MyRelationsBetweenPlayerAndBlock.NoOwnership:
                    retval = Color.Gray;
                    break;
            }
            return retval;
        }

        public static MyDataReceiver AsNetworker(this IMyRadioAntenna Antenna)
        {
            return Antenna.GetComponent<MyDataReceiver>();
        }

        public static Sandbox.ModAPI.Ingame.MyDetectedEntityInfo Rename(this Sandbox.ModAPI.Ingame.MyDetectedEntityInfo Info, string Name)
        {
            return new Sandbox.ModAPI.Ingame.MyDetectedEntityInfo(Info.EntityId, Name, Info.Type, Info.HitPosition, Info.Orientation, Info.Velocity, Info.Relationship, Info.BoundingBox, Info.TimeStamp);
        }

        public static StringBuilder RemoveTrailingNewlines(this StringBuilder Builder)
        {
            while (Builder.Length > 0 && char.IsWhiteSpace(Builder[Builder.Length-1])) Builder.Length--;
            return Builder;
        }

        /// <summary>
        /// Returns world speed cap, in m/s.
        /// </summary>
        public static float GetSpeedCap(this IMyShipController ShipController)
        {
            if (ShipController.CubeGrid.GridSizeEnum == MyCubeSize.Small) return MyDefinitionManager.Static.EnvironmentDefinition.SmallShipMaxSpeed;
            if (ShipController.CubeGrid.GridSizeEnum == MyCubeSize.Large) return MyDefinitionManager.Static.EnvironmentDefinition.LargeShipMaxSpeed;
            return 100;
        }

        /// <summary>
        /// Returns world speed cap ratio to default cap of 100 m/s.
        /// </summary>
        public static float GetSpeedCapRatioToDefault(this IMyShipController ShipController)
        {
            return ShipController.GetSpeedCap() / 100;
        }

        public static void LogError(this IMyCubeGrid Grid, string Source, Exception Scrap)
        {
            string DisplayName = "";
            try
            {
                DisplayName = Grid.DisplayName;
            }
            finally
            {
                MyAPIGateway.Utilities.ShowMessage(DisplayName, $"Fatal error in '{Source}': {Scrap.Message}. {(Scrap.InnerException != null ? Scrap.InnerException.Message : "No additional info was given by the game :(")}");
            }
        }

        public static string Line(this string Str, int LineNumber, string NewlineStyle = "\r\n")
        {
            return Str.Split(NewlineStyle.ToCharArray())[LineNumber];
        }

        public static void DebugWrite(this IMyCubeGrid Grid, string Source, string Message)
        {
            if (SessionCore.Debug) MyAPIGateway.Utilities.ShowMessage(Grid.DisplayName, $"Debug message from '{Source}': {Message}");
        }

        public static List<IMyFaction> GetFactions(this IMyFactionCollection FactionCollection)
        {
            List<IMyFaction> AllFactions = new List<IMyFaction>();

            foreach (var FactionBuilder in FactionCollection.GetObjectBuilder().Factions)
            {
                IMyFaction Faction = null;
                Faction = FactionCollection.TryGetFactionById(FactionBuilder.FactionId);
                if (Faction != null) AllFactions.Add(Faction);
            }

            return AllFactions;
        }

        public static bool IsShared(this MyRelationsBetweenPlayerAndBlock Relations)
        {
            return Relations == MyRelationsBetweenPlayerAndBlock.Owner || Relations == MyRelationsBetweenPlayerAndBlock.FactionShare;
        }
    }

    public static class StringExt
    {
        public static string Truncate(this string value, int maxLength)
        {
            if (maxLength < 1) return "";
            if (string.IsNullOrEmpty(value)) return value;
            if (value.Length <= maxLength) return value;

            return value.Substring(0, maxLength);
        }

        public static VRage.Utils.MyStringHash ToMyStringHash(this string value)
        {
            return VRage.Utils.MyStringHash.GetOrCompute(value);
        }
    }

    public class EntityByDistanceSorter : IComparer<IMyEntity>, IComparer<IMySlimBlock>
    {
        public Vector3D Position { get; set; }
        public EntityByDistanceSorter(Vector3D Position)
        {
            this.Position = Position;
        }

        public int Compare(IMyEntity x, IMyEntity y)
        {
            var DistanceX = Vector3D.DistanceSquared(Position, x.GetPosition());
            var DistanceY = Vector3D.DistanceSquared(Position, y.GetPosition());

            if (DistanceX < DistanceY) return -1;
            if (DistanceX > DistanceY) return 1;
            return 0;
        }

        public int Compare(IMySlimBlock x, IMySlimBlock y)
        {
            var DistanceX = Vector3D.DistanceSquared(Position, x.CubeGrid.GridIntegerToWorld(x.Position));
            var DistanceY = Vector3D.DistanceSquared(Position, y.CubeGrid.GridIntegerToWorld(y.Position));

            if (DistanceX < DistanceY) return -1;
            if (DistanceX > DistanceY) return 1;
            return 0;
        }
    }

    public static class TypeHelpers
    {
        /// <summary>
        /// Checks if the given object is of given type.
        /// </summary>
        public static bool IsOfType<T>(this object Object, out T Casted) where T : class
        {
            Casted = Object as T;
            return Casted != null;
        }

        public static bool IsOfType<T>(this object Object) where T : class
        {
            return Object is T;
        }

        public static void AddOrUpdate<TKey, TValue>(this IDictionary<TKey, TValue> Dict, TKey Key, TValue Value)
        {
            if (Dict.ContainsKey(Key)) Dict[Key] = Value;
            else Dict.Add(Key, Value);
        }

        public static void RemoveAll<TKey, TValue>(this Dictionary<TKey, TValue> Dict, IEnumerable<TKey> RemoveKeys)
        {
            if (RemoveKeys == null || RemoveKeys.Count() == 0) return;
            foreach (TKey Key in RemoveKeys)
                Dict.Remove(Key);
        }

        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> Filter, out TSource First)
        {
            First = source.FirstOrDefault(Filter);
            return !First.Equals(default(TSource));
        }

        public static HashSet<T> ToHashSet<T>(this ICollection<T> Enum)
        {
            var Hashset = new HashSet<T>(Enum);
            if (Hashset.Count > 0 && Enum.Count > 0) return Hashset;
            foreach (var Item in Enum)
                Hashset.Add(Item);
            return Hashset;
        }

        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> Enum)
        {
            var Hashset = new HashSet<T>(Enum);
            if (Hashset.Count > 0 && Enum.Count() > 0) return Hashset;
            foreach (var Item in Enum)
                Hashset.Add(Item);
            return Hashset;
        }

        /// <summary>
        /// Sorts out an enumerable into lists of different types using a single loop.
        /// <para />
        /// This method is suited for 2 types.
        /// </summary>
        public static void SortByType<TI, TO1, TO2>(this IEnumerable<TI> Collection, ICollection<TO1> Type1, ICollection<TO2> Type2) where TI : class where TO1 : class, TI where TO2 : class, TI
        {
            foreach (TI Item in Collection)
            {
                TO1 Type1Item = Item as TO1;
                TO2 Type2Item = Item as TO2;
                if (Type1Item != null) Type1.Add(Type1Item);
                if (Type2Item != null) Type2.Add(Type2Item);
            }
        }

        /// <summary>
        /// Sorts out an enumerable into lists of different types using a single loop.
        /// <para />
        /// This method is suited for 3 types.
        /// </summary>
        public static void SortByType<TI, TO1, TO2, TO3>(this IEnumerable<TI> Collection, ICollection<TO1> Type1, ICollection<TO2> Type2, ICollection<TO3> Type3) where TI : class where TO1 : class, TI where TO2 : class, TI where TO3 : class, TI
        {
            foreach (TI Item in Collection)
            {
                TO1 Type1Item = Item as TO1;
                TO2 Type2Item = Item as TO2;
                TO3 Type3Item = Item as TO3;
                if (Type1Item != null) Type1.Add(Type1Item);
                if (Type2Item != null) Type2.Add(Type2Item);
                if (Type3Item != null) Type3.Add(Type3Item);
            }
        }

        /// <summary>
        /// Sorts out an enumerable into lists of different types using a single loop.
        /// <para />
        /// This method is suited for 4 types.
        /// </summary>
        public static void SortByType<TI, TO1, TO2, TO3, TO4>(this IEnumerable<TI> Collection, ICollection<TO1> Type1, ICollection<TO2> Type2, ICollection<TO3> Type3, ICollection<TO4> Type4) where TI : class where TO1 : class, TI where TO2 : class, TI where TO3 : class, TI where TO4 : class, TI
        {
            foreach (TI Item in Collection)
            {
                TO1 Type1Item = Item as TO1;
                TO2 Type2Item = Item as TO2;
                TO3 Type3Item = Item as TO3;
                TO4 Type4Item = Item as TO4;
                if (Type1Item != null) Type1.Add(Type1Item);
                if (Type2Item != null) Type2.Add(Type2Item);
                if (Type3Item != null) Type3.Add(Type3Item);
                if (Type4Item != null) Type4.Add(Type4Item);
            }
        }

        public static Object GetData<Key, Object>(this Dictionary<Key, Object> Dict, Key Tag)
        {
            return Dict[Tag];
        }

        /// <summary>
        /// Takes an enumerable and returns a dictionary.
        /// Key is the object, and value is the number of occurrences.
        /// </summary>
        public static Dictionary<T, int> CollapseDuplicates<T>(this IEnumerable<T> Enum)
        {
            Dictionary<T, int> DupeList = new Dictionary<T, int>(Enum.Count());
            foreach (T Item in Enum)
            {
                if (!DupeList.ContainsKey(Item)) DupeList.Add(Item, 1);
                else DupeList[Item] += 1;
            }
            return DupeList;
        }

        public static IList<T> Except<T>(this IList<T> Enum, T Exclude)
        {
            Enum.Remove(Exclude);
            return Enum;
        }

        public static List<T> Except<T>(this List<T> Enum, T Exclude)
        {
            Enum.Remove(Exclude);
            return Enum;
        }

        public static HashSet<T> Except<T>(this HashSet<T> Enum, T Exclude)
        {
            Enum.Remove(Exclude);
            return Enum;
        }
    }

    public static class Rexxar
    {
        public static bool PullAny(this IMyInventory Inventory, HashSet<IMyCubeBlock> sourceInventories, string component, int count)
        {
            return PullAny(Inventory, sourceInventories, new Dictionary<string, int> { { component, count } });
        }

        public static bool PullAny(this IMyInventory Inventory, HashSet<IMyCubeBlock> sourceInventories, Dictionary<string, int> toPull)
        {
            bool result = false;
            MyInventory inventory = Inventory as MyInventory;
            foreach (KeyValuePair<string, int> entry in toPull)
            {
                int remainingAmount = entry.Value;
                //Logging.Instance.WriteDebug(entry.Key + entry.Value);
                foreach (IMyCubeBlock block in sourceInventories)
                {
                    if (block == null || block.Closed)
                        continue;

                    MyInventory sourceInventory;
                    //get the output inventory for production blocks
                    if (((MyEntity)block).InventoryCount > 1)
                        sourceInventory = ((MyEntity)block).GetInventory(1);
                    else
                        sourceInventory = ((MyEntity)block).GetInventory();

                    List<MyPhysicalInventoryItem> sourceItems = sourceInventory.GetItems();
                    if (sourceItems.Count == 0)
                        continue;

                    var toMove = new List<KeyValuePair<MyPhysicalInventoryItem, int>>();
                    foreach (MyPhysicalInventoryItem item in sourceItems)
                    {
                        if (item.Content.SubtypeName == entry.Key)
                        {
                            if (item.Amount <= 0) //KEEEN
                                continue;

                            if (item.Amount >= remainingAmount)
                            {
                                toMove.Add(new KeyValuePair<MyPhysicalInventoryItem, int>(item, remainingAmount));
                                remainingAmount = 0;
                                result = true;
                            }
                            else
                            {
                                remainingAmount -= (int)item.Amount;
                                toMove.Add(new KeyValuePair<MyPhysicalInventoryItem, int>(item, (int)item.Amount));
                                result = true;
                            }
                        }
                    }

                    foreach (KeyValuePair<MyPhysicalInventoryItem, int> itemEntry in toMove)
                    {
                        if (inventory.ComputeAmountThatFits(itemEntry.Key.Content.GetId()) < itemEntry.Value)
                            return false;

                        sourceInventory.Remove(itemEntry.Key, itemEntry.Value);
                        inventory.Add(itemEntry.Key, itemEntry.Value);
                    }

                    if (remainingAmount == 0)
                        break;
                }
            }

            return result;
        }

        public static bool PullAny(this IMyInventory Inventory, HashSet<IMyCubeBlock> sourceInventories, MyDefinitionId item, float amount)
        {
            return PullAny(Inventory, sourceInventories, new Dictionary<MyDefinitionId, float> { { item, amount } });
        }

        public static bool PullAny(this IMyInventory Inventory, HashSet<IMyCubeBlock> sourceInventories, Dictionary<MyDefinitionId, float> toPull)
        {
            bool result = false;
            MyInventory inventory = Inventory as MyInventory;
            foreach (KeyValuePair<MyDefinitionId, float> entry in toPull)
            {
                VRage.MyFixedPoint remainingAmount = (VRage.MyFixedPoint)entry.Value;
                //Logging.Instance.WriteDebug(entry.Key + entry.Value);
                foreach (IMyCubeBlock block in sourceInventories)
                {
                    if (block == null || block.Closed)
                        continue;

                    MyInventory sourceInventory;
                    //get the output inventory for production blocks
                    if (((MyEntity)block).InventoryCount > 1)
                        sourceInventory = ((MyEntity)block).GetInventory(1);
                    else
                        sourceInventory = ((MyEntity)block).GetInventory();

                    List<MyPhysicalInventoryItem> sourceItems = sourceInventory.GetItems();
                    if (sourceItems.Count == 0)
                        continue;

                    var toMove = new List<KeyValuePair<MyPhysicalInventoryItem, VRage.MyFixedPoint>>();
                    foreach (MyPhysicalInventoryItem item in sourceItems)
                    {
                        if (item.Content.GetId() == entry.Key)
                        {
                            if (item.Amount <= 0) //KEEEN
                                continue;

                            if (item.Amount >= remainingAmount)
                            {
                                toMove.Add(new KeyValuePair<MyPhysicalInventoryItem, VRage.MyFixedPoint>(item, remainingAmount));
                                remainingAmount = 0;
                                result = true;
                            }
                            else
                            {
                                remainingAmount -= (int)item.Amount;
                                toMove.Add(new KeyValuePair<MyPhysicalInventoryItem, VRage.MyFixedPoint>(item, item.Amount));
                                result = true;
                            }
                        }
                    }

                    foreach (KeyValuePair<MyPhysicalInventoryItem, VRage.MyFixedPoint> itemEntry in toMove)
                    {
                        if (inventory.ComputeAmountThatFits(itemEntry.Key.Content.GetId()) < itemEntry.Value)
                            return false;

                        sourceInventory.Remove(itemEntry.Key, itemEntry.Value);
                        inventory.Add(itemEntry.Key, itemEntry.Value);
                    }

                    if (remainingAmount == 0)
                        break;
                }
            }

            return result;
        }
    }
}
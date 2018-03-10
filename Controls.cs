using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using VRage.Utils;

namespace Cheetah.LaserTools
{
    static class Controls
    {
        public static IMyTerminalControlSlider LaserBeam<T>() where T: IMyTerminalBlock
        {
            IMyTerminalControlSlider LaserBeam = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, T>("BeamLength");
            LaserBeam.Title = MyStringId.GetOrCompute("Beam Length");
            LaserBeam.Tooltip = MyStringId.GetOrCompute("Sets the laser beam's length.");
            LaserBeam.SupportsMultipleBlocks = true;
            LaserBeam.Enabled = HasBlockLogic;
            LaserBeam.Visible = HasBlockLogic;
            LaserBeam.SetLimits(Block => BlockReturn(Block, x => x.MinBeamLengthBlocks), Block => BlockReturn(Block, x => x.MaxBeamLengthBlocks));
            LaserBeam.Getter = (Block) => BlockReturn(Block, x => x.BeamLength);
            LaserBeam.Setter = (Block, NewLength) => BlockAction(Block, x => x.BeamLength = (int)NewLength);
            LaserBeam.Writer = (Block, Info) => Info.Append($"{BlockReturn(Block, x => x.BeamLength)} blocks");
            return LaserBeam;
        }

        public static IMyTerminalControlSlider SpeedMultiplier<T>() where T : IMyTerminalBlock
        {
            IMyTerminalControlSlider SpeedMultiplier = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, T>("SpeedMultiplier");
            SpeedMultiplier.Title = MyStringId.GetOrCompute("Speed Multiplier");
            SpeedMultiplier.Tooltip = MyStringId.GetOrCompute("Allows to increase tool's speed at the cost of power usage.\nThis is more efficient than piling on multiple tools.");
            SpeedMultiplier.SupportsMultipleBlocks = true;
            SpeedMultiplier.Enabled = HasBlockLogic;
            SpeedMultiplier.Visible = HasBlockLogic;
            SpeedMultiplier.SetLimits(1, 4);
            SpeedMultiplier.Getter = (Block) => BlockReturn(Block, x => x.SpeedMultiplier);
            SpeedMultiplier.Setter = (Block, NewSpeed) => BlockAction(Block, x => x.SpeedMultiplier = (int)NewSpeed);
            SpeedMultiplier.Writer = (Block, Info) => Info.Append($"x{BlockReturn(Block, x => x.SpeedMultiplier)}");
            return SpeedMultiplier;
        }

        public static IMyTerminalControlSlider HarvestEfficiency()
        {
            IMyTerminalControlSlider HarvestEfficiency = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipDrill>("HarvestEfficiency");
            HarvestEfficiency.Title = MyStringId.GetOrCompute("Harvest Efficiency");
            HarvestEfficiency.Tooltip = MyStringId.GetOrCompute("Allows to increase drill's yield at the cost of power usage.");
            HarvestEfficiency.SupportsMultipleBlocks = true;
            HarvestEfficiency.Enabled = HasDrillLogic;
            HarvestEfficiency.Visible = HasDrillLogic;
            HarvestEfficiency.SetLimits(1, 2);
            HarvestEfficiency.Getter = (Block) => DrillReturn(Block, x => x.HarvestEfficiency);
            HarvestEfficiency.Setter = (Block, NewEfficiency) => DrillAction(Block, x => x.HarvestEfficiency = NewEfficiency);
            HarvestEfficiency.Writer = (Block, Info) => Info.Append($"x{Math.Round(DrillReturn(Block, x => x.HarvestEfficiency), 2)}");
            return HarvestEfficiency;
        }

        public static IMyTerminalControlCheckbox DistanceMode<T>() where T : IMyTerminalBlock
        {
            IMyTerminalControlCheckbox DistanceMode = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, T>("DistanceMode");
            DistanceMode.SupportsMultipleBlocks = true;
            DistanceMode.Enabled = HasBlockLogic;
            DistanceMode.Visible = HasBlockLogic;
            DistanceMode.Getter = (Block) => BlockReturn(Block, x => x.DistanceMode);
            DistanceMode.Setter = (Block, NewMode) => BlockAction(Block, x => x.DistanceMode = NewMode);
            return DistanceMode;
        }

        public static bool HasBlockLogic(IMyTerminalBlock Block)
        {
            try
            {
                return Block.HasComponent<LaserToolBase>();
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError("IsOurBlock", Scrap);
                return false;
            }
        }

        public static bool HasDrillLogic(IMyTerminalBlock Block)
        {
            try
            {
                return Block.HasComponent<LaserDrill>();
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError("IsOurBlock", Scrap);
                return false;
            }
        }

        public static void DrillAction(IMyTerminalBlock Block, Action<LaserDrill> Action)
        {
            try
            {
                LaserDrill Logic;
                if (!Block.TryGetComponent(out Logic)) return;
                Action(Logic);
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError("BlockAction", Scrap);
                return;
            }
        }

        public static void BlockAction(IMyTerminalBlock Block, Action<LaserToolBase> Action)
        {
            try
            {
                LaserToolBase Logic;
                if (!Block.TryGetComponent(out Logic)) return;
                Action(Logic);
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError("BlockAction", Scrap);
                return;
            }
        }

        public static T DrillReturn<T>(IMyTerminalBlock Block, Func<LaserDrill, T> Getter, T Default = default(T))
        {
            try
            {
                LaserDrill Logic;
                if (!Block.TryGetComponent(out Logic)) return Default;
                return Getter(Logic);
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError("BlockReturn", Scrap);
                return Default;
            }
        }

        public static T BlockReturn<T>(IMyTerminalBlock Block, Func<LaserToolBase, T> Getter, T Default = default(T))
        {
            try
            {
                LaserToolBase Logic;
                if (!Block.TryGetComponent(out Logic)) return Default;
                return Getter(Logic);
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError("BlockReturn", Scrap);
                return Default;
            }
        }
    }
}

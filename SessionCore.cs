using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Cheetah.LaserTools
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class SessionCore : MySessionComponentBase
    {
        public static bool Debug { get; } = false;
        public static bool VerboseDebug { get; } = false;
        /// <summary>
        /// How many ticks to skip between Work() calls. Working speed is compensated.
        /// </summary>
        public static int WorkSkipTicks { get; } = 30;
        /// <summary>
        /// How many ticks to skip between BuildInventoryCache() calls.
        /// </summary>
        public static int InventoryRebuildSkipTicks { get; } = 300;
        public const float TickLengthMs = 1000 / 60;
        public const string ModName = "LaserWelders.";
        public const uint ModID = 927381544;
        public static readonly Guid StorageGuid = new Guid("22125116-4EE3-4F87-B6D6-AE1232014EA5");

        static bool Inited = false;
        protected static readonly HashSet<Action> SaveActions = new HashSet<Action>();
        public static void SaveRegister(Action Proc) => SaveActions.Add(Proc);
        public static void SaveUnregister(Action Proc) => SaveActions.Remove(Proc);

        public override void UpdateBeforeSimulation()
        {
            if (!Inited) Init();
        }

        void Init()
        {
            if (Inited || MyAPIGateway.Session == null) return;
            try
            {
                Networking.Networker.Init(ModID);
            }
            catch (Exception Scrap)
            {
                LogError("Init", Scrap);
            }
            Inited = true;
        }

        public override void SaveData()
        {
            foreach (var Proc in SaveActions)
                try
                {
                    Proc.Invoke();
                }
                catch { }
        }

        protected override void UnloadData()
        {
            try
            {
                SaveActions.Clear();
                Networking.Networker.Close();
            }
            catch { }
        }

        public static bool InitedWelderControls { get; protected set; } = false;
        public static void InitWelderControls()
        {
            if (InitedWelderControls) return;

            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(Controls.LaserBeam<IMyShipWelder>());
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(Controls.SpeedMultiplier<IMyShipWelder>());
            var DistanceMode = Controls.DistanceMode<IMyShipWelder>();
            DistanceMode.Title = MyStringId.GetOrCompute("Weld Furthest First");
            DistanceMode.Tooltip = MyStringId.GetOrCompute($"If enabled, Laser Welder will build furthest block first before proceeding on new one.");
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(DistanceMode);

            InitedWelderControls = true;
        }

        public static bool InitedGrinderControls { get; protected set; } = false;
        public static void InitGrinderControls()
        {
            if (InitedGrinderControls) return;

            MyAPIGateway.TerminalControls.AddControl<IMyShipGrinder>(Controls.LaserBeam<IMyShipGrinder>());
            MyAPIGateway.TerminalControls.AddControl<IMyShipGrinder>(Controls.SpeedMultiplier<IMyShipGrinder>());
            var DistanceMode = Controls.DistanceMode<IMyShipGrinder>();
            DistanceMode.Title = MyStringId.GetOrCompute("Grind Closest First");
            DistanceMode.Tooltip = MyStringId.GetOrCompute($"If enabled, Laser Grinder will dismantle closest block first before proceeding on new one.");
            MyAPIGateway.TerminalControls.AddControl<IMyShipGrinder>(DistanceMode);

            InitedGrinderControls = true;
        }

        public static bool InitedDrillControls { get; protected set; } = false;
        public static void InitDrillControls()
        {
            if (InitedDrillControls) return;

            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(Controls.HarvestEfficiency());

            InitedDrillControls = true;
        }

        public static void DebugWrite(string Source, string Message, bool IsExcessive = false, string DebugPrefix = null)
        {
            try
            {
                MyLog.Default.WriteLine(DebugPrefix + Source + $": Debug message: {Message}");
                MyLog.Default.Flush();
                if (DebugPrefix == null) DebugPrefix = $"{ModName}.";
                if (Debug && (!IsExcessive || VerboseDebug))
                    MyAPIGateway.Utilities.ShowMessage(DebugPrefix + Source, $"Debug message: {Message}");
            }
            catch { }
        }

        public static void LogError(string Source, Exception Scrap, bool IsExcessive = false, string DebugPrefix = null)
        {
            try
            {
                MyLog.Default.WriteLine($"{DebugPrefix + Source}: CRASH: '{Scrap.Message}'");
                MyLog.Default.WriteLine(Scrap);
                MyLog.Default.Flush();
                if (DebugPrefix == null) DebugPrefix = $"{ModName}.";
                if (Debug/* && (!IsExcessive || VerboseDebug)*/)
                    MyAPIGateway.Utilities.ShowMessage(DebugPrefix + Source, $"CRASH: '{Scrap.Message}'");
            }
            catch { }
        }
    }

    public static class DebugHelper
    {
        private static readonly List<int> AlreadyPostedMessages = new List<int>();
        public static bool Debug => SessionCore.Debug;

        public static void Print(string Source, string Message, bool AntiSpam = true)
        {
            string combined = Source + ": " + Message;
            int hash = combined.GetHashCode();

            if (!AlreadyPostedMessages.Contains(hash))
            {
                AlreadyPostedMessages.Add(hash);
                MyAPIGateway.Utilities.ShowMessage(Source, Message);
                MyLog.Default.WriteLine($"{Source}: Debug message: {Message}");
                MyLog.Default.Flush();
            }
        }

        public static void DebugWrite(this IMyCubeGrid Grid, string Source, string Message, bool AntiSpam = true, bool ForceWrite = false)
        {
            if (Debug || ForceWrite) Print(Grid.DisplayName, $"Debug message from '{Source}': {Message}");
        }

        public static void LogError(this IMyCubeGrid Grid, string Source, Exception Scrap, bool AntiSpam = true, bool ForceWrite = false)
        {
            if (!Debug && !ForceWrite) return;
            string DisplayName = "Unknown Grid";
            try
            {
                DisplayName = Grid.DisplayName;
            }
            finally
            {
                string Message = $"Fatal error in '{Source}': {Scrap.Message}. {(Scrap.InnerException != null ? Scrap.InnerException.Message : "No additional info was given by the game :(")}";
                Print(DisplayName, Message);
                MyLog.Default.WriteLine(Scrap);
                MyLog.Default.Flush();
            }
        }
    }
}

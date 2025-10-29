using BepInEx;
using BepInEx.Unity.IL2CPP;
using GTFO.API;
using HarmonyLib;
using System.Linq;
using Il2CppInterop.Runtime.Injection;
using ShuttleboxPlugin.Patches;
using ShuttleboxPlugin.Modules;
using ShuttleboxPlugin.Utils;
using Player;
using TerminalQueryAPI;

namespace ShuttleboxPlugin
{
    [BepInPlugin("JarheadHME.ShuttleboxPlugin", "ShuttleboxPlugin", VersionInfo.Version)]

    [BepInDependency("dev.gtfomodding.gtfo-api",     BepInDependency.DependencyFlags.HardDependency)]

    [BepInDependency(MTFO.MTFO.GUID,                 BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(PartialData_GUID,               BepInDependency.DependencyFlags.SoftDependency)]

    [BepInDependency(QueryableAPI.PLUGIN_GUID,       BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(AmorLib_GUID,                   BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(TexturePainterAPI_GUID,         BepInDependency.DependencyFlags.HardDependency)]

    // Used to handle inserting small items, including pocket items
    [BepInDependency("JarheadHME.CustomInteractAPI", BepInDependency.DependencyFlags.HardDependency)]
    internal class EntryPoint : BasePlugin
    {
        private Harmony _Harmony = null;

        internal const string PartialData_GUID = "MTFO.Extension.PartialBlocks";
        internal const string TexturePainterAPI_GUID = "TexturePainterAPI";
        internal const string AmorLib_GUID = "Amor.AmorLib";

        public override void Load()
        {
            _Harmony = new Harmony($"{VersionInfo.RootNamespace}.Harmony");
            _Harmony.PatchAll();
            Logger.Info($"Plugin has loaded with {_Harmony.GetPatchedMethods().Count()} patches!");

            ClassInjector.RegisterTypeInIl2Cpp<Shuttlebox_Core>();
            ClassInjector.RegisterTypeInIl2Cpp<Shuttlebox_LightFader>();
            GTFO.API.Il2CppAPI.InjectWithInterface<Shuttlebox_AnyItemInteract>(); 

            InsertTypeEnum.Init();

            NetworkAPI.RegisterEvent<pItemData>(NetworkEvents.DeregisterTerminalItemEvent, NetworkEvents.AttemptDeregisterTerminalItem);
            NetworkAPI.RegisterEvent<pItemData>(NetworkEvents.OverrideQueryEvent, NetworkEvents.OverrideItemQueryInformation);
            NetworkAPI.RegisterEvent<pItemData>(NetworkEvents.ResetOverriddenQueryEvent, NetworkEvents.ResetItemQueryOverride);

            GameDataAPI.OnGameDataInitialized += ShuttleboxPlacements.Init;
            
            LevelAPI.OnLevelCleanup += OnLevelCleanup;

            ElevatorPatch.OnStopElevator += OnStopElevator;
        }

        public static void OnLevelCleanup()
        {
            SpawnShuttleboxesPatch.ClearDistributedZones();
            Shuttlebox_Core.OnLevelCleanup();
            KeyItemTracker.ClearTrackedKeyIds();
        }

        public static void OnStopElevator()
        {
            KeyItemTracker.TrackAllKeysInLevel();

            Shuttlebox_Core.ActivateLights();
            Shuttlebox_Core.FinishSetupSummonItems();
            Shuttlebox_Core.CloseShuttleboxesAtStart();
            Shuttlebox_Core.FinishSetupAllItemDicts();
        }

        public override bool Unload()
        {
            _Harmony.UnpatchSelf();
            return base.Unload();
        }
    }
}

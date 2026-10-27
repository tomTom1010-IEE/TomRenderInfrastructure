using BepInEx;
using HarmonyLib;
using TomProbeRuntime;

namespace TomProbePlugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("CharaStudio")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.tomtom1010.kkshadersplus.probes";
        public const string PluginName = "Tom Probe Runtime";
        public const string PluginVersion = "0.2.2";
        private Harmony _harmony;

        private void Awake()
        {
            _harmony = Harmony.CreateAndPatchAll(typeof(VisibilityHooks), PluginGuid);
            TomProbeManager.EnsureInstance();
            var budget = Config.Bind("Capture", "SH samples per frame", 1,
                new BepInEx.Configuration.ConfigDescription("Global sample budget, shared by all SH volumes. One sample captures six faces and reads them back.",
                    new BepInEx.Configuration.AcceptableValueRange<int>(1, 4)));
            TomProbeCaptureScheduler.SamplesPerFrame = budget.Value;
            budget.SettingChanged += (sender, args) => TomProbeCaptureScheduler.SamplesPerFrame = budget.Value;
            TomProbeCaptureScheduler.EnsureInstance();
            Logger.LogInfo("Tom probe runtime " + PluginVersion + ": camera-scoped sky snapshots, linear HDR backgrounds, sky-source invalidation, atomic SH receiver refresh. Environment preset excludes named Chara layers, not arbitrary objects.");
        }

        private void OnDestroy() { if (_harmony != null) _harmony.UnpatchSelf(); }

        private static class VisibilityHooks
        {
            // Studio hides item renderers without deactivating their GameObjects.
            [HarmonyPostfix, HarmonyPatch(typeof(Studio.OCIItem), "set_visible")]
            private static void AfterVisibility(Studio.OCIItem __instance, bool __0)
            {
                if (__instance.objectItem == null) return;
                foreach (var control in __instance.objectItem.GetComponentsInChildren<TomProbeControl>(true))
                    control.SetSceneVisible(__0);
            }
        }
    }
}

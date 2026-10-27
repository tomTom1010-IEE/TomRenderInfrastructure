using System.IO;
using System;
using BepInEx;
using TomBSDFRuntime;

namespace TomBSDFPlugin
{
    [BepInPlugin("com.tomtom1010.tombsdf.scene", "TomBSDF KKS Scene Companion", "0.6.0")]
    [BepInProcess("CharaStudio")]
    [BepInProcess("KoikatsuSunshine")]
    public sealed class Plugin : BaseUnityPlugin
    {
        TomBSDFSceneService service;
        TomBSDFDiagnostics diagnostics;
        ScreenshotQualityAdapter captureAdapter;
        float nextCaptureLookup;
        void Awake()
        {
            var active = Config.Bind("Scene Lighting", "Enabled", true, "Master switch for rectangle lights, scene buffers and transparent native-shadow receivers. Off preserves the existing native rendering path.");
            var buffers = Config.Bind("Scene Buffers", "Enabled", true, "Allow camera-scoped color/depth requests, including visible tom/BSDFGlass refraction. No consumers means no copy or allocation. Off keeps glass absorption/reflection without screen refraction. No SSR.");
            var captureQuality = Config.Bind("Area Light Quality", "Screenshot Reference", true, "Promote rectangle lights to the preserved 128-sample Reference during a Screenshot Manager render capture. Live view and saved ME quality values are unchanged. Does not change thin-film quality.");
            var receiving = Config.Bind("Alpha Shadows", "Receive Native Shadows", true, "Reuse native directional, spot and point shadow maps for modern coverage Alpha. Requires the matching shader resource; unavailable maps stay at native transparent fallback. No extra shadow rendering.");
            var shadowBudget = Config.Bind("Alpha Shadows", "Copy Memory Budget MB", 128, "Maximum native shadow copy memory per camera. Unselected/over-budget lights retain the original transparent behavior; opaque materials are unchanged.");
            var bridge = Config.Bind("Validation", "File Bridge", false, "Explicit local validation commands only. No startup GPU tests. Results are written below UserData/TomBSDF/Validation.");
            service = TomBSDFSceneService.EnsureInstance();
            service.extensionEnabled = active.Value;
            service.sceneBuffersEnabled = buffers.Value;
            service.captureReferenceEnabled = captureQuality.Value;
            service.shadowReceiversEnabled = receiving.Value;
            service.Shadows.MemoryBudgetMB = Math.Max(1, shadowBudget.Value);
            active.SettingChanged += (sender, args) => { if (service) service.extensionEnabled = active.Value; };
            buffers.SettingChanged += (sender, args) => { if (service) service.sceneBuffersEnabled = buffers.Value; };
            captureQuality.SettingChanged += (sender, args) => { if (service) service.captureReferenceEnabled = captureQuality.Value; };
            receiving.SettingChanged += (sender, args) => { if (service) service.shadowReceiversEnabled = receiving.Value; };
            shadowBudget.SettingChanged += (sender, args) => { if (service && service.Shadows != null) service.Shadows.MemoryBudgetMB = Math.Max(1, shadowBudget.Value); };
            captureAdapter = new ScreenshotQualityAdapter();
            ConnectScreenshotManager();
            diagnostics = gameObject.AddComponent<TomBSDFDiagnostics>();
            diagnostics.root = Path.Combine(Paths.GameRootPath, "UserData/TomBSDF/Validation");
            diagnostics.enabled = bridge.Value;
            bridge.SettingChanged += (sender, args) => { if (diagnostics) diagnostics.enabled = bridge.Value; };
            Logger.LogInfo("TomBSDF scene/buffer protocol v1 active; optional BSDFGlass finite screen refraction. No color-space, Probe or SH replacement, extra shadow rendering or SSR. Validation bridge=" + bridge.Value);
        }
        void Update()
        {
            if (captureAdapter == null || captureAdapter.Connected || UnityEngine.Time.realtimeSinceStartup < nextCaptureLookup) return;
            nextCaptureLookup = UnityEngine.Time.realtimeSinceStartup + 5;
            ConnectScreenshotManager();
        }
        void ConnectScreenshotManager()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (captureAdapter.Connect(assembly.GetType("Screencap.ScreenshotManager", false)))
                {
                    Logger.LogInfo("Screenshot Manager capture quality API connected; ordinary UI screenshots stay at live quality.");
                    break;
                }
        }
        void OnDestroy()
        {
            if (captureAdapter != null) captureAdapter.Dispose();
            if (diagnostics) Destroy(diagnostics);
            if (service) Destroy(service.gameObject);
        }
    }
}

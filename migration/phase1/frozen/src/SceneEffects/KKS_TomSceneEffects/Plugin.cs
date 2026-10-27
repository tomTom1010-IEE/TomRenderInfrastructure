using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using KKAPI.Studio.SaveLoad;
using TomSceneEffects;
using UnityEngine;

namespace TomSceneEffectsPlugin
{
    [BepInPlugin(Guid, "Tom Shared Scene Effects (DOF prototype)", "0.1.0")]
    [BepInProcess("CharaStudio.exe")]
    [BepInDependency("marco.kkapi")]
    [BepInDependency("com.tomtom1010.tombsdf.scene", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.tomtom1010.sceneeffects";
        public static Plugin Current { get; private set; }
        public DofScenePolicy ScenePolicy { get; private set; } = DofScenePolicy.Inherit;
        public DofMode EffectiveMode { get { return DofPolicy.Resolve(global.Value, ScenePolicy); } }
        public DofBinding Binding { get; private set; }
        ConfigEntry<DofMode> global;
        Type nativeType;
        MethodInfo nativeRender;
        Harmony harmony;
        bool warned, ready;
        volatile bool captureLookupPending;
        readonly List<MonoBehaviour> components = new List<MonoBehaviour>();

        void Awake()
        {
            Current = this;
            global = Config.Bind("Depth Of Field", "Default Mode", DofMode.Native,
                "Native keeps original Studio rendering. BeforeTransparents runs native DOF before all transparent geometry, keeping transparent highlights sharp. Scene overrides take priority; native DOF parameters remain Studio-owned.");
            if (global.Value != DofMode.Native && global.Value != DofMode.BeforeTransparents) global.Value = DofMode.Native;
            global.SettingChanged += Changed;
            StudioSaveLoadApi.RegisterExtraBehaviour<DofSceneController>(Guid);
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if (assembly.GetName().Name == "Assembly-CSharp-firstpass")
                    { nativeType = assembly.GetType("UnityStandardAssets.ImageEffects.DepthOfField", false); break; }
                if (nativeType == null || !typeof(MonoBehaviour).IsAssignableFrom(nativeType)) throw new TypeLoadException("Supported native Studio DepthOfField unavailable");
                nativeRender = nativeType.GetMethod("OnRenderImage", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(RenderTexture), typeof(RenderTexture) }, null);
                if (nativeRender == null || nativeRender.IsStatic || nativeRender.ReturnType != typeof(void)) throw new MissingMethodException("Native DepthOfField.OnRenderImage signature");
                var patches = Harmony.GetPatchInfo(nativeRender);
                if (patches != null && (patches.Prefixes.Count != 0 || patches.Transpilers.Count != 0 || patches.Postfixes.Count != 0 || patches.Finalizers.Count != 0))
                    throw new InvalidOperationException("Another provider already intercepts native DOF; automatic takeover refused");
                harmony = new Harmony(Guid);
                harmony.Patch(nativeRender, prefix: new HarmonyMethod(typeof(Plugin), nameof(NativePrefix)));
                AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;
                BsdfCaptureAdapter.TryInstall(harmony, Warn);
                Camera.onPreCull += PrepareCamera;
                ready = true;
                Logger.LogInfo("Shared DOF prototype ready. Default=Native; no shader, Bundle, native DOF parameters or shared depth changes. Early mode affects ALL transparent geometry and adds one late passthrough.");
            }
            catch (Exception error) { Warn(error); }
        }

        static bool NativePrefix(MonoBehaviour __instance, RenderTexture source, RenderTexture destination)
        {
            return DofBinding.BeforeNative(__instance, source, destination);
        }

        void Changed(object sender, EventArgs args) { ApplyPolicy(); }
        void AssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            // Loading callbacks may run off the Unity thread. Defer patching once;
            // never poll assemblies or allocate a lookup every disabled frame.
            if (args.LoadedAssembly.GetName().Name == "TomBSDF.Runtime") captureLookupPending = true;
        }
        public void SetScenePolicy(int value)
        {
            if (value < -1 || value > 1) throw new ArgumentOutOfRangeException("scenePolicy");
            ScenePolicy = (DofScenePolicy)value; ApplyPolicy();
        }
        public void SetGlobalMode(int value)
        {
            if (value < 0 || value > 1) throw new ArgumentOutOfRangeException("globalMode");
            global.Value = (DofMode)value; ApplyPolicy();
        }
        void ApplyPolicy() { if (Binding != null) Binding.SetMode(BsdfCaptureAdapter.Failed ? DofMode.Native : EffectiveMode); }
        void LateUpdate()
        {
            if (captureLookupPending && harmony != null)
            {
                captureLookupPending = false;
                BsdfCaptureAdapter.TryInstall(harmony, Warn); ApplyPolicy();
            }
            if (Binding != null && (!Binding.Camera || !Binding.Native)) Release();
            if (Binding != null) Binding.Synchronize();
        }

        void PrepareCamera(Camera camera)
        {
            // The normal Screencap path re-renders the real Studio camera with a new target.
            // Unknown clone/reflection/probe cameras are not guessed from names or sizes.
            if (!camera || camera != Camera.main || nativeType == null || nativeRender == null) return;
            if (camera.cameraType == UnityEngine.CameraType.Reflection || camera.cameraType == UnityEngine.CameraType.Preview) return;
            if (Binding == null || Binding.Camera != camera)
            {
                Release();
                camera.GetComponents(components);
                MonoBehaviour effect = null;
                foreach (var component in components) if (component && component.GetType() == nativeType) { effect = component; break; }
                if (!effect) return;
                try
                {
                    Binding = new DofBinding(camera, effect, nativeRender);
                    Binding.Failed = Warn;
                    Binding.SetMode(BsdfCaptureAdapter.Failed ? DofMode.Native : EffectiveMode);
                }
                catch (Exception error) { Warn(error); return; }
            }
            Binding.PrepareRender();
        }

        void Warn(Exception error)
        {
            if (warned) return;
            warned = true; Logger.LogWarning("DOF compatibility unavailable; keeping native behavior. " + error);
        }
        void Release() { if (Binding != null) { Binding.Dispose(); Binding = null; } }
        void OnDestroy()
        {
            Camera.onPreCull -= PrepareCamera;
            AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
            if (global != null) global.SettingChanged -= Changed;
            Release();
            if (harmony != null) harmony.UnpatchSelf();
            if (Current == this) Current = null;
        }

        // Bounded optional integration API; no generic reflection execution or file commands.
        public static string StateJson()
        {
            var p = Current;
            if (!p) return "{\"available\":false}";
            var b = p.Binding;
            var dof = b == null ? null : b.Native;
            return JsonUtility.ToJson(new State { available = p.ready, version = "0.1.0-prototype",
                globalMode = (int)p.global.Value, scenePolicy = (int)p.ScenePolicy, effectiveMode = (int)p.EffectiveMode,
                cameraID = b == null || !b.Camera ? 0 : b.Camera.GetInstanceID(),
                nativeEnabled = b != null && b.Native && b.Native.enabled,
                relayEnabled = b != null && b.Relay && b.Relay.enabled,
                status = !p.ready ? "Unsupported native DOF; original untouched" : b == null ? "Waiting for Studio main camera" : b.Status,
                sceneColorRouting = BsdfCaptureAdapter.Status,
                sequence = b == null ? 0 : b.Sequence, earlyExecutions = b == null ? 0 : b.EarlyExecutions,
                lateCopies = b == null ? 0 : b.LateCopies, nativeExecutions = b == null ? 0 : b.NativeExecutions,
                failures = b == null ? 0 : b.Failures,
                nativeFocalSize = NativeFloat(dof,"focalSize"), nativeAperture = NativeFloat(dof,"aperture"),
                nativeFocalLength = NativeFloat(dof,"focalLength"),
                cameraWidth = b == null || !b.Camera ? 0 : b.Camera.pixelWidth,
                cameraHeight = b == null || !b.Camera ? 0 : b.Camera.pixelHeight,
                depthMode = b == null || !b.Camera ? "" : b.Camera.depthTextureMode.ToString() });
        }
        static float NativeFloat(MonoBehaviour effect, string field)
        {
            if (!effect) return 0;
            var f = effect.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            return f == null || f.FieldType != typeof(float) ? 0 : (float)f.GetValue(effect);
        }
        public static void ApplyScenePolicy(int value)
        {
            if (!Current) throw new InvalidOperationException("Scene effects provider unavailable");
            Current.SetScenePolicy(value);
        }
        public static void ApplyGlobalMode(int value)
        {
            if (!Current) throw new InvalidOperationException("Scene effects provider unavailable");
            Current.SetGlobalMode(value);
        }
        [Serializable] sealed class State
        {
            public bool available, nativeEnabled, relayEnabled;
            public string version, status, sceneColorRouting;
            public int globalMode, scenePolicy, effectiveMode, cameraID, sequence, earlyExecutions, lateCopies, nativeExecutions, failures;
            public int cameraWidth, cameraHeight;
            public float nativeFocalSize, nativeAperture, nativeFocalLength;
            public string depthMode;
        }
    }
}

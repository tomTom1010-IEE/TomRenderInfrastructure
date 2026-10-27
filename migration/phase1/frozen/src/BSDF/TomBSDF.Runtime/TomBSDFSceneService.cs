using System.Collections.Generic;
using UnityEngine;

namespace TomBSDFRuntime
{
    [ExecuteAlways]
    public sealed class TomBSDFSceneService : MonoBehaviour
    {
        public const int ProtocolVersion = 1, MaxLights = 4;
        static readonly List<TomRectAreaLightControl> Lights = new List<TomRectAreaLightControl>();
        static TomBSDFSceneService instance;
        public static TomBSDFSceneService Current { get { return instance; } }
        static readonly int Params = Shader.PropertyToID("_TomBSDFSceneParams");
        static readonly int Centers = Shader.PropertyToID("_TomBSDFRectCenter");
        static readonly int Rights = Shader.PropertyToID("_TomBSDFRectRight");
        static readonly int Ups = Shader.PropertyToID("_TomBSDFRectUp");
        static readonly int Radiances = Shader.PropertyToID("_TomBSDFRectRadiance");
        public bool extensionEnabled = true;
        public bool captureReferenceEnabled = true;
        public bool sceneBuffersEnabled = true;
        public bool shadowReceiversEnabled = true;
        public TomBSDFShadowResources shadowResources;
        public TomBSDFSceneBufferResources sceneBufferResources;
        float nextBufferResourceLookup;
        public TomBSDFSceneBuffers Buffers { get; private set; }
        public TomBSDFShadowReceivers Shadows { get; private set; }
        public TomBSDFGlassConsumers Glass { get; private set; }
        public TomBSDFTessellationBounds TessellationBounds { get; private set; }
        float nextShadowResourceLookup;
        public int selectedLights, droppedLights;
        readonly List<TomRectAreaLightControl.Settings> selection = new List<TomRectAreaLightControl.Settings>();
        sealed class State
        {
            public Camera camera;
            public Vector4 parameters;
            public readonly TomBSDFMaterialQualityScope quality = new TomBSDFMaterialQualityScope();
            public readonly Vector4[] center = new Vector4[MaxLights], right = new Vector4[MaxLights], up = new Vector4[MaxLights], radiance = new Vector4[MaxLights];
        }
        readonly List<State> states = new List<State>();
        int depth;

        public static void Register(TomRectAreaLightControl light) { if (!Lights.Contains(light)) Lights.Add(light); }
        public static void Unregister(TomRectAreaLightControl light) { Lights.Remove(light); }
        public static TomBSDFSceneService EnsureInstance()
        {
            if (instance) return instance;
            var go = new GameObject("TomBSDF Scene Service") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying) DontDestroyOnLoad(go);
            return go.AddComponent<TomBSDFSceneService>();
        }

        void OnEnable()
        {
            if (instance && instance != this) { enabled = false; return; }
            instance = this;
            depth = 0;
            Buffers = new TomBSDFSceneBuffers();
            Glass = new TomBSDFGlassConsumers();
            TessellationBounds = new TomBSDFTessellationBounds();
            Shadows = new TomBSDFShadowReceivers();
            Shadows.Abort();
            Clear();
            Camera.onPreCull += PrepareCamera;
            Camera.onPreRender += BeginCamera;
            Camera.onPostRender += EndCamera;
        }
        void OnDisable()
        {
            Camera.onPreCull -= PrepareCamera;
            Camera.onPreRender -= BeginCamera;
            Camera.onPostRender -= EndCamera;
            RestoreMaterialQuality();
            if (Glass != null) { Glass.Dispose(); Glass = null; }
            if (TessellationBounds != null) { TessellationBounds.Dispose(); TessellationBounds = null; }
            if (Buffers != null) { Buffers.Dispose(); Buffers = null; }
            if (Shadows != null) { Shadows.Dispose(); Shadows = null; }
            if (instance == this) { depth = 0; Clear(); instance = null; }
        }
        public static void Clear() { Shader.SetGlobalVector(Params, Vector4.zero); TomBSDFSceneBuffers.ClearGlobals(); TomBSDFShadowReceivers.ClearGlobals(); }

        void RestoreMaterialQuality()
        {
            for (int i = states.Count - 1; i >= 0; i--) states[i].quality.Dispose();
        }
        void LateUpdate()
        {
            // Camera.Render is synchronous. An outstanding scope at this point
            // belongs to an aborted external render, not an enclosing camera.
            if (Shadows != null) Shadows.Abort();
            if (Glass != null) Glass.Abort();
            if (Buffers != null) Buffers.Abort();
            if (TessellationBounds != null) TessellationBounds.Abort();
            if (depth == 0) return;
            RestoreMaterialQuality(); depth = 0; Clear();
        }

        public static TomSceneBufferLease AcquireSceneBuffers(Camera camera, TomSceneBufferFlags flags,
            TomSceneBufferResolution resolution = TomSceneBufferResolution.Full)
        {
            return instance && instance.enabled && instance.Buffers != null
                ? instance.Buffers.Acquire(camera, flags, resolution) : new TomSceneBufferLease();
        }
        void PrepareCamera(Camera camera)
        {
            // Native geometry works with extension off too. Bounds must be
            // ready BEFORE camera/light culling, not in OnWillRenderObject.
            if (TessellationBounds != null) TessellationBounds.Prepare(camera);
            if (Shadows != null)
            {
                if (extensionEnabled && shadowReceiversEnabled && !shadowResources && Time.realtimeSinceStartup >= nextShadowResourceLookup)
                {
                    nextShadowResourceLookup = Time.realtimeSinceStartup + 1;
                    shadowResources = TomBSDFShadowResources.FindLoaded();
                }
                Shadows.SetResources(shadowResources);
                Shadows.PrepareCamera(camera, extensionEnabled && shadowReceiversEnabled);
            }
            if (Buffers == null) return;
            if (Glass != null) Glass.PrepareCamera(camera, Buffers, extensionEnabled && sceneBuffersEnabled);
            if (extensionEnabled && sceneBuffersEnabled && Buffers.NeedsDepth && !sceneBufferResources
                && Time.realtimeSinceStartup >= nextBufferResourceLookup)
            {
                nextBufferResourceLookup = Time.realtimeSinceStartup + 1;
                sceneBufferResources = TomBSDFSceneBufferResources.FindLoaded();
            }
            Buffers.SetResources(sceneBufferResources);
            Buffers.PrepareCamera(camera, extensionEnabled && sceneBuffersEnabled);
        }

        void BeginCamera(Camera camera)
        {
            if (Shadows != null) Shadows.BeginCamera(camera);
            if (Buffers != null) Buffers.BeginCamera(camera);
            // A camera destroyed by a failed external render must not leak its state.
            if (depth > 0 && !states[depth - 1].camera) { RestoreMaterialQuality(); depth = 0; }
            if (depth >= 8) { RestoreMaterialQuality(); depth = 0; Clear(); return; }
            if (states.Count <= depth) states.Add(new State());
            var state = states[depth++]; state.camera = camera;
            selection.Clear();
            if (extensionEnabled && camera.actualRenderingPath == RenderingPath.Forward
                && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
                for (int i = Lights.Count - 1; i >= 0; i--)
                {
                    if (!Lights[i]) { Lights.RemoveAt(i); continue; }
                    TomRectAreaLightControl.Settings value;
                    if (Lights[i].Read(camera, out value)) selection.Add(value);
                }
            selection.Sort(CompareLights);
            selectedLights = Mathf.Min(MaxLights, selection.Count);
            droppedLights = Mathf.Max(0, selection.Count - MaxLights);
            for (int i = 0; i < MaxLights; i++)
            {
                bool valid = i < selectedLights;
                state.center[i] = valid ? selection[i].center : Vector4.zero;
                state.right[i] = valid ? selection[i].right : Vector4.zero;
                state.up[i] = valid ? selection[i].up : Vector4.zero;
                state.radiance[i] = valid ? selection[i].radiance : Vector4.zero;
            }
            state.parameters = new Vector4(ProtocolVersion, selectedLights, extensionEnabled ? 1 : 0, 0);
            state.quality.Begin(camera, selectedLights > 0 && extensionEnabled,
                captureReferenceEnabled && TomBSDFCaptureQuality.IsReferenceCamera(camera));
            Publish(state);
        }
        static int CompareLights(TomRectAreaLightControl.Settings a, TomRectAreaLightControl.Settings b)
        {
            int priority = b.priority.CompareTo(a.priority);
            return priority != 0 ? priority : a.id.CompareTo(b.id);
        }
        void EndCamera(Camera camera)
        {
            if (TessellationBounds != null) TessellationBounds.EndCamera(camera);
            if (Shadows != null) Shadows.EndCamera(camera);
            if (Buffers != null) Buffers.EndCamera(camera);
            if (Glass != null) Glass.EndCamera(camera);
            if (depth == 0) { Clear(); return; }
            if (states[depth - 1].camera != camera) { RestoreMaterialQuality(); depth = 0; Clear(); return; }
            states[depth - 1].quality.Dispose();
            depth--;
            if (depth == 0) Clear(); else Publish(states[depth - 1]);
        }
        static void Publish(State state)
        {
            Shader.SetGlobalVectorArray(Centers, state.center);
            Shader.SetGlobalVectorArray(Rights, state.right);
            Shader.SetGlobalVectorArray(Ups, state.up);
            Shader.SetGlobalVectorArray(Radiances, state.radiance);
            Shader.SetGlobalVector(Params, state.parameters);
        }
    }
}

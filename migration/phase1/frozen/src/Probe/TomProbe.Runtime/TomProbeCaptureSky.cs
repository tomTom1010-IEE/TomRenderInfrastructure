using System;
using System.Collections.Generic;
using UnityEngine;

namespace TomProbeRuntime
{
    // Native reflection cameras are reused by Unity. Bind a per-job material instead
    // of inheriting a stale camera override or changing the main view's RenderSettings.
    public sealed class TomProbeCaptureSky : IDisposable
    {
        private readonly Material _material;
        private readonly Dictionary<Camera, CameraState> _cameras = new Dictionary<Camera, CameraState>();
        private ReflectionProbe _probe;
        private Camera _camera;
        private bool _disposed;
        public int CapturedFaces { get; private set; }
        public string LastCamera { get; private set; }
        public string SourceDescription
        { get { return _material == null ? "released" : _material.shader.name + " exposure=" + (_material.HasProperty("_Exposure") ? _material.GetFloat("_Exposure").ToString() : "n/a"); } }

        private sealed class CameraState
        {
            internal Skybox skybox;
            internal bool created, enabled;
            internal Material material;
            internal CameraClearFlags clearFlags;
            internal bool applied;
        }

        public TomProbeCaptureSky(TomProbeSettings settings, Shader backgroundShader)
        {
            Material source = settings.skybox ? RenderSettings.skybox : null;
            if (source != null)
            {
                if (source.shader == null || !source.shader.isSupported)
                    throw new InvalidOperationException("The active sky shader is missing or unsupported.");
                _material = new Material(source) { name = "Tom probe sky snapshot", hideFlags = HideFlags.HideAndDontSave };
            }
            else
            {
                if (backgroundShader == null) backgroundShader = Shader.Find("Hidden/Tom/ProbeBackground");
                if (backgroundShader == null || !backgroundShader.isSupported)
                    throw new InvalidOperationException("TomProbe HDR background shader is missing. Install the matching probe zipmod and DLLs.");
                Color c = settings.background;
                if (float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b)
                    || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b))
                    throw new InvalidOperationException("Probe background must contain finite linear radiance.");
                _material = new Material(backgroundShader) { name = "Tom probe linear HDR background", hideFlags = HideFlags.HideAndDontSave };
                _material.SetVector("_TomProbeBackground", new Vector4(c.r, c.g, c.b, 1));
            }
        }

        public void Bind(Camera camera)
        {
            if (_camera != null || _probe != null) throw new InvalidOperationException("Sky snapshot is already bound.");
            _camera = camera;
            Subscribe();
            Before(camera);
        }

        public void Bind(ReflectionProbe probe, ReflectionProbe completedSibling = null)
        {
            if (_camera != null || _probe != null) throw new InvalidOperationException("Sky snapshot is already bound.");
            // Unity exposes no native-probe owner on its reused per-face camera. Fail
            // closed for coincident realtime owners rather than modifying another job.
            foreach (var other in Resources.FindObjectsOfTypeAll<ReflectionProbe>())
                if (other != probe && other != completedSibling && other.enabled && other.gameObject.activeInHierarchy
                    && other.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime
                    && (other.transform.position - probe.transform.position).sqrMagnitude < 1e-8f)
                    throw new InvalidOperationException("Coincident realtime reflection probes make capture-camera ownership ambiguous. Disable or move the other probe before refreshing.");
            _probe = probe;
            Subscribe();
        }

        private void Subscribe()
        {
            Camera.onPreCull += Before;
            Camera.onPreRender += Before;
            Camera.onPostRender += After;
        }

        private bool Owns(Camera camera)
        {
            if (_disposed || camera == null) return false;
            if (_camera != null) return camera == _camera;
            return _probe != null && camera.cameraType == CameraType.Reflection
                && (camera.transform.position - _probe.transform.position).sqrMagnitude < 1e-8f
                && camera.targetTexture != null && camera.targetTexture.width == _probe.resolution;
        }

        private void Before(Camera camera)
        {
            if (_probe != null && camera != null && camera.cameraType == CameraType.Reflection)
                LastCamera = "position=" + camera.transform.position + " expected=" + _probe.transform.position
                    + " width=" + (camera.targetTexture == null ? 0 : camera.targetTexture.width) + " expected=" + _probe.resolution;
            if (!Owns(camera))
            {
                CameraState previous;
                if (camera != null && _cameras.TryGetValue(camera, out previous)) Restore(camera, previous);
                return;
            }
            CameraState state;
            if (!_cameras.TryGetValue(camera, out state))
            {
                var skybox = camera.GetComponent<Skybox>();
                state = new CameraState { skybox = skybox, created = skybox == null };
                if (state.created) state.skybox = camera.gameObject.AddComponent<Skybox>();
                _cameras.Add(camera, state);
            }
            if (!state.applied)
            {
                state.enabled = state.skybox.enabled;
                state.material = state.skybox.material;
                state.clearFlags = camera.clearFlags;
                state.applied = true;
            }
            state.skybox.material = _material;
            state.skybox.enabled = true;
            camera.clearFlags = CameraClearFlags.Skybox;
        }

        private void After(Camera camera)
        {
            CameraState counted;
            if (_cameras.TryGetValue(camera, out counted) && counted.applied) ++CapturedFaces;
            // Keep one lease for the native job, restoring it before unrelated
            // reuse of the pooled camera or when the capture is disposed.
        }

        private static void Restore(Camera camera, CameraState state)
        {
            if (!state.applied) return;
            if (state.skybox != null)
            {
                state.skybox.material = state.material;
                state.skybox.enabled = state.enabled;
            }
            if (camera != null) camera.clearFlags = state.clearFlags;
            state.applied = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Camera.onPreCull -= Before;
            Camera.onPreRender -= Before;
            Camera.onPostRender -= After;
            foreach (var pair in _cameras)
            {
                Restore(pair.Key, pair.Value);
                if (pair.Value.created && pair.Value.skybox != null) Release(pair.Value.skybox);
            }
            _cameras.Clear();
            Release(_material);
        }

        private static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TomProbeRuntime
{
    [DisallowMultipleComponent]
    public sealed class TomSHProbeVolume : MonoBehaviour
    {
        private const int CoefficientBlockCount = 7;
        public Vector3 localCenter = Vector3.zero, localSize = new Vector3(8, 4, 8);
        public Vector3Int gridResolution = new Vector3Int(4, 3, 4);
        public float edgeFade = .15f, intensity = 1;
        public int priority, captureResolution = 16, capturesPerFrame = 1;
        public LayerMask cullingMask = ~0;
        public float nearClip = .1f, farClip = 100;
        public CameraClearFlags clearFlags = CameraClearFlags.Skybox;
        public Color backgroundColor = Color.black;
        public bool useOcclusionCulling = true;
        public TomProbeUpdateMode updateMode = TomProbeUpdateMode.OnTransformChanged;
        public bool captureOnEnable = true;
        public float updateInterval = 30;
        public Texture3D AtlasTexture { get; private set; }
        public TomProbeSettings PublishedSettings { get; private set; }
        public int DataVersion { get; private set; }
        public bool IsReady { get { return AtlasTexture != null && PublishedSettings != null && _active; } }
        public bool IsCapturing { get { return _resources != null; } }
        private bool _active = true;
        private CaptureResources _resources;
        private TomProbeSettings _capture;
        private SphericalHarmonicsL2[] _samples;
        private int _sampleIndex;

        public TomProbeSettings LegacySettings()
        {
            return new TomProbeSettings { origin = transform.position + localCenter, size = localSize,
                grid = new Vector3Int(Mathf.Clamp(gridResolution.x, 2, 8), Mathf.Clamp(gridResolution.y, 2, 8), Mathf.Clamp(gridResolution.z, 2, 8)),
                edgeFade = edgeFade, intensity = intensity, priority = priority,
                captureResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(captureResolution), 8, 64),
                budget = Mathf.Clamp(capturesPerFrame, 1, 4), mask = cullingMask,
                nearClip = Mathf.Max(.01f, nearClip), farClip = Mathf.Max(nearClip + .01f, farClip),
                skybox = clearFlags == CameraClearFlags.Skybox, background = backgroundColor,
                occlusion = useOcclusionCulling, updateMode = updateMode, onEnable = captureOnEnable,
                interval = Mathf.Max(5, updateInterval) };
        }

        public void RequestCapture()
        {
            var control = GetComponent<TomProbeControl>();
            if (control != null) control.Request(Time.realtimeSinceStartup, true);
        }

        public void SetActive(bool active)
        {
            if (_active == active) return;
            _active = active;
            TomProbeManager.NotifyVolumeChanged(this);
        }

        public void ApplyDisplay(TomProbeSettings settings)
        {
            if (intensity == settings.intensity && edgeFade == settings.edgeFade && priority == settings.priority) return;
            intensity = settings.intensity; edgeFade = settings.edgeFade; priority = settings.priority;
            TomProbeManager.NotifyVolumeChanged(this);
        }

        public float GetInfluence(Vector3 worldPosition)
        {
            Vector3 uv;
            if (!TryGetNormalizedPosition(worldPosition, out uv)) return 0;
            float edge = Mathf.Min(Mathf.Min(Mathf.Min(uv.x, 1 - uv.x), Mathf.Min(uv.y, 1 - uv.y)), Mathf.Min(uv.z, 1 - uv.z));
            return edgeFade <= .0001f ? 1 : Mathf.SmoothStep(0, 1, edge / edgeFade);
        }

        public bool TryGetNormalizedPosition(Vector3 worldPosition, out Vector3 uv)
        {
            uv = Vector3.zero;
            if (!IsReady) return false;
            Vector3 min = PublishedSettings.origin - PublishedSettings.size * .5f;
            Vector3 p = worldPosition - min;
            uv = new Vector3(p.x / PublishedSettings.size.x, p.y / PublishedSettings.size.y, p.z / PublishedSettings.size.z);
            return uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1 && uv.z >= 0 && uv.z <= 1;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            TomProbeManager.Register(this);
            var control = GetComponent<TomProbeControl>();
            if (control == null) control = gameObject.AddComponent<TomProbeControl>();
            TomProbeCaptureScheduler.Register(control);
        }

        private void OnDisable()
        {
            var control = GetComponent<TomProbeControl>();
            if (control != null) TomProbeCaptureScheduler.Unregister(control);
            TomProbeManager.Unregister(this);
            CancelCapture();
            DestroyAtlas();
        }

        public void BeginCapture(TomProbeSettings snapshot)
        {
            CancelCapture();
            _capture = snapshot;
            _samples = new SphericalHarmonicsL2[snapshot.grid.x * snapshot.grid.y * snapshot.grid.z];
            _sampleIndex = 0;
            var control = GetComponent<TomProbeControl>();
            _resources = new CaptureResources(snapshot, name, control == null ? null : control.captureBackgroundShader);
        }

        public bool StepCapture(TomProbeSettings current, int globalBudget)
        {
            if (_resources == null) return true;
            if (!_capture.SameCapture(current, true)) { CancelCapture(); return true; }
            int end = Mathf.Min(_samples.Length, _sampleIndex + Mathf.Min(_capture.budget, Mathf.Clamp(globalBudget, 1, 4)));
            for (; _sampleIndex < end; _sampleIndex++)
                _samples[_sampleIndex] = CaptureSphericalHarmonics(_resources, _capture.SamplePosition(_sampleIndex), _capture);
            if (_sampleIndex < _samples.Length) return false;
            UploadAtlas(_samples, _capture);
            CancelCapture();
            return true;
        }

        public void CancelCapture()
        {
            if (_resources != null) _resources.Dispose();
            _resources = null;
            _samples = null;
            _capture = null;
        }

        private static SphericalHarmonicsL2 CaptureSphericalHarmonics(CaptureResources resources, Vector3 position, TomProbeSettings s)
        {
            resources.Camera.transform.position = position;
            resources.Camera.transform.rotation = Quaternion.identity;
            var sh = new SphericalHarmonicsL2();
            RenderTexture previous = RenderTexture.active;
            float oldShadowDistance = QualitySettings.shadowDistance;
            try
            {
                QualitySettings.shadowDistance = s.shadowDistance;
                // Only this synchronous camera render suppresses custom SH, never the main view.
                using (TomProbeManager.SuppressForCapture())
                    if (!resources.Camera.RenderToCubemap(resources.Cubemap))
                        throw new InvalidOperationException("Camera.RenderToCubemap failed for Tom SH volume.");
                for (int faceIndex = 0; faceIndex < 6; faceIndex++)
                {
                    var face = (CubemapFace)faceIndex;
                    Graphics.SetRenderTarget(resources.Cubemap, 0, face);
                    resources.Readback.ReadPixels(new Rect(0, 0, s.captureResolution, s.captureResolution), 0, 0, false);
                    resources.Readback.Apply(false, false);
                    AddFaceToSphericalHarmonics(ref sh, face, resources.Readback.GetPixels(), s.captureResolution);
                }
            }
            finally { RenderTexture.active = previous; QualitySettings.shadowDistance = oldShadowDistance; }
            return sh;
        }

        private static void AddFaceToSphericalHarmonics(
            ref SphericalHarmonicsL2 sh,
            CubemapFace face,
            Color[] pixels,
            int resolution)
        {
            float texelSize = 2f / resolution;

            for (int y = 0; y < resolution; y++)
            {
                float v = ((y + 0.5f) * texelSize) - 1f;
                for (int x = 0; x < resolution; x++)
                {
                    float u = ((x + 0.5f) * texelSize) - 1f;
                    Vector3 direction = CubemapDirection(face, u, v).normalized;
                    float solidAngle = texelSize * texelSize
                        / Mathf.Pow(1f + u * u + v * v, 1.5f);
                    Color color = pixels[y * resolution + x];
                    color.a = 1f;
                    sh.AddDirectionalLight(direction, color, solidAngle / Mathf.PI);
                }
            }
        }

        private static Vector3 CubemapDirection(CubemapFace face, float u, float v)
        {
            switch (face)
            {
                case CubemapFace.PositiveX:
                    return new Vector3(1f, -v, -u);
                case CubemapFace.NegativeX:
                    return new Vector3(-1f, -v, u);
                case CubemapFace.PositiveY:
                    return new Vector3(u, 1f, v);
                case CubemapFace.NegativeY:
                    return new Vector3(u, -1f, -v);
                case CubemapFace.PositiveZ:
                    return new Vector3(u, -v, 1f);
                case CubemapFace.NegativeZ:
                    return new Vector3(-u, -v, -1f);
                default:
                    return Vector3.forward;
            }
        }

        private void UploadAtlas(SphericalHarmonicsL2[] samples, TomProbeSettings s)
        {
            int width = s.grid.x * CoefficientBlockCount, height = s.grid.y, depth = s.grid.z;
            Color[] pixels = new Color[width * height * depth];
            for (int z = 0; z < depth; z++)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < s.grid.x; x++)
                    {
                        Vector4[] packed = PackUnitySH(samples[x + s.grid.x * (y + height * z)]);
                        for (int block = 0; block < CoefficientBlockCount; block++)
                        {
                            Vector4 v = packed[block];
                            pixels[x + block * s.grid.x + width * (y + height * z)] = new Color(v.x, v.y, v.z, v.w);
                        }
                    }
            var next = new Texture3D(width, height, depth, TextureFormat.RGBAHalf, false);
            try
            {
                next.name = name + "_TomSHAtlas";
                next.wrapMode = TextureWrapMode.Clamp;
                next.filterMode = FilterMode.Bilinear;
                next.SetPixels(pixels);
                next.Apply(false, true);
            }
            catch { Release(next); throw; }
            // Rebind receivers in this call: the manager's Update may have already run,
            // and Destroy retires the old GPU texture before the next camera render.
            var old = AtlasTexture;
            var oldSettings = PublishedSettings;
            int oldVersion = DataVersion;
            AtlasTexture = next;
            PublishedSettings = s;
            DataVersion++;
            try { TomProbeManager.RefreshNow(); }
            catch
            {
                AtlasTexture = old;
                PublishedSettings = oldSettings;
                DataVersion = oldVersion;
                try { TomProbeManager.RefreshNow(); }
                finally { Release(next); }
                throw;
            }
            if (old != null) Release(old);
        }

        private static Vector4[] PackUnitySH(SphericalHarmonicsL2 sh)
        {
            Vector4[] packed = new Vector4[CoefficientBlockCount];
            packed[0] = new Vector4(sh[0, 3], sh[0, 1], sh[0, 2], sh[0, 0] - sh[0, 6]);
            packed[1] = new Vector4(sh[1, 3], sh[1, 1], sh[1, 2], sh[1, 0] - sh[1, 6]);
            packed[2] = new Vector4(sh[2, 3], sh[2, 1], sh[2, 2], sh[2, 0] - sh[2, 6]);
            packed[3] = new Vector4(sh[0, 4], sh[0, 5], sh[0, 6] * 3f, sh[0, 7]);
            packed[4] = new Vector4(sh[1, 4], sh[1, 5], sh[1, 6] * 3f, sh[1, 7]);
            packed[5] = new Vector4(sh[2, 4], sh[2, 5], sh[2, 6] * 3f, sh[2, 7]);
            packed[6] = new Vector4(sh[0, 8], sh[1, 8], sh[2, 8], 1f);
            return packed;
        }

        private void DestroyAtlas()
        {
            if (AtlasTexture != null) Release(AtlasTexture);
            AtlasTexture = null;
            PublishedSettings = null;
        }

        private static void Release(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private sealed class CaptureResources : IDisposable
        {
            public Camera Camera;
            public RenderTexture Cubemap;
            public Texture2D Readback;
            private TomProbeCaptureSky _sky;
            public CaptureResources(TomProbeSettings s, string name, Shader backgroundShader)
            {
                try
                {
                    var go = new GameObject("Tom SH Capture Camera") { hideFlags = HideFlags.HideAndDontSave };
                    Camera = go.AddComponent<Camera>();
                    Camera.enabled = false;
                    Camera.renderingPath = RenderingPath.Forward;
                    Camera.allowHDR = true;
                    Camera.allowMSAA = false;
                    Camera.cullingMask = s.mask;
                    Camera.nearClipPlane = s.nearClip;
                    Camera.farClipPlane = s.farClip;
                    Camera.clearFlags = s.skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                    Camera.backgroundColor = s.background;
                    Camera.useOcclusionCulling = s.occlusion;
                    _sky = new TomProbeCaptureSky(s, backgroundShader);
                    _sky.Bind(Camera);
                    Cubemap = new RenderTexture(s.captureResolution, s.captureResolution, 24,
                        RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    Cubemap.name = name + "_TomSHCapture";
                    Cubemap.dimension = TextureDimension.Cube;
                    Cubemap.useMipMap = false;
                    Cubemap.autoGenerateMips = false;
                    if (!Cubemap.Create()) throw new InvalidOperationException("Could not allocate SH capture cubemap.");
                    Readback = new Texture2D(s.captureResolution, s.captureResolution, TextureFormat.RGBAHalf, false, true);
                    Readback.hideFlags = HideFlags.HideAndDontSave;
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (_sky != null) _sky.Dispose();
                if (Cubemap != null) { Cubemap.Release(); Release(Cubemap); }
                if (Readback != null) Release(Readback);
                if (Camera != null) Release(Camera.gameObject);
            }
        }
    }
}

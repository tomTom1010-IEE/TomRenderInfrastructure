using UnityEngine;
using UnityEngine.Rendering;

namespace TomProbeRuntime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ReflectionProbe))]
    public sealed class TomReflectionProbeController : MonoBehaviour
    {
        // Retain 0.1 prefab fields as fallback for items without a control material.
        public ReflectionProbe probe;
        public TomProbeUpdateMode updateMode = TomProbeUpdateMode.OnTransformChanged;
        public bool renderOnEnable = true;
        public float updateInterval = 5;
        public int resolution = 512;
        public bool hdr = true, boxProjection = true;
        public float intensity = 1, blendDistance = 1, nearClip = .1f, farClip = 100;
        public Vector3 localCenter = Vector3.zero, localSize = new Vector3(10, 6, 10);
        public LayerMask cullingMask = ~0;
        public ReflectionProbeClearFlags clearFlags = ReflectionProbeClearFlags.Skybox;
        public Color backgroundColor = Color.black;
        public ReflectionProbeTimeSlicingMode timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
        private ReflectionProbe _runtimeProbe;
        private ReflectionProbe _captureProbe;
        private TomProbeSettings _captureSettings;
        private int _renderId = -1;
        private TomProbeCaptureSky _captureSky;
        public ReflectionProbe RuntimeProbe { get { return _runtimeProbe; } }
        public string LastSkyCapture { get; private set; }

        public TomProbeSettings LegacySettings()
        {
            return new TomProbeSettings { origin = transform.position + localCenter, size = localSize,
                resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(resolution, 16, 2048)), hdr = hdr,
                boxProjection = boxProjection, intensity = intensity, blendDistance = blendDistance,
                nearClip = Mathf.Max(.01f, nearClip), farClip = Mathf.Max(nearClip + .01f, farClip),
                mask = cullingMask, skybox = clearFlags == ReflectionProbeClearFlags.Skybox,
                background = backgroundColor, timeSlicing = timeSlicingMode, interval = Mathf.Max(.5f, updateInterval),
                updateMode = updateMode, onEnable = renderOnEnable };
        }

        public void ApplySettings()
        {
            if (probe == null) probe = GetComponent<ReflectionProbe>();
            Apply(probe, LegacySettings(), false);
        }

        private static void Apply(ReflectionProbe target, TomProbeSettings s, bool runtime)
        {
            if (target.refreshMode != ReflectionProbeRefreshMode.ViaScripting) target.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            if (target.mode != ReflectionProbeMode.Realtime) target.mode = ReflectionProbeMode.Realtime;
            if (target.timeSlicingMode != s.timeSlicing) target.timeSlicingMode = s.timeSlicing;
            if (target.resolution != s.resolution) target.resolution = s.resolution;
            if (target.hdr != s.hdr) target.hdr = s.hdr;
            if (target.nearClipPlane != s.nearClip) target.nearClipPlane = s.nearClip;
            if (target.farClipPlane != s.farClip) target.farClipPlane = s.farClip;
            if (target.cullingMask != s.mask) target.cullingMask = s.mask;
            var flags = s.skybox ? ReflectionProbeClearFlags.Skybox : ReflectionProbeClearFlags.SolidColor;
            if (target.clearFlags != flags) target.clearFlags = flags;
            if (target.backgroundColor != s.background) target.backgroundColor = s.background;
            if (target.shadowDistance != s.shadowDistance) target.shadowDistance = s.shadowDistance;
            if (runtime) { target.transform.position = s.origin; target.transform.rotation = Quaternion.identity; }
            target.center = runtime ? Vector3.zero : s.origin - target.transform.position;
            Display(target, s);
        }

        private static void Display(ReflectionProbe target, TomProbeSettings s)
        {
            if (target.size != s.size) target.size = s.size;
            if (target.boxProjection != s.boxProjection) target.boxProjection = s.boxProjection;
            if (target.intensity != s.intensity) target.intensity = s.intensity;
            if (target.blendDistance != s.blendDistance) target.blendDistance = s.blendDistance;
            if (target.importance != s.priority) target.importance = s.priority;
        }

        public void ApplyDisplay(TomProbeSettings s)
        {
            // Freeze the detached probe's world position throughout a time-sliced capture.
            // The Studio item remains freely editable without rotating the environment.
            if (_runtimeProbe != null) Display(_runtimeProbe, s);
        }

        public void SetActive(bool active)
        { if (_runtimeProbe != null && _runtimeProbe.enabled != active) _runtimeProbe.enabled = active; }

        public void BeginCapture(TomProbeSettings s)
        {
            if (!QualitySettings.realtimeReflectionProbes)
                throw new System.InvalidOperationException("Realtime Reflection Probes are disabled in the game's quality settings. Enable them and request a refresh.");
            if (probe == null) probe = GetComponent<ReflectionProbe>();
            probe.enabled = false;
            CancelCapture();
            var go = new GameObject(name + " [Tom reflection capture]") { hideFlags = HideFlags.HideAndDontSave };
            _captureProbe = go.AddComponent<ReflectionProbe>();
            _captureProbe.enabled = false;
            Apply(_captureProbe, s, true);
            // Native RenderProbe does not progress on a disabled component. Keep
            // staging influence far outside receivers while rendering at s.origin.
            _captureProbe.center = Vector3.one * 1000000f;
            _captureProbe.size = Vector3.one;
            _captureProbe.intensity = 0;
            _captureProbe.enabled = true;
            _captureSettings = s;
            var control = GetComponent<TomProbeControl>();
            _captureSky = new TomProbeCaptureSky(s, control == null ? null : control.captureBackgroundShader);
            _captureSky.Bind(_captureProbe, _runtimeProbe);
            _renderId = _captureProbe.RenderProbe();
            if (_renderId < 0) throw new System.InvalidOperationException("ReflectionProbe.RenderProbe failed.");
        }

        public bool StepCapture()
        {
            if (_captureProbe == null || _renderId < 0) return true;
            if (!_captureProbe.IsFinishedRendering(_renderId)) return false;
            _renderId = -1;
            if (_captureSky != null) LastSkyCapture = _captureSky.SourceDescription + " faces=" + _captureSky.CapturedFaces + " " + _captureSky.LastCamera;
            if (_captureSky == null || _captureSky.CapturedFaces != 6)
                throw new System.InvalidOperationException("The owned reflection camera was not bound for all six faces. " + LastSkyCapture);
            if (_captureSky != null) _captureSky.Dispose();
            _captureSky = null;
            var previous = _runtimeProbe;
            _runtimeProbe = _captureProbe;
            _captureProbe = null;
            _runtimeProbe.center = Vector3.zero;
            Display(_runtimeProbe, _captureSettings);
            _runtimeProbe.enabled = true;
            _captureSettings = null;
            ReleaseProbe(previous);
            return true;
        }

        public void CancelCapture()
        {
            _renderId = -1;
            if (_captureSky != null) _captureSky.Dispose();
            _captureSky = null;
            ReleaseProbe(_captureProbe);
            _captureProbe = null;
            _captureSettings = null;
        }

        private static void ReleaseProbe(ReflectionProbe target)
        {
            if (target == null) return;
            target.enabled = false;
            if (Application.isPlaying) Destroy(target.gameObject);
            else DestroyImmediate(target.gameObject);
        }

        public void RenderNow()
        {
            var control = GetComponent<TomProbeControl>();
            if (control != null) control.Request(Time.realtimeSinceStartup, true);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (probe == null) probe = GetComponent<ReflectionProbe>();
            probe.enabled = false;
            var control = GetComponent<TomProbeControl>();
            if (control == null) control = gameObject.AddComponent<TomProbeControl>();
            TomProbeCaptureScheduler.Register(control);
        }

        private void OnDisable()
        {
            var control = GetComponent<TomProbeControl>();
            if (control != null) TomProbeCaptureScheduler.Unregister(control);
            CancelCapture();
            ReleaseProbe(_runtimeProbe);
            _runtimeProbe = null;
        }

        private void OnValidate() { if (!Application.isPlaying) ApplySettings(); }
    }
}

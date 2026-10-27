using System;
using UnityEngine;

namespace TomProbeRuntime
{
    [DisallowMultipleComponent]
    public sealed class TomProbeControl : MonoBehaviour
    {
        public Renderer controlRenderer;
        public Shader captureBackgroundShader;
        public TomProbeSettings Settings { get; private set; }
        public bool IsSH { get { return _sh != null; } }
        public int CompletedCaptures { get; private set; }
        public string Status { get; internal set; }
        public float ReadyAt { get; private set; }
        internal bool Pending;
        internal int Batch;
        private TomSHProbeVolume _sh;
        private TomReflectionProbeController _reflection;
        private Material _ownedMaterial, _boundMaterial;
        private float _pollAt, _intervalAt;
        private bool _initialized;
        private bool _sceneVisible = true;
        private int _startedVersion;
        private TomProbeSettings _requestedCapture;
        private int _skyRevision;
        private const float SettleSeconds = .4f;

        public void SetSceneVisible(bool visible)
        {
            if (_sceneVisible == visible) return;
            _sceneVisible = visible;
            _pollAt = 0;
            if (isActiveAndEnabled) Poll(Time.realtimeSinceStartup);
        }

        private void OnEnable()
        {
            _initialized = false;
            Settings = null;
            _requestedCapture = null;
            Pending = false;
            _pollAt = 0;
            Status = "Waiting for material restore";
            if (Application.isPlaying) TomProbeCaptureScheduler.Register(this);
        }

        private void OnDisable()
        {
            Pending = false;
            TomProbeCaptureScheduler.Unregister(this);
        }

        private void OnDestroy()
        {
            if (_ownedMaterial != null)
            {
                if (Application.isPlaying) Destroy(_ownedMaterial);
                else DestroyImmediate(_ownedMaterial);
            }
        }

        public void Poll(float now)
        {
            if (now < _pollAt) return;
            _pollAt = now + .1f;
            if (_sh == null) _sh = GetComponent<TomSHProbeVolume>();
            if (_reflection == null) _reflection = GetComponent<TomReflectionProbeController>();
            if (_sh == null && _reflection == null) return;
            if (_sh != null && !_sh.enabled || _reflection != null && !_reflection.enabled) return;
            Material material = null;
            if (controlRenderer != null)
            {
                material = controlRenderer.sharedMaterial;
                string expected = IsSH ? "tom/SHVolumeControl" : "tom/ReflectionProbeControl";
                if (material == null || material.shader == null || material.shader.name != expected)
                {
                    Pending = false;
                    Status = "Control material missing or shader changed";
                    if (_reflection != null) _reflection.SetActive(false);
                    if (_sh != null) _sh.SetActive(false);
                    if (Settings != null) Settings.enabled = false;
                    return;
                }
                if (_boundMaterial == null || material != _boundMaterial)
                {
                    // Clone once per binding, including Studio duplication and ME material replacement.
                    var previous = _ownedMaterial;
                    _ownedMaterial = new Material(material) { name = material.name };
                    controlRenderer.sharedMaterial = _ownedMaterial;
                    material = _ownedMaterial;
                    if (previous != null)
                    {
                        if (Application.isPlaying) Destroy(previous);
                        else DestroyImmediate(previous);
                    }
                }
            }
            bool rebound = material != _boundMaterial;
            _boundMaterial = material;
            var next = IsSH ? _sh.LegacySettings() : _reflection.LegacySettings();
            next.ReadControl(material, transform.position, IsSH);
            next.enabled &= _sceneVisible;
            bool changed = _requestedCapture != null && !next.SameCapture(_requestedCapture, IsSH);
            bool configurationChanged = Settings != null && !next.SameCapture(Settings, IsSH, false);
            bool enabledNow = next.enabled && (Settings == null || !Settings.enabled);
            bool updateModeChanged = Settings == null || Settings.updateMode != next.updateMode || Settings.interval != next.interval;
            bool skyChanged = _initialized && next.skybox && _skyRevision != TomProbeSkyState.Revision;
            _skyRevision = TomProbeSkyState.Revision;
            bool trigger = _initialized && !rebound && next.refresh != Settings.refresh;
            Settings = next;
            if (_reflection != null) _reflection.SetActive(next.enabled);
            if (_sh != null) _sh.SetActive(next.enabled);
            if (!next.enabled) { Pending = false; Status = "Disabled"; }
            else
            {
                // Manual mode defers position-only updates. Capture-setting edits still
                // invalidate the result; late ME restores must replace any default capture.
                if (!_initialized || enabledNow)
                {
                    if (next.onEnable) Request(now, true);
                }
                else if (rebound && (next.onEnable || changed)) Request(now, true);
                else if (changed && (configurationChanged || Pending || TomProbeCaptureScheduler.IsActive(this)
                    || next.updateMode == TomProbeUpdateMode.OnTransformChanged)) Request(now, true);
                if (skyChanged && (next.onEnable || CompletedCaptures > 0 || Pending || TomProbeCaptureScheduler.IsActive(this)))
                    Request(now, true);
                if (trigger) TomProbeCaptureScheduler.Request(this, next.scope, now);
                if (!updateModeChanged && next.updateMode == TomProbeUpdateMode.Interval && now >= _intervalAt)
                {
                    Request(now, false);
                    _intervalAt = now + next.interval;
                }
                if (_reflection != null && !TomProbeCaptureScheduler.IsActive(this)) _reflection.ApplyDisplay(next);
                if (_sh != null) _sh.ApplyDisplay(next);
            }
            if (updateModeChanged || enabledNow) _intervalAt = now + next.interval;
            if (_requestedCapture == null) _requestedCapture = next;
            _initialized = true;
        }

        internal void Request(float now, bool settle)
        {
            if (Settings == null || !Settings.enabled) return;
            _requestedCapture = Settings;
            bool wasPending = Pending;
            Pending = true;
            if (settle || !wasPending) ReadyAt = now + (settle ? SettleSeconds : 0);
            Status = "Queued";
        }

        internal bool StartCapture()
        {
            if (Settings == null || !Settings.enabled) return false;
            Pending = false;
            Status = "Capturing";
            if (IsSH) _sh.BeginCapture(Settings);
            else _reflection.BeginCapture(Settings);
            _startedVersion = IsSH ? _sh.DataVersion : 0;
            return true;
        }

        internal bool StepCapture()
        {
            if (Settings == null || !Settings.enabled || !isActiveAndEnabled)
            { CancelCapture(); return true; }
            bool done = IsSH ? _sh.StepCapture(Settings, TomProbeCaptureScheduler.SamplesPerFrame)
                : _reflection.StepCapture();
            if (!done) return false;
            if (!IsSH || _sh.DataVersion != _startedVersion) CompletedCaptures++;
            Status = Pending ? "Queued" : "Ready";
            return true;
        }

        internal void CancelCapture()
        {
            if (_sh != null) _sh.CancelCapture();
            if (_reflection != null) _reflection.CancelCapture();
        }
    }
}

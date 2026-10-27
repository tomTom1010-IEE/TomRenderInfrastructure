using System;
using System.Collections.Generic;
using UnityEngine;

namespace TomProbeRuntime
{
    public sealed class TomProbeCaptureScheduler : MonoBehaviour
    {
        private static TomProbeCaptureScheduler _instance;
        private static readonly List<TomProbeControl> Controls = new List<TomProbeControl>();
        private static TomProbeControl _active;
        public static int SamplesPerFrame = 1;
        private static float _startedAt;
        private static int _batch;
        public static int RegisteredCount { get { return Controls.Count; } }

        public static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("Tom Probe Capture Scheduler") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying) DontDestroyOnLoad(go);
            _instance = go.AddComponent<TomProbeCaptureScheduler>();
        }

        public static void Register(TomProbeControl control)
        {
            EnsureInstance();
            if (!Controls.Contains(control)) Controls.Add(control);
        }

        public static void Unregister(TomProbeControl control)
        {
            Controls.Remove(control);
            if (_active == control) { control.CancelCapture(); _active = null; }
        }

        public static bool IsActive(TomProbeControl control) { return _active == control; }

        public static void Request(TomProbeControl sender, int scope, float now)
        {
            if (sender.Settings == null) return;
            int batch = ++_batch;
            foreach (var control in Controls)
                if (control != null && control.Settings != null && (scope == 2 || control == sender
                    || scope == 1 && control.Settings.group == sender.Settings.group))
                { control.Batch = batch; control.Request(now, true); }
        }

        private void Update() { if (Application.isPlaying) Tick(Time.realtimeSinceStartup); }

        public static void Tick(float now)
        {
            TomProbeSkyState.Poll(now);
            Controls.RemoveAll(c => c == null);
            foreach (var control in Controls) if (control.isActiveAndEnabled) control.Poll(now);
            if (_active != null)
            {
                try
                {
                    if (now - _startedAt > 120) throw new TimeoutException("Probe capture timed out.");
                    if (!_active.StepCapture()) return;
                }
                catch (Exception ex)
                {
                    _active.CancelCapture();
                    _active.Status = "Failed: " + ex.Message;
                    Debug.LogError("[TomProbe] " + _active.name + ": " + ex);
                }
                _active = null;
                return;
            }
            // Oldest ready requests first; SH precedes reflection for a shared refresh batch.
            TomProbeControl best = null;
            foreach (var control in Controls)
            {
                if (!control.isActiveAndEnabled || !control.Pending || control.ReadyAt > now) continue;
                if (!control.IsSH && control.Batch != 0 && Controls.Exists(c => c != null && c.IsSH
                    && c.isActiveAndEnabled && c.Pending && c.Batch == control.Batch)) continue;
                if (best == null || control.ReadyAt < best.ReadyAt - .01f
                    || Mathf.Abs(control.ReadyAt - best.ReadyAt) <= .01f && control.IsSH && !best.IsSH) best = control;
            }
            if (best == null) return;
            try
            {
                if (best.StartCapture()) { _active = best; _startedAt = now; }
            }
            catch (Exception ex)
            {
                best.Pending = false;
                best.CancelCapture();
                best.Status = "Failed: " + ex.Message;
                Debug.LogError("[TomProbe] " + best.name + ": " + ex);
            }
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            if (_active != null) _active.CancelCapture();
            _active = null;
            Controls.Clear();
            _instance = null;
        }
    }
}

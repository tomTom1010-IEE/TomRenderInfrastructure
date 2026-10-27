using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TomProbeRuntime
{
    // One immutable-by-convention snapshot per queued capture. No live material reads in a job.
    public sealed class TomProbeSettings
    {
        public bool enabled = true, hdr = true, boxProjection = true, onEnable = true;
        public float intensity = 1, edgeFade = .15f, blendDistance = 1, nearClip = .1f, farClip = 100;
        public float interval = 30, shadowDistance = 50;
        public int priority, group, scope, refresh, resolution = 512, captureResolution = 16, mask = -1;
        public int budget = 1;
        public TomProbeUpdateMode updateMode = TomProbeUpdateMode.OnTransformChanged;
        public ReflectionProbeTimeSlicingMode timeSlicing = ReflectionProbeTimeSlicingMode.IndividualFaces;
        public Vector3 origin, size = new Vector3(8, 4, 8);
        public Vector3Int grid = new Vector3Int(4, 3, 4);
        public bool skybox = true, occlusion = true;
        public Color background = Color.black;

        public static float Read(Material material, string key, float fallback, float min, float max)
        {
            float value = material != null && material.HasProperty(key) ? material.GetFloat(key) : fallback;
            if (float.IsNaN(value) || float.IsInfinity(value)) value = fallback;
            return Mathf.Clamp(value, min, max);
        }

        public static int Integer(Material m, string key, int fallback, int min, int max)
        { return Mathf.RoundToInt(Read(m, key, fallback, min, max)); }

        public void ReadControl(Material m, Vector3 position, bool sh)
        {
            if (m == null) return;
            enabled = Read(m, "_ProbeEnabled", 1, 0, 1) >= .5f;
            intensity = Read(m, "_ProbeIntensity", intensity, 0, 8);
            priority = Integer(m, "_ProbePriority", priority, 0, 100);
            group = Integer(m, "_ProbeGroup", 0, 0, 999);
            scope = Integer(m, "_ProbeRefreshScope", 0, 0, 2);
            refresh = Integer(m, "_ProbeRefresh", 0, 0, 1);
            updateMode = (TomProbeUpdateMode)Integer(m, "_ProbeUpdateMode", (int)updateMode, 0, 3);
            onEnable = Read(m, "_ProbeCaptureOnLoad", 1, 0, 1) >= .5f;
            interval = Read(m, "_ProbeInterval", interval, sh ? 5 : .5f, 3600);
            nearClip = Read(m, "_ProbeNearClip", nearClip, .01f, 100);
            farClip = Mathf.Max(nearClip + .01f, Read(m, "_ProbeFarClip", farClip, .02f, 2000));
            shadowDistance = Read(m, "_ProbeShadowDistance", shadowDistance, 0, 500);
            origin = position + new Vector3(Read(m, "_ProbeOffsetX", 0, -1000, 1000),
                Read(m, "_ProbeOffsetY", 0, -1000, 1000), Read(m, "_ProbeOffsetZ", 0, -1000, 1000));
            size = new Vector3(Read(m, "_ProbeSizeX", size.x, .01f, 1000),
                Read(m, "_ProbeSizeY", size.y, .01f, 1000), Read(m, "_ProbeSizeZ", size.z, .01f, 1000));
            skybox = Read(m, "_ProbeSkybox", 1, 0, 1) >= .5f;
            if (m.HasProperty("_ProbeBackground")) background = m.GetColor("_ProbeBackground");
            occlusion = Read(m, "_ProbeOcclusionCulling", 1, 0, 1) >= .5f;
            int preset = Integer(m, "_ProbeCapturePreset", 0, 0, 2);
            if (preset == 2)
                mask = unchecked((int)((uint)Integer(m, "_ProbeMaskLow", 65535, 0, 65535)
                    | ((uint)Integer(m, "_ProbeMaskHigh", 65535, 0, 65535) << 16)));
            else
            {
                mask = ~LayerMask.GetMask("UI", "PowerUI", "Config", "Studio/Col", "Studio/Select", "Studio/Camera", "Studio/Route");
                if (preset == 0) mask &= ~LayerMask.GetMask("Chara", "Action/Chara");
            }
            if (sh)
            {
                grid = new Vector3Int(Integer(m, "_SHGridX", grid.x, 2, 8),
                    Integer(m, "_SHGridY", grid.y, 2, 8), Integer(m, "_SHGridZ", grid.z, 2, 8));
                captureResolution = 8 << Integer(m, "_SHCaptureQuality", 1, 0, 3);
                edgeFade = Read(m, "_SHEdgeFade", edgeFade, 0, .49f);
                budget = Integer(m, "_SHSamplesPerFrame", 1, 1, 4);
            }
            else
            {
                resolution = 256 << Integer(m, "_ProbeQuality", 1, 0, 2);
                hdr = Read(m, "_ProbeHDR", 1, 0, 1) >= .5f;
                boxProjection = Read(m, "_ProbeBoxProjection", 1, 0, 1) >= .5f;
                blendDistance = Read(m, "_ProbeBlendDistance", blendDistance, 0, 100);
                timeSlicing = (ReflectionProbeTimeSlicingMode)Integer(m, "_ProbeTimeSlicing", 1, 0, 2);
            }
        }

        public bool SameCapture(TomProbeSettings b, bool sh, bool includePosition = true)
        {
            return b != null && (!includePosition || (origin - b.origin).sqrMagnitude < .000001f)
                && nearClip == b.nearClip && farClip == b.farClip && mask == b.mask
                && skybox == b.skybox && background == b.background && occlusion == b.occlusion && shadowDistance == b.shadowDistance
                && (sh ? (grid == b.grid && size == b.size && captureResolution == b.captureResolution)
                    : (resolution == b.resolution && hdr == b.hdr && shadowDistance == b.shadowDistance));
        }

        public Vector3 SamplePosition(int index)
        {
            int x = index % grid.x, y = index / grid.x % grid.y, z = index / (grid.x * grid.y);
            return origin + Vector3.Scale(new Vector3((float)x / (grid.x - 1),
                (float)y / (grid.y - 1), (float)z / (grid.z - 1)) - Vector3.one * .5f, size);
        }
    }
}

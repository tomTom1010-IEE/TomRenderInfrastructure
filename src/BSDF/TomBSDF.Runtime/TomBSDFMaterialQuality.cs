using System;
using System.Collections.Generic;
using TomRenderInfrastructure;
using UnityEngine;

namespace TomBSDFRuntime
{
    public enum TomBSDFAreaPath { Off, Realtime, Reference }

    // ME floats remain authoritative. Keywords are temporary draw state, never
    // saved material settings, so shader reassignment/copy/load need no ME patch.
    public sealed class TomBSDFMaterialQualityScope : IDisposable
    {
        public const string RealtimeKeyword = "TOM_BSDF_AREA_REALTIME";
        public const string ReferenceKeyword = "TOM_BSDF_AREA_REFERENCE";
        struct Saved
        {
            public Material material;
            public Shader shader;
            public bool realtime, reference;
        }
        readonly List<Saved> saved = new List<Saved>();
        readonly HashSet<Material> seen = new HashSet<Material>();
        readonly List<Material> materials = new List<Material>();
        RendererInterest targets;
        readonly bool ownsTargets;
        public int MaterialCount { get { return saved.Count; } }

        public TomBSDFMaterialQualityScope()
        { targets = SceneDiscovery.WatchShaders("BSDF area quality", IsFamily); ownsTargets = true; }
        internal TomBSDFMaterialQualityScope(RendererInterest targets) { this.targets = targets; }

        internal static bool IsFamily(Material material)
        {
            if (!material) return false;
            string name = SceneDiscovery.ShaderName(material.shader);
            return name == "tom/BSDF" || name == "tom/BSDFCutout"
                || name == "tom/BSDFAlpha" || name == "tom/BSDFAlphaBackFront" || name == "tom/BSDFGlass"
                || name == "tom/BSDFTess" || name == "tom/BSDFCutoutTess";
        }

        public static bool Supports(Material material)
        {
            return IsFamily(material) && material.GetTag("TomBSDFAreaVariants", false, "") == "1";
        }

        public static TomBSDFAreaPath Resolve(Material material, bool areaAvailable, bool captureReference)
        {
            if (!areaAvailable || material.GetFloat("_SceneLighting") <= .5f
                || material.GetFloat("_AreaLightIntensity") <= 0) return TomBSDFAreaPath.Off;
            return captureReference || material.GetFloat("_AreaLightQuality") > .5f
                ? TomBSDFAreaPath.Reference : TomBSDFAreaPath.Realtime;
        }

        public void Apply(Material material, bool areaAvailable, bool captureReference)
        {
            if (!material || !seen.Add(material) || !Supports(material)) return;
            var value = new Saved { material = material, shader = material.shader,
                realtime = material.IsKeywordEnabled(RealtimeKeyword), reference = material.IsKeywordEnabled(ReferenceKeyword) };
            var path = Resolve(material, areaAvailable, captureReference);
            if (value.realtime == (path == TomBSDFAreaPath.Realtime)
                && value.reference == (path == TomBSDFAreaPath.Reference)) return;
            saved.Add(value); Set(material, path);
        }

        public void Begin(Camera camera, bool areaAvailable, bool captureReference)
        {
            Restore();
            if (targets == null && ownsTargets) targets = SceneDiscovery.WatchShaders("BSDF area quality", IsFamily);
            if (targets == null || !targets.Affects(camera)) return;
            // Membership is cached; shared materials, tags and floats stay live.
            for (int index = 0; index < targets.Count; index++)
            {
                var renderer = targets[index];
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                materials.Clear(); renderer.GetSharedMaterials(materials);
                foreach (var material in materials) Apply(material, areaAvailable, captureReference);
            }
            materials.Clear();
        }

        static void Set(Material material, TomBSDFAreaPath path)
        {
            Keyword(material, RealtimeKeyword, path == TomBSDFAreaPath.Realtime);
            Keyword(material, ReferenceKeyword, path == TomBSDFAreaPath.Reference);
        }
        static void Keyword(Material material, string keyword, bool enabled)
        {
            if (material.IsKeywordEnabled(keyword) == enabled) return;
            if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
        }

        public void Dispose()
        {
            Restore();
            if (ownsTargets && targets != null) { targets.Dispose(); targets = null; }
        }
        void Restore()
        {
            for (int i = saved.Count - 1; i >= 0; i--)
            {
                var value = saved[i];
                if (!value.material || value.material.shader != value.shader) continue;
                // Do not assign shaderKeywords: preserve other plugins' edits.
                Keyword(value.material, RealtimeKeyword, value.realtime);
                Keyword(value.material, ReferenceKeyword, value.reference);
            }
            saved.Clear(); seen.Clear(); materials.Clear();
        }
    }

    public static class TomBSDFCaptureQuality
    {
        sealed class Lease : IDisposable
        {
            public Camera camera;
            public RenderTexture originalTarget;
            public bool requireTargetChange;
            public void Dispose() { leases.Remove(this); camera = null; }
        }
        static readonly List<Lease> leases = new List<Lease>();
        public static int ActiveCount { get { return leases.Count; } }

        // Screenshot Manager often reuses Camera.main. A lifecycle AND target
        // change identify the capture, not a scene name, resolution or camera name.
        public static IDisposable Begin(Camera camera, bool requireTargetChange = true)
        {
            var lease = new Lease { camera = camera, originalTarget = camera ? camera.targetTexture : null,
                requireTargetChange = requireTargetChange };
            if (camera) leases.Add(lease);
            return lease;
        }

        public static bool IsReferenceCamera(Camera camera)
        {
            if (!camera || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview) return false;
            for (int i = leases.Count - 1; i >= 0; i--)
            {
                var lease = leases[i];
                if (!lease.camera) { leases.RemoveAt(i); continue; }
                if (lease.camera == camera && (!lease.requireTargetChange
                    || (camera.targetTexture && camera.targetTexture != lease.originalTarget))) return true;
            }
            return false;
        }
    }
}

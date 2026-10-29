using System;
using System.Collections.Generic;
using TomRenderInfrastructure;
using UnityEngine;

namespace TomBSDFRuntime
{
    // ME materials need no component or saved keyword. Only visible glass that
    // opts into refraction leases the existing per-camera color/depth service.
    public sealed class TomBSDFGlassConsumers : IDisposable
    {
        sealed class Entry
        {
            public Camera camera;
            public TomSceneBufferLease lease;
            public TomSceneBufferResolution resolution;
            public int sequence;
        }
        readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        readonly List<Material> materials = new List<Material>();
        readonly List<Camera> active = new List<Camera>();
        readonly List<int> dead = new List<int>();
        readonly Plane[] planes = new Plane[6];
        readonly RendererInterest targets = SceneDiscovery.WatchShaders("BSDF refractive glass", IsGlass);
        int sequence;
        static bool IsGlass(Material material)
        { return material && SceneDiscovery.ShaderName(material.shader) == "tom/BSDFGlass"; }
        public int CameraCount { get { return entries.Count; } }
        internal bool OwnsCamera(Camera camera) { return active.Count > 0 && active[active.Count - 1] == camera; }

        public void PrepareCamera(Camera camera, TomBSDFSceneBuffers buffers, bool enabled)
        {
            if (!camera || buffers == null) return;
            dead.Clear(); foreach (var pair in entries) if (!pair.Value.camera) dead.Add(pair.Key);
            foreach (int id in dead) Drop(id);
            if (!enabled && active.Count == 0) { foreach (var e in entries.Values) if (e.lease != null) e.lease.Dispose(); entries.Clear(); }
            bool demand = false;
            var resolution = TomSceneBufferResolution.Half;
            if (enabled && !CameraRoles.IsAuxiliaryCapture(camera) && camera.cameraType != CameraType.Reflection && camera.cameraType != CameraType.Preview && !camera.stereoEnabled && targets.Affects(camera))
            {
                GeometryUtility.CalculateFrustumPlanes(camera, planes);
                for (int index = 0; index < targets.Count; index++)
                {
                    var renderer = targets[index];
                    if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                        || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                        || !GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;
                    materials.Clear(); renderer.GetSharedMaterials(materials);
                    foreach (var m in materials)
                    {
                        if (!IsGlass(m)
                            || m.GetFloat("_UseSceneRefraction") < .5f || m.GetFloat("_Coverage") <= 0) continue;
                        demand = true;
                        if (m.GetFloat("_GlassBufferResolution") < 1.5f) resolution = TomSceneBufferResolution.Full;
                    }
                }
            }
            int key = camera.GetInstanceID(); Entry entry;
            if (!demand) Drop(key);
            else
            {
                if (!entries.TryGetValue(key, out entry))
                {
                    if (entries.Count >= 8)
                    {
                        Entry oldest = null;
                        foreach (var value in entries.Values)
                            if (!active.Contains(value.camera) && (oldest == null || value.sequence < oldest.sequence)) oldest = value;
                        if (oldest != null) Drop(oldest.camera.GetInstanceID());
                    }
                    if (entries.Count < 8) entries.Add(key, entry = new Entry { camera = camera });
                }
                if (entry != null)
                {
                    if (entry.lease == null || !entry.lease.IsActive || entry.resolution != resolution)
                    {
                        if (entry.lease != null) entry.lease.Dispose();
                        entry.lease = buffers.Acquire(camera, TomSceneBufferFlags.Color | TomSceneBufferFlags.Depth, resolution);
                        entry.resolution = resolution;
                    }
                    entry.sequence = ++sequence;
                }
            }
            active.Add(camera);
        }
        void Drop(int id)
        {
            Entry entry; if (!entries.TryGetValue(id, out entry)) return;
            if (entry.lease != null) entry.lease.Dispose(); entries.Remove(id);
        }
        public void EndCamera(Camera camera)
        {
            if (active.Count == 0) return;
            if (active[active.Count - 1] != camera) { active.Clear(); return; }
            active.RemoveAt(active.Count - 1);
        }
        public void Abort() { active.Clear(); }
        public void Dispose()
        {
            foreach (var entry in entries.Values) if (entry.lease != null) entry.lease.Dispose();
            entries.Clear(); active.Clear(); materials.Clear();
            targets.Dispose();
        }
    }
}

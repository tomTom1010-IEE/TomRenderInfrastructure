using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TomBSDFRuntime
{
    // GPU displacement cannot change CPU culling bounds in Unity 2019. The
    // shader itself is plugin-independent; this service owns only transient
    // renderer bounds/mesh copies and restores them on shader switch/disposal.
    public sealed class TomBSDFTessellationBounds : IDisposable
    {
        sealed class Entry
        {
            public Renderer renderer;
            public MeshFilter filter;
            public Mesh original, owned;
            public SkinnedMeshRenderer skin;
            public Bounds baseline, applied;
            public bool updatesSuspended;
            public int stamp;
        }
        readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        readonly List<Material> materials = new List<Material>();
        readonly List<int> expired = new List<int>();
        readonly List<Camera> active = new List<Camera>();
        int stamp;
        public int RendererCount { get { return entries.Count; } }
        public int UnsupportedStaticRenderers { get; private set; }

        public static bool Supports(Material material)
        {
            return material && material.shader && (material.shader.name == "tom/BSDFTess"
                || material.shader.name == "tom/BSDFCutoutTess");
        }
        public static Vector3 LocalPadding(Transform transform, float worldDepth)
        {
            return LocalPadding(transform.worldToLocalMatrix, worldDepth);
        }
        static Vector3 LocalPadding(Matrix4x4 m, float worldDepth)
        {
            return new Vector3(new Vector3(m.m00, m.m01, m.m02).magnitude,
                new Vector3(m.m10, m.m11, m.m12).magnitude,
                new Vector3(m.m20, m.m21, m.m22).magnitude) * Mathf.Clamp(worldDepth, 0, .2f);
        }
        public void Prepare(Camera camera)
        {
            if (!camera) return;
            if (active.Count >= 8) Abort();
            active.Add(camera);
            Prepare();
        }
        public void Prepare()
        {
            ++stamp;
            UnsupportedStaticRenderers = 0;
            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                materials.Clear(); renderer.GetSharedMaterials(materials);
                float depth = 0;
                foreach (var material in materials)
                    if (Supports(material)) depth = Mathf.Max(depth, Mathf.Clamp(material.GetFloat("_HeightScale"), 0, .2f));
                if (depth <= 0) continue;
                // A precombined static batch has renderer-specific ranges and
                // culling metadata; cloning its combined Mesh is not a safe
                // per-renderer bounds override in Unity 2019. Unbatch/author it.
                if (renderer.isPartOfStaticBatch) { ++UnsupportedStaticRenderers; continue; }
                int id = renderer.GetInstanceID(); Entry entry;
                if (entries.TryGetValue(id, out entry) && ((entry.filter && entry.filter.sharedMesh != entry.owned)
                    || (entry.skin && entry.skin.sharedMesh != entry.original)))
                {
                    Release(entry); entries.Remove(id); entry = null;
                }
                if (entry == null)
                {
                    entry = new Entry { renderer = renderer, skin = renderer as SkinnedMeshRenderer };
                    if (entry.skin)
                    {
                        entry.original = entry.skin.sharedMesh;
                        SuspendSkinUpdates(entry);
                        entry.baseline = entry.skin.localBounds;
                    }
                    else
                    {
                        entry.filter = renderer.GetComponent<MeshFilter>();
                        if (!entry.filter || !entry.filter.sharedMesh) continue;
                        entry.original = entry.filter.sharedMesh;
                        entry.owned = Object.Instantiate(entry.original);
                        entry.owned.name = entry.original.name + " (TomBSDF transient bounds)";
                        entry.owned.hideFlags = HideFlags.HideAndDontSave;
                        entry.baseline = entry.original.bounds;
                        entry.filter.sharedMesh = entry.owned;
                    }
                    entry.applied = entry.baseline;
                    entries.Add(id, entry);
                }
                SuspendSkinUpdates(entry);
                Bounds current = entry.skin ? entry.skin.localBounds : entry.owned.bounds;
                // ME/body/animation systems can replace their bounds. Rebase,
                // rather than accumulating our expansion or undoing their work.
                if (current != entry.applied) entry.baseline = current;
                entry.applied = entry.baseline;
                Vector3 padding = LocalPadding(renderer.worldToLocalMatrix, depth);
                // Skin bounds follow the root bone. Conservatively contain
                // either local frame when a renderer/root transform differs.
                if (entry.skin && entry.skin.rootBone)
                    padding = Vector3.Max(padding, LocalPadding(entry.skin.rootBone.worldToLocalMatrix, depth));
                entry.applied.Expand(padding * 2);
                if (entry.skin) entry.skin.localBounds = entry.applied;
                else entry.owned.bounds = entry.applied;
                entry.stamp = stamp;
            }
            expired.Clear();
            foreach (var pair in entries) if (!pair.Value.renderer || pair.Value.stamp != stamp) expired.Add(pair.Key);
            foreach (int id in expired) { Release(entries[id]); entries.Remove(id); }
            materials.Clear();
        }
        static void SuspendSkinUpdates(Entry entry)
        {
            if (!entry.skin || !entry.skin.updateWhenOffscreen) return;
            // Unity 2019 recomputes the skin AABB even when merely reading it
            // with this flag enabled. Suspend only during the camera/cull scope;
            // visible skinning still runs, and offscreen updates resume after it.
            entry.skin.updateWhenOffscreen = false;
            entry.updatesSuspended = true;
        }
        static void ResumeSkinUpdates(Entry entry)
        {
            if (!entry.updatesSuspended) return;
            if (entry.skin && !entry.skin.updateWhenOffscreen) entry.skin.updateWhenOffscreen = true;
            entry.updatesSuspended = false;
        }
        public void EndCamera(Camera camera)
        {
            if (active.Count > 0 && active[active.Count - 1] == camera) active.RemoveAt(active.Count - 1);
            else active.Clear();
            if (active.Count == 0) foreach (var entry in entries.Values) ResumeSkinUpdates(entry);
        }
        public void Abort()
        {
            active.Clear();
            foreach (var entry in entries.Values) ResumeSkinUpdates(entry);
        }
        static void Release(Entry entry)
        {
            if (entry.skin && entry.skin.localBounds == entry.applied) entry.skin.localBounds = entry.baseline;
            if (entry.filter && entry.filter.sharedMesh == entry.owned) entry.filter.sharedMesh = entry.original;
            ResumeSkinUpdates(entry);
            if (entry.owned)
            {
                if (Application.isPlaying) Object.Destroy(entry.owned); else Object.DestroyImmediate(entry.owned);
            }
        }
        public void Dispose()
        {
            foreach (var entry in entries.Values) Release(entry);
            entries.Clear(); materials.Clear(); expired.Clear(); active.Clear();
        }
    }
}

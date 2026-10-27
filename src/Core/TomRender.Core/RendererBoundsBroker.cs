using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TomRenderInfrastructure
{
    // Contributions are AABB unions against one unexpanded baseline. One clone
    // per MeshFilter, even when different material slots use different families.
    public static class RendererBoundsBroker
    {
        internal sealed class Entry
        {
            internal Renderer renderer;
            internal SkinnedMeshRenderer skin;
            internal MeshFilter filter;
            internal Mesh original, owned;
            internal Bounds baseline, applied;
            internal bool initialized, suspended;
            internal readonly List<Lease> leases = new List<Lease>();
        }
        public sealed class Lease : IDisposable
        {
            Entry entry;
            internal float worldPadding, smoothing;
            internal bool rootFrame, suspend;
            internal Lease(Entry value) { entry = value; }
            public bool IsActive { get { return entry != null && entry.renderer; } }
            public void Set(float worldPadding, float smoothing = 0, bool rootFrame = false, bool suspendSkin = false)
            {
                if (entry == null) throw new ObjectDisposedException("Bounds lease");
                this.worldPadding = Finite(worldPadding); this.smoothing = Finite(smoothing);
                this.rootFrame = rootFrame; suspend = suspendSkin;
                Apply(entry);
            }
            public void ResumeSkin() { if (entry != null) { suspend = false; ApplySuspension(entry); } }
            public void Dispose()
            {
                var previous = entry; if (previous == null) return; entry = null;
                previous.leases.Remove(this);
                if (previous.leases.Count > 0) { Apply(previous); return; }
                Restore(previous); entries.Remove(previous.renderer);
            }
        }
        static readonly Dictionary<Renderer, Entry> entries = new Dictionary<Renderer, Entry>();
        public static int RendererCount { get { return entries.Count; } }
        public static int MeshCount { get { int n=0;foreach(var entry in entries.Values)if(entry.owned)++n;return n; } }
        internal static void CollectDestroyedRenderers()
        {
            var expired=new List<Entry>();foreach(var entry in entries.Values)if(!entry.renderer)expired.Add(entry);
            foreach(var entry in expired)foreach(var lease in entry.leases.ToArray())lease.Dispose();
        }
        static float Finite(float value) { return float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0,value); }
        public static Lease Acquire(Renderer renderer)
        {
            if (!renderer) throw new ArgumentNullException("renderer");
            Entry entry;
            if (!entries.TryGetValue(renderer, out entry))
            {
                entry = new Entry { renderer=renderer, skin=renderer as SkinnedMeshRenderer, filter=renderer.GetComponent<MeshFilter>() };
                if (!entry.skin && (!entry.filter || !entry.filter.sharedMesh)) return null;
                entries.Add(renderer,entry);
            }
            var lease = new Lease(entry); entry.leases.Add(lease); return lease;
        }
        static Vector3 LocalPadding(Matrix4x4 matrix,float world)
        {
            return new Vector3(new Vector3(matrix.m00,matrix.m01,matrix.m02).magnitude,
                new Vector3(matrix.m10,matrix.m11,matrix.m12).magnitude,
                new Vector3(matrix.m20,matrix.m21,matrix.m22).magnitude)*world;
        }
        static void ApplySuspension(Entry entry)
        {
            if (!entry.skin) return;
            bool wanted=false;foreach(var lease in entry.leases)wanted |= lease.suspend;
            if (wanted && entry.skin.updateWhenOffscreen) { entry.skin.updateWhenOffscreen=false;entry.suspended=true; }
            else if (!wanted && entry.suspended) { if(!entry.skin.updateWhenOffscreen)entry.skin.updateWhenOffscreen=true;entry.suspended=false; }
        }
        static void Apply(Entry entry)
        {
            if (!entry.renderer) return;
            ApplySuspension(entry);
            if (entry.skin)
            {
                if (!entry.initialized || entry.original!=entry.skin.sharedMesh || entry.skin.localBounds!=entry.applied)
                    entry.baseline=entry.skin.localBounds;
                entry.original=entry.skin.sharedMesh;
            }
            else
            {
                if (!entry.filter) return;
                if (!entry.owned || entry.filter.sharedMesh!=entry.owned)
                {
                    Destroy(entry.owned);entry.owned=null;entry.original=entry.filter.sharedMesh;
                    if(!entry.original)return;
                    entry.baseline=entry.original.bounds;
                    entry.owned=Object.Instantiate(entry.original);
                    entry.owned.name=entry.original.name+" [Tom shared bounds]";
                    entry.owned.hideFlags=HideFlags.HideAndDontSave;entry.filter.sharedMesh=entry.owned;
                }
                else if(entry.initialized && entry.owned.bounds!=entry.applied)entry.baseline=entry.owned.bounds;
            }
            Vector3 union=Vector3.zero;
            foreach(var lease in entry.leases)
            {
                var padding=LocalPadding(entry.renderer.worldToLocalMatrix,lease.worldPadding);
                if(lease.rootFrame && entry.skin && entry.skin.rootBone)
                    padding=Vector3.Max(padding,LocalPadding(entry.skin.rootBone.worldToLocalMatrix,lease.worldPadding));
                padding+=Vector3.one*(entry.baseline.size.magnitude*lease.smoothing);
                union=Vector3.Max(union,padding);
            }
            entry.applied=entry.baseline;entry.applied.Expand(union*2);entry.initialized=true;
            if(entry.skin)entry.skin.localBounds=entry.applied;else entry.owned.bounds=entry.applied;
        }
        static void Restore(Entry entry)
        {
            if(entry.skin && entry.initialized && entry.skin.localBounds==entry.applied)entry.skin.localBounds=entry.baseline;
            if(entry.filter && entry.owned && entry.filter.sharedMesh==entry.owned)entry.filter.sharedMesh=entry.original;
            ApplySuspension(entry);Destroy(entry.owned);entry.owned=null;
        }
        static void Destroy(Object value) { if(!value)return;if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value); }
    }
}

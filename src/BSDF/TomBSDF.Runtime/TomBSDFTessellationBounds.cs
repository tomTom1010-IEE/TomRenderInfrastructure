using System;
using System.Collections.Generic;
using TomRenderInfrastructure;
using UnityEngine;

namespace TomBSDFRuntime
{
    // Keep BSDF inward-height policy separate from the shared bounds owner.
    public sealed class TomBSDFTessellationBounds : IDisposable
    {
        sealed class Entry { public RendererBoundsBroker.Lease lease;public int stamp; }
        readonly Dictionary<Renderer,Entry> entries=new Dictionary<Renderer,Entry>();
        readonly List<Material> materials=new List<Material>();
        readonly List<Renderer> expired=new List<Renderer>();
        readonly List<Camera> active=new List<Camera>();
        int stamp;
        public int RendererCount { get { return entries.Count; } }
        internal bool OwnsCamera(Camera camera) { return active.Count>0 && active[active.Count-1]==camera; }
        public int UnsupportedStaticRenderers { get; private set; }
        public static bool Supports(Material material)
        {return material && material.shader && (material.shader.name=="tom/BSDFTess" || material.shader.name=="tom/BSDFCutoutTess");}
        public static Vector3 LocalPadding(Transform transform,float worldDepth)
        {
            var m=transform.worldToLocalMatrix;
            return new Vector3(new Vector3(m.m00,m.m01,m.m02).magnitude,new Vector3(m.m10,m.m11,m.m12).magnitude,
                new Vector3(m.m20,m.m21,m.m22).magnitude)*Mathf.Clamp(worldDepth,0,.2f);
        }
        public void Prepare(Camera camera)
        {if(!camera)return;if(active.Count>=8)Abort();active.Add(camera);Prepare();}
        public void Prepare()
        {
            ++stamp;UnsupportedStaticRenderers=0;
            foreach(var renderer in SceneDiscovery.Renderers())
                PrepareRenderer(renderer);
            expired.Clear();foreach(var pair in entries)if(!pair.Key || pair.Value.stamp!=stamp)expired.Add(pair.Key);
            foreach(var renderer in expired){entries[renderer].lease.Dispose();entries.Remove(renderer);}
            materials.Clear();
        }
        void PrepareRenderer(Renderer renderer)
        {
            if(!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)return;
            materials.Clear();renderer.GetSharedMaterials(materials);float depth=0;
            foreach(var material in materials)if(Supports(material))depth=Mathf.Max(depth,Mathf.Clamp(material.GetFloat("_HeightScale"),0,.2f));
            if(depth<=0)return;
            if(renderer.isPartOfStaticBatch){++UnsupportedStaticRenderers;return;}
            Entry entry;
            if(!entries.TryGetValue(renderer,out entry))
            {
                var lease=RendererBoundsBroker.Acquire(renderer);if(lease==null)return;
                entries.Add(renderer,entry=new Entry{lease=lease});
            }
            entry.lease.Set(depth,0,true,true);entry.stamp=stamp;
        }
        public void EndCamera(Camera camera)
        {
            if(active.Count>0 && active[active.Count-1]==camera)active.RemoveAt(active.Count-1);else active.Clear();
            if(active.Count==0)foreach(var entry in entries.Values)entry.lease.ResumeSkin();
        }
        public void Abort() { active.Clear();foreach(var entry in entries.Values)entry.lease.ResumeSkin(); }
        public void Dispose()
        {foreach(var entry in entries.Values)entry.lease.Dispose();entries.Clear();materials.Clear();expired.Clear();active.Clear();}
    }
}

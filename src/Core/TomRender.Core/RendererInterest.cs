using System;
using System.Collections.Generic;
using UnityEngine;

namespace TomRenderInfrastructure
{
    // A view owns shader membership, not material values, visibility or bounds.
    public sealed class RendererInterest : IDisposable
    {
        internal readonly Func<Material, bool> classifier;
        internal readonly ulong bit;
        readonly List<Renderer> targets = new List<Renderer>();
        readonly Dictionary<Renderer, int> indices = new Dictionary<Renderer, int>();
        internal bool layersDirty;
        int layers;
        public string Name { get; private set; }
        public long Revision { get; private set; }
        public int Count { get { return targets.Count; } }
        public Renderer this[int index] { get { return targets[index]; } }
        public bool Contains(Renderer renderer) { return indices.ContainsKey(renderer); }
        public int LayerMask { get { UpdateLayers(); return layers; } }
        internal RendererInterest(string name, ulong bit, Func<Material, bool> classifier)
        { Name = name; this.bit = bit; this.classifier = classifier; }
        public bool Affects(Camera camera)
        {
            if (!camera || camera.cullingMask == 0) return false;
            SceneDiscovery.RefreshForCamera(camera);
            return (camera.cullingMask & LayerMask) != 0;
        }
        internal void Set(Renderer renderer, bool included)
        {
            int index;
            bool exists = indices.TryGetValue(renderer, out index);
            if (included && !exists)
            { indices.Add(renderer, targets.Count); targets.Add(renderer); layersDirty = true; ++Revision; }
            else if (!included && exists)
            {
                int last = targets.Count - 1;
                var moved = targets[last]; targets[index] = moved; indices[moved] = index;
                targets.RemoveAt(last); indices.Remove(renderer); layersDirty = true; ++Revision;
            }
        }
        internal void Clear() { targets.Clear(); indices.Clear(); layers = 0; layersDirty = false; ++Revision; }
        void UpdateLayers()
        {
            if (!layersDirty) return;
            layers = 0;
            for (int i = 0; i < targets.Count; i++) if (targets[i]) layers |= 1 << targets[i].gameObject.layer;
            layersDirty = false;
        }
        public void Dispose() { SceneDiscovery.Remove(this); }
    }
}

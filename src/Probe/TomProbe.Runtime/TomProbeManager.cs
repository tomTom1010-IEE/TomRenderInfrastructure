using System;
using System.Collections.Generic;
using TomRenderInfrastructure;
using UnityEngine;

namespace TomProbeRuntime
{
    public sealed class TomProbeManager : MonoBehaviour
    {
        private const float AssignmentInterval = 0.5f;

        private static readonly List<TomSHProbeVolume> Volumes = new List<TomSHProbeVolume>();
        private static TomProbeManager _instance;

        private static readonly int VolumeTextureId = Shader.PropertyToID("_TomSHVolumeTex");
        private static readonly int WorldToLocalId = Shader.PropertyToID("_TomSHWorldToLocal");
        private static readonly int BoundsMinId = Shader.PropertyToID("_TomSHBoundsMin");
        private static readonly int BoundsInvSizeId = Shader.PropertyToID("_TomSHBoundsInvSize");
        private static readonly int GridId = Shader.PropertyToID("_TomSHVolumeGrid");
        private static readonly int ParamsId = Shader.PropertyToID("_TomSHVolumeParams");
        private static readonly int CustomBlendId = Shader.PropertyToID("_CustomSHVolumeBlend");

        private readonly Dictionary<Renderer, TomSHProbeVolume> _assignments =
            new Dictionary<Renderer, TomSHProbeVolume>();
        private MaterialPropertyBlock _propertyBlock;
        private float _nextAssignmentTime;
        private bool _assignmentDirty = true;
        private RendererInterest _targets;
        private readonly HashSet<Renderer> _visited = new HashSet<Renderer>();
        private readonly List<Renderer> _stale = new List<Renderer>();
        private readonly List<Material> _materials = new List<Material>();

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            _targets = SceneDiscovery.WatchShaders("SH volume receivers", HasCustomSH);
        }
        private static bool HasCustomSH(Material material) { return material && material.HasProperty(CustomBlendId); }

        public static void EnsureInstance()
        {
            if (_instance != null)
                return;

            GameObject managerObject = new GameObject("Tom Probe Manager");
            managerObject.hideFlags = HideFlags.HideAndDontSave;
            if (Application.isPlaying) DontDestroyOnLoad(managerObject);
            _instance = managerObject.AddComponent<TomProbeManager>();
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }

        public static void Register(TomSHProbeVolume volume)
        {
            if (volume == null)
                return;

            EnsureInstance();
            if (!Volumes.Contains(volume))
                Volumes.Add(volume);
            _instance._assignmentDirty = true;
        }

        public static void Unregister(TomSHProbeVolume volume)
        {
            Volumes.Remove(volume);
            if (_instance != null)
                _instance._assignmentDirty = true;
        }

        public static void NotifyVolumeChanged(TomSHProbeVolume volume)
        {
            if (_instance != null)
                _instance._assignmentDirty = true;
        }

        private void Update()
        {
            if (!_assignmentDirty && Time.unscaledTime < _nextAssignmentTime)
                return;

            _assignmentDirty = false;
            _nextAssignmentTime = Time.unscaledTime + AssignmentInterval;
            RefreshAssignments();
        }

        private void RefreshAssignments()
        {
            RemoveInvalidVolumes();
            bool ready = false;
            for (int i = 0; i < Volumes.Count; i++)
                if (Volumes[i] && Volumes[i].isActiveAndEnabled && Volumes[i].IsReady) { ready = true; break; }
            if (!ready)
            {
                foreach (var pair in _assignments) if (pair.Key) ClearVolume(pair.Key);
                _assignments.Clear();
                return;
            }
            SceneDiscovery.RefreshKnown();
            _visited.Clear();

            for (int index = 0; index < _targets.Count; index++)
            {
                var renderer = _targets[index];
                if (!IsTargetRenderer(renderer))
                    continue;

                _visited.Add(renderer);
                TomSHProbeVolume bestVolume = FindBestVolume(renderer.bounds.center);
                ApplyVolume(renderer, bestVolume);
            }

            _stale.Clear();
            foreach (KeyValuePair<Renderer, TomSHProbeVolume> pair in _assignments)
            {
                if (pair.Key == null || !_visited.Contains(pair.Key))
                    _stale.Add(pair.Key);
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                Renderer renderer = _stale[i];
                if (renderer != null)
                    ClearVolume(renderer);
                _assignments.Remove(renderer);
            }
            _materials.Clear(); _visited.Clear(); _stale.Clear();
        }

        private bool IsTargetRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            _materials.Clear(); renderer.GetSharedMaterials(_materials);
            for (int i = 0; i < _materials.Count; i++)
            {
                Material material = _materials[i];
                if (material != null && material.HasProperty(CustomBlendId))
                    return true;
            }

            return false;
        }

        private static TomSHProbeVolume FindBestVolume(Vector3 worldPosition)
        {
            TomSHProbeVolume best = null;
            float bestInfluence = 0f;
            int bestPriority = int.MinValue;

            for (int i = 0; i < Volumes.Count; i++)
            {
                TomSHProbeVolume volume = Volumes[i];
                if (volume == null || !volume.isActiveAndEnabled || !volume.IsReady)
                    continue;

                float influence = volume.GetInfluence(worldPosition);
                if (influence <= 0f)
                    continue;

                if (volume.priority > bestPriority
                    || (volume.priority == bestPriority && influence > bestInfluence))
                {
                    best = volume;
                    bestPriority = volume.priority;
                    bestInfluence = influence;
                }
            }

            return best;
        }

        private void ApplyVolume(Renderer renderer, TomSHProbeVolume volume)
        {
            if (volume == null)
            {
                if (_assignments.ContainsKey(renderer))
                {
                    ClearVolume(renderer);
                    _assignments.Remove(renderer);
                }
                return;
            }

            renderer.GetPropertyBlock(_propertyBlock);
            TomProbeSettings published = volume.PublishedSettings;
            Vector3 minimum = published.origin - published.size * 0.5f;
            Vector3 inverseSize = new Vector3(
                1f / published.size.x,
                1f / published.size.y,
                1f / published.size.z);

            _propertyBlock.SetTexture(VolumeTextureId, volume.AtlasTexture);
            _propertyBlock.SetMatrix(WorldToLocalId, Matrix4x4.identity);
            _propertyBlock.SetVector(BoundsMinId, new Vector4(minimum.x, minimum.y, minimum.z, 0f));
            _propertyBlock.SetVector(
                BoundsInvSizeId,
                new Vector4(inverseSize.x, inverseSize.y, inverseSize.z, 0f));
            _propertyBlock.SetVector(
                GridId,
                new Vector4(
                    published.grid.x,
                    published.grid.y,
                    published.grid.z,
                    published.grid.x * 7f));
            _propertyBlock.SetVector(
                ParamsId,
                new Vector4(1f, volume.intensity, volume.edgeFade, volume.DataVersion));
            renderer.SetPropertyBlock(_propertyBlock);
            _assignments[renderer] = volume;
        }

        private void ClearVolume(Renderer renderer)
        {
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetVector(ParamsId, Vector4.zero);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        private static void RemoveInvalidVolumes()
        {
            for (int i = Volumes.Count - 1; i >= 0; i--)
            {
                if (Volumes[i] == null)
                    Volumes.RemoveAt(i);
            }
        }

        public static void RefreshNow()
        { if (_instance != null) _instance.RefreshAssignments(); }

        public static IDisposable SuppressForCapture()
        { return new CaptureScope(); }

        private sealed class CaptureScope : IDisposable
        {
            private readonly List<RendererPropertyBroker.Lease> _originals = new List<RendererPropertyBroker.Lease>();
            public CaptureScope()
            {
                if (_instance == null) return;
                try
                {
                    foreach (var pair in _instance._assignments)
                    {
                        if (pair.Key == null) continue;
                        _originals.Add(RendererPropertyBroker.Vector(pair.Key,ParamsId,Vector4.zero));
                    }
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                for(int i=_originals.Count-1;i>=0;i--)_originals[i].Dispose();
                _originals.Clear();
            }
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            foreach (var pair in _assignments) if (pair.Key != null) ClearVolume(pair.Key);
            _assignments.Clear();
            _materials.Clear(); _visited.Clear(); _stale.Clear();
            if (_targets != null) { _targets.Dispose(); _targets = null; }
            _instance = null;
        }
    }
}

using System;
using System.Collections.Generic;
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

        private void Awake() { _propertyBlock = new MaterialPropertyBlock(); }

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
            Renderer[] renderers = FindObjectsOfType<Renderer>();
            HashSet<Renderer> visited = new HashSet<Renderer>();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!IsTargetRenderer(renderer))
                    continue;

                visited.Add(renderer);
                TomSHProbeVolume bestVolume = FindBestVolume(renderer.bounds.center);
                ApplyVolume(renderer, bestVolume);
            }

            List<Renderer> stale = new List<Renderer>();
            foreach (KeyValuePair<Renderer, TomSHProbeVolume> pair in _assignments)
            {
                if (pair.Key == null || !visited.Contains(pair.Key))
                    stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                Renderer renderer = stale[i];
                if (renderer != null)
                    ClearVolume(renderer);
                _assignments.Remove(renderer);
            }
        }

        private static bool IsTargetRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
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
            private readonly Dictionary<Renderer, MaterialPropertyBlock> _originals =
                new Dictionary<Renderer, MaterialPropertyBlock>();
            public CaptureScope()
            {
                if (_instance == null) return;
                try
                {
                    foreach (var pair in _instance._assignments)
                    {
                        if (pair.Key == null) continue;
                        var original = new MaterialPropertyBlock();
                        pair.Key.GetPropertyBlock(original);
                        _originals.Add(pair.Key, original);
                        var muted = new MaterialPropertyBlock();
                        pair.Key.GetPropertyBlock(muted);
                        muted.SetVector(ParamsId, Vector4.zero);
                        pair.Key.SetPropertyBlock(muted);
                    }
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                foreach (var pair in _originals)
                    if (pair.Key != null) pair.Key.SetPropertyBlock(pair.Value);
                _originals.Clear();
            }
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            foreach (var pair in _assignments) if (pair.Key != null) ClearVolume(pair.Key);
            _assignments.Clear();
            _instance = null;
        }
    }
}

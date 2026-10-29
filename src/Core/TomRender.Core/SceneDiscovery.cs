using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TomRenderInfrastructure
{
    public static class SceneDiscovery
    {
        sealed class Record
        {
            public Renderer renderer;
            public int index, layer, seen;
            public ulong mask;
            public bool watched, notified;
        }
        sealed class ShaderInfo { public string name; public int epoch; public ulong mask; }
        struct Walk { public Transform transform; public int child; public bool read; }
        static readonly List<RendererInterest> interests = new List<RendererInterest>();
        static readonly Dictionary<Renderer, Record> records = new Dictionary<Renderer, Record>();
        static readonly List<Record> all = new List<Record>(), watched = new List<Record>();
        static readonly List<Renderer> renderers = new List<Renderer>(), nodeRenderers = new List<Renderer>();
        static readonly List<Light> lights = new List<Light>(), nodeLights = new List<Light>();
        static readonly HashSet<Light> lightSet = new HashSet<Light>();
        static readonly List<Material> materials = new List<Material>();
        static readonly Dictionary<Shader, ShaderInfo> shaders = new Dictionary<Shader, ShaderInfo>();
        static readonly List<Shader> deadShaders = new List<Shader>();
        static readonly List<GameObject> sceneRoots = new List<GameObject>();
        static readonly List<Transform> roots = new List<Transform>();
        static readonly List<Walk> walk = new List<Walk>();
        static Scene persistentScene;
        static bool initialized, scanning, reaping, requested;
        static int rootIndex, reapIndex, generation, classifierEpoch = 1, knownFrame = -1;
        static float nextPass;
        static long knownInvocation;
        static int watchedLayers;
        static bool watchedLayersDirty;
        public const double DefaultBudgetMilliseconds = .35;
        public const float DefaultRescanInterval = 1f;
        public static long HierarchyPasses { get; private set; }
        public static long NodesVisited { get; private set; }
        public static long MaterialReads { get; private set; }
        public static double LastTickMilliseconds { get; private set; }
        // Retained ABI counters: no native global object searches remain here.
        public static long RendererScans { get; private set; }
        public static long LightScans { get; private set; }
        public static int ScopeCount { get { return 0; } }
        public static int RendererCount { get { return all.Count; } }
        public static int WatchedCount { get { return watched.Count; } }
        public static int InterestCount { get { return interests.Count; } }
        internal static bool IsNeeded { get { return interests.Count != 0; } }

        // Classifiers inspect shader identity/declarations, never material floats,
        // keywords, texture values or override tags. Those remain live.
        public static RendererInterest WatchShaders(string name, Func<Material, bool> classifier)
        {
            if (classifier == null) throw new ArgumentNullException("classifier");
            ulong used = 0; foreach (var item in interests) used |= item.bit;
            ulong bit = 1;
            while ((used & bit) != 0 && bit != (1UL << 63)) bit <<= 1;
            if ((used & bit) != 0) throw new InvalidOperationException("Too many renderer interests.");
            var value = new RendererInterest(name, bit, classifier);
            interests.Add(value); ++classifierEpoch; knownFrame = -1;
            Initialize(); RequestRescan(); UnityCameraHub.DiscoveryChanged();
            return value;
        }
        internal static void Remove(RendererInterest interest)
        {
            if (!interests.Remove(interest)) return;
            interest.Clear(); ++classifierEpoch;
            for (int i = 0; i < watched.Count; i++) watched[i].mask &= ~interest.bit;
            if (!IsNeeded) Shutdown();
            UnityCameraHub.DiscoveryChanged();
        }
        static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
            SceneManager.activeSceneChanged += ActiveSceneChanged;
        }
        static void SceneLoaded(Scene scene, LoadSceneMode mode) { RequestRescan(); }
        static void SceneUnloaded(Scene scene) { RequestRescan(); knownFrame = -1; }
        static void ActiveSceneChanged(Scene before, Scene after) { RequestRescan(); }
        public static void RequestRescan() { requested = true; nextPass = 0; }
        internal static void SetPersistentScene(Scene scene) { persistentScene = scene; }

        public static string ShaderName(Shader shader)
        {
            if (!shader) return "";
            ShaderInfo value;
            if (!shaders.TryGetValue(shader, out value))
                shaders.Add(shader, value = new ShaderInfo { name = shader.name });
            return value.name;
        }
        static ulong Classify(Material material)
        {
            if (!material) return 0;
            var shader = material.shader;
            if (!shader) return 0;
            ShaderInfo value;
            if (!shaders.TryGetValue(shader, out value))
                shaders.Add(shader, value = new ShaderInfo { name = shader.name });
            if (value.epoch != classifierEpoch)
            {
                value.mask = 0;
                for (int i = 0; i < interests.Count; i++)
                    if (interests[i].classifier(material)) value.mask |= interests[i].bit;
                value.epoch = classifierEpoch;
            }
            return value.mask;
        }
        static bool SceneObject(Component component)
        { return component && component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded; }
        static void Read(Record record)
        {
            ulong mask = 0;
            materials.Clear(); record.renderer.GetSharedMaterials(materials); ++MaterialReads;
            for (int i = 0; i < materials.Count; i++) mask |= Classify(materials[i]);
            materials.Clear();
            int layer = record.renderer.gameObject.layer;
            if (record.mask != mask || record.layer != layer)
                for (int i = 0; i < interests.Count; i++)
                {
                    var interest = interests[i];
                    interest.Set(record.renderer, (mask & interest.bit) != 0);
                    if (record.layer != layer && (mask & interest.bit) != 0) interest.layersDirty = true;
                }
            if (record.layer != layer) watchedLayersDirty = true;
            record.mask = mask; record.layer = layer;
            if (mask != 0 && !record.watched) { record.watched = true; watched.Add(record); watchedLayersDirty = true; }
        }
        static void Discover(Renderer renderer, bool notified)
        {
            if (!SceneObject(renderer)) return;
            Record record;
            if (!records.TryGetValue(renderer, out record))
            {
                record = new Record { renderer = renderer, index = all.Count, layer = -1 };
                records.Add(renderer, record); all.Add(record); renderers.Add(renderer);
            }
            record.seen = generation; record.notified |= notified;
            Read(record);
        }
        public static void NotifyRenderer(Renderer renderer)
        {
            if (!IsNeeded || ReferenceEquals(renderer, null)) return;
            Record record;
            if (!renderer && records.TryGetValue(renderer, out record)) RemoveRecord(record.index);
            else Discover(renderer, true);
        }

        // Explicit ME/object notifications are bounded to the given subtree,
        // not a search of the scene or loaded resources. No material instances.
        public static void NotifyHierarchy(GameObject root)
        {
            if (!IsNeeded || !root || !root.scene.IsValid()) return;
            root.GetComponentsInChildren(true, nodeRenderers);
            for (int i = 0; i < nodeRenderers.Count; i++) Discover(nodeRenderers[i], true);
            nodeRenderers.Clear();
            root.GetComponentsInChildren(true, nodeLights);
            for (int i = 0; i < nodeLights.Count; i++) AddLight(nodeLights[i]);
            nodeLights.Clear();
        }
        static void AddLight(Light light) { if (SceneObject(light) && lightSet.Add(light)) lights.Add(light); }
        static void RemoveRecord(int index)
        {
            var record = all[index];
            for (int i = 0; i < interests.Count; i++) interests[i].Set(record.renderer, false);
            records.Remove(record.renderer);
            if (record.watched) { watched.Remove(record); record.watched = false; watchedLayersDirty = true; }
            int last = all.Count - 1;
            all[index] = all[last]; all[index].index = index;
            renderers[index] = renderers[last]; all.RemoveAt(last); renderers.RemoveAt(last);
        }
        public static void RefreshKnown()
        {
            if (knownFrame == Time.frameCount) return;
            knownFrame = Time.frameCount;
            for (int i = watched.Count - 1; i >= 0; i--)
            {
                var record = watched[i];
                if (!SceneObject(record.renderer)) RemoveRecord(record.index);
                else Read(record);
            }
        }
        public static void RefreshForCamera(Camera camera)
        {
            if (!camera || camera.cullingMask == 0) return;
            if (watchedLayersDirty)
            {
                watchedLayers = 0;
                for (int i = 0; i < watched.Count; i++)
                    if (watched[i].renderer) watchedLayers |= 1 << watched[i].renderer.gameObject.layer;
                watchedLayersDirty = false;
            }
            if ((camera.cullingMask & watchedLayers) == 0) return;
            var context = UnityCameraHub.Coordinator.Find(camera);
            if (context == null) { RefreshKnown(); return; }
            if (knownInvocation == context.Id) return;
            knownInvocation = context.Id;
            // Only previously relevant identities, and only on visible layers.
            // This catches direct shader/clone changes even after LateUpdate or
            // between two Camera.Render calls in one frame, without a ME event.
            for (int i = watched.Count - 1; i >= 0; i--)
            {
                var record = watched[i];
                if (!SceneObject(record.renderer)) RemoveRecord(record.index);
                else if ((camera.cullingMask & (1 << record.renderer.gameObject.layer)) != 0) Read(record);
            }
        }
        // Compatibility views remain complete indexed candidates, not BSDF-only.
        // They never initiate discovery from a render invocation.
        public static IEnumerable<Renderer> Renderers() { return renderers; }
        public static IEnumerable<Light> Lights() { return lights; }

        static void AddRoots(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            sceneRoots.Clear();
            if (sceneRoots.Capacity < scene.rootCount) sceneRoots.Capacity = scene.rootCount;
            scene.GetRootGameObjects(sceneRoots);
            for (int i = 0; i < sceneRoots.Count; i++) if (sceneRoots[i]) roots.Add(sceneRoots[i].transform);
            sceneRoots.Clear();
        }
        static void StartPass()
        {
            scanning = true; reaping = false; requested = false; ++generation; ++HierarchyPasses;
            roots.Clear(); walk.Clear(); rootIndex = reapIndex = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++) AddRoots(SceneManager.GetSceneAt(i));
            if (Application.isPlaying) AddRoots(persistentScene);
        }
        static void Step()
        {
            if (reaping)
            {
                if (reapIndex < all.Count)
                {
                    var record = all[reapIndex];
                    if (!SceneObject(record.renderer) || (!record.notified && record.seen != generation)) RemoveRecord(reapIndex);
                    else ++reapIndex;
                    return;
                }
                for (int i = lights.Count - 1; i >= 0; i--)
                    if (!SceneObject(lights[i])) { lightSet.Remove(lights[i]); lights.RemoveAt(i); }
                deadShaders.Clear(); foreach (var pair in shaders) if (!pair.Key) deadShaders.Add(pair.Key);
                foreach (var shader in deadShaders) shaders.Remove(shader); deadShaders.Clear();
                scanning = reaping = false; roots.Clear(); walk.Clear();
                nextPass = Time.realtimeSinceStartup + DefaultRescanInterval;
                return;
            }
            if (walk.Count == 0)
            {
                if (rootIndex >= roots.Count) { reaping = true; return; }
                walk.Add(new Walk { transform = roots[rootIndex++] });
            }
            int index = walk.Count - 1;
            var current = walk[index];
            if (!current.transform) { walk.RemoveAt(index); return; }
            if (!current.read)
            {
                current.read = true; ++NodesVisited;
                var go = current.transform.gameObject;
                if ((go.hideFlags & HideFlags.DontSave) == 0)
                {
                    go.GetComponents(nodeRenderers);
                    for (int i = 0; i < nodeRenderers.Count; i++) Discover(nodeRenderers[i], false);
                    nodeRenderers.Clear();
                    go.GetComponents(nodeLights);
                    for (int i = 0; i < nodeLights.Count; i++) AddLight(nodeLights[i]);
                    nodeLights.Clear();
                }
                walk[index] = current;
                return;
            }
            if (current.child < current.transform.childCount)
            {
                var child = current.transform.GetChild(current.child++);
                walk[index] = current; walk.Add(new Walk { transform = child });
            }
            else walk.RemoveAt(index);
        }
        public static void Pump(double budgetMilliseconds = DefaultBudgetMilliseconds)
        {
            if (!IsNeeded || UnityCameraHub.Coordinator.ActiveDepth != 0) { LastTickMilliseconds = 0; return; }
            long start = Stopwatch.GetTimestamp();
            RefreshKnown();
            if (!scanning && (requested || Time.realtimeSinceStartup >= nextPass)) StartPass();
            long deadline = start + (long)(Math.Max(.01, budgetMilliseconds) * Stopwatch.Frequency / 1000.0);
            while (scanning && Stopwatch.GetTimestamp() < deadline) Step();
            LastTickMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }
        static void Shutdown()
        {
            if (initialized)
            {
                SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneUnloaded -= SceneUnloaded;
                SceneManager.activeSceneChanged -= ActiveSceneChanged; initialized = false;
            }
            records.Clear(); all.Clear(); watched.Clear(); renderers.Clear(); lights.Clear(); lightSet.Clear();
            shaders.Clear(); roots.Clear(); walk.Clear(); materials.Clear(); nodeRenderers.Clear(); nodeLights.Clear();
            scanning = reaping = requested = false; knownFrame = -1; knownInvocation = 0;
            watchedLayers = 0; watchedLayersDirty = false; LastTickMilliseconds = 0;
        }
    }
}

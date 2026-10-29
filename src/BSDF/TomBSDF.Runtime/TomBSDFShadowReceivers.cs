using System;
using System.Collections.Generic;
using TomRenderInfrastructure;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TomBSDFRuntime
{
    // Native maps are copied, not re-rendered. Nothing owns or replaces a Unity
    // shadow texture, light matrix, project shadow setting, or camera depth.
    public sealed class TomBSDFShadowReceivers : IDisposable
    {
        public const int Version = 1, MaxLights = 8, MetadataWidth = 40;
        public const string CommandName = "TomBSDF native shadow receivers v1";
        static readonly int Params = Shader.PropertyToID("_TomBSDFNativeShadowParams");
        static readonly int Metadata = Shader.PropertyToID("_TomBSDFNativeShadowMetadata");
        static readonly int ReceiverOptOut = Shader.PropertyToID("_TomBSDFReceiverOptOut");
        static readonly int CaptureScope = Shader.PropertyToID("_TomNativeCaptureScope");
        static readonly int[] Maps = new int[MaxLights];
        static TomBSDFShadowReceivers() { for (int i = 0; i < MaxLights; i++) Maps[i] = Shader.PropertyToID("_TomBSDFNativeShadow" + i); }

        sealed class Map : IDisposable
        {
            public Light light;
            public RenderTexture texture;
            public RenderResourceLease lease;
            public int width, height;
            public void Dispose()
            {
                if(lease!=null)lease.Dispose();lease=null;texture=null;light=null;
            }
        }
        sealed class Hook : IDisposable
        {
            public Light light;
            public CommandBuffer command;
            public CommandBuffer directionalMetadata;
            public bool directional;
            public int hash;
            public bool attached;
            public void Attach() { if (!attached && light) { light.AddCommandBuffer(LightEvent.AfterShadowMap, command); if (directional) light.AddCommandBuffer(LightEvent.BeforeScreenspaceMask, directionalMetadata); attached = true; } }
            public void Detach() { if (attached && light) { light.RemoveCommandBuffer(LightEvent.AfterShadowMap, command); light.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, directionalMetadata); } attached = false; }
            public void Dispose() { Detach(); command.Dispose(); directionalMetadata.Dispose(); }
        }
        sealed class CameraResources : IDisposable
        {
            public Camera camera;
            public RenderTexture metadata;
            public RenderTexture depthStamp;
            public RenderResourceLease metadataLease,stampLease;
            public readonly List<Map> maps = new List<Map>();
            public int sequence, dropped, owner;
            public string status = "No demand";
            public void Dispose()
            {
                foreach (var map in maps) map.Dispose(); maps.Clear();
                if(metadataLease!=null)metadataLease.Dispose();metadataLease=null;metadata=null;
                if(stampLease!=null)stampLease.Dispose();stampLease=null;depthStamp=null;
            }
        }
        sealed class Scope
        {
            public Camera camera;
            public CameraResources resources;
            public Vector4 previousParams;
            public Vector4 previousCapture, capture;
            public Texture previousMetadata;
            public readonly Texture[] previousMaps = new Texture[MaxLights];
            public readonly List<RendererPropertyBroker.Lease> optOuts = new List<RendererPropertyBroker.Lease>();
        }
        readonly Dictionary<int, CameraResources> cameras = new Dictionary<int, CameraResources>();
        readonly Dictionary<int, Hook> hooks = new Dictionary<int, Hook>();
        readonly List<Scope> stack = new List<Scope>();
        readonly List<Scope> scopePool = new List<Scope>();
        readonly List<Light> lights = new List<Light>();
        readonly List<Material> materials = new List<Material>();
        readonly List<Renderer> optOuts = new List<Renderer>();
        readonly List<int> dead = new List<int>();
        readonly Plane[] planes = new Plane[6];
        readonly RendererInterest targets = SceneDiscovery.WatchShaders("BSDF alpha shadow receivers", IsReceiver);
        TomBSDFShadowResources resources;
        Material copy;
        int sequence;
        public int MemoryBudgetMB = 128;
        public int CameraCount { get { return cameras.Count; } }
        internal bool OwnsCamera(Camera camera) { return stack.Count > 0 && stack[stack.Count - 1].camera == camera; }
        public int TextureCount { get { int n = 0; foreach (var c in cameras.Values) n += c.maps.Count + (c.metadata ? 1 : 0) + (c.depthStamp ? 1 : 0); return n; } }
        public string Status(Camera camera) { CameraResources c; return camera && cameras.TryGetValue(camera.GetInstanceID(), out c) ? c.status : "No demand"; }
        public bool TryGetTextures(Camera camera, out RenderTexture metadata, out RenderTexture[] maps)
        {
            CameraResources c; metadata = null; maps = new RenderTexture[0];
            if (!camera || !cameras.TryGetValue(camera.GetInstanceID(), out c) || !c.metadata) return false;
            metadata = c.metadata; maps = new RenderTexture[c.maps.Count];
            for (int i = 0; i < maps.Length; i++) maps[i] = c.maps[i].texture;
            return true;
        }
        public void SetResources(TomBSDFShadowResources value)
        {
            if (resources == value) return;
            Abort(); foreach (var c in cameras.Values) c.Dispose(); cameras.Clear();
            foreach (var hook in hooks.Values) hook.Dispose(); hooks.Clear();
            if (copy) Destroy(copy); copy = null; resources = value;
        }
        static bool IsReceiver(Material m)
        {
            if (!m) return false;
            string name = SceneDiscovery.ShaderName(m.shader);
            return name == "tom/BSDFAlpha" || name == "tom/BSDFAlphaBackFront" || name == "tom/BSDFGlass";
        }
        int Demand(Camera camera)
        {
            int layers = 0; optOuts.Clear();
            if (!targets.Affects(camera)) return 0;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            for (int index = 0; index < targets.Count; index++)
            {
                var r = targets[index];
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy || (camera.cullingMask & (1 << r.gameObject.layer)) == 0
                    || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                materials.Clear(); r.GetSharedMaterials(materials);
                bool alpha = false; foreach (var m in materials) if (IsReceiver(m)) { alpha = true; break; }
                if (!alpha) continue;
                if (r.receiveShadows) layers |= 1 << r.gameObject.layer; else optOuts.Add(r);
            }
            return layers;
        }
        static int Resolution(Light light)
        {
            if (light.shadowCustomResolution > 0) return Mathf.NextPowerOfTwo(light.shadowCustomResolution);
            int quality = light.shadowResolution == LightShadowResolution.FromQualitySettings
                ? (int)QualitySettings.shadowResolution : (int)light.shadowResolution;
            int size = 512 << Mathf.Clamp(quality, 0, 3);
            if (light.type == LightType.Spot) size /= 2;
            if (light.type == LightType.Point) size /= 4;
            return Mathf.Max(128, size);
        }
        static RenderResourceLease Target(object owner,int width, int height, RenderTextureFormat format, string name)
        {
            var lease=RenderResourceLease.Create(owner,new RenderResourceDescriptor{width=width,height=height,format=format,linear=true,
                filter=FilterMode.Point,purpose=CommandName+" "+name,stage="AfterShadowMap/BeforeScreenspaceMask"});
            var rt=lease.Texture;
            // An unexecuted first-frame light event must never expose recycled
            // GPU memory as a valid metadata packet after a domain reload.
            var previous = RenderTexture.active;
            try { RenderTexture.active = rt; GL.Clear(false, true, Color.clear); }
            finally { RenderTexture.active = previous; }
            return lease;
        }
        public void PrepareCamera(Camera camera, bool enabled)
        {
            if (!camera) return;
            if (stack.Count >= 8) { Abort(); return; }
            if (scopePool.Count <= stack.Count) scopePool.Add(new Scope());
            var scope = scopePool[stack.Count];
            scope.camera = camera; scope.resources = null; scope.optOuts.Clear();
            scope.previousParams = Shader.GetGlobalVector(Params); scope.previousMetadata = Shader.GetGlobalTexture(Metadata);
            scope.previousCapture = Shader.GetGlobalVector(CaptureScope); scope.capture = new Vector4(0, 0, 1, (int)QualitySettings.shadowProjection);
            for (int i = 0; i < MaxLights; i++) scope.previousMaps[i] = Shader.GetGlobalTexture(Maps[i]);
            stack.Add(scope); ClearGlobals();
            Shader.SetGlobalVector(CaptureScope, scope.capture);
            dead.Clear(); foreach (var pair in cameras) if (!pair.Value.camera) dead.Add(pair.Key);
            foreach (int id in dead) { cameras[id].Dispose(); cameras.Remove(id); }
            CameraResources c;
            if (!cameras.TryGetValue(camera.GetInstanceID(), out c))
            {
                if (cameras.Count >= 8)
                {
                    if (stack.Count > 1) return;
                    CameraResources oldest = null;
                    foreach (var cached in cameras.Values) if (oldest == null || cached.sequence < oldest.sequence) oldest = cached;
                    if (oldest != null) { cameras.Remove(oldest.camera.GetInstanceID()); oldest.Dispose(); }
                }
                cameras.Add(camera.GetInstanceID(), c = new CameraResources { camera = camera });
            }
            if (!enabled || !resources || !resources.IsValid || camera.actualRenderingPath != RenderingPath.Forward
                || camera.stereoEnabled || camera.cameraType == CameraType.Preview
                || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 || !SystemInfo.supportsRawShadowDepthSampling
                || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat) || QualitySettings.shadows == ShadowQuality.Disable)
            {
                c.Dispose(); c.status = "Disabled, missing resources or unsupported host";
                if (stack.Count == 1 && (!enabled || !resources || !resources.IsValid)) foreach (var cached in cameras.Values) cached.Dispose();
                RebuildHooks(); return;
            }
            int layers = Demand(camera);
            if (layers == 0) { c.Dispose(); c.status = "No visible Alpha shadow receivers"; RebuildHooks(); return; }
            lights.Clear();
            var sceneLights = SceneDiscovery.Lights();
            foreach (var light in sceneLights)
                if (light && light.enabled && light.gameObject.activeInHierarchy && light.intensity > 0 && light.shadowStrength > 0
                    && light.shadows != LightShadows.None && light.bakingOutput.lightmapBakeType != LightmapBakeType.Baked
                    && (light.cullingMask & layers) != 0 && (light.type == LightType.Directional || light.type == LightType.Spot || light.type == LightType.Point)
                    && light.renderMode != LightRenderMode.ForceVertex
                    && (light.type == LightType.Directional || Vector3.Distance(camera.transform.position, light.transform.position) < camera.farClipPlane + light.range))
                {
                    // Shadow events do not bind the current light color. Public
                    // Forward position/type keys must therefore be unique, even
                    // against coincident active lights that do not cast shadows.
                    bool ambiguous = false;
                    foreach (var other in sceneLights)
                    {
                        if (!other || other == light || !other.enabled || !other.gameObject.activeInHierarchy || other.intensity <= 0
                            || other.type != light.type || other.renderMode == LightRenderMode.ForceVertex || (other.cullingMask & layers) == 0) continue;
                        Vector3 key = light.type == LightType.Directional ? -light.transform.forward : light.transform.position;
                        Vector3 otherKey = other.type == LightType.Directional ? -other.transform.forward : other.transform.position;
                        if ((key - otherKey).sqrMagnitude <= .000001f) { ambiguous = true; break; }
                    }
                    if (!ambiguous) lights.Add(light);
                }
            lights.Sort((a, b) => {
                int type = (a.type == LightType.Directional ? 0 : 1).CompareTo(b.type == LightType.Directional ? 0 : 1);
                if (type != 0) return type;
                int distance = (a.transform.position - camera.transform.position).sqrMagnitude.CompareTo((b.transform.position - camera.transform.position).sqrMagnitude);
                return distance != 0 ? distance : a.GetInstanceID().CompareTo(b.GetInstanceID()); });
            if (lights.Count == 0) { c.Dispose(); c.status = "No native shadow lights"; RebuildHooks(); return; }
            try
            {
                if (!copy) copy = new Material(resources.copyNativeShadow) { hideFlags = HideFlags.HideAndDontSave };
                if (!c.metadata || !c.metadata.IsCreated()) {
                    if(c.metadataLease!=null)c.metadataLease.Dispose();
                    c.metadataLease=Target(c,MetadataWidth,MaxLights,RenderTextureFormat.ARGBFloat,"metadata");c.metadata=c.metadataLease.Texture;
                }
                if (!c.depthStamp || !c.depthStamp.IsCreated()) {
                    if(c.stampLease!=null)c.stampLease.Dispose();
                    c.stampLease=Target(c,1,MaxLights,RenderTextureFormat.RFloat,"depth freshness");c.depthStamp=c.stampLease.Texture;
                }
                c.sequence = ++sequence; if (sequence >= 8000000) sequence = 0;
                if (c.owner == 0) for (int token = 1; token <= 8; token++)
                {
                    bool usedToken = false; foreach (var cached in cameras.Values) if (cached.owner == token) usedToken = true;
                    if (!usedToken) { c.owner = token; break; }
                }
                long budget = Math.Max(1, MemoryBudgetMB) * 1024L * 1024L, used = 0;
                int selected = 0;
                foreach (var light in lights)
                {
                    if (selected == MaxLights) break;
                    int size = Resolution(light), width = light.type == LightType.Point ? size * 3 : size;
                    int height = light.type == LightType.Point ? size * 2 : size;
                    long bytes = (long)width * height * 4;
                    if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize || used + bytes > budget) continue;
                    used += bytes;
                    if (c.maps.Count <= selected) c.maps.Add(new Map());
                    var map = c.maps[selected];
                    if (!map.texture || !map.texture.IsCreated() || map.width != width || map.height != height) {
                        map.Dispose();map.lease=Target(c,width,height,RenderTextureFormat.RFloat,"map "+selected);map.texture=map.lease.Texture;map.width=width;map.height=height;
                    }
                    map.light = light;
                    selected++;
                }
                for (int i = c.maps.Count - 1; i >= selected; i--) { c.maps[i].Dispose(); c.maps.RemoveAt(i); }
                c.dropped = lights.Count - selected; scope.resources = c;
                var context=UnityCameraHub.Coordinator.Find(camera);
                c.metadataLease.Bind(context);c.stampLease.Bind(context);foreach(var map in c.maps)map.lease.Bind(context);
                scope.capture = new Vector4(c.owner, c.sequence,
                    camera.orthographic ? 1 : Math.Max(1, QualitySettings.shadowCascades), (int)QualitySettings.shadowProjection);
                Shader.SetGlobalVector(CaptureScope, scope.capture);
                foreach (var renderer in optOuts)
                {
                    scope.optOuts.Add(RendererPropertyBroker.Float(renderer, ReceiverOptOut, 1));
                    // Indexed blocks override the renderer-wide block, not just
                    // individual values. Preserve them and scope the flag there too.
                    materials.Clear(); renderer.GetSharedMaterials(materials);
                    for (int index = 0; index < materials.Count; index++)
                    {
                        if (!IsReceiver(materials[index])) continue;
                        var indexed = new MaterialPropertyBlock(); renderer.GetPropertyBlock(indexed, index);
                        if (indexed.isEmpty) continue;
                        scope.optOuts.Add(RendererPropertyBroker.Float(renderer, ReceiverOptOut, 1, index));
                    }
                }
                c.status = "Native copy slots scheduled: " + selected + ", unshadowed overflow: " + c.dropped + "; GPU packet freshness determines actual availability";
            }
            catch (Exception e) { c.Dispose(); scope.resources = null; c.status = "Unavailable: " + e.Message; }
            RebuildHooks();
        }
        void RebuildHooks()
        {
            foreach (var c in cameras.Values) for (int i = c.maps.Count - 1; i >= 0; i--)
                if (!c.maps[i].light) { c.maps[i].Dispose(); c.maps.RemoveAt(i); }
            dead.Clear();
            foreach (var pair in hooks)
            {
                bool needed = false;
                foreach (var c in cameras.Values) foreach (var map in c.maps) if (map.light == pair.Value.light && map.light) needed = true;
                if (!needed) dead.Add(pair.Key);
            }
            foreach (int id in dead) { hooks[id].Dispose(); hooks.Remove(id); }
            foreach (var c in cameras.Values) foreach (var map in c.maps)
            {
                int id = map.light.GetInstanceID();
                if (!hooks.ContainsKey(id)) hooks.Add(id, new Hook { light = map.light, command = new CommandBuffer { name = CommandName },
                    directionalMetadata = new CommandBuffer { name = CommandName + " directional matrices" }, directional = map.light.type == LightType.Directional });
            }
            foreach (var hook in hooks.Values)
            {
                int hash = hook.light.shadowStrength.GetHashCode() ^ ((int)hook.light.shadows << 4) ^ ((int)hook.light.type << 8);
                unchecked { foreach (var c in cameras.Values) for (int slot = 0; slot < c.maps.Count; slot++) if (c.maps[slot].light == hook.light)
                    hash = hash * 31 + c.maps[slot].texture.GetInstanceID() * 17 + c.camera.GetInstanceID() + c.metadata.GetInstanceID() + c.depthStamp.GetInstanceID() + slot; }
                if (hash != hook.hash || hook.command.sizeInBytes == 0)
                {
                    bool directional = hook.light.type == LightType.Directional;
                    if (directional != hook.directional) { hook.Detach(); hook.directional = directional; }
                    hook.hash = hash; var cb = hook.command; cb.Clear();
                    var late = hook.directionalMetadata; late.Clear();
                    var native = new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive);
                    int type = hook.light.type == LightType.Directional ? 0 : hook.light.type == LightType.Spot ? 1 : 2;
                    // One stable buffer per light captures the native alias once.
                    // Camera-owned draws choose their target on the GPU, so nested
                    // culling never removes the parent's cached native light hook.
                    cb.SetGlobalTexture(type == 2 ? "_TomNativeShadowCube" : "_TomNativeShadow2D", native);
                    cb.SetShadowSamplingMode(native, ShadowSamplingMode.RawDepth);
                    cb.SetGlobalVector("_TomNativeCaptureInfo", new Vector4(Version, 0, type, 0));
                    cb.SetGlobalVector("_TomNativeCaptureSettings", new Vector4(0, hook.light.shadows == LightShadows.Soft ? 1 : 0, hook.light.shadowStrength, 0));
                    late.SetGlobalVector("_TomNativeCaptureInfo", new Vector4(Version, 0, type, 0));
                    late.SetGlobalVector("_TomNativeCaptureSettings", new Vector4(0, hook.light.shadows == LightShadows.Soft ? 1 : 0, hook.light.shadowStrength, 0));
                    foreach (var c in cameras.Values) for (int slot = 0; slot < c.maps.Count; slot++)
                    {
                        var map = c.maps[slot]; if (map.light != hook.light) continue;
                        var block = new MaterialPropertyBlock();
                        block.SetFloat("_TomNativeCopyOwner", c.owner); block.SetFloat("_TomNativeCopySlot", slot);
                        block.SetVector("_TomNativeCaptureDestination", new Vector4(map.width, map.height, 1f / map.width, 1f / map.height));
                        block.SetTexture("_TomNativeCopyStamp", c.depthStamp);
                        cb.SetRenderTarget(map.texture); cb.SetViewport(new Rect(0, 0, map.width, map.height));
                        cb.DrawProcedural(Matrix4x4.identity, copy, type == 2 ? 2 : 1, MeshTopology.Triangles, 3, 1, block);
                        cb.SetRenderTarget(c.metadata); cb.SetViewport(new Rect(0, slot, MetadataWidth, 1));
                        cb.DrawProcedural(Matrix4x4.identity, copy, 0, MeshTopology.Triangles, 3, 1, block);
                        if (directional)
                        {
                            cb.SetRenderTarget(c.depthStamp); cb.SetViewport(new Rect(0, slot, 1, 1));
                            cb.DrawProcedural(Matrix4x4.identity, copy, 4, MeshTopology.Triangles, 3, 1, block);
                            late.SetRenderTarget(c.metadata); late.SetViewport(new Rect(0, slot, MetadataWidth, 1));
                            late.DrawProcedural(Matrix4x4.identity, copy, 3, MeshTopology.Triangles, 3, 1, block);
                        }
                    }
                }
                if (HasActiveResources()) hook.Attach(); else hook.Detach();
            }
        }
        bool HasActiveResources()
        {
            foreach (var scope in stack) if (scope.resources != null && scope.resources.maps.Count > 0) return true;
            return false;
        }
        public void BeginCamera(Camera camera)
        {
            if (stack.Count == 0 || stack[stack.Count - 1].camera != camera) { ClearGlobals(); return; }
            Shader.SetGlobalVector(CaptureScope, stack[stack.Count - 1].capture);
            Publish(stack[stack.Count - 1].resources);
        }
        static void Publish(CameraResources c)
        {
            if (c == null || !c.metadata || c.maps.Count == 0) { ClearGlobals(); return; }
            Shader.SetGlobalTexture(Metadata, c.metadata);
            for (int i = 0; i < MaxLights; i++) Shader.SetGlobalTexture(Maps[i], i < c.maps.Count ? (Texture)c.maps[i].texture : Texture2D.blackTexture);
            Shader.SetGlobalVector(Params, new Vector4(Version, c.maps.Count, c.sequence, 1));
        }
        public void EndCamera(Camera camera)
        {
            if (stack.Count == 0 || stack[stack.Count - 1].camera != camera) { Abort(); return; }
            var scope = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
            for (int i = scope.optOuts.Count - 1; i >= 0; i--)
            {
                scope.optOuts[i].Dispose();
            }
            scope.optOuts.Clear();
            Shader.SetGlobalVector(Params, scope.previousParams); Shader.SetGlobalTexture(Metadata, scope.previousMetadata);
            Shader.SetGlobalVector(CaptureScope, scope.previousCapture);
            for (int i = 0; i < MaxLights; i++) Shader.SetGlobalTexture(Maps[i], scope.previousMaps[i]);
            scope.camera = null; scope.resources = null; scope.previousMetadata = null;
            Array.Clear(scope.previousMaps, 0, scope.previousMaps.Length);
            if (!HasActiveResources()) foreach (var hook in hooks.Values) hook.Detach();
        }
        public static void ClearGlobals() { Shader.SetGlobalVector(Params, Vector4.zero); }
        public void Abort() { while (stack.Count > 0) EndCamera(stack[stack.Count - 1].camera); foreach (var hook in hooks.Values) hook.Detach(); ClearGlobals(); Shader.SetGlobalVector(CaptureScope, Vector4.zero); }
        public void Dispose()
        {
            Abort(); foreach (var c in cameras.Values) c.Dispose(); cameras.Clear();
            foreach (var hook in hooks.Values) hook.Dispose(); hooks.Clear();
            if (copy) Destroy(copy); copy = null;
            targets.Dispose(); scopePool.Clear();
        }
        static void Destroy(Object value) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}

using System;
using System.Collections.Generic;
using TomRenderInfrastructure;
using UnityEngine;
using UnityEngine.Rendering;

namespace TomBSDFRuntime
{
    [Flags]
    public enum TomSceneBufferFlags
    {
        None = 0, Color = 1, Depth = 2,
        // Reserved capabilities are rejected, not silently treated as implemented.
        ReservedNormals = 4, ReservedScreenReflection = 8
    }

    public enum TomSceneBufferResolution { Half = 2, Full = 1 }

    public sealed class TomSceneBufferLease : IDisposable
    {
        internal TomBSDFSceneBuffers owner;
        internal int cameraID;
        internal TomSceneBufferFlags flags;
        internal int divisor;
        public bool IsActive { get { return owner != null && owner.IsLeaseActive(this); } }
        public void Dispose()
        {
            if (owner == null) return;
            var previous = owner; owner = null; previous.Release(this);
        }
    }

    public struct TomSceneBufferSnapshot
    {
        public RenderTexture color, depth;
        public TomSceneBufferFlags flags;
        public int width, height, cameraID, sequence;
        public bool orthographic, reversedZ;
        public float near, far;
        public Matrix4x4 inverseProjection, cameraToWorld;
    }

    // Main-thread service. Acquisition never starts a camera or creates a service.
    public sealed class TomBSDFSceneBuffers : IDisposable
    {
        public const int ProtocolVersion = 1;
        public const TomSceneBufferFlags Supported = TomSceneBufferFlags.Color | TomSceneBufferFlags.Depth;
        public const CameraEvent CaptureEvent = CameraEvent.BeforeForwardAlpha;
        public const string CommandName = "TomBSDF scene buffers v1";
        static readonly int Parameters = Shader.PropertyToID("_TomBSDFBufferParams");
        static readonly int Size = Shader.PropertyToID("_TomBSDFBufferSize");
        static readonly int Projection = Shader.PropertyToID("_TomBSDFBufferProjection");
        static readonly int InverseProjection = Shader.PropertyToID("_TomBSDFBufferInverseProjection");
        static readonly int CameraToWorld = Shader.PropertyToID("_TomBSDFBufferCameraToWorld");
        static readonly int Color = Shader.PropertyToID("_TomBSDFSceneColor");
        static readonly int Depth = Shader.PropertyToID("_TomBSDFSceneDepth");
        TomBSDFSceneBufferResources resources;
        Material depthMaterial;

        sealed class CameraResources
        {
            public Camera camera;
            public readonly List<TomSceneBufferLease> leases = new List<TomSceneBufferLease>();
            public RenderTexture color, depth;
            public RenderResourceLease colorLease, depthLease;
            public CommandBuffer command;
            public SceneColorRouteLease colorRoute;
            public long routeInvocation;
            public TomSceneBufferFlags flags;
            public int width, height, sequence;
            public bool scheduled, completed, requestedDepth;
            public DepthTextureMode previousDepth;
            public string status = "Waiting for camera";
            public Matrix4x4 inverseProjection, cameraToWorld;
            public Vector4 projection;
        }
        sealed class Scope
        {
            public Camera camera;
            public Vector4 parameters, size, projection;
            public Matrix4x4 inverseProjection, cameraToWorld;
            public Texture color, depth;
        }
        readonly Dictionary<int, CameraResources> cameras = new Dictionary<int, CameraResources>();
        readonly List<Scope> scopes = new List<Scope>();
        readonly List<int> dead = new List<int>();
        public bool IsDisposed { get; private set; }
        public int CameraCount { get { return cameras.Count; } }
        internal bool IsLeaseActive(TomSceneBufferLease lease)
        {
            CameraResources c;
            return !IsDisposed && cameras.TryGetValue(lease.cameraID, out c) && c.camera && c.leases.Contains(lease);
        }
        public bool NeedsDepth
        {
            get { foreach (var c in cameras.Values) foreach (var lease in c.leases) if ((lease.flags & TomSceneBufferFlags.Depth) != 0) return true; return false; }
        }
        public void SetResources(TomBSDFSceneBufferResources value)
        {
            if (resources == value) return;
            foreach (var c in cameras.Values) ReleaseResources(c);
            DestroyDepthMaterial(); resources = value; ClearGlobals();
        }
        public int TextureCount
        {
            get { int count = 0; foreach (var c in cameras.Values) { if (c.color) count++; if (c.depth) count++; } return count; }
        }

        public TomSceneBufferLease Acquire(Camera camera, TomSceneBufferFlags flags,
            TomSceneBufferResolution resolution = TomSceneBufferResolution.Full)
        {
            if (IsDisposed) throw new ObjectDisposedException("TomBSDFSceneBuffers");
            if (!camera) throw new ArgumentNullException("camera");
            if (flags == TomSceneBufferFlags.None || (flags & ~Supported) != 0) throw new ArgumentOutOfRangeException("flags");
            if (resolution != TomSceneBufferResolution.Full && resolution != TomSceneBufferResolution.Half)
                throw new ArgumentOutOfRangeException("resolution");
            int id = camera.GetInstanceID(); CameraResources resources;
            if (!cameras.TryGetValue(id, out resources)) cameras.Add(id, resources = new CameraResources { camera = camera });
            var lease = new TomSceneBufferLease { owner = this, cameraID = id, flags = flags, divisor = (int)resolution };
            resources.leases.Add(lease); resources.completed = false;
            return lease;
        }

        internal void Release(TomSceneBufferLease lease)
        {
            CameraResources resources;
            if (!cameras.TryGetValue(lease.cameraID, out resources)) return;
            resources.leases.Remove(lease);
            // Do not keep advertising last frame's broader capability set.
            ReleaseResources(resources);
            if (resources.leases.Count == 0) cameras.Remove(lease.cameraID);
            if (!NeedsDepth) DestroyDepthMaterial();
            // Releasing a child/idle camera lease must not erase an enclosing
            // camera's published buffers before the child's BeginCamera saves it.
            if (Shader.GetGlobalVector(Parameters).z == (float)lease.cameraID) ClearGlobals();
        }

        public string Status(Camera camera)
        {
            CameraResources resources;
            return camera && cameras.TryGetValue(camera.GetInstanceID(), out resources) ? resources.status : "No demand";
        }

        public bool TryGetSnapshot(Camera camera, out TomSceneBufferSnapshot snapshot)
        {
            snapshot = new TomSceneBufferSnapshot(); CameraResources c;
            if (IsDisposed || !camera || !cameras.TryGetValue(camera.GetInstanceID(), out c) || !c.completed) return false;
            if (((c.flags & TomSceneBufferFlags.Color) != 0 && (!c.color || !c.color.IsCreated()))
                || ((c.flags & TomSceneBufferFlags.Depth) != 0 && (!c.depth || !c.depth.IsCreated()))) return false;
            snapshot = new TomSceneBufferSnapshot { color = c.color, depth = c.depth, flags = c.flags,
                width = c.width, height = c.height, cameraID = camera.GetInstanceID(), sequence = c.sequence,
                near = c.projection.x, far = c.projection.y, orthographic = c.projection.z > .5f,
                reversedZ = c.projection.w > .5f, inverseProjection = c.inverseProjection, cameraToWorld = c.cameraToWorld };
            return true;
        }

        internal void PrepareCamera(Camera camera, bool enabled)
        {
            if (IsDisposed) return;
            dead.Clear(); foreach (var entry in cameras) if (!entry.Value.camera) dead.Add(entry.Key);
            foreach (int id in dead) { ReleaseResources(cameras[id]); cameras.Remove(id); }
            if (!enabled)
            {
                foreach (var entry in cameras.Values) { ReleaseResources(entry); entry.status = "Extension disabled"; }
                DestroyDepthMaterial();
                return;
            }
            CameraResources c;
            if (!camera || !cameras.TryGetValue(camera.GetInstanceID(), out c)) return;
            c.completed = false;
            string unsupported = Unsupported(camera);
            if (unsupported != null) { ReleaseResources(c); c.status = unsupported; return; }
            TomSceneBufferFlags flags = TomSceneBufferFlags.None; int divisor = 2;
            foreach (var lease in c.leases) { flags |= lease.flags; divisor = Math.Min(divisor, lease.divisor); }
            if ((flags & TomSceneBufferFlags.Depth) != 0 && (!resources || !resources.IsValid))
            {
                ReleaseResources(c); c.status = "Depth copy resource missing or incompatible"; return;
            }
            int width = Math.Max(1, (camera.pixelWidth + divisor - 1) / divisor);
            int height = Math.Max(1, (camera.pixelHeight + divisor - 1) / divisor);
            bool lost = (c.color && !c.color.IsCreated()) || (c.depth && !c.depth.IsCreated());
            if (c.flags != flags || c.width != width || c.height != height || lost) ReleaseResources(c);
            c.flags = flags; c.width = width; c.height = height;
            try
            {
                if ((flags & TomSceneBufferFlags.Color) != 0 && !c.color)
                {c.colorLease=CreateTexture(c,width,height,RenderTextureFormat.ARGBHalf,FilterMode.Bilinear,"color");c.color=c.colorLease.Texture;}
                if ((flags & TomSceneBufferFlags.Depth) != 0 && !c.depth)
                {c.depthLease=CreateTexture(c,width,height,RenderTextureFormat.RFloat,FilterMode.Point,"raw depth");c.depth=c.depthLease.Texture;}
                if (c.depth && !depthMaterial)
                    depthMaterial = new Material(resources.copyDepth) { hideFlags = HideFlags.HideAndDontSave };
                if ((flags & TomSceneBufferFlags.Depth) != 0)
                {
                    c.previousDepth = camera.depthTextureMode;
                    c.requestedDepth = (c.previousDepth & DepthTextureMode.Depth) == 0;
                    camera.depthTextureMode |= DepthTextureMode.Depth;
                }
                c.projection = new Vector4(camera.nearClipPlane, camera.farClipPlane,
                    camera.orthographic ? 1 : 0, SystemInfo.usesReversedZBuffer ? 1 : 0);
                c.inverseProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true).inverse;
                c.cameraToWorld = camera.cameraToWorldMatrix;
                if (c.command == null) { c.command = new CommandBuffer { name = CommandName }; camera.AddCommandBuffer(CaptureEvent, c.command); }
                if (c.colorRoute != null) c.colorRoute.Dispose();
                var context = UnityCameraHub.Coordinator.Find(camera);
                if(c.colorLease!=null)c.colorLease.Bind(context);if(c.depthLease!=null)c.depthLease.Bind(context);
                c.routeInvocation = context == null ? 0 : context.Id;
                c.colorRoute = UnityCameraHub.ColorRoutes.Subscribe(context, c, route => RebindColor(c, route));
                ++c.sequence; c.scheduled = true;
                RecordCommand(c);
                c.status = "Scheduled BeforeForwardAlpha";
            }
            catch (Exception e)
            {
                ReleaseResources(c); c.status = "Unavailable: " + e.Message;
            }
        }

        void RebindColor(CameraResources c, SceneColorRoute route)
        {
            if (!c.scheduled || c.command == null || route.invocation != c.routeInvocation) return;
            try { RecordCommand(c); }
            catch (Exception error) { ReleaseResources(c); c.status = "Unavailable color route: " + error.Message; }
        }
        void RecordCommand(CameraResources c)
        {
            c.command.Clear();
            var source = c.colorRoute != null && c.colorRoute.Route.source == SceneColorSource.OpaqueEffectsCurrentActive
                ? BuiltinRenderTextureType.CurrentActive : BuiltinRenderTextureType.CameraTarget;
            if (c.color) c.command.Blit(source, c.color);
            if (c.depth) c.command.Blit(Texture2D.blackTexture, c.depth, depthMaterial, 0);
            c.command.SetGlobalTexture(Color, c.color ? (Texture)c.color : Texture2D.blackTexture);
            c.command.SetGlobalTexture(Depth, c.depth ? (Texture)c.depth : Texture2D.blackTexture);
            c.command.SetGlobalVector(Size, new Vector4(c.width, c.height, 1f / c.width, 1f / c.height));
            c.command.SetGlobalVector(Projection, c.projection);
            c.command.SetGlobalMatrix(InverseProjection, c.inverseProjection);
            c.command.SetGlobalMatrix(CameraToWorld, c.cameraToWorld);
            c.command.SetGlobalVector(Parameters, new Vector4(ProtocolVersion, (int)c.flags, c.camera.GetInstanceID(), c.sequence));
            // Re-record the same not-yet-executed event buffer; never add or
            // execute another copy pass when an opaque effect completes.
            c.command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        }

        internal void BeginCamera(Camera camera)
        {
            if (IsDisposed) return;
            if (scopes.Count > 0 && !scopes[scopes.Count - 1].camera) scopes.Clear();
            // Save actual globals, including command-buffer publication by an
            // enclosing camera, rather than guessing whether it has captured.
            scopes.Add(new Scope { camera = camera, parameters = Shader.GetGlobalVector(Parameters),
                size = Shader.GetGlobalVector(Size), projection = Shader.GetGlobalVector(Projection),
                inverseProjection = Shader.GetGlobalMatrix(InverseProjection), cameraToWorld = Shader.GetGlobalMatrix(CameraToWorld),
                color = Shader.GetGlobalTexture(Color), depth = Shader.GetGlobalTexture(Depth) });
            ClearGlobals();
        }

        internal void EndCamera(Camera camera)
        {
            EndCamera(camera, false);
        }
        internal void EndCamera(Camera camera, bool aborted)
        {
            if (IsDisposed) return;
            bool ownsScope = scopes.Count > 0 && scopes[scopes.Count - 1].camera == camera;
            CameraResources c;
            if (camera && cameras.TryGetValue(camera.GetInstanceID(), out c))
            {
                var published = Shader.GetGlobalVector(Parameters);
                c.completed = !aborted && ownsScope && c.scheduled && published.x == ProtocolVersion
                    && published.z == (float)camera.GetInstanceID() && published.w == (float)c.sequence;
                c.scheduled = false;
                RestoreDepthRequest(c);
                if (c.colorRoute != null) { c.colorRoute.Dispose(); c.colorRoute = null; }
                if (aborted) { ReleaseResources(c); c.status = "Aborted camera capture"; }
                if (c.completed) c.status = "Captured";
                else if (!aborted && c.command != null) c.status = "Capture event did not publish current-camera resources";
            }
            // A failed Prepare has no Begin scope of its own. In particular it
            // must not pop an enclosing camera's saved global bindings.
            if (!ownsScope) { if (scopes.Count == 0) ClearGlobals(); return; }
            if (scopes.Count == 0) { ClearGlobals(); return; }
            var scope = scopes[scopes.Count - 1]; scopes.RemoveAt(scopes.Count - 1);
            if (scope.camera != camera || scopes.Count == 0) { scopes.Clear(); ClearGlobals(); return; }
            Shader.SetGlobalTexture(Color, scope.color ? scope.color : Texture2D.blackTexture);
            Shader.SetGlobalTexture(Depth, scope.depth ? scope.depth : Texture2D.blackTexture);
            Shader.SetGlobalVector(Size, scope.size); Shader.SetGlobalVector(Projection, scope.projection);
            Shader.SetGlobalMatrix(InverseProjection, scope.inverseProjection); Shader.SetGlobalMatrix(CameraToWorld, scope.cameraToWorld);
            Shader.SetGlobalVector(Parameters, scope.parameters);
        }

        static string Unsupported(Camera camera)
        {
            if(CameraRoles.IsAuxiliaryCapture(camera))return "Auxiliary capture excluded";
            if (camera.actualRenderingPath != RenderingPath.Forward) return "Unsupported rendering path";
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11) return "Unsupported graphics API";
            if (camera.stereoEnabled) return "Stereo is reserved";
            if (camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview) return "Auxiliary camera excluded";
            if (camera.rect != new Rect(0, 0, 1, 1)) return "Sub-rect cameras are reserved";
            if (Mathf.Abs(camera.projectionMatrix.m20) > 1e-6f || Mathf.Abs(camera.projectionMatrix.m21) > 1e-6f)
                return "Oblique near planes are reserved";
            if (camera.targetTexture && (camera.targetTexture.dimension != TextureDimension.Tex2D || camera.targetTexture.useDynamicScale))
                return "Array/dynamic-resolution targets are reserved";
            if (QualitySettings.activeColorSpace != ColorSpace.Linear && (!camera.targetTexture || camera.targetTexture.sRGB))
                return "Requires KKS Linear host or an explicit linear Editor target";
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)) return "Required floating-point formats unavailable";
            return null;
        }

        static RenderResourceLease CreateTexture(object owner,int width, int height, RenderTextureFormat format, FilterMode filter, string kind)
        {
            return RenderResourceLease.Create(owner,new RenderResourceDescriptor{width=width,height=height,format=format,filter=filter,
                linear=true,purpose="TomBSDF scene "+kind,stage="BeforeForwardAlpha"});
        }
        static void RestoreDepthRequest(CameraResources c)
        {
            // Depth flags have no Unity ownership counter. Only undo our own bit,
            // and only if nobody changed any camera flags during this render.
            if (c.camera && c.requestedDepth && c.camera.depthTextureMode == (c.previousDepth | DepthTextureMode.Depth))
                c.camera.depthTextureMode = c.previousDepth;
            c.requestedDepth = false;
        }
        static void ReleaseResources(CameraResources c)
        {
            if (c.colorRoute != null) { c.colorRoute.Dispose(); c.colorRoute = null; }
            RestoreDepthRequest(c);
            if (c.command != null) { if (c.camera) c.camera.RemoveCommandBuffer(CaptureEvent, c.command); c.command.Release(); c.command = null; }
            if(c.colorLease!=null)c.colorLease.Dispose();if(c.depthLease!=null)c.depthLease.Dispose();
            c.colorLease=c.depthLease=null;c.color = null; c.depth = null;
            c.flags = TomSceneBufferFlags.None; c.scheduled = false; c.completed = false;
        }
        void DestroyDepthMaterial()
        {
            if (!depthMaterial) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(depthMaterial); else UnityEngine.Object.DestroyImmediate(depthMaterial);
            depthMaterial = null;
        }
        public static void ClearGlobals()
        {
            Shader.SetGlobalVector(Parameters, Vector4.zero); Shader.SetGlobalVector(Size, Vector4.zero);
            Shader.SetGlobalVector(Projection, Vector4.zero);
            Shader.SetGlobalMatrix(InverseProjection, Matrix4x4.identity); Shader.SetGlobalMatrix(CameraToWorld, Matrix4x4.identity);
            Shader.SetGlobalTexture(Color, Texture2D.blackTexture); Shader.SetGlobalTexture(Depth, Texture2D.blackTexture);
        }
        internal void Abort()
        {
            if (IsDisposed) return;
            bool pending = scopes.Count > 0;
            foreach (var c in cameras.Values) if (c.scheduled)
            {
                pending = true; ReleaseResources(c); c.status = "Aborted camera capture";
            }
            scopes.Clear();
            if (pending) ClearGlobals();
        }
        public void Dispose()
        {
            if (IsDisposed) return;
            foreach (var c in cameras.Values) ReleaseResources(c);
            cameras.Clear(); scopes.Clear(); DestroyDepthMaterial(); ClearGlobals(); IsDisposed = true;
        }
    }
}

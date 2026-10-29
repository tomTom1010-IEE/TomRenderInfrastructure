using System;
using UnityEngine;

namespace TomRenderInfrastructure
{
    // One process-wide callback bridge. Allocation remains explicit and leased.
    public static class UnityCameraHub
    {
        public const int ProtocolVersion = 1;
        static readonly RenderCoordinator coordinator = new RenderCoordinator();
        static readonly SceneColorRouter colorRoutes = new SceneColorRouter(coordinator);
        static bool attached;
        static TomRenderDriver driver;
        static float nextWarning;
        public static RenderCoordinator Coordinator { get { return coordinator; } }
        public static SceneColorRouter ColorRoutes { get { return colorRoutes; } }
        public static int SceneColorProtocol { get { return SceneColorRouter.ProtocolVersion; } }
        public static bool IsAttached { get { return attached; } }
        public static int OwnedGpuResources { get { return RenderResourceLease.Count + RendererBoundsBroker.MeshCount; } }

        sealed class Lease : IDisposable
        {
            IDisposable registration;
            internal Lease(IDisposable value) { registration = value; }
            public void Dispose()
            {
                if (registration == null) return;
                registration.Dispose(); registration = null; DetachIfIdle();
            }
        }

        public static IDisposable Register(object owner, string name, int order,
            Action<RenderContext> prepare, Action<RenderContext> begin, Action<RenderContext, bool> end)
        {
            var registration = coordinator.Register(owner, name, order, prepare, begin, end);
            try { Attach(); return new Lease(registration); }
            catch { registration.Dispose(); DetachIfIdle(); throw; }
        }

        public static IDisposable Observe(object owner, string name, Action<RenderContext, RenderPhase> observer)
        {
            if (observer == null) throw new ArgumentNullException("observer");
            return Register(owner, name, int.MaxValue, c => observer(c, RenderPhase.Prepared),
                c => observer(c, RenderPhase.Rendering), (c, abort) => observer(c, abort ? RenderPhase.Aborted : RenderPhase.Completed));
        }

        public static Camera Camera(RenderContext context) { return context == null ? null : context.CameraObject as Camera; }

        static void Attach()
        {
            if (attached) return;
            UnityEngine.Camera.onPreCull += Prepare;
            UnityEngine.Camera.onPreRender += Begin;
            UnityEngine.Camera.onPostRender += End;
            attached = true;
            coordinator.Faulted = Warn;
            try
            {
                var go = new GameObject("Tom Render Infrastructure") { hideFlags = HideFlags.HideAndDontSave };
                if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(go);
                driver = go.AddComponent<TomRenderDriver>();
                SceneDiscovery.SetPersistentScene(go.scene);
            }
            catch { Detach(); throw; }
        }

        static void Warn(string owner, Exception error)
        {
            if (Time.realtimeSinceStartup < nextWarning) return;
            nextWarning = Time.realtimeSinceStartup + 5;
            Debug.LogWarning("[TomRender] Participant failed and its scope was unwound: " + owner + ": " + error.GetBaseException().Message);
        }

        static void Prepare(Camera camera)
        {
            if (!camera) return;
            var role = camera.cameraType == CameraType.Reflection ? CameraRole.Reflection
                : camera.cameraType == CameraType.Preview ? CameraRole.Preview
                : camera.cameraType == CameraType.SceneView ? CameraRole.SceneView : CameraRole.View;
            role=CameraRoles.Resolve(camera,role);
            coordinator.Prepare(camera, camera.GetInstanceID(), Time.frameCount,
                camera.targetTexture ? camera.targetTexture.GetInstanceID() : 0, camera.pixelWidth, camera.pixelHeight, role);
        }
        static void Begin(Camera camera) { if (camera) coordinator.Begin(camera); }
        static void End(Camera camera) { if (camera) coordinator.End(camera); DetachIfIdle(); }

        internal static void Tick()
        {
            // Camera.Render is synchronous; leftover scopes at LateUpdate belong
            // to an interrupted earlier render, never to an asynchronous camera.
            coordinator.AbortAll();
            coordinator.CollectCameras(value => value is Camera && (Camera)value);
            RenderResourceLease.CollectDeadCameras();
            RendererBoundsBroker.CollectDestroyedRenderers();
            SceneDiscovery.Pump();
            DetachIfIdle();
        }
        internal static void DiscoveryChanged()
        {
            if (SceneDiscovery.IsNeeded) Attach(); else DetachIfIdle();
        }
        static void DetachIfIdle()
        {
            if (coordinator.RegistrationCount != 0 || coordinator.ActiveDepth != 0 || SceneDiscovery.IsNeeded) return;
            Detach();
            coordinator.CollectCameras(value => false);
            var previous = driver; driver = null;
            if (previous)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(previous.gameObject);
                else UnityEngine.Object.DestroyImmediate(previous.gameObject);
            }
        }
        static void Detach()
        {
            if (!attached) return;
            UnityEngine.Camera.onPreCull -= Prepare;
            UnityEngine.Camera.onPreRender -= Begin;
            UnityEngine.Camera.onPostRender -= End;
            attached = false;
        }
        internal static void DriverDestroyed(TomRenderDriver value)
        {
            if (driver != value) return;
            driver = null; coordinator.AbortAll(); Detach();
        }
    }

    [ExecuteAlways]
    public sealed class TomRenderDriver : MonoBehaviour
    {
        void LateUpdate() { UnityCameraHub.Tick(); }
        void OnDestroy() { UnityCameraHub.DriverDestroyed(this); }
    }
}

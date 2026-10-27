using System;
using System.Collections.Generic;
using System.Reflection;
using TomRenderInfrastructure;
using UnityEngine;
using UnityEngine.Rendering;

namespace TomSceneEffects
{
    public enum DofMode { Native = 0, BeforeTransparents = 1 }
    public enum DofScenePolicy { Inherit = -1, Native = 0, BeforeTransparents = 1 }

    public static class DofPolicy
    {
        public static DofMode Resolve(DofMode global, DofScenePolicy scene)
        {
            if (!Enum.IsDefined(typeof(DofMode), global) || !Enum.IsDefined(typeof(DofScenePolicy), scene))
                throw new ArgumentOutOfRangeException("DOF policy");
            return scene == DofScenePolicy.Inherit ? global : (DofMode)(int)scene;
        }
    }

    // Shared Unity source, also linked by the standalone plugin runtime project.
    // The game adapter owns the single native-method patch.
    public sealed class DofBinding : IDisposable
    {
        static readonly Dictionary<MonoBehaviour, DofBinding> Bindings = new Dictionary<MonoBehaviour, DofBinding>();
        readonly Action<RenderTexture, RenderTexture> invoke;
        public readonly Camera Camera;
        public readonly MonoBehaviour Native;
        public readonly TomDofOpaqueRelay Relay;
        public DofMode Mode { get; private set; }
        public int Sequence { get; private set; }
        public int EarlyExecutions { get; private set; }
        public int LateCopies { get; private set; }
        public int NativeExecutions { get; private set; }
        public int Failures { get; private set; }
        public string Status { get; private set; }
        public bool EarlySucceeded { get; private set; }
        public bool Disposed { get; private set; }
        public Action<string> Observe;
        public Action<Exception> Failed;
        int invoking, lateBudget;
        bool useEarly, faulted;
        RenderContext renderContext;

        public DofBinding(Camera camera, MonoBehaviour native, MethodInfo renderImage)
        {
            if (!camera || !native || native.gameObject != camera.gameObject) throw new ArgumentException("Camera-local native effect required");
            if (renderImage == null || renderImage.ReturnType != typeof(void)) throw new ArgumentException("OnRenderImage unavailable");
            if (Bindings.ContainsKey(native) || camera.GetComponent<TomDofOpaqueRelay>()) throw new InvalidOperationException("Camera already has a DOF provider");
            invoke = (Action<RenderTexture, RenderTexture>)Delegate.CreateDelegate(typeof(Action<RenderTexture, RenderTexture>), native, renderImage);
            Camera = camera; Native = native;
            Relay = camera.gameObject.AddComponent<TomDofOpaqueRelay>();
            Relay.hideFlags = HideFlags.HideAndDontSave;
            Relay.Owner = this; Relay.enabled = false;
            Bindings.Add(native, this);
            Status = "Native";
        }

        public void SetMode(DofMode mode)
        {
            if (mode != DofMode.Native && mode != DofMode.BeforeTransparents) throw new ArgumentOutOfRangeException("mode");
            Mode = mode;
            Synchronize();
        }

        public void Synchronize()
        {
            if (Disposed || !Relay) return;
            bool wanted = Mode == DofMode.BeforeTransparents && Native && Native.isActiveAndEnabled && !faulted;
            if (Relay.enabled != wanted) Relay.enabled = wanted;
        }

        // Called for every render, including repeated Camera.Render in one frame.
        public void PrepareRender()
        {
            if (Disposed) return;
            ++Sequence; EarlySucceeded = false; lateBudget = 0;
            renderContext = UnityCameraHub.Coordinator.Find(Camera);
            Synchronize();
            useEarly = Relay && Relay.isActiveAndEnabled && Camera && !Camera.stereoEnabled
                && !CameraRoles.IsAuxiliaryCapture(Camera)
                && Camera.cameraType != CameraType.Reflection && Camera.cameraType != CameraType.Preview
                && Camera.actualRenderingPath == RenderingPath.Forward;
            if (!useEarly && Relay && Relay.enabled) Relay.enabled = false;
            Status = faulted ? "Native: relay fault (rebind to retry)" : useEarly ? "Early scheduled" : "Native";
        }

        internal void RenderEarly(RenderTexture source, RenderTexture destination)
        {
            if (!useEarly || Disposed || !Native || !Native.isActiveAndEnabled)
            {
                Graphics.Blit(source, destination);
                return;
            }
            try
            {
                if (Observe != null) Observe("DOF opaque relay");
                ++invoking;
                invoke(source, destination);
                EarlySucceeded = true; lateBudget = 1; ++EarlyExecutions;
                Status = "Early complete; awaiting native late passthrough";
                UnityCameraHub.ColorRoutes.PublishOpaqueResult(renderContext, this, "Tom native DOF opaque relay");
            }
            catch (Exception error)
            {
                faulted = true; ++Failures; useEarly = false; lateBudget = 0;
                Graphics.Blit(source, destination);
                Status = "Native fallback: " + error.GetBaseException().Message;
                if (Failed != null) Failed(error.GetBaseException());
            }
            finally { --invoking; }
        }

        // Prefix contract: false means destination has already been fully written.
        public static bool BeforeNative(MonoBehaviour effect, RenderTexture source, RenderTexture destination)
        {
            DofBinding binding;
            if (!effect || !Bindings.TryGetValue(effect, out binding) || binding.Disposed) return true;
            if (binding.invoking > 0) { ++binding.NativeExecutions; return true; }
            if (binding.EarlySucceeded && binding.lateBudget > 0)
            {
                --binding.lateBudget;
                Graphics.Blit(source, destination);
                ++binding.LateCopies;
                binding.Status = "Early DOF + one native late passthrough";
                if (binding.Observe != null) binding.Observe("Native late passthrough");
                return false;
            }
            ++binding.NativeExecutions;
            if (binding.Observe != null) binding.Observe("Native late DOF");
            return true;
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true; useEarly = false; lateBudget = 0;
            Bindings.Remove(Native);
            renderContext = null;
            if (Relay)
            {
                Relay.enabled = false; Relay.Owner = null;
                if (Application.isPlaying) UnityEngine.Object.Destroy(Relay); else UnityEngine.Object.DestroyImmediate(Relay);
            }
            Observe = null; Failed = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TomBSDFRuntime
{
    // Optional public API binding: no hard Screenshot Manager or Harmony dependency.
    // This class is also exercised with a public-event fixture in the Editor.
    public sealed class ScreenshotQualityAdapter : IDisposable
    {
        EventInfo before, after;
        readonly Stack<IDisposable> captures = new Stack<IDisposable>();
        readonly Action pre, post;
        readonly Func<Camera> camera;
        public bool Connected { get { return before != null && after != null; } }
        public ScreenshotQualityAdapter(Func<Camera> captureCamera = null)
        {
            camera = captureCamera ?? (() => Camera.main);
            pre = Begin; post = End;
        }

        public bool Connect(Type type)
        {
            Dispose();
            if (type == null) return false;
            var a = type.GetEvent("OnPreCapture", BindingFlags.Public | BindingFlags.Static);
            var b = type.GetEvent("OnPostCapture", BindingFlags.Public | BindingFlags.Static);
            if (a == null || b == null || a.EventHandlerType != typeof(Action) || b.EventHandlerType != typeof(Action)) return false;
            try
            {
                a.AddEventHandler(null, pre); before = a;
                b.AddEventHandler(null, post); after = b;
                return true;
            }
            catch { Dispose(); return false; }
        }

        void Begin()
        {
            // VideoExport brackets the entire recording, not each frame. Nested
            // captures must restore that enclosing recording lease, not cancel it.
            captures.Push(TomBSDFCaptureQuality.Begin(camera()));
        }
        void End() { if (captures.Count > 0) captures.Pop().Dispose(); }
        public void Dispose()
        {
            while (captures.Count > 0) End();
            if (before != null) before.RemoveEventHandler(null, pre);
            if (after != null) after.RemoveEventHandler(null, post);
            before = after = null;
        }
    }
}

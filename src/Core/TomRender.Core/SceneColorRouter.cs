using System;
using System.Collections.Generic;

namespace TomRenderInfrastructure
{
    public enum SceneColorSource { NativeCameraTarget = 0, OpaqueEffectsCurrentActive = 1 }

    public struct SceneColorRoute
    {
        public long invocation;
        public int camera, generation, target;
        public SceneColorSource source;
        public string provider;
    }

    public sealed class SceneColorRouteLease : IDisposable
    {
        internal SceneColorRouter owner;
        internal long invocation;
        internal object consumer;
        internal Action<SceneColorRoute> changed;
        internal SceneColorRoute route;
        public bool IsActive { get { return owner != null; } }
        public SceneColorRoute Route { get { return route; } }
        public void Dispose()
        {
            if (owner == null) return;
            var previous = owner; owner = null; changed = null; consumer = null;
            previous.Release(this);
        }
        internal void Close() { owner = null; changed = null; consumer = null; }
    }

    // CPU routing metadata only: no RenderTexture ownership, GPU commands or
    // implicit camera rendering. A render invocation, not a frame number, owns it.
    public sealed class SceneColorRouter : IDisposable
    {
        public const int ProtocolVersion = 1;
        sealed class Entry
        {
            internal RenderContext context;
            internal object provider;
            internal SceneColorRoute route;
            internal readonly List<SceneColorRouteLease> consumers = new List<SceneColorRouteLease>();
        }
        readonly RenderCoordinator coordinator;
        readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
        bool disposed;
        public int EntryCount { get { return entries.Count; } }
        public int LeaseCount { get { int n = 0; foreach (var entry in entries.Values) n += entry.consumers.Count; return n; } }
        public long Publications { get; private set; }
        public long ConsumerFaults { get; private set; }
        public Action<Exception> ConsumerFailed;

        public SceneColorRouter(RenderCoordinator coordinator)
        {
            if (coordinator == null) throw new ArgumentNullException("coordinator");
            this.coordinator = coordinator;
            coordinator.Closed += End;
        }

        bool Active(RenderContext context)
        {
            return !disposed && context != null && coordinator.Find(context.CameraObject) == context
                && (context.Phase == RenderPhase.Prepared || context.Phase == RenderPhase.Rendering);
        }
        Entry Get(RenderContext context)
        {
            Entry entry;
            if (!entries.TryGetValue(context.Id, out entry))
                entries.Add(context.Id, entry = new Entry { context = context, route = new SceneColorRoute {
                    invocation = context.Id, camera = context.CameraId, generation = context.CameraGeneration,
                    target = context.TargetId, source = SceneColorSource.NativeCameraTarget } });
            return entry;
        }

        public SceneColorRouteLease Subscribe(RenderContext context, object consumer, Action<SceneColorRoute> changed)
        {
            if (consumer == null) throw new ArgumentNullException("consumer");
            if (!Active(context)) return new SceneColorRouteLease();
            var entry = Get(context);
            foreach (var existing in entry.consumers)
                if (ReferenceEquals(existing.consumer, consumer)) throw new InvalidOperationException("Scene-color consumer already registered for this invocation");
            var lease = new SceneColorRouteLease { owner = this, invocation = context.Id, consumer = consumer,
                changed = changed, route = entry.route };
            entry.consumers.Add(lease);
            return lease;
        }

        public bool PublishOpaqueResult(RenderContext context, object provider, string name)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            if (!Active(context) || coordinator.Current != context || context.Phase != RenderPhase.Rendering) return false;
            var entry = Get(context);
            if (entry.provider != null) return ReferenceEquals(entry.provider, provider);
            entry.provider = provider;
            entry.route.source = SceneColorSource.OpaqueEffectsCurrentActive;
            entry.route.provider = name;
            ++Publications;
            foreach (var lease in entry.consumers.ToArray())
            {
                if (!lease.IsActive) continue;
                lease.route = entry.route;
                try { if (lease.changed != null) lease.changed(entry.route); }
                catch (Exception error)
                {
                    ++ConsumerFaults; lease.Dispose();
                    try { if (ConsumerFailed != null) ConsumerFailed(error); } catch { }
                }
            }
            return true;
        }

        internal void Release(SceneColorRouteLease lease)
        {
            Entry entry;
            if (!entries.TryGetValue(lease.invocation, out entry)) return;
            entry.consumers.Remove(lease);
            if (entry.consumers.Count == 0 && entry.provider == null) entries.Remove(lease.invocation);
        }
        void End(RenderContext context, bool aborted)
        {
            Entry entry;
            if (!entries.TryGetValue(context.Id, out entry)) return;
            foreach (var lease in entry.consumers) lease.Close();
            entries.Remove(context.Id);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; coordinator.Closed -= End;
            foreach (var entry in entries.Values) foreach (var lease in entry.consumers) lease.Close();
            entries.Clear(); ConsumerFailed = null;
        }
    }
}

using System;
using System.Collections.Generic;

namespace TomRenderInfrastructure
{
    [Flags]
    public enum CameraRole { View = 1, SceneView = 2, Preview = 4, Reflection = 8, Probe = 16, Capture = 32 }
    public enum RenderPhase { Prepared, Rendering, Completed, Aborted, CallbackFault, Registered, Unregistered }

    public sealed class RenderContext
    {
        public object CameraObject { get; internal set; }
        public long Id { get; internal set; }
        public long ParentId { get; internal set; }
        public int CameraId { get; internal set; }
        public int CameraGeneration { get; internal set; }
        public int Frame { get; internal set; }
        public int TargetId { get; internal set; }
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public int Depth { get; internal set; }
        public CameraRole Role { get; internal set; }
        public RenderPhase Phase { get; internal set; }
    }

    public struct RenderTrace
    {
        public long invocation, parent;
        public int camera, generation, frame, depth;
        public RenderPhase phase;
        public string owner, error;
    }

    public sealed class RenderStatistics
    {
        public long prepared, started, completed, aborted, callbackFaults, duplicateRegistrations, mismatchedEnds;
        public int registrations, activeDepth, maxDepth, cameraRecords;
    }

    // Pure managed state machine. It neither subscribes to Unity nor owns GPU work.
    public sealed class RenderCoordinator
    {
        sealed class Registration : IDisposable
        {
            internal RenderCoordinator coordinator;
            internal object owner;
            internal string name;
            internal int order;
            internal long ordinal;
            internal bool active = true;
            internal Action<RenderContext> prepare, begin;
            internal Action<RenderContext, bool> end;
            public void Dispose() { if (active) coordinator.Remove(this); }
        }
        sealed class Participant
        {
            internal Registration registration;
            internal bool prepared, begun, finished;
        }
        sealed class Invocation
        {
            internal RenderContext context;
            internal bool closing;
            internal readonly List<Participant> participants = new List<Participant>();
        }
        sealed class CameraIdentity
        {
            internal WeakReference camera;
            internal int generation;
        }
        readonly List<Registration> registrations = new List<Registration>();
        readonly List<Invocation> stack = new List<Invocation>();
        readonly Dictionary<int, CameraIdentity> cameras = new Dictionary<int, CameraIdentity>();
        readonly List<int> expiredCameras = new List<int>();
        readonly List<Registration> removed = new List<Registration>();
        readonly RenderTrace[] trace;
        readonly RenderStatistics counters = new RenderStatistics();
        long sequence, ordinal;
        int generation, tracePosition, traceCount, dispatchDepth;
        bool draining, abortRequested;
        public bool TraceEnabled { get; set; }
        public Action<string, Exception> Faulted;
        public event Action<RenderContext, bool> Closed;
        public int ActiveDepth { get { return stack.Count; } }
        public int RegistrationCount { get { return registrations.Count; } }
        public RenderContext Current { get { return stack.Count == 0 ? null : stack[stack.Count - 1].context; } }

        public RenderCoordinator(int traceCapacity = 512)
        {
            if (traceCapacity < 0 || traceCapacity > 65536) throw new ArgumentOutOfRangeException("traceCapacity");
            trace = new RenderTrace[traceCapacity];
        }

        public IDisposable Register(object owner, string name, int order,
            Action<RenderContext> prepare, Action<RenderContext> begin, Action<RenderContext, bool> end)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Participant name required", "name");
            foreach (var entry in registrations) if (ReferenceEquals(entry.owner, owner))
            { ++counters.duplicateRegistrations; throw new InvalidOperationException("Owner already registered: " + name); }
            var registration = new Registration { coordinator = this, owner = owner, name = name,
                order = order, ordinal = ++ordinal, prepare = prepare, begin = begin, end = end };
            registrations.Add(registration);
            registrations.Sort((a,b) => a.order != b.order ? a.order.CompareTo(b.order) : a.ordinal.CompareTo(b.ordinal));
            Record(null, RenderPhase.Registered, name, null);
            return registration;
        }

        void Remove(Registration registration)
        {
            registration.active = false;
            registrations.Remove(registration);
            removed.Add(registration);
            if (dispatchDepth == 0) DrainRemoved();
        }

        void DrainRemoved()
        {
            if (draining) return;
            draining = true;
            try
            {
                while (removed.Count > 0)
                {
                    var registration = removed[0]; removed.RemoveAt(0);
                    for (int i = stack.Count - 1; i >= 0; --i)
                        for (int n = stack[i].participants.Count - 1; n >= 0; --n)
                            if (stack[i].participants[n].registration == registration)
                                Finish(stack[i], stack[i].participants[n], true);
                    Record(null, RenderPhase.Unregistered, registration.name, null);
                }
            }
            finally { draining = false; }
            if (abortRequested) { abortRequested = false; AbortAll(); }
        }

        public RenderContext Prepare(object camera, int cameraId, int frame, int targetId,
            int width, int height, CameraRole role)
        {
            if (camera == null) throw new ArgumentNullException("camera");
            if (stack.Count >= 64) throw new InvalidOperationException("Render nesting limit exceeded");
            CameraIdentity identity;
            if (!cameras.TryGetValue(cameraId, out identity) || !ReferenceEquals(identity.camera.Target, camera))
                cameras[cameraId] = identity = new CameraIdentity { camera = new WeakReference(camera), generation = ++generation };
            var context = new RenderContext { CameraObject = camera, CameraId = cameraId, CameraGeneration = identity.generation,
                Id = ++sequence, ParentId = Current == null ? 0 : Current.Id, Frame = frame, TargetId = targetId,
                Width = width, Height = height, Role = role, Depth = stack.Count + 1, Phase = RenderPhase.Prepared };
            var invocation = new Invocation { context = context };
            foreach (var registration in registrations) invocation.participants.Add(new Participant { registration = registration });
            stack.Add(invocation); ++counters.prepared; counters.maxDepth = Math.Max(counters.maxDepth, stack.Count);
            Record(context, RenderPhase.Prepared, null, null);
            ++dispatchDepth;
            try
            {
                foreach (var participant in invocation.participants)
                {
                    if (!participant.registration.active || participant.finished) continue;
                    participant.prepared = true;
                    try { if (participant.registration.prepare != null) participant.registration.prepare(context); }
                    catch (Exception error) { Fault(invocation, participant, error); }
                }
            }
            finally { if (--dispatchDepth == 0) DrainRemoved(); }
            return context;
        }

        public bool Begin(object camera)
        {
            var invocation = Top(camera);
            if (invocation == null || invocation.context.Phase != RenderPhase.Prepared) return false;
            invocation.context.Phase = RenderPhase.Rendering; ++counters.started;
            Record(invocation.context, RenderPhase.Rendering, null, null);
            ++dispatchDepth;
            try
            {
                foreach (var participant in invocation.participants)
                {
                    if (!participant.prepared || participant.finished || !participant.registration.active) continue;
                    participant.begun = true;
                    try { if (participant.registration.begin != null) participant.registration.begin(invocation.context); }
                    catch (Exception error) { Fault(invocation, participant, error); }
                }
            }
            finally { if (--dispatchDepth == 0) DrainRemoved(); }
            return true;
        }

        public bool End(object camera)
        {
            int index = stack.Count - 1;
            while (index >= 0 && !ReferenceEquals(stack[index].context.CameraObject, camera)) --index;
            if (index < 0) { ++counters.mismatchedEnds; return false; }
            for (int i = index; i < stack.Count; ++i)
                if (stack[i].closing) { ++counters.mismatchedEnds; return false; }
            while (stack.Count - 1 > index) CloseTop(true);
            CloseTop(false);
            return true;
        }

        public RenderContext Find(object camera)
        {
            for (int i = stack.Count - 1; i >= 0; --i)
                if (ReferenceEquals(stack[i].context.CameraObject, camera)) return stack[i].context;
            return null;
        }

        Invocation Top(object camera)
        {
            return stack.Count > 0 && ReferenceEquals(stack[stack.Count - 1].context.CameraObject, camera) ? stack[stack.Count - 1] : null;
        }

        void CloseTop(bool aborted)
        {
            var invocation = stack[stack.Count - 1];
            invocation.closing = true;
            ++dispatchDepth;
            try
            {
                for (int i = invocation.participants.Count - 1; i >= 0; --i) Finish(invocation, invocation.participants[i], aborted);
            }
            finally
            {
                // End callbacks can render nested cameras; those must unwind first.
                while (stack.Count > 0 && stack[stack.Count - 1] != invocation) CloseTop(true);
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                invocation.context.Phase = aborted ? RenderPhase.Aborted : RenderPhase.Completed;
                if (aborted) ++counters.aborted; else ++counters.completed;
                Record(invocation.context, invocation.context.Phase, null, null);
                if (Closed != null)
                {
                    try { Closed(invocation.context, aborted); }
                    catch (Exception error) { ReportFault(invocation.context, "Core scope closure", error); }
                }
                if (--dispatchDepth == 0) DrainRemoved();
            }
        }

        void Finish(Invocation invocation, Participant participant, bool aborted)
        {
            if (!participant.prepared || participant.finished) return;
            participant.finished = true;
            try { if (participant.registration.end != null) participant.registration.end(invocation.context, aborted); }
            catch (Exception error) { ReportFault(invocation.context, participant.registration.name, error); }
        }
        void Fault(Invocation invocation, Participant participant, Exception error)
        {
            ReportFault(invocation.context, participant.registration.name, error);
            Finish(invocation, participant, true);
        }
        void ReportFault(RenderContext context, string owner, Exception error)
        {
            ++counters.callbackFaults;
            Record(context, RenderPhase.CallbackFault, owner, error.GetBaseException().Message);
            try { if (Faulted != null) Faulted(owner, error); } catch { }
        }

        public void AbortAll()
        {
            if (dispatchDepth > 0) { abortRequested = true; return; }
            while (stack.Count > 0) CloseTop(true);
        }
        public void CollectCameras(Func<object, bool> alive)
        {
            if (stack.Count != 0) return;
            expiredCameras.Clear();
            foreach (var pair in cameras)
            {
                var camera = pair.Value.camera.Target;
                if (camera == null || alive != null && !alive(camera)) expiredCameras.Add(pair.Key);
            }
            foreach (int id in expiredCameras) cameras.Remove(id);
        }
        public RenderStatistics Statistics()
        {
            return new RenderStatistics { prepared=counters.prepared, started=counters.started, completed=counters.completed,
                aborted=counters.aborted, callbackFaults=counters.callbackFaults, duplicateRegistrations=counters.duplicateRegistrations,
                mismatchedEnds=counters.mismatchedEnds, registrations=registrations.Count, activeDepth=stack.Count,
                maxDepth=counters.maxDepth, cameraRecords=cameras.Count };
        }
        void Record(RenderContext context, RenderPhase phase, string owner, string error)
        {
            if (!TraceEnabled || trace.Length == 0) return;
            trace[tracePosition] = new RenderTrace { invocation=context == null ? 0 : context.Id,
                parent=context == null ? 0 : context.ParentId, camera=context == null ? 0 : context.CameraId,
                generation=context == null ? 0 : context.CameraGeneration, frame=context == null ? 0 : context.Frame,
                depth=context == null ? 0 : context.Depth, phase=phase, owner=owner, error=error };
            tracePosition = (tracePosition + 1) % trace.Length; traceCount = Math.Min(trace.Length, traceCount + 1);
        }
        public RenderTrace[] ReadTrace()
        {
            var result = new RenderTrace[traceCount];
            for (int i = 0; i < result.Length; ++i) result[i] = trace[(tracePosition - traceCount + i + trace.Length) % trace.Length];
            return result;
        }
    }
}

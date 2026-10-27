using System;
using System.Collections.Generic;

namespace TomRenderInfrastructure
{
    // Capture lifetime spans faces and frames, unlike a camera invocation.
    // Existing modules keep selection, budgets, timeout and publish algorithms.
    public static class CaptureJobs
    {
        public sealed class Lease : IDisposable
        {
            public string Lane { get; internal set; }
            public long Id { get; internal set; }
            public double StartedAt { get; internal set; }
            public int SampleBudget { get; internal set; }
            public int Steps { get; private set; }
            public bool IsActive { get; private set; }
            internal Lease() { IsActive=true; }
            public void Step() { if(IsActive)++Steps; }
            public void Dispose() { if(!IsActive)return;IsActive=false;lanes.Remove(Lane); }
        }
        static readonly Dictionary<string,Lease> lanes=new Dictionary<string,Lease>(StringComparer.Ordinal);
        static long sequence;
        public static int Count { get { return lanes.Count; } }
        public static Lease Begin(string lane,double now,int sampleBudget)
        {
            if(string.IsNullOrEmpty(lane))throw new ArgumentException("Capture lane required");
            if(lanes.ContainsKey(lane))throw new InvalidOperationException("Capture lane already owned: "+lane);
            var lease=new Lease{Lane=lane,Id=++sequence,StartedAt=now,SampleBudget=sampleBudget};lanes.Add(lane,lease);return lease;
        }
    }
}

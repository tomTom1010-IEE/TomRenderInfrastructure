using System.Collections.Generic;
using UnityEngine;

namespace TomRenderInfrastructure
{
    // Cache candidates only for one invocation. Never cache shared materials,
    // their values, visibility, bounds or property blocks across participants.
    public static class SceneDiscovery
    {
        sealed class Candidates { public Renderer[] renderers; public Light[] lights; }
        static readonly Dictionary<long, Candidates> scopes = new Dictionary<long, Candidates>();
        public static long RendererScans { get; private set; }
        public static long LightScans { get; private set; }
        public static int ScopeCount { get { return scopes.Count; } }
        static SceneDiscovery() { UnityCameraHub.Coordinator.Closed += (context, aborted) => scopes.Remove(context.Id); }
        static Candidates Current()
        {
            var context = UnityCameraHub.Coordinator.Current;
            if (context == null) return new Candidates();
            Candidates value;
            if (!scopes.TryGetValue(context.Id, out value)) scopes.Add(context.Id, value = new Candidates());
            return value;
        }
        public static IEnumerable<Renderer> Renderers()
        {
            var value = Current();
            if (value.renderers == null) { value.renderers = Object.FindObjectsOfType<Renderer>(); ++RendererScans; }
            return value.renderers;
        }
        public static IEnumerable<Light> Lights()
        {
            var value = Current();
            if (value.lights == null) { value.lights = Object.FindObjectsOfType<Light>(); ++LightScans; }
            return value.lights;
        }
    }
}

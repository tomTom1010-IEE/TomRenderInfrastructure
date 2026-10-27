using UnityEngine;

namespace TomProbeRuntime
{
    // Poll once per scheduler tick, not once for every probe. These capture-critical
    // properties exclude shader time animation, which must not launch endless jobs.
    internal static class TomProbeSkyState
    {
        private static readonly string[] Floats = { "_Exposure", "_Rotation", "_AtmosphereThickness", "_SunSize" };
        private static readonly string[] Colors = { "_Tint", "_SkyTint", "_GroundColor" };
        private static float _pollAt;
        private static int _signature;
        private static bool _initialized;
        internal static int Revision { get; private set; }

        internal static void Poll(float now)
        {
            if (_initialized && now < _pollAt) return;
            _pollAt = now + .1f;
            int signature = Signature(RenderSettings.skybox);
            if (!_initialized || signature != _signature)
            {
                _signature = signature;
                ++Revision;
            }
            _initialized = true;
        }

        private static int Signature(Material material)
        {
            if (material == null) return 0;
            unchecked
            {
                int h = material.GetInstanceID();
                h = h * 397 ^ (material.shader == null ? 0 : material.shader.GetInstanceID());
                foreach (string name in Floats) if (material.HasProperty(name)) h = h * 397 ^ material.GetFloat(name).GetHashCode();
                foreach (string name in Colors) if (material.HasProperty(name)) h = h * 397 ^ material.GetColor(name).GetHashCode();
                foreach (string name in material.GetTexturePropertyNames())
                {
                    Texture texture = material.GetTexture(name);
                    h = h * 397 ^ (texture == null ? 0 : texture.GetInstanceID());
                }
                foreach (string keyword in material.shaderKeywords) h = h * 397 ^ keyword.GetHashCode();
                return h;
            }
        }
    }
}

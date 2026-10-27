using System.Collections.Generic;
using UnityEngine;

namespace TomBSDFRuntime
{
    // Prefab-facing controls live in a separate DLL shared by authoring and KKS.
    [ExecuteAlways]
    public sealed class TomRectAreaLightControl : MonoBehaviour
    {
        readonly List<Material> materials = new List<Material>();
        Renderer source;
        public struct Settings
        {
            public Vector4 center, right, up, radiance;
            public int priority, id;
        }

        void OnEnable() { TomBSDFSceneService.Register(this); }
        void OnDisable() { TomBSDFSceneService.Unregister(this); }

        public bool Read(Camera camera, out Settings value)
        {
            value = new Settings();
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy || !gameObject.scene.IsValid()) return false;
            if (camera && (camera.cullingMask & (1 << gameObject.layer)) == 0) return false;
            if (!source) source = GetComponent<Renderer>();
            if (!source || !source.enabled) return false;
            source.GetSharedMaterials(materials);
            Material m = null;
            foreach (var candidate in materials)
                if (candidate && candidate.HasProperty("_TomRectAreaLightControl")) { m = candidate; break; }
            if (!m || m.GetFloat("_AreaLightEnabled") < .5f) return false;
            float width = Mathf.Max(0, m.GetFloat("_AreaLightWidth"));
            float height = Mathf.Max(0, m.GetFloat("_AreaLightHeight"));
            float intensity = Mathf.Clamp(m.GetFloat("_AreaLightRadiance"), 0, 10000);
            if (!Finite(width) || !Finite(height) || !Finite(intensity)) return false;
            Vector3 x = transform.TransformVector(Vector3.right) * width * .5f;
            Vector3 y = transform.TransformVector(Vector3.up) * height * .5f;
            if (x.sqrMagnitude < 1e-10f || y.sqrMagnitude < 1e-10f || intensity <= 0) return false;
            Color c = m.GetColor("_AreaLightColor");
            if (!Finite(c.r) || !Finite(c.g) || !Finite(c.b)) return false;
            // ME stores these color values as sRGB; the scene protocol is linear.
            if (m.GetFloat("_AreaLightColorInput") < .5f) c = c.linear;
            value.center = new Vector4(transform.position.x, transform.position.y, transform.position.z, 1);
            value.right = new Vector4(x.x, x.y, x.z, m.GetFloat("_AreaLightTwoSided") > .5f ? 1 : 0);
            value.up = new Vector4(y.x, y.y, y.z, 0);
            value.radiance = new Vector4(Mathf.Max(0, c.r) * intensity, Mathf.Max(0, c.g) * intensity, Mathf.Max(0, c.b) * intensity, 0);
            value.priority = Mathf.RoundToInt(m.GetFloat("_AreaLightPriority"));
            value.id = GetInstanceID();
            return true;
        }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

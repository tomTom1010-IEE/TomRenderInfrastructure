using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using TomBSDFRuntime;
using TomRenderInfrastructure;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TomRenderValidation
{
    public static class DiscoveryValidation
    {
        [Serializable] public sealed class CheckResult { public string name; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed; public string error, unity;
            public string scope = "Isolated Editor render/lifecycle regression of the imported runtime. No shaders or serialized assets are edited. CPU tests are not GPU timings. Actual ME/Screencap and large-scene A/B are separate player gates.";
            public List<CheckResult> checks = new List<CheckResult>();
            public Dictionary<string, double> shadowDifferences = new Dictionary<string, double>();
        }
        const string Root = "Assets/Mods/TomShadersBSDF";
        static void Check(Report report, string name, bool passed)
        { report.checks.Add(new CheckResult { name = name, passed = passed }); if (!passed) throw new InvalidOperationException(name); }
        static T Own<T>(List<Object> owned, T value) where T : Object { owned.Add(value); return value; }
        static Material Material(List<Object> owned, string entry)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Material/m_" + entry + ".mat");
            return Own(owned, source ? new Material(source) : new Material(Shader.Find("tom/" + entry)));
        }
        static Color[] Shot(Camera camera, RenderTexture target, string path)
        {
            camera.Render(); camera.Render();
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                if (path != null) File.WriteAllBytes(path, image.EncodeToPNG());
                return image.GetPixels();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        static double Difference(Color[] a, Color[] b)
        { double sum = 0; for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b); return sum / (3 * a.Length); }

        public static string Run(string output)
        {
            if (Directory.Exists(output)) throw new InvalidOperationException("Use a new evidence folder.");
            Directory.CreateDirectory(output);
            var report = new Report { unity = Application.unityVersion };
            var owned = new List<Object>(); var scene = EditorSceneManager.NewPreviewScene();
            TomBSDFSceneService service = null; RendererInterest unlit = null;
            IDisposable observer = null; TomSceneBufferLease external = null;
            try
            {
                Check(report, "No existing user service/registry is taken over", !TomBSDFSceneService.Current && SceneDiscovery.InterestCount == 0);
                var root = Own(owned, new GameObject("Discovery regression"));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var cameraObject = new GameObject("Discovery regression camera"); cameraObject.transform.SetParent(root.transform);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
                camera.transform.position = new Vector3(0, 0, -4); camera.orthographic = true; camera.orthographicSize = 1.5f;
                camera.nearClipPlane = .1f; camera.farClipPlane = 20; camera.cullingMask = 1 << 30;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .08f, .12f, 1);
                camera.renderingPath = RenderingPath.Forward; camera.allowHDR = true; camera.allowMSAA = false;
                var target = Own(owned, new RenderTexture(256, 192, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)); target.Create(); camera.targetTexture = target;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.transform.SetParent(root.transform); quad.layer = 30;
                var renderer = quad.GetComponent<Renderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off;
                var originalMesh = quad.GetComponent<MeshFilter>().sharedMesh;
                var plain = Own(owned, new Material(Shader.Find("Unlit/Color"))); renderer.sharedMaterial = plain;
                service = TomBSDFSceneService.EnsureInstance();
                service.sceneBufferResources = AssetDatabase.LoadAssetAtPath<TomBSDFSceneBufferResources>(TomBSDFSceneBufferResources.AssetPath);
                service.shadowResources = AssetDatabase.LoadAssetAtPath<TomBSDFShadowResources>(TomBSDFShadowResources.AssetPath);
                Check(report, "Original scene/shadow resources resolve", service.sceneBufferResources && service.sceneBufferResources.IsValid && service.shadowResources && service.shadowResources.IsValid);
                unlit = SceneDiscovery.WatchShaders("Fixture unlit", m => m && SceneDiscovery.ShaderName(m.shader) == "Unlit/Color");
                SceneDiscovery.NotifyHierarchy(root);
                Check(report, "Explicit hierarchy registration is immediate", unlit.Count == 1);
                long scans = SceneDiscovery.RendererScans, reads = SceneDiscovery.MaterialReads, nodes = SceneDiscovery.NodesVisited;
                camera.Render();
                Check(report, "Unrelated renderer creates no BSDF consumers", service.Glass.CameraCount == 0 && service.TessellationBounds.RendererCount == 0 && service.Buffers.TextureCount == 0);
                Check(report, "Camera callbacks never advance scene traversal or global scans", SceneDiscovery.RendererScans == scans && SceneDiscovery.NodesVisited == nodes);

                var glass = Material(owned, "BSDFGlass"); glass.SetFloat("_UseSceneRefraction", 1); glass.SetFloat("_Coverage", 1); glass.SetFloat("_GlassBufferResolution", 0);
                renderer.sharedMaterial = glass; SceneDiscovery.NotifyRenderer(renderer);
                Shot(camera, target, Path.Combine(output, "glass-full.png"));
                TomSceneBufferSnapshot snapshot;
                Check(report, "New glass shader notification creates automatic demand", service.Glass.CameraCount == 1 && service.Buffers.TryGetSnapshot(camera, out snapshot) && snapshot.width == 256);
                renderer.sharedMaterial = plain; camera.Render();
                Check(report, "Known shader switch without notification retires demand in the same frame", service.Glass.CameraCount == 0);
                renderer.sharedMaterial = glass; camera.Render();
                Check(report, "Known shader switch without notification recovers in the same frame", service.Glass.CameraCount == 1);
                glass.SetFloat("_GlassBufferResolution", 2); camera.Render();
                Check(report, "Live glass resolution selects half-sized buffers", service.Buffers.TryGetSnapshot(camera, out snapshot) && snapshot.width == 128);
                var clone = Own(owned, new Material(glass)); clone.SetFloat("_UseSceneRefraction", 0); renderer.sharedMaterial = clone;
                camera.Render();
                Check(report, "Known renderer material clone is read without instantiation/cache", service.Glass.CameraCount == 0 && service.Buffers.TextureCount == 0 && renderer.sharedMaterial == clone);
                clone.SetFloat("_UseSceneRefraction", 1); camera.Render();
                Check(report, "Live refraction toggle recovers immediately", service.Glass.CameraCount == 1 && service.Buffers.TextureCount == 2);
                renderer.sharedMaterial = plain; SceneDiscovery.NotifyRenderer(renderer); camera.Render();
                Check(report, "Switching out of glass retires demand", service.Glass.CameraCount == 0 && service.Buffers.TextureCount == 0);
                external = service.Buffers.Acquire(camera, TomSceneBufferFlags.Color | TomSceneBufferFlags.Depth);
                camera.Render();
                Check(report, "External buffer lease works with no BSDF renderer", service.Buffers.TryGetSnapshot(camera, out snapshot) && service.Buffers.TextureCount == 2);
                external.Dispose(); external = null;

                var tess = Material(owned, "BSDFTess"); tess.SetFloat("_HeightScale", .05f);
                renderer.sharedMaterial = tess; SceneDiscovery.NotifyRenderer(renderer); camera.Render();
                Check(report, "Registered Tess acquires conservative bounds", service.TessellationBounds.RendererCount == 1 && quad.GetComponent<MeshFilter>().sharedMesh != originalMesh);
                var boundsMesh = quad.GetComponent<MeshFilter>().sharedMesh; var extent = boundsMesh.bounds.size;
                camera.Render(); camera.Render();
                Check(report, "Repeated Tess preparation does not inflate bounds", quad.GetComponent<MeshFilter>().sharedMesh == boundsMesh && boundsMesh.bounds.size == extent);
                tess.SetFloat("_HeightScale", 0); camera.Render();
                Check(report, "Live zero height releases bounds", service.TessellationBounds.RendererCount == 0 && quad.GetComponent<MeshFilter>().sharedMesh == originalMesh);
                tess.SetFloat("_HeightScale", .05f); camera.Render();
                for (int i = 0; i < 3; i++)
                {
                    var ui = new GameObject("Empty UI camera " + i); ui.transform.SetParent(root.transform);
                    var child = ui.AddComponent<Camera>(); child.CopyFrom(camera); child.scene = scene; child.enabled = false; child.cullingMask = 0;
                    reads = SceneDiscovery.MaterialReads; nodes = SceneDiscovery.NodesVisited;
                    child.Render(); child.Render();
                    Check(report, "Empty camera performs no registry material/hierarchy work " + i, SceneDiscovery.MaterialReads == reads && SceneDiscovery.NodesVisited == nodes);
                }
                renderer.sharedMaterial = plain; SceneDiscovery.NotifyRenderer(renderer); camera.Render();
                Check(report, "Leaving Tess releases the last geometry lease", service.TessellationBounds.RendererCount == 0 && quad.GetComponent<MeshFilter>().sharedMesh == originalMesh);

                var areaObject = GameObject.CreatePrimitive(PrimitiveType.Quad); areaObject.transform.SetParent(root.transform); areaObject.layer = 30;
                areaObject.transform.position = new Vector3(0, 1, -1); areaObject.transform.rotation = Quaternion.Euler(45, 0, 0);
                var control = Material(owned, "RectAreaLight"); control.SetFloat("_AreaLightEnabled", 1); control.SetFloat("_AreaLightRadiance", 2);
                areaObject.GetComponent<Renderer>().sharedMaterial = control; areaObject.AddComponent<TomRectAreaLightControl>();
                var surface = Material(owned, "BSDF"); surface.SetFloat("_SceneLighting", 1); surface.SetFloat("_AreaLightIntensity", 1); surface.SetFloat("_AreaLightQuality", 0);
                renderer.sharedMaterial = surface; SceneDiscovery.NotifyHierarchy(root);
                bool realtime = false, reference = false;
                observer = UnityCameraHub.Register(new object(), "Discovery quality observation", 900, null, context => {
                    if (UnityCameraHub.Camera(context) != camera) return;
                    realtime |= surface.IsKeywordEnabled(TomBSDFMaterialQualityScope.RealtimeKeyword);
                    reference |= surface.IsKeywordEnabled(TomBSDFMaterialQualityScope.ReferenceKeyword);
                }, null);
                camera.Render();
                Check(report, "Realtime area keyword is scoped to the draw", realtime && !reference && !surface.IsKeywordEnabled(TomBSDFMaterialQualityScope.RealtimeKeyword));
                using (TomBSDFCaptureQuality.Begin(camera, false)) camera.Render();
                Check(report, "Capture reference path restores keywords and ME value", reference && surface.GetFloat("_AreaLightQuality") == 0 && !surface.IsKeywordEnabled(TomBSDFMaterialQualityScope.ReferenceKeyword));
                observer.Dispose(); observer = null; areaObject.SetActive(false);

                ShadowCases(report, owned, root, renderer, camera, target, service, output);
                renderer.sharedMaterial = plain; SceneDiscovery.NotifyRenderer(renderer);
                Check(report, "Leaving shader interest preserves complete compatibility candidates", unlit.Contains(renderer) && SceneDiscovery.Renderers().Contains(renderer));
                Object.DestroyImmediate(quad); SceneDiscovery.NotifyRenderer(renderer);
                Check(report, "Destroyed renderer notification releases tracked identity", !unlit.Contains(renderer));
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); throw; }
            finally
            {
                if (observer != null) observer.Dispose(); if (external != null) external.Dispose();
                if (service) Object.DestroyImmediate(service.gameObject); if (unlit != null) unlit.Dispose();
                UnityCameraHub.Coordinator.AbortAll();
                for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) { var rt = owned[i] as RenderTexture; if (rt) rt.Release(); Object.DestroyImmediate(owned[i]); }
                EditorSceneManager.ClosePreviewScene(scene);
                using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(Report)).WriteObject(stream, report); File.WriteAllText(Path.Combine(output, "report.json"), Encoding.UTF8.GetString(stream.ToArray())); }
            }
            return Path.Combine(output, "report.json");
        }

        static void ShadowCases(Report report, List<Object> owned, GameObject root, Renderer receiver, Camera camera, RenderTexture target, TomBSDFSceneService service, string output)
        {
            var oldShadows = QualitySettings.shadows; var oldDistance = QualitySettings.shadowDistance;
            try
            {
                QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowDistance = 20;
                var alpha = Material(owned, "BSDFAlpha"); alpha.SetFloat("_Coverage", .6f); alpha.SetFloat("_SpecularWeight", 0);
                alpha.SetFloat("_IndirectDiffuseIntensity", 0); alpha.SetFloat("_ReflectionIntensity", 0);
                receiver.sharedMaterial = alpha;
                var caster = GameObject.CreatePrimitive(PrimitiveType.Cube); caster.transform.SetParent(root.transform); caster.layer = 30;
                caster.transform.position = new Vector3(-.25f, .15f, -.6f); caster.transform.localScale = new Vector3(.45f, .45f, .2f);
                caster.GetComponent<Renderer>().sharedMaterial = Material(owned, "BSDF");
                var back = GameObject.CreatePrimitive(PrimitiveType.Quad); back.transform.SetParent(root.transform); back.layer = 30;
                back.transform.position = new Vector3(0, 0, .4f); back.transform.localScale = Vector3.one * 3;
                back.GetComponent<Renderer>().sharedMaterial = Material(owned, "BSDF");
                var lightObject = new GameObject("Native shadow key"); lightObject.transform.SetParent(root.transform);
                var light = lightObject.AddComponent<Light>(); light.cullingMask = 1 << 30; light.intensity = 2;
                light.shadows = LightShadows.Hard; light.shadowCustomResolution = 512; light.shadowStrength = 1; light.range = 8;
                foreach (var kind in new[] { LightType.Directional, LightType.Spot, LightType.Point })
                {
                    light.type = kind; light.transform.position = new Vector3(-1, .8f, -2);
                    light.transform.LookAt(Vector3.zero); light.spotAngle = 100;
                    SceneDiscovery.NotifyHierarchy(root);
                    service.shadowReceiversEnabled = false; var off = Shot(camera, target, Path.Combine(output, "alpha-" + kind + "-off.png"));
                    service.shadowReceiversEnabled = true; var on = Shot(camera, target, Path.Combine(output, "alpha-" + kind + "-on.png"));
                    double difference = Difference(off, on); report.shadowDifferences.Add(kind.ToString(), difference);
                    Check(report, kind + " has native copied shadow resources", service.Shadows.TextureCount >= 2);
                    Check(report, kind + " changes direct lighting rather than silently falling back", difference > .00001);
                    receiver.receiveShadows = false; var optOut = Shot(camera, target, null); receiver.receiveShadows = true;
                    Check(report, kind + " receiver opt-out restores the fallback", Difference(off, optOut) < .00001);
                }
                light.enabled = false;
            }
            finally { QualitySettings.shadows = oldShadows; QualitySettings.shadowDistance = oldDistance; }
        }
    }
}

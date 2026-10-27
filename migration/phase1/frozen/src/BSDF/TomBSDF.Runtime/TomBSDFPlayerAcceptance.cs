using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TomBSDFRuntime
{
    public static class TomBSDFPlayerAcceptance
    {
        public static Func<Scene> EditorSceneCreate;
        public static Action<Scene> EditorSceneClose;
        const string Assets = "Assets/Mods/TomShadersBSDF";
        [Serializable] sealed class Receipt
        {
            public string sha256 = "", referenceSHA256 = "", referenceRoot = Assets, colorContract = "", targetColorSpace = "", buildColorSpace = "";
        }
        [Serializable] sealed class Check { public string name; public double value; public bool passed; }
        [Serializable] sealed class Timing { public string material; public int size, lights; public double[] completedLatencyMs; }
        [Serializable] sealed class Report
        {
            public string unity, scope, colorSpace, bundleSHA256, colorContract = "KKSLinear-v1", readback = "Linear ARGBFloat -> explicit AsyncGPUReadback RGBAFloat; display PNG uses x/(1+x), raw .bin is authoritative.";
            public string timingScope = "Identical isolated quad, one native directional light, 0/1 additional rectangle, no shadows/postprocess/MSAA. CPU+GPU+driver/readback completed latency, NOT GPU timestamps or full-game FPS. Warm-up GPU work is drained before timing.";
            public List<Check> checks = new List<Check>();
            public List<Timing> timings = new List<Timing>();
        }
        sealed class Fixture : IDisposable
        {
            public readonly List<Object> owned = new List<Object>();
            public readonly Camera camera;
            public readonly Light nativeLight;
            public readonly GameObject subject, emitter;
            public readonly Material control;
            public readonly TomBSDFSceneService service;
            public readonly Report report = new Report();
            public readonly string output;
            readonly Dictionary<Light, int> masks = new Dictionary<Light, int>();
            readonly Scene previous, scene;
            readonly float linearOutput;
            readonly bool oldEnabled, oldExtension, createdService;
            readonly RenderTexture target;
            public T Own<T>(T o) where T : Object { owned.Add(o); return o; }
            public Fixture(string output, Shader controlShader)
            {
                if (!SystemInfo.supportsAsyncGPUReadback) throw new Exception("Explicit acceptance requires AsyncGPUReadback. No float ReadPixels fallback.");
                this.output = output;
                report.unity = Application.unityVersion;
                report.colorSpace = QualitySettings.activeColorSpace.ToString();
                report.scope = Application.isEditor ? "Shared acceptance harness running inside Unity Editor; not player/ME acceptance." : "Shared acceptance harness running in actual KKS player; isolated temporary geometry, no user material/scene edits.";
                previous = SceneManager.GetActiveScene();
                var used = new HashSet<int>(Object.FindObjectsOfType<GameObject>().Select(g => g.layer));
                int layer = 31; while (layer >= 8 && used.Contains(layer)) layer--;
                if (layer < 8) throw new Exception("No isolated unused layer; refusing to touch user objects.");
                if (Application.isEditor && (EditorSceneCreate == null || EditorSceneClose == null))
                    throw new Exception("Use the explicit Unity Bridge Editor scene adapter.");
                scene = Application.isEditor ? EditorSceneCreate() : SceneManager.CreateScene("TomBSDF explicit acceptance " + Guid.NewGuid());
                SceneManager.SetActiveScene(scene);
                linearOutput = Shader.GetGlobalFloat("_TomBSDFLinearOutput"); Shader.SetGlobalFloat("_TomBSDFLinearOutput", 1);
                foreach (var light in Object.FindObjectsOfType<Light>()) { masks[light] = light.cullingMask; light.cullingMask &= ~(1 << layer); }
                var existing = TomBSDFSceneService.Current; createdService = !existing;
                service = existing ? existing : TomBSDFSceneService.EnsureInstance();
                oldEnabled = service.enabled; oldExtension = service.extensionEnabled;
                service.enabled = true; service.extensionEnabled = true;
                camera = Own(new GameObject("TomBSDF acceptance camera")).AddComponent<Camera>();
                camera.enabled = false; camera.renderingPath = RenderingPath.Forward;
                camera.cullingMask = 1 << layer; camera.orthographic = true; camera.orthographicSize = .7f;
                camera.transform.position = new Vector3(0, 0, -3); camera.transform.LookAt(Vector3.zero);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.nearClipPlane = .05f; camera.farClipPlane = 10; camera.allowHDR = true; camera.allowMSAA = false;
                target = Own(new RenderTexture(128, 128, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                target.Create(); camera.targetTexture = target;
                nativeLight = Own(new GameObject("TomBSDF native reference light")).AddComponent<Light>();
                nativeLight.type = LightType.Directional; nativeLight.cullingMask = 1 << layer;
                nativeLight.intensity = .6f; nativeLight.renderMode = LightRenderMode.ForcePixel;
                nativeLight.transform.rotation = Quaternion.Euler(20, -25, 0); nativeLight.enabled = false;
                subject = Own(GameObject.CreatePrimitive(PrimitiveType.Quad)); subject.name = "TomBSDF test receiver"; subject.layer = layer;
                Object.DestroyImmediate(subject.GetComponent<Collider>());
                subject.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.Off;
                subject.GetComponent<Renderer>().reflectionProbeUsage = ReflectionProbeUsage.Off;
                emitter = Own(GameObject.CreatePrimitive(PrimitiveType.Quad)); emitter.name = "TomBSDF test emitter"; emitter.layer = layer;
                Object.DestroyImmediate(emitter.GetComponent<Collider>());
                control = Own(new Material(controlShader)); control.name = "TomBSDF emitter controls";
                emitter.GetComponent<Renderer>().sharedMaterial = control;
                emitter.AddComponent<TomRectAreaLightControl>(); emitter.transform.position = new Vector3(.25f, .3f, -1.2f);
                emitter.transform.LookAt(Vector3.zero);
            }
            public Material Material(Shader shader)
            {
                var m = Own(new Material(shader)); m.name = "TomBSDF acceptance material";
                m.SetFloat("_ColorValueMode", 2); m.SetFloat("_ColorTextureMode", 2);
                m.SetFloat("_ReflectionIntensity", 0); m.SetFloat("_IndirectDiffuseIntensity", 0);
                m.SetFloat("_NormalMapScale", 0); m.SetFloat("_DetailNormalMapScale", 0);
                m.SetColor("_BaseColor", new Color(.65f, .42f, .22f, 1)); m.SetFloat("_Roughness", .45f);
                return m;
            }
            public void Check(string name, double value, bool passed) { report.checks.Add(new Check { name = name, value = value, passed = passed }); }
            public Color[] Shot(string name, Material m, bool save = true)
            {
                subject.GetComponent<Renderer>().sharedMaterial = m;
                camera.Render(); camera.Render();
                var request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat); request.WaitForCompletion();
                if (request.hasError) throw new Exception("Readback error: " + name);
                var pixels = request.GetData<Color>().ToArray();
                int invalid = pixels.Count(c => !Finite(c.r) || !Finite(c.g) || !Finite(c.b) || c.r < -1e-5f || c.g < -1e-5f || c.b < -1e-5f);
                Check(name + " finite/nonnegative", invalid, invalid == 0);
                if (save)
                {
                    var raw = new float[pixels.Length * 4];
                    for (int i = 0; i < pixels.Length; i++) for (int c = 0; c < 4; c++) raw[i * 4 + c] = pixels[i][c];
                    var bytes = new byte[raw.Length * 4]; Buffer.BlockCopy(raw, 0, bytes, 0, bytes.Length);
                    File.WriteAllBytes(Path.Combine(output, name + ".bin"), bytes);
                    var png = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                    try
                    {
                        png.SetPixels(pixels.Select(c => new Color(Display(c.r), Display(c.g), Display(c.b), 1)).ToArray()); png.Apply();
                        File.WriteAllBytes(Path.Combine(output, name + ".png"), png.EncodeToPNG());
                    }
                    finally { Object.DestroyImmediate(png); }
                }
                return pixels;
            }
            public void Resize(int size)
            { target.Release(); target.width = size; target.height = size; target.Create(); }
            public void Measure(Material material, string name, int size)
            {
                Resize(size);
                subject.GetComponent<Renderer>().sharedMaterial = material;
                nativeLight.enabled = true;
                foreach (int lights in new[] { 0, 1 })
                {
                    control.SetFloat("_AreaLightEnabled", lights);
                    for (int warm = 0; warm < 6; warm++) camera.Render();
                    var warmup = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat); warmup.WaitForCompletion();
                    if (warmup.hasError) throw new Exception("Warm-up readback failed.");
                    var times = new double[6];
                    for (int sample = 0; sample < times.Length; sample++)
                    {
                        var watch = Stopwatch.StartNew();
                        for (int frame = 0; frame < 4; frame++) camera.Render();
                        var request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat); request.WaitForCompletion();
                        if (request.hasError) throw new Exception("Timing readback failed.");
                        watch.Stop(); times[sample] = watch.Elapsed.TotalMilliseconds / 4;
                    }
                    report.timings.Add(new Timing { material = name, size = size, lights = lights, completedLatencyMs = times });
                }
                Resize(128); control.SetFloat("_AreaLightEnabled", 1); nativeLight.enabled = false;
            }
            public void Dispose()
            {
                camera.targetTexture = null;
                foreach (var pair in masks) if (pair.Key) pair.Key.cullingMask = pair.Value;
                service.extensionEnabled = oldExtension; service.enabled = oldEnabled;
                if (createdService) Object.DestroyImmediate(service.gameObject);
                Check("Fixture preserves pre-existing scene-service lifetime", 0,
                    createdService ? !TomBSDFSceneService.Current : TomBSDFSceneService.Current == service);
                Shader.SetGlobalFloat("_TomBSDFLinearOutput", linearOutput);
                foreach (var o in owned) if (o) Object.DestroyImmediate(o);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                if (Application.isEditor) EditorSceneClose(scene); else SceneManager.UnloadSceneAsync(scene);
                JsonReport.Write(Path.Combine(output, "acceptance.json"), report);
                if (report.checks.Any(c => !c.passed)) throw new Exception("Shared acceptance or fixture lifecycle checks failed; see acceptance.json.");
            }
        }
        static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }
        static float Display(float x) { return Mathf.LinearToGammaSpace(Mathf.Max(0, x) / (1 + Mathf.Max(0, x))); }
        static double Difference(Color[] a, Color[] b)
        { double sum = 0; for (int i = 0; i < a.Length; i++) for (int c = 0; c < 3; c++) sum += Math.Abs(a[i][c] - b[i][c]); return sum / (a.Length * 3); }
        static double Mean(Color[] pixels) { return pixels.Average(c => (c.r + c.g + c.b) / 3.0); }
        static double NRMSE(Color[] a, Color[] b)
        { double x = 0, y = 0; for (int i = 0; i < a.Length; i++) for (int c = 0; c < 3; c++) { double delta = a[i][c] - b[i][c]; x += delta * delta; y += b[i][c] * b[i][c]; } return Math.Sqrt(x / Math.Max(y, 1e-15)); }
        static Type METype(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null); }
        static bool MECall(string method, params object[] args)
        {
            var type = METype("MaterialEditorAPI.MaterialAPI");
            if (type == null) return false;
            var member = type.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == method && m.GetParameters().Length == args.Length);
            return member != null && (bool)member.Invoke(null, args);
        }

        public static IEnumerator Run(string root, string output)
        {
            Directory.CreateDirectory(output);
            string payload = Path.Combine(root, "payload");
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(payload, "receipt.json")));
            if (receipt.colorContract != "KKSLinear-v1" || receipt.targetColorSpace != "Linear")
                throw new Exception("Payload predates the corrected KKS color contract. Rebuild in the main project through Bridge.");
            if (!Application.isEditor && QualitySettings.activeColorSpace != ColorSpace.Linear)
                throw new Exception("This KKS adapter targets the Linear player; game settings will not be changed.");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(Path.Combine(payload, "modern.bundle")))
                if (string.Concat(sha.ComputeHash(stream).Select(x => x.ToString("x2"))) != receipt.sha256) throw new Exception("Acceptance bundle hash mismatch.");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(Path.Combine(payload, "reference.bundle")))
                if (string.Concat(sha.ComputeHash(stream).Select(x => x.ToString("x2"))) != receipt.referenceSHA256) throw new Exception("Acceptance reference hash mismatch.");
            var bundle = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b.name == "modern.bundle");
            bool ownsBundle = !bundle;
            if (!bundle) bundle = AssetBundle.LoadFromFile(Path.Combine(payload, "modern.bundle"));
            var previous = AssetBundle.LoadFromFile(Path.Combine(payload, "reference.bundle"));
            if (!bundle || !previous) throw new Exception("Acceptance needs current and preserved stage-3 bundles.");
            try
            {
                var carrier = bundle.LoadAsset<GameObject>(Assets + "/Prefab/a_RectAreaLight.prefab");
                if (!carrier.GetComponent<TomRectAreaLightControl>()) throw new Exception("Packaged area-light runtime component is missing.");
                var controlShader = carrier.GetComponent<Renderer>().sharedMaterial.shader;
                using (var f = new Fixture(output, controlShader))
                {
                    f.report.bundleSHA256 = receipt.sha256;
                    try
                    {
                        foreach (string entry in new[] { "BSDF", "BSDFCutout" })
                        {
                            var shader = bundle.LoadAsset<GameObject>(Assets + "/Prefab/a_" + entry + ".prefab").GetComponent<Renderer>().sharedMaterial.shader;
                            var oldShader = previous.LoadAsset<GameObject>(receipt.referenceRoot + "/Prefab/a_" + entry + ".prefab").GetComponent<Renderer>().sharedMaterial.shader;
                            var m = f.Material(shader); var old = f.Material(oldShader);
                            foreach (string materialCase in new[] { "bare", "metal", "anisotropic", "coat", "film" })
                            {
                                ApplyCase(m, materialCase); old.CopyPropertiesFromMaterial(m);
                                f.nativeLight.enabled = true;
                                m.SetFloat("_SceneLighting", 0);
                                var baseline = f.Shot(entry + "-" + materialCase + "-stage3", old);
                                f.Check(entry + "/" + materialCase + " baseline is genuinely lit", Mean(baseline), Mean(baseline) > .0001);
                                var off = f.Shot(entry + "-" + materialCase + "-off", m);
                                double error = Difference(baseline, off);
                                f.Check(entry + "/" + materialCase + " extension off preserves accepted stage3", error, error < .00001);
                                m.SetFloat("_SceneLighting", 1); f.service.enabled = false;
                                var absent = f.Shot(entry + "-" + materialCase + "-no-service", m);
                                f.Check(entry + "/" + materialCase + " missing service equals off", Difference(off, absent), Difference(off, absent) < .00001);
                                f.service.enabled = true;
                                f.nativeLight.enabled = false;
                                var lit = f.Shot(entry + "-" + materialCase + "-area", m);
                                f.Check(entry + "/" + materialCase + " rectangle contributes", Mean(lit), Mean(lit) > .0001);
                                f.Check(entry + "/" + materialCase + " camera state cleared after render", Shader.GetGlobalVector("_TomBSDFSceneParams").sqrMagnitude, Shader.GetGlobalVector("_TomBSDFSceneParams").sqrMagnitude == 0);
                            }
                            ApplyCase(m, "bare"); var unit = f.Shot(entry + "-unit", m);
                            m.SetFloat("_AreaLightIntensity", 2); var twice = f.Shot(entry + "-twice", m);
                            double scaleError = Math.Abs(Mean(twice) / Math.Max(Mean(unit), 1e-12) - 2);
                            f.Check(entry + " linear radiance multiplier", scaleError, scaleError < .002);
                            m.SetFloat("_AreaLightIntensity", 0); var zero = f.Shot(entry + "-zero", m);
                            f.Check(entry + " zero radiance neutral", Mean(zero), Mean(zero) < 1e-7);
                            m.SetFloat("_AreaLightIntensity", 1);
                            f.emitter.transform.Rotate(0, 180, 0);
                            var back = f.Shot(entry + "-emitter-back", m);
                            f.Check(entry + " one-sided emitter back is dark", Mean(back), Mean(back) < 1e-7);
                            f.control.SetFloat("_AreaLightTwoSided", 1);
                            var doubleSided = f.Shot(entry + "-emitter-two-sided", m);
                            f.Check(entry + " two-sided emitter restores front result", Difference(unit, doubleSided), Difference(unit, doubleSided) < .0001);
                            f.emitter.transform.Rotate(0, 180, 0); f.control.SetFloat("_AreaLightTwoSided", 0);
                            TestHostColorContract(f, entry, shader);
                            if (entry == "BSDFCutout")
                            {
                                m.SetFloat("_Coverage", 0); var hidden = f.Shot("cutout-empty", m);
                                f.Check("Rectangle obeys shared coverage", Mean(hidden), Mean(hidden) < 1e-7); m.SetFloat("_Coverage", 1);
                            }
                            if (!Application.isEditor)
                            {
                                var standard = f.Own(new Material(Shader.Find("Standard"))); standard.name = "TomBSDF ME switch fixture";
                                f.subject.GetComponent<Renderer>().sharedMaterial = standard;
                                bool switched = MECall("SetShader", f.subject, standard.name, "tom/" + entry, false);
                                f.Check(entry + " genuine ME registry shader switch", switched ? 1 : 0, switched);
                                var actual = f.subject.GetComponent<Renderer>().sharedMaterial;
                                f.Check(entry + " shader-owned resources survive ME switch", 0, actual.GetTexture("_TomBSDFEnergyLUT") && actual.GetTexture("_TomBSDFAnisoEnergyLUT") && actual.GetTexture("_TomBSDFRectLTC"));
                                bool edited = MECall("SetFloat", f.subject, actual.name, "SceneLighting", 0f);
                                f.Check(entry + " ME float edit does not require keywords", actual.GetFloat("_SceneLighting"), edited && actual.GetFloat("_SceneLighting") == 0);
                                TestMERecordPersistence(f, entry);
                            }
                            if (entry == "BSDF")
                                foreach (string materialCase in new[] { "bare", "coat", "anisotropic", "film" })
                                {
                                    ApplyCase(m, materialCase); old.CopyPropertiesFromMaterial(m);
                                    f.Measure(old, materialCase + "-accepted-stage3", 256);
                                    f.Measure(m, materialCase + "-stage4", 256);
                                }
                        }
                        int failed = f.report.checks.Count(c => !c.passed);
                        if (failed > 0) throw new Exception(failed + " player/shared acceptance checks failed.");
                    }
                    finally { JsonReport.Write(Path.Combine(output, "acceptance.json"), f.report); }
                }
            }
            // Never destroy shader objects that the live ME/Sideloader may own.
            finally { if (bundle && ownsBundle) bundle.Unload(false); if (previous) previous.Unload(false); }
            yield break;
        }
        public static void ApplyCase(Material m, string name)
        {
            m.SetFloat("_Metallic", name == "metal" || name == "anisotropic" ? 1 : 0);
            m.SetFloat("_Anisotropy", name == "anisotropic" ? .8f : 0);
            m.SetFloat("_AnisotropyRotation", 37);
            m.SetFloat("_CoatWeight", name == "coat" || name == "film" ? 1 : 0);
            m.SetFloat("_CoatNormalScale", 0); m.SetFloat("_CoatRoughness", .2f);
            m.SetFloat("_ThinFilmWeight", name == "film" ? 1 : 0); m.SetFloat("_ThinFilmIOR", 2.2f);
            m.SetFloat("_ThinFilmThickness", 400); m.SetFloat("_ThinFilmQuality", 0);
            m.SetFloat("_AreaLightIntensity", 1); m.SetFloat("_SceneLighting", 1); m.SetFloat("_AreaLightQuality", 0);
        }

        static void TestHostColorContract(Fixture f, string entry, Shader shader)
        {
            float previousOutput = Shader.GetGlobalFloat("_TomBSDFLinearOutput");
            f.control.SetFloat("_AreaLightEnabled", 0); f.nativeLight.enabled = false;
            try
            {
                var m = f.Material(shader);
                m.SetColor("_EmissionColor", Color.white); m.SetTexture("_EmissionMask", Texture2D.whiteTexture);
                m.SetFloat("_EmissionIntensity", .25f);
                Shader.SetGlobalFloat("_TomBSDFLinearOutput", 1);
                var linear = f.Shot(entry + "-host-emission-linear", m);
                Shader.SetGlobalFloat("_TomBSDFLinearOutput", 0);
                var native = f.Shot(entry + "-host-emission-default", m);
                double error = Difference(native, linear);
                f.Check(entry + " bundled default output is KKS linear without output override", error, error < .0001);
                double levelError = Math.Abs(Mean(native.Where(c => c.a > .5f).ToArray()) - .25);
                f.Check(entry + " default .25 emission is not display-encoded", levelError, levelError < .0001);

                // A linear texture bypasses importer ambiguity and tests the compiled Auto branch.
                var texture = f.Own(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
                texture.SetPixel(0, 0, new Color(.5f, .5f, .5f, 1)); texture.Apply(false, false);
                m.SetTexture("_EmissionMask", texture); m.SetFloat("_ColorTextureMode", 0);
                Shader.SetGlobalFloat("_TomBSDFLinearOutput", 1);
                var automatic = f.Shot(entry + "-host-auto-texture", m);
                m.SetFloat("_ColorTextureMode", 2);
                var explicitInput = f.Shot(entry + "-host-explicit-texture", m);
                error = Difference(automatic, explicitInput);
                f.Check(entry + " bundled Auto texture mode follows KKS linear contract", error, error < .0001);
            }
            finally
            {
                Shader.SetGlobalFloat("_TomBSDFLinearOutput", previousOutput);
                f.control.SetFloat("_AreaLightEnabled", 1);
            }
        }

        static void TestMERecordPersistence(Fixture f, string entry)
        {
            var controller = METype("KK_Plugins.MaterialEditor.SceneController");
            var serializer = METype("MessagePack.MessagePackSerializer");
            if (controller == null || serializer == null) throw new Exception("Player ME persistence types unavailable.");
            var model = controller.GetNestedType("MaterialFloatProperty", BindingFlags.Public | BindingFlags.NonPublic);
            var listType = typeof(List<>).MakeGenericType(model);
            var list = (IList)Activator.CreateInstance(listType);
            foreach (string property in new[] { "SceneLighting", "AreaLightIntensity", "AreaLightQuality", "AreaLightWidth", "AreaLightHeight", "AreaLightTwoSided" })
                list.Add(Activator.CreateInstance(model, new object[] { -101011, "isolated TomBSDF fixture", property, "0.375", "1" }));
            var serialize = serializer.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "Serialize"
                && m.IsGenericMethodDefinition && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType.IsGenericParameter
                && m.GetParameters().Skip(1).All(p => p.IsOptional));
            var deserialize = serializer.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "Deserialize"
                && m.IsGenericMethodDefinition && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(byte[])
                && m.GetParameters().Skip(1).All(p => p.IsOptional));
            if (serialize == null || deserialize == null) throw new Exception("Player ME serializer API unsupported; not substituting a different serializer.");
            var args = new object[serialize.GetParameters().Length]; args[0] = list;
            for (int j = 1; j < args.Length; j++) args[j] = Type.Missing;
            var bytes = (byte[])serialize.MakeGenericMethod(listType).Invoke(null, args);
            File.WriteAllBytes(Path.Combine(f.output, entry + "-me-float-records.msgpack"), bytes);
            args = new object[deserialize.GetParameters().Length]; args[0] = bytes;
            for (int j = 1; j < args.Length; j++) args[j] = Type.Missing;
            var restored = (IList)deserialize.MakeGenericMethod(listType).Invoke(null, args);
            bool same = restored.Count == list.Count;
            for (int j = 0; j < restored.Count && same; j++)
                foreach (string field in new[] { "ID", "MaterialName", "Property", "Value", "ValueOriginal" })
                    same &= Equals(model.GetField(field).GetValue(restored[j]), model.GetField(field).GetValue(list[j]));
            f.Check(entry + " genuine ME typed-property serializer roundtrip (not interactive scene import)", bytes.Length, same);
        }
    }
}

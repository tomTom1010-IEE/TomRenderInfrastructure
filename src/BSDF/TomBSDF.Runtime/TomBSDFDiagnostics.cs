using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TomBSDFRuntime
{
    // Opt-in local command bridge. Commands are claimed before work and never replayed.
    public sealed partial class TomBSDFDiagnostics : MonoBehaviour
    {
        public string root;
        float nextPoll;
        bool busy;
        [Serializable] public sealed class Command { public string operation; public int frames = 180; }
        [Serializable] sealed class Result { public bool success; public string message, unity, utc, output; }
        [Serializable] sealed class Host
        {
            public string scope = "Actual player host; no project/camera/quality modifications.";
            public string unity, product, api, gpu, colorSpace, meAssembly, activeScene;
            public bool linearLightIntensity, asyncReadback, hdrRT, companion;
            public int width, height, msaa, vsync, targetFrameRate, pixelLights;
            public int cameraCount;
            public bool sceneExtensionEnabled;
            public bool sceneBuffersEnabled;
            public int bufferCameraCount, bufferTextureCount;
            public CameraInfo[] cameras;
            public string[] bsdfMaterials;
            public bool meBSDF, meCutout, meAreaControl;
            public string note = "KKSLinear-v1 output. SceneBuffer-v1 is opt-in per camera; no demand means no captures. Shader coverage Alpha needs no scene buffers. Glass/transmission/refraction and SSR are not implemented.";
        }
        [Serializable] sealed class CameraInfo
        {
            public string name, path, target, depthTexture, components;
            public bool enabled, hdr, msaa, srgb;
            public int width, height, buffers, cullingMask;
        }
        [Serializable] sealed class Baseline
        {
            public string scope = "Actual game frames, unchanged host settings. CPU frame interval is not shader/GPU time. GPU frame times are included only if the player actually exposes valid FrameTiming data.";
            public List<float> frameIntervalMs = new List<float>();
            public List<double> cpuFrameMs = new List<double>(), gpuFrameMs = new List<double>();
            public string gpuStatus;
            public double gpuFrequency;
        }

        void Update()
        {
            if (busy || string.IsNullOrEmpty(root) || Time.realtimeSinceStartup < nextPoll) return;
            nextPoll = Time.realtimeSinceStartup + .25f;
            string inbox = Path.Combine(root, "Inbox"); Directory.CreateDirectory(inbox);
            Directory.CreateDirectory(Path.Combine(root, "Outbox"));
            foreach (string file in Directory.GetFiles(inbox, "*.json").OrderBy(x => x))
            {
                try
                {
                    File.Move(file, file + ".running");
                    var command = JsonUtility.FromJson<Command>(File.ReadAllText(file + ".running"));
                    if (command == null || (command.operation != "inspect" && command.operation != "hostbaseline" && command.operation != "acceptance"))
                        throw new Exception("Unknown validation operation.");
                    busy = true; StartCoroutine(Execute(file, command));
                }
                catch (Exception e) { WriteResult(file, false, e.ToString(), ""); }
                break;
            }
        }
        IEnumerator Execute(string file, Command command)
        {
            string output = Path.Combine(root, "Reports", Path.GetFileNameWithoutExtension(file));
            Directory.CreateDirectory(output);
            IEnumerator job = Run(command, output);
            string error = null;
            while (true)
            {
                bool more;
                try { more = job.MoveNext(); }
                catch (Exception e) { error = e.ToString(); break; }
                if (!more) break;
                yield return job.Current;
            }
            var disposable = job as IDisposable;
            if (disposable != null) disposable.Dispose();
            WriteResult(file, error == null, error ?? "Completed; see saved reports.", output);
            busy = false;
        }
        void WriteResult(string command, bool success, string message, string output)
        {
            File.WriteAllText(Path.Combine(root, "Outbox", Path.GetFileNameWithoutExtension(command) + ".result.json"),
                JsonUtility.ToJson(new Result { success = success, message = message, output = output, unity = Application.unityVersion, utc = DateTime.UtcNow.ToString("o") }, true));
            if (File.Exists(command + ".running")) File.Move(command + ".running", command + ".done");
        }
        IEnumerator Run(Command command, string output)
        {
            Inspect(output);
            if (command.operation == "hostbaseline")
            {
                var report = new Baseline(); var timings = new FrameTiming[4]; ulong lastTimestamp = 0;
                report.gpuFrequency = FrameTimingManager.GetGpuTimerFrequency();
                for (int i = 0; i < Mathf.Clamp(command.frames, 30, 1200); i++)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                    report.frameIntervalMs.Add(Time.unscaledDeltaTime * 1000);
                    uint count = FrameTimingManager.GetLatestTimings(4, timings);
                    for (int j = (int)count - 1; j >= 0; j--)
                    {
                        if (timings[j].cpuTimeFrameComplete <= lastTimestamp) continue;
                        lastTimestamp = timings[j].cpuTimeFrameComplete;
                        if (timings[j].cpuFrameTime > 0) report.cpuFrameMs.Add(timings[j].cpuFrameTime);
                        if (timings[j].gpuFrameTime > 0) report.gpuFrameMs.Add(timings[j].gpuFrameTime);
                    }
                }
                report.gpuStatus = report.gpuFrameMs.Count > 0 ? "Valid player GPU frame timings; whole game, not isolated shader cost."
                    : "Unavailable in this player/configuration. No zero values or CPU/readback latency substituted as GPU timings.";
                JsonReport.Write(Path.Combine(output, "host-baseline.json"), report);
            }
            else if (command.operation == "acceptance")
            {
                var acceptance = RunAcceptance(output);
                try { while (acceptance.MoveNext()) yield return acceptance.Current; }
                finally { var disposable = acceptance as IDisposable; if (disposable != null) disposable.Dispose(); }
            }
        }
        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            { var type = assembly.GetType(fullName, false); if (type != null) return type; }
            return null;
        }
        static IDictionary MERegistry(string field)
        {
            var type = FindType("MaterialEditorAPI.MaterialEditorPluginBase");
            return type == null ? null : type.GetField(field, BindingFlags.Public | BindingFlags.Static).GetValue(null) as IDictionary;
        }
        static void Inspect(string output)
        {
            var me = FindType("MaterialEditorAPI.MaterialAPI");
            var registry = MERegistry("XMLShaderProperties");
            var host = new Host {
                unity = Application.unityVersion, product = Application.productName,
                api = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName,
                colorSpace = QualitySettings.activeColorSpace.ToString(), linearLightIntensity = GraphicsSettings.lightsUseLinearIntensity,
                asyncReadback = SystemInfo.supportsAsyncGPUReadback, hdrRT = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf),
                width = Screen.width, height = Screen.height, msaa = QualitySettings.antiAliasing, vsync = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate, pixelLights = QualitySettings.pixelLightCount,
                companion = TomBSDFSceneService.Current,
                sceneExtensionEnabled = TomBSDFSceneService.Current && TomBSDFSceneService.Current.extensionEnabled,
                sceneBuffersEnabled = TomBSDFSceneService.Current && TomBSDFSceneService.Current.sceneBuffersEnabled,
                bufferCameraCount = TomBSDFSceneService.Current && TomBSDFSceneService.Current.Buffers != null ? TomBSDFSceneService.Current.Buffers.CameraCount : 0,
                bufferTextureCount = TomBSDFSceneService.Current && TomBSDFSceneService.Current.Buffers != null ? TomBSDFSceneService.Current.Buffers.TextureCount : 0,
                activeScene = SceneManager.GetActiveScene().name, cameraCount = Camera.allCamerasCount,
                meAssembly = me == null ? "missing" : me.Assembly.FullName,
                meBSDF = registry != null && registry.Contains("tom/BSDF"), meCutout = registry != null && registry.Contains("tom/BSDFCutout"),
                meAreaControl = registry != null && registry.Contains("tom/RectAreaLight"),
                cameras = Camera.allCameras.Select(c => new CameraInfo {
                    name = c.name, path = c.actualRenderingPath.ToString(), enabled = c.enabled, hdr = c.allowHDR, msaa = c.allowMSAA,
                    depthTexture = c.depthTextureMode.ToString(), target = c.targetTexture ? c.targetTexture.format.ToString() : "backbuffer",
                    srgb = c.targetTexture && c.targetTexture.sRGB, width = c.pixelWidth, height = c.pixelHeight, cullingMask = c.cullingMask,
                    buffers = c.commandBufferCount, components = string.Join(", ", c.GetComponents<Component>().Where(x => x).Select(x => x.GetType().FullName).ToArray()) }).ToArray(),
                bsdfMaterials = Resources.FindObjectsOfTypeAll<Material>().Where(m => m.shader && m.shader.name.StartsWith("tom/BSDF"))
                    .Select(m => m.name + " | " + m.shader.name).ToArray()
            };
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                host.note = "Actual host is Linear, unlike the authoring project's Gamma setting. Do not apply Gamma-host composition assumptions. No global color-space or camera changes made by the companion.";
            JsonReport.Write(Path.Combine(output, "host.json"), host);
        }
        // Implemented by the explicitly invoked isolated acceptance fixture.
        IEnumerator RunAcceptance(string output) { return TomBSDFPlayerAcceptance.Run(root, output); }
    }
}

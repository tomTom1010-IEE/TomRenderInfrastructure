using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using TomProbeRuntime;
using TomBSDFRuntime;
using TomSceneEffects;
using TomRenderInfrastructure;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace TomRenderValidation
{
    public static class Step4Validation
    {
        [Serializable] public sealed class Check { public string name;public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed,baseline;public string error,unity;
            public List<Check> checks=new List<Check>();public List<Color[]> facePixels=new List<Color[]>();
            public int callbacks,faces;
            public float[] shCoefficients;
            public List<double> shCpuMs=new List<double>();
            public float maximumRadianceDifference,maximumShDifference;
            public string scope="Explicit synchronous SH-camera sky fixture in an isolated preview scene; native RenderProbe and scheduler need additional gates. No Bundle build/deployment.";
        }
        static void Assert(Report r,string name,bool passed){r.checks.Add(new Check{name=name,passed=passed});if(!passed)throw new InvalidOperationException("Step 4: "+name);}
        public static string Run(string output,bool baseline)
        {
            if(Directory.Exists(output))throw new InvalidOperationException("Evidence directory already exists");Directory.CreateDirectory(output);
            var report=new Report{baseline=baseline,unity=Application.unityVersion};var scene=EditorSceneManager.NewPreviewScene();
            GameObject go=null;RenderTexture cube=null;Texture2D pixels=null;TomProbeCaptureSky sky=null;
            var oldActive=RenderTexture.active;var oldSky=RenderSettings.skybox;int pre=Camera.onPreCull==null?0:Camera.onPreCull.GetInvocationList().Length;
            try
            {
                Assert(report,"No other Core owner is active",!UnityCameraHub.IsAttached && UnityCameraHub.Coordinator.RegistrationCount==0);
                go=new GameObject("Explicit SH-camera sky fixture");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
                var camera=go.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cullingMask=0;camera.allowHDR=true;camera.allowMSAA=false;
                camera.renderingPath=RenderingPath.Forward;camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.magenta;
                cube=new RenderTexture(32,32,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){dimension=TextureDimension.Cube,useMipMap=false,autoGenerateMips=false};cube.Create();
                var shader=Shader.Find("Hidden/Tom/ProbeBackground");Assert(report,"Original probe background shader is available",shader && shader.isSupported);
                var settings=new TomProbeSettings{skybox=false,background=new Color(.12f,.3f,.75f,1)};
                sky=new TomProbeCaptureSky(settings,shader);sky.Bind(camera);
                report.callbacks=(Camera.onPreCull==null?0:Camera.onPreCull.GetInvocationList().Length)-pre;
                Assert(report,"One camera subscription set for a capture job",report.callbacks==1);
                Assert(report,"Original capture returns six faces",camera.RenderToCubemap(cube));report.faces=sky.CapturedFaces;
                Assert(report,"Every owned face was observed exactly once",sky.CapturedFaces==6);
                pixels=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
                for(int face=0;face<6;face++)
                {
                    Graphics.SetRenderTarget(cube,0,(CubemapFace)face);pixels.ReadPixels(new Rect(0,0,32,32),0,0,false);pixels.Apply(false,false);
                    var colors=pixels.GetPixels();report.facePixels.Add(colors);var center=colors[16*32+16];
                    Assert(report,"Sky radiance is valid on face "+face,Mathf.Abs(center.r-.12f)<.003f && Mathf.Abs(center.g-.3f)<.003f && Mathf.Abs(center.b-.75f)<.003f);
                    File.WriteAllBytes(Path.Combine(output,"face-"+face+".png"),pixels.EncodeToPNG());
                }
                sky.Dispose();sky=null;
                Assert(report,"Capture releases its temporary sky component",!go.GetComponent<Skybox>());
                Assert(report,"Camera clear flags are restored",camera.clearFlags==CameraClearFlags.SolidColor);
                Assert(report,"Main RenderSettings sky is untouched",RenderSettings.skybox==oldSky);
                Assert(report,"All native camera callbacks are released",(Camera.onPreCull==null?0:Camera.onPreCull.GetInvocationList().Length)==pre && !UnityCameraHub.IsAttached);
                CaptureSH(report,scene,settings,shader);
                Scheduler(report,scene,shader);
                if(!baseline)
                {
                    string root=output;while(!File.Exists(Path.Combine(root,"release.manifest.json")))root=Directory.GetParent(root).FullName;
                    Report old;using(var input=File.OpenRead(Path.Combine(root,"artifacts/integration/step4-frozen-probe-full-v2/report.json")))old=(Report)new DataContractJsonSerializer(typeof(Report)).ReadObject(input);
                    for(int face=0;face<6;face++)for(int pixel=0;pixel<report.facePixels[face].Length;pixel++)
                    {var a=report.facePixels[face][pixel];var b=old.facePixels[face][pixel];report.maximumRadianceDifference=Mathf.Max(report.maximumRadianceDifference,Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Max(Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b))));}
                    for(int i=0;i<report.shCoefficients.Length;i++)report.maximumShDifference=Mathf.Max(report.maximumShDifference,Mathf.Abs(report.shCoefficients[i]-old.shCoefficients[i]));
                    Assert(report,"Six-face floating radiance matches original",report.maximumRadianceDifference==0);
                    Assert(report,"All SH integration coefficients match original",report.maximumShDifference==0);
                    Guards(report,scene,shader);
                }
                report.passed=true;
            }
            catch(Exception error){report.error=error.ToString();throw;}
            finally
            {
                if(sky!=null)sky.Dispose();RenderTexture.active=oldActive;
                if(pixels)Object.DestroyImmediate(pixels);if(cube){cube.Release();Object.DestroyImmediate(cube);}if(go)Object.DestroyImmediate(go);EditorSceneManager.ClosePreviewScene(scene);
                using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(Report)).WriteObject(stream,report);File.WriteAllText(Path.Combine(output,"report.json"),Encoding.UTF8.GetString(stream.ToArray()));}
            }
            return Path.Combine(output,"report.json");
        }
        static void CaptureSH(Report report,UnityEngine.SceneManagement.Scene scene,TomProbeSettings settings,Shader shader)
        {
            var go=new GameObject("SH volume atomic fixture");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            var volume=go.AddComponent<TomSHProbeVolume>();
            settings.grid=new Vector3Int(2,2,2);settings.captureResolution=8;settings.mask=0;settings.budget=1;
            try
            {
                volume.BeginCapture(settings);
                var samples=(SphericalHarmonicsL2[])typeof(TomSHProbeVolume).GetField("_samples",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(volume);
                for(int sample=0;sample<8;sample++)
                {
                    var timer=Stopwatch.StartNew();bool done=volume.StepCapture(settings,1);timer.Stop();report.shCpuMs.Add(timer.Elapsed.TotalMilliseconds);
                    Assert(report,"SH sample budget/atomic publication at step "+sample,done==(sample==7) && volume.DataVersion==(sample==7?1:0));
                }
                report.shCoefficients=new float[8*27];for(int sample=0;sample<8;sample++)for(int channel=0;channel<3;channel++)for(int coefficient=0;coefficient<9;coefficient++)report.shCoefficients[sample*27+channel*9+coefficient]=samples[sample][channel,coefficient];
                var published=volume.AtlasTexture;Assert(report,"Complete SH atlas publishes original snapshot",published && volume.IsReady && ReferenceEquals(volume.PublishedSettings,settings));
                var next=volume.LegacySettings();next.grid=settings.grid;next.captureResolution=8;next.mask=0;next.skybox=false;next.budget=1;next.background=Color.green;
                volume.BeginCapture(next);volume.StepCapture(next,1);
                Assert(report,"Incomplete replacement cannot retire published atlas",volume.DataVersion==1 && volume.AtlasTexture==published && ReferenceEquals(volume.PublishedSettings,settings));
                volume.CancelCapture();Assert(report,"Cancelled SH keeps last complete result",!volume.IsCapturing && volume.DataVersion==1 && volume.AtlasTexture==published);
                if(!report.baseline)Assert(report,"SH job releases exclusive cubemap and camera roles",RenderResourceLease.Count==0 && CameraRoles.Count==0 && !UnityCameraHub.IsAttached);
            }
            finally{Object.DestroyImmediate(go);}
        }
        static void Guards(Report report,UnityEngine.SceneManagement.Scene scene,Shader shader)
        {
            var go=new GameObject("Probe role screen-effect fixture");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            var camera=go.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cullingMask=0;camera.clearFlags=CameraClearFlags.SolidColor;
            var target=new RenderTexture(64,64,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            TomProbeCaptureSky sky=null;DofBinding binding=null;TomBSDFSceneService service=null;IDisposable preparation=null,observation=null;TomSceneBufferLease buffers=null;
            try
            {
                Assert(report,"Probe screen-effect fixture does not take over a scene service",!TomBSDFSceneService.Current);
                sky=new TomProbeCaptureSky(new TomProbeSettings{skybox=false,background=Color.blue},shader);sky.Bind(camera);
                service=TomBSDFSceneService.EnsureInstance();service.shadowReceiversEnabled=false;buffers=service.Buffers.Acquire(camera,TomSceneBufferFlags.Color);
                var native=go.AddComponent<IntegrationDofEffect>();binding=new DofBinding(camera,native,typeof(IntegrationDofEffect).GetMethod("OnRenderImage",BindingFlags.Instance|BindingFlags.NonPublic));binding.SetMode(DofMode.BeforeTransparents);
                preparation=UnityCameraHub.Register(new object(),"Probe DOF guard fixture",200,c=>{if(UnityCameraHub.Camera(c)==camera)binding.PrepareRender();},null,null);
                bool role=false,lighting=false;
                observation=UnityCameraHub.Register(new object(),"Probe role observer",650,null,c=>{if(UnityCameraHub.Camera(c)==camera){role=(c.Role&CameraRole.Probe)!=0;lighting=Shader.GetGlobalVector("_TomBSDFSceneParams").x==1;}},null);
                camera.Render();TomSceneBufferSnapshot snapshot;
                Assert(report,"Explicit SH camera is classified before participants run",role);
                Assert(report,"Probe role excludes screen buffers and early DOF",service.Buffers.TextureCount==0 && !service.Buffers.TryGetSnapshot(camera,out snapshot) && binding.EarlyExecutions==0);
                Assert(report,"Probe role does not suppress legitimate lighting packet",lighting);
            }
            finally
            {
                if(observation!=null)observation.Dispose();if(preparation!=null)preparation.Dispose();if(binding!=null)binding.Dispose();if(buffers!=null)buffers.Dispose();if(service)Object.DestroyImmediate(service.gameObject);if(sky!=null)sky.Dispose();
                Object.DestroyImmediate(go);target.Release();Object.DestroyImmediate(target);
            }
            Assert(report,"Probe/screen guard teardown leaves no callbacks or roles",!UnityCameraHub.IsAttached && CameraRoles.Count==0 && RenderResourceLease.Count==0);
            GameObject first=null,second=null;TomProbeCaptureSky ambiguous=null;
            try
            {
                first=new GameObject("Coincident probe A");second=new GameObject("Coincident probe B");first.SetActive(false);second.SetActive(false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(first,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(second,scene);
                first.transform.position=second.transform.position=new Vector3(1711,521,737);
                var a=first.AddComponent<ReflectionProbe>();var b=second.AddComponent<ReflectionProbe>();
                foreach(var probe in new[]{a,b}){probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.ViaScripting;probe.resolution=16;}
                first.SetActive(true);second.SetActive(true);ambiguous=new TomProbeCaptureSky(new TomProbeSettings{skybox=false},shader);
                bool rejected=false;try{ambiguous.Bind(a);}catch(InvalidOperationException error){rejected=error.Message.Contains("ambiguous");}
                Assert(report,"Coincident native owners are rejected before role claim",rejected && CameraRoles.Count==0 && !UnityCameraHub.IsAttached);
            }
            finally{if(ambiguous!=null)ambiguous.Dispose();if(first)Object.DestroyImmediate(first);if(second)Object.DestroyImmediate(second);}
        }
        static void Scheduler(Report report,UnityEngine.SceneManagement.Scene scene,Shader shader)
        {
            Assert(report,"No pre-existing scheduler is taken over",Resources.FindObjectsOfTypeAll<TomProbeCaptureScheduler>().Length==0);
            GameObject shObject=null,reflectionObject=null;int oldBudget=TomProbeCaptureScheduler.SamplesPerFrame;
            try
            {
                shObject=new GameObject("Scheduled SH fixture");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(shObject,scene);
                var volume=shObject.AddComponent<TomSHProbeVolume>();volume.gridResolution=new Vector3Int(2,2,2);volume.captureResolution=8;volume.cullingMask=0;volume.clearFlags=CameraClearFlags.SolidColor;volume.backgroundColor=new Color(.2f,.3f,.4f,1);volume.capturesPerFrame=1;
                var sh=shObject.AddComponent<TomProbeControl>();sh.captureBackgroundShader=shader;
                reflectionObject=new GameObject("Queued reflection fixture");reflectionObject.SetActive(false);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(reflectionObject,scene);
                var probe=reflectionObject.AddComponent<ReflectionProbe>();probe.enabled=false;probe.refreshMode=ReflectionProbeRefreshMode.ViaScripting;
                var reflection=reflectionObject.AddComponent<TomReflectionProbeController>();reflection.probe=probe;reflection.resolution=16;
                var control=reflectionObject.AddComponent<TomProbeControl>();control.captureBackgroundShader=shader;reflectionObject.SetActive(true);
                TomProbeCaptureScheduler.Register(sh);TomProbeCaptureScheduler.Register(control);sh.Poll(0);control.Poll(0);
                TomProbeCaptureScheduler.Request(sh,2,0);TomProbeCaptureScheduler.SamplesPerFrame=1;TomProbeCaptureScheduler.Tick(1);
                Assert(report,"Shared refresh batch selects SH before reflection",TomProbeCaptureScheduler.IsActive(sh) && !TomProbeCaptureScheduler.IsActive(control));
                if(!report.baseline)Assert(report,"Scheduler owns one cross-frame capture lane",CaptureJobs.Count==1);
                for(int sample=0;sample<8;sample++)TomProbeCaptureScheduler.Tick(1.1f+sample*.1f);
                Assert(report,"Scheduler completes budgeted SH before another job",sh.CompletedCaptures==1 && control.CompletedCaptures==0 && !TomProbeCaptureScheduler.IsActive(sh));
                if(!report.baseline)Assert(report,"Successful capture releases cross-frame lane",CaptureJobs.Count==0);
                TomProbeCaptureScheduler.Request(sh,2,3);TomProbeCaptureScheduler.Tick(4);
                Assert(report,"Next batch again starts one SH job",TomProbeCaptureScheduler.IsActive(sh));
                TomProbeCaptureScheduler.Tick(125);
                Assert(report,"Original 120-second timeout cancels capture",!TomProbeCaptureScheduler.IsActive(sh) && sh.Status.StartsWith("Failed: Probe capture timed out.") && !volume.IsCapturing);
                if(!report.baseline)Assert(report,"Timeout releases capture lane/roles/resources",CaptureJobs.Count==0 && CameraRoles.Count==0 && RenderResourceLease.Count==0 && !UnityCameraHub.IsAttached);
                TomProbeCaptureScheduler.Unregister(sh);TomProbeCaptureScheduler.Unregister(control);
                Assert(report,"Unregister removes both queued controls",TomProbeCaptureScheduler.RegisteredCount==0);
            }
            finally
            {
                if(shObject)Object.DestroyImmediate(shObject);if(reflectionObject)Object.DestroyImmediate(reflectionObject);
                foreach(var scheduler in Resources.FindObjectsOfTypeAll<TomProbeCaptureScheduler>())if(scheduler)Object.DestroyImmediate(scheduler.gameObject);
                TomProbeCaptureScheduler.SamplesPerFrame=oldBudget;
            }
        }
    }
}

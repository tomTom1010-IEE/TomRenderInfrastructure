using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using TomBSDFRuntime;
using TomRenderInfrastructure;
using TomSceneEffects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TomRenderValidation
{
    public static class Step2Validation
    {
        [Serializable] public sealed class Check { public string name;public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed;
            public string unity,error,coreAssembly,bsdfAssembly,dofAssembly;
            public string scope="Actual imported authoring runtime, explicit shared scopes/scene-color routing. Blur surrogate is not native Studio DOF; no Bundle build or game operation. CPU render intervals are not GPU timings.";
            public List<Check> checks=new List<Check>();
            public List<double> nativeCpuMs=new List<double>(),earlyCpuMs=new List<double>();
            public int nativePixelDifference,earlyPixelDifference,opaqueSnapshotDifference;
            public long publications;
        }
        static void Assert(Report report,string name,bool pass)
        {report.checks.Add(new Check{name=name,passed=pass});if(!pass)throw new InvalidOperationException("Step 2: "+name);}
        static T Own<T>(List<Object> owned,T value)where T:Object{owned.Add(value);return value;}
        static Color32[] Read(RenderTexture texture,string path)
        {
            var before=RenderTexture.active;var read=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true);
            try{RenderTexture.active=texture;read.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);read.Apply(false,false);if(path!=null)File.WriteAllBytes(path,read.EncodeToPNG());return read.GetPixels32();}
            finally{RenderTexture.active=before;Object.DestroyImmediate(read);}
        }
        static Color32[] Load(string path)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try{if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false))throw new IOException(path);return texture.GetPixels32();}
            finally{Object.DestroyImmediate(texture);}
        }
        static int Difference(Color32[] a,Color32[] b)
        {
            if(a.Length!=b.Length)return int.MaxValue;int maximum=0;
            for(int i=0;i<a.Length;i++)maximum=Math.Max(maximum,Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Abs(a[i].b-b[i].b))));return maximum;
        }
        static int Red(Color32[] pixels){return pixels.Count(p=>p.r>2*p.g && p.r>2*p.b && p.r>100);}
        static int Listeners(Camera.CameraCallback value){return value==null?0:value.GetInvocationList().Length;}
        static string Repository(string path)
        {
            var directory=new DirectoryInfo(Path.GetFullPath(path));
            while(directory!=null){if(File.Exists(Path.Combine(directory.FullName,"release.manifest.json")))return directory.FullName;directory=directory.Parent;}
            throw new InvalidOperationException("Evidence directory must be inside the infrastructure repository");
        }
        static void Write(string path,Report report)
        {
            using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(Report)).WriteObject(stream,report);File.WriteAllText(path,Encoding.UTF8.GetString(stream.ToArray()));}
        }

        public static string Run(string output)
        {
            if(Directory.Exists(output))throw new InvalidOperationException("Evidence directory already exists");Directory.CreateDirectory(output);
            var root=Repository(output);var report=new Report{unity=Application.unityVersion,
                coreAssembly=typeof(UnityCameraHub).Assembly.Location,bsdfAssembly=typeof(TomBSDFSceneService).Assembly.Location,dofAssembly=typeof(DofBinding).Assembly.GetName().Name};
            var owned=new List<Object>();var scene=EditorSceneManager.NewPreviewScene();var previousRT=RenderTexture.active;
            TomBSDFSceneService service=null;DofBinding binding=null;IDisposable dofRegistration=null,nestedRegistration=null;
            TomSceneBufferLease mainLease=null,childLease=null;int pre=Listeners(Camera.onPreCull),begin=Listeners(Camera.onPreRender),end=Listeners(Camera.onPostRender);
            long initialPublications=UnityCameraHub.ColorRoutes.Publications;
            try
            {
                Assert(report,"No pre-existing service or user scene state is taken over",!TomBSDFSceneService.Current);
                Assert(report,"Authoring DOF remains in Assembly-CSharp",report.dofAssembly=="Assembly-CSharp");
                Assert(report,"Exactly one Core assembly is loaded",AppDomain.CurrentDomain.GetAssemblies().Count(a=>a.GetName().Name=="TomRender.Core")==1);
                var go=Own(owned,new GameObject("Step 2 camera"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
                var camera=go.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.4f;
                camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.07f,.09f,0);camera.renderingPath=RenderingPath.Forward;camera.cullingMask=1<<30;
                var target=Own(owned,new RenderTexture(768,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));target.Create();camera.targetTexture=target;
                go.AddComponent<IntegrationOpaqueMarker>();var native=go.AddComponent<IntegrationDofEffect>();go.AddComponent<IntegrationLateMarker>();
                var pattern=Own(owned,new Texture2D(128,128,TextureFormat.RGBA32,false,true));pattern.filterMode=FilterMode.Point;var pixels=new Color32[128*128];
                for(int y=0;y<128;y++)for(int x=0;x<128;x++)pixels[y*128+x]=((x/4+y/4)%2)==0?new Color32(230,235,245,255):new Color32(35,50,65,255);
                pattern.SetPixels32(pixels);pattern.Apply();
                var back=Own(owned,GameObject.CreatePrimitive(PrimitiveType.Quad));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(back,scene);back.layer=30;back.transform.position=new Vector3(0,0,4);back.transform.localScale=new Vector3(4.1f,2.7f,1);
                var background=Own(owned,new Material(Shader.Find("Unlit/Texture")));background.mainTexture=pattern;back.GetComponent<Renderer>().sharedMaterial=background;
                var transparent=Own(owned,new Material(Shader.Find("Sprites/Default")));transparent.color=new Color(1,.24f,.035f,.85f);
                for(int i=0;i<17;i++){var bar=Own(owned,GameObject.CreatePrimitive(PrimitiveType.Quad));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(bar,scene);bar.layer=30;bar.transform.position=new Vector3(-1.7f+i*.21f,0,2);bar.transform.localScale=new Vector3(.025f,1.8f,1);bar.GetComponent<Renderer>().sharedMaterial=transparent;}
                var oldBaseline=Path.Combine(root,"artifacts/integration/accepted-step1/original-dof-baseline");
                camera.Render();var unbound=Read(target,Path.Combine(output,"00-unbound.png"));
                Assert(report,"Unbound fixture matches frozen original DOF baseline",Difference(unbound,Load(Path.Combine(oldBaseline,"01-native-unbound.png")))==0);

                service=TomBSDFSceneService.EnsureInstance();service.extensionEnabled=true;service.sceneBuffersEnabled=true;service.shadowReceiversEnabled=false;service.captureReferenceEnabled=false;
                service.sceneBufferResources=AssetDatabase.LoadAssetAtPath<TomBSDFSceneBufferResources>(TomBSDFSceneBufferResources.AssetPath);
                Assert(report,"Original depth-copy resource deserializes after ABI-preserving DLL import",service.sceneBufferResources && service.sceneBufferResources.IsValid);
                binding=new DofBinding(camera,native,typeof(IntegrationDofEffect).GetMethod("OnRenderImage",BindingFlags.NonPublic|BindingFlags.Instance));
                dofRegistration=UnityCameraHub.Register(new object(),"Step 2 DOF fixture",200,c=>{if(UnityCameraHub.Camera(c)==camera)binding.PrepareRender();},null,null);
                Assert(report,"BSDF and DOF share one callback set",Listeners(Camera.onPreCull)==pre+1 && Listeners(Camera.onPreRender)==begin+1 && Listeners(Camera.onPostRender)==end+1);
                camera.Render();Assert(report,"No demand creates no scene buffers",service.Buffers.TextureCount==0 && camera.GetCommandBuffers(TomBSDFSceneBuffers.CaptureEvent).Length==0);
                mainLease=service.Buffers.Acquire(camera,TomSceneBufferFlags.Color|TomSceneBufferFlags.Depth);
                var originalDepth=camera.depthTextureMode;binding.SetMode(DofMode.Native);camera.Render();
                var nativeOutput=Read(target,Path.Combine(output,"01-native.png"));TomSceneBufferSnapshot snapshot;
                Assert(report,"Native demand produces a completed snapshot",service.Buffers.TryGetSnapshot(camera,out snapshot));
                var nativeOpaque=Read(snapshot.color,Path.Combine(output,"01-native-opaque.png"));
                report.nativePixelDifference=Difference(nativeOutput,Load(Path.Combine(oldBaseline,"02-native-default.png")));
                Assert(report,"Native output remains pixel-identical to frozen baseline",report.nativePixelDifference==0);
                Assert(report,"Native opaque snapshot excludes transparent bars",Red(nativeOpaque)==0);
                Assert(report,"Depth request is restored after render",camera.depthTextureMode==originalDepth);
                Assert(report,"Color/depth demand owns two textures and one event buffer",service.Buffers.TextureCount==2 && camera.GetCommandBuffers(TomBSDFSceneBuffers.CaptureEvent).Length==1);
                int sequence=snapshot.sequence;long publications=UnityCameraHub.ColorRoutes.Publications;
                binding.SetMode(DofMode.BeforeTransparents);camera.Render();
                var earlyOutput=Read(target,Path.Combine(output,"02-early.png"));
                Assert(report,"Early path produces a completed snapshot",service.Buffers.TryGetSnapshot(camera,out snapshot));
                var earlyOpaque=Read(snapshot.color,Path.Combine(output,"02-early-opaque.png"));
                report.earlyPixelDifference=Difference(earlyOutput,Load(Path.Combine(oldBaseline,"03-before-transparents.png")));
                report.opaqueSnapshotDifference=Difference(earlyOpaque,Load(Path.Combine(oldBaseline,"03-early-opaque-snapshot.png")));
                Assert(report,"Early output remains pixel-identical to frozen baseline",report.earlyPixelDifference==0);
                Assert(report,"Actual BSDF color buffer contains successful opaque DOF",report.opaqueSnapshotDifference<=1 && Difference(nativeOpaque,earlyOpaque)>10);
                Assert(report,"Scene-color routing does not move capture past transparency",Red(earlyOpaque)==0 && Red(earlyOutput)>500);
                Assert(report,"One completed early result and one late passthrough",binding.EarlyExecutions==1 && binding.LateCopies==1 && UnityCameraHub.ColorRoutes.Publications==publications+1);
                Assert(report,"Route re-recording keeps one event buffer and one sequence",camera.GetCommandBuffers(TomBSDFSceneBuffers.CaptureEvent).Length==1 && snapshot.sequence==sequence+1);
                camera.Render();Assert(report,"Repeated same-frame camera render executes once again",binding.EarlyExecutions==2 && binding.LateCopies==2);
                Assert(report,"Completed routes do not retain consumers or entries",UnityCameraHub.ColorRoutes.EntryCount==0 && UnityCameraHub.ColorRoutes.LeaseCount==0 && UnityCameraHub.Coordinator.ActiveDepth==0);

                publications=UnityCameraHub.ColorRoutes.Publications;native.FailNext=true;camera.Render();
                Assert(report,"Failed early effect falls back without publishing completed color",binding.Failures==1 && UnityCameraHub.ColorRoutes.Publications==publications);
                Assert(report,"Failed early effect retains native final appearance",Difference(nativeOutput,Read(target,Path.Combine(output,"03-failure-native.png")))==0);
                Assert(report,"Failed early effect still has native opaque snapshot",
                    service.Buffers.TryGetSnapshot(camera,out snapshot)
                    && Difference(nativeOpaque,Read(snapshot.color,Path.Combine(output,"03-failure-opaque.png")))<=1);
                binding.Dispose();binding=new DofBinding(camera,native,typeof(IntegrationDofEffect).GetMethod("OnRenderImage",BindingFlags.NonPublic|BindingFlags.Instance));binding.SetMode(DofMode.Native);

                var childObject=Own(owned,new GameObject("Step 2 nested camera"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(childObject,scene);
                var child=childObject.AddComponent<Camera>();child.CopyFrom(camera);child.enabled=false;child.scene=scene;
                var childTarget=Own(owned,new RenderTexture(256,192,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));childTarget.Create();child.targetTexture=childTarget;
                childLease=service.Buffers.Acquire(child,TomSceneBufferFlags.Color|TomSceneBufferFlags.Depth);
                bool nested=false,restored=false;
                nestedRegistration=UnityCameraHub.Register(new object(),"Step 2 nested renderer",500,null,c=>{
                    if(UnityCameraHub.Camera(c)!=camera || nested)return;nested=true;var packet=Shader.GetGlobalVector("_TomBSDFSceneParams");child.Render();
                    restored=UnityCameraHub.Coordinator.Current==c && Shader.GetGlobalVector("_TomBSDFSceneParams")==packet;
                },null);
                camera.Render();Assert(report,"Nested render restores parent scope and area packet",nested && restored && UnityCameraHub.Coordinator.ActiveDepth==0);
                Assert(report,"Nested cameras each retain their own completed snapshot",service.Buffers.TryGetSnapshot(child,out snapshot) && snapshot.width==256 && service.Buffers.TryGetSnapshot(camera,out snapshot) && snapshot.width==768);
                nestedRegistration.Dispose();nestedRegistration=null;

                var parentContext=UnityCameraHub.Coordinator.Prepare(camera,camera.GetInstanceID(),Time.frameCount,target.GetInstanceID(),768,512,CameraRole.View);UnityCameraHub.Coordinator.Begin(camera);
                var parentPacket=Shader.GetGlobalVector("_TomBSDFSceneParams");
                UnityCameraHub.Coordinator.Prepare(child,child.GetInstanceID(),Time.frameCount,childTarget.GetInstanceID(),256,192,CameraRole.View);
                typeof(TomBSDFSceneService).GetMethod("EndCamera",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(Camera),typeof(bool)},null).Invoke(service,new object[]{child,true});
                Assert(report,"Cleanup of a child that never began preserves parent globals",Shader.GetGlobalVector("_TomBSDFSceneParams")==parentPacket);
                Assert(report,"Aborted pre-Begin capture never becomes a snapshot",!service.Buffers.TryGetSnapshot(child,out snapshot));
                UnityCameraHub.Coordinator.End(child);Assert(report,"Idempotent child cleanup cannot pop parent",UnityCameraHub.Coordinator.Current==parentContext && Shader.GetGlobalVector("_TomBSDFSceneParams")==parentPacket);
                UnityCameraHub.Coordinator.End(camera);
                UnityCameraHub.Coordinator.Prepare(child,child.GetInstanceID(),Time.frameCount,childTarget.GetInstanceID(),256,192,CameraRole.View);UnityCameraHub.Coordinator.AbortAll();
                Assert(report,"Aborted render releases its prepared event buffer",child.GetCommandBuffers(TomBSDFSceneBuffers.CaptureEvent).Length==0 && !service.Buffers.TryGetSnapshot(child,out snapshot));
                childLease.Dispose();childLease=null;Object.DestroyImmediate(childObject);

                service.extensionEnabled=false;camera.Render();Assert(report,"Extension off releases GPU copies and invalidates snapshots",service.Buffers.TextureCount==0 && camera.GetCommandBuffers(TomBSDFSceneBuffers.CaptureEvent).Length==0 && !service.Buffers.TryGetSnapshot(camera,out snapshot));
                Assert(report,"Extension off keeps native final rendering",Difference(nativeOutput,Read(target,null))==0);
                service.extensionEnabled=true;camera.Render();Assert(report,"Extension re-enable recreates valid requested buffers",service.Buffers.TryGetSnapshot(camera,out snapshot) && service.Buffers.TextureCount==2);
                service.sceneBufferResources=null;camera.Render();Assert(report,"Missing resources use an explicit unavailable fallback",service.Buffers.TextureCount==0 && !service.Buffers.TryGetSnapshot(camera,out snapshot));
                service.sceneBufferResources=AssetDatabase.LoadAssetAtPath<TomBSDFSceneBufferResources>(TomBSDFSceneBufferResources.AssetPath);camera.Render();
                Assert(report,"Resource restoration recovers without another copy pass",service.Buffers.TryGetSnapshot(camera,out snapshot) && camera.GetCommandBuffers(TomBSDFSceneBuffers.CaptureEvent).Length==1);
                snapshot.color.Release();camera.Render();Assert(report,"Lost GPU allocation is recreated for the next render",service.Buffers.TryGetSnapshot(camera,out snapshot) && snapshot.color.IsCreated());

                for(int pair=0;pair<8;pair++)for(int order=0;order<2;order++)
                {
                    bool early=(pair%2==0)==(order==1);binding.SetMode(early?DofMode.BeforeTransparents:DofMode.Native);
                    camera.Render();camera.Render();var timer=Stopwatch.StartNew();for(int i=0;i<5;i++)camera.Render();timer.Stop();
                    (early?report.earlyCpuMs:report.nativeCpuMs).Add(timer.Elapsed.TotalMilliseconds/5);
                }
                Assert(report,"Raw paired routing CPU timings completed",report.nativeCpuMs.Count==8 && report.earlyCpuMs.Count==8);
                report.publications=UnityCameraHub.ColorRoutes.Publications-initialPublications;
                mainLease.Dispose();mainLease=null;Assert(report,"Last buffer lease removes all capture GPU work",service.Buffers.TextureCount==0 && camera.GetCommandBuffers(TomBSDFSceneBuffers.CaptureEvent).Length==0);
                binding.Dispose();binding=null;dofRegistration.Dispose();dofRegistration=null;
                Object.DestroyImmediate(service.gameObject);service=null;
                Assert(report,"Module teardown removes registrations, driver and route state",!UnityCameraHub.IsAttached && UnityCameraHub.Coordinator.RegistrationCount==0 && UnityCameraHub.ColorRoutes.EntryCount==0 && UnityCameraHub.ColorRoutes.LeaseCount==0);
                Assert(report,"Native callbacks return to original counts",Listeners(Camera.onPreCull)==pre && Listeners(Camera.onPreRender)==begin && Listeners(Camera.onPostRender)==end);
                Assert(report,"No scene-service singleton remains",!TomBSDFSceneService.Current);
                report.passed=true;
            }
            catch(Exception error){report.error=error.ToString();throw;}
            finally
            {
                if(nestedRegistration!=null)nestedRegistration.Dispose();if(binding!=null)binding.Dispose();if(dofRegistration!=null)dofRegistration.Dispose();
                UnityCameraHub.Coordinator.AbortAll();if(childLease!=null)childLease.Dispose();if(mainLease!=null)mainLease.Dispose();if(service)Object.DestroyImmediate(service.gameObject);
                RenderTexture.active=previousRT;for(int i=owned.Count-1;i>=0;i--)if(owned[i]){var texture=owned[i]as RenderTexture;if(texture)texture.Release();Object.DestroyImmediate(owned[i]);}EditorSceneManager.ClosePreviewScene(scene);
                Write(Path.Combine(output,"report.json"),report);
            }
            return Path.Combine(output,"report.json");
        }
    }

    [ExecuteAlways] public sealed class IntegrationOpaqueMarker:MonoBehaviour
    {
        [ImageEffectOpaque]void OnRenderImage(RenderTexture source,RenderTexture destination){Graphics.Blit(source,destination);}
    }
    [ExecuteAlways] public sealed class IntegrationLateMarker:MonoBehaviour
    {
        void OnRenderImage(RenderTexture source,RenderTexture destination){Graphics.Blit(source,destination);}
    }
    [ExecuteAlways] public sealed class IntegrationDofEffect:MonoBehaviour
    {
        public bool FailNext;
        void OnRenderImage(RenderTexture source,RenderTexture destination)
        {
            if(!DofBinding.BeforeNative(this,source,destination))return;
            if(FailNext){FailNext=false;throw new InvalidOperationException("Explicit integration fixture failure");}
            var low=RenderTexture.GetTemporary(Math.Max(1,source.width/32),Math.Max(1,source.height/32),0,source.format,RenderTextureReadWrite.Linear);
            try{low.filterMode=FilterMode.Bilinear;Graphics.Blit(source,low);Graphics.Blit(low,destination);}finally{RenderTexture.ReleaseTemporary(low);}
        }
    }
}

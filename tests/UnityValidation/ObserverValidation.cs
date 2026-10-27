using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using TomRenderInfrastructure;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TomRenderValidation
{
    public static class ObserverValidation
    {
        [Serializable] public sealed class Check { public string name; public bool passed; }
        [Serializable] public sealed class Identity { public string path, guid, type, assembly; public long localFileId; }
        [Serializable] public sealed class Report
        {
            public bool passed;
            public string unity, scope, error;
            public List<Check> checks = new List<Check>();
            public List<Identity> authoringIdentities = new List<Identity>();
            public List<string> trace = new List<string>();
            public List<double> baselineMilliseconds = new List<double>(), observerMilliseconds = new List<double>();
            public double baselineMedianMs, observerMedianMs, pairedMedianDeltaMs;
            public int maximumPixelDifference, coreGpuResources, preCullDelta, preRenderDelta, postRenderDelta;
            public bool timingTraceEnabled;
            public string[] materials;
        }

        static void Assert(Report report, string name, bool pass)
        {
            report.checks.Add(new Check { name=name, passed=pass });
            if (!pass) throw new InvalidOperationException("Observer validation: " + name);
        }
        static T Own<T>(List<Object> owned, T value) where T : Object { owned.Add(value); return value; }
        static Color32[] Save(RenderTexture texture, string path)
        {
            var before=RenderTexture.active; var read=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true);
            try { RenderTexture.active=texture; read.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); read.Apply(false,false); File.WriteAllBytes(path,read.EncodeToPNG()); return read.GetPixels32(); }
            finally { RenderTexture.active=before; Object.DestroyImmediate(read); }
        }
        static int Difference(Color32[] a, Color32[] b)
        {
            if(a.Length!=b.Length)return int.MaxValue;
            int maximum=0;
            for(int i=0;i<a.Length;i++)maximum=Math.Max(maximum,Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Max(Math.Abs(a[i].b-b[i].b),Math.Abs(a[i].a-b[i].a)))));
            return maximum;
        }
        static int ListenerCount(Camera.CameraCallback value) { return value==null?0:value.GetInvocationList().Length; }
        static int[] GpuObjects()
        {
            return Resources.FindObjectsOfTypeAll<Object>().Where(o=>o && (o is RenderTexture || o is Material || o is Mesh))
                .Select(o=>o.GetInstanceID()).OrderBy(id=>id).ToArray();
        }
        static string[] Buffers(Camera camera)
        {
            var result=new List<string>();
            foreach(CameraEvent stage in Enum.GetValues(typeof(CameraEvent)))
                foreach(var command in camera.GetCommandBuffers(stage))result.Add(stage+":"+command.name+":"+command.sizeInBytes);
            result.Sort(StringComparer.Ordinal);return result.ToArray();
        }
        static double Median(List<double> values) { var sorted=values.OrderBy(v=>v).ToArray(); return sorted[sorted.Length/2]; }

        public static string Run(string output)
        {
            Directory.CreateDirectory(output);
            var report=new Report { unity=Application.unityVersion,
                scope="Phase 1 observer only. Existing six TomBSDF presets, isolated preview scene, no Bundle build/deployment. CPU Camera.Render intervals are not GPU shader timings." };
            var owned=new List<Object>(); var scene=EditorSceneManager.NewPreviewScene();
            var oldRT=RenderTexture.active; IDisposable observation=null, nestedObservation=null;
            int oldPreCull=ListenerCount(Camera.onPreCull),oldPreRender=ListenerCount(Camera.onPreRender),oldPost=ListenerCount(Camera.onPostRender);
            try
            {
                Assert(report,"Core starts detached and with no participants",!UnityCameraHub.IsAttached && UnityCameraHub.Coordinator.RegistrationCount==0);
                var go=Own(owned,new GameObject("TomRender observation camera")); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
                var camera=go.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;
                camera.transform.position=new Vector3(0,0,-7);camera.transform.rotation=Quaternion.identity;
                camera.orthographic=true;camera.orthographicSize=2.1f;camera.nearClipPlane=.1f;camera.farClipPlane=30;
                camera.renderingPath=RenderingPath.Forward;camera.allowHDR=true;camera.allowMSAA=false;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.05f,.075f,.1f,1);camera.cullingMask=1<<30;
                var target=Own(owned,new RenderTexture(768,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));target.Create();camera.targetTexture=target;
                var otherTarget=Own(owned,new RenderTexture(320,192,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));otherTarget.Create();
                var lightObject=Own(owned,new GameObject("TomRender validation key"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,scene);
                var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.shadows=LightShadows.None;light.cullingMask=1<<30;
                light.transform.rotation=Quaternion.Euler(25,-35,0);
                string[] names={"Plastic","Metal","BrushedMetal","CarPaint","ThinFilm","Glass"};report.materials=names;
                for(int i=0;i<names.Length;i++)
                {
                    var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/Mods/TomShadersBSDF/Material/Presets/m_Preset_"+names[i]+"_Realtime.mat");
                    Assert(report,"Existing preset available: "+names[i],source && source.shader && source.shader.isSupported);
                    var material=Own(owned,new Material(source));
                    var sphere=Own(owned,GameObject.CreatePrimitive(PrimitiveType.Sphere));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sphere,scene);
                    sphere.name=names[i];sphere.layer=30;sphere.transform.position=new Vector3((i%3-1)*1.7f,i<3?.95f:-.95f,0);
                    sphere.transform.localScale=Vector3.one*1.45f;sphere.GetComponent<Renderer>().sharedMaterial=material;
                }
                for(int i=0;i<3;i++)camera.Render();
                var baseline=Save(target,Path.Combine(output,"baseline-presets.png"));
                Assert(report,"Baseline is not a blank image",baseline.Select(p=>(int)p.r<<16|(int)p.g<<8|p.b).Distinct().Count()>100);
                var oldCameraDepth=camera.depthTextureMode;var oldCameraTarget=camera.targetTexture;var oldProjection=camera.projectionMatrix;
                var gpuBefore=GpuObjects();var buffersBefore=Buffers(camera);
                UnityCameraHub.Coordinator.TraceEnabled=true;
                var contexts=new List<RenderContext>();
                observation=UnityCameraHub.Observe(new object(),"validation observer",(context,phase)=>{
                    if(UnityCameraHub.Camera(context)!=camera)return;
                    if(phase==RenderPhase.Prepared)contexts.Add(context);
                    report.trace.Add(context.Id+" parent="+context.ParentId+" camera="+context.CameraId+" "+phase);
                });
                report.preCullDelta=ListenerCount(Camera.onPreCull)-oldPreCull;
                report.preRenderDelta=ListenerCount(Camera.onPreRender)-oldPreRender;
                report.postRenderDelta=ListenerCount(Camera.onPostRender)-oldPost;
                Assert(report,"Exactly one Core subscription per native camera event",report.preCullDelta==1 && report.preRenderDelta==1 && report.postRenderDelta==1);
                camera.Render();camera.Render();
                var observed=Save(target,Path.Combine(output,"observer-presets.png"));
                report.maximumPixelDifference=Difference(baseline,observed);
                Assert(report,"Observer does not change preset pixels",report.maximumPixelDifference==0);
                Assert(report,"Every repeated same-frame render has a distinct invocation",contexts.Count==2 && contexts[0].Id!=contexts[1].Id && contexts[0].Frame==contexts[1].Frame);
                Assert(report,"Observer restores empty nesting after each render",UnityCameraHub.Coordinator.ActiveDepth==0);
                Assert(report,"Observer adds no GPU objects",gpuBefore.SequenceEqual(GpuObjects()));
                Assert(report,"Observer adds no camera command buffers",buffersBefore.SequenceEqual(Buffers(camera)));
                Assert(report,"Observer does not edit camera flags/target/projection",camera.depthTextureMode==oldCameraDepth && camera.targetTexture==oldCameraTarget && camera.projectionMatrix==oldProjection);
                report.coreGpuResources=UnityCameraHub.OwnedGpuResources;
                camera.targetTexture=otherTarget;camera.Render();
                Assert(report,"Changed render target receives current descriptor",contexts.Last().TargetId==otherTarget.GetInstanceID() && contexts.Last().Width==320 && contexts.Last().Height==192);
                camera.targetTexture=target;
                var childObject=Own(owned,new GameObject("TomRender nested camera"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(childObject,scene);
                var child=childObject.AddComponent<Camera>();child.CopyFrom(camera);child.scene=scene;child.enabled=false;child.targetTexture=otherTarget;
                bool nested=false,restored=false;long parentId=0,childId=0;
                nestedObservation=UnityCameraHub.Observe(new object(),"nested observer",(context,phase)=>{
                    if(UnityCameraHub.Camera(context)==child && phase==RenderPhase.Prepared){childId=context.Id;Assert(report,"Nested camera carries enclosing invocation",context.ParentId==parentId && context.Depth==2);}
                    if(UnityCameraHub.Camera(context)==camera && phase==RenderPhase.Rendering && !nested){nested=true;parentId=context.Id;child.Render();restored=UnityCameraHub.Coordinator.Current==context;}
                });
                Assert(report,"Second participant does not add native subscriptions",ListenerCount(Camera.onPreCull)-oldPreCull==1);
                camera.Render();
                Assert(report,"Nested native Camera.Render balances and restores parent",nested && restored && childId!=0 && childId!=parentId && UnityCameraHub.Coordinator.ActiveDepth==0);
                nestedObservation.Dispose();nestedObservation=null;
                observation.Dispose();observation=null;
                Assert(report,"Last registration detaches all Core callbacks",!UnityCameraHub.IsAttached && ListenerCount(Camera.onPreCull)==oldPreCull && ListenerCount(Camera.onPreRender)==oldPreRender && ListenerCount(Camera.onPostRender)==oldPost);
                Assert(report,"Transient Core driver is destroyed",Resources.FindObjectsOfTypeAll<TomRenderDriver>().Length==0);
                Assert(report,"Idle Core releases weak camera index entries",UnityCameraHub.Coordinator.Statistics().cameraRecords==0);

                // Paired AB/BA measurements reduce order/thermal bias. The fixture
                // still reports raw CPU intervals and does not claim GPU speedups.
                UnityCameraHub.Coordinator.TraceEnabled=false;report.timingTraceEnabled=false;
                for(int pair=0;pair<16;pair++)
                {
                    for(int order=0;order<2;order++)
                    {
                        bool withObserver=(pair%2==0)==(order==1);
                        if(withObserver)observation=UnityCameraHub.Observe(new object(),"timing observer",(context,phase)=>{});
                        for(int warm=0;warm<2;warm++)camera.Render();
                        var timer=Stopwatch.StartNew();for(int n=0;n<5;n++)camera.Render();timer.Stop();
                        (withObserver?report.observerMilliseconds:report.baselineMilliseconds).Add(timer.Elapsed.TotalMilliseconds/5);
                        if(observation!=null){observation.Dispose();observation=null;}
                    }
                }
                report.baselineMedianMs=Median(report.baselineMilliseconds);report.observerMedianMs=Median(report.observerMilliseconds);
                var deltas=new List<double>();for(int i=0;i<report.baselineMilliseconds.Count;i++)deltas.Add(report.observerMilliseconds[i]-report.baselineMilliseconds[i]);
                report.pairedMedianDeltaMs=Median(deltas);
                Assert(report,"Paired CPU timing completed without active scopes",report.baselineMilliseconds.Count==16 && report.observerMilliseconds.Count==16 && UnityCameraHub.Coordinator.ActiveDepth==0 && !UnityCameraHub.IsAttached);
                foreach(string path in new[]{"Assets/Mods/TomShadersX/Runtime/TomHairFeatherCamera.cs","Assets/Mods/TomShadersX/Runtime/TomSkinSDFHead.cs","Assets/Mods/TomShadersX/Runtime/TomTessellationBounds.cs","Assets/TomSceneEffects/Runtime/TomDofOpaqueRelay.cs"})
                {
                    var script=AssetDatabase.LoadAssetAtPath<MonoScript>(path);var type=script?script.GetClass():null;
                    string guid;long fileId;AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script,out guid,out fileId);
                    report.authoringIdentities.Add(new Identity {path=path,guid=guid,localFileId=fileId,type=type==null?"unresolved":type.FullName,assembly=type==null?"unresolved":type.Assembly.GetName().Name});
                    Assert(report,"Existing authoring script resolved: "+path,script && type!=null);
                }
                foreach(string path in new[]{"Assets/Mods/TomShadersBSDF/Runtime/TomBSDF.Runtime.dll","Assets/Mods/TomLightingProbes/Runtime/TomProbe.Runtime.dll"})
                {
                    var scripts=AssetDatabase.LoadAllAssetsAtPath(path).OfType<MonoScript>().ToArray();
                    int beforeCount=report.authoringIdentities.Count;
                    foreach(var script in scripts)
                    {
                        var type=script.GetClass();if(type==null || !typeof(Object).IsAssignableFrom(type))continue;
                        string guid;long fileId;AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script,out guid,out fileId);
                        report.authoringIdentities.Add(new Identity {path=path,guid=guid,localFileId=fileId,type=type.FullName,assembly=type.Assembly.GetName().Name});
                    }
                    Assert(report,"Existing authoring DLL script identities resolved: "+path,report.authoringIdentities.Count>beforeCount);
                }
                report.passed=true;
            }
            catch(Exception error){report.error=error.ToString();throw;}
            finally
            {
                if(nestedObservation!=null)nestedObservation.Dispose();if(observation!=null)observation.Dispose();
                UnityCameraHub.Coordinator.AbortAll();UnityCameraHub.Coordinator.TraceEnabled=false;
                RenderTexture.active=oldRT;
                for(int i=owned.Count-1;i>=0;i--)if(owned[i]){var texture=owned[i] as RenderTexture;if(texture)texture.Release();Object.DestroyImmediate(owned[i]);}
                EditorSceneManager.ClosePreviewScene(scene);
                using(var stream=new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(Report)).WriteObject(stream,report);
                    File.WriteAllText(Path.Combine(output,"report.json"),Encoding.UTF8.GetString(stream.ToArray()));
                }
            }
            return Path.Combine(output,"report.json");
        }
    }
}

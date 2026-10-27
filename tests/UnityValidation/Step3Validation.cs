using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using TomBSDFRuntime;
using TomRenderInfrastructure;
using TomShadersX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace TomRenderValidation
{
    public static class Step3Validation
    {
        [Serializable] public sealed class Check { public string name;public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed,baseline;public string error,unity;
            public string scope="Isolated Editor fixture, unchanged TomX shaders. CPU Camera.Render timings are not GPU timings. No game deployment or Bundle build.";
            public List<Check> checks=new List<Check>();public List<double> cpuMs=new List<double>();
            public int maximumPixelDifference,sourceDraws,filterPasses;
        }
        static void Assert(Report report,string name,bool pass)
        {report.checks.Add(new Check{name=name,passed=pass});if(!pass)throw new InvalidOperationException("Step 3: "+name);}
        static T Own<T>(List<Object> objects,T value)where T:Object{objects.Add(value);return value;}
        static Color32[] Read(RenderTexture texture,string path)
        {
            var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true);
            try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();var result=pixels.GetPixels32();if(path!=null)File.WriteAllBytes(path,pixels.EncodeToPNG());return result;}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(pixels);}
        }
        static int Difference(Color32[] current,string path)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try
            {
                if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false))throw new IOException(path);var old=texture.GetPixels32();
                if(old.Length!=current.Length)return int.MaxValue;int max=0;
                for(int i=0;i<old.Length;i++)max=Math.Max(max,Math.Max(Math.Abs(old[i].r-current[i].r),Math.Max(Math.Abs(old[i].g-current[i].g),Math.Abs(old[i].b-current[i].b))));return max;
            }
            finally{Object.DestroyImmediate(texture);}
        }
        static string Root(string output)
        {var directory=new DirectoryInfo(output);while(directory!=null){if(File.Exists(Path.Combine(directory.FullName,"release.manifest.json")))return directory.FullName;directory=directory.Parent;}throw new IOException("Missing repository root");}
        static GameObject Quad(List<Object> owned,UnityEngine.SceneManagement.Scene scene,string name,Vector3 position,Vector3 scale,Material material)
        {
            var value=Own(owned,GameObject.CreatePrimitive(PrimitiveType.Quad));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(value,scene);
            value.name=name;value.layer=30;value.transform.position=position;value.transform.localScale=scale;value.GetComponent<Renderer>().sharedMaterial=material;return value;
        }
        public static string Run(string output,bool baseline)
        {
            if(Directory.Exists(output))throw new InvalidOperationException("Evidence directory already exists");Directory.CreateDirectory(output);
            var root=Root(output);var frozen=Path.Combine(root,"artifacts/integration/step3-frozen-feather-v2");
            var report=new Report{baseline=baseline,unity=Application.unityVersion};var owned=new List<Object>();var scene=EditorSceneManager.NewPreviewScene();
            var previous=RenderTexture.active;TomHairFeatherCamera helper=null;
            int callbacks=Camera.onPreCull==null?0:Camera.onPreCull.GetInvocationList().Length;
            try
            {
                Assert(report,"No pre-existing active Core owners",!UnityCameraHub.IsAttached && UnityCameraHub.Coordinator.RegistrationCount==0);
                var cameraObject=Own(owned,new GameObject("Feather integration camera"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.orthographicSize=1.5f;
                camera.transform.position=new Vector3(0,0,-6);camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.cullingMask=1<<30;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.16f,.22f,1);camera.renderingPath=RenderingPath.Forward;camera.allowHDR=false;camera.allowMSAA=false;
                var target=Own(owned,new RenderTexture(512,384,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));target.Create();camera.targetTexture=target;
                var lightObject=Own(owned,new GameObject("Feather validation key"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,scene);
                var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.shadows=LightShadows.None;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(15,-20,0);
                var hairMaterial=Own(owned,new Material(Shader.Find("tom/HairX")));hairMaterial.SetFloat("_HairFrontMode",2);hairMaterial.SetFloat("_HairFeatherWidth",16);hairMaterial.SetColor("_BaseColor",new Color(.1f,.3f,.45f,1));hairMaterial.renderQueue=2475;
                var eyeMaterial=Own(owned,new Material(Shader.Find("tom/EyeWX")));eyeMaterial.SetColor("_BaseColor",new Color(.9f,.4f,.1f,1));eyeMaterial.renderQueue=2472;
                var texture=Own(owned,new Texture2D(64,64,TextureFormat.RGBA32,false,true));var pixels=new Color32[64*64];
                for(int y=0;y<64;y++)for(int x=0;x<64;x++){bool inside=(new Vector2(x-31.5f,y-31.5f)).sqrMagnitude<24*24;pixels[y*64+x]=new Color32(255,170,75,(byte)(inside?255:0));}texture.SetPixels32(pixels);texture.Apply();eyeMaterial.SetTexture("_MainTex",texture);
                var eye=Quad(owned,scene,"Eye writer",new Vector3(0,0,1),new Vector3(2.3f,1.7f,1),eyeMaterial);
                var hair=Quad(owned,scene,"Hair receiver",Vector3.zero,new Vector3(3.5f,2.5f,1),hairMaterial);
                helper=cameraObject.AddComponent<TomHairFeatherCamera>();helper.fieldShader=Shader.Find("Hidden/TomX/HairFeather");helper.receivers=new[]{hair.GetComponent<Renderer>()};
                helper.sources=new[]{new TomHairFeatherCamera.Source{renderer=eye.GetComponent<Renderer>(),materialIndex=0,passName="StencilMask"}};
                var views=new[]{"full","partial","world"};
                for(int index=0;index<views.Length;index++)
                {
                    camera.rect=index==1?new Rect(.15f,.2f,.65f,.6f):new Rect(0,0,1,1);
                    hairMaterial.SetFloat("_HairFeatherWidthMode",index==2?1:0);hairMaterial.SetFloat("_HairFeatherWorldWidth",.03f);
                    camera.Render();camera.Render();
                    Assert(report,"Feather schedules eligible writer: "+views[index],helper.Active && helper.SourceDraws==1 && helper.FilterPasses>0);
                    var image=Read(target,Path.Combine(output,views[index]+".png"));
                    Assert(report,"Feather output is nonblank: "+views[index],image.Select(p=>((int)p.r<<16)|((int)p.g<<8)|p.b).Distinct().Count()>10);
                    Read(helper.MaskTexture,Path.Combine(output,views[index]+"-mask.png"));Read(helper.DistanceTexture,Path.Combine(output,views[index]+"-distance.png"));
                    if(!baseline)
                    {
                        int difference=Difference(image,Path.Combine(frozen,views[index]+".png"));report.maximumPixelDifference=Math.Max(report.maximumPixelDifference,difference);
                        Assert(report,"Frozen Feather pixels match: "+views[index],difference==0);
                        Assert(report,"Binary proxy matches: "+views[index],Difference(Read(helper.MaskTexture,null),Path.Combine(frozen,views[index]+"-mask.png"))==0);
                        Assert(report,"Distance field matches: "+views[index],Difference(Read(helper.DistanceTexture,null),Path.Combine(frozen,views[index]+"-distance.png"))==0);
                        Assert(report,"One preparation per render, no legacy OnPreCull",typeof(TomHairFeatherCamera).GetMethod("OnPreCull",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)==null);
                    }
                }
                report.sourceDraws=helper.SourceDraws;report.filterPasses=helper.FilterPasses;
                camera.rect=new Rect(0,0,1,1);hairMaterial.SetFloat("_HairFeatherWidthMode",0);
                for(int sample=0;sample<12;sample++){camera.Render();var watch=Stopwatch.StartNew();for(int i=0;i<5;i++)camera.Render();watch.Stop();report.cpuMs.Add(watch.Elapsed.TotalMilliseconds/5);}
                helper.sources[0].passName="MissingValidationPass";camera.Render();
                Assert(report,"Missing writer pass retains hard fallback",!helper.Active && helper.MissingPassSources==1);
                helper.sources[0].passName="StencilMask";eyeMaterial.renderQueue=3000;camera.Render();
                Assert(report,"Late writer cannot invent stencil coverage",!helper.Active && helper.LateSources==1);
                eyeMaterial.renderQueue=2472;camera.Render();Assert(report,"Writer recovery recreates field",helper.Active);
                if(!baseline)
                {
                    helper.DistanceTexture.Release();camera.Render();
                    Assert(report,"Lost field allocation recovers without a second provider",helper.Active && helper.DistanceTexture.IsCreated() && RenderResourceLease.Count==4);
                    bool nested=false,childWasClear=false,parentRestored=false;
                    var childObject=Own(owned,new GameObject("Feather child without helper"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(childObject,scene);
                    var child=childObject.AddComponent<Camera>();child.CopyFrom(camera);child.enabled=false;child.scene=scene;child.cullingMask=0;
                    var sentinel=new Vector4(1,100,100,32);
                    using(UnityCameraHub.Register(new object(),"Feather nesting fixture",500,null,context=>{
                        if(UnityCameraHub.Camera(context)==child){childWasClear=Shader.GetGlobalVector("_TomHairFeatherParams")==Vector4.zero;return;}
                        if(UnityCameraHub.Camera(context)!=camera || nested)return;nested=true;
                        Shader.SetGlobalVector("_TomHairFeatherParams",sentinel);child.Render();parentRestored=Shader.GetGlobalVector("_TomHairFeatherParams")==sentinel;
                    },null))camera.Render();
                    Assert(report,"Helper-free child cannot inherit a parent field",nested && childWasClear && parentRestored);
                    UnityCameraHub.Coordinator.Prepare(camera,camera.GetInstanceID(),Time.frameCount,target.GetInstanceID(),512,384,CameraRole.View);
                    UnityCameraHub.Coordinator.AbortAll();
                    Assert(report,"Aborted pre-Begin feather removes GPU commands and textures",!helper.Active && RenderResourceLease.Count==0 && camera.GetCommandBuffers(CameraEvent.BeforeForwardOpaque).Length==0);
                    camera.Render();Assert(report,"Feather recovers after scope abort",helper.Active);
                }
                hairMaterial.SetFloat("_HairFrontMode",0);camera.Render();Assert(report,"Zero demand releases field",!helper.Active);
                hairMaterial.SetFloat("_HairFrontMode",2);camera.Render();
                helper.enabled=false;Assert(report,"Disable removes command buffers",camera.GetCommandBuffers(CameraEvent.BeforeForwardOpaque).Length==0 && camera.GetCommandBuffers(CameraEvent.AfterEverything).Length==0);
                Object.DestroyImmediate(helper);helper=null;
                Assert(report,"Final helper removes callback ownership",!UnityCameraHub.IsAttached && (Camera.onPreCull==null?0:Camera.onPreCull.GetInvocationList().Length)==callbacks);
                if(!baseline)SharedOwnership(report,owned,scene,camera);
                report.passed=true;
            }
            catch(Exception error){report.error=error.ToString();throw;}
            finally
            {
                if(helper)Object.DestroyImmediate(helper);UnityCameraHub.Coordinator.AbortAll();RenderTexture.active=previous;
                for(int i=owned.Count-1;i>=0;i--)if(owned[i]){var rt=owned[i]as RenderTexture;if(rt)rt.Release();Object.DestroyImmediate(owned[i]);}EditorSceneManager.ClosePreviewScene(scene);
                using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(Report)).WriteObject(stream,report);File.WriteAllText(Path.Combine(output,"report.json"),Encoding.UTF8.GetString(stream.ToArray()));}
            }
            return Path.Combine(output,"report.json");
        }

        static void SharedOwnership(Report report,List<Object> owned,UnityEngine.SceneManagement.Scene scene,Camera camera)
        {
            var material=Own(owned,new Material(Shader.Find("Unlit/Color")));
            var go=Quad(owned,scene,"Bounds/MPB owner",Vector3.zero,Vector3.one,material);var renderer=go.GetComponent<Renderer>();var filter=go.GetComponent<MeshFilter>();var original=filter.sharedMesh;var before=original.bounds;
            RendererBoundsBroker.Lease first=null,second=null;RendererPropertyBroker.Lease outer=null,inner=null;IDisposable registration=null;RenderResourceLease color=null,depth=null;
            try
            {
                first=RendererBoundsBroker.Acquire(renderer);second=RendererBoundsBroker.Acquire(renderer);first.Set(.02f);second.Set(.05f);
                var clone=filter.sharedMesh;Assert(report,"Two families share one mesh clone",clone!=original && RendererBoundsBroker.MeshCount==1);
                Assert(report,"Bounds union is max, not accumulated padding",Mathf.Abs(clone.bounds.size.x-before.size.x-.1f)<1e-6f);
                for(int i=0;i<8;i++){first.Set(.02f);second.Set(.05f);}
                Assert(report,"Repeated updates cannot inflate bounds",Mathf.Abs(clone.bounds.size.x-before.size.x-.1f)<1e-6f);
                second.Dispose();second=null;Assert(report,"Releasing one contributor preserves remaining bounds",filter.sharedMesh==clone && Mathf.Abs(clone.bounds.size.x-before.size.x-.04f)<1e-6f);
                first.Dispose();first=null;Assert(report,"Final contributor restores original mesh",filter.sharedMesh==original && RendererBoundsBroker.RendererCount==0);
                first=RendererBoundsBroker.Acquire(renderer);first.Set(.02f);var replacedClone=filter.sharedMesh;
                var externalMesh=Own(owned,Object.Instantiate(original));externalMesh.bounds=new Bounds(Vector3.one,new Vector3(2,3,1));filter.sharedMesh=externalMesh;
                first.Set(.04f);Assert(report,"External mesh replacement rebases and releases old clone",!replacedClone && filter.sharedMesh!=externalMesh && Mathf.Abs(filter.sharedMesh.bounds.size.x-2.08f)<1e-5f);
                first.Dispose();first=null;Assert(report,"External mesh survives final restore",filter.sharedMesh==externalMesh);
                filter.sharedMesh=original;
                var xMaterial=Own(owned,new Material(Shader.Find("tom/MainOpaqueXTess")));xMaterial.SetFloat("_TessellationSmooth",.02f);xMaterial.SetFloat("_DisplacementStrength",.03f);xMaterial.SetFloat("_DisplacementMidlevel",.5f);xMaterial.SetFloat("_TessellationBoundsPadding",.01f);
                var bsdfMaterial=Own(owned,new Material(Shader.Find("tom/BSDFTess")));bsdfMaterial.SetFloat("_HeightScale",.08f);renderer.sharedMaterials=new[]{xMaterial,bsdfMaterial};
                var xBounds=go.AddComponent<TomTessellationBounds>();var xClone=filter.sharedMesh;
                try
                {
                    using(var bsdfBounds=new TomBSDFTessellationBounds())
                    {
                        // Unity's scene-wide FindObjectsOfType omits preview scenes.
                        // Exercise the exact per-renderer calculator, not user objects.
                        typeof(TomBSDFTessellationBounds).GetMethod("PrepareRenderer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(bsdfBounds,new object[]{renderer});
                        xBounds.ApplyBounds();
                        Assert(report,"Actual TomX and BSDF bounds share one original mesh",filter.sharedMesh==xClone && RendererBoundsBroker.MeshCount==1 && Mathf.Abs(xClone.bounds.size.x-before.size.x-.16f)<1e-5f);
                        bsdfMaterial.SetFloat("_HeightScale",0);bsdfBounds.Dispose();
                        using(var zeroBounds=new TomBSDFTessellationBounds())
                        {
                            typeof(TomBSDFTessellationBounds).GetMethod("PrepareRenderer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(zeroBounds,new object[]{renderer});
                            Assert(report,"BSDF zero-height release preserves TomX smoothing",zeroBounds.RendererCount==0 && filter.sharedMesh==xClone && xClone.bounds.size.x>before.size.x);
                        }
                    }
                }
                finally{Object.DestroyImmediate(xBounds);}
                Assert(report,"Both facade teardown restores original geometry",filter.sharedMesh==original && RendererBoundsBroker.RendererCount==0);
                var skinObject=Own(owned,new GameObject("Bounds skin"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(skinObject,scene);
                var skin=skinObject.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=original;skin.localBounds=new Bounds(Vector3.zero,Vector3.one);skin.updateWhenOffscreen=true;
                first=RendererBoundsBroker.Acquire(skin);first.Set(.02f,0,true,true);
                Assert(report,"Skinned camera scope suspends automatic AABB updates",!skin.updateWhenOffscreen && RendererBoundsBroker.MeshCount==0);
                first.ResumeSkin();Assert(report,"Skinned camera completion restores offscreen updates",skin.updateWhenOffscreen);
                first.Dispose();first=null;Assert(report,"Skin lease teardown restores ownership",RendererBoundsBroker.RendererCount==0);
                renderer.sharedMaterial=material;
                int flag=Shader.PropertyToID("_TomBSDFReceiverOptOut"),external=Shader.PropertyToID("_TomValidationExternal");
                var block=new MaterialPropertyBlock();block.SetFloat(external,3);renderer.SetPropertyBlock(block);
                outer=RendererPropertyBroker.Float(renderer,flag,1);inner=RendererPropertyBroker.Float(renderer,flag,2);
                renderer.GetPropertyBlock(block);block.SetFloat(external,7);renderer.SetPropertyBlock(block);inner.Dispose();inner=null;renderer.GetPropertyBlock(block);
                Assert(report,"Nested MPB release preserves current unrelated updates",block.GetFloat(flag)==1 && block.GetFloat(external)==7);
                renderer.GetPropertyBlock(block);block.SetFloat(flag,9);renderer.SetPropertyBlock(block);outer.Dispose();outer=null;renderer.GetPropertyBlock(block);
                Assert(report,"External writer of owned key survives teardown",block.GetFloat(flag)==9 && block.GetFloat(external)==7 && RendererPropertyBroker.PropertyCount==0);
                var skinMaterial=Own(owned,new Material(Shader.Find("tom/SkinX")));skinMaterial.SetFloat("_UseFaceSDF",1);skinMaterial.SetFloat("_FaceSDFDirectionSource",1);renderer.sharedMaterial=skinMaterial;
                renderer.GetPropertyBlock(block,0);block.SetFloat(external,11);renderer.SetPropertyBlock(block,0);
                var sdf=go.AddComponent<TomSkinSDFHead>();sdf.head=go.transform;sdf.ApplyFrame();renderer.GetPropertyBlock(block,0);
                Assert(report,"SDF facade publishes a valid per-slot frame",block.GetFloat("_TomFaceSDFHeadValid")==1 && block.GetFloat(external)==11);
                block.SetFloat(external,13);renderer.SetPropertyBlock(block,0);Object.DestroyImmediate(sdf);renderer.GetPropertyBlock(block,0);
                Assert(report,"SDF teardown preserves external per-slot edits",block.GetFloat("_TomFaceSDFHeadValid")==0 && block.GetFloat(external)==13 && RendererPropertyBroker.PropertyCount==0);
                renderer.sharedMaterial=material;
                registration=UnityCameraHub.Register(new object(),"Shared ownership discovery fixture",0,null,null,null);
                var context=UnityCameraHub.Coordinator.Prepare(camera,camera.GetInstanceID(),Time.frameCount,0,512,384,CameraRole.View);
                long scans=SceneDiscovery.RendererScans;var a=SceneDiscovery.Renderers();var b=SceneDiscovery.Renderers();
                Assert(report,"Candidate discovery runs once per invocation",ReferenceEquals(a,b) && SceneDiscovery.RendererScans==scans+1);
                var replacement=Own(owned,new Material(material));renderer.sharedMaterial=replacement;
                var currentMaterials=new List<Material>();renderer.GetSharedMaterials(currentMaterials);Assert(report,"ME clones are read live, not cached",currentMaterials[0]==replacement);
                var descriptor=new RenderResourceDescriptor{width=32,height=16,format=RenderTextureFormat.ARGBHalf,filter=FilterMode.Bilinear,linear=true,purpose="Validation color",stage="BeforeForwardAlpha"};
                var owner=new object();color=RenderResourceLease.Create(owner,descriptor,8192);descriptor.purpose="Validation depth";descriptor.format=RenderTextureFormat.RFloat;depth=RenderResourceLease.Create(owner,descriptor,8192);
                Assert(report,"Equal-size different-purpose resources cannot alias",color.Texture!=depth.Texture && color.Invocation==context.Id && color.Generation==context.CameraGeneration);
                bool refused=false;try{RenderResourceLease.Create(owner,descriptor,7000).Dispose();}catch(InvalidOperationException){refused=true;}
                Assert(report,"Per-owner allocation budget is enforced",refused);
                UnityCameraHub.Coordinator.AbortAll();registration.Dispose();registration=null;
                Assert(report,"Aborted invocation clears discovery snapshot",SceneDiscovery.ScopeCount==0);
                color.Dispose();color=null;depth.Dispose();depth=null;Assert(report,"Exclusive resource leases release completely",RenderResourceLease.Count==0);
                registration=UnityCameraHub.Register(new object(),"Destroyed camera fixture",0,null,null,null);
                var doomedObject=Own(owned,new GameObject("Resource camera"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(doomedObject,scene);var doomed=doomedObject.AddComponent<Camera>();doomed.enabled=false;
                UnityCameraHub.Coordinator.Prepare(doomed,doomed.GetInstanceID(),Time.frameCount,0,32,16,CameraRole.View);
                color=RenderResourceLease.Create(owner,descriptor);UnityCameraHub.Coordinator.End(doomed);Object.DestroyImmediate(doomedObject);
                typeof(UnityCameraHub).GetMethod("Tick",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
                Assert(report,"Destroyed cameras release owned GPU allocation without another render",!color.IsActive && RenderResourceLease.Count==0);
                color.Dispose();color=null;registration.Dispose();registration=null;
            }
            finally{if(first!=null)first.Dispose();if(second!=null)second.Dispose();if(outer!=null)outer.Dispose();if(inner!=null)inner.Dispose();if(color!=null)color.Dispose();if(depth!=null)depth.Dispose();UnityCameraHub.Coordinator.AbortAll();if(registration!=null)registration.Dispose();}
        }
    }
}

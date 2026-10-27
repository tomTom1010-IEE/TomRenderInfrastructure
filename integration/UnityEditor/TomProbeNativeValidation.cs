using System;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using TomProbeRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Bounded asynchronous native RenderProbe fixture. An explicit request owns a
// short Play-mode session; no scene save, shader edit or Bundle build.
[InitializeOnLoad]
public static class TomProbeNativeValidation
{
    const string Pending="TomRender.NativeProbe.Pending";
    [Serializable] sealed class Report
    {
        public bool complete,passed;public string error,sky,runtimeSha256;public int ticks;
        public Color[] centers;public double elapsedSeconds;
    }
    static Report report;
    static string output;
    static double started;
    static GameObject root,pumpObject;
    static Camera pump;
    static RenderTexture target;
    static TomReflectionProbeController controller;
    static UnityEngine.SceneManagement.Scene scene;
    static bool previousRealtime;
    static TomProbeSettings settings;
    public static string Inventory()
    {
        return string.Join("\n",Resources.FindObjectsOfTypeAll<ReflectionProbe>().Select(p=>p.name+" | "+p.transform.position+" | "+p.gameObject.hideFlags+" | "+p.enabled+" | "+p.mode+" | "+p.resolution+" | mask="+p.cullingMask+" | persistent="+EditorUtility.IsPersistent(p)).ToArray());
    }
    static TomProbeNativeValidation()
    {
        EditorApplication.playModeStateChanged+=state=>{
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                string path=SessionState.GetString(Pending,"");if(string.IsNullOrEmpty(path))return;
                SessionState.EraseString(Pending);
                try{Run(path);}catch(Exception error){Debug.LogException(error);}
            }
            else if(state==PlayModeStateChange.ExitingPlayMode && report!=null)Finish(new OperationCanceledException("Explicit native fixture interrupted"));
        };
    }
    public static string Begin(string directory)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || !string.IsNullOrEmpty(SessionState.GetString(Pending,"")))throw new InvalidOperationException("No takeover of another Play-mode session");
        if(Directory.Exists(directory))throw new InvalidOperationException("Evidence directory already exists");
        // Only the exact diagnostic-created orphan is eligible. Never sweep
        // native/player/user probes by position, camera type or a loose prefix.
        foreach(var probe in Resources.FindObjectsOfTypeAll<ReflectionProbe>())
            if(probe.name=="Native probe integration fixture [Tom reflection capture]" && probe.gameObject.hideFlags==HideFlags.HideAndDontSave
                && probe.resolution==16 && probe.cullingMask==0 && (probe.transform.position-new Vector3(1031,611,937)).sqrMagnitude<1e-8f && !EditorUtility.IsPersistent(probe))
                Object.DestroyImmediate(probe.gameObject);
        SessionState.SetString(Pending,directory);
        EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;
        return "Explicit native-probe Play-mode fixture requested. Completion evidence: "+Path.Combine(directory,"report.json");
    }
    static void Run(string directory)
    {
        if(report!=null)throw new InvalidOperationException("Native probe fixture already active");
        if(Directory.Exists(directory))throw new InvalidOperationException("Evidence directory already exists");
        if(Resources.FindObjectsOfTypeAll<TomProbeCaptureScheduler>().Length!=0)throw new InvalidOperationException("Another probe scheduler exists; no takeover");
        Directory.CreateDirectory(directory);output=directory;report=new Report();started=EditorApplication.timeSinceStartup;
        previousRealtime=QualitySettings.realtimeReflectionProbes;
        try
        {
            using(var input=File.OpenRead(typeof(TomProbeControl).Assembly.Location))using(var sha=SHA256.Create())report.runtimeSha256=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
            QualitySettings.realtimeReflectionProbes=true;scene=EditorSceneManager.NewPreviewScene();
            root=new GameObject("Native probe integration fixture");root.SetActive(false);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            var probe=root.AddComponent<ReflectionProbe>();probe.enabled=false;probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.ViaScripting;
            controller=root.AddComponent<TomReflectionProbeController>();controller.probe=probe;root.SetActive(true);
            pumpObject=new GameObject("Native probe integration pump");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pumpObject,scene);
            pump=pumpObject.AddComponent<Camera>();pump.enabled=false;pump.scene=scene;pump.cullingMask=0;pump.clearFlags=CameraClearFlags.SolidColor;
            target=new RenderTexture(32,32,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);target.Create();pump.targetTexture=target;
            settings=new TomProbeSettings{origin=new Vector3(1031,611,937),resolution=16,mask=0,skybox=false,background=new Color(.12f,.3f,.75f,1),timeSlicing=ReflectionProbeTimeSlicingMode.IndividualFaces};
            controller.BeginCapture(settings);EditorApplication.update+=Tick;Write();
        }
        catch(Exception error){Finish(error);throw;}
    }
    static void Tick()
    {
        try
        {
            ++report.ticks;
            if(EditorApplication.timeSinceStartup-started>25)throw new TimeoutException("Native RenderProbe did not complete within bounded Editor fixture");
            pump.Render();EditorApplication.QueuePlayerLoopUpdate();
            if(!controller.StepCapture())return;
            report.sky=controller.LastSkyCapture;
            var cube=controller.RuntimeProbe.texture as RenderTexture;
            if(!cube)throw new InvalidOperationException("Native probe did not publish a realtime cubemap");
            report.centers=new Color[6];var previous=RenderTexture.active;var pixels=new Texture2D(cube.width,cube.height,TextureFormat.RGBAFloat,false,true);
            try
            {
                for(int face=0;face<6;face++)
                {
                    Graphics.SetRenderTarget(cube,0,(CubemapFace)face);pixels.ReadPixels(new Rect(0,0,cube.width,cube.height),0,0);pixels.Apply(false,false);
                    var c=pixels.GetPixel(cube.width/2,cube.height/2);report.centers[face]=c;
                    if(Mathf.Abs(c.r-settings.background.r)>.004f || Mathf.Abs(c.g-settings.background.g)>.004f || Mathf.Abs(c.b-settings.background.b)>.004f)throw new InvalidOperationException("Native probe sky color mismatch on face "+face+": "+c);
                    File.WriteAllBytes(Path.Combine(output,"face-"+face+".png"),pixels.EncodeToPNG());
                }
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(pixels);}
            Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;
        try
        {
            var published=controller?controller.RuntimeProbe:null;
            var field=typeof(TomReflectionProbeController).GetField("_captureProbe",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var staging=controller?field.GetValue(controller) as ReflectionProbe:null;
            if(controller)controller.CancelCapture();if(root)Object.DestroyImmediate(root);if(pumpObject)Object.DestroyImmediate(pumpObject);
            // Production Destroy is deferred. Retire the owned diagnostic probes
            // immediately before leaving Play mode, rather than leaking an orphan.
            if(published)Object.DestroyImmediate(published.gameObject);if(staging)Object.DestroyImmediate(staging.gameObject);
            if(target){target.Release();Object.DestroyImmediate(target);}if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
        }
        finally
        {
            QualitySettings.realtimeReflectionProbes=previousRealtime;
            report.complete=true;report.passed=error==null;report.error=error==null?null:error.ToString();report.elapsedSeconds=EditorApplication.timeSinceStartup-started;
            Write();report=null;root=pumpObject=null;controller=null;pump=null;target=null;
            EditorApplication.delayCall+=()=>EditorApplication.isPlaying=false;
        }
    }
    static void Write(){File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));}
}

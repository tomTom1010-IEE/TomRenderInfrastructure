using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using TomRenderInfrastructure;

static class Program
{
    static readonly List<object> checks = new List<object>();
    static void Check(string name, bool pass)
    {
        checks.Add(new { name, passed=pass });
        if (!pass) throw new Exception(name);
    }
    static RenderContext Prepare(RenderCoordinator c, object camera, int id=1, int target=1)
    { return c.Prepare(camera,id,11,target,640,480,CameraRole.View); }
    static void Main(string[] args)
    {
        var output = args.Length == 0 ? "artifacts/core-model-tests/report.json" : args[0];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        object performance = null;
        try
        {
            var c = new RenderCoordinator(12) { TraceEnabled=true };
            var calls = new List<string>(); var camera=new object(); var owner=new object();
            var a=c.Register(owner,"A",20,_=>calls.Add("prepare A"),_=>calls.Add("begin A"),(_,abort)=>calls.Add("end A "+abort));
            var b=c.Register(new object(),"B",10,_=>calls.Add("prepare B"),_=>calls.Add("begin B"),(_,abort)=>calls.Add("end B "+abort));
            Prepare(c,camera); c.Begin(camera); c.End(camera);
            Check("Deterministic prepare/begin order and reverse cleanup",string.Join(",",calls)=="prepare B,prepare A,begin B,begin A,end A False,end B False");
            Check("No active scope after completed render",c.ActiveDepth==0 && c.Statistics().completed==1);
            bool duplicate=false; try { c.Register(owner,"duplicate",0,null,null,null); } catch(InvalidOperationException){duplicate=true;}
            Check("Duplicate owner rejected without second execution",duplicate && c.RegistrationCount==2);
            a.Dispose(); a.Dispose(); b.Dispose();
            Check("Disposal is idempotent",c.RegistrationCount==0);

            var parent=Prepare(c,camera); c.Begin(camera);
            var childCamera=new object(); var child=Prepare(c,childCamera,2); c.Begin(childCamera);
            Check("Nested camera has unique invocation and parent",child.Id!=parent.Id && child.ParentId==parent.Id && child.Depth==2);
            c.End(childCamera); Check("Child end restores current parent",c.Current==parent); c.End(camera);
            var repeated=Prepare(c,camera,target:9); c.Begin(camera); c.End(camera);
            Check("Same-frame repeated camera is not deduplicated",repeated.Frame==parent.Frame && repeated.Id!=parent.Id && repeated.TargetId==9);
            Check("Same live camera retains generation",repeated.CameraGeneration==parent.CameraGeneration);
            var replacement=Prepare(c,new object()); c.End(replacement.CameraObject);
            Check("Reused native ID with new reference receives new generation",replacement.CameraGeneration!=parent.CameraGeneration);
            Check("Trace ring remains bounded",c.ReadTrace().Length==12);

            int ended=0, aborted=0; var d=new RenderCoordinator();
            d.Register(new object(),"faulty",0,_=>throw new Exception("test"),null,(_,isAbort)=>{ended++;if(isAbort)aborted++;});
            int healthy=0; d.Register(new object(),"healthy",1,_=>healthy++,_=>healthy++,(_,abort)=>healthy++);
            Prepare(d,camera); d.Begin(camera); d.End(camera);
            Check("Prepare fault gets one abort cleanup",ended==1 && aborted==1 && d.Statistics().callbackFaults==1);
            Check("Fault does not cancel another participant",healthy==3);

            int removals=0; var e=new RenderCoordinator(); IDisposable lease=null;
            lease=e.Register(new object(),"self-remove",0,_=>lease.Dispose(),_=>throw new Exception("must not begin"),(_,abort)=>{if(abort)removals++;});
            Prepare(e,camera); e.Begin(camera); e.End(camera);
            Check("Self-removal unwinds after callback once",removals==1 && e.Statistics().callbackFaults==0 && e.RegistrationCount==0);

            var f=new RenderCoordinator(); int aborts=0;
            f.Register(new object(),"abort",0,null,null,(_,abort)=>{if(abort)aborts++;});
            Prepare(f,camera); Prepare(f,childCamera,2); f.End(camera);
            Check("Out-of-order parent end aborts only unfinished children",aborts==1 && f.Statistics().aborted==1 && f.Statistics().completed==1 && f.ActiveDepth==0);
            Prepare(f,camera); f.Begin(camera); f.AbortAll(); f.AbortAll();
            Check("Explicit abort is idempotent",aborts==2 && f.ActiveDepth==0);
            Check("Unknown end cannot discard another camera",!f.End(new object()) && f.Statistics().mismatchedEnds==1);

            var g=new RenderCoordinator();
            g.Register(new object(),"abort-in-callback",0,_=>g.AbortAll(),null,null);
            Prepare(g,camera);
            Check("Abort during callback is deferred and balanced",g.ActiveDepth==0 && g.Statistics().aborted==1);
            var h=new RenderCoordinator();
            h.Register(new object(),"recursive-end",0,null,null,(_,abort)=>h.End(camera));
            Prepare(h,camera); h.Begin(camera); h.End(camera);
            Check("Recursive end cannot pop the enclosing scope twice",h.ActiveDepth==0 && h.Statistics().completed==1);
            var recursive=new RenderCoordinator();
            recursive.Register(new object(),"child-end",0,null,null,(context,abort)=>{if(context.CameraId==2)recursive.End(camera);});
            Prepare(recursive,camera);Prepare(recursive,childCamera,2);recursive.End(childCamera);
            Check("Parent end requested inside child cleanup cannot pop either scope twice",recursive.ActiveDepth==1 && recursive.Current.CameraId==1 && recursive.Statistics().completed==1);
            recursive.End(camera);Check("Parent remains normally completable after rejected recursive end",recursive.ActiveDepth==0 && recursive.Statistics().completed==2);

            var j=new RenderCoordinator();
            j.Register(new object(),"late-register",0,_=>j.Register(new object(),"late",1,_=>calls.Add("late"),null,null),null,null);
            calls.Clear(); Prepare(j,camera); j.Begin(camera); j.End(camera);
            Check("Registration during dispatch applies to next invocation",calls.Count==0 && j.RegistrationCount==2);
            Prepare(j,camera); j.End(camera); Check("New registration runs on following invocation",calls.Count==1);

            var k=new RenderCoordinator(); int depth=0;
            for(int n=0;n<64;n++){Prepare(k,new object(),100+n);depth++;}
            bool limit=false;try{Prepare(k,new object(),999);}catch(InvalidOperationException){limit=true;}
            Check("Recursion limit preserves existing scopes",limit && k.ActiveDepth==depth);
            k.AbortAll(); Check("Deep abort fully unwinds",k.ActiveDepth==0 && k.Statistics().aborted==64);
            k.CollectCameras(_=>false); Check("Destroyed camera records can be removed",k.Statistics().cameraRecords==0);

            var random=new Random(71027); var fuzz=new RenderCoordinator(); var objects=new List<object>(); int generated=0;
            for(int n=0;n<3000;n++)
            {
                if(objects.Count==0 || objects.Count<12 && random.Next(2)==0)
                {var o=new object();objects.Add(o);Prepare(fuzz,o,++generated);fuzz.Begin(o);}
                else {var o=objects[objects.Count-1];objects.RemoveAt(objects.Count-1);fuzz.End(o);}
            }
            while(objects.Count>0){fuzz.End(objects[objects.Count-1]);objects.RemoveAt(objects.Count-1);}
            Check("3000 randomized nested operations remain balanced",fuzz.ActiveDepth==0 && fuzz.Statistics().prepared==fuzz.Statistics().completed && fuzz.Statistics().aborted==0);

            var measured=new RenderCoordinator(0); measured.Register(new object(),"noop",0,_=>{},_=>{},(_,abort)=>{});
            for(int n=0;n<2000;n++){Prepare(measured,camera);measured.Begin(camera);measured.End(camera);}
            long before=GC.GetAllocatedBytesForCurrentThread();var sw=Stopwatch.StartNew();const int count=50000;
            for(int n=0;n<count;n++){Prepare(measured,camera);measured.Begin(camera);measured.End(camera);}
            sw.Stop();long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            performance=new{invocations=count,microsecondsPerInvocation=sw.Elapsed.TotalMilliseconds*1000/count,bytesPerInvocation=(double)allocated/count,
                scope="Pure managed production coordinator source on .NET 9. Not Unity/Mono frame time or GPU timing; no performance gate inferred from this alone."};
            Check("No trace storage when tracing disabled",measured.ReadTrace().Length==0);
            var routing=new RenderCoordinator();using(var router=new SceneColorRouter(routing))
            {
                var scope=Prepare(routing,camera);var receiver=new object();int updates=0;
                var route=router.Subscribe(scope,receiver,_=>updates++);
                Check("Unpublished scene color uses native camera target",route.IsActive && route.Route.source==SceneColorSource.NativeCameraTarget);
                Check("Scheduled opaque work is not a completed route",!router.PublishOpaqueResult(scope,owner,"DOF") && updates==0);
                routing.Begin(camera);
                Check("Successful opaque completion publishes once",router.PublishOpaqueResult(scope,owner,"DOF") && updates==1 && route.Route.source==SceneColorSource.OpaqueEffectsCurrentActive);
                Check("Duplicate completion cannot rerun consumers",router.PublishOpaqueResult(scope,owner,"DOF") && updates==1 && router.Publications==1);
                Check("Another provider cannot take over a completed route",!router.PublishOpaqueResult(scope,new object(),"other"));
                var childScope=Prepare(routing,childCamera,2);routing.Begin(childCamera);
                Check("A child render cannot publish its active target as the parent result",!router.PublishOpaqueResult(scope,owner,"wrong active target"));
                var childRoute=router.Subscribe(childScope,new object(),null);
                Check("Nested child starts native without inheriting parent result",childRoute.Route.source==SceneColorSource.NativeCameraTarget && childRoute.Route.invocation!=route.Route.invocation);
                routing.End(childCamera);
                Check("Child completion preserves parent route",!childRoute.IsActive && route.IsActive && router.EntryCount==1);
                routing.End(camera);
                Check("Scope completion removes all route leases and entries",!route.IsActive && router.EntryCount==0 && router.LeaseCount==0);
                Check("Completed invocation cannot publish stale color",!router.PublishOpaqueResult(scope,owner,"stale"));
                var next=Prepare(routing,camera);routing.Begin(camera);
                var nextRoute=router.Subscribe(next,new object(),null);
                Check("Same-frame next render starts with a fresh native route",nextRoute.Route.source==SceneColorSource.NativeCameraTarget && nextRoute.Route.invocation!=scope.Id);
                routing.AbortAll();Check("Abort invalidates routes",!nextRoute.IsActive && router.EntryCount==0);
                var late=Prepare(routing,camera);routing.Begin(camera);router.PublishOpaqueResult(late,owner,"early");
                var lateRoute=router.Subscribe(late,new object(),null);
                Check("Late observer sees already completed opaque result",lateRoute.Route.source==SceneColorSource.OpaqueEffectsCurrentActive);
                lateRoute.Dispose();Check("Disposed route consumer is removed",router.LeaseCount==0);
                routing.End(camera);
                var failing=Prepare(routing,camera);routing.Begin(camera);
                router.Subscribe(failing,new object(),_=>throw new Exception("consumer failure"));int others=0;
                router.Subscribe(failing,new object(),_=>others++);
                router.PublishOpaqueResult(failing,owner,"DOF");
                Check("Route consumer failure does not affect other consumers",router.ConsumerFaults==1 && others==1 && router.LeaseCount==1);
                routing.End(camera);Check("Faulted consumers leave no route state",router.EntryCount==0);
            }
            using(var job=CaptureJobs.Begin("Probe test",12.5,3))
            {
                Check("Capture job lifetime starts outside camera invocation",job.IsActive && CaptureJobs.Count==1 && job.StartedAt==12.5 && job.SampleBudget==3);
                bool busy=false;try{CaptureJobs.Begin("Probe test",13,1);}catch(InvalidOperationException){busy=true;}
                Check("A capture lane has one active owner",busy && CaptureJobs.Count==1);
                job.Step();job.Step();Check("Capture progress does not change its original budget or timeout origin",job.Steps==2 && job.StartedAt==12.5 && job.SampleBudget==3);
                job.Dispose();job.Dispose();Check("Capture completion is idempotent",!job.IsActive && CaptureJobs.Count==0);
                using(var next=CaptureJobs.Begin("Probe test",14,1))Check("Reused capture lane gets a fresh job identity",next.Id!=job.Id);
                Check("Closing all captures leaves no lane records",CaptureJobs.Count==0);
            }
            File.WriteAllText(output,JsonSerializer.Serialize(new{passed=true,checks,performance},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("PASS: "+checks.Count+" coordinator checks. "+output);
        }
        catch(Exception error)
        {
            File.WriteAllText(output,JsonSerializer.Serialize(new{passed=false,checks,error=error.ToString(),performance},new JsonSerializerOptions{WriteIndented=true}));
            throw;
        }
    }
}

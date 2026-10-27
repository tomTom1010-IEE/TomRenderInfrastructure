using System;
using System.Collections.Generic;
using UnityEngine;

namespace TomRenderInfrastructure
{
    // A job can own an explicit SH camera, or resolve Unity's pooled native
    // reflection camera after its original guarded ownership checks succeed.
    public static class CameraRoles
    {
        sealed class Lease : IDisposable
        {
            internal Camera camera;internal Func<Camera,bool> resolver;internal CameraRole role;
            public void Dispose() { leases.Remove(this);camera=null;resolver=null; }
        }
        static readonly List<Lease> leases=new List<Lease>();
        public static int Count { get { return leases.Count; } }
        public static IDisposable Claim(Camera camera,CameraRole role)
        {
            if(!camera)throw new ArgumentNullException("camera");
            var value=new Lease{camera=camera,role=role};leases.Add(value);return value;
        }
        public static IDisposable ResolveWith(Func<Camera,bool> resolver,CameraRole role)
        {
            if(resolver==null)throw new ArgumentNullException("resolver");
            var value=new Lease{resolver=resolver,role=role};leases.Add(value);return value;
        }
        public static CameraRole Resolve(Camera camera,CameraRole nativeRole)
        {
            foreach(var lease in leases)
                if(lease.camera ? lease.camera==camera : lease.resolver!=null && lease.resolver(camera))nativeRole|=lease.role;
            return nativeRole;
        }
        public static bool IsAuxiliaryCapture(Camera camera)
        {
            var context=UnityCameraHub.Coordinator.Find(camera);
            return context!=null && (context.Role&CameraRole.Probe)!=0;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TomRenderInfrastructure
{
    public struct RenderResourceDescriptor
    {
        public int width,height,depth;
        public RenderTextureFormat format;
        public TextureDimension dimension;
        public FilterMode filter;
        public string purpose,stage;
        public bool linear;
        public long EstimatedBytes
        {
            get
            {
                int bits=format==RenderTextureFormat.ARGBFloat?128:format==RenderTextureFormat.ARGBHalf || format==RenderTextureFormat.RGFloat?64
                    :format==RenderTextureFormat.RHalf?16:32;
                return checked((long)width*height*((bits+(depth>0?32:0)+7)/8)*(dimension==TextureDimension.Cube?6:1));
            }
        }
    }
    // Exclusive allocation leases, not a cross-purpose texture pool. A color
    // copy, depth copy and feather field never alias merely due to equal sizes.
    public sealed class RenderResourceLease : IDisposable
    {
        static readonly HashSet<RenderResourceLease> live=new HashSet<RenderResourceLease>();
        readonly object owner;
        Camera camera;
        bool cameraBound;
        public RenderTexture Texture { get; private set; }
        public RenderResourceDescriptor Descriptor { get; private set; }
        public long Invocation { get; private set; }
        public int CameraId { get; private set; }
        public int Generation { get; private set; }
        public bool IsActive { get { return Texture && Texture.IsCreated(); } }
        public static int Count { get { return live.Count; } }
        public static long EstimatedBytes { get { long n=0;foreach(var lease in live)n+=lease.Descriptor.EstimatedBytes;return n; } }
        RenderResourceLease(object owner,RenderResourceDescriptor descriptor)
        {this.owner=owner;Descriptor=descriptor;}
        public static RenderResourceLease Create(object owner,RenderResourceDescriptor descriptor,long budgetBytes=long.MaxValue)
        {
            if(owner==null)throw new ArgumentNullException("owner");
            if(descriptor.dimension==TextureDimension.Unknown || descriptor.dimension==TextureDimension.None)descriptor.dimension=TextureDimension.Tex2D;
            if(descriptor.dimension!=TextureDimension.Tex2D && descriptor.dimension!=TextureDimension.Cube)throw new NotSupportedException("Unsupported owned allocation dimension");
            if(descriptor.dimension==TextureDimension.Cube && descriptor.width!=descriptor.height)throw new ArgumentException("Cubemap must be square");
            if(descriptor.width<=0 || descriptor.height<=0 || descriptor.width>SystemInfo.maxTextureSize || descriptor.height>SystemInfo.maxTextureSize
                || string.IsNullOrEmpty(descriptor.purpose) || string.IsNullOrEmpty(descriptor.stage))throw new ArgumentException("Complete allocation descriptor required");
            if(!SystemInfo.SupportsRenderTextureFormat(descriptor.format))throw new NotSupportedException("Render texture format unavailable");
            long used=0;foreach(var lease in live)if(ReferenceEquals(lease.owner,owner))used+=lease.Descriptor.EstimatedBytes;
            if(descriptor.EstimatedBytes>budgetBytes-used)throw new InvalidOperationException("Owner render-resource budget exceeded");
            var result=new RenderResourceLease(owner,descriptor);
            try
            {
                result.Texture=new RenderTexture(descriptor.width,descriptor.height,descriptor.depth,descriptor.format,
                    descriptor.linear?RenderTextureReadWrite.Linear:RenderTextureReadWrite.sRGB)
                {name=descriptor.purpose,dimension=descriptor.dimension,hideFlags=HideFlags.HideAndDontSave,filterMode=descriptor.filter,wrapMode=TextureWrapMode.Clamp,antiAliasing=1,useMipMap=false,autoGenerateMips=false};
                if(!result.Texture.Create())throw new InvalidOperationException("Render-resource allocation failed: "+descriptor.purpose);
                result.Bind(UnityCameraHub.Coordinator.Current);live.Add(result);return result;
            }
            catch {result.Dispose();throw;}
        }
        public void Bind(RenderContext context)
        {
            Invocation=context==null?0:context.Id;CameraId=context==null?0:context.CameraId;Generation=context==null?0:context.CameraGeneration;
            camera=context==null?null:UnityCameraHub.Camera(context);cameraBound=context!=null;
        }
        internal static void CollectDeadCameras()
        {
            var expired=new List<RenderResourceLease>();foreach(var lease in live)if(lease.cameraBound && !lease.camera)expired.Add(lease);
            foreach(var lease in expired)lease.Dispose();
        }
        public void Dispose()
        {
            live.Remove(this);var texture=Texture;Texture=null;
            if(texture){texture.Release();if(Application.isPlaying)UnityEngine.Object.Destroy(texture);else UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace TomRenderInfrastructure
{
    // Never restore an old entire MPB. Unity 2019 cannot remove one property;
    // a previously absent owned float restores to zero, leaving other keys intact.
    public static class RendererPropertyBroker
    {
        internal struct Key : IEquatable<Key>
        {
            internal Renderer renderer; internal int slot,property,kind;
            public bool Equals(Key other) { return ReferenceEquals(renderer,other.renderer)&&slot==other.slot&&property==other.property&&kind==other.kind; }
            public override bool Equals(object other) { return other is Key && Equals((Key)other); }
            public override int GetHashCode() { return renderer.GetInstanceID() ^ slot*397 ^ property ^ kind; }
        }
        internal sealed class Entry
        {
            internal Key key;internal float before,applied;internal Matrix4x4 beforeMatrix,appliedMatrix;
            internal Vector4 beforeVector,appliedVector;
            internal readonly List<Lease> leases=new List<Lease>();
        }
        public sealed class Lease : IDisposable
        {
            Entry entry;internal float value;internal Matrix4x4 matrixValue;
            internal Vector4 vectorValue;
            internal Lease(Entry value) { entry=value; }
            public void Set(float value) { if(entry==null || entry.key.kind!=0)throw new InvalidOperationException("Not an active float lease");this.value=value;Apply(entry); }
            public void Set(Matrix4x4 value) { if(entry==null || entry.key.kind!=1)throw new InvalidOperationException("Not an active matrix lease");matrixValue=value;Apply(entry); }
            public void Set(Vector4 value) { if(entry==null || entry.key.kind!=2)throw new InvalidOperationException("Not an active vector lease");vectorValue=value;Apply(entry); }
            public void Dispose()
            {
                var previous=entry;if(previous==null)return;entry=null;
                previous.leases.Remove(this);Apply(previous);
                if(previous.leases.Count==0)entries.Remove(previous.key);
            }
        }
        static readonly Dictionary<Key,Entry> entries=new Dictionary<Key,Entry>();
        public static int PropertyCount { get { return entries.Count; } }
        static MaterialPropertyBlock Read(Key key)
        {
            var block=new MaterialPropertyBlock();
            if(key.slot<0)key.renderer.GetPropertyBlock(block);else key.renderer.GetPropertyBlock(block,key.slot);
            return block;
        }
        static Lease Acquire(Renderer renderer,int property,int slot,int kind)
        {
            if(!renderer)throw new ArgumentNullException("renderer");
            var key=new Key{renderer=renderer,property=property,slot=slot,kind=kind};Entry entry;
            if(!entries.TryGetValue(key,out entry))
            {
                var block=Read(key);entry=new Entry{key=key,before=block.GetFloat(property),applied=block.GetFloat(property),beforeMatrix=block.GetMatrix(property),appliedMatrix=block.GetMatrix(property),beforeVector=block.GetVector(property),appliedVector=block.GetVector(property)};
                entries.Add(key,entry);
            }
            var lease=new Lease(entry);entry.leases.Add(lease);return lease;
        }
        public static Lease Float(Renderer renderer,int property,float value,int slot=-1)
        {var lease=Acquire(renderer,property,slot,0);lease.Set(value);return lease;}
        public static Lease Matrix(Renderer renderer,int property,Matrix4x4 value,int slot=-1)
        {var lease=Acquire(renderer,property,slot,1);lease.Set(value);return lease;}
        public static Lease Vector(Renderer renderer,int property,Vector4 value,int slot=-1)
        {var lease=Acquire(renderer,property,slot,2);lease.Set(value);return lease;}
        static void Apply(Entry entry)
        {
            var key=entry.key;if(!key.renderer)return;
            var block=Read(key);
            // Preserve an external writer even when it changed our own key.
            bool changed=key.kind==1?block.GetMatrix(key.property)!=entry.appliedMatrix:key.kind==2?block.GetVector(key.property)!=entry.appliedVector:block.GetFloat(key.property)!=entry.applied;
            if(changed)
            {entry.before=block.GetFloat(key.property);entry.beforeMatrix=block.GetMatrix(key.property);entry.beforeVector=block.GetVector(key.property);if(entry.leases.Count==0)return;}
            if(key.kind==1)
            {entry.appliedMatrix=entry.leases.Count==0?entry.beforeMatrix:entry.leases[entry.leases.Count-1].matrixValue;block.SetMatrix(key.property,entry.appliedMatrix);}
            else if(key.kind==2)
            {entry.appliedVector=entry.leases.Count==0?entry.beforeVector:entry.leases[entry.leases.Count-1].vectorValue;block.SetVector(key.property,entry.appliedVector);}
            else
            {entry.applied=entry.leases.Count==0?entry.before:entry.leases[entry.leases.Count-1].value;block.SetFloat(key.property,entry.applied);}
            if(key.slot<0)key.renderer.SetPropertyBlock(block);else key.renderer.SetPropertyBlock(block,key.slot);
        }
    }
}

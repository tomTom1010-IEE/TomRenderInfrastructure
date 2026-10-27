using UnityEngine;

namespace TomSceneEffects
{
    [ExecuteAlways, RequireComponent(typeof(Camera)), DisallowMultipleComponent]
    public sealed class TomDofOpaqueRelay : MonoBehaviour
    {
        internal DofBinding Owner;
        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (Owner == null) Graphics.Blit(source, destination);
            else Owner.RenderEarly(source, destination);
        }
    }
}

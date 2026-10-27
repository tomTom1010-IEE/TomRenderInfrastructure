using UnityEngine;

namespace TomBSDFRuntime
{
    public sealed class TomBSDFSceneBufferResources : ScriptableObject
    {
        public const string AssetPath = "Assets/Mods/TomShadersBSDF/ResourcesData/TomBSDFSceneBuffersV1.asset";
        public int version = 1;
        public Shader copyDepth;
        public bool IsValid { get { return version == 1 && copyDepth && copyDepth.isSupported; } }

        // The companion never loads a zipmod/bundle itself or relies on an
        // unreferenced hidden shader surviving Shader.Find/variant stripping.
        public static TomBSDFSceneBufferResources FindLoaded()
        {
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
                if (bundle && bundle.Contains(AssetPath))
                {
                    var resources = bundle.LoadAsset<TomBSDFSceneBufferResources>(AssetPath);
                    if (resources && resources.IsValid) return resources;
                }
            return null;
        }
    }
}

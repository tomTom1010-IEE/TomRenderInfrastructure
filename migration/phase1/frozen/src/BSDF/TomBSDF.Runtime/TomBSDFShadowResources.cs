using UnityEngine;

namespace TomBSDFRuntime
{
    public sealed class TomBSDFShadowResources : ScriptableObject
    {
        public const string AssetPath = "Assets/Mods/TomShadersBSDF/ResourcesData/TomBSDFNativeShadowsV1.asset";
        public int version = 1;
        public Shader copyNativeShadow;
        public bool IsValid { get { return version == 1 && copyNativeShadow && copyNativeShadow.isSupported; } }
        public static TomBSDFShadowResources FindLoaded()
        {
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
                if (bundle && bundle.Contains(AssetPath))
                {
                    var value = bundle.LoadAsset<TomBSDFShadowResources>(AssetPath);
                    if (value && value.IsValid) return value;
                }
            return null;
        }
    }
}

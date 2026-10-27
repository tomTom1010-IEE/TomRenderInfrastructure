using ExtensibleSaveFormat;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using Studio;
using TomSceneEffects;

namespace TomSceneEffectsPlugin
{
    public sealed class DofSceneController : SceneCustomFunctionController
    {
        protected override void OnSceneSave()
        {
            var plugin = Plugin.Current;
            if (!plugin || plugin.ScenePolicy == DofScenePolicy.Inherit) { SetExtendedData(null); return; }
            var data = new PluginData { version = 1 };
            data.data["dofPolicy"] = (int)plugin.ScenePolicy;
            SetExtendedData(data);
        }

        protected override void OnSceneLoad(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            var plugin = Plugin.Current;
            if (!plugin || operation == SceneOperationKind.Import) return;
            plugin.SetScenePolicy(-1);
            if (operation != SceneOperationKind.Load) return;
            var data = GetExtendedData(); object raw;
            if (data != null && data.version == 1 && data.data != null && data.data.TryGetValue("dofPolicy", out raw)
                && raw is int && ((int)raw == 0 || (int)raw == 1)) plugin.SetScenePolicy((int)raw);
        }
    }
}

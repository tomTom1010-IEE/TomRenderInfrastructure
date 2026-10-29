using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using TomRenderInfrastructure;
using UnityEngine;

namespace TomBSDFPlugin
{
    // Optional public-API notifications. No dependency on ME internals or IL.
    // Unknown providers continue to work through the budgeted hierarchy fallback.
    internal sealed class SceneDiscoveryHooks : IDisposable
    {
        readonly Harmony harmony = new Harmony("com.tomtom1010.tombsdf.discovery");
        readonly ManualLogSource log;
        public bool MaterialEditorConnected { get; private set; }
        public SceneDiscoveryHooks(ManualLogSource log)
        {
            this.log = log;
            PatchSceneMethod("LoadScene"); PatchSceneMethod("ImportScene");
            PatchAdd(typeof(Studio.AddObjectItem));
            PatchAdd(typeof(Studio.AddObjectFemale));
            PatchAdd(typeof(Studio.AddObjectMale));
            PatchAdd(typeof(Studio.AddObjectLight));
            ConnectMaterialEditor();
        }
        void PatchSceneMethod(string name)
        {
            foreach (var method in typeof(Studio.Studio).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (method.Name == name) Patch(method, "SceneChanged");
        }
        void PatchAdd(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (method.Name == "Add" && typeof(Studio.ObjectCtrlInfo).IsAssignableFrom(method.ReturnType)) Patch(method, "ObjectAdded");
        }
        void Patch(MethodInfo method, string postfix)
        {
            try { harmony.Patch(method, postfix: new HarmonyMethod(typeof(SceneDiscoveryHooks), postfix)); }
            catch (Exception error) { log.LogWarning("Discovery notification unavailable for " + method.DeclaringType.FullName + "." + method.Name + ": " + error.GetBaseException().Message + "; budgeted fallback remains active."); }
        }
        public void ConnectMaterialEditor()
        {
            if (MaterialEditorConnected) return;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("MaterialEditorAPI.MaterialAPI", false);
                if (type == null) continue;
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var args = method.GetParameters();
                    if (args.Length < 2 || args[0].ParameterType != typeof(GameObject)) continue;
                    if (method.Name == "SetShader" && args.Length >= 3 && args[1].ParameterType == typeof(string)) Patch(method, "MaterialShaderChanged");
                    if ((method.Name == "GetMaterials" || method.Name == "SetMaterials") && args[1].ParameterType == typeof(Renderer)) Patch(method, "RendererMaterialsChanged");
                }
                MaterialEditorConnected = true;
                log.LogInfo("Shared discovery connected to public ME shader/material APIs; no global per-camera search.");
                return;
            }
        }
        static void SceneChanged() { SceneDiscovery.RequestRescan(); }
        static void ObjectAdded(object __result)
        {
            var item = __result as Studio.OCIItem;
            if (item != null) SceneDiscovery.NotifyHierarchy(item.objectItem);
            var character = __result as Studio.OCIChar;
            if (character != null && character.charInfo) SceneDiscovery.NotifyHierarchy(character.charInfo.gameObject);
            var lamp = __result as Studio.OCILight;
            if (lamp != null && lamp.light) SceneDiscovery.NotifyHierarchy(lamp.light.gameObject);
            // Deferred character creation is also covered by the shared pass.
            SceneDiscovery.RequestRescan();
        }
        static void MaterialShaderChanged(GameObject __0) { SceneDiscovery.NotifyHierarchy(__0); }
        static void RendererMaterialsChanged(Renderer __1) { SceneDiscovery.NotifyRenderer(__1); }
        public void Dispose() { harmony.UnpatchSelf(); }
    }
}

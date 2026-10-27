using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TomSceneEffects;
using UnityEngine;
using UnityEngine.Rendering;

namespace TomSceneEffectsPlugin
{
    // Optional SceneBuffer-v1 adapter. Keep shader/bundle/runtime binaries intact.
    // Opaque image effects can leave CameraTarget pointing at the unprocessed
    // attachment; the current active color is the post-opaque image-effect result.
    internal static class BsdfCaptureAdapter
    {
        internal static string Status = "No BSDF SceneBuffer-v1 provider loaded";
        internal static bool Failed;
        static bool installed;

        internal static void TryInstall(Harmony harmony, Action<Exception> warn)
        {
            if (installed || Failed) return;
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "TomBSDF.Runtime") { type = assembly.GetType("TomBSDFRuntime.TomBSDFSceneBuffers", false); break; }
            if (type == null) return;
            try
            {
                if (type.GetField("ProtocolVersion").GetRawConstantValue().Equals(1) == false)
                    throw new NotSupportedException("Unknown BSDF scene buffer protocol");
                var method = type.GetMethod("PrepareCamera", BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(Camera), typeof(bool) }, null);
                if (method == null) throw new MissingMethodException("SceneBuffer-v1.PrepareCamera");
                var existing = Harmony.GetPatchInfo(method);
                if (existing != null && existing.Transpilers.Count > 0) throw new NotSupportedException("Another scene-buffer transpiler is active");
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(BsdfCaptureAdapter), nameof(Transpile)));
                installed = true;
                Status = "SceneBuffer-v1: CurrentActive only for early DOF; native CameraTarget unchanged otherwise";
            }
            catch (Exception error)
            {
                Failed = true; Status = "Native fallback: incompatible BSDF capture adapter";
                warn(error);
            }
        }

        static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int match = -1, count = 0;
            for (int i = 1; i < code.Count; ++i)
            {
                var method = code[i].operand as MethodInfo;
                if (method == null || method.DeclaringType != typeof(RenderTargetIdentifier) || method.Name != "op_Implicit"
                    || method.GetParameters()[0].ParameterType != typeof(BuiltinRenderTextureType)
                    || !code[i-1].LoadsConstant((long)BuiltinRenderTextureType.CameraTarget)) continue;
                for (int j = i+1; j < Math.Min(code.Count,i+8); ++j)
                {
                    var call = code[j].operand as MethodInfo;
                    if (call == null || call.DeclaringType != typeof(CommandBuffer) || call.Name != "Blit") continue;
                    var p = call.GetParameters();
                    if (p.Length == 2 && p[0].ParameterType == typeof(RenderTargetIdentifier) && p[1].ParameterType == typeof(RenderTargetIdentifier))
                    { match = i-1; ++count; }
                    break;
                }
            }
            if (count != 1) throw new NotSupportedException("Expected exactly one SceneBuffer-v1 color source; found " + count);
            code[match].opcode = OpCodes.Ldarg_1; code[match].operand = null;
            code.Insert(match+1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BsdfCaptureAdapter), nameof(Source))));
            return code;
        }

        static BuiltinRenderTextureType Source(Camera camera)
        {
            var plugin = Plugin.Current;
            var binding = plugin ? plugin.Binding : null;
            return binding != null && binding.Camera == camera && binding.Mode == DofMode.BeforeTransparents
                && binding.Relay && binding.Relay.isActiveAndEnabled && binding.Native && binding.Native.isActiveAndEnabled
                ? BuiltinRenderTextureType.CurrentActive : BuiltinRenderTextureType.CameraTarget;
        }
    }
}

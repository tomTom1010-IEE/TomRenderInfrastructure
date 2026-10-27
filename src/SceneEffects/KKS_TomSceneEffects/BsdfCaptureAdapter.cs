using System;
using TomRenderInfrastructure;

namespace TomSceneEffectsPlugin
{
    // The former private-IL patch is deliberately gone. The DOF runtime
    // publishes successful opaque completion through the shared Core contract.
    internal static class BsdfCaptureAdapter
    {
        internal static string Status = "Core scene-color route not validated";
        internal static bool Failed;

        internal static void Validate(Action<Exception> warn)
        {
            try
            {
                if (UnityCameraHub.SceneColorProtocol != 1)
                    throw new NotSupportedException("Unknown Core scene-color protocol");
                Failed = false;
                Status = "Core SceneColor-v1: explicit successful opaque completion; no BSDF IL patch";
            }
            catch (Exception error)
            {
                Failed = true; Status = "Native fallback: incompatible Core scene-color protocol";
                warn(error);
            }
        }
    }
}

Shader "Hidden/TomRender/ScreenReflectionDepth" {
    SubShader { Cull Off ZTest Always ZWrite Off
        Pass { CGPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11
            #pragma vertex vert_img
            #pragma fragment sourceDepth
            #include "UnityCG.cginc"
            Texture2D<float> _CameraDepthTexture;
            float sourceDepth(v2f_img i) : SV_Target {
                return _CameraDepthTexture.Load(int3((int2)i.pos.xy,0));
            }
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11
            #pragma vertex vert_img
            #pragma fragment opaqueColor
            #include "UnityCG.cginc"
            Texture2D<float4> _TomSSRSceneSnapshot;
            Texture2D<float> _TomSSRSourceDepth;
            float4 opaqueColor(v2f_img i):SV_Target {
                float raw=_TomSSRSourceDepth.Load(int3((int2)i.pos.xy,0));
                #if defined(UNITY_REVERSED_Z)
                    if(raw<=0)return 0;
                #else
                    if(raw>=1)return 0;
                #endif
                // Premultiply by hit coverage before generating rough mips.
                // The transient red/green/black clear is not reflected geometry.
                return float4(_TomSSRSceneSnapshot.Load(int3((int2)i.pos.xy,0)).rgb,1);
            }
        ENDCG }
    }
    Fallback Off
}

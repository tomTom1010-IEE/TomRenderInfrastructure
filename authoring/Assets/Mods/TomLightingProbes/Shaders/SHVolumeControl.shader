Shader "tom/SHVolumeControl"
{
    Properties
    {
        _ProbeEnabled ("Probe Enabled", Range(0,1)) = 1
        _ProbeIntensity ("Intensity", Range(0,8)) = 1
        _ProbePriority ("Priority", Range(0,100)) = 0
        _ProbeGroup ("Capture Group", Range(0,999)) = 0
        _ProbeRefreshScope ("Refresh Scope (0 Self, 1 Group, 2 All)", Range(0,2)) = 0
        _ProbeRefresh ("Refresh (change 0 or 1 once)", Range(0,1)) = 0
        _ProbeUpdateMode ("Update (0 Load, 1 Changes, 2 Interval, 3 Manual)", Range(0,3)) = 1
        _ProbeCaptureOnLoad ("Capture On Load", Range(0,1)) = 1
        _ProbeInterval ("Update Interval Seconds", Range(5,3600)) = 30
        _ProbeNearClip ("Near Clip (not object exclusion)", Range(0.01,100)) = 0.1
        _ProbeFarClip ("Far Clip", Range(0.02,2000)) = 100
        _ProbeShadowDistance ("Shadow Distance", Range(0,500)) = 50
        _ProbeCapturePreset ("Capture (0 Environment, 1 With Characters, 2 Custom)", Range(0,2)) = 0
        _ProbeMaskLow ("Custom Layer Mask Bits 0-15", Range(0,65535)) = 65535
        _ProbeMaskHigh ("Custom Layer Mask Bits 16-31", Range(0,65535)) = 65535
        _ProbeSkybox ("Skybox Background", Range(0,1)) = 1
        _ProbeBackground ("Background Color", Color) = (0,0,0,1)
        _ProbeSizeX ("World Size X", Range(0.01,1000)) = 8
        _ProbeSizeY ("World Size Y", Range(0.01,1000)) = 4
        _ProbeSizeZ ("World Size Z", Range(0.01,1000)) = 8
        _ProbeOffsetX ("World Offset X", Range(-1000,1000)) = 0
        _ProbeOffsetY ("World Offset Y", Range(-1000,1000)) = 0
        _ProbeOffsetZ ("World Offset Z", Range(-1000,1000)) = 0
        _SHGridX ("SH Grid X", Range(2,8)) = 4
        _SHGridY ("SH Grid Y", Range(2,8)) = 3
        _SHGridZ ("SH Grid Z", Range(2,8)) = 4
        _SHCaptureQuality ("SH Face Resolution (0 8, 1 16, 2 32, 3 64)", Range(0,3)) = 1
        _SHEdgeFade ("SH Edge Fade", Range(0,0.49)) = 0.15
        _SHSamplesPerFrame ("SH Samples Per Frame (global cap applies)", Range(1,4)) = 1
        _ProbeOcclusionCulling ("Occlusion Culling", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            ColorMask 0
            ZWrite Off
            ZTest Always
            Cull Off
            Stencil { WriteMask 0 }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag() : SV_Target { clip(-1); return 0; }
            ENDCG
        }
    }
    Fallback Off
}

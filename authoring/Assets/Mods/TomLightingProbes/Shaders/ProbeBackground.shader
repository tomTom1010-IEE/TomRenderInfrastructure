Shader "Hidden/Tom/ProbeBackground"
{
    Properties { _TomProbeBackground ("Linear HDR Background", Vector) = (0,0,0,1) }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Pass
        {
            Cull Off ZWrite Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _TomProbeBackground;
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 frag() : SV_Target { return float4(max(_TomProbeBackground.rgb, 0), 1); }
            ENDCG
        }
    }
    Fallback Off
}

// A dedicated rename-only slot; the carrier controls belong on the other material.
Shader "Hidden/TomPlanar/RainSelector" {
    SubShader { Tags { "Queue"="Geometry" "RenderType"="TomPlanarControl" }
        Pass { ZWrite Off ColorMask 0 Cull Off }
    }
    Fallback Off
}

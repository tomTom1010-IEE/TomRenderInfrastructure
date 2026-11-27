Shader "tom/ScreenReflectionControl" {
    Properties {
        [ToggleUI] _ScreenReflectionEnabled ("Enable screen-space reflections", Range(0,1)) = 1
        _ScreenReflectionLiveScale ("Live trace resolution scale", Range(0.25,1)) = 0.5
        _ScreenReflectionSteps ("Maximum linear trace steps", Range(8,128)) = 64
        _ScreenReflectionDistance ("Maximum trace length (m)", Range(0.1,100)) = 24
        _ScreenReflectionThickness ("Hit thickness (m)", Range(0.01,1)) = 0.25
    }
    SubShader { Tags { "Queue"="Geometry" "RenderType"="TomScreenControl" }
        Pass { ZWrite Off ColorMask 0 Cull Off }
    }
    Fallback Off
}

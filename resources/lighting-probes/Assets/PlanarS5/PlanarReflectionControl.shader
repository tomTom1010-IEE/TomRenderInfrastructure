Shader "tom/PlanarReflectionControl" {
    Properties {
        [HideInInspector] _TomPlanarReflectionControl ("Control signature",Float)=1
        _PlanarPlaneEnabled ("Enabled",Range(0,1))=1
        _PlanarPlaneWidth ("Local width (X)",Range(0.01,1000))=10
        _PlanarPlaneLength ("Local length (Z)",Range(0.01,1000))=10
        _PlanarHeightTolerance ("Height tolerance (world m)",Range(0.001,2))=0.05
        _PlanarEdgeFade ("Inside edge fade (world m)",Range(0,10))=0.25
        _PlanarPlaneGroup ("Receiver group",Range(0,65535))=0
        _PlanarPlanePriority ("Priority",Range(-32768,32767))=0
        _PlanarClipBias ("Clip bias",Range(0.0001,0.1))=0.01
        _PlanarCaptureLayersLo ("Layers 0-15",Range(0,65535))=65535
        _PlanarCaptureLayersHi ("Layers 16-31",Range(0,65535))=65535
        _PlanarNear ("Near (0 disables)",Range(0,10000))=0
        _PlanarFar ("Far (0 unlimited)",Range(0,10000))=0
        _PlanarNearFade ("Near fade",Range(0,10000))=0.5
        _PlanarFarFade ("Far fade",Range(0,10000))=2
        _PlanarContentMode ("Include ordinary transparency",Range(0,1))=1
        _PlanarIncludeRain ("Include rain",Range(0,1))=0
        _PlanarAdaptive ("Adaptive live resolution",Range(0,1))=1
        _PlanarBudget ("Adaptive GPU budget (ms)",Range(0.25,32))=6
        _PlanarRealtimeScale ("Fixed resolution scale",Range(0.25,1))=0.5
        _PlanarUpdateInterval ("Live update interval",Range(1,4))=1
    }
    SubShader { Tags { "Queue"="Geometry" "RenderType"="TomPlanarControl" }
        Pass { ZWrite Off ColorMask 0 Cull Off }
    }
    Fallback Off
}

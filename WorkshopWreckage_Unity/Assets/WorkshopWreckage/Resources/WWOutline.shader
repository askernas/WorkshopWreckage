// Hover highlight: enlarged back-face shell (port of Godot outline.gdshader).
Shader "WorkshopWreckage/Outline"
{
    Properties { _Color ("Color", Color) = (1, 0.82, 0.35, 1) _Grow ("Grow", Float) = 0.08 }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Grow;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            float4 vert(A v) : SV_POSITION { return TransformObjectToHClip(v.p.xyz * (1.0 + _Grow)); }
            half4 frag() : SV_Target { return half4(_Color.rgb * (0.8 + 0.2 * sin(_Time.y * 6.0)), 1); }
            ENDHLSL
        }
    }
}

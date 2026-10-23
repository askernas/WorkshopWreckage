// Rust / grime stains on the robots: a multiply blend that tints whatever lit surface lies under
// it (overlapping strips build up darker, so a streak fades towards its tail). No shadow, depth or
// depth-normals pass: stains never cast shadows or touch SSAO.
Shader "WorkshopWreckage/Stain"
{
    Properties { _Color ("Tint", Color) = (0.62, 0.36, 0.2, 1) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }
        Pass
        {
            Name "Stain"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            struct V { float4 cs : SV_POSITION; };
            V vert(A a) { V o; o.cs = TransformObjectToHClip(a.p.xyz); return o; }
            half4 frag(V i) : SV_Target { return half4(_Color.rgb, 1); }
            ENDHLSL
        }
    }
}

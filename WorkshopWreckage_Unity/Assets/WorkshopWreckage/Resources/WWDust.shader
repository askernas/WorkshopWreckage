// Dust motes drifting in the window light: soft additive dots for a ParticleSystem
// (vertex colour carries the particle colour and fade).
Shader "WorkshopWreckage/Dust"
{
    Properties { _Intensity ("Intensity", Float) = 1.5 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" }
        Pass
        {
            Name "Dust"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
            CBUFFER_END
            struct A { float4 p : POSITION; float4 c : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float4 c : COLOR; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.cs = TransformObjectToHClip(a.p.xyz); o.c = a.c; o.uv = a.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float d = length(i.uv - 0.5) * 2;
                float a = saturate(1 - d);
                return half4(i.c.rgb * i.c.a * a * a * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}

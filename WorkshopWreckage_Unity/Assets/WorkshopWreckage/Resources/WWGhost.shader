// Placement preview / destination highlight: translucent additive rim (port of ghost.gdshader).
Shader "WorkshopWreckage/Ghost"
{
    Properties { _Color ("Color", Color) = (0.4, 1, 0.5, 0.35) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            struct V { float4 cs : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };
            V vert(A a)
            {
                V o;
                float3 ws = TransformObjectToWorld(a.p.xyz);
                o.cs = TransformWorldToHClip(ws);
                o.n = TransformObjectToWorldNormal(a.n);
                o.v = GetWorldSpaceNormalizeViewDir(ws);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float pulse = 0.75 + 0.25 * sin(_Time.y * 5.0);
                float rim = pow(1.0 - abs(dot(normalize(i.n), normalize(i.v))), 2.0);
                return half4(_Color.rgb * _Color.a * pulse * (0.35 + 0.65 * rim), 1);
            }
            ENDHLSL
        }
    }
}

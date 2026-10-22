// Sunlight shaft through a window: an additive, unlit prism (the window opening extruded
// along the sun direction). Brightest where you look through a lot of it (grazing view
// of a side face), fading along its length, at its edges and close to the camera, with
// slow drifting dust variation.
Shader "WorkshopWreckage/Shaft"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.85, 0.6, 1)
        _Intensity ("Intensity", Float) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" }
        Pass
        {
            Name "Shaft"
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
                float _Intensity;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; float2 uv : TEXCOORD2; };
            V vert(A a)
            {
                V o;
                o.ws = TransformObjectToWorld(a.p.xyz);
                o.cs = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(a.n);
                o.uv = a.uv;
                return o;
            }
            float hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(hash(i), hash(i + float3(1, 0, 0)), f.x), lerp(hash(i + float3(0, 1, 0)), hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0, 0, 1)), hash(i + float3(1, 0, 1)), f.x), lerp(hash(i + float3(0, 1, 1)), hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }
            half4 frag(V i) : SV_Target
            {
                float3 v = GetWorldSpaceNormalizeViewDir(i.ws);
                float graze = 1 - abs(dot(normalize(i.n), v));
                float along = pow(saturate(1 - i.uv.y), 1.4) * smoothstep(0, 0.06, i.uv.y);
                float edge = smoothstep(0, 0.18, i.uv.x) * smoothstep(1, 0.82, i.uv.x);
                float dist = length(_WorldSpaceCameraPos - i.ws);
                float nearFade = smoothstep(0.4, 2.0, dist);
                float dust = 0.65 + 0.7 * vnoise(i.ws * 1.7 + float3(0, _Time.y * 0.05, _Time.y * 0.03));
                float k = _Intensity * (0.25 + graze * graze) * along * edge * nearFade * dust;
                return half4(_Color.rgb * k, 1);
            }
            ENDHLSL
        }
    }
}

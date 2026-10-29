// Spray-painted wall stencil (BlueprintPosters.cs: "ROBOT WORKSHOP"): the text mask in
// _MainTex.r, paint colour with overspray and wear, lit by the main light + ambient so it
// sits in the room's lighting. Transparent, drawn just in front of the wall.
Shader "WorkshopWreckage/Stencil"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "black" {}
        _Color ("Paint", Color) = (0.9, 0.85, 0.7, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Color;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 ws : TEXCOORD2; };
            V vert(A v)
            {
                V o;
                o.ws = TransformObjectToWorld(v.pos.xyz);
                o.cs = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(v.n);
                o.uv = v.uv;
                return o;
            }
            float H(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float N(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(H(i), H(i + float2(1, 0)), f.x), lerp(H(i + float2(0, 1)), H(i + 1), f.x), f.y);
            }
            half4 frag(V i) : SV_Target
            {
                float m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                // overspray: a soft halo from a blurred sample
                float halo = 0;
                for (int k = 0; k < 6; k++)
                {
                    float a = k * 1.047;
                    halo += SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv + float2(cos(a), sin(a)) * float2(0.004, 0.02), 2).r;
                }
                halo /= 6;
                float wear = smoothstep(0.35, 0.75, N(i.uv * float2(90, 18))) * 0.6 + smoothstep(0.6, 0.9, N(i.uv * float2(400, 80))) * 0.4;
                float alpha = saturate(m * 1.3) * (0.9 - wear * 0.65) + halo * 0.12;
                float3 n = normalize(i.n);
                Light l = GetMainLight();
                // pale paint stays readable in the dim upper wall: a floor under the lighting
                float3 light = max(SampleSH(n) + l.color * saturate(dot(n, l.direction)) * 0.4, 0.62);
                return half4(_Color.rgb * light, saturate(alpha));
            }
            ENDHLSL
        }
    }
}

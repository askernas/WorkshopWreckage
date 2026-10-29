// Robot finish (RobotFinish.cs): painted or bare metal in object space, so the wear moves with
// the robot. Wear follows the construction instead of covering the surface evenly:
// surface curvature, measured per pixel from screen-space derivatives of the object-space
// normal and position (independent of distance), marks bevels and edges. On paint those
// edges are rubbed lighter, scratched and, broken up by noise, chipped through to dark steel;
// on bare metal they are polished brighter. Undersides collect grime. One tileable noise
// texture (R chip breakup, G scratches, B fine grain, A low-frequency blotches) serves all.
Shader "WorkshopWreckage/Robot"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _SmoothVar ("Smoothness variation", Range(0,1)) = 0.1
        _ColorVar ("Colour variation", Range(0,1)) = 0.06
        _NoiseTex ("Noise (R chips, G scratches, B grain, A blotch)", 2D) = "grey" {}
        _NoiseScale ("Noise tiling (1/model unit)", Float) = 5
        _Edge ("Curvature range for edges (lo, hi)", Vector) = (14, 45, 0, 0)
        _Wear ("Edge wear", Range(0,1)) = 0
        _Chips ("How much of a worn edge chips through", Range(0,1)) = 0.2
        _FaceChips ("Small chips on flat panels", Range(0,1)) = 0
        _Fade ("Paint fading on upward faces", Range(0,1)) = 0
        _ChipColor ("Chipped-through metal", Color) = (0.16,0.15,0.14,1)
        _ChipMetallic ("Chip metallic", Range(0,1)) = 0.8
        _ChipSmooth ("Chip smoothness", Range(0,1)) = 0.4
        _Scratch ("Scratches", Range(0,1)) = 0
        _ScratchColor ("Scratch colour", Color) = (0.55,0.52,0.47,1)
        _EdgePolish ("Bare metal: edges polished brighter", Range(0,1)) = 0
        _Grime ("Grime", Range(0,1)) = 0.3
        _GrimeColor ("Grime multiplier", Vector) = (0.55,0.5,0.42,1)
        _Emission ("Emission", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _NoiseTex_ST;
        float4 _Edge;
        float4 _ChipColor;
        float4 _ScratchColor;
        float4 _GrimeColor;
        float _Metallic;
        float _Smoothness;
        float _SmoothVar;
        float _ColorVar;
        float _NoiseScale;
        float _Wear;
        float _Chips;
        float _FaceChips;
        float _Fade;
        float _ChipMetallic;
        float _ChipSmooth;
        float _Scratch;
        float _EdgePolish;
        float _Grime;
        float _Emission;
    CBUFFER_END
    TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    float4 Tri(float3 p, float3 n)
    {
        float3 w = pow(abs(n), 4);
        w /= (w.x + w.y + w.z);
        return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.zy) * w.x
             + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xz) * w.y
             + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xy) * w.z;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 normalOS : TEXCOORD3;
                float fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.positionOS;
                float3 on = normalize(i.normalOS);
                // curvature (1 / radius in model units): flat faces 0, a 2 cm bevel 50
                float curv = length(fwidth(i.normalOS)) / max(length(fwidth(p)), 1e-5);
                float edge = smoothstep(_Edge.x, _Edge.y, curv);
                float4 nz = Tri(p * _NoiseScale, on);                    // panel scale
                float4 nf = Tri(p * (_NoiseScale * 3.7) + 0.37, on);     // fine scale
                float under = saturate(-on.y * 1.4 - 0.2);

                float3 col = _BaseColor.rgb;
                // subtle colour drift across panels (a few percent, slightly warmer where darker)
                float drift = (nz.a - 0.5) * 2 * _ColorVar;
                col *= 1 + drift;
                col = lerp(col, col * float3(1.0, 0.96, 0.9), saturate(-drift * 4) * 0.5);
                float metallic = _Metallic;
                float smooth = _Smoothness + (nf.b - 0.5) * 2 * _SmoothVar + (nz.b - 0.5) * _SmoothVar;

                // rubbed, scratched and chipped edges (paint), polished edges (bare metal)
                float breakup = nz.r * 0.62 + nf.r * 0.38;
                float worn = edge * _Wear;
                float t = 0.64 - 0.2 * _Chips;
                float chip = worn * smoothstep(t, t + 0.05, breakup);   // edges only allow chips; the noise picks the spots
                // a few small chips on the flat panels too (knocks), sparse and fine-scaled
                chip = max(chip, _FaceChips * smoothstep(0.76, 0.79, nf.r * 0.7 + nz.r * 0.45) * (1 - edge * 0.5));
                float rub = worn * (1 - chip);
                col = lerp(col, col * 1.1 + 0.015, rub * 0.45);
                smooth += rub * 0.1;
                float scratch = smoothstep(0.55, 0.85, nf.g) * _Scratch * saturate(0.2 + edge * 1.6 + under * 0.4);
                col = lerp(col, _ScratchColor.rgb, scratch * 0.7);
                // uneven fading where the light falls: paler, slightly greyer patches on upward faces
                float fade = _Fade * saturate((nz.a - 0.42) * 2.2) * saturate(on.y * 0.6 + 0.5);
                col = lerp(col, lerp(col, dot(col, float3(0.3, 0.59, 0.11)).xxx, 0.3) * 1.08 + 0.025, fade);
                smooth += scratch * 0.08;
                col = lerp(col, col * 1.35 + 0.03, edge * _EdgePolish * (0.6 + 0.4 * nf.b));
                smooth += edge * _EdgePolish * 0.15;
                // grime settles underneath and in blotches, and dulls the finish
                float grime = saturate(under * 0.7 + (nz.a - 0.55) * 1.6 + (nf.r - 0.6) * 0.6) * _Grime * (1 - rub * 0.6);
                col *= lerp(float3(1, 1, 1), _GrimeColor.rgb, grime);
                smooth *= 1 - grime * 0.45;
                // chips through to dark steel; the deepest ones have a trace of rust
                float3 chipCol = lerp(_ChipColor.rgb, float3(0.3, 0.15, 0.07), smoothstep(0.78, 0.95, breakup) * 0.55);
                col = lerp(col, chipCol, chip);
                metallic = lerp(metallic, _ChipMetallic, chip);
                smooth = lerp(smooth, _ChipSmooth, chip);

                float3 n = normalize(i.normalWS);
                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(n);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = saturate(col);
                s.metallic = saturate(metallic);
                s.smoothness = saturate(smooth);
                s.occlusion = 1 - grime * 0.3;
                s.alpha = 1;
                s.emission = s.albedo * _Emission;
                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, i.fog);
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            float4 vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            float4 vert(Attributes v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct V2F { float4 cs : SV_POSITION; float3 n : TEXCOORD0; };
            V2F vert(Attributes v) { UNITY_SETUP_INSTANCE_ID(v); V2F o; o.cs = TransformObjectToHClip(v.positionOS.xyz); o.n = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(V2F i) : SV_Target { return half4(NormalizeNormalPerPixel(i.n), 0); }
            ENDHLSL
        }
    }
}

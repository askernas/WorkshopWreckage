// Environment material: world-space triplanar PBR. Albedo + mask (R metallic,
// G smoothness, B ambient occlusion when _AO > 0) + optional triplanar normal map
// (whiteout blend), anti-tiling macro variation (_Macro) for big floors and contact
// grime near the floor (_Grime). Textures are baked (Resources/EnvTex) or generated
// at start-up. _WW_UV switches to mesh UVs (label atlases), _WW_OBJECT to object space.
Shader "WorkshopWreckage/Env"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _MaskTex ("Mask (R metallic, G smoothness)", 2D) = "white" {}
        _Scale ("World tiling (1/m)", Float) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.3
        _Emission ("Emission", Float) = 0
        _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 0
        _AO ("Mask B is occlusion", Range(0,1)) = 0
        _Macro ("Anti-tiling variation", Range(0,1)) = 0
        _Grime ("Contact grime near the floor", Range(0,1)) = 0
        _SilTex ("Painted outlines (R signed distance, G chips, B brush, A scuffs)", 2D) = "black" {}
        _SilOn ("Painted outlines on (_WW_UV)", Float) = 0
        _SilRange ("Outline distance range (m)", Float) = 0.016
        _SilRim ("Outline rim width (m)", Float) = 0.0022
        _SilChip ("Outline chip threshold", Float) = 0.76
        _SilColor ("Outline fill (a = opacity)", Color) = (0.17, 0.1, 0.06, 0.9)
        _SilRimColor ("Outline rim", Color) = (0.08, 0.04, 0.02, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _MainTex_ST;
        float _Scale;
        float _Metallic;
        float _Smoothness;
        float _Emission;
        float _BumpScale;
        float _AO;
        float _Macro;
        float _Grime;
        float _SilOn;
        float _SilRange;
        float _SilRim;
        float _SilChip;
        float4 _SilColor;
        float4 _SilRimColor;
    CBUFFER_END
    TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
    TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
    TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
    TEXTURE2D(_SilTex); SAMPLER(sampler_SilTex);

    // the pegboard's painted tool outlines (Pegboard/PegboardMaterials.cs): a signed distance field, so the edge
    // stays crisp at any distance; a darker rim inside the edge, brush streaks, chips and scuffs from noise channels
    float3 PaintedOutlines(float3 c, float2 uv)
    {
        float4 t = SAMPLE_TEXTURE2D(_SilTex, sampler_SilTex, uv);
        float d = (t.r - 0.5) * _SilRange;                       // metres from the outline, + inside
        float w = max(fwidth(d), 1e-5);
        float gw = max(fwidth(t.g), 1e-4);
        float inside = saturate(d / w + 0.5);
        float rim = (1 - saturate((d - _SilRim) / w + 0.5)) * inside;
        float chip = saturate((t.g - (_SilChip - 0.06 * rim)) / gw + 0.5);   // knocked off along the rim first
        float scuff = smoothstep(0.68, 0.82, t.a) * 0.25;
        float3 fill = _SilColor.rgb * (0.86 + 0.26 * t.b);
        fill = lerp(fill, _SilRimColor.rgb, rim);
        return lerp(c, fill, inside * _SilColor.a * (1 - chip * 0.85) * (1 - scuff));
    }

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    float4 SampleTri(TEXTURE2D_PARAM(tex, smp), float3 p, float3 n)
    {
        float3 w = pow(abs(n), 4);
        w /= (w.x + w.y + w.z);
        float4 x = SAMPLE_TEXTURE2D(tex, smp, p.zy * _Scale);
        float4 y = SAMPLE_TEXTURE2D(tex, smp, p.xz * _Scale);
        float4 z = SAMPLE_TEXTURE2D(tex, smp, p.xy * _Scale);
        return x * w.x + y * w.y + z * w.z;
    }

    // triplanar normal mapping, whiteout blend (Ben Golus)
    float3 TriNormal(float3 p, float3 n)
    {
        float3 w = pow(abs(n), 4);
        w /= (w.x + w.y + w.z);
        float3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.zy * _Scale), _BumpScale);
        float3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.xz * _Scale), _BumpScale);
        float3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.xy * _Scale), _BumpScale);
        float3 s = sign(n);
        tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
        ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
        tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
        return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
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
            #pragma multi_compile_local _ _WW_UV _WW_OBJECT
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
                float2 uv : TEXCOORD2;
                float fog : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                float3 normalOS : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                #if defined(_WW_UV)
                    float4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                    float4 mask = float4(1, 1, 1, 1);
                    if (_SilOn > 0) albedo.rgb = PaintedOutlines(albedo.rgb, i.uv);
                #elif defined(_WW_OBJECT)
                    // things that move (robots): the wear sticks to the object instead of the room
                    float3 on = normalize(i.normalOS);
                    float4 albedo = SampleTri(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), i.positionOS, on);
                    float4 mask = SampleTri(TEXTURE2D_ARGS(_MaskTex, sampler_MaskTex), i.positionOS, on);
                #else
                    float4 albedo = SampleTri(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), i.positionWS, n);
                    float4 mask = SampleTri(TEXTURE2D_ARGS(_MaskTex, sampler_MaskTex), i.positionWS, n);
                    if (_Macro > 0)
                    {
                        // a second, much larger sample breaks up the repetition of big surfaces
                        float3 q = i.positionWS * 0.137 + 0.31;
                        float3 m2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, q.xz * _Scale).rgb;
                        float l = dot(m2, float3(0.3, 0.59, 0.11)) / max(dot(albedo.rgb, float3(0.3, 0.59, 0.11)), 0.05);
                        albedo.rgb *= lerp(1, clamp(l, 0.65, 1.35), _Macro);
                    }
                    if (_BumpScale > 0) n = TriNormal(i.positionWS, n);
                    if (_Grime > 0) albedo.rgb *= 1 - _Grime * (1 - smoothstep(0.0, 0.55, i.positionWS.y)) * 0.45;
                #endif
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
                s.albedo = albedo.rgb * _BaseColor.rgb;
                s.metallic = _Metallic * mask.r;
                s.smoothness = _Smoothness * mask.g;
                s.occlusion = lerp(1, mask.b, _AO);
                s.alpha = 1;
                s.emission = s.albedo * _Emission;
                half4 col = UniversalFragmentPBR(d, s);
                col.rgb = MixFog(col.rgb, i.fog);
                return col;
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

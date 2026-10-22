// Environment material: world-space triplanar albedo (brick, planks, wood,
// painted metal textures generated at start-up), tint, metallic/smoothness
// from a mask texture. _WW_UV switches to mesh UVs (label atlases).
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
    CBUFFER_END
    TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
    TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);

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
                #elif defined(_WW_OBJECT)
                    // things that move (robots): the wear sticks to the object instead of the room
                    float3 on = normalize(i.normalOS);
                    float4 albedo = SampleTri(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), i.positionOS, on);
                    float4 mask = SampleTri(TEXTURE2D_ARGS(_MaskTex, sampler_MaskTex), i.positionOS, on);
                #else
                    float4 albedo = SampleTri(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), i.positionWS, n);
                    float4 mask = SampleTri(TEXTURE2D_ARGS(_MaskTex, sampler_MaskTex), i.positionWS, n);
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
                s.occlusion = 1;
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

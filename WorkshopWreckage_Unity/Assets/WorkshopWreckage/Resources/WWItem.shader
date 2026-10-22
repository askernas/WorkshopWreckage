// Shared material for every item (port of Godot's item_world.gdshader).
// Instanced draws (keyword _WW_INSTANCED): per-instance albedo + (metallic,
// roughness, wear, seed) come from _ItemData[(offset + instanceID) * 2 + {0,1}].
// Non-instanced draws (inspection, held items) use the material properties.
// Vertex colour rgb tints, vertex alpha marks threaded regions (UV.y = turns).
Shader "WorkshopWreckage/Item"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 1
        _Roughness ("Roughness", Range(0,1)) = 0.4
        _Wear ("Wear", Range(0,1)) = 0.3
        _Seed ("Seed", Float) = 0
        _WearNoise ("Wear Noise", 2D) = "white" {}
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float _Metallic;
        float _Roughness;
        float _Wear;
        float _Seed;
    CBUFFER_END

    TEXTURE2D(_WearNoise); SAMPLER(sampler_WearNoise);
    StructuredBuffer<float4> _ItemData;
    float _ItemOffset;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        float4 color : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    void InstanceParams(out float4 albedo, out float4 custom)
    {
        #if defined(_WW_INSTANCED) && defined(UNITY_INSTANCING_ENABLED)
            uint idx = ((uint)_ItemOffset + unity_InstanceID) * 2;
            albedo = _ItemData[idx];
            custom = _ItemData[idx + 1];
        #else
            albedo = _BaseColor;
            custom = float4(_Metallic, _Roughness, _Wear, _Seed);
        #endif
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // Double-sided: any hole or wrongly wound face in a procedural part still renders
            // (back faces get a flipped normal) instead of letting you see into the part.
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _WW_INSTANCED
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
                float4 color : TEXCOORD3;
                float3 objPos : TEXCOORD4;
                float3 axisWS : TEXCOORD5;
                float4 custom : TEXCOORD6;
                float fog : TEXCOORD7;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float4 albedo, custom;
                InstanceParams(albedo, custom);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.axisWS = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                o.uv = v.uv;
                o.color = float4(v.color.rgb * albedo.rgb, v.color.a);
                o.objPos = v.positionOS.xyz;
                o.custom = custom;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                i.normalWS = IS_FRONT_VFACE(face, i.normalWS, -i.normalWS);
                float4 c = i.custom;
                float n = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, i.objPos.xy * 0.45 + i.objPos.zz * 0.21 + c.w * 7.0).r;
                float grime = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, i.objPos.zx * 0.13 + c.w * 3.0).r;
                float wear = c.z;
                float thread = i.color.a;
                float ridge = sin(i.uv.y * 6.2831853);
                float3 baseCol = i.color.rgb;
                baseCol *= lerp(1.0, 0.62 + 0.55 * n, wear);
                baseCol = lerp(baseCol, baseCol * float3(0.55, 0.45, 0.35), smoothstep(0.62, 0.85, grime) * wear * 0.6);
                baseCol *= 1.0 - thread * 0.32 * (0.5 - 0.5 * ridge);
                float rough = clamp(c.y + (n - 0.5) * 0.4 * wear + thread * 0.08, 0.18, 1.0);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.normalWS = normalize(normalize(i.normalWS) + i.axisWS * ridge * 0.5 * thread);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = baseCol;
                s.metallic = c.x;
                s.smoothness = 1.0 - rough;
                s.occlusion = 1.0;
                s.alpha = 1.0;
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
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
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
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            float4 vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(v.positionOS.xyz);
            }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct V2F { float4 cs : SV_POSITION; float3 n : TEXCOORD0; };
            V2F vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V2F o;
                o.cs = TransformObjectToHClip(v.positionOS.xyz);
                o.n = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(V2F i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target { return half4(NormalizeNormalPerPixel(IS_FRONT_VFACE(face, i.n, -i.n)), 0); }
            ENDHLSL
        }
    }
}

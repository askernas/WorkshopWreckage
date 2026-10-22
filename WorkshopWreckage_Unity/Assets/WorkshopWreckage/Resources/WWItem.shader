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
    // salvage look: the share of the PBR reflection kept (0.12: far below a clean part's);
    // a global so QA can compare (0 or unset = the 0.25 default)
    float _WWItemGloss;
    float _WWItemDebug;   // QA (--itemdebug): 1 normals, 2 ambient only, 3 main light only, 4 additional lights only

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
            // Double-sided: any hole in a procedural part still renders instead of letting
            // you see into the part. Shading always uses the mesh normal (verified outward
            // for every exported part): the instanced draws report front faces as back
            // faces, and flipping by SV_IsFrontFace turned every upward face black.
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
                float matClass : TEXCOORD8;   // 1 bare metal, 0.5 painted, ~0 electronics / accents
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
                o.matClass = albedo.a;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 c = i.custom;
                float n = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, i.objPos.xy * 0.45 + i.objPos.zz * 0.21 + c.w * 7.0).r;
                float grime = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, i.objPos.zx * 0.13 + c.w * 3.0).r;
                float wear = c.z;
                float thread = i.color.a;
                float ridge = sin(i.uv.y * 6.2831853);
                float3 baseCol = i.color.rgb;
                // ---- salvage: grime, rust, chipped paint, scratches, dents, dust (all scale with wear)
                float cls = i.matClass;
                float metalW = saturate((cls - 0.6) * 4.0);
                float paintW = saturate(1.0 - abs(cls - 0.5) * 6.0);
                float big = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, i.objPos.xz * 0.31 + i.objPos.yy * 0.17 + c.w * 5.0).r;
                float fine = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, i.objPos.xy * 2.3 + i.objPos.zz * 1.7 + c.w * 11.0).r;
                float scr = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, float2(i.objPos.x * 9.0 + i.objPos.z * 3.0, i.objPos.y * 0.6) + c.w * 13.0).r;
                baseCol *= lerp(1.0, 0.55 + 0.6 * n, wear);                                       // uneven, handled surface
                baseCol *= lerp(1.0, 0.78, wear);                                                 // salvage is dull, never factory-bright
                float oil = smoothstep(0.5, 0.8, grime) * wear;
                baseCol = lerp(baseCol, baseCol * float3(0.42, 0.36, 0.3), oil * 0.8);            // oily grime (large)
                float dirt = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, i.objPos.yz * 1.1 + i.objPos.xx * 0.7 + c.w * 23.0).r;
                baseCol = lerp(baseCol, baseCol * float3(0.5, 0.44, 0.38), smoothstep(0.55, 0.75, dirt) * wear * 0.6);   // smudges (small)
                float3 rustCol = lerp(float3(0.30, 0.17, 0.09), float3(0.45, 0.25, 0.12), fine);
                // rust in patches (about a third of a fully worn part), steel stays steel elsewhere
                float rustMask = smoothstep(0.64 - 0.25 * wear, 0.76 - 0.25 * wear, big * 0.6 + fine * 0.4) * wear * metalW;
                // paint chips: small and mostly at the part's edges / corners (far from its centre)
                float chipN = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, i.objPos.xy * 7.0 + i.objPos.zz * 5.0 + c.w * 17.0).r;
                float edge = saturate(length(i.objPos) * 1.6 - 0.45);
                float chip = smoothstep(0.76 - 0.15 * wear, 0.80 - 0.15 * wear, chipN * (0.75 + 0.45 * edge)) * paintW * wear;
                baseCol = lerp(baseCol, lerp(float3(0.24, 0.22, 0.2), rustCol, 0.35), chip);
                baseCol = lerp(baseCol, rustCol, rustMask);
                float scratch = smoothstep(0.74, 0.77, scr) * wear * (1.0 - rustMask);
                baseCol = lerp(baseCol, baseCol * 1.3 + 0.05, scratch * 0.45);                     // bright scratches
                float up = saturate(i.normalWS.y);
                float dust = up * up * wear * (0.3 + 0.45 * grime);
                baseCol = lerp(baseCol, float3(0.41, 0.39, 0.36), dust * 0.55);                    // dust on top faces
                baseCol *= 1.0 - thread * 0.32 * (0.5 - 0.5 * ridge);
                float rough = clamp(c.y + (n - 0.5) * 0.4 * wear + thread * 0.08, 0.18, 1.0);
                rough = lerp(rough, 1.0, max(max(rustMask, chip), saturate(dust * 1.5)));
                rough = lerp(rough, 1.0, 0.35 * wear);
                i.normalWS += (float3(fine - 0.5, 0, big - 0.5)) * 0.35 * wear;                   // small dents

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
                float gloss = _WWItemGloss > 0 ? _WWItemGloss : 0.12;
                s.metallic = c.x * (1.0 - rustMask) * (1.0 - chip) * 0.3;
                s.smoothness = (1.0 - rough) * gloss;   // 75 % less reflection: no plastic sheen, even on new parts
                s.occlusion = 1.0;
                s.alpha = 1.0;
                half4 col = UniversalFragmentPBR(d, s);
                if (_WWItemDebug > 0.5)
                {
                    if (_WWItemDebug < 1.5) return half4(d.normalWS * 0.5 + 0.5, 1);
                    if (_WWItemDebug < 2.5) return half4(d.bakedGI, 1);
                    if (_WWItemDebug < 3.5)
                    {
                        Light ml = GetMainLight(d.shadowCoord);
                        return half4(ml.color * ml.shadowAttenuation * ml.distanceAttenuation * saturate(dot(d.normalWS, ml.direction)), 1);
                    }
                    half3 acc = 0;
                    uint n = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP || USE_FORWARD_PLUS
                    InputData inputData = d;
                    LIGHT_LOOP_BEGIN(n)
                        Light al = GetAdditionalLight(lightIndex, d.positionWS, half4(1, 1, 1, 1));
                        acc += al.color * al.distanceAttenuation * al.shadowAttenuation * saturate(dot(d.normalWS, al.direction));
                    LIGHT_LOOP_END
                    #endif
                    return half4(acc, 1);
                }
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
            half4 frag(V2F i) : SV_Target { return half4(NormalizeNormalPerPixel(i.n), 0); }
            ENDHLSL
        }
    }
}

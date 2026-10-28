// Shared material for every item (port of Godot's item_world.gdshader).
// Instanced draws (keyword _WW_INSTANCED): per-instance albedo + (metallic,
// roughness, wear, seed) come from _ItemData[(offset + instanceID) * 2 + {0,1}].
// Non-instanced draws (inspection, held items) use the material properties.
// Vertex colour rgb tints, vertex alpha marks threaded regions (UV.y = turns).
// Skins (ItemSkins.cs, docs/SKIN_FIRST_COMPONENT_AUDIT.md): two more per-instance vectors
// (skin A, skin B) choose a family look; UV1 / UV2 (SkinUV.cs) give each surface its edge
// distance and label placement. Family 0 keeps the original salvage look (robot parts).
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
        _Decals ("Label Atlas", 2D) = "black" {}
        _SkinA ("Skin A", Vector) = (0,0,0,0)
        _SkinB ("Skin B", Vector) = (0,0,0,0)
        _Slot ("Material Slot", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float _Metallic;
        float _Roughness;
        float _Wear;
        float _Seed;
        float4 _SkinA;
        float4 _SkinB;
        float _Slot;
    CBUFFER_END

    TEXTURE2D(_WearNoise); SAMPLER(sampler_WearNoise);
    TEXTURE2D(_Decals); SAMPLER(sampler_Decals);
    StructuredBuffer<float4> _ItemData;
    float _ItemOffset;
    // salvage look: the share of the PBR reflection kept (0.12: far below a clean part's);
    // a global so QA can compare (0 or unset = the 0.25 default)
    float _WWItemGloss;
    float _WWItemDebug;   // QA (--itemdebug): 1 normals, 2 ambient only, 3 main light only, 4 additional lights only, 5 cavity mask (skins), 6 art layer masks (skins: chip, worn edge, wash)
    // art direction (WorkshopArtDirectionProfile, docs/ART_DIRECTION_PASS.md); every one 0 = the look before the pass:
    // saturation lift, warm rim on silhouettes (scaled by the ambient there), ambient lift, less salvage mottling, more gloss;
    // skins: cavity wash, grime, chipped edge wear, edge highlight, paint roughness and metallic response
    float _WWArtSat, _WWArtRim, _WWArtAmbient, _WWArtWear, _WWArtGloss;
    float _WWArtWash, _WWArtGrime, _WWArtEdgeWear, _WWArtEdge, _WWArtRough, _WWArtMetal, _WWArtWashEdge, _WWArtChipSize;
    float4 _WWArtRimColor, _WWArtWashColor, _WWArtGrimeColor;
    // the visual sandbox's experiments (Scripts/VisualSandbox/VisualStyle.cs): 0, as in the game, skips them
    float _WWLabBevel, _WWLabBevelW, _WWLabNormal;
    // sandbox Variant B "Playful Industrial" (docs/VISUAL_SANDBOX.md): _WWLabB 0 (the game) skips all of it.
    // Paint smoothness (absolute, 0 = as authored), roughness variation, metal response (0 = the item gloss damping,
    // 1 = full PBR like the robots), rust in old chips and recesses, the room's ambient in reflections, edge polish
    float _WWLabB, _WWLabPaintSmooth, _WWLabRoughVar, _WWLabMetalResp, _WWLabRust, _WWLabEnvRefl, _WWLabEdgePolish;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        float2 uv1 : TEXCOORD1;   // skin: (u, v) on the surface
        float4 uv2 : TEXCOORD2;   // skin: (size u, size v, kind, label slot)
        float2 uv3 : TEXCOORD3;   // skin: cavity mask (concave, convex), CavityMask.cs
        float4 color : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    // tin / accent slots (_Slot 1 / 2) keep their own colour and finish; the skin and seed
    // always come from the part
    void InstanceParams(out float4 albedo, out float4 custom, out float4 skinA, out float4 skinB)
    {
        #if defined(_WW_INSTANCED) && defined(UNITY_INSTANCING_ENABLED)
            uint idx = ((uint)_ItemOffset + unity_InstanceID) * 4;
            albedo = _ItemData[idx];
            custom = _ItemData[idx + 1];
            skinA = _ItemData[idx + 2];
            skinB = _ItemData[idx + 3];
            if (_Slot > 0.5)
            {
                albedo = _BaseColor;
                custom = float4(_Metallic, _Roughness, _Wear, custom.w);
            }
        #else
            albedo = _BaseColor;
            custom = float4(_Metallic, _Roughness, _Wear, _Seed);
            skinA = _SkinA;
            skinB = _SkinB;
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
            #pragma multi_compile_local _ _WW_SKIN
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
                #if defined(_WW_SKIN)   // the original look reads none of these: keep them out of its interpolants
                float2 suv : TEXCOORD9;
                float4 sinfo : TEXCOORD10;
                nointerpolation float4 skinA : TEXCOORD11;
                nointerpolation float4 skinB : TEXCOORD12;
                nointerpolation float scale : TEXCOORD13;   // mesh units -> metres
                float2 cav : TEXCOORD14;                     // (concave, convex)
                #endif
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float4 albedo, custom, skinA, skinB;
                InstanceParams(albedo, custom, skinA, skinB);
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
                #if defined(_WW_SKIN)
                o.suv = v.uv1;
                o.sinfo = v.uv2;
                o.cav = v.uv3;
                o.skinA = skinA;
                o.skinB = skinB;
                o.scale = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                #endif
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float WN(float2 p) { return SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, p).r; }
            bool Near(float3 a, float3 b, float tol) { float3 d = abs(a - b); return max(d.x, max(d.y, d.z)) < tol; }

            #if defined(_WW_SKIN)
            // ---- skins: material separation, wear that tells a mechanical story, labels.
            // Skin A.x packs family + 8 * wear profile + 64 * metal finish (ItemSkins.cs).
            // Wear comes from a few shared masks, all scaled by the part's condition:
            //   edge     thin band along a surface's edges (handled corners, chipped paint)
            //   contact  where mating parts and tools rub: thread crests, nut faces, washer rings,
            //            gear flanks, bearing races and pins (from the profile and the part's axis)
            //   recess   thread valleys and gear roots: darker, oil, the first rust
            //   scratch  sparse directional scratches on worn parts
            static float4 gArtDbg = 0;   // QA (--itemdebug=6): the art layers' masks (chip, worn edge, wash)
            static float3 gLabB = float3(0, 0, 1);   // sandbox Variant B: paint coverage, roughness variation, reflection occlusion

            // pegboard tools: wood with its grain along the handle. On a turned handle the grain runs along the axis
            // (uv.y) and the normal picks the streak round the handle; on a flat face it follows the longer side.
            float3 ToolWood(float3 vc, float2 uv, float kind, float4 sinfo, float scale, float3 nWS, float seed, float edge, float cond, out float rough)
            {
                float2 g;
                if (kind > 0.5 && kind < 1.5) g = float2(dot(normalize(nWS), float3(2.1, 1.3, 2.9)) * 1.6, uv.y * sinfo.y * scale * 2.5);
                else
                {
                    float2 m = uv * sinfo.xy * scale;
                    g = sinfo.y >= sinfo.x ? float2(m.x * 45.0, m.y * 2.5) : float2(m.y * 45.0, m.x * 2.5);
                }
                g += seed * 13.0;
                float w1 = WN(g);
                float w2 = WN(float2(g.x * 2.3 + w1 * 0.8, g.y * 0.6) + 0.37);
                float streak = smoothstep(0.3, 0.75, w1 * 0.55 + w2 * 0.45);
                float pore = smoothstep(0.6, 0.68, WN(float2(g.x * 7.0, g.y * 30.0)));
                float3 c = vc * lerp(0.6, 1.18, streak) * (1.0 - pore * 0.2);
                c = lerp(c, c * 0.55, edge * (0.25 + 0.5 * cond));   // ends and arrises darkened by hands and oil
                rough = lerp(0.66, 0.5, streak) - 0.08 * cond;       // handled wood polishes a little
                return c;
            }

            void Skin(Varyings i, out float3 col, out float metal, out float rough, out float3 emit, inout float3 nWS)
            {
                float packed = round(i.skinA.x);
                float fam = fmod(packed, 8);
                float prof = fmod(floor(packed / 8), 8);
                float fin = fmod(floor(packed / 64), 8);
                // pegboard tools (Pegboard/ToolBatch.cs): +512; condition per tool in UV2.w (+10) of the merged mesh,
                // family 7 = wood, and plastic, wood and steel parts never chip to bare steel
                bool tool = packed > 511.5;
                float cond = tool && i.sinfo.w > 9.5 ? i.sinfo.w - 10 : i.skinB.w;
                float seed = i.custom.w;
                float3 wp = i.objPos * i.scale;
                float kind = round(i.sinfo.z);
                float2 uv = i.suv;
                emit = 0;
                bool hardware = prof > 2.5;

                // distance to the surface's edges, in metres
                float ed;
                if (kind < 0.5) ed = min(min(uv.x, 1 - uv.x) * i.sinfo.x, min(uv.y, 1 - uv.y) * i.sinfo.y);
                else if (kind < 1.5) ed = min(uv.y, 1 - uv.y) * i.sinfo.y;
                else { ed = (1 - uv.x) * i.sinfo.x; if (uv.y > 0.5) ed = min(ed, uv.x * i.sinfo.x); }
                ed *= i.scale;
                float nEdge = WN(wp.xy * 31.0 + wp.zz * 17.0 + seed * 7.0);
                float nFine = WN(wp.yz * 90.0 + wp.xx * 53.0 + seed * 11.0);
                float big = WN(wp.xz * 3.1 + wp.yy * 1.7 + seed * 5.0);
                // the worn band scales down on small faces, so a nut's flank is not all "edge"
                float faceSize = (kind < 0.5 ? min(i.sinfo.x, i.sinfo.y) : kind < 1.5 ? i.sinfo.y : i.sinfo.x) * i.scale;
                float edgeW = clamp(faceSize * 0.12, 0.0008, 0.0045);
                float edge = 1 - smoothstep(0.0, edgeW, ed - (nEdge - 0.5) * edgeW);
                float edgeWide = 1 - smoothstep(0.0, 0.02, ed);
                float brk = smoothstep(0.45, 0.75, nFine * 0.6 + nEdge * 0.6);   // breaks an edge into separate chips

                // machining direction: brushed along a flat face, lathe rings on cylinders and discs
                float mach;
                if (kind < 0.5) mach = WN(float2(uv.x * i.sinfo.x * i.scale * 4.0, uv.y * i.sinfo.y * i.scale * 260.0 + seed));
                else if (kind < 1.5) mach = WN(float2(uv.y * i.sinfo.y * i.scale * 300.0, seed * 3.0));
                else mach = WN(float2(uv.x * i.sinfo.x * i.scale * 400.0, seed * 5.0));

                // mechanical masks
                float axial = abs(dot(normalize(nWS), i.axisWS));   // 1 on faces across the part's axis: heads, nut faces, washer and gear faces
                float thread = i.color.a;
                float ridge = sin(i.uv.y * 6.2831853);              // +1 thread crest, -1 valley
                float contact = edge * brk;
                float recess = thread * saturate(-ridge);
                if (prof > 2.5 && prof < 3.5)                        // fasteners: head edges, nut corners, thread crests, bearing faces of nuts
                    contact = max(max(contact, thread * saturate(ridge) * 0.8), (1 - thread) * smoothstep(0.92, 0.99, axial) * 0.35);
                else if (prof > 3.5 && prof < 4.5)                   // washers: the ring the bolt head bears on
                    contact = max(contact * 0.6, kind > 1.5 ? (1 - smoothstep(0.05, 0.5, uv.x)) * step(0.5, uv.y) : 0);
                else if (prof > 4.5 && prof < 5.5)                   // gears: tooth flanks and bore; roots and hub stay darker
                {
                    contact = max(contact * 0.5, 1 - smoothstep(0.25, 0.6, axial));
                    float rr = length(uv - 0.5) * 2;
                    recess = max(recess, kind < 0.5 ? smoothstep(0.6, 0.78, rr) * (1 - smoothstep(0.86, 0.95, rr)) * smoothstep(0.9, 0.98, axial) : 0);
                }
                else if (prof > 5.5 && prof < 6.5) contact = 0;      // springs: one material, no blotches
                else if (prof > 6.5)                                 // pivots: races, pins and knuckles run polished
                    contact = max(contact * 0.6, kind > 0.5 && kind < 1.5 ? 0.8 : 0);
                float polish = contact * (0.35 + 0.65 * cond);
                float scratch = 0;
                if (kind < 0.5) scratch = smoothstep(0.88, 0.93, WN(float2(uv.x * i.sinfo.x * i.scale * 6.0 + seed * 9.0, uv.y * i.sinfo.y * i.scale * 600.0)));
                else scratch = smoothstep(0.88, 0.93, WN(float2(wp.x * 7.0 + wp.z * 5.0 + seed * 9.0, wp.y * 600.0)));
                scratch *= saturate(cond * 2.2 - 0.45) * (hardware ? 0.4 : 1.0);   // small parts: scratches would read as blotches

                float3 vc = i.color.rgb;
                col = vc;
                metal = 0; rough = 0.55;
                float rs = frac(seed * 3.7);   // old paint under the rust / wear: one of the linear enamels (selects, not an indexed array)
                float3 remnant = rs < 0.25 ? float3(0.013, 0.047, 0.16) : rs < 0.5 ? float3(0.055, 0.078, 0.027) : rs < 0.75 ? float3(0.17, 0.012, 0.006) : float3(0.45, 0.21, 0.007);
                float3 bare = float3(0.36, 0.36, 0.37);
                float3 rustDark = float3(0.085, 0.03, 0.012), rustLight = float3(0.25, 0.08, 0.022);

                if (_Slot < 0.5)
                {
                    if (fam < 1.5)          // painted enamel over steel: chips at edges and rubbing points show primer, then steel
                    {
                        col *= 1 + (big - 0.5) * 0.1 * cond;
                        float chip = smoothstep(0.5, 0.7, contact * (0.55 + cond));
                        float3 chipCol = lerp(float3(0.04, 0.035, 0.03), bare, smoothstep(0.75, 0.95, edge * nFine * 1.6));
                        col = lerp(col, chipCol, chip);
                        col = lerp(col, col * 0.75 + bare * 0.2, scratch * 0.5);
                        metal = chip * 0.7;
                        rough = lerp(0.42 + 0.14 * cond, 0.4, chip);
                        if (tool)           // painted tools: the odd knock across the face too, in patches, more on older tools
                        {
                            float knockT = smoothstep(0.585, 0.605, WN(wp.xy * 9.0 + wp.zz * 6.0 + seed * 13.0) * (0.85 + 0.3 * big));   // the fBm noise peaks near 0.7
                            knockT *= saturate(0.45 + cond);
                            col = lerp(col, lerp(bare * 0.85, float3(0.05, 0.04, 0.035), smoothstep(0.4, 0.7, nFine)), knockT);
                            metal = max(metal, knockT * 0.7); rough = lerp(rough, 0.42, knockT);
                        }
                    }
                    else if (fam < 3.5)     // bare and warm metal: the finish decides colour, gloss and pattern
                    {
                        float machAmt = 0.12, m = 0.75, r = 0.4;
                        if (fin < 0.5) { }                                                       // plain steel: turned, faint oil
                        else if (fin < 1.5) { machAmt = 0.16; m = 0.85; r = 0.3; }                // stainless: brushed
                        else if (fin < 2.5)                                                       // zinc plate: blue-silver crystals
                        {
                            float spangle = WN(wp.xz * 38.0 + wp.yy * 26.0 + seed * 4.0);
                            col *= 0.93 + 0.12 * smoothstep(0.42, 0.58, spangle);
                            machAmt = 0.04; m = 0.7; r = 0.42;
                        }
                        else if (fin < 3.5) { machAmt = 0.06; m = 0.55; r = 0.36; }              // black oxide: oily satin black
                        else if (fin < 4.5) { col *= 0.96 + 0.06 * nFine; machAmt = 0.04; m = 0.8; r = 0.5; }   // aluminium: bead-blasted, soft
                        else if (fin < 5.5) { machAmt = 0.05; m = 0.95; r = 0.13; }              // polished
                        else { machAmt = 0.08; m = 0.9; r = 0.34; }                               // brass, copper
                        if (!hardware && kind < 1.5 && fin < 4.5 && !tool)                        // housings: cast or blasted body, turned faces stay bright
                        {
                            float grain = WN(wp.xy * 140.0 + wp.zz * 90.0);
                            col *= 0.9 + 0.1 * grain;
                            m *= 0.6; r += 0.12 + 0.1 * grain;
                        }
                        col *= 1 + (mach - 0.5) * 2 * machAmt;
                        rough = r + (mach - 0.5) * machAmt + 0.1 * cond;
                        metal = m;
                        if (tool) { metal = min(m, 0.5); col *= 1.3; }   // hand tools: bright forged chrome under the warm board lights, not the room's dark reflection
                        if (fin > 5.5 || (fam > 2.5 && fin > 4.5))                                // warm metals tarnish evenly; handled spots stay bright
                        {
                            float tarn = saturate((0.12 + 0.7 * cond) * (0.75 + 0.25 * big + 0.25 * nEdge)) * (1 - polish);
                            col = lerp(col, col * float3(0.5, 0.4, 0.3), tarn * 0.75);
                            rough = lerp(rough, 0.62, tarn);
                        }
                        if (fin > 2.5 && fin < 3.5) col = lerp(col, bare * 1.05, polish * cond * 0.8);   // black oxide rubs through to steel
                        else
                        {
                            col = lerp(col, col * 1.25 + 0.02, polish * 0.6);
                            rough = lerp(rough, rough * 0.55, polish);
                        }
                        if (fin < 0.5)                                                            // plain steel: oil in recesses, first rust specks when old
                        {
                            col *= 1 - 0.15 * cond * (1 - polish);
                            col = lerp(col, float3(0.05, 0.04, 0.03), recess * 0.4);
                            col = lerp(col, rustLight, smoothstep(0.8, 0.86, nFine) * max(recess, edge) * saturate(cond * 2.5 - 1.0));
                        }
                        else col = lerp(col, col * 0.6, recess * 0.4);
                        col = lerp(col, col * 0.8, scratch * 0.4);
                    }
                    else if (fam < 4.5)     // oxidised: one coherent rust coat, pitting, fresh orange rust in recesses
                    {
                        float r1 = WN(wp.xy * 40.0 + wp.zz * 23.0 + seed * 3.0);
                        col = lerp(rustDark, rustLight, smoothstep(0.25, 0.8, big * 0.4 + r1 * 0.35 + nEdge * 0.25));
                        col = lerp(col, rustDark * 0.6, smoothstep(0.62, 0.8, nEdge * 0.6 + big * 0.4) * 0.5);   // patches of dark old scale
                        col *= 1 - smoothstep(0.78, 0.85, nFine) * 0.35;
                        col = lerp(col, float3(0.32, 0.11, 0.025), recess * 0.6);
                        float maintained = 1 - smoothstep(0.62, 0.85, cond);                      // handled parts: high points rubbed back to dark steel
                        col = lerp(col, float3(0.09, 0.075, 0.065), contact * maintained * 0.6);
                        if (!hardware && kind < 0.5)                                              // housings: the odd patch of old paint on broad faces
                        {
                            float paint = smoothstep(0.68, 0.72, big * 0.8 + nFine * 0.3) * (1 - edgeWide);
                            col = lerp(col, remnant * 0.75, paint * 0.8);
                        }
                        rough = lerp(0.9, 0.6, contact * maintained);
                        metal = contact * maintained * 0.4;
                        nWS += (float3(nFine, r1, nEdge) - 0.5) * 0.15;
                    }
                    else if (fam < 5.5)     // used
                    {
                        if (hardware)       // used bare steel: dull grey, polished where it works, grime and rust specks in recesses
                        {
                            col *= 1.12 + 0.05 * big + (mach - 0.5) * 0.12;
                            float film = smoothstep(0.45, 0.75, nEdge * 0.7 + big * 0.3) * (1 - polish);   // uneven oxidation: a dull, faintly warm film, never orange
                            col = lerp(col, col * float3(0.78, 0.7, 0.62), film * 0.55);
                            col = lerp(col, bare * 1.35, polish * 0.65);
                            col = lerp(col, bare * 1.3, edge * (1 - recess) * 0.3);                 // worn edges catch the light
                            col *= 1 - 0.45 * recess;                                               // dark thread roots and gear roots
                            col = lerp(col, lerp(rustDark * 1.6, rustLight, nFine), recess * smoothstep(0.35, 0.65, nFine * 0.6 + nEdge * 0.4) * 0.7);   // corrosion creeps along them
                            col = lerp(col, rustLight, smoothstep(0.8, 0.86, nFine) * max(recess, edge * 0.5));
                            col = lerp(col, bare * 1.2, scratch * 0.5);
                            metal = 0.3 + 0.4 * polish; rough = lerp(0.58, 0.32, polish) + film * 0.1;
                        }
                        else                // housings: a faded old coat, worn through to bare steel at edges and scuffs
                        {
                            float3 faded = lerp(remnant, dot(remnant, float3(0.3, 0.59, 0.11)).xxx, 0.45) * 0.85 + 0.03;
                            float bareAmt = max(contact, scratch * 0.6);
                            col = lerp(faded * (0.95 + 0.1 * big), bare * (0.95 + 0.2 * mach), bareAmt);
                            col = lerp(col, float3(0.07, 0.02, 0.006), smoothstep(0.8, 0.86, nFine) * edge * 0.6);
                            metal = bareAmt * 0.6;
                            rough = lerp(0.66, 0.42, bareAmt);
                        }
                    }
                    else if (fam < 6.5)     // electronics: clean solder mask / plastic, worn only at the corners
                    {
                        col = lerp(col, col * 1.3 + 0.05, edge * 0.35);
                        rough = 0.45;
                        if (tool)           // tool handles: satin moulded plastic, dulled and a little grimy where the hand works
                        {
                            col *= 1 - 0.14 * cond * smoothstep(0.35, 0.8, big);
                            rough = 0.32 + 0.22 * cond * big + 0.06 * nFine;
                        }
                    }
                    else col = ToolWood(vc, uv, kind, i.sinfo, i.scale, nWS, seed, edge, cond, rough);   // tools: wooden handles

                    // drive motors: body, cooling ribs, end bells and foot read as separate parts
                    if (prof > 0.5 && prof < 2.5)
                    {
                        bool rib = kind > 1.5 || (kind > 0.5 && kind < 1.5 && i.sinfo.y < 0.05);
                        bool foot = kind < 0.5 && i.objPos.y < -0.325;
                        if (foot)
                        {
                            col = i.skinB.rgb * 0.6 * (0.94 + 0.12 * mach);
                            col = lerp(col, bare, edge * brk * cond);
                            metal = 0.3; rough = 0.55;
                        }
                        else if (rib)
                        {
                            float3 ribCol = prof < 1.5 ? float3(0.022, 0.022, 0.024) : col * 0.55;
                            col = lerp(ribCol, bare * 1.1, edge * brk * (0.15 + cond) * 0.6);
                            metal = prof < 1.5 ? 0.35 : metal; rough = prof < 1.5 ? 0.5 : rough;
                        }
                        else if (kind > 0.5 && kind < 1.5) col *= 0.82;   // the body between the ribs sits in their shadow
                    }
                }
                else if (_Slot < 1.5)
                {
                    col = vc * (0.92 + 0.16 * mach);
                    metal = 0.6; rough = 0.38;
                }
                else
                {
                    if (Near(vc, float3(0.25, 0.26, 0.28), 0.03))          // dark steel accents: the skin's end bells, bezels, flanges
                    {
                        col = i.skinB.rgb * (0.94 + 0.12 * mach);
                        float shiny = smoothstep(0.08, 0.25, dot(i.skinB.rgb, float3(0.3, 0.59, 0.11)));
                        col = lerp(col, bare, edge * brk * 0.6 * cond);
                        metal = lerp(0.25, 0.8, shiny); rough = lerp(0.5, 0.3, shiny);
                    }
                    else if (Near(vc, float3(0.82, 0.83, 0.85), 0.03))     // chrome shafts: ground and polished
                    {
                        col = float3(0.55, 0.56, 0.58) * (0.9 + 0.2 * mach);
                        metal = 0.9; rough = 0.18 + 0.1 * mach;
                    }
                    else if (Near(vc, float3(0.72, 0.74, 0.76), 0.03))     // aluminium heatsinks / caps: brushed along the fins
                    {
                        col = float3(0.5, 0.52, 0.54) * (0.88 + 0.24 * mach);
                        col *= 1 - edgeWide * 0.12;                         // fin roots a touch darker: the fins read as fins
                        metal = 0.75; rough = 0.32 + 0.1 * mach;
                    }
                    else if (Near(vc, float3(0.15, 0.55, 0.75), 0.04))     // sensor optics: dark pupil, coated blue iris, rim
                    {
                        float r = kind > 1.5 ? uv.x : 0.5;
                        float3 iris = lerp(float3(0.16, 0.7, 0.95), float3(0.006, 0.06, 0.16), smoothstep(0.25, 0.85, r));
                        col = lerp(float3(0.003, 0.01, 0.025), iris, smoothstep(0.12, 0.24, r));
                        col *= 1 - smoothstep(0.86, 0.97, r) * 0.85;
                        float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                        float fres = pow(1 - saturate(dot(normalize(nWS), V)), 4);
                        col += fres * float3(0.04, 0.1, 0.07);              // anti-reflective coating sheen at grazing angles
                        emit = iris * 0.16 * smoothstep(0.12, 0.24, r) * (1 - smoothstep(0.55, 0.9, r));
                        metal = 0; rough = 0.04;
                    }
                    else if (max(vc.r, max(vc.g, vc.b)) < 0.09)            // rubber, holes
                    {
                        rough = 0.85;
                    }
                    else if (max(vc.r, max(vc.g, vc.b)) - min(vc.r, min(vc.g, vc.b)) < 0.03 && vc.g > 0.3)   // grey accents: bearing housings, bosses: machined like the shafts
                    {
                        col = vc * vc * (0.9 + 0.2 * mach);
                        metal = 0.7; rough = 0.3 + 0.1 * mach;
                    }
                    else if (tool)          // tool accents: wood, brass, steel fittings, dipped and moulded plastic grips
                    {
                        col = vc * vc;
                        float sat = max(vc.r, max(vc.g, vc.b)) - min(vc.r, min(vc.g, vc.b));
                        if (Near(vc, float3(0.58, 0.4, 0.22), 0.02) || Near(vc, float3(0.36, 0.22, 0.12), 0.02))
                            col = ToolWood(col, uv, kind, i.sinfo, i.scale, nWS, seed, edge, cond, rough);
                        else if (Near(vc, float3(0.68, 0.52, 0.24), 0.02))   // brass: a warm tarnish, bright where it rubs
                        {
                            col = lerp(col * (0.9 + 0.2 * mach), col * float3(0.6, 0.5, 0.38), saturate(0.2 + 0.6 * cond) * (1 - contact) * big);
                            metal = 0.85; rough = 0.32 + 0.12 * cond;
                        }
                        else if (sat < 0.04)                                  // steel fittings: ferrules, screws, the dark knurled nuts
                        {
                            col *= 0.9 + 0.2 * mach;
                            col = lerp(col, col * 1.4 + 0.02, contact * 0.5);
                            metal = 0.7; rough = 0.32 + 0.1 * mach + 0.1 * cond;
                        }
                        else                                                  // plastic: satin, dulled by hands, grime in the creases
                        {
                            col *= 1 - 0.14 * cond * smoothstep(0.35, 0.8, big);
                            col = lerp(col, col * 1.2 + 0.015, edge * 0.3);
                            rough = 0.34 + 0.22 * cond * big;
                        }
                    }
                    else
                    {
                        col = vc * vc;   // the accent colours are sRGB-ish: to linear
                        float chip = smoothstep(0.5, 0.7, edge * brk * (0.5 + cond));
                        col = lerp(col, bare * 0.7, chip);
                        rough = 0.5;
                    }
                }

                // labels: type plates, stencils, warnings, PCB faces from the shared atlas
                float ls = tool ? 0 : round(i.sinfo.w);
                float t = ls < 0.5 ? 0 : ls < 1.5 ? i.skinA.y : ls < 2.5 ? i.skinA.z : i.skinA.w;
                if (abs(t) > 0.5 && kind < 0.5)
                {
                    float2 l;
                    if (t < 0 || ls > 2.5) l = uv;
                    else
                    {
                        float pw = min(i.sinfo.x * 0.7, i.sinfo.y * 0.75 * 2.0);
                        l = float2((uv.x - 0.5) * i.sinfo.x / pw + 0.5, (uv.y - 0.5) * i.sinfo.y / (pw * 0.5) + 0.5);
                    }
                    float at = abs(t);
                    float row = floor(at / 4 + 0.01), column = at - row * 4;
                    float2 raw = float2((column + l.x) / 4, 1 - (row + 1 - l.y) / 8);
                    float2 auv = float2((column + clamp(l.x, 0.01, 0.99)) / 4, 1 - (row + 1 - clamp(l.y, 0.02, 0.98)) / 8);
                    float4 dec = SAMPLE_TEXTURE2D_GRAD(_Decals, sampler_Decals, auv, ddx(raw), ddy(raw));
                    float inside = step(0, l.x) * step(l.x, 1) * step(0, l.y) * step(l.y, 1);
                    dec.a *= inside * (1 - edge * cond * 0.7) * (1 - smoothstep(0.7, 0.8, nFine) * cond * 0.6);
                    col = lerp(col, dec.rgb, dec.a);
                    metal = lerp(metal, 0.2, dec.a);
                    rough = lerp(rough, 0.45, dec.a);
                    emit *= 1 - dec.a;
                }

                // storage: dust settles on top faces of older parts, grime under them
                float up = saturate(nWS.y), down = saturate(-nWS.y);
                col = lerp(col, float3(0.15, 0.13, 0.11), up * up * saturate(cond - 0.25) * (0.15 + 0.3 * big));
                col *= 1 - down * 0.2 * cond;

                // art pass (WorkshopArtDirectionProfile): miniature-style layers over the skin, from the mesh's cavity
                // mask (concave, convex) and the face edge distance: wash -> grime -> edge wear -> highlight -> paint
                // response. Every amount 0 = the skin as it was.
                {
                    float cc = i.cav.x, cx = i.cav.y;
                    float gl = dot(col, float3(0.2126, 0.7152, 0.0722));
                    // each layer group sits behind a uniform branch: an amount of 0 in the profile skips its work
                    float wash = 0, chip = 0;
                    [branch] if (_WWArtWash + _WWArtGrime > 0)
                    {
                        // wash: the vertex mask says which edges of a face are inside creases; the face's own edge
                        // distance turns that into a sharp line along the crease (seams, panel joints, the foot of a
                        // boss, tooth roots) instead of a gradient smeared over the whole low-poly face
                        float washW = clamp(faceSize * 0.14, 0.002, 0.018);
                        float crease = (1 - smoothstep(0.0, washW, ed + (nFine - 0.5) * washW * 0.5)) * smoothstep(0.1, 0.55, cc);
                        wash = saturate(max(max(crease * _WWArtWashEdge, cc * cc * 0.4), recess * 0.8)) * step(0.0001, _WWArtWash);
                        col = lerp(col, col * _WWArtWashColor.rgb, wash * _WWArtWash);
                        // grime: oily dirt spreading out of the creases and on undersides, in patches, more on old parts
                        float patchy = smoothstep(0.3, 0.7, big * 0.65 + nFine * 0.35);
                        float grime = saturate((crease * 0.8 + cc * 0.9) * (0.4 + 0.6 * patchy) + down * 0.4 * patchy) * (0.35 + 0.65 * cond);
                        col = lerp(col, lerp(col, gl.xxx, 0.35) * _WWArtGrimeColor.rgb, grime * _WWArtGrime);
                    }
                    [branch] if (_WWArtEdgeWear > 0)
                    {
                    // edge wear: chips sized to the part (wider on big housings), heaviest at corners, on raised edges
                    // only, in stressed patches (low-frequency noise picks them), never in a crease or on bare metal
                    float wearW = clamp(faceSize * 0.1, 0.0015, 0.03) * _WWArtChipSize;
                    float ew = 1 - smoothstep(0.0, wearW, ed - (nEdge - 0.5) * wearW * 1.4);
                    float corner = 0;
                    if (kind < 0.5)
                    {
                        float ex = min(uv.x, 1 - uv.x) * i.sinfo.x * i.scale, ey = min(uv.y, 1 - uv.y) * i.sinfo.y * i.scale;
                        corner = (1 - smoothstep(0.0, wearW * 2.2, ex)) * (1 - smoothstep(0.0, wearW * 2.2, ey));
                    }
                    float stress = smoothstep(0.38, 0.62, big * 0.7 + nEdge * 0.3);
                    float raised = saturate(cx * 1.6 + 0.25) * (1 - smoothstep(0.2, 0.6, cc));
                    float worn = saturate(max(ew * (0.4 + 0.6 * stress), corner)) * raised;
                    float knock = smoothstep(0.78, 0.81, nFine * 0.7 + nEdge * 0.45) * smoothstep(0.06, 0.15, faceSize) * stress * 0.8;   // the odd knock on a broad panel
                    float wearAmt = (0.75 + 0.5 * cond) * (1 - metal) * (tool && (_Slot > 0.5 || fam > 1.5) ? 0.0 : 1.0);   // tools: only paint chips
                    float brkC = 0.3 + 0.9 * smoothstep(0.35, 0.65, nEdge * 0.6 + nFine * 0.4);   // breaks a worn edge into separate chips
                    chip = smoothstep(0.27, 0.39, max(worn, knock) * brkC * wearAmt);
                    float rub = smoothstep(0.14, 0.3, worn * brkC * wearAmt) * (1 - chip);   // paint rubbed thin around a chip
                    float3 steel = lerp(float3(0.16, 0.15, 0.14), gl.xxx * 0.55, 0.3) * (0.85 + 0.3 * nFine);   // dark steel, a hint of the paint's value
                    steel = lerp(steel, float3(0.22, 0.1, 0.045), smoothstep(0.62, 0.8, nEdge) * 0.5 * cond);   // the oldest chips show a trace of rust
                    col = lerp(col, col * 1.18 + gl * 0.04, rub * 0.6 * _WWArtEdgeWear);
                    col = lerp(col, steel, chip * _WWArtEdgeWear);
                    gArtDbg.g = worn;
                    metal = lerp(metal, 0.65, chip * _WWArtEdgeWear);
                    rough = lerp(rough, 0.45, chip * _WWArtEdgeWear);
                    }
                    gArtDbg.rb = float2(chip, wash);
                    // highlight: raised edges and tooth tips a step lighter than the paint, never white
                    float band = 1 - smoothstep(0.0, edgeW * 2.5, ed);
                    float hi = cx * band * (1 - wash) * (1 - chip);
                    col = lerp(col, saturate(col * 1.35 + gl * 0.06), hi * _WWArtEdge);
                    // paint response: rougher = broader, softer highlights; metallic scaled
                    rough = saturate(rough + _WWArtRough);
                    metal = saturate(metal * (1 + _WWArtMetal));

                    // sandbox Variant B: layered wear (paint -> exposed steel -> the odd bit of rust), steel chips with
                    // the robots' response, polished edges on bare metal; SkinSurface turns paint and metal up
                    [branch] if (_WWLabB > 0)
                    {
                        bool painted = _Slot < 0.5 && fam < 1.5;
                        bool steelish = _Slot < 0.5 && (fam < 3.5 || (fam > 4.5 && fam < 5.5));   // paint over steel, bare and used metal: not rust, plastic or wood
                        // exposed steel in the chips: the robots' dark steel (metallic 0.8, smoothness 0.4)
                        metal = lerp(metal, 0.8, chip * _WWArtEdgeWear);
                        rough = lerp(rough, 0.55, chip * _WWArtEdgeWear);
                        // rust: only on old parts, at the heart of the chips and in creases and thread roots, in patches
                        float age = saturate(cond * 1.6 - 0.45);
                        float rustM = max(chip * smoothstep(0.5, 0.72, nEdge), max(cc * 0.7, recess * 0.6) * smoothstep(0.55, 0.75, big * 0.55 + nFine * 0.45));
                        rustM = saturate(rustM * age * _WWLabRust * 1.4) * (steelish ? 1.0 : 0.0);
                        col = lerp(col, lerp(rustDark * 1.4, rustLight, smoothstep(0.3, 0.8, nFine)), rustM);
                        metal = lerp(metal, 0, rustM); rough = lerp(rough, 0.85, rustM);
                        // bare metal: raised edges and rubbed spots polished brighter and sharper
                        float hiB = saturate(cx * (1 - smoothstep(0.0, edgeW * 3.0, ed)) + polish * 0.5) * (1 - wash) * saturate(metal * 1.5 - 0.3);
                        col = lerp(col, col * 1.25 + 0.015, hiB * _WWLabEdgePolish * 0.6);
                        rough = lerp(rough, rough * 0.55, hiB * _WWLabEdgePolish);
                        gLabB = float3(painted ? 1 - saturate(metal / 0.6) : 0, big * 0.65 + nFine * 0.35 - 0.5, (1 - wash * 0.7) * (1 - cc * 0.6));
                    }
                }

                // sandbox: painted bevels. The face's edge distance as a rounded height ramp; its screen-space slope
                // bends the normal round the edge like a small chamfer, with no change to the mesh
                [branch] if (_WWLabBevel > 0)
                {
                    float bw = max(_WWLabBevelW, 1e-4);
                    float r = 1 - saturate(ed / bw);
                    float hgt = bw * (1 - r * r);
                    float3 dpx = ddx(i.positionWS), dpy = ddy(i.positionWS);
                    float3 n0 = normalize(nWS);
                    float3 r1 = cross(dpy, n0), r2 = cross(n0, dpx);
                    float det = dot(dpx, r1);
                    float3 grad = sign(det) * (ddx(hgt) * r1 + ddy(hgt) * r2);
                    nWS = normalize(abs(det) * n0 - grad * _WWLabBevel);
                }
            }

            // skin surface: material, wear and labels; lit by the shared lighting in frag
            void SkinSurface(Varyings i, out float3 nWS, out float3 col, out float metallic, out float smoothness, out float3 emit)
            {
                nWS = i.normalWS;
                float metal, rough;
                Skin(i, col, metal, rough, emit, nWS);
                float thread = i.color.a;
                float ridge = sin(i.uv.y * 6.2831853);
                col *= 1.0 - thread * 0.32 * (0.5 - 0.5 * ridge);
                nWS = normalize(normalize(nWS) + i.axisWS * ridge * 0.5 * thread);
                float gloss = _WWItemGloss > 0 ? _WWItemGloss * 4.0 : 0.5;
                metallic = metal * 0.75;
                smoothness = (1.0 - rough) * gloss;
                // sandbox Variant B: the item gloss damping lifted for metals (full at 1, as the robots), a little for
                // plastic and rubber, paint at a set satin with low-frequency variation: each material reads as itself
                [branch] if (_WWLabB > 0)
                {
                    float mr = _WWLabMetalResp * saturate(metal * 1.4);
                    metallic = metal * lerp(0.75, 1.0, mr);
                    smoothness = (1.0 - rough) * lerp(gloss, 1.0, mr + (1 - mr) * 0.35 * _WWLabMetalResp);
                    if (_WWLabPaintSmooth > 0) smoothness = lerp(smoothness, _WWLabPaintSmooth, gLabB.x);
                    smoothness = saturate(smoothness - gLabB.y * 2.0 * _WWLabRoughVar);
                }
            }

            #endif

            // original salvage look (robot parts, --skins=0): grime, rust, chipped paint, scratches, dents, dust (all scale with wear)
            void LegacySurface(Varyings i, out float3 nWS, out float3 baseCol, out float metallic, out float smoothness)
            {
                float4 c = i.custom;
                float n = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, i.objPos.xy * 0.45 + i.objPos.zz * 0.21 + c.w * 7.0).r;
                float grime = SAMPLE_TEXTURE2D(_WearNoise, sampler_WearNoise, i.objPos.zx * 0.13 + c.w * 3.0).r;
                float wear = c.z * (1.0 - _WWArtWear);
                float thread = i.color.a;
                float ridge = sin(i.uv.y * 6.2831853);
                baseCol = i.color.rgb;
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
                float3 dn = i.normalWS + (float3(fine - 0.5, 0, big - 0.5)) * 0.35 * wear;       // small dents
                nWS = normalize(normalize(dn) + i.axisWS * ridge * 0.5 * thread);
                float gloss = _WWItemGloss > 0 ? _WWItemGloss : 0.12;
                metallic = c.x * (1.0 - rustMask) * (1.0 - chip) * 0.3;
                smoothness = (1.0 - rough) * gloss;   // 75 % less reflection: no plastic sheen, even on new parts
            }

            half4 frag(Varyings i) : SV_Target
            {
                // skins and the original look are separate variants (_WW_SKIN): each only builds a surface,
                // the lighting below is shared
                float3 nWS, albedo, emit = 0;
                float metallic, smoothness;
                #if defined(_WW_SKIN)
                    SkinSurface(i, nWS, albedo, metallic, smoothness, emit);
                #else
                    LegacySurface(i, nWS, albedo, metallic, smoothness);
                #endif
                // sandbox: how strongly the surface detail bends the normal (0 as authored)
                [branch] if (_WWLabNormal != 0) nWS = normalize(lerp(normalize(i.normalWS), normalize(nWS), 1 + _WWLabNormal));

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.normalWS = nWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                if (_WWItemDebug < 0.5)
                {
                    albedo = max(0, lerp(dot(albedo, float3(0.2126, 0.7152, 0.0722)).xxx, albedo, 1.0 + _WWArtSat));
                    smoothness = saturate(smoothness * (1.0 + _WWArtGloss));
                    d.bakedGI *= 1.0 + _WWArtAmbient;
                }
                if (_WWItemDebug > 0.5)
                {
                    #if defined(_WW_SKIN)
                    if (_WWItemDebug > 5.5) return half4(gArtDbg.rgb, 1);
                    if (_WWItemDebug > 4.5) return half4(i.cav.x, i.cav.y, 0, 1);
                    #endif
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

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = metallic;
                s.smoothness = smoothness;
                s.occlusion = 1.0;
                s.emission = emit;
                s.alpha = 1.0;
                half4 col = UniversalFragmentPBR(d, s);
                float rim = pow(1.0 - saturate(dot(nWS, d.viewDirectionWS)), 3.0);
                col.rgb += rim * _WWArtRim * _WWArtRimColor.rgb * (0.3 + 0.7 * albedo) * dot(d.bakedGI, float3(0.2126, 0.7152, 0.0722));
                #if defined(_WW_SKIN)
                // sandbox Variant B: no realtime probe, so metals reflect the dim sky; the room's ambient (SH) in the
                // reflection direction instead, Fresnel-weighted, shaded in creases. Skins only: robot parts keep their look
                [branch] if (_WWLabEnvRefl > 0 && _WWItemDebug < 0.5)
                {
                    float3 R = reflect(-d.viewDirectionWS, nWS);
                    float nv = saturate(dot(nWS, d.viewDirectionWS));
                    float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
                    float3 fr = f0 + (max(smoothness.xxx, f0) - f0) * pow(1.0 - nv, 5.0);
                    col.rgb += SampleSH(R) * fr * smoothness * smoothness * gLabB.z * _WWLabEnvRefl * (1.0 + _WWArtAmbient);
                }
                #endif
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

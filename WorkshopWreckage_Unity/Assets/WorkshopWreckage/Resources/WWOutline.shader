// Hover highlight: a glowing rim of fixed screen width round the item (playtest 2026-10-25: the old
// shell, the mesh scaled up round its pivot, looked much larger than the item). Back faces pushed out
// along their normals in clip space by a few pixels: a soft glow, then a thin bright edge over it.
Shader "WorkshopWreckage/Outline"
{
    Properties { _Color ("Color", Color) = (1, 0.82, 0.35, 1) _Width ("Edge width (px)", Float) = 2 _Glow ("Glow width (px)", Float) = 5 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Width;
            float _Glow;
        CBUFFER_END
        struct A { float4 p : POSITION; float3 n : NORMAL; };
        float4 Push(A v, float px)
        {
            float4 c = TransformObjectToHClip(v.p.xyz);
            float3 nw = TransformObjectToWorldNormal(v.n);
            float2 nc = mul((float3x3)UNITY_MATRIX_VP, nw).xy;
            float len = length(nc);
            if (len > 1e-5) c.xy += nc / len * px * 2.0 / _ScreenParams.xy * c.w;
            return c;
        }
        half Pulse() { return 0.8 + 0.2 * sin(_Time.y * 6.0); }
        ENDHLSL
        // URP draws SRPDefaultUnlit before UniversalForward: the glow first, the edge over it
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Back   // item meshes are mirrored from Godot space: this keeps their real back faces
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(A v) : SV_POSITION { return Push(v, _Glow); }
            half4 frag() : SV_Target { return half4(_Color.rgb, 0.35 * Pulse()); }
            ENDHLSL
        }
        Pass
        {
            Name "Edge"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back   // item meshes are mirrored from Godot space: this keeps their real back faces
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(A v) : SV_POSITION { return Push(v, _Width); }
            half4 frag() : SV_Target { return half4(_Color.rgb * (0.9 + 0.35 * Pulse()), 1); }
            ENDHLSL
        }
    }
}

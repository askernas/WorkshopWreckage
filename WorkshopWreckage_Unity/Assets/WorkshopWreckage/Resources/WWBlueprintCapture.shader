// Blueprint posters (BlueprintPosters.cs): renders a robot model as view-space normals
// (rgb) with coverage in alpha. WWPoster.shader turns normal / silhouette edges into ink.
Shader "WorkshopWreckage/BlueprintCapture"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Capture"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 cs : SV_POSITION; float3 n : TEXCOORD0; };
            V vert(A v)
            {
                V o;
                o.cs = TransformObjectToHClip(v.pos.xyz);
                o.n = TransformWorldToViewDir(TransformObjectToWorldNormal(v.n));
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                if (n.z < 0) n = -n;   // double-sided: always face the camera
                return half4(n * 0.5 + 0.5, 1);
            }
            ENDHLSL
        }
    }
}

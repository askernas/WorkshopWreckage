// Blueprint posters (BlueprintPosters.cs): composes one aged engineering drawing from
// three WWBlueprintCapture views of a real robot model (Graphics.Blit into the poster
// texture, done once at startup). Layout in poster UV (y up): front view left, side view
// top right, top view bottom right, notes bottom left, title block bottom right.
Shader "WorkshopWreckage/Poster"
{
    Properties
    {
        _MainTex ("unused", 2D) = "black" {}
        _Front ("Front", 2D) = "black" {}
        _Side ("Side", 2D) = "black" {}
        _Top ("Top", 2D) = "black" {}
        _Title ("Title", 2D) = "white" {}
        _Paper ("Paper", Color) = (0.87, 0.83, 0.7, 1)
        _Ink ("Ink", Color) = (0.12, 0.17, 0.3, 1)
        _Seed ("Seed", Float) = 0
        _DimFront ("Front extent (x0,x1,y0,y1)", Vector) = (0.2, 0.8, 0.1, 0.9)
        _DimSide ("Side extent", Vector) = (0.2, 0.8, 0.1, 0.9)
        _DimTop ("Top extent", Vector) = (0.2, 0.8, 0.1, 0.9)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Front); SAMPLER(sampler_Front); float4 _Front_TexelSize;
            TEXTURE2D(_Side); SAMPLER(sampler_Side); float4 _Side_TexelSize;
            TEXTURE2D(_Top); SAMPLER(sampler_Top); float4 _Top_TexelSize;
            TEXTURE2D(_Title); SAMPLER(sampler_Title);
            float4 _Paper, _Ink, _DimFront, _DimSide, _DimTop;
            float _Seed;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.cs = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv; return o; }

            float H(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7)) + _Seed * 17.13) * 43758.5453); }
            float N(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(H(i), H(i + float2(1, 0)), f.x), lerp(H(i + float2(0, 1)), H(i + 1), f.x), f.y);
            }
            float Fbm(float2 p) { float s = 0, a = 0.5; for (int k = 0; k < 5; k++) { s += a * N(p); p *= 2.03; a *= 0.5; } return s; }
            float Line(float d, float w) { return 1 - smoothstep(w * 0.5, w, abs(d)); }
            float Inside(float2 uv, float4 r) { return step(r.x, uv.x) * step(uv.x, r.z) * step(r.y, uv.y) * step(uv.y, r.w); }
            float Rect(float2 uv, float4 r, float w)
            {
                float inX = step(r.x - w, uv.x) * step(uv.x, r.z + w), inY = step(r.y - w, uv.y) * step(uv.y, r.w + w);
                return saturate((Line(uv.x - r.x, w) + Line(uv.x - r.z, w)) * inY + (Line(uv.y - r.y, w) + Line(uv.y - r.w, w)) * inX);
            }

            // ink of one captured view inside rect r (x0,y0,x1,y1); dim = silhouette extent in view uv
            float View(TEXTURE2D_PARAM(t, s), float4 ts, float2 uv, float4 r, float4 dim)
            {
                float2 l = (uv - r.xy) / (r.zw - r.xy);
                float ink = 0;
                if (all(l >= 0) && all(l <= 1))
                {
                    float4 c = SAMPLE_TEXTURE2D(t, s, l);
                    float2 o = ts.xy * 1.3;
                    float e = 0;
                    float2 offs[4] = { float2(o.x, 0), float2(-o.x, 0), float2(0, o.y), float2(0, -o.y) };
                    for (int k = 0; k < 4; k++)
                    {
                        float4 q = SAMPLE_TEXTURE2D(t, s, l + offs[k]);
                        e += length(q.rgb - c.rgb) * min(q.a, c.a) * 1.3 + abs(q.a - c.a) * 2.0;
                    }
                    ink = smoothstep(0.22, 0.55, e);
                    // shading: light hatching on the faces turned away from the light (left / down)
                    float3 n = c.rgb * 2 - 1;
                    float shade = saturate(-n.x * 0.7 - n.y * 0.5 + 0.15) * c.a;
                    float hatch = step(frac((uv.x + uv.y) * 180), 0.28);
                    ink = max(ink, hatch * shade * 0.45);
                    ink = max(ink, c.a * 0.06);
                    // centre lines (dash-dot)
                    float dash = step(0.35, frac(uv.y * 40)) * (step(frac(uv.y * 40), 0.8) + step(0.9, frac(uv.y * 40)));
                    ink = max(ink, Line(l.x - (dim.x + dim.y) * 0.5, 0.0025) * dash * 0.5);
                    float dashX = step(0.35, frac(uv.x * 40)) * (step(frac(uv.x * 40), 0.8) + step(0.9, frac(uv.x * 40)));
                    ink = max(ink, Line(l.y - (dim.z + dim.w) * 0.5, 0.003) * dashX * 0.5);
                }
                // dimension line below the view: extent with end ticks and arrow heads
                float x0 = lerp(r.x, r.z, dim.x), x1 = lerp(r.x, r.z, dim.y);
                float y = r.y - 0.018;
                float onSpan = step(x0, uv.x) * step(uv.x, x1);
                ink = max(ink, Line(uv.y - y, 0.0012) * onSpan);
                float tick = (Line(uv.x - x0, 0.0012) + Line(uv.x - x1, 0.0012)) * step(abs(uv.y - y), 0.012);
                ink = max(ink, tick);
                float ah = 0.012;
                float arrow = step(abs(uv.y - y), (ah - (uv.x - x0)) * 0.35) * step(x0, uv.x) + step(abs(uv.y - y), (ah - (x1 - uv.x)) * 0.35) * step(uv.x, x1);
                ink = max(ink, saturate(arrow));
                // a little hand-written figure in the middle of the dimension line (scribble)
                float mx = (x0 + x1) * 0.5;
                float sc = step(abs(uv.x - mx), 0.03) * Line(uv.y - (y + 0.012 + 0.004 * sin(uv.x * 900)), 0.0016);
                return max(ink, sc);
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.uv;
                bool blue = _Paper.b > _Paper.r;
                // paper: fibres, stains, sun-faded edges
                float f = Fbm(uv * float2(9, 7));
                float3 col = _Paper.rgb * (0.94 + 0.1 * f);
                float stain = smoothstep(0.62, 0.8, Fbm(uv * 3.1 + 4.7));
                col *= 1 - stain * 0.12;
                float ring = Line(length(uv - float2(0.22 + 0.1 * H(float2(3, 3)), 0.17)) - 0.06, 0.004) * 0.35;   // mug ring
                col *= 1 - ring * (blue ? 0.3 : 0.5);
                float edge = min(min(uv.x, 1 - uv.x), min(uv.y, 1 - uv.y));
                col *= lerp(0.8, 1, smoothstep(0, 0.05, edge + (f - 0.5) * 0.02));

                float ink = 0;
                // grid and frame
                float2 g = abs(frac(uv * float2(32, 24)) - 0.5);
                ink += (1 - smoothstep(0.47, 0.5, max(g.x, g.y))) * (blue ? 0.12 : 0.06);
                ink = max(ink, Rect(uv, float4(0.03, 0.03, 0.97, 0.97), 0.0025));
                ink = max(ink, Rect(uv, float4(0.04, 0.04, 0.96, 0.96), 0.0012));
                // views
                ink = max(ink, View(TEXTURE2D_ARGS(_Front, sampler_Front), _Front_TexelSize, uv, float4(0.06, 0.32, 0.52, 0.92), _DimFront));
                ink = max(ink, View(TEXTURE2D_ARGS(_Side, sampler_Side), _Side_TexelSize, uv, float4(0.56, 0.64, 0.94, 0.92), _DimSide));
                ink = max(ink, View(TEXTURE2D_ARGS(_Top, sampler_Top), _Top_TexelSize, uv, float4(0.56, 0.32, 0.94, 0.58), _DimTop));
                // notes: scribbled lines of "handwriting" with a parts-list box
                float4 nr = float4(0.06, 0.06, 0.52, 0.26);
                ink = max(ink, Rect(uv, nr, 0.0012) * 0.8);
                if (Inside(uv, nr))
                {
                    float row = floor((uv.y - nr.y) / 0.028);
                    float ly = nr.y + (row + 0.5) * 0.028;
                    float len = 0.12 + 0.3 * H(float2(row, 9));
                    float word = step(0.18, N(float2(uv.x * 55, row * 3.1)));
                    ink = max(ink, Line(uv.y - ly - 0.0035 * sin(uv.x * 700 + row * 3) , 0.0016) * word * step(uv.x, nr.x + 0.02 + len) * step(nr.x + 0.02, uv.x) * 0.85);
                    ink = max(ink, Line(uv.x - (nr.x + 0.06), 0.001) * 0.5);
                }
                // title block
                float4 tr = float4(0.56, 0.06, 0.94, 0.26);
                ink = max(ink, Rect(uv, tr, 0.0025));
                ink = max(ink, Line(uv.y - 0.13, 0.0012) * step(tr.x, uv.x) * step(uv.x, tr.z));
                ink = max(ink, Line(uv.x - 0.82, 0.0012) * step(tr.y, uv.y) * step(uv.y, 0.13));
                float2 tl = (uv - float2(0.57, 0.135)) / float2(0.36, 0.12);
                if (all(tl >= 0) && all(tl <= 1))
                {
                    float t = SAMPLE_TEXTURE2D(_Title, sampler_Title, tl).r;
                    ink = max(ink, saturate(t * 1.4));
                }
                // revision stamp: a faded red circle
                float st = Line(length(uv - float2(0.88, 0.095)) - 0.028, 0.003) + Line(length(uv - float2(0.88, 0.095)) - 0.022, 0.0015);
                // faded ink
                ink *= 0.78 + 0.22 * N(uv * 40);
                col = lerp(col, _Ink.rgb, saturate(ink));
                col = lerp(col, float3(0.62, 0.15, 0.12), saturate(st) * 0.55);
                // masking tape on the corners
                float2 corners[4] = { float2(0.035, 0.035), float2(0.965, 0.035), float2(0.035, 0.965), float2(0.965, 0.965) };
                for (int k = 0; k < 4; k++)
                {
                    float2 d = uv - corners[k];
                    float a = (k == 0 || k == 3 ? 0.78 : -0.78) + (H(float2(k, 5)) - 0.5) * 0.4;
                    float2 rd = float2(cos(a) * d.x + sin(a) * d.y, -sin(a) * d.x + cos(a) * d.y);
                    float tw = 0.075 + 0.02 * H(float2(k, 7)), th = 0.022;
                    float torn = (N(float2(rd.y * 400, k)) - 0.5) * 0.008;
                    float m = step(abs(rd.x), tw + torn) * step(abs(rd.y), th);
                    float3 tape = float3(0.86, 0.8, 0.6) * (0.9 + 0.1 * N(uv * 300));
                    col = lerp(col, tape, m * 0.82);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}

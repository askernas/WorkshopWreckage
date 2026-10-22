using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WorkshopWreckage
{
    /// <summary>Robot Workshop redesign (Phase B): taped paper engineering drawings of the
    /// real robots on the wall above the Robot Workbench, plus a sprayed "ROBOT WORKSHOP"
    /// stencil. Each drawing is made once at startup from the in-game model: the fully
    /// assembled RobotModel is rendered offscreen (front, side and top, orthographic,
    /// WWBlueprintCapture) and WWPoster turns the views into ink on aged paper.</summary>
    public static class BlueprintPosters
    {
        /// <summary>type, drawing code, blueprint-blue paper?, width, height (m), offset along
        /// the wall (bench local x), height of centre, tilt (degrees).</summary>
        static readonly (string type, string code, bool blue, float w, float h, float x, float y, float tilt)[] Sheets =
        {
            ("retrobot", "RB-01", false, 0.62f, 0.47f, -1.12f, 2.42f, -1.5f),
            ("unibot", "UB-02", true, 0.5f, 0.38f, -0.5f, 2.62f, 2f),
            ("spiderbot", "SB-03", false, 0.58f, 0.44f, 0.08f, 2.36f, -0.8f),
            ("caterpillarbot", "CB-04", true, 0.66f, 0.5f, 0.72f, 2.58f, 1.2f),
            ("hoverbot", "HB-05", false, 0.56f, 0.42f, 1.3f, 2.33f, -2.2f),
        };
        const int CaptureLayer = TextAtlas.Layer;

        /// <summary>Builds every poster and the stencil under the workbench (they follow it).</summary>
        public static Transform Build(Game g)
        {
            var wb = g.workbench;
            var root = new GameObject("BlueprintPosters").transform;
            root.SetParent(wb.transform, false);
            float wallZ = -RobotWorkbench.Bench.z / 2 - 0.044f;   // Godot-local: the wall behind the back board
            for (int k = 0; k < Sheets.Length; k++)
            {
                var s = Sheets[k];
                var p = wb.P(s.type);
                if (p == null) continue;
                var tex = Draw(s.type, p.name, s.code, new HashSet<string>(p.partOrder), s.blue, k);
                var go = new GameObject("Poster_" + s.type);
                go.transform.SetParent(root, false);
                go.transform.localPosition = Batch.G(s.x, s.y, wallZ + 0.004f + k * 0.0004f);
                go.transform.localRotation = Quaternion.Euler(0, 0, s.tilt);
                go.AddComponent<MeshFilter>().sharedMesh = Sheet(s.w, s.h, 0.012f + 0.01f * (k % 3));
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = EnvMaterials.Atlas(tex);
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            // the stencil above the drawings
            var mask = TextAtlas.RenderSingle("ROBOT  WORKSHOP", 2048, 256, Color.black, Color.white, 170, true);
            var st = new GameObject("Stencil_RobotWorkshop");
            st.transform.SetParent(root, false);
            st.transform.localPosition = Batch.G(0.1f, 3.2f, wallZ + 0.003f);
            st.AddComponent<MeshFilter>().sharedMesh = Sheet(2.9f, 0.36f, 0);
            var sm = new Material(Resources.Load<Shader>("WWStencil")) { name = "Stencil" };
            sm.SetTexture("_MainTex", mask);
            sm.SetColor("_Color", new Color(0.93f, 0.88f, 0.74f));
            var smr = st.AddComponent<MeshRenderer>();
            smr.sharedMaterial = sm;
            smr.shadowCastingMode = ShadowCastingMode.Off;
            return root;
        }

        /// <summary>A paper sheet facing the room (local -Z in Godot-local = into the room after
        /// the bench's rotation): a grid with the lower corners curled away from the wall.</summary>
        static Mesh Sheet(float w, float h, float curl)
        {
            int nx = 12, ny = 9;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float u = (float)i / nx, q = (float)j / ny;
                    float cx = Mathf.Max(0, Mathf.Abs(u - 0.5f) * 2 - 0.6f) / 0.4f, cy = Mathf.Max(0, 0.35f - q) / 0.35f;
                    float lift = curl * cx * cx * cy * cy + curl * 0.15f * Mathf.Sin(u * Mathf.PI);
                    // Godot-local: +Z points out of the wall into the room (Batch.G flips it)
                    v.Add(Batch.G((u - 0.5f) * w, (q - 0.5f) * h, lift));
                    n.Add(Batch.G(0, 0, 1));
                    uv.Add(new Vector2(u, q));
                }
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    t.AddRange(new[] { a, c, b, b, c, d });
                }
            var m = new Mesh { name = "PosterSheet" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            // face the room (+Z Godot-local); fix the winding to match the normals
            var tri = m.triangles;
            var c0 = Vector3.Cross(v[tri[1]] - v[tri[0]], v[tri[2]] - v[tri[0]]);
            if (Vector3.Dot(c0, n[0]) < 0) { for (int k = 0; k < tri.Length; k += 3) (tri[k + 1], tri[k + 2]) = (tri[k + 2], tri[k + 1]); m.triangles = tri; }
            m.RecalculateBounds();
            return m;
        }

        static RobotModel NewModel(string type) => type switch
        {
            "unibot" => new GameObject("PosterModel").AddComponent<UnibotModel>(),
            "spiderbot" => new GameObject("PosterModel").AddComponent<SpiderbotModel>(),
            "caterpillarbot" => new GameObject("PosterModel").AddComponent<CaterpillarbotModel>(),
            "hoverbot" => new GameObject("PosterModel").AddComponent<HoverbotModel>(),
            _ => new GameObject("PosterModel").AddComponent<RetrobotModel>(),
        };

        /// <summary>The finished drawing of one robot (1536 × 1152 texture).</summary>
        static Texture Draw(string type, string name, string code, HashSet<string> parts, bool blue, int seed)
        {
            var model = NewModel(type);
            model.transform.position = new Vector3(-20000 - seed * 50, 10000, 0);
            model.Init();
            model.Build(parts);
            var capture = new Material(Resources.Load<Shader>("WWBlueprintCapture"));
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in renderers)
            {
                r.gameObject.layer = CaptureLayer;
                r.sharedMaterials = Enumerable.Repeat(capture, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
            }
            var c = bounds.center; var sz = bounds.size;
            var cam = new GameObject("PosterCamera").AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.cullingMask = 1 << CaptureLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            float d = sz.magnitude + 1;
            var front = Render(cam, c, c + Vector3.forward * d, Vector3.up, new Vector2(sz.x, sz.y), 640, 640, out var dimF);
            var side = Render(cam, c, c + Vector3.right * d, Vector3.up, new Vector2(sz.z, sz.y), 760, 420, out var dimS);
            var top = Render(cam, c, c + Vector3.up * d, Vector3.forward, new Vector2(sz.x, sz.z), 760, 420, out var dimT);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(model.gameObject);

            var paper = blue ? new Color(0.16f, 0.3f, 0.5f) : new Color(0.87f, 0.83f, 0.7f);
            var ink = blue ? new Color(0.88f, 0.92f, 0.95f) : new Color(0.13f, 0.18f, 0.32f);
            var title = TextAtlas.RenderSingle($"{name.ToUpper()} — GENERAL ARRANGEMENT\nDWG {code}   SCALE 1:2   SHEET 1/1", 1024, 340, Color.black, Color.white, 64, false);
            var mat = new Material(Resources.Load<Shader>("WWPoster"));
            mat.SetTexture("_Front", front); mat.SetTexture("_Side", side); mat.SetTexture("_Top", top);
            mat.SetTexture("_Title", title);
            mat.SetColor("_Paper", paper); mat.SetColor("_Ink", ink);
            mat.SetFloat("_Seed", seed * 3.7f + 1);
            mat.SetVector("_DimFront", dimF); mat.SetVector("_DimSide", dimS); mat.SetVector("_DimTop", dimT);
            var rt = new RenderTexture(1536, 1152, 0, RenderTextureFormat.ARGB32) { useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "Poster_" + type };
            Graphics.Blit(Texture2D.blackTexture, rt, mat);
            front.Release(); side.Release(); top.Release();
            if (title is RenderTexture trt) trt.Release();
            return rt;
        }

        /// <summary>One orthographic view, framed on the model's extent; dim = the extent in
        /// view UV (x0, x1, y0, y1) for the dimension line.</summary>
        static RenderTexture Render(Camera cam, Vector3 target, Vector3 eye, Vector3 up, Vector2 extent, int w, int h, out Vector4 dim)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            float aspect = (float)w / h;
            cam.aspect = aspect;
            cam.orthographicSize = Mathf.Max(extent.y, extent.x / aspect) * 0.5f * 1.18f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, up));
            cam.targetTexture = rt;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            cam.targetTexture = null;
            float vw = cam.orthographicSize * 2 * aspect, vh = cam.orthographicSize * 2;
            dim = new Vector4(0.5f - extent.x / vw * 0.5f, 0.5f + extent.x / vw * 0.5f, 0.5f - extent.y / vh * 0.5f, 0.5f + extent.y / vh * 0.5f);
            return rt;
        }
    }
}

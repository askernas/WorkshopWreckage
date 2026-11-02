using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WorkshopWreckage
{
    /// <summary>Robot Workshop redesign: taped paper engineering drawings on the workshop walls,
    /// plus a sprayed "ROBOT WORKSHOP" stencil above the Robot Workbench. Every drawing is
    /// made once per session from real in-game geometry: a fully assembled RobotModel (the six
    /// machines) or an item mesh (salvage part drawings) is rendered offscreen (front, side
    /// and top, orthographic, WWBlueprintCapture) and WWPoster turns the views into ink on
    /// aged paper. The drawings are shared textures; the sheets on the walls (rebuilt with
    /// each layout) only choose size, tilt and curl. Wall sheets hang at eye height on bare
    /// wall and above pallet zones, never above shelving or cabinets (their name boards hang in
    /// front of the wall), windows, the door, signs, the enamel signs or the tray floodlight.</summary>
    public static class BlueprintPosters
    {
        /// <summary>The six machines above the workbench: type, drawing code, blue paper?,
        /// width, height (m), bench-local x, centre height, tilt (degrees).</summary>
        static readonly (string type, string code, bool blue, float w, float h, float x, float y, float tilt)[] BenchSheets =
        {
            ("retrobot", "RB-01", false, 0.56f, 0.42f, -1.38f, 2.42f, -1.5f),
            ("unibot", "UB-02", true, 0.46f, 0.35f, -0.84f, 2.62f, 2f),
            ("spiderbot", "SB-03", false, 0.54f, 0.41f, -0.3f, 2.36f, -0.8f),
            ("caterpillarbot", "CB-04", true, 0.6f, 0.45f, 0.28f, 2.58f, 1.2f),
            ("hoverbot", "HB-05", false, 0.52f, 0.39f, 0.84f, 2.33f, -2.2f),
            ("sorting_arm", "SA-06", true, 0.5f, 0.38f, 1.38f, 2.6f, 1.6f),
        };

        /// <summary>Salvage part drawings: geometry key (or prefix), title, drawing code.</summary>
        static readonly (string key, string title, string code)[] PartSheets =
        {
            ("mach:drive_motor", "Drive Motor", "P-031"), ("mach:hydraulic_actuator", "Hydraulic Actuator", "P-044"),
            ("mach:servo_joint", "Servo Joint", "P-052"), ("mach:limb_housing", "Limb Housing", "P-067"),
            ("mach:chassis_bracket", "Chassis Bracket", "P-071"), ("mach:gearbox", "Gearbox", "P-083"),
            ("mach:sprocket_hub", "Sprocket Hub Assembly", "P-090"), ("spec:u_joint", "Universal Joint", "P-112"),
            ("gear:spur", "Spur Gear", "P-120"),
        };

        const int CaptureLayer = TextAtlas.Layer;
        static readonly Dictionary<string, Texture> _drawings = new();
        static int _seed;

        /// <summary>Builds the bench sheets, the stencil and the wall sheets for the current layout.</summary>
        public static Transform Build(Game g)
        {
            var root = new GameObject("BlueprintPosters").transform;
            root.SetParent(g.transform, false);
            BuildBench(g, root);
            EnamelSigns.Build(g.layout, root);   // first: the wall sheets keep clear of them
            BuildWalls(g, root);
            return root;
        }

        // ---------------------------------------------------------------- placement

        static void BuildBench(Game g, Transform root)
        {
            var wb = g.workbench;
            var bench = new GameObject("Bench").transform;
            bench.SetParent(root, false);
            bench.SetPositionAndRotation(wb.transform.position, wb.transform.rotation);
            float wallZ = -RobotWorkbench.Bench.z / 2 - 0.044f;   // Layout-local: the wall behind the back board
            for (int k = 0; k < BenchSheets.Length; k++)
            {
                var s = BenchSheets[k];
                var tex = RobotDrawing(g, s.type, s.code, s.blue);
                if (tex == null) continue;
                Sheet(bench, tex, Batch.G(s.x, s.y, wallZ + 0.004f + k * 0.0004f), Quaternion.Euler(0, 0, s.tilt), s.w, s.h, 0.012f + 0.01f * (k % 3), "Poster_" + s.type);
            }
            var mask = TextAtlas.RenderSingle("ROBOT  WORKSHOP", 2048, 256, Color.black, Color.white, 170, true);
            var st = new GameObject("Stencil_RobotWorkshop");
            st.transform.SetParent(bench, false);
            st.transform.localPosition = Batch.G(0.0f, 3.2f, wallZ + 0.003f);
            st.AddComponent<MeshFilter>().sharedMesh = SheetMesh(3.0f, 0.37f, 0);
            var sm = new Material(Resources.Load<Shader>("WWStencil")) { name = "Stencil" };
            sm.SetTexture("_MainTex", mask);
            sm.SetColor("_Color", new Color(0.93f, 0.88f, 0.74f));
            var smr = st.AddComponent<MeshRenderer>();
            smr.sharedMaterial = sm;
            smr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Sheets along all four walls, spaced 1.3–1.9 m, only where no storage unit's
        /// name board can overlap them (bare wall, above pallet zones).</summary>
        static void BuildWalls(Game g, Transform root)
        {
            var L = g.layout;
            var rng = new System.Random(g.world != null ? g.world.seed * 31 + 7 : 7);
            float west = L.roomMin.x, east = L.roomMax.x, north = L.roomMin.z, south = L.roomMax.z;
            var pool = new List<System.Func<Texture>>();
            for (int k = 0; k < BenchSheets.Length; k++) { var s = BenchSheets[k]; pool.Add(() => RobotDrawing(g, s.type, s.code, !s.blue)); }
            foreach (var p in PartSheets) { var pp = p; pool.Add(() => PartDrawing(g, pp.key, pp.title, pp.code, pp.code.GetHashCode() % 2 == 0)); }
            var order = Enumerable.Range(0, pool.Count).OrderBy(_ => rng.Next()).ToList();
            int next = 0;
            // walls: (start, direction along the wall, inward normal, length), layout XZ
            var walls = new (Vector2 a, Vector2 dir, Vector2 n, float len, string name)[]
            {
                (new Vector2(west, north), Vector2.right, Vector2.up, east - west, "north"),
                (new Vector2(east, north), Vector2.up, Vector2.left, south - north, "east"),
                (new Vector2(east, south), Vector2.left, Vector2.down, east - west, "south"),
                (new Vector2(west, south), Vector2.down, Vector2.right, south - north, "west"),
            };
            var wallRoot = new GameObject("Walls").transform;
            wallRoot.SetParent(root, false);
            int placed = 0;
            foreach (var wall in walls)
            {
                for (float t = 1.0f + (float)rng.NextDouble() * 0.6f; t < wall.len - 1.0f; t += 1.3f + (float)rng.NextDouble() * 0.6f)
                {
                    if (rng.NextDouble() < 0.1) continue;   // a gap now and then
                    float w = 0.75f + (float)rng.NextDouble() * 0.3f, h = w * 0.75f;
                    var c = wall.a + wall.dir * t;
                    float bottom = BottomAt(L, wall.name, c, wall.dir, w, h);
                    if (bottom < 0 || bottom + h > 4.3f) continue;
                    var tex = pool[order[next++ % order.Count]]();
                    if (tex == null) continue;
                    float y = bottom + h / 2 + (float)rng.NextDouble() * 0.1f;
                    var pos = new Vector3(c.x, y, c.y) + new Vector3(wall.n.x, 0, wall.n.y) * (0.006f + placed % 3 * 0.0005f);
                    var rot = Shared.YRot(Mathf.Atan2(wall.n.x, wall.n.y)) * Quaternion.Euler(0, 0, ((float)rng.NextDouble() - 0.5f) * 5f);
                    Sheet(wallRoot, tex, Shared.Pos(pos.x, pos.y, pos.z), rot, w, h, 0.01f + 0.015f * (float)rng.NextDouble(), $"WallPoster_{wall.name}_{placed}");
                    Debug.Log($"[posters] {wall.name} wall at ({pos.x:0.00}, {pos.y:0.00}, {pos.z:0.00}) layout, {w:0.00} m: {tex.name}");
                    placed++;
                }
            }
            Debug.Log($"[posters] {placed} wall sheets, {_drawings.Count} drawings");
        }

        /// <summary>Lowest clear sheet bottom (m) at wall point c (layout XZ) for a sheet of width
        /// w, or -1 where none may hang (window, door, sign, existing board, workbench, corner).</summary>
        static float BottomAt(WorkshopLayout L, string wall, Vector2 c, Vector2 dir, float w, float h)
        {
            float half = w / 2 + 0.25f;
            bool Near(Vector2 p, float along, float reach) => Mathf.Abs(Vector2.Dot(p - c, dir)) < along + half && DistToWall(L, wall, p) < reach;
            // the workbench wall section has its own six drawings and the stencil
            var bay = new Vector2(L.repairBayPos.x, L.repairBayPos.z);
            if (Near(bay, RobotWorkbench.Bench.x / 2 + 0.3f, 1.2f)) return -1;
            foreach (var win in L.windows)
                if (wall == "west" && Mathf.Abs((float)Json.Num(win, "z") - c.y) < (float)Json.Num(win, "width") / 2 + half) return -1;
            var tray = L.trayRect;
            if (wall == "south" && c.x > tray.xMin - half && c.x < tray.xMax + half) return -1;   // the tray's floodlight
            float bottom = 1.45f;   // bare wall: at eye level
            var mounted = new List<float>();   // lower edges of signs and clocks above this spot
            foreach (var f in L.furniture)
            {
                string type = Json.Str(f, "type");
                var p = WorkshopLayout.V(f["pos"]);
                var s = WorkshopLayout.V(f["size"]);
                var fp = new Vector2(p.x, p.z);
                bool along = Mathf.Abs(Mathf.Sin((float)Json.Num(f, "rot"))) < 0.5f;
                float ext = (along == (Mathf.Abs(dir.x) > 0.5f) ? s.x : s.z) / 2;
                if (!Near(fp, ext, 1.3f)) continue;
                switch (type)
                {
                    case "door": case "blueprint": return -1;
                    case "plant": continue;
                    case "pegboard": return -1;   // the tool pegboard fills the wall up to the sign: its sheets hang elsewhere
                    case "sign": case "clock":
                        mounted.Add(p.y - s.y / 2);   // wall-mounted: checked once the height is known
                        break;
                    default: bottom = Mathf.Max(bottom, p.y + s.y + 0.3f); break;
                }
            }
            foreach (var u in L.units.Values)
            {
                var p = WorkshopLayout.V(u["pos"]);
                bool alongX = Mathf.Abs(Mathf.Sin((float)Json.Num(u, "rot_y"))) < 0.5f;
                float ext = (alongX == (Mathf.Abs(dir.x) > 0.5f) ? (float)Json.Num(u, "width") : (float)Json.Num(u, "depth")) / 2;
                if (!Near(new Vector2(p.x, p.z), ext, 1.2f)) continue;
                // shelving and cabinets carry their name boards at the front, in front of the wall:
                // from the floor they would always overlap a sheet above them, so none hangs there
                if (Json.Str(u, "type") != "pallet_zone") return -1;
                bottom = Mathf.Max(bottom, 2.0f);   // pallet zone: parts piled on it, its sign is a low post
            }
            // the sheet may hang below a wall sign or clock, never across it
            foreach (var lower in mounted) if (bottom + h > lower - 0.1f) return -1;
            // nor across one of the big enamel signs (they hang straight; the sheets move elsewhere)
            if (EnamelSigns.Blocks(L, wall, wall == "north" || wall == "south" ? c.x : c.y, w, bottom, bottom + h)) return -1;
            return bottom;
        }

        static float DistToWall(WorkshopLayout L, string wall, Vector2 p) => wall switch
        {
            "north" => p.y - L.roomMin.z,
            "south" => L.roomMax.z - p.y,
            "west" => p.x - L.roomMin.x,
            _ => L.roomMax.x - p.x,
        };

        internal static void Sheet(Transform parent, Texture tex, Vector3 localPos, Quaternion localRot, float w, float h, float curl, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.AddComponent<MeshFilter>().sharedMesh = SheetMesh(w, h, curl);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = EnvMaterials.Atlas(tex);
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A paper sheet facing +Z layout-local (into the room): a grid with the lower
        /// corners curled away from the wall.</summary>
        static Mesh SheetMesh(float w, float h, float curl)
        {
            int nx = 12, ny = 9;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float u = (float)i / nx, q = (float)j / ny;
                    float cx = Mathf.Max(0, Mathf.Abs(u - 0.5f) * 2 - 0.6f) / 0.4f, cy = Mathf.Max(0, 0.35f - q) / 0.35f;
                    float lift = curl * cx * cx * cy * cy + curl * 0.15f * Mathf.Sin(u * Mathf.PI);
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
            var tri = m.triangles;
            var c0 = Vector3.Cross(v[tri[1]] - v[tri[0]], v[tri[2]] - v[tri[0]]);
            if (Vector3.Dot(c0, n[0]) < 0) { for (int k = 0; k < tri.Length; k += 3) (tri[k + 1], tri[k + 2]) = (tri[k + 2], tri[k + 1]); m.triangles = tri; }
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- drawings

        static RobotModel NewModel(string type) => type switch
        {
            "unibot" => new GameObject("PosterModel").AddComponent<UnibotModel>(),
            "spiderbot" => new GameObject("PosterModel").AddComponent<SpiderbotModel>(),
            "caterpillarbot" => new GameObject("PosterModel").AddComponent<CaterpillarbotModel>(),
            "hoverbot" => new GameObject("PosterModel").AddComponent<HoverbotModel>(),
            "sorting_arm" => new GameObject("PosterModel").AddComponent<SortingArmModel>(),
            _ => new GameObject("PosterModel").AddComponent<RetrobotModel>(),
        };

        /// <summary>General arrangement of one machine (cached per type and paper colour).</summary>
        internal static Texture RobotDrawing(Game g, string type, string code, bool blue)
        {
            string key = $"robot:{type}:{blue}";
            if (_drawings.TryGetValue(key, out var t)) return t;
            var p = g.workbench.P(type);
            if (p == null) return null;
            var model = NewModel(type);
            model.Init();
            model.Build(new HashSet<string>(p.partOrder));
            if (model is SortingArmModel arm) { arm.SetStanding(false); arm.Reach(model.transform.position + new Vector3(0, -1.2f, 0.9f), Vector3.down); }
            t = Capture(model.gameObject, $"{p.name.ToUpper()} — GENERAL ARRANGEMENT\nDWG {code}   SCALE 1:2   SHEET 1/1", blue);
            return _drawings[key] = t;
        }

        /// <summary>A salvage part drawing from its item mesh (key or key prefix in the catalog).</summary>
        internal static Texture PartDrawing(Game g, string keyOrPrefix, string title, string code, bool blue)
        {
            string key = $"part:{keyOrPrefix}";
            if (_drawings.TryGetValue(key, out var t)) return t;
            var def = g.db.order.Select(id => g.db.Get(id)).FirstOrDefault(d => d.geometryKey == keyOrPrefix || d.geometryKey.StartsWith(keyOrPrefix + "_") || d.geometryKey.StartsWith(keyOrPrefix + ":"))
                      ?? g.db.order.Select(id => g.db.Get(id)).FirstOrDefault(d => d.geometryKey.StartsWith(keyOrPrefix));
            if (def == null) return null;
            var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodInspect);
            var go = new GameObject("PosterPart");
            go.AddComponent<MeshFilter>().sharedMesh = e.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new Material[e.mesh.subMeshCount];
            go.transform.rotation = Quaternion.Euler(-20, 30, 0);   // a slightly turned part reads better in all three views
            t = Capture(go, $"{title.ToUpper()} — PART DRAWING\nDWG {code}   SCALE 2:1   MATERIAL: AS LISTED", blue);
            return _drawings[key] = t;
        }

        /// <summary>Renders front, side and top views of `subject` and composes the sheet
        /// (1024 × 768). The subject is destroyed.</summary>
        static Texture Capture(GameObject subject, string title, bool blue)
        {
            int seed = ++_seed;
            subject.transform.position = new Vector3(-20000 - seed * 50, 10000, 0);
            var capture = new Material(Resources.Load<Shader>("WWBlueprintCapture"));
            var renderers = subject.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(subject.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in renderers)
            {
                r.gameObject.layer = CaptureLayer;
                r.sharedMaterials = Enumerable.Repeat(capture, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
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
            cam.farClipPlane = 100;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            float d = sz.magnitude + 1;
            var front = Render(cam, c, c + Vector3.forward * d, Vector3.up, new Vector2(sz.x, sz.y), 448, 448, out var dimF);
            var side = Render(cam, c, c + Vector3.right * d, Vector3.up, new Vector2(sz.z, sz.y), 512, 288, out var dimS);
            var top = Render(cam, c, c + Vector3.up * d, Vector3.forward, new Vector2(sz.x, sz.z), 512, 288, out var dimT);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(subject);

            var paper = blue ? new Color(0.16f, 0.3f, 0.5f) : new Color(0.87f, 0.83f, 0.7f);
            var ink = blue ? new Color(0.88f, 0.92f, 0.95f) : new Color(0.13f, 0.18f, 0.32f);
            var titleTex = TextAtlas.RenderSingle(title, 1024, 340, Color.black, Color.white, 64, false);
            var mat = new Material(Resources.Load<Shader>("WWPoster"));
            mat.SetTexture("_Front", front); mat.SetTexture("_Side", side); mat.SetTexture("_Top", top);
            mat.SetTexture("_Title", titleTex);
            mat.SetColor("_Paper", paper); mat.SetColor("_Ink", ink);
            mat.SetFloat("_Seed", seed * 3.7f + 1);
            mat.SetVector("_DimFront", dimF); mat.SetVector("_DimSide", dimS); mat.SetVector("_DimTop", dimT);
            var rt = new RenderTexture(1024, 768, 0, RenderTextureFormat.ARGB32) { useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = title.Split((char)0x2014)[0].Trim() };
            Graphics.Blit(Texture2D.blackTexture, rt, mat);
            front.Release(); side.Release(); top.Release();
            if (titleTex is RenderTexture trt) trt.Release();
            Object.Destroy(mat);
            Object.Destroy(capture);
            return rt;
        }

        /// <summary>One orthographic view, framed on the subject's extent; dim = the extent in
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

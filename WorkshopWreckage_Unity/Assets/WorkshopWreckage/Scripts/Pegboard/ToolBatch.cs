using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>Many tools as a few merged meshes: each tool's own colour is baked into its vertex colours (the item
    /// shader multiplies vertex colour by _BaseColor), so every tool with the same finish (surface class, metallic,
    /// roughness, wear) shares one white-based material; accents keep the shared accent material. Used for the hung
    /// tools and for the tools lying in the room, so 80 unique tool meshes cost a handful of draw calls.
    /// The look is the item shader's skin path (WWItem.shader, `tool`): painted steel that chips at the edges,
    /// polished or plain steel, satin plastic and wooden handles, each tool in its own condition (carried per vertex
    /// in UV2.w, so the condition costs no extra material).</summary>
    public static class ToolBatch
    {
        class Src { public Vector3[] v, n; public Color[] c; public Vector2[] uv, uv1, uv3; public Vector4[] uv2; public int[][] tris; public int[] slots; }
        class Part
        {
            public readonly List<Vector3> v = new(), n = new(); public readonly List<Color> c = new();
            public readonly List<Vector2> uv = new(), uv1 = new(), uv3 = new(); public readonly List<Vector4> uv2 = new(); public readonly List<int> t = new();
        }

        static readonly Dictionary<string, Material> _own = new();
        static readonly Dictionary<int, Material> _held = new();
        static readonly Dictionary<Mesh, Src> _src = new();

        /// <summary>One tool's triangles for one material, deduplicated, tinted and skin-scaled once: a rebuild (the
        /// room tools re-merge every quarter second while they settle) only transforms positions and normals.</summary>
        class Piece { public Material mat; public Vector3[] v, n; public Color[] c; public Vector2[] uv, uv1, uv3; public Vector4[] uv2; public int[] t; }
        static readonly Dictionary<(int id, int scale), Piece[]> _pieces = new();

        // the skin's packed family + 64 * finish, +512 marks a tool for the shader; family 7 = wood (tools only)
        const int ToolFlag = 512, Wood = 7;

        /// <summary>Skin vectors of a tool: A = (packed family / finish, no labels), B = (dark steel accent, condition).</summary>
        public static (Vector4 a, Vector4 b) Skin(ItemDefinition def)
        {
            int fam = def.material switch { "painted" => ItemSkins.Painted, "electronic" => IsWood(def) ? Wood : ItemSkins.Electronic, _ => ItemSkins.Metal };
            int fin = def.finish == "polished" ? ItemSkins.FinPolished : def.material == "stainless" ? ItemSkins.FinStainless : ItemSkins.FinSteel;
            return (new Vector4(fam + 64 * fin + ToolFlag, 0, 0, 0), new Vector4(0.2f, 0.2f, 0.21f, Condition(def)));
        }

        /// <summary>Brown handles (the catalogue's wood colour) are wood, every other handle colour moulded plastic.</summary>
        static bool IsWood(ItemDefinition def)
        {
            Color.RGBToHSV(def.variantColor, out var h, out var sat, out var v);
            return h > 0.05f && h < 0.12f && sat > 0.4f && v > 0.3f && v < 0.75f;
        }

        /// <summary>Condition 0 new .. 1 worn, fixed per tool: a used workshop set, never two neighbours alike.
        /// Polished spanners stay nearly clean; painted steel and plain steel show the most use.</summary>
        public static float Condition(ItemDefinition def)
        {
            unchecked
            {
                uint h = (uint)(def.id * 7 + 11) * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                float r = (h & 0xffff) / 65535f;
                if (def.finish == "polished") return 0.05f + 0.25f * r;
                return def.material switch { "painted" => 0.2f + 0.5f * r, "steel" => 0.25f + 0.45f * r, _ => 0.1f + 0.45f * r };
            }
        }

        /// <summary>The shared material of a tool's own slot, and the linear tint that goes into its vertices
        /// (exactly what ItemAppearance.For would put in _BaseColor).</summary>
        public static Material OwnMaterial(ItemDefinition def, out Color tint)
        {
            var (albedo, c) = ItemAppearance.Params(def);
            float wear = Mathf.Clamp01(0.45f + c.z * 0.8f);
            float cls = ItemAppearance.SurfaceClass(def);
            tint = albedo.linear;
            var (sa, sb) = Skin(def);
            var key = $"{sa.x:0}|{c.x:0.00}|{c.y:0.00}";
            if (_own.TryGetValue(key, out var m)) return m;
            m = ItemAppearance.Make(new Color(1, 1, 1, cls), c.x, c.y, wear);
            m.name = "peg_tool_" + key;
            m.EnableKeyword("_WW_SKIN");
            m.SetVector("_SkinA", sa);
            m.SetVector("_SkinB", sb);
            m.SetFloat("_Seed", 0.5f);
            _own[key] = m;
            return m;
        }

        /// <summary>A tool in the hand or the inspect view: one mesh, its own colour in _BaseColor, the condition in
        /// _SkinB (the mesh's UV2.w is not marked with +10, so the shader reads it there).</summary>
        public static Material HeldMaterial(ItemDefinition def)
        {
            if (_held.TryGetValue(def.id, out var m)) return m;
            var (albedo, c) = ItemAppearance.Params(def);
            var tint = albedo.linear;
            tint.a = ItemAppearance.SurfaceClass(def);
            m = ItemAppearance.Make(tint, c.x, c.y, Mathf.Clamp01(0.45f + c.z * 0.8f));
            m.name = "peg_tool_held_" + def.id;
            var (sa, sb) = Skin(def);
            m.EnableKeyword("_WW_SKIN");
            m.SetVector("_SkinA", sa);
            m.SetVector("_SkinB", sb);
            m.SetFloat("_Seed", (def.id * 0.618f) % 1f);
            _held[def.id] = m;
            return m;
        }

        static Src Source(Mesh mesh, int[] slots)
        {
            if (_src.TryGetValue(mesh, out var s)) return s;
            s = new Src { v = mesh.vertices, n = mesh.normals, c = mesh.colors, uv = mesh.uv, slots = slots, tris = new int[mesh.subMeshCount][] };
            if (s.c.Length != s.v.Length) { s.c = new Color[s.v.Length]; for (int i = 0; i < s.c.Length; i++) s.c[i] = Color.white; }
            if (s.uv.Length != s.v.Length) s.uv = new Vector2[s.v.Length];
            var l2 = new List<Vector2>(); var l4 = new List<Vector4>();
            mesh.GetUVs(1, l2); s.uv1 = l2.Count == s.v.Length ? l2.ToArray() : new Vector2[s.v.Length];
            mesh.GetUVs(2, l4); s.uv2 = l4.Count == s.v.Length ? l4.ToArray() : new Vector4[s.v.Length];
            l2.Clear(); mesh.GetUVs(3, l2); s.uv3 = l2.Count == s.v.Length ? l2.ToArray() : new Vector2[s.v.Length];
            for (int k = 0; k < mesh.subMeshCount; k++) s.tris[k] = mesh.GetTriangles(k);
            _src[mesh] = s;
            return s;
        }

        /// <summary>Builds the merged renderers under `parent` (matrices are in the parent's space).</summary>
        public static void Build(Transform parent, IEnumerable<(ItemDefinition def, Matrix4x4 m)> tools, int layer, string name)
        {
            var parts = new Dictionary<Material, Part>();
            foreach (var (def, m) in tools)
            {
                float scale = m.GetColumn(0).magnitude;   // skin sizes are in mesh units: to the parent's
                foreach (var pc in Pieces(def, scale))
                {
                    if (!parts.TryGetValue(pc.mat, out var p)) parts[pc.mat] = p = new Part();
                    int b = p.v.Count;
                    for (int i = 0; i < pc.v.Length; i++)
                    {
                        p.v.Add(m.MultiplyPoint3x4(pc.v[i]));
                        p.n.Add(m.MultiplyVector(pc.n[i]).normalized);
                    }
                    p.c.AddRange(pc.c); p.uv.AddRange(pc.uv); p.uv1.AddRange(pc.uv1); p.uv2.AddRange(pc.uv2); p.uv3.AddRange(pc.uv3);
                    foreach (int t in pc.t) p.t.Add(b + t);
                }
            }
            foreach (var kv in parts)
            {
                var p = kv.Value;
                var mesh = new Mesh { name = name + "_" + kv.Key.name, indexFormat = p.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(p.v);
                mesh.SetNormals(p.n);
                mesh.SetColors(p.c);
                mesh.SetUVs(0, p.uv);
                mesh.SetUVs(1, p.uv1);
                mesh.SetUVs(2, p.uv2);
                mesh.SetUVs(3, p.uv3);
                mesh.SetTriangles(p.t, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var go = new GameObject(name + "_" + kv.Key.name) { layer = layer };
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kv.Key;
                mr.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        static Piece[] Pieces(ItemDefinition def, float scale)
        {
            var key = (def.id, Mathf.RoundToInt(scale * 100000));
            if (_pieces.TryGetValue(key, out var cached)) return cached;
            var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodWorld);
            var src = Source(e.mesh, e.slots);
            var own = OwnMaterial(def, out var tint);
            var slotMats = ItemAppearance.SlotMaterials(e, own);   // skinned steel and accent copies of the own material
            float cond = 10 + Condition(def);
            var byMat = new Dictionary<Material, Part>();
            for (int sm = 0; sm < src.tris.Length; sm++)
            {
                bool isOwn = src.slots[sm] == 0;
                var mat = isOwn ? own : slotMats[sm];
                if (!byMat.TryGetValue(mat, out var p)) byMat[mat] = p = new Part();
                var map = new Dictionary<int, int>();
                foreach (int i in src.tris[sm])
                {
                    if (!map.TryGetValue(i, out int j))
                    {
                        j = map[i] = p.v.Count;
                        p.v.Add(src.v[i]);
                        p.n.Add(src.n[i]);
                        var c = src.c[i];
                        p.c.Add(isOwn ? new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a) : c);
                        p.uv.Add(src.uv[i]);
                        p.uv1.Add(src.uv1[i]);
                        var s2 = src.uv2[i];
                        p.uv2.Add(new Vector4(s2.x * scale, s2.y * scale, s2.z, cond));
                        p.uv3.Add(src.uv3[i]);
                    }
                    p.t.Add(j);
                }
            }
            var list = new List<Piece>();
            foreach (var kv in byMat)
            {
                var p = kv.Value;
                list.Add(new Piece { mat = kv.Key, v = p.v.ToArray(), n = p.n.ToArray(), c = p.c.ToArray(), uv = p.uv.ToArray(), uv1 = p.uv1.ToArray(),
                    uv2 = p.uv2.ToArray(), uv3 = p.uv3.ToArray(), t = p.t.ToArray() });
            }
            return _pieces[key] = list.ToArray();
        }

        public static void Clear(Transform parent)
        {
            foreach (Transform c in parent)
            {
                if (c.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh != null) Object.Destroy(f.sharedMesh);
                Object.Destroy(c.gameObject);
            }
        }
    }
}

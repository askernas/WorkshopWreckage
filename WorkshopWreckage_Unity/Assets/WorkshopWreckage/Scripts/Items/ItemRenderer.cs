using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WorkshopWreckage
{
    /// <summary>Draws every visible item with Graphics.RenderMeshInstanced, one call per shared geometry (and
    /// submesh) per batch. Each geometry's instances are sorted by floor cell, so a batch holds the parts of one
    /// cell: batches out of view are skipped, and each batch picks its mesh and shadows by its distance to the
    /// camera (performance pass 2026-11-01). Each logical instance owns one slot; hiding an item zero-scales only
    /// that slot. Per-instance colour and PBR parameters live in one GraphicsBuffer read by the item shader at
    /// the batch's _ItemOffset + instance id; a moved part's slot data moves with it when its geometry re-sorts.</summary>
    public class ItemRenderer : MonoBehaviour
    {
        const int Chunk = 500;
        /// <summary>Floor cell of a batch (m, x and z): from 2 m for a common geometry to 12 m for a rare one, so a
        /// batch holds about TargetPerBatch parts (each batch costs a draw call per submesh and pass).</summary>
        public const float MinCell = 2f, MaxCell = 12f;
        const float TargetPerBatch = 48f;
        /// <summary>Part shadows (Settings, saved in PlayerPrefs "ww_part_shadows"): 0 = every part, 1 = by distance
        /// (default: parts from 5 cm within 3 m, 10 cm within 8 m, 20 cm beyond), 2 = none (slower PCs).</summary>
        public static int ShadowMode = UnityEngine.PlayerPrefs.GetInt("ww_part_shadows", 1);
        public static readonly string[] ShadowModeNames = { "every part", "by distance", "off" };
        /// <summary>Distance LOD: the detailed mesh (WorldLod) only near the camera, the world mesh further away.
        /// The visual sandbox turns it off to compare geometry at any distance.</summary>
        public static bool DistanceLod = true;

        class Lod
        {
            public MeshLibrary.Entry entry;
            public Material[] mats;
        }

        class Batch
        {
            public int start, count;   // slots in the group's id order
            public long cell;
            public Matrix4x4[] m;
            public MaterialPropertyBlock mpb;
            public Bounds bounds;      // tight, so URP culls it per view / shadow pass
        }

        class Group
        {
            public Lod far;            // MeshLibrary.LodWorld
            public Lod near;           // WorldLod's detailed mesh, or null
            public readonly List<int> ids = new();
            public readonly List<Batch> batches = new();
            public int offset;         // first slot in the data buffer
            public float radius, size; // largest bounding radius / extent of its instances (m)
            public float cell;         // floor cell size (m)
            public bool external;      // drawn elsewhere: the pegboard's tools (Pegboard.ScatteredTools)
            public bool dirty;         // a shown part left its batch's cell: re-sort
        }

        /// <summary>The visual sandbox's geometry quality: the mesh LOD for a geometry key at a size (largest world
        /// extent of its instances, m). Null, as in the game, draws every part at MeshLibrary.LodWorld.</summary>
        public static System.Func<string, float, int> WorldLod;

        /// <summary>The visual sandbox's wear levels: an instance's skin condition (world, instance id, definition,
        /// condition) -> condition. Null, as in the game, keeps the catalogue's condition.</summary>
        public static System.Func<ItemWorld, int, ItemDefinition, float, float> SkinCondition;

        public ItemWorld world;
        int[] _group;      // instance -> group index into _groupList
        int[] _slot;       // instance -> slot within its group's id order
        int[] _batch;      // instance -> batch index within its group
        readonly List<Group> _groupList = new();
        readonly HashSet<int> _hidden = new();
        readonly Dictionary<int, Matrix4x4> _flying = new();
        Vector4[] _itemData;   // per item id: albedo, (metallic, roughness, wear, seed), skin A, skin B
        Vector4[] _scratch;
        long[] _cellOf;        // per item id while a group sorts
        System.Comparison<int> _byCell;
        GraphicsBuffer _data;
        Material _material, _skinMaterial;
        readonly Plane[] _planes = new Plane[6];
        float _nextSort;
        public int VisibleCount { get; private set; }
        public int GroupCount => _groupList.Count;
        /// <summary>Last frame: batches drawn, of them with the detailed mesh, and skipped out of view (benchmark).</summary>
        public int DrawnBatches { get; private set; }
        public int NearBatches { get; private set; }
        public int CulledBatches { get; private set; }
        /// <summary>Last frame: batches in the view but hidden behind something (line of sight, ItemOcclusion).</summary>
        public int OccludedBatches { get; private set; }
        /// <summary>Line of sight on (default); --nooccl turns it off for comparisons.</summary>
        public static bool Occlusion = true;
        ItemOcclusion _occlusion;

        public void Build(ItemWorld w)
        {
            Release();
            world = w;
            _groupList.Clear();
            _hidden.Clear();
            _flying.Clear();
            if (_material == null)   // a rebuild (the visual sandbox's pile) keeps its materials and their slot copies
            {
                _material = new Material(ItemAppearance.Shader) { name = "ItemInstanced", enableInstancing = true };
                _material.EnableKeyword("_WW_INSTANCED");
                _material.SetTexture("_WearNoise", ItemAppearance.Noise);
                _material.SetTexture("_Decals", ItemAppearance.Decals);
                // skinned parts draw with their own variant, so neither look pays for the other's registers
                _skinMaterial = new Material(_material) { name = "ItemInstancedSkin" };
                _skinMaterial.EnableKeyword("_WW_SKIN");
            }
            _group = new int[w.Count];
            _slot = new int[w.Count];
            _batch = new int[w.Count];
            _itemData = new Vector4[w.Count * 4];
            _scratch = new Vector4[w.Count * 4];
            _cellOf = new long[w.Count];
            _byCell ??= (a, b) => _cellOf[a] != _cellOf[b] ? _cellOf[a].CompareTo(_cellOf[b]) : a.CompareTo(b);
            var members = new Dictionary<(string key, bool skin), List<int>>();
            var area = new Bounds(w.Count > 0 ? w.instances[0].Position : Vector3.zero, Vector3.zero);
            foreach (var inst in w.instances)
            {
                area.Encapsulate(inst.Position);
                var def = w.DefOf(inst.id);
                var key = (def.geometryKey, ItemSkins.Applies(def));
                if (!members.TryGetValue(key, out var l)) members[key] = l = new List<int>();
                l.Add(inst.id);
            }
            _data = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _itemData.Length, 16);
            int offset = 0;
            foreach (var kv in members)
            {
                var g = new Group { offset = offset, external = kv.Key.key.StartsWith(Pegboard.ToolFactory.Prefix) };
                g.cell = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1f, area.size.x * area.size.z) * TargetPerBatch / kv.Value.Count), MinCell, MaxCell);
                var baseMat = kv.Key.skin ? _skinMaterial : _material;
                g.far = MakeLod(MeshLibrary.Get(kv.Key.key, MeshLibrary.LodWorld), baseMat);
                var bs = g.far.entry.bounds.size;
                float ext = Mathf.Max(bs.x, Mathf.Max(bs.y, bs.z));
                foreach (var id in kv.Value)
                {
                    g.radius = Mathf.Max(g.radius, g.far.entry.bounds.extents.magnitude * w.DefOf(id).worldScale);
                    g.size = Mathf.Max(g.size, ext * w.DefOf(id).worldScale);
                }
                if (WorldLod != null && !g.external)
                {
                    int lod = WorldLod(kv.Key.key, g.size);
                    if (lod != MeshLibrary.LodWorld) g.near = MakeLod(MeshLibrary.Get(kv.Key.key, lod), baseMat);
                }
                g.ids.AddRange(kv.Value);
                foreach (var id in kv.Value)
                {
                    _group[id] = _groupList.Count;
                    var def = w.DefOf(id);
                    var (albedo, custom) = ItemAppearance.Params(def);
                    float jitter = 0.9f + 0.2f * Rand01(id);
                    var lin = albedo * jitter;   // raw values, as instance colours
                    if (ItemSkins.Applies(def)) lin = ItemSkins.Base(def, albedo) * (0.95f + 0.1f * Rand01(id));   // skins: linear colour
                    // salvage workshop: most parts are used and worn; ~15 % are new (clean, still matte)
                    bool fresh = Rand01(id * 31 + 5) < 0.15f;
                    custom.z = fresh ? custom.z * 0.15f : Mathf.Clamp01(0.45f + custom.z * 0.8f + 0.25f * Rand01(id * 7 + 3));
                    custom.w = Rand01(id * 13 + 1);
                    float cond = ItemSkins.Applies(def) ? Mathf.Clamp01(ItemSkins.Condition(def) + 0.08f * (Rand01(id * 7 + 3) - 0.5f)) : custom.z;   // skins: the catalogue's condition, a little per instance
                    if (SkinCondition != null && ItemSkins.Applies(def)) cond = SkinCondition(w, id, def, cond);
                    var (sa, sb) = ItemSkins.For(def, cond);
                    _itemData[id * 4] = new Vector4(lin.r, lin.g, lin.b, ItemAppearance.SurfaceClass(def));
                    _itemData[id * 4 + 1] = custom;
                    _itemData[id * 4 + 2] = sa;
                    _itemData[id * 4 + 3] = sb;
                }
                offset += kv.Value.Count;
                _groupList.Add(g);
                Sort(g);
            }
            foreach (var g in _groupList)
                foreach (var lod in new[] { g.far, g.near })
                    if (lod != null && lod.entry.materials == null) foreach (var m in lod.mats) m.SetBuffer("_ItemData", _data);
            Recount();
            w.StateChanged += (inst, _) => { Refresh(inst.id); Recount(); };
            w.TransformChanged += inst => Refresh(inst.id);
        }

        static Lod MakeLod(MeshLibrary.Entry e, Material baseMat) => new() { entry = e, mats = ItemAppearance.SlotMaterials(e, baseMat) };

        static long CellOf(Vector3 p, float size) =>
            ((long)(Mathf.FloorToInt(p.x / size) + 1_000_000) << 32) | (uint)(Mathf.FloorToInt(p.z / size) + 1_000_000);

        /// <summary>Sorts a group's instances by floor cell, cuts them into batches (one cell, at most Chunk parts
        /// each) and writes their slot data in that order.</summary>
        void Sort(Group g)
        {
            foreach (var id in g.ids) _cellOf[id] = CellOf(world.instances[id].Position, g.cell);
            g.ids.Sort(_byCell);
            g.batches.Clear();
            for (int k = 0; k < g.ids.Count; k++)
            {
                int id = g.ids[k];
                var last = g.batches.Count > 0 ? g.batches[^1] : null;
                if (last == null || last.cell != _cellOf[id] || last.count == Chunk)
                {
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetFloat("_ItemOffset", g.offset + k);
                    last = new Batch { start = k, cell = _cellOf[id], mpb = mpb, bounds = new Bounds(world.instances[id].Position, Vector3.one * g.radius * 2) };
                    g.batches.Add(last);
                }
                last.count++;
                last.bounds.Encapsulate(new Bounds(world.instances[id].Position, Vector3.one * g.radius * 2));
                _slot[id] = k;
                _batch[id] = g.batches.Count - 1;
                System.Array.Copy(_itemData, id * 4, _scratch, k * 4, 4);
            }
            foreach (var b in g.batches) b.m = new Matrix4x4[b.count];
            _data.SetData(_scratch, 0, g.offset * 4, g.ids.Count * 4);
            g.dirty = false;
            foreach (var id in g.ids) Refresh(id);
        }

        static float Rand01(int x)
        {
            unchecked
            {
                uint h = (uint)x * 2654435761u;
                h ^= h >> 16;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xffff) / 65535f;
            }
        }

        public bool Shows(ItemInstance inst)
        {
            if (_hidden.Contains(inst.id)) return false;
            if (inst.state == ItemState.InWorld) return true;
            return inst.state == ItemState.Stored && inst.owner.StartsWith("box:") && world.db.IsOpenKind(inst.owner.Substring(4));
        }

        public void Refresh(int id)
        {
            var inst = world.instances[id];
            var g = _groupList[_group[id]];
            var b = g.batches[_batch[id]];
            bool flying = _flying.TryGetValue(id, out var fm);
            bool show = flying || Shows(inst);
            var m = flying ? fm : inst.transform;
            b.m[_slot[id] - b.start] = show ? m : Matrix4x4.TRS(inst.Position, Quaternion.identity, Vector3.zero);
            if (show)
            {
                // a part flying or dropped elsewhere grows its batch's bounds until its group re-sorts
                b.bounds.Encapsulate(new Bounds(m.GetColumn(3), Vector3.one * g.radius * 2));
                if (!flying && CellOf(inst.Position, g.cell) != b.cell) g.dirty = true;
            }
        }

        public void SetHidden(int id, bool hidden)
        {
            if (hidden) _hidden.Add(id); else _hidden.Remove(id);
            Refresh(id);
            Recount();
        }

        public bool IsHidden(int id) => _hidden.Contains(id);

        /// <summary>Draws an instance at m whatever its state or hidden flag (a part flying between the hand and where
        /// it lands), with the same world mesh, skin and wear as when it lies there; null gives it back to its state.
        /// False: drawn elsewhere (the pegboard's tools).</summary>
        public bool Fly(int id, Matrix4x4? m)
        {
            if (_group == null || id < 0 || id >= _group.Length || _groupList[_group[id]].external) return false;
            if (m is Matrix4x4 mm) _flying[id] = mm; else _flying.Remove(id);
            Refresh(id);
            return true;
        }

        public bool IsFlying(int id) => _flying.ContainsKey(id);

        void Recount()
        {
            int n = 0;
            foreach (var i in world.instances) if (Shows(i)) n++;
            VisibleCount = n;
        }

        /// <summary>The detailed mesh where there is one (held part, aim outline, ghosts).</summary>
        public Mesh WorldMeshFor(int id) { var g = _groupList[_group[id]]; return (g.near ?? g.far).entry.mesh; }

        /// <summary>Within this distance (m, camera to the batch's bounds) a group draws its detailed mesh: 2 m for
        /// small parts, up to 6 m for the largest.</summary>
        static float NearDistance(Group g) => Mathf.Clamp(g.size * 20f, 2f, 6f);

        static bool CastsShadow(Group g, float dist) => ShadowMode switch
        {
            0 => g.radius >= 0.045f,
            1 => g.size >= (dist < 3f ? 0.05f : dist < 8f ? 0.10f : 0.20f),
            _ => false,
        };

        void Update()
        {
            if (Game.Booting) return;   // the game is still being built (loading splash)
            if (world == null) return;
            if (Time.unscaledTime >= _nextSort)
            {
                _nextSort = Time.unscaledTime + 0.5f;
                foreach (var g in _groupList) if (g.dirty && !g.external) Sort(g);
            }
            // only while the player's camera is the only one: other cameras (workbench preview, posters, QA shots)
            // see the parts from elsewhere, so then every batch is drawn as it was before, detailed and unculled
            var cam = Camera.allCamerasCount == 1 ? Camera.main : null;
            bool lod = DistanceLod && cam != null;
            var eye = cam != null ? cam.transform.position : Vector3.zero;
            if (cam != null) GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            if (Occlusion && _occlusion == null) _occlusion = ItemOcclusion.Create();
            _occlusion?.Track(Occlusion ? cam : null);
            var occ = Occlusion && cam != null ? _occlusion : null;
            float shadowReach = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? urp.shadowDistance : 25f;
            int drawn = 0, near = 0, culled = 0, occluded = 0;
            foreach (var g in _groupList)
            {
                if (g.external) continue;
                foreach (var b in g.batches)
                {
                    float dist = lod ? Mathf.Sqrt(b.bounds.SqrDistance(eye)) : 0f;
                    bool shadow = lod ? CastsShadow(g, dist) : g.radius >= (ShadowMode == 0 ? 0.045f : ShadowMode == 1 ? 0.07f : float.MaxValue);
                    bool inView = cam == null || GeometryUtility.TestPlanesAABB(_planes, b.bounds);
                    if (inView && occ != null && occ.Hidden(b.bounds, eye)) { inView = false; occluded++; }
                    // out of view or hidden: skipped, unless its shadow may still fall where it is seen (a part's
                    // shadow reaches about its own size: within a metre or two of the view's edge, and not hidden
                    // with that margin)
                    if (!inView && (!shadow || dist > shadowReach || !GeometryUtility.TestPlanesAABB(_planes, new Bounds(b.bounds.center, b.bounds.size + Vector3.one * (2f + 4f * g.size)))
                                    || (occ != null && occ.Hidden(new Bounds(b.bounds.center, b.bounds.size + Vector3.one * (0.3f + 2f * g.size)), eye))))
                    {
                        culled++;
                        continue;
                    }
                    drawn++;
                    bool detailed = g.near != null && (!lod || dist < NearDistance(g));
                    if (!detailed)
                        Draw(g.far, b, !inView ? ShadowCastingMode.ShadowsOnly : shadow ? ShadowCastingMode.On : ShadowCastingMode.Off);
                    else
                    {
                        near++;
                        // the detailed mesh in view; its shadow from the light world mesh (the shadow map can't tell)
                        if (inView) Draw(g.near, b, lod ? ShadowCastingMode.Off : shadow ? ShadowCastingMode.On : ShadowCastingMode.Off);
                        else if (!lod) Draw(g.near, b, ShadowCastingMode.ShadowsOnly);
                        if (lod && shadow) Draw(g.far, b, ShadowCastingMode.ShadowsOnly);
                    }
                }
            }
            DrawnBatches = drawn; NearBatches = near; CulledBatches = culled; OccludedBatches = occluded;
        }

        static void Draw(Lod l, Batch b, ShadowCastingMode shadows)
        {
            for (int sub = 0; sub < l.mats.Length; sub++)
            {
                var rp = new RenderParams(l.mats[sub])
                {
                    worldBounds = b.bounds,
                    shadowCastingMode = shadows,
                    receiveShadows = true,
                    matProps = b.mpb,
                };
                Graphics.RenderMeshInstanced(rp, l.entry.mesh, sub, b.m, b.count);
            }
        }

        void Release()
        {
            _data?.Release();
            _data = null;
        }

        void OnDestroy()
        {
            Release();
            _occlusion?.Dispose();
            _occlusion = null;
        }
    }
}

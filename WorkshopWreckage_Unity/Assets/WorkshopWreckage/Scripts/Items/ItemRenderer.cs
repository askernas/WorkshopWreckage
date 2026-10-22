using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>Draws every visible item with Graphics.RenderMeshInstanced, one call
    /// per shared geometry (and submesh), in chunks. Each logical instance owns one
    /// slot; hiding an item zero-scales only that slot. Per-instance colour and PBR
    /// parameters live in one GraphicsBuffer read by the item shader.</summary>
    public class ItemRenderer : MonoBehaviour
    {
        const int Chunk = 500;
        /// <summary>Parts smaller than this (bounding radius, m) don't cast shadows: their
        /// shadows are barely visible but every group is drawn again per shadow cascade.</summary>
        /// <summary>Part shadows (Settings, saved in PlayerPrefs "ww_part_shadows"): 0 = every part,
        /// 1 = parts from ~14 cm across (default: the GPU budget on Massive), 2 = none (slower PCs).</summary>
        public static int ShadowMode = UnityEngine.PlayerPrefs.GetInt("ww_part_shadows", 1);
        public static readonly string[] ShadowModeNames = { "every part", "larger parts only", "off" };
        static float ShadowMinRadius => ShadowMode == 0 ? 0.045f : ShadowMode == 1 ? 0.07f : float.MaxValue;

        class Group
        {
            public MeshLibrary.Entry entry;
            public Material[] mats;
            public List<Matrix4x4[]> chunks = new();
            public List<int> chunkCounts = new();
            public List<MaterialPropertyBlock> blocks = new();
            public List<Bounds> chunkBounds = new();   // tight per-chunk bounds so URP can cull per view / shadow pass
            public float radius;
        }

        public ItemWorld world;
        readonly Dictionary<string, Group> _groups = new();
        int[] _group;      // instance -> group index into _groupList
        int[] _slot;       // instance -> slot within group
        readonly List<Group> _groupList = new();
        readonly HashSet<int> _hidden = new();
        GraphicsBuffer _data;
        Material _material;
        public int VisibleCount { get; private set; }
        public int GroupCount => _groupList.Count;

        public void Build(ItemWorld w)
        {
            Release();
            world = w;
            _groups.Clear();
            _groupList.Clear();
            _hidden.Clear();
            _material = new Material(ItemAppearance.Shader) { name = "ItemInstanced", enableInstancing = true };
            _material.EnableKeyword("_WW_INSTANCED");
            _material.SetTexture("_WearNoise", ItemAppearance.Noise);
            _group = new int[w.Count];
            _slot = new int[w.Count];
            var members = new Dictionary<string, List<int>>();
            foreach (var inst in w.instances)
            {
                var key = w.DefOf(inst.id).geometryKey;
                if (!members.TryGetValue(key, out var l)) members[key] = l = new List<int>();
                l.Add(inst.id);
            }
            // Instances are laid out in the data buffer group by group, so each
            // chunk's offset + instanceID addresses its own entries.
            var data = new Vector4[w.Count * 2];
            int offset = 0;
            foreach (var kv in members)
            {
                var g = new Group { entry = MeshLibrary.Get(kv.Key, MeshLibrary.LodWorld) };
                g.radius = 0;
                foreach (var id in kv.Value) g.radius = Mathf.Max(g.radius, g.entry.bounds.extents.magnitude * w.DefOf(id).worldScale);
                g.mats = ItemAppearance.SlotMaterials(g.entry, _material);
                var ids = kv.Value;
                for (int c = 0; c < ids.Count; c += Chunk)
                {
                    int n = Mathf.Min(Chunk, ids.Count - c);
                    g.chunks.Add(new Matrix4x4[n]);
                    g.chunkCounts.Add(n);
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetFloat("_ItemOffset", offset + c);
                    g.blocks.Add(mpb);
                    var b = new Bounds(w.instances[ids[c]].Position, Vector3.one * g.radius * 2);
                    for (int k = c; k < c + n; k++) b.Encapsulate(new Bounds(w.instances[ids[k]].Position, Vector3.one * g.radius * 2));
                    g.chunkBounds.Add(b);
                }
                for (int k = 0; k < ids.Count; k++)
                {
                    int id = ids[k];
                    _group[id] = _groupList.Count;
                    _slot[id] = k;
                    var def = w.DefOf(id);
                    var (albedo, custom) = ItemAppearance.Params(def);
                    float jitter = 0.9f + 0.2f * Rand01(id);
                    var lin = albedo * jitter;   // raw values, as Godot's instance colours
                    // salvage workshop: most parts are used and worn; ~15 % are new (clean, still matte)
                    bool fresh = Rand01(id * 31 + 5) < 0.15f;
                    custom.z = fresh ? custom.z * 0.15f : Mathf.Clamp01(0.45f + custom.z * 0.8f + 0.25f * Rand01(id * 7 + 3));
                    custom.w = Rand01(id * 13 + 1);
                    data[(offset + k) * 2] = new Vector4(lin.r, lin.g, lin.b, ItemAppearance.SurfaceClass(def));
                    data[(offset + k) * 2 + 1] = custom;
                }
                offset += ids.Count;
                _groups[kv.Key] = g;
                _groupList.Add(g);
            }
            _data = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, 16);
            _data.SetData(data);
            _material.SetBuffer("_ItemData", _data);
            foreach (var inst in w.instances) Refresh(inst.id);
            Recount();
            w.StateChanged += (inst, _) => { Refresh(inst.id); Recount(); };
            w.TransformChanged += inst => Refresh(inst.id);
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
            int s = _slot[id];
            bool show = Shows(inst);
            g.chunks[s / Chunk][s % Chunk] = show ? inst.transform : Matrix4x4.TRS(inst.Position, Quaternion.identity, Vector3.zero);
            if (show)
            {
                // items moved into storage or dropped elsewhere grow their chunk's bounds
                var cb = g.chunkBounds[s / Chunk];
                cb.Encapsulate(new Bounds(inst.Position, Vector3.one * g.radius * 2));
                g.chunkBounds[s / Chunk] = cb;
            }
        }

        public void SetHidden(int id, bool hidden)
        {
            if (hidden) _hidden.Add(id); else _hidden.Remove(id);
            Refresh(id);
            Recount();
        }

        public bool IsHidden(int id) => _hidden.Contains(id);

        void Recount()
        {
            int n = 0;
            foreach (var i in world.instances) if (Shows(i)) n++;
            VisibleCount = n;
        }

        public Mesh WorldMeshFor(int id) => _groupList[_group[id]].entry.mesh;

        void Update()
        {
            if (world == null) return;
            foreach (var g in _groupList)
            {
                for (int c = 0; c < g.chunks.Count; c++)
                {
                    for (int sub = 0; sub < g.mats.Length; sub++)
                    {
                        var rp = new RenderParams(g.mats[sub])
                        {
                            worldBounds = g.chunkBounds[c],
                            shadowCastingMode = g.radius >= ShadowMinRadius ? ShadowCastingMode.On : ShadowCastingMode.Off,
                            receiveShadows = true,
                            matProps = g.blocks[c],
                        };
                        Graphics.RenderMeshInstanced(rp, g.entry.mesh, sub, g.chunks[c], g.chunkCounts[c]);
                    }
                }
            }
        }

        void Release()
        {
            _data?.Release();
            _data = null;
        }

        void OnDestroy() => Release();
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>Loads the shared item meshes (OBJ with a vertex-colour extension in
    /// shared/meshes) and mirrors them into Unity space; robot-themed salvage keys are
    /// built in code (SalvageMeshes).
    /// Submeshes are material slots: 0 = part material, 1 = tinned metal, 2 = accents.</summary>
    public static class MeshLibrary
    {
        public const int LodInspect = 0;
        public const int LodWorld = 1;

        public class Entry
        {
            public Mesh mesh;
            public int[] slots;      // material slot of each submesh
            public Bounds bounds;    // unit space, centred
        }

        static readonly Dictionary<string, Entry> _cache = new();

        static string FileFor(string key, int lod) => Shared.MeshPath(key.Replace(":", "_") + "__lod" + lod + ".obj.gz");

        public static bool Has(string key) => SalvageMeshes.Has(key) || Pegboard.ToolFactory.Has(key) || _cache.ContainsKey(key + "|1") || File.Exists(FileFor(key, LodWorld));

        static IEnumerable<string> Lines(string path)
        {
            using var fs = File.OpenRead(path);
            using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
            using var r = new StreamReader(gz);
            string line;
            while ((line = r.ReadLine()) != null) yield return line;
        }

        /// <summary>Builder for LODs above LodWorld. Only the visual sandbox sets it (its geometry pass); the game
        /// never asks for those LODs.</summary>
        public static System.Func<string, int, Entry> Extra;

        public static Entry Get(string key, int lod)
        {
            var k = key + "|" + lod;
            if (!_cache.TryGetValue(k, out var e))
            {
                e = lod > LodWorld && Extra != null ? Extra(key, lod)   // sandbox-only extra LODs (null in the game)
                    : SalvageMeshes.Has(key) ? SalvageMeshes.Build(key, lod)
                    : Pegboard.ToolFactory.Has(key) ? Pegboard.ToolFactory.Build(key, lod)   // pegboard hand tools
                    : Load(FileFor(key, lod), key);
                _cache[k] = e;
            }
            return e;
        }

        public static Bounds UnitBounds(string key) => Get(key, LodWorld).bounds;

        static Entry Load(string path, string name)
        {
            var pos = new List<Vector3>();
            var col = new List<Color>();
            var nrm = new List<Vector3>();
            var uv = new List<Vector2>();
            var slotTris = new SortedDictionary<int, List<int>>();
            List<int> current = null;
            var ci = CultureInfo.InvariantCulture;
            foreach (var raw in Lines(path))
            {
                if (raw.Length < 2) continue;
                var p = raw.Split(' ');
                switch (p[0])
                {
                    case "v":
                        pos.Add(new Vector3(float.Parse(p[1], ci), float.Parse(p[2], ci), -float.Parse(p[3], ci)));
                        col.Add(p.Length >= 8
                            ? new Color(float.Parse(p[4], ci), float.Parse(p[5], ci), float.Parse(p[6], ci), float.Parse(p[7], ci))
                            : Color.white);
                        break;
                    case "vn":
                        nrm.Add(new Vector3(float.Parse(p[1], ci), float.Parse(p[2], ci), -float.Parse(p[3], ci)));
                        break;
                    case "vt":
                        uv.Add(new Vector2(float.Parse(p[1], ci), float.Parse(p[2], ci)));
                        break;
                    case "usemtl":
                        int slot = int.Parse(p[1].Substring(4));
                        if (!slotTris.TryGetValue(slot, out current)) slotTris[slot] = current = new List<int>();
                        break;
                    case "f":
                        // OBJ faces are counter-clockwise in right-handed space; after the
                        // Z mirror they are clockwise, which is Unity's front face.
                        for (int k = 1; k <= 3; k++)
                        {
                            var s = p[k];
                            int slash = s.IndexOf('/');
                            current.Add(int.Parse(slash < 0 ? s : s.Substring(0, slash)) - 1);
                        }
                        break;
                }
            }
            var mesh = new Mesh { name = name };
            if (pos.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.subMeshCount = slotTris.Count;
            var slots = new int[slotTris.Count];
            int sub = 0;
            foreach (var kv in slotTris)
            {
                mesh.SetTriangles(kv.Value, sub, false);
                slots[sub++] = kv.Key;
            }
            mesh.RecalculateBounds();
            SkinUV.Apply(mesh, slots);   // skin coordinates (extra channels only)
            mesh.UploadMeshData(false);
            return new Entry { mesh = mesh, slots = slots, bounds = mesh.bounds };
        }
    }
}

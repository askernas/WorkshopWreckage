using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Locates the engine-neutral shared data and converts layout-space
    /// (right-handed, -Z forward) coordinates into Unity (left-handed, +Z forward)
    /// by mirroring Z. Meshes and transforms are mirrored the same way, so the
    /// geometry is identical to the reference data.</summary>
    public static class Shared
    {
        static string _root;

        /// <summary>StreamingAssets/ww in builds; the repository's shared/ folder in the editor.</summary>
        public static string Root
        {
            get
            {
                if (_root != null) return _root;
                var built = Path.Combine(Application.streamingAssetsPath, "ww");
                _root = Directory.Exists(built) ? built : Path.GetFullPath(Path.Combine(Application.dataPath, "../../shared"));
                return _root;
            }
        }

        public static string DataPath(string file) => Path.Combine(Root, "data", file);
        public static string GeneratedPath(string file) => Path.Combine(Root, "generated", file);
        public static string MeshPath(string file) => Path.Combine(Root, "meshes", file);

        public static Dictionary<string, object> ReadObject(string path) => Json.Obj(Json.Parse(File.ReadAllText(path)));

        public static Vector3 Pos(float x, float y, float z) => new Vector3(x, y, -z);
        public static Vector3 Pos(List<object> a) => new Vector3(Json.F(a[0]), Json.F(a[1]), -Json.F(a[2]));

        /// <summary>Layout basis columns + origin (all layout space) -> Unity matrix (S * T * S).</summary>
        public static Matrix4x4 Matrix(Vector3 bx, Vector3 by, Vector3 bz, Vector3 origin)
        {
            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(bx.x, bx.y, -bx.z, 0));
            m.SetColumn(1, new Vector4(by.x, by.y, -by.z, 0));
            m.SetColumn(2, new Vector4(-bz.x, -bz.y, bz.z, 0));
            m.SetColumn(3, new Vector4(origin.x, origin.y, -origin.z, 1));
            return m;
        }

        /// <summary>Layout yaw/pitch (degrees) -> Unity camera rotation.</summary>
        public static Quaternion View(float yawDeg, float pitchDeg)
            => Quaternion.Euler(-pitchDeg, -yawDeg, 0);

        /// <summary>Layout rotation about Y (radians) -> Unity rotation.</summary>
        public static Quaternion YRot(float rad) => Quaternion.Euler(0, -rad * Mathf.Rad2Deg, 0);
    }
}

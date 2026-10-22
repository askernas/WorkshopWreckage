using System.Linq;
using UnityEngine;

namespace WorkshopWreckage.EditorTools
{
    /// <summary>QA: triangle counts of the code-built salvage meshes (SalvageMeshes) at both
    /// LODs, against the per-part budget (docs/ROBOT_WORKSHOP_ASSET_AUDIT.md §7).
    /// CLI: Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod WorkshopWreckage.EditorTools.SalvageReport.Run</summary>
    public static class SalvageReport
    {
        public static void Run()
        {
            var keys = SalvageMeshes.Keys.Concat(new[] { "shaft:servo_shaft:a2", "shaft:servo_shaft:a10" });
            foreach (var k in keys)
            {
                int t0 = SalvageMeshes.Build(k, MeshLibrary.LodInspect).mesh.triangles.Length / 3;
                int t1 = SalvageMeshes.Build(k, MeshLibrary.LodWorld).mesh.triangles.Length / 3;
                Debug.Log($"[salvage] {k}: LOD0 {t0} tris, LOD1 {t1} tris");
            }
        }
    }
}

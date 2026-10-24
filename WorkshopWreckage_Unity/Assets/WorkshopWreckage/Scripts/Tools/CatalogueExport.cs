using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WorkshopWreckage
{
    /// <summary>Component catalogue QA (--catalogue=dir [--preset=massive] [--catres=384] [--catonly=id,id]):
    /// read-only. Every sortable definition of the loaded preset is photographed alone with its in-game mesh
    /// (inspect LOD) and materials, at its true world size, in a light-grey studio (directional lights only, so
    /// the lighting does not depend on the part's size), three-quarter view, framed to its bounds:
    /// dir/images/&lt;id&gt;.png. The measured data of every definition (robot parts included, flagged) goes to
    /// dir/catalogue_export.json: gameplay extents (world LOD x worldScale, the size every placement, grip and
    /// fit check uses), the inspect-LOD extents, physical metadata, and its destination with the game's own fit
    /// check. Nothing in the game state is changed; quits when done.</summary>
    public static class CatalogueExport
    {
        const int Layer = 30;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static IEnumerator Run(Game g, string dir)
        {
            var db = g.db;
            var imgDir = Path.Combine(dir, "images");
            Directory.CreateDirectory(imgDir);
            int res = g.args.TryGetValue("catres", out var cr) ? int.Parse(cr) : 384;
            HashSet<int> only = g.args.TryGetValue("catonly", out var co) ? co.Split(',').Select(int.Parse).ToHashSet() : null;
            WriteJson(db, Path.Combine(dir, "catalogue_export.json"));

            g.player.enabled = false;
            g.hud.SetHudVisible(false);
            var cam = g.player.cam;
            cam.cullingMask = 1 << Layer;
            foreach (var l in Object.FindObjectsByType<Light>()) l.enabled = false;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.40f, 0.40f, 0.42f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.86f, 0.86f, 0.85f);
            cam.fieldOfView = 22;
            var cd = cam.GetComponent<UniversalAdditionalCameraData>();
            if (cd != null) cd.renderPostProcessing = false;   // no bloom, grade or vignette: the colours as they are
            foreach (var (euler, col, inten) in new[]
            {
                (new Vector3(40, 30, 0), new Color(1f, 0.97f, 0.92f), 1.5f),     // key: above, from the camera's left
                (new Vector3(15, -60, 0), new Color(0.85f, 0.9f, 1f), 0.55f),    // fill from the right
                (new Vector3(25, 170, 0), new Color(1f, 0.95f, 0.88f), 0.8f),    // rim from behind
            })
            {
                var lg = new GameObject("CatalogueLight").AddComponent<Light>();
                lg.type = LightType.Directional;
                lg.color = col; lg.intensity = inten;
                lg.shadows = LightShadows.None;   // no floor; self-shadow bias would change with the part's size
                lg.cullingMask = 1 << Layer;
                lg.transform.rotation = Quaternion.Euler(euler);
            }
            yield return null;

            var origin = new Vector3(0, 40, 0);   // far above the workshop (not drawn anyway)
            var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
            int n = 0, failed = 0;
            foreach (var id in db.order)
            {
                var def = db.definitions[id];
                if (def.IsRobotPart || (only != null && !only.Contains(id))) continue;
                GameObject go = null;
                try
                {
                    var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodInspect);
                    go = new GameObject("Cat_" + id) { layer = Layer };
                    go.AddComponent<MeshFilter>().sharedMesh = e.mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = ItemAppearance.SlotMaterials(e, ItemAppearance.For(def));
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    // the rest pose a part takes on a surface in the game (long parts on their side), true size
                    go.transform.SetPositionAndRotation(origin, def.restMode == RestMode.Long ? Quaternion.Euler(0, 0, 90) : Quaternion.identity);
                    go.transform.localScale = Vector3.one * def.worldScale;
                }
                catch (System.Exception ex) { Debug.LogError($"[catalogue] {id} {def.geometryKey}: {ex.Message}"); failed++; if (go) Object.Destroy(go); continue; }

                var bb = go.GetComponent<MeshRenderer>().bounds;
                float radius = bb.extents.magnitude * 1.08f;
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                var dirCam = Quaternion.Euler(28, -35, 0) * Vector3.back;   // three-quarter front left, from above
                var camPos = bb.center + dirCam * dist;
                cam.nearClipPlane = Mathf.Max(dist * 0.05f, 1e-5f);
                cam.farClipPlane = dist * 4 + 1;
                cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(bb.center - camPos));

                yield return new WaitForEndOfFrame();
                Render(cam, tex, res);
                File.WriteAllBytes(Path.Combine(imgDir, id + ".png"), tex.EncodeToPNG());
                Object.Destroy(go);
                if (++n % 100 == 0) Debug.Log($"[catalogue] {n} rendered");
            }
            Debug.Log($"[catalogue] done: {n} images, {failed} failed -> {dir}");
            Application.Quit();
        }

        static void Render(Camera cam, Texture2D tex, int res)
        {
            const int ss = 2;   // supersampled, box-filtered down
            var rt = new RenderTexture(res * ss, res * ss, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.ResetAspect();
            cam.Render();
            cam.targetTexture = prev;
            cam.ResetAspect();
            var half = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Graphics.Blit(rt, half);
            var ldr = new RenderTexture(res, res, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(half, ldr);
            var was = RenderTexture.active;
            RenderTexture.active = ldr;
            tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
            tex.Apply();
            RenderTexture.active = was;
            rt.Release(); half.Release(); ldr.Release();
        }

        static string F(double v) => v.ToString("0.###", Inv);
        static string S(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        static void WriteJson(ItemDatabase db, string path)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"preset\": ").Append(S(db.presetId)).Append(",\n  \"units\": \"mm\",\n  \"items\": [\n");
            bool first = true;
            foreach (var id in db.order)
            {
                var d = db.definitions[id];
                var w = MeshLibrary.UnitBounds(d.geometryKey).size * d.worldScale * 1000;
                var i = MeshLibrary.Get(d.geometryKey, MeshLibrary.LodInspect).bounds.size * d.worldScale * 1000;
                db.boxes.TryGetValue(d.storageBox, out var b);
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {")
                  .Append($"\"id\": {d.id}, \"name\": {S(d.displayName)}, \"category\": {S(d.CategoryName)}, \"robot_part\": {(d.IsRobotPart ? "true" : "false")}, ")
                  .Append($"\"subcategory\": {S(d.subcategory)}, \"shape\": {S(d.shape)}, \"material\": {S(d.material)}, \"finish\": {S(d.finish)}, ")
                  .Append($"\"geometry_key\": {S(d.geometryKey)}, \"mesh_scale_mm\": {F(d.meshScaleMm)}, \"visual_scale\": {F(d.visualScale)}, \"world_scale\": {d.worldScale.ToString("R", Inv)}, ")
                  .Append($"\"rest_mode\": {S(d.restMode.ToString().ToLowerInvariant())}, ")
                  .Append($"\"size_mm\": [{F(w.x)}, {F(w.y)}, {F(w.z)}], \"size_inspect_mm\": [{F(i.x)}, {F(i.y)}, {F(i.z)}], ")
                  .Append($"\"weight_g\": {F(d.weightG)}, \"diameter_mm\": {F(d.diameterMm)}, \"length_mm\": {F(d.lengthMm)}, \"thread_size\": {S(d.threadSize)}, \"thread_pitch_mm\": {F(d.threadPitchMm)}, ")
                  .Append($"\"magnetic\": {(d.magnetic ? "true" : "false")}, \"rarity\": {d.rarity}, \"storage_category\": {S(d.storageCategory)}, \"crafting_purpose\": {S(d.craftingPurpose)}, ")
                  .Append($"\"storage_box\": {S(d.storageBox)}, \"spawn_count\": {d.spawnCount}, \"fits\": {(db.Fits(d, d.storageBox) ? "true" : "false")}, ");
                if (b != null)
                    sb.Append($"\"box\": {{\"kind\": {S(Json.Str(b, "kind"))}, \"unit\": {S(Json.Str(b, "unit"))}, \"inner_mm\": [{F(Json.Num(b, "width") * 1000)}, {F(Json.Num(b, "height") * 1000)}, {F(Json.Num(b, "depth") * 1000)}], \"capacity\": {db.BoxCapacity(d.storageBox)}, \"open\": {(db.IsOpenKind(d.storageBox) ? "true" : "false")}}}");
                else
                    sb.Append($"\"box\": {{\"kind\": \"station\", \"capacity\": {db.BoxCapacity(d.storageBox)}}}");
                sb.Append("}");
            }
            sb.Append("\n  ],\n  \"valid\": ").Append(db.IsValid ? "true" : "false").Append(", \"errors\": [")
              .Append(string.Join(", ", db.errors.Select(S))).Append("]\n}\n");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[catalogue] wrote {path}");
        }
    }
}

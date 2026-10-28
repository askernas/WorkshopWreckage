using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorkshopWreckage.Sandbox
{
    /// <summary>--sandbox: the visual development lab (docs/VISUAL_SANDBOX.md). The game's item database, meshes,
    /// materials, shaders, renderer and pile physics in the game's own room (WorkshopBuilder.BuildShell: walls,
    /// windows, floor, ceiling fixtures and lighting, no furniture), without robots or any game system: a gallery of every distinct object, a physics pile, lighting and style presets, camera presets,
    /// screenshots with their settings beside them, and a performance readout. Settings live in
    /// shared/data/visual_sandbox/; the game's own files and its look are never changed from here.</summary>
    public class VisualSandbox : MonoBehaviour
    {
        // the gallery's front left corner (near the west windows) and the pile, on open floor in the room
        public Vector3 GalleryOrigin, PileCentre = new(2.6f, 0, 1.4f);

        Game _g;
        SandboxCamera _cam;
        SandboxLighting _light;
        VisualStyleController _style;
        ObjectGallery _gallery;
        GeometryTestSet _testSet;
        SandboxItems _testItems;
        PileGenerator _pile;
        SandboxItems _galleryItems, _pileItems;
        WorkshopBuilder _room;
        Bounds _roomBox;
        SandboxItems.Entry _selected;
        string _status = "";
        int _shotW = 1920, _shotH = 1080, _abOther = 1;
        bool _panel = true, _labels = true, _secView = true, _secGallery = true, _secPile = true, _secStyle = true, _secShot = true;
        bool _pileLocked, _allSliders;
        int _geomQ;   // the geometry quality the items are drawn at
        Vector2 _scroll;
        Rect _panelRect;
        string _seedText = "1";
        static string SettingsPath => Shared.DataPath("visual_sandbox/sandbox.json");
        string OutDir => _g.args.TryGetValue("sandboxout", out var o) ? o : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SandboxOutput"));

        // stats
        ProfilerRecorder _draws, _batches, _setPass, _tris, _gcAlloc;
        float _dtSmooth = 1 / 60f, _statT;
        string _stats = "";

        public static VisualSandbox Open(Game g)
        {
            var s = new GameObject("VisualSandbox").AddComponent<VisualSandbox>();
            s.transform.SetParent(g.transform, false);
            s._g = g;
            s.Init();
            return s;
        }

        void Init()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cam = SandboxCamera.Create(transform);
            _light = new GameObject("SandboxLighting").AddComponent<SandboxLighting>();
            _light.transform.SetParent(transform, false);
            var sky = RenderSettings.skybox;
            BuildRoom();
            _galleryItems = SandboxItems.Create("Gallery", transform);
            _pileItems = SandboxItems.Create("Pile", transform);
            _gallery = new ObjectGallery(_g.db, _galleryItems, GalleryOrigin);
            _pile = new PileGenerator(_g.db, _pileItems, PileCentre);
            _style = new VisualStyleController();
            ItemRenderer.WorldLod = (key, size) => VisualStyleController.LodFor(key, size, _geomQ);
            MeshLibrary.Extra = ShapeMeshes.Build;
            var cfg = File.Exists(SettingsPath) ? Shared.ReadObject(SettingsPath) : new Dictionary<string, object>();
            LoadSettings(cfg);
            _gallery.Build();
            _testItems = SandboxItems.Create("GeometryTestSet", transform);
            _testSet = new GeometryTestSet(_g.db, _testItems, new Vector3(GalleryOrigin.x, 0, Mathf.Max(_roomBox.min.z + 0.7f, GalleryOrigin.z - 0.6f)));
            _testSet.Build();
            _light.Init(_room, sky);
            var start = Json.Obj(cfg.GetValueOrDefault("start")) ?? new Dictionary<string, object>();
            int li = Array.FindIndex(SandboxLighting.Presets, p => p.id == Json.Str(start, "lighting", "workshop"));
            _light.Set(li < 0 ? 1 : li);
            int si = Array.FindIndex(VisualStyleController.Slots, p => p.id == Json.Str(start, "style", "baseline"));
            _style.current = Mathf.Max(0, si);
            if (_g.args.TryGetValue("sbstyle", out var sst)) _style.current = Mathf.Max(0, Array.FindIndex(VisualStyleController.Slots, p => p.id == sst));
            if (_g.args.TryGetValue("sblight", out var slt)) _light.Set(Mathf.Max(0, Array.FindIndex(SandboxLighting.Presets, p => p.id == slt)));
            if (_g.args.TryGetValue("sbseed", out var sseed)) _pile.cfg.seed = int.Parse(sseed);
            ApplyStyle();
            _pile.Generate();
            _seedText = _pile.cfg.seed.ToString();
            CameraPreset(Json.Int(start, "camera", 1));
            _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            Debug.Log($"[sandbox] open in {sw.ElapsedMilliseconds} ms: {_gallery.distinct} distinct objects, pile {_pile.lastInfo}");
            if (_g.args.TryGetValue("sbauto", out var dir)) StartCoroutine(AutoRun(dir));
        }

        void OnDestroy()
        {
            ItemRenderer.WorldLod = null;
            MeshLibrary.Extra = null;
            _draws.Dispose(); _batches.Dispose(); _setPass.Dispose(); _tris.Dispose(); _gcAlloc.Dispose();
        }

        // ---------------------------------------------------------------- settings

        void LoadSettings(Dictionary<string, object> d)
        {
            var p = Json.Obj(d.GetValueOrDefault("pile")) ?? new Dictionary<string, object>();
            var c = _pile.cfg;
            c.count = Json.Int(p, "count", 40);
            c.seed = Json.Int(p, "seed", 1);
            c.radius = (float)Json.Num(p, "radius", 0.45);
            c.sizeVariation = (float)Json.Num(p, "size_variation", 0);
            c.reuseSeed = Json.Bool(p, "reuse_seed");
            c.evenMix = Json.Str(p, "mix", "game") == "even";
            c.categories.Clear();
            foreach (var n in Json.Arr(p.GetValueOrDefault("categories")))
            {
                int k = Array.IndexOf(ItemDefinition.CategoryNames, n as string);
                if (k >= 0) c.categories.Add(k);
            }
            var s = Json.Obj(d.GetValueOrDefault("screenshot")) ?? new Dictionary<string, object>();
            _shotW = Json.Int(s, "width", 1920);
            _shotH = Json.Int(s, "height", 1080);
        }

        void SaveSettings()
        {
            var d = new Dictionary<string, object>
            {
                ["_note"] = "visual sandbox settings (docs/VISUAL_SANDBOX.md); styles/ holds the style presets",
                ["pile"] = _pile.cfg.ToDict(),
                ["screenshot"] = new Dictionary<string, object> { ["width"] = _shotW, ["height"] = _shotH },
                ["start"] = new Dictionary<string, object> { ["lighting"] = SandboxLighting.Presets[_light.current].id, ["style"] = _style.Style.id, ["camera"] = 1 },
            };
            ((Dictionary<string, object>)d["pile"])["reuse_seed"] = _pile.cfg.reuseSeed;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, Json.Write(d));
            _status = "Saved " + SettingsPath;
        }

        // ---------------------------------------------------------------- environment

        /// <summary>The game's room for the preset (its default layout seed), built by the game's own builder with the
        /// player's graphics settings applied; the gallery goes in front of the west windows.</summary>
        void BuildRoom()
        {
            var layout = WorkshopLayout.Generate(_g.db, Json.Int(_g.db.config, "default_seed", 1912));
            _room = new GameObject("Room").AddComponent<WorkshopBuilder>();
            _room.transform.SetParent(transform, false);
            _room.BuildShell(layout);
            GameSettings.ApplyWorld(_room);
            var a = Shared.Pos(layout.roomMin.x, 0, layout.roomMin.z);
            var b = Shared.Pos(layout.roomMax.x, layout.roomMax.y, layout.roomMax.z);
            _roomBox = new Bounds((a + b) / 2, Vector3.zero);
            _roomBox.Encapsulate(a); _roomBox.Encapsulate(b);
            GalleryOrigin = new Vector3(_roomBox.min.x + 1.5f, 0, -2.8f);
        }

        // ---------------------------------------------------------------- camera

        static readonly string[] CamNames = { "", "gallery overview", "gallery close-up", "pile overview", "pile close-up", "low-angle pile", "geometry test set", "test gear close-up" };

        /// <summary>The nearest eye along `dir` from the box's centre that still sees all of the box (8% margin).</summary>
        Vector3 FitEye(Bounds b, Vector3 dir)
        {
            dir.Normalize();
            var rot = Quaternion.LookRotation(-dir);
            float tv = Mathf.Tan(_cam.cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f, th = tv * _cam.cam.aspect;
            float d = 0.5f;
            for (int c = 0; c < 8; c++)
            {
                var p = Quaternion.Inverse(rot) * (b.center + Vector3.Scale(b.extents, new Vector3((c & 1) * 2 - 1, (c >> 1 & 1) * 2 - 1, (c >> 2 & 1) * 2 - 1)) - b.center);
                d = Mathf.Max(d, p.z + Mathf.Abs(p.x) / th, p.z + Mathf.Abs(p.y) / tv);
            }
            // kept inside the room (under the ceiling beams, off the walls): a big page may then not fit whole
            var eye = b.center + dir * d;
            return new Vector3(Mathf.Clamp(eye.x, _roomBox.min.x + 0.4f, _roomBox.max.x - 0.4f), Mathf.Min(eye.y, _roomBox.max.y - 0.6f), Mathf.Clamp(eye.z, _roomBox.min.z + 0.4f, _roomBox.max.z - 0.4f));
        }

        void CameraPreset(int k)
        {
            var gb = _gallery.bounds;
            var o = GalleryOrigin;
            var pc = PileCentre;
            switch (k)
            {
                case 2:
                    if (_selected != null) { _cam.Focus(_selected.centre, Mathf.Max(0.12f, _selected.radius * 3.2f), CamNames[2]); return; }
                    _cam.Place(new Vector3(o.x + 0.9f, 0.42f, o.z - 0.5f), new Vector3(o.x + 1.1f, 0.02f, o.z + 0.3f), CamNames[2]); break;
                case 3: _cam.Place(pc + new Vector3(0, 0.95f, -1.25f), pc + Vector3.up * 0.1f, CamNames[3]); break;
                case 4: _cam.Place(pc + new Vector3(0.25f, 0.5f, -0.8f), pc + Vector3.up * 0.1f, CamNames[4]); break;
                case 5: _cam.Place(pc + new Vector3(-0.55f, 0.1f, -1.05f), pc + Vector3.up * 0.16f, CamNames[5]); break;
                case 6: { var tb = _testSet.bounds; _cam.Place(FitEye(tb, new Vector3(0, 0.5f, 0.85f)), tb.center, CamNames[6]); break; }
                case 7: { var fb = _testSet.first; _cam.Place(fb.center + new Vector3(-0.35f, 0.75f, 0.9f) * Mathf.Max(0.15f, fb.extents.magnitude * 2.2f), fb.center, CamNames[7]); break; }
                default:
                    _cam.Place(FitEye(gb, new Vector3(0, 0.62f, -0.78f)), gb.center, CamNames[1]); break;
            }
        }

        // ---------------------------------------------------------------- frame

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var mp = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            _cam.blockMouse = _panel && _panelRect.Contains(new Vector2(mp.x, Screen.height - mp.y));
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame) _panel = !_panel;
                if (kb.digit1Key.wasPressedThisFrame) SelectStyle(0);   // BASELINE
                if (kb.digit2Key.wasPressedThisFrame) SelectStyle(VisualStyleController.VariantB);
                if (kb.digit3Key.wasPressedThisFrame) SelectStyle(2);
                if (kb.digit4Key.wasPressedThisFrame) SelectStyle(3);
                if (kb.digit5Key.wasPressedThisFrame) SelectStyle(4);
                if (kb.f1Key.wasPressedThisFrame) CameraPreset(1);
                if (kb.f2Key.wasPressedThisFrame) CameraPreset(2);
                if (kb.f3Key.wasPressedThisFrame) CameraPreset(3);
                if (kb.f4Key.wasPressedThisFrame) CameraPreset(4);
                if (kb.f5Key.wasPressedThisFrame) CameraPreset(5);
                if (kb.f6Key.wasPressedThisFrame) CameraPreset(6);
                if (kb.f7Key.wasPressedThisFrame) CameraPreset(7);
                if (kb.fKey.wasPressedThisFrame && _selected != null) _cam.Focus(_selected.centre, Mathf.Max(0.12f, _selected.radius * 3.2f), "focus " + _selected.def.displayName);
                if (kb.gKey.wasPressedThisFrame) FocusGallery();
                if (kb.pKey.wasPressedThisFrame) FocusPile();
                if (kb.rKey.wasPressedThisFrame && !_pileLocked) _pile.Regenerate();
                if (kb.tKey.wasPressedThisFrame) ToggleAB();
                if (kb.lKey.wasPressedThisFrame) _labels = !_labels;
                if (kb.f12Key.wasPressedThisFrame) Shot();
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !_cam.blockMouse)
                _selected = _pileItems.Pick(_cam.cam, mp) is { } pe && (_galleryItems.Pick(_cam.cam, mp) is not { } ge || Vector3.Distance(_cam.transform.position, pe.centre) < Vector3.Distance(_cam.transform.position, ge.centre)) ? pe : _galleryItems.Pick(_cam.cam, mp);
            Stats();
        }

        /// <summary>The current style on the shader globals and lights; a change of geometry quality redraws the gallery
        /// and the pile with the same items in the same places (nothing is regenerated).</summary>
        void ApplyStyle()
        {
            _style.Apply(_light);
            if (_style.GeometryQuality == _geomQ) return;
            _geomQ = _style.GeometryQuality;
            _galleryItems.Redraw();
            _pileItems.Redraw();
            _testItems.Redraw();
        }

        void SelectStyle(int k)
        {
            if (k < 0 || k >= _style.presets.Count) return;
            _style.current = k;
            ApplyStyle();
            _status = "Style: " + _style.Style.name;
        }

        void FocusGallery() { var gb = _gallery.bounds; _cam.Focus(gb.center, Mathf.Max(gb.size.x, gb.size.z) * 0.9f + 0.5f, "focus gallery"); }
        void FocusPile() => _cam.Focus(PileCentre + Vector3.up * 0.12f, _pile.cfg.radius * 3.5f + 0.4f, "focus pile");

        void Stats()
        {
            _dtSmooth = Mathf.Lerp(_dtSmooth, Time.unscaledDeltaTime, 0.05f);
            _statT += Time.unscaledDeltaTime;
            if (_statT < 0.25f) return;
            _statT = 0;
            string R(ProfilerRecorder r) => r.Valid ? r.LastValue.ToString("N0", CultureInfo.InvariantCulture) : "n/a";
            _stats = $"{1 / _dtSmooth:0} fps   {_dtSmooth * 1000:0.0} ms\n"
                + $"draw calls {R(_draws)}   batches {R(_batches)}   set-pass {R(_setPass)}\n"
                + $"triangles {R(_tris)} (items {(_galleryItems.Triangles() + _pileItems.Triangles()):N0})\n"
                + $"objects: gallery {_galleryItems.entries.Count}, pile {_pileItems.entries.Count}   GC/frame {(_gcAlloc.Valid ? _gcAlloc.LastValue / 1024f : 0):0.0} KB";
        }

        void ToggleAB()
        {
            int other = _style.current == 0 ? _abOther : 0;
            _style.current = other;
            ApplyStyle();
            _status = "Style: " + _style.Style.name + " (T toggles A/B)";
        }

        // ---------------------------------------------------------------- screenshots

        Dictionary<string, object> Metadata(string file)
        {
            return new Dictionary<string, object>
            {
                ["file"] = file,
                ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["resolution"] = new List<object> { _shotW, _shotH },
                ["preset"] = _g.presetId,
                ["camera"] = _cam.ToDict(),
                ["lighting"] = SandboxLighting.Presets[_light.current].id,
                ["style"] = _style.Style.ToDict(),
                ["environment"] = "workshop room (no furniture)",
                ["pile"] = _pile.ToDict(true),
                ["gallery_page"] = _gallery.PageName,
            };
        }

        string Stem() => $"{DateTime.Now:yyyyMMdd_HHmmss}_{SandboxLighting.Presets[_light.current].id}_{_style.Style.id}_{_cam.presetName.Replace(' ', '-')}";

        /// <summary>The camera rendered off-screen at the screenshot resolution (no panel), PNG and JSON side by side.</summary>
        public string Shot(string stem = null)
        {
            Directory.CreateDirectory(OutDir);
            stem ??= Stem();
            var path = Path.Combine(OutDir, stem + ".png");
            var tex = Render(_shotW, _shotH);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            File.WriteAllText(Path.ChangeExtension(path, ".json"), Json.Write(Metadata(Path.GetFileName(path))));
            _status = "Saved " + path;
            Debug.Log("[sandbox] screenshot " + path);
            return path;
        }

        /// <summary>A/B: the current style and the other one, same frame, side by side in one image. Each style gets
        /// two frames before its capture: the items are drawn from Update, so a geometry-quality switch only reaches
        /// the screen (and an off-screen render) on the next frame.</summary>
        public IEnumerator ShotAB(string stem = null, Action<string> done = null)
        {
            Directory.CreateDirectory(OutDir);
            int a = _style.current, b = a == 0 ? _abOther : 0;
            stem ??= $"{DateTime.Now:yyyyMMdd_HHmmss}_AB_{_style.presets[a].id}_vs_{_style.presets[b].id}_{SandboxLighting.Presets[_light.current].id}";
            yield return null; yield return null;
            var ta = Render(_shotW, _shotH);
            var meta = Metadata(stem + ".png");
            _style.current = b; ApplyStyle();
            yield return null; yield return null;
            var tb = Render(_shotW, _shotH);
            meta["style_right"] = _style.Style.ToDict();
            _style.current = a; ApplyStyle();
            var both = new Texture2D(_shotW * 2 + 8, _shotH, TextureFormat.RGB24, false);
            var fill = new Color32[both.width * both.height];
            Array.Fill(fill, new Color32(20, 20, 20, 255));
            both.SetPixels32(fill);
            both.SetPixels32(0, 0, _shotW, _shotH, ta.GetPixels32());
            both.SetPixels32(_shotW + 8, 0, _shotW, _shotH, tb.GetPixels32());
            both.Apply();
            var path = Path.Combine(OutDir, stem + ".png");
            File.WriteAllBytes(path, both.EncodeToPNG());
            meta["layout"] = "left = style, right = style_right";
            File.WriteAllText(Path.ChangeExtension(path, ".json"), Json.Write(meta));
            Destroy(ta); Destroy(tb); Destroy(both);
            _status = "Saved " + path;
            done?.Invoke(path);
        }

        /// <summary>As Game.CaptureStill: a half-float target (HDR, bloom), MSAA, then to sRGB bytes.</summary>
        Texture2D Render(int w, int h)
        {
            var cam = _cam.cam;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 8 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.ResetAspect();
            cam.Render();
            cam.targetTexture = prev;
            cam.ResetAspect();
            var ldr = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(rt, ldr);
            var was = RenderTexture.active;
            RenderTexture.active = ldr;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = was;
            rt.Release(); ldr.Release();
            Destroy(rt); Destroy(ldr);
            return tex;
        }

        // ---------------------------------------------------------------- QA

        /// <summary>--sbauto=dir: every lighting preset from every camera preset, an A/B pair, and the pile's
        /// regenerate / reset round trip (reset must give back the first pile exactly); a report, then quit.</summary>
        IEnumerator AutoRun(string dir)
        {
            _g.args["sandboxout"] = dir;
            _panel = false;
            var log = new List<string>();
            for (int i = 0; i < 30; i++) yield return null;
            var first = new List<(int, Matrix4x4)>(_pile.settled);
            int firstSeed = _pile.cfg.seed;
            var shots = _g.args.TryGetValue("sbshots", out var ss) ? ss.Split(',').Select(int.Parse).ToArray() : new[] { 1, 2, 3, 4, 5 };
            var lights = _g.args.TryGetValue("sblights", out var sl) ? sl.Split(',') : SandboxLighting.Presets.Select(p => p.id).ToArray();
            foreach (var l in lights)
            {
                _light.Set(Array.FindIndex(SandboxLighting.Presets, p => p.id == l));
                ApplyStyle();
                foreach (var k in shots)
                {
                    CameraPreset(k);
                    for (int i = 0; i < 4; i++) yield return null;
                    log.Add("shot " + Shot($"{l}_{k}_{CamNames[k].Replace(' ', '-')}"));
                }
            }
            if (_g.args.ContainsKey("sbab"))
            {
                _abOther = VisualStyleController.VariantB;
                foreach (var k in shots)
                {
                    CameraPreset(k);
                    for (int i = 0; i < 4; i++) yield return null;
                    yield return ShotAB($"ab_{k}_{CamNames[k].Replace(' ', '-')}", p => log.Add("ab " + p));
                }
                // switching styles (and geometry quality) must leave every item where it was
                var before = _pileItems.world.instances.Select(x => x.transform).Concat(_galleryItems.world.instances.Select(x => x.transform)).ToList();
                int s0 = _style.current;
                SelectStyle(VisualStyleController.VariantB); yield return null;
                long trisB = _galleryItems.Triangles() + _pileItems.Triangles();
                SelectStyle(0); yield return null;
                long trisA = _galleryItems.Triangles() + _pileItems.Triangles();
                SelectStyle(s0);
                var after = _pileItems.world.instances.Select(x => x.transform).Concat(_galleryItems.world.instances.Select(x => x.transform)).ToList();
                log.Add($"style switch BASELINE <-> VARIANT B: {before.Count} item transforms unchanged: {before.SequenceEqual(after)}; item triangles baseline {trisA:N0}, variant B {trisB:N0}");
            }
            var t0 = Time.realtimeSinceStartup;
            _pile.Regenerate();
            log.Add($"regenerate: seed {firstSeed} -> {_pile.cfg.seed}, {_pile.settled.Count} parts, {(Time.realtimeSinceStartup - t0) * 1000:0} ms; {_pile.lastInfo}");
            yield return null;
            _pile.Reset();
            bool same = _pile.cfg.seed == firstSeed && _pile.settled.Count == first.Count && _pile.settled.Zip(first, (a, b) => a.defId == b.Item1 && a.m == b.Item2).All(x => x);
            log.Add($"reset: seed {_pile.cfg.seed}, identical to the first pile: {same}");
            // the same seed again through the physics: does it repeat on its own?
            _pile.Generate();
            float maxD = _pile.settled.Count == first.Count ? _pile.settled.Zip(first, (a, b) => Vector3.Distance(a.m.GetColumn(3), b.Item2.GetColumn(3))).Max() : -1;
            log.Add($"same seed re-simulated: largest position difference {maxD * 1000:0.000} mm");
            Stats();
            log.Add("stats " + _stats.Replace("\n", " | "));
            log.Add("room " + _roomBox.min.ToString("0.00") + " .. " + _roomBox.max.ToString("0.00") + "; test set at " + _testSet.bounds.center.ToString("0.00"));
            log.AddRange(_testSet.log.Select(l => "test set " + l));
            foreach (var st in new[] { 0, VisualStyleController.VariantB })
            {
                SelectStyle(st);
                log.Add($"test set meshes ({_style.Style.name}, quality {_geomQ}): " + string.Join(", ", _testItems.entries.Select(e => { var m = _testItems.itemRenderer.WorldMeshFor(e.id); return $"{m.name} {m.vertexCount}v"; })));
            }
            SelectStyle(0);
            log.AddRange(GeometryTestSet.Audit(_g.db));
            if (_g.args.TryGetValue("sbdense", out var dn)) yield return Dense(int.Parse(dn), log);
            File.WriteAllLines(Path.Combine(dir, "sandbox_auto.txt"), log);
            Debug.Log("[sandbox] auto run done:\n" + string.Join("\n", log));
            yield return null;
            Application.Quit();
        }

        /// <summary>--sbdense=N: a pile of N parts, then the mean frame time over 180 frames (no captures, vsync off)
        /// in BASELINE and in VARIANT B from the pile overview.</summary>
        IEnumerator Dense(int n, List<string> log)
        {
            int vs = QualitySettings.vSyncCount, fr = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            int count = _pile.cfg.count; float radius = _pile.cfg.radius;
            _pile.cfg.count = n; _pile.cfg.radius = Mathf.Max(radius, 0.35f + 0.05f * Mathf.Sqrt(n));
            _pile.Regenerate();
            CameraPreset(3);
            foreach (var k in new[] { 0, VisualStyleController.VariantB, 0, VisualStyleController.VariantB })
            {
                SelectStyle(k);
                for (int i = 0; i < 30; i++) yield return null;
                float t0 = Time.realtimeSinceStartup;
                for (int i = 0; i < 180; i++) yield return null;
                float ms = (Time.realtimeSinceStartup - t0) / 180 * 1000;
                Stats();
                log.Add($"dense pile {_pile.settled.Count} parts, {_style.Style.name}: {ms:0.00} ms/frame; {_stats.Replace("\n", " | ")}");
            }
            Shot($"dense_{n}_variant_b");
            SelectStyle(0);
            yield return null; yield return null;
            Shot($"dense_{n}_baseline");
            _pile.cfg.count = count; _pile.cfg.radius = radius;
            QualitySettings.vSyncCount = vs; Application.targetFrameRate = fr;
        }

        // ---------------------------------------------------------------- UI

        GUIStyle _label, _small, _head, _box;

        void OnGUI()
        {
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true, alignment = TextAnchor.MiddleCenter };
            _head ??= new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
            _box ??= new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.UpperLeft, richText = true };
            if (_labels) WorldLabels();
            // stats, top right
            GUI.Box(new Rect(Screen.width - 430, 8, 422, 72), _stats, _box);
            if (_selected != null)
            {
                var d = _selected.def;
                var mesh = (_pileItems.entries.Contains(_selected) ? _pileItems : _galleryItems).itemRenderer.WorldMeshFor(_selected.id);
                GUI.Box(new Rect(Screen.width - 430, 86, 422, 92),
                    $"<b>{d.displayName}</b>\n{d.CategoryName} · {d.geometryKey}\n{d.material} {d.finish} · {d.worldScale * MeshLibrary.Get(d.geometryKey, MeshLibrary.LodWorld).bounds.size.magnitude * 100:0.0} cm · {Tris(mesh):N0} tris\nF focus · F2 close-up · click elsewhere to change", _box);
            }
            if (!_panel)
            {
                GUI.Label(new Rect(10, Screen.height - 24, 900, 22), "Tab: panel   " + _status, _label);
                return;
            }
            _panelRect = new Rect(8, 8, 360, Screen.height - 16);
            GUILayout.BeginArea(_panelRect, GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("<b>VISUAL SANDBOX</b>   " + _g.presetId, _head);
            GUILayout.Label("1 BASELINE · 2 VARIANT B · 3-5 EXP A-C · F1-F5 cameras · F6/F7 geometry test set · RMB look/orbit · WASD QE move · wheel speed/zoom · click select · F focus · G gallery · P pile · R regenerate · T A/B · L labels · F12 shot · Tab hide", _label);

            if (Section(ref _secView, "View"))
            {
                GUILayout.Label("Camera: " + _cam.presetName + $"   speed {_cam.speed:0.00} m/s", _label);
                GUILayout.BeginHorizontal();
                for (int k = 1; k <= 5; k++) if (GUILayout.Button(k + "", GUILayout.Width(30))) CameraPreset(k);
                if (GUILayout.Button("Gallery")) FocusGallery();
                if (GUILayout.Button("Pile")) FocusPile();
                GUILayout.EndHorizontal();
                GUILayout.Label("Lighting (materials untouched)", _label);
                GUILayout.BeginHorizontal();
                for (int k = 0; k < SandboxLighting.Presets.Length; k++)
                    if (GUILayout.Toggle(_light.current == k, SandboxLighting.Presets[k].name, GUI.skin.button) && _light.current != k) { _light.Set(k); ApplyStyle(); }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                _labels = GUILayout.Toggle(_labels, "Labels");
                GUILayout.EndHorizontal();
            }

            if (Section(ref _secGallery, $"Gallery ({_gallery.distinct} distinct objects)"))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("<", GUILayout.Width(30))) { _gallery.page = _gallery.page < 0 ? _gallery.categories.Count - 1 : _gallery.page - 1; RebuildGallery(); }
                GUILayout.Label(_gallery.PageName + $" ({_galleryItems.entries.Count})", _label);
                if (GUILayout.Button(">", GUILayout.Width(30))) { _gallery.page = _gallery.page >= _gallery.categories.Count - 1 ? -1 : _gallery.page + 1; RebuildGallery(); }
                GUILayout.EndHorizontal();
            }

            if (Section(ref _secPile, "Pile"))
            {
                var c = _pile.cfg;
                c.count = Mathf.RoundToInt(Slider("Count", c.count, 1, 400, "0"));
                c.radius = Slider("Radius (m)", c.radius, 0.1f, 2f, "0.00");
                c.sizeVariation = Slider("Size variation", c.sizeVariation, 0, 0.5f, "0.00");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Seed", _label, GUILayout.Width(40));
                _seedText = GUILayout.TextField(_seedText, GUILayout.Width(90));
                if (int.TryParse(_seedText, out var sd)) c.seed = sd;
                c.reuseSeed = GUILayout.Toggle(c.reuseSeed, "Keep seed");
                c.evenMix = GUILayout.Toggle(c.evenMix, "Even mix");
                GUILayout.EndHorizontal();
                GUILayout.Label(c.categories.Count == 0 ? "Categories: all" : "Categories:", _label);
                int col = 0;
                GUILayout.BeginHorizontal();
                foreach (var cat in _gallery.categories)
                {
                    bool on = c.categories.Contains(cat);
                    bool now = GUILayout.Toggle(on, ItemDefinition.CategoryNames[cat], GUILayout.Width(110));
                    if (now != on) { if (now) c.categories.Add(cat); else c.categories.Remove(cat); }
                    if (++col % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUI.enabled = !_pileLocked;
                if (GUILayout.Button("REGENERATE PILE", GUILayout.Height(28))) { _pile.Regenerate(); _seedText = c.seed.ToString(); _selected = null; }
                if (GUILayout.Button("RESET PILE", GUILayout.Height(28))) { _pile.Reset(); _seedText = _pile.cfg.seed.ToString(); _selected = null; }
                GUI.enabled = true;
                _pileLocked = GUILayout.Toggle(_pileLocked, "Lock", GUI.skin.button, GUILayout.Width(56), GUILayout.Height(28));
                GUILayout.EndHorizontal();
                GUILayout.Label("<size=11>" + _pile.lastInfo + "</size>", _label);
            }

            if (Section(ref _secStyle, "Visual style"))
            {
                GUILayout.BeginHorizontal();
                for (int k = 0; k < _style.presets.Count; k++)
                    if (GUILayout.Toggle(_style.current == k, ShortName(k), GUI.skin.button) && _style.current != k) SelectStyle(k);
                GUILayout.EndHorizontal();
                if (!_style.CanEdit) GUILayout.Label("<size=11>BASELINE is the game as it is: pick VARIANT B or an experiment to edit</size>", _label);
                bool compact = _style.current == VisualStyleController.VariantB && !_allSliders;
                GUI.enabled = _style.CanEdit;
                bool changed = false;
                foreach (var p in VisualStyle.Params)
                {
                    if (compact && Array.IndexOf(VisualStyle.VariantBKeys, p.key) < 0) continue;
                    float v0 = _style.Style[p.key];
                    float v1 = Slider(p.label + (p.live ? "" : " *"), v0, p.min, p.max, "0.00");
                    if (!Mathf.Approximately(v0, v1)) { _style.Style[p.key] = v1; changed = true; }
                }
                if (changed) ApplyStyle();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save")) { _style.Save(); _status = "Saved " + _style.Style.id + ".json"; }
                if (GUILayout.Button("Revert")) { _style.Revert(); ApplyStyle(); }
                if (GUILayout.Button("Zero")) { _style.Zero(); ApplyStyle(); }
                if (_style.current == VisualStyleController.VariantB && GUILayout.Button("Reset B")) { _style.ResetVariantB(); ApplyStyle(); _status = "Variant B defaults (Save keeps them)"; }
                GUILayout.EndHorizontal();
                GUI.enabled = true;
                if (_style.current == VisualStyleController.VariantB) _allSliders = GUILayout.Toggle(_allSliders, "all sliders");
                GUILayout.Label("<size=11>* placeholder: stored in the preset, no effect yet. Painted bevels and normal strength act on skinned parts and tools.</size>", _label);
                GUILayout.BeginHorizontal();
                GUILayout.Label("A/B against", _label, GUILayout.Width(80));
                for (int k = 1; k < _style.presets.Count; k++) if (GUILayout.Toggle(_abOther == k, ShortName(k), GUI.skin.button)) _abOther = k;
                GUILayout.EndHorizontal();
            }

            if (Section(ref _secShot, "Screenshots"))
            {
                GUILayout.Label($"{_shotW} x {_shotH} → {OutDir}", _label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("1280x720")) { _shotW = 1280; _shotH = 720; }
                if (GUILayout.Button("1920x1080")) { _shotW = 1920; _shotH = 1080; }
                if (GUILayout.Button("2560x1440")) { _shotW = 2560; _shotH = 1440; }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("SCREENSHOT (F12)", GUILayout.Height(26))) Shot();
                if (GUILayout.Button("A/B SIDE BY SIDE", GUILayout.Height(26))) StartCoroutine(ShotAB());
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Save sandbox settings (pile, start lighting / style)")) SaveSettings();
            }
            GUILayout.Label("<size=11>" + _status + "</size>", _label);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        string ShortName(int k) => _style.presets[k].name.Replace("EXPERIMENT ", "EXP ").Replace("VARIANT ", "VAR ");

        static long Tris(Mesh m)
        {
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
            return n;
        }

        void RebuildGallery()
        {
            _selected = null;
            _gallery.Build();
        }

        bool Section(ref bool open, string title)
        {
            open = GUILayout.Toggle(open, (open ? "▼ " : "► ") + title, _head);
            return open;
        }

        float Slider(string label, float v, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label, GUILayout.Width(170));
            v = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(110));
            GUILayout.Label(v.ToString(fmt, CultureInfo.InvariantCulture), _label, GUILayout.Width(44));
            GUILayout.EndHorizontal();
            return v;
        }

        void WorldLabels()
        {
            var cam = _cam.cam;
            var cp = cam.transform.position;
            foreach (var (name, at) in _gallery.headers)
            {
                var sp = cam.WorldToScreenPoint(at);
                if (sp.z <= 0 || sp.z > 30) continue;
                GUI.Label(new Rect(sp.x - 4, Screen.height - sp.y - 22, 300, 22), "<b>" + name + "</b>", _head);
            }
            foreach (var e in _galleryItems.entries)
            {
                if ((e.centre - cp).sqrMagnitude > 2.4f * 2.4f) continue;
                var sp = cam.WorldToScreenPoint(e.centre - Vector3.forward * e.radius);
                if (sp.z <= 0) continue;
                bool sel = e == _selected;
                GUI.Label(new Rect(sp.x - 70, Screen.height - sp.y + 4, 140, 30), sel ? "<color=#ffd060>" + e.def.displayName + "</color>" : e.def.displayName, _small);
            }
            if (_selected != null && _selected.group == "Pile")
            {
                var sp = cam.WorldToScreenPoint(_selected.centre);
                if (sp.z > 0) GUI.Label(new Rect(sp.x - 70, Screen.height - sp.y - 30, 140, 30), "<color=#ffd060>" + _selected.def.displayName + "</color>", _small);
            }
        }
    }
}

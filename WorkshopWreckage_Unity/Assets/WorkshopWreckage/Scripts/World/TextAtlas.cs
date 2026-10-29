using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace WorkshopWreckage
{
    /// <summary>Renders text into textures (Unity counterpart of Godot's SubViewport
    /// label atlas): a world-space uGUI canvas on a private layer is drawn by an
    /// offscreen camera into a RenderTexture whenever the text changes.</summary>
    public class TextAtlas
    {
        public const int Layer = 31;
        static Font _font, _display;
        static float _nextX;
        static int _slot;

        public static Font Font => _font ??= Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Arial" }, 32);
        public static Font Display => _display ??= Font.CreateDynamicFontFromOSFont(new[] { "Rockwell Extra Bold", "Rockwell", "Bahnschrift" }, 64);

        public readonly RenderTexture texture;
        readonly Camera _cam;
        readonly RectTransform _root;
        readonly List<(Text code, Text name, Text count)> _cells = new();
        public readonly int cols, rows, count;
        public readonly Vector2Int cellPx;

        public TextAtlas(int count, Vector2Int cellPx, Color paper)
        {
            this.count = count;
            this.cellPx = cellPx;
            cols = Mathf.Min(8, Mathf.Max(1, count));
            rows = Mathf.CeilToInt((float)count / cols);
            texture = new RenderTexture(cellPx.x * cols, cellPx.y * rows, 16, RenderTextureFormat.ARGB32) { name = "LabelAtlas", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear };
            (_cam, _root) = MakeStage(texture);
            for (int i = 0; i < count; i++)
            {
                var cell = Panel(_root, new Vector2((i % cols) * cellPx.x, (i / cols) * cellPx.y), cellPx, new Color(0.4f, 0.31f, 0.18f));
                Panel(cell, new Vector2(4, 4), cellPx - new Vector2Int(8, 8), paper);
                var code = Label(cell, new Vector2(8, 2), new Vector2(110, 40), 30, new Color(0.15f, 0.1f, 0.06f), Display, TextAnchor.UpperLeft);
                var cnt = Label(cell, new Vector2(96, 4), new Vector2(98, 30), 22, new Color(0.35f, 0.22f, 0.1f), Font, TextAnchor.UpperRight);
                var nm = Label(cell, new Vector2(8, 38), new Vector2(cellPx.x - 14, 40), 15, new Color(0.12f, 0.09f, 0.06f), Font, TextAnchor.UpperLeft);
                nm.lineSpacing = 0.85f;
                _cells.Add((code, nm, cnt));
            }
        }

        public void Set(int i, string code, string name, string countText, bool known)
        {
            var c = _cells[i];
            c.code.text = code;
            c.name.text = name;
            c.name.color = new Color(0.12f, 0.09f, 0.06f, known ? 1 : 0.55f);
            c.count.text = countText;
        }

        public Rect CellUV(int i)
        {
            int cx = i % cols, cy = i / cols;
            // canvas y grows downward in our layout; texture v grows upward
            return new Rect((float)cx / cols, 1f - (float)(cy + 1) / rows, 1f / cols, 1f / rows);
        }

        public void Redraw()
        {
            Canvas.ForceUpdateCanvases();   // text set this frame must be laid out before rendering
            Render(_cam, texture);
        }

        // ------------------------------------------------------------------ helpers

        static (Camera, RectTransform) MakeStage(RenderTexture rt)
        {
            // each stage lives far away on the private layer, side by side: a canvas is as
            // wide in world units as its texture is in pixels, so stages are spaced by width
            // (fixed small spacing made atlas cameras capture their neighbours' labels)
            var origin = new Vector3(10000 + _nextX, 10000, 0);
            _nextX += rt.width + 200;
            _slot++;
            var camGo = new GameObject("TextAtlasCamera");
            camGo.transform.position = origin + new Vector3(rt.width / 2f, -rt.height / 2f, -10);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = rt.height / 2f;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 1;
            cam.farClipPlane = 20;
            cam.targetTexture = rt;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            var canvasGo = new GameObject("TextAtlasCanvas") { layer = Layer };
            canvasGo.transform.position = origin;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            var rtf = (RectTransform)canvasGo.transform;
            rtf.pivot = new Vector2(0, 1);
            rtf.sizeDelta = new Vector2(rt.width, rt.height);
            rtf.position = origin;
            return (cam, rtf);
        }

        static RectTransform Panel(Transform parent, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image)) { layer = Layer };
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(pos.x, -pos.y);
            r.sizeDelta = size;
            go.GetComponent<Image>().color = c;
            return r;
        }

        static Text Label(Transform parent, Vector2 pos, Vector2 size, int fontSize, Color c, Font font, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)) { layer = Layer };
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(pos.x, -pos.y);
            r.sizeDelta = size;
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = fontSize;
            t.color = c;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        static void Render(Camera cam, RenderTexture rt)
        {
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
        }

        /// <summary>One-off text texture (e.g. the big workshop sign).</summary>
        public static Texture RenderSingle(string text, int w, int h, Color bg, Color fg, int fontSize, bool display)
        {
            var rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear };
            var (cam, root) = MakeStage(rt);
            Panel(root, Vector2.zero, new Vector2(w, h), bg);
            var t = Label(root, Vector2.zero, new Vector2(w, h), fontSize, fg, display ? Display : Font, TextAnchor.MiddleCenter);
            t.text = text;
            t.lineSpacing = 0.9f;
            var outline = t.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.18f, 0.1f, 0.05f);
            outline.effectDistance = new Vector2(4, -4);
            Render(cam, rt);
            return rt;
        }
    }
}

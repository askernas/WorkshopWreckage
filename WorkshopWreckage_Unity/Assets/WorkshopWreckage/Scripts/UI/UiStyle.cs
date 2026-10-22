using UnityEngine;
using UnityEngine.UI;

namespace WorkshopWreckage
{
    /// <summary>The game's menu look: blueprint sheets. Menus are deep-blue engineering paper
    /// with a fine and a major grid, a white-ink double frame and masking tape on the corners,
    /// the same language as the drawings taped to the workshop walls. Buttons are ink-outlined
    /// fields that warm to amber when hovered (rust against blueprint). All textures are made
    /// in code once.</summary>
    public static class UiStyle
    {
        public static readonly Color Paper = new(0.12f, 0.25f, 0.4f, 0.97f);
        public static readonly Color Ink = new(0.9f, 0.94f, 0.98f);
        public static readonly Color InkDim = new(0.7f, 0.8f, 0.9f);
        public static readonly Color Amber = new(1f, 0.74f, 0.38f);
        public static readonly Color Tape = new(0.88f, 0.82f, 0.62f, 0.86f);

        static Sprite _paper, _white;

        static float H(int x, int y) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177; return (h & 0xffff) / 65535f; } }

        /// <summary>Tileable blueprint paper (256 px: major lines every 64, minor every 16).</summary>
        public static Sprite PaperSprite
        {
            get
            {
                if (_paper != null) return _paper;
                const int n = 256;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "BlueprintPaper" };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float fib = 0.94f + 0.08f * Mathf.PerlinNoise(x * 0.07f, y * 0.07f) + 0.03f * H(x, y);
                        float stain = Mathf.PerlinNoise(x * 0.012f + 3.1f, y * 0.012f + 7.7f);
                        var c = new Color(1, 1, 1) * fib * (1 - Mathf.SmoothStep(0.62f, 0.8f, stain) * 0.12f);
                        bool major = x % 64 == 0 || y % 64 == 0, minor = x % 16 == 0 || y % 16 == 0;
                        if (major) c = Color.Lerp(c, new Color(1.6f, 1.7f, 1.8f), 0.32f);
                        else if (minor) c = Color.Lerp(c, new Color(1.4f, 1.5f, 1.6f), 0.12f);
                        c.a = 1;
                        px[y * n + x] = c;
                    }
                t.SetPixels(px);
                t.Apply(true);
                return _paper = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            }
        }

        static Sprite White => _white ??= Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            return (RectTransform)go.transform;
        }

        /// <summary>Turns a panel into a taped blueprint sheet (replaces its plain fill).</summary>
        public static void Blueprint(RectTransform panel)
        {
            var img = panel.GetComponent<Image>() ?? panel.gameObject.AddComponent<Image>();
            img.sprite = PaperSprite;
            img.type = Image.Type.Tiled;
            img.color = Paper;
            foreach (Transform c in panel) if (c.name == "bg") c.gameObject.SetActive(false);
            // white-ink double frame: four thin lines each
            foreach (var (inset, width, alpha) in new[] { (7f, 2f, 0.85f), (12f, 1f, 0.5f) })
                foreach (var (amin, amax, omin, omax) in new[]
                {
                    (new Vector2(0, 1), new Vector2(1, 1), new Vector2(inset, -inset - width), new Vector2(-inset, -inset)),   // top
                    (new Vector2(0, 0), new Vector2(1, 0), new Vector2(inset, inset), new Vector2(-inset, inset + width)),       // bottom
                    (new Vector2(0, 0), new Vector2(0, 1), new Vector2(inset, inset), new Vector2(inset + width, -inset)),       // left
                    (new Vector2(1, 0), new Vector2(1, 1), new Vector2(-inset - width, inset), new Vector2(-inset, -inset)),     // right
                })
                {
                    var f = Child("Frame", panel);
                    f.anchorMin = amin; f.anchorMax = amax; f.offsetMin = omin; f.offsetMax = omax;
                    var fi = f.gameObject.AddComponent<Image>();
                    fi.sprite = White;
                    fi.color = new Color(Ink.r, Ink.g, Ink.b, alpha);
                    fi.raycastTarget = false;
                }
            // masking tape across the corners
            foreach (var (ax, ay, rot) in new[] { (0f, 1f, 38f), (1f, 1f, -38f), (0f, 0f, -38f), (1f, 0f, 38f) })
            {
                var tp = Child("Tape", panel);
                tp.anchorMin = tp.anchorMax = new Vector2(ax, ay);
                tp.sizeDelta = new Vector2(92, 26);
                tp.anchoredPosition = new Vector2(ax < 0.5f ? 14 : -14, ay > 0.5f ? -10 : 10);
                tp.localRotation = Quaternion.Euler(0, 0, rot);
                var ti = tp.gameObject.AddComponent<Image>();
                ti.sprite = White;
                ti.color = Tape;
                ti.raycastTarget = false;
            }
        }

        /// <summary>An ink-outlined button that warms to amber on hover.</summary>
        public static void InkButton(Button b)
        {
            var img = b.GetComponent<Image>();
            img.sprite = White;
            img.color = Color.white;   // the state tints below give the colour (UI colours clip at 1)
            var o = b.gameObject.GetComponent<Outline>() ?? b.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.6f);
            o.effectDistance = new Vector2(1, 1);
            o.useGraphicAlpha = false;
            var cols = b.colors;
            cols.normalColor = new Color(0.07f, 0.16f, 0.27f, 1f);       // deep blueprint blue, opaque
            cols.highlightedColor = new Color(0.62f, 0.38f, 0.13f, 1f);  // hover: warm amber (rust against blueprint)
            cols.pressedColor = new Color(0.78f, 0.5f, 0.18f, 1f);
            cols.selectedColor = cols.normalColor;
            cols.disabledColor = new Color(0.07f, 0.13f, 0.2f, 0.7f);
            cols.colorMultiplier = 1;
            b.colors = cols;
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.color = Ink;
        }
    }
}

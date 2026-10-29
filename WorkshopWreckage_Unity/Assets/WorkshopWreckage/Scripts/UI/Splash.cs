using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WorkshopWreckage
{
    /// <summary>The title splash (resources/splash screen, playtest 2026-10-26): shown over everything when
    /// the game starts and stays until a key or click. Then the instructions (resources/instructions.png)
    /// fade in over it until the next key or click, and fade out again. Then the splash drops behind the HUD and stays as the main
    /// menu's backdrop (the menu sits below the painted title) until the game begins; then the game fades in
    /// from black (FadeIn), so the world has settled before it is seen.</summary>
    public class Splash : MonoBehaviour
    {
        const float Appear = 0.5f, Cross = 0.4f;
        /// <summary>Behind the HUD canvas (sortingOrder 10) once dismissed, so the menu is drawn on top.</summary>
        const int BackdropOrder = 5;

        static Splash _current;
        /// <summary>The splash is up as the menu's backdrop (dismissed, the game not begun yet).</summary>
        public static bool IsBackdrop => _current != null && _current._dismissed;

        Canvas _canvas;
        CanvasGroup _group, _instructions;
        Action _onDismiss;
        bool _dismissed, _skip;

        /// <summary>QA (--menushot --splash=backdrop): dismisses it as a click would.</summary>
        public static void Dismiss() { if (_current != null) _current._skip = true; }
        static int _qaPresses;
        /// <summary>QA (--menushot --splash=instructions): one key press, as if the player had clicked.</summary>
        public static void Press() => _qaPresses++;

        /// <summary>Shows the splash; `onDismiss` runs at the first key or click (the menu appears then).</summary>
        public static void Show(Action onDismiss = null)
        {
            var tex = Resources.Load<Texture2D>("Splash/splash");
            if (tex == null) { onDismiss?.Invoke(); return; }
            var go = Overlay("Splash", Color.black, out var canvas, out var group);
            go.AddComponent<GraphicRaycaster>();   // swallows clicks meant for the menu below until dismissed
            var img = new GameObject("Image").AddComponent<RawImage>();
            img.transform.SetParent(go.transform, false);
            img.texture = tex;
            Stretch(img.rectTransform);
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;   // fills the screen, crops the overflow
            fit.aspectRatio = (float)tex.width / tex.height;
            group.alpha = 0;
            CanvasGroup instructions = null;
            if (Resources.Load<Texture2D>("Splash/instructions") is Texture2D how)
            {
                var page = new GameObject("Instructions").AddComponent<RawImage>();
                page.transform.SetParent(go.transform, false);
                page.texture = how;
                Stretch(page.rectTransform);
                var pageFit = page.gameObject.AddComponent<AspectRatioFitter>();
                pageFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                pageFit.aspectRatio = (float)how.width / how.height;
                instructions = page.gameObject.AddComponent<CanvasGroup>();
                instructions.alpha = 0;
            }
            _current = go.AddComponent<Splash>();
            _current._instructions = instructions;
            _current._canvas = canvas;
            _current._group = group;
            _current._onDismiss = onDismiss;
        }

        /// <summary>The game begins: the splash backdrop goes.</summary>
        public static void Close()
        {
            if (_current != null) Destroy(_current.gameObject);
            _current = null;
        }

        /// <summary>Black over everything, held `hold` seconds then faded out over `fade` (unscaled time).</summary>
        public static void FadeIn(float hold = 0.35f, float fade = 0.6f)
        {
            var go = Overlay("FadeIn", Color.black, out _, out var group);
            go.AddComponent<Fader>().Run(group, hold, fade);
        }

        static GameObject Overlay(string name, Color back, out Canvas canvas, out CanvasGroup group)
        {
            var go = new GameObject(name);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            group = go.AddComponent<CanvasGroup>();
            var img = new GameObject("Back").AddComponent<Image>();
            img.transform.SetParent(go.transform, false);
            img.color = back;
            Stretch(img.rectTransform);
            return go;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        IEnumerator Start()
        {
            for (float t = 0; t < Appear; t += Time.unscaledDeltaTime)
            {
                _group.alpha = t / Appear;
                if (Pressed()) break;
                yield return null;
            }
            _group.alpha = 1;
            while (!Pressed() && !_skip) yield return null;
            if (_instructions != null && !_skip)
            {
                yield return Crossfade(_instructions, 1);
                yield return null;   // the click that brought it up doesn't also close it
                while (!Pressed() && !_skip) yield return null;
                yield return Crossfade(_instructions, 0);
            }
            _dismissed = true;
            _canvas.sortingOrder = BackdropOrder;
            _group.blocksRaycasts = false;
            _onDismiss?.Invoke();
        }

        IEnumerator Crossfade(CanvasGroup g, float to)
        {
            float from = g.alpha;
            for (float t = 0; t < Cross; t += Time.unscaledDeltaTime)
            {
                g.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / Cross));
                yield return null;
            }
            g.alpha = to;
        }

        static bool Pressed()
        {
            if (_qaPresses > 0) { _qaPresses--; return true; }
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            return (kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame));
        }

        class Fader : MonoBehaviour
        {
            public void Run(CanvasGroup g, float hold, float fade) => StartCoroutine(Go(g, hold, fade));

            IEnumerator Go(CanvasGroup g, float hold, float fade)
            {
                for (float t = 0; t < hold; t += Time.unscaledDeltaTime) yield return null;
                for (float t = 0; t < fade; t += Time.unscaledDeltaTime)
                {
                    g.alpha = 1 - Mathf.SmoothStep(0, 1, t / fade);
                    yield return null;
                }
                Destroy(gameObject);
            }
        }
    }
}

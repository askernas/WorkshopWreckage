using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WorkshopWreckage
{
    /// <summary>The title splash (resources/splash screen, playtest 2026-10-26; loading 2026-10-31). Shown the first
    /// frame the game runs, with "Loading" and animated dots while the workshop is built (Game.Start yields between
    /// its steps). When the game is ready a "Start game" button appears; it brings the instructions
    /// (resources/instructions.png) in over the splash until the next key or click, and they fade out again. Then the
    /// splash drops behind the HUD and stays as the main menu's backdrop (the menu sits below the painted title) until
    /// the game begins; then the game fades in from black (FadeIn), so the world has settled before it is seen.</summary>
    public class Splash : MonoBehaviour
    {
        const float Cross = 0.4f;
        /// <summary>Behind the HUD canvas (sortingOrder 10) once dismissed, so the menu is drawn on top.</summary>
        const int BackdropOrder = 5;

        static Splash _current;
        /// <summary>The splash is up as the menu's backdrop (dismissed, the game not begun yet).</summary>
        public static bool IsBackdrop => _current != null && _current._dismissed;
        /// <summary>The splash is up and the "Start game" button is waiting.</summary>
        public static bool IsWaitingToStart => _current != null && _current._ready && !_current._started;
        /// <summary>The text under the title now ("Loading..." while loading).</summary>
        public static string StatusText => _current != null && _current._loading.gameObject.activeSelf ? _current._loading.text : "";

        Canvas _canvas;
        CanvasGroup _instructions;
        Action _onDismiss;
        Text _loading;
        Button _start;
        bool _dismissed, _skip, _ready, _started;

        /// <summary>QA (--menushot --splash=backdrop): dismisses it as a click would.</summary>
        public static void Dismiss() { if (_current != null) _current._skip = true; }
        static int _qaPresses;
        /// <summary>QA (--menushot --splash=instructions): one press, as if the player had clicked (the first one
        /// is the Start game button).</summary>
        public static void Press() => _qaPresses++;

        /// <summary>The splash with "Loading..." (no button yet); Ready brings the button once the game is built.</summary>
        public static void ShowLoading()
        {
            Close();
            var tex = Resources.Load<Texture2D>("Splash/splash");
            var go = Overlay("Splash", Color.black, out var canvas, out _);
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            go.AddComponent<GraphicRaycaster>();   // swallows clicks meant for the menu below until dismissed
            if (tex != null)
            {
                var img = new GameObject("Image").AddComponent<RawImage>();
                img.transform.SetParent(go.transform, false);
                img.texture = tex;
                Stretch(img.rectTransform);
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;   // fills the screen, crops the overflow
                fit.aspectRatio = (float)tex.width / tex.height;
            }
            _current = go.AddComponent<Splash>();
            _current._canvas = canvas;

            // a dark plaque low on the picture: "Loading..." on it, then the button
            var plaque = new GameObject("Plaque", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            plaque.SetParent(go.transform, false);
            plaque.anchorMin = plaque.anchorMax = new Vector2(0.5f, 0.09f);
            plaque.sizeDelta = new Vector2(420, 84);
            plaque.GetComponent<Image>().color = new Color(0.05f, 0.04f, 0.03f, 0.88f);
            plaque.GetComponent<Image>().raycastTarget = false;
            var label = new GameObject("Loading", typeof(RectTransform), typeof(Text), typeof(Shadow)).GetComponent<Text>();
            label.transform.SetParent(plaque, false);
            Stretch(label.rectTransform);
            label.font = TextAtlas.Display; label.fontSize = 46; label.color = new Color(1f, 0.86f, 0.55f);
            label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.GetComponent<Shadow>().effectDistance = new Vector2(2, -2);
            _current._loading = label;

            var btnGo = DefaultControls.CreateButton(new DefaultControls.Resources());
            btnGo.name = "Start game";
            btnGo.transform.SetParent(plaque, false);
            var br = (RectTransform)btnGo.transform;
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.offsetMin = new Vector2(10, 10); br.offsetMax = new Vector2(-10, -10);
            var b = btnGo.GetComponent<Button>();
            UiStyle.InkButton(b);
            var bt = btnGo.GetComponentInChildren<Text>();
            bt.text = "START GAME"; bt.font = TextAtlas.Display; bt.fontSize = 34;
            b.onClick.AddListener(() => { if (_current != null) _current._started = true; });
            btnGo.SetActive(false);
            _current._start = b;
            _current.UpdateDots();
        }

        /// <summary>The game is built: "Loading" goes, the Start game button comes. `onDismiss` runs when the
        /// instructions are closed (the menu appears then).</summary>
        public static void Ready(Action onDismiss = null)
        {
            if (_current == null) ShowLoading();
            _current._onDismiss = onDismiss;
            _current._ready = true;
            _current._loading.gameObject.SetActive(false);
            _current._start.gameObject.SetActive(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_current._start.gameObject);   // Enter / gamepad A press it too
        }

        /// <summary>Loading and ready at once (QA --menushot --splash).</summary>
        public static void Show(Action onDismiss = null)
        {
            ShowLoading();
            Ready(onDismiss);
        }

        /// <summary>Back to the main menu from a game (playtest 2026-11-02: "when you exit the game to main menu,
        /// you should have the main menu over the splash screen"): the splash straight up as the menu's backdrop,
        /// without Loading, the Start game button or the instructions.</summary>
        public static void ShowBackdrop()
        {
            ShowLoading();
            var s = _current;
            s._ready = s._skip = s._dismissed = true;
            s._loading.transform.parent.gameObject.SetActive(false);   // the plaque
            s._canvas.sortingOrder = BackdropOrder;
            s.GetComponent<CanvasGroup>().blocksRaycasts = false;
        }

        /// <summary>The game begins (or a test or tool needs no splash): the splash goes.</summary>
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

        /// <summary>"Loading", then one, two, three dots and round again, by the clock: the steps of the build come
        /// unevenly, so the dots show where it has got to whenever a frame is drawn.</summary>
        void UpdateDots()
        {
            if (_loading == null || !_loading.gameObject.activeSelf) return;
            int n = (int)(Time.realtimeSinceStartup * 3f) % 4;
            // the missing dots are drawn invisible, so "Loading" stays put
            _loading.text = "Loading" + new string('.', n) + $"<color=#00000000>{new string('.', 3 - n)}</color>";
        }

        void Update() => UpdateDots();

        IEnumerator Start()
        {
            // waits for the button (a click, or Enter / gamepad A on the selected button; QA: Press)
            while (!_started && !_skip)
            {
                if (_ready && _qaPresses > 0) { _qaPresses--; _started = true; }
                if (_ready && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != _start.gameObject)
                    EventSystem.current.SetSelectedGameObject(_start.gameObject);   // a stray click must not leave it unselectable
                yield return null;
            }
            _start.gameObject.SetActive(false);
            if (!_skip && Resources.Load<Texture2D>("Splash/instructions") is Texture2D how)
            {
                var page = new GameObject("Instructions").AddComponent<RawImage>();
                page.transform.SetParent(transform, false);
                page.texture = how;
                Stretch(page.rectTransform);
                var pageFit = page.gameObject.AddComponent<AspectRatioFitter>();
                pageFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                pageFit.aspectRatio = (float)how.width / how.height;
                _instructions = page.gameObject.AddComponent<CanvasGroup>();
                _instructions.alpha = 0;
                yield return Crossfade(_instructions, 1);
                yield return null;   // the click that brought it up doesn't also close it
                while (!Pressed() && !_skip) yield return null;
                yield return Crossfade(_instructions, 0);
            }
            _dismissed = true;
            _canvas.sortingOrder = BackdropOrder;
            GetComponent<CanvasGroup>().blocksRaycasts = false;
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
            var gp = Gamepad.current;
            return (kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
                   || (gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame));
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

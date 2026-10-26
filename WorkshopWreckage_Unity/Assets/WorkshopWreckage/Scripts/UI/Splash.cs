using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WorkshopWreckage
{
    /// <summary>The title splash (resources/splash screen, playtest 2026-10-26): shown over everything when
    /// the game starts, before the main menu, then fades out. Any key or click skips it.</summary>
    public class Splash : MonoBehaviour
    {
        const float Hold = 3f, Fade = 0.7f;

        public static void Show()
        {
            var tex = Resources.Load<Texture2D>("Splash/splash");
            if (tex == null) return;
            var go = new GameObject("Splash");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            go.AddComponent<GraphicRaycaster>();   // swallows clicks meant for the menu below
            var group = go.AddComponent<CanvasGroup>();

            var back = new GameObject("Back").AddComponent<Image>();
            back.transform.SetParent(go.transform, false);
            back.color = Color.black;
            Stretch(back.rectTransform);

            var img = new GameObject("Image").AddComponent<RawImage>();
            img.transform.SetParent(go.transform, false);
            img.texture = tex;
            Stretch(img.rectTransform);
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;   // fills the screen, crops the overflow
            fit.aspectRatio = (float)tex.width / tex.height;

            go.AddComponent<Splash>()._group = group;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        CanvasGroup _group;

        IEnumerator Start()
        {
            for (float t = 0; t < Hold && !Skipped(); t += Time.unscaledDeltaTime) yield return null;
            for (float t = 0; t < Fade; t += Time.unscaledDeltaTime)
            {
                _group.alpha = 1 - t / Fade;
                yield return null;
            }
            Destroy(gameObject);
        }

        static bool Skipped()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            return (kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame));
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>--pegview: the pegboard on its own for editing (docs/PEGBOARD.md). The camera faces the board;
    /// edit shared/data/pegboard/*.json in the build's data folder and press R to rebuild it from the files.
    /// Keys: R reload, 0-4 hang none / a quarter / half / three quarters / all, H highlight an empty slot,
    /// A/D W/S Q/E move left-right, in-out, up-down, F front view, Tab hide this help.</summary>
    public class PegboardPreview : MonoBehaviour
    {
        Game _g;
        float _fill = 1, _x, _y, _dist = 2.6f;
        bool _help = true;
        string _status = "";
        int _highlight = -1;

        public static PegboardPreview Open(Game g, float fill)
        {
            var p = g.gameObject.AddComponent<PegboardPreview>();
            p._g = g;
            p._fill = Mathf.Clamp01(fill);
            g.player.enabled = false;
            p.Front();
            p.Apply();
            return p;
        }

        WorkshopPegboard Board => _g.builder?.pegboard;

        void Front()
        {
            _x = 0;
            _y = Board != null ? Board.cfg.PanelCentre.y : 0.8f;
            _dist = 2.6f;
        }

        void Apply()
        {
            var pb = Board;
            if (pb == null) { _status = "No pegboard in this workshop"; return; }
            pb.PreviewFill(_fill);
            pb.Highlight(null);
            _highlight = -1;
            _status = $"{pb.Hung} / {pb.Total} tools hung · {pb.Missing} missing · {pb.Percent:0}%" + (pb.cfg.errors.Count > 0 ? $" · {pb.cfg.errors.Count} config errors (see the log)" : "");
        }

        void Reload()
        {
            var pb = Board;
            if (pb == null) return;
            pb.cfg = PegboardConfig.Reload();
            pb.Build();
            Apply();
            Debug.Log("[pegboard] preview: reloaded shared/data/pegboard " + (pb.cfg.errors.Count == 0 ? "(no errors)" : string.Join("; ", pb.cfg.errors)));
        }

        void Update()
        {
            var kb = Keyboard.current;
            var pb = Board;
            if (kb == null || pb == null) return;
            if (kb.rKey.wasPressedThisFrame) Reload();
            var fills = new[] { (kb.digit0Key, 0f), (kb.digit1Key, 0.25f), (kb.digit2Key, 0.5f), (kb.digit3Key, 0.75f), (kb.digit4Key, 1f) };
            foreach (var (key, f) in fills) if (key.wasPressedThisFrame) { _fill = f; Apply(); }
            if (kb.hKey.wasPressedThisFrame)
            {
                var empty = new List<string>();
                foreach (var s in pb.cfg.slots) if (!pb.IsOccupied(s.id)) empty.Add(s.id);
                _highlight = empty.Count == 0 ? -1 : (_highlight + 1) % empty.Count;
                pb.Highlight(_highlight < 0 ? null : empty[_highlight]);
            }
            if (kb.fKey.wasPressedThisFrame) Front();
            if (kb.tabKey.wasPressedThisFrame) _help = !_help;
            float sp = (kb.leftShiftKey.isPressed ? 2.5f : 1f) * Time.unscaledDeltaTime;
            _x += ((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0)) * sp;
            _y += ((kb.eKey.isPressed ? 1 : 0) - (kb.qKey.isPressed ? 1 : 0)) * sp;
            _dist = Mathf.Clamp(_dist + ((kb.sKey.isPressed ? 1 : 0) - (kb.wKey.isPressed ? 1 : 0)) * sp * 1.5f, 0.35f, 6f);
            var at = pb.transform.TransformPoint(new Vector3(_x, _y, pb.cfg.PanelFront));
            var eye = at - pb.transform.forward * _dist;
            // Unity direction -> the player's yaw/pitch (layout convention, mirrored Z), as Autoplay.LookFrom
            var d = (at - eye).normalized;
            var gd = new Vector3(d.x, d.y, -d.z);
            _g.player.Teleport(eye - Vector3.up * PlayerController.Eye);
            _g.player.SetView(Mathf.Atan2(-gd.x, -gd.z), Mathf.Asin(Mathf.Clamp(gd.y, -1, 1)));
            _g.player.head.localPosition = new Vector3(0, PlayerController.Eye, 0);
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
            string t = "<b>PEGBOARD PREVIEW</b>   " + _status;
            if (_help) t += "\nR reload the JSON · 0-4 hang 0/25/50/75/100 % · H highlight an empty slot · A/D W/S Q/E move (Shift faster) · F front · Tab hide help";
            GUI.Box(new Rect(10, 10, 980, _help ? 52 : 30), "");
            GUI.Label(new Rect(18, 12, 970, 50), t, style);
        }
    }
}

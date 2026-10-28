using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace WorkshopWreckage.Sandbox
{
    /// <summary>The sandbox camera, set up as the player's (fov, clip planes, post-processing, SMAA). Fly: right
    /// mouse button looks, WASD moves, Q/E down/up, Shift faster, the wheel sets the speed. Orbit (F on a selected
    /// object, or a focus button): right mouse button orbits, the wheel zooms, WASD leaves the orbit.</summary>
    public class SandboxCamera : MonoBehaviour
    {
        public Camera cam;
        public float speed = 1.5f;
        public bool orbiting;
        public Vector3 target;
        public float distance = 1.5f;
        float _yaw, _pitch;
        public string presetName = "free";
        public bool blockMouse;   // the pointer is over the panel

        public static SandboxCamera Create(Transform parent)
        {
            var go = new GameObject("SandboxCamera");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<SandboxCamera>();
            c.cam = go.AddComponent<Camera>();
            c.cam.nearClipPlane = 0.02f;
            c.cam.farClipPlane = 80;
            c.cam.fieldOfView = 50f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.renderShadows = true;
            go.tag = "MainCamera";
            go.AddComponent<AudioListener>();
            return c;
        }

        public void Place(Vector3 eye, Vector3 look, string name)
        {
            orbiting = false;
            presetName = name;
            transform.position = eye;
            LookAt(look);
        }

        void LookAt(Vector3 at)
        {
            var d = (at - transform.position).normalized;
            _yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            _pitch = -Mathf.Asin(Mathf.Clamp(d.y, -1, 1)) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0);
        }

        /// <summary>Orbit `at` from `dist`, keeping the current direction (a little from above at least).</summary>
        public void Focus(Vector3 at, float dist, string name)
        {
            orbiting = true;
            presetName = name;
            target = at;
            distance = dist;
            _pitch = Mathf.Clamp(_pitch, 12, 80);
            UpdateOrbit();
        }

        void UpdateOrbit()
        {
            var rot = Quaternion.Euler(_pitch, _yaw, 0);
            transform.SetPositionAndRotation(target - rot * Vector3.forward * distance, rot);
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            float dt = Time.unscaledDeltaTime;
            bool look = mouse.rightButton.isPressed && !blockMouse;
            if (look)
            {
                var d = mouse.delta.ReadValue() * 0.12f;
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, -89, 89);
                presetName = "free";
            }
            float wheel = blockMouse ? 0 : mouse.scroll.ReadValue().y;
            var move = new Vector3((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.eKey.isPressed ? 1 : 0) - (kb.qKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            if (orbiting)
            {
                if (move.sqrMagnitude > 0) orbiting = false;
                else
                {
                    if (Mathf.Abs(wheel) > 0.01f) { distance = Mathf.Clamp(distance * Mathf.Pow(0.9f, Mathf.Sign(wheel)), 0.08f, 30f); presetName = "free"; }
                    UpdateOrbit();
                    return;
                }
            }
            if (Mathf.Abs(wheel) > 0.01f) speed = Mathf.Clamp(speed * Mathf.Pow(1.2f, Mathf.Sign(wheel)), 0.05f, 20f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0);
            if (move.sqrMagnitude > 0)
            {
                float sp = speed * (kb.leftShiftKey.isPressed ? 3f : 1f) * (kb.leftCtrlKey.isPressed ? 0.25f : 1f);
                transform.position += (transform.rotation * new Vector3(move.x, 0, move.z) + Vector3.up * move.y) * sp * dt;
                presetName = "free";
            }
        }

        public System.Collections.Generic.Dictionary<string, object> ToDict()
        {
            var p = transform.position; var e = transform.eulerAngles;
            return new()
            {
                ["preset"] = presetName, ["position"] = new System.Collections.Generic.List<object> { (double)p.x, (double)p.y, (double)p.z },
                ["euler"] = new System.Collections.Generic.List<object> { (double)e.x, (double)e.y, (double)e.z }, ["fov"] = (double)cam.fieldOfView,
                ["orbit"] = orbiting, ["orbit_target"] = new System.Collections.Generic.List<object> { (double)target.x, (double)target.y, (double)target.z }, ["orbit_distance"] = (double)distance,
            };
        }
    }
}

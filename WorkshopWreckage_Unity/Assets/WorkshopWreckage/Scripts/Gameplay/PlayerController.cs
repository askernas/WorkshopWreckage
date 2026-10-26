using UnityEngine;
using UnityEngine.InputSystem;

namespace WorkshopWreckage
{
    /// <summary>First-person movement (port of PlayerController.gd): walk, sprint,
    /// crouch, jump, mouse look, Q/E turning, head bob, footsteps.</summary>
    public class PlayerController : MonoBehaviour
    {
        const float Walk = 3.0f, Sprint = 5.2f, CrouchSpeed = 1.6f, Accel = 14f, Gravity = 9.8f, TurnSpeed = 2.6f;
        public const float Eye = 1.62f, EyeCrouch = 0.95f;

        public Camera cam;
        public Transform head;
        public bool inputEnabled = true, lookEnabled = true;
        public float sensitivity = 0.22f;
        /// <summary>The field of view set in Settings; holding the right mouse button zooms in from it.</summary>
        public float fov = 72f;
        const float ZoomFactor = 2.5f, ZoomTime = 0.15f;
        float _zoom;   // 0 = normal view, 1 = fully zoomed in
        public bool invertY;
        CharacterController _cc;
        Vector3 _vel;
        float _yaw, _pitch, _bob, _stepAcc, _eye = Eye;
        public System.Action stepped;
        public DebrisField debris;                       // loose parts underfoot: walk over them
        const float StepUp = 0.35f, MaxSlope = 1.2f;     // step onto ~16 cm at once, climb slopes up to ~50°; steeper piles block
        float _stepSmooth;
        bool _grounded;

        /// <summary>Godot-convention yaw/pitch in radians (yaw 0 = look along Godot -Z).</summary>
        public float Yaw => _yaw;
        public float Pitch => _pitch;

        public void Init()
        {
            gameObject.layer = Layers.Player;
            _cc = gameObject.AddComponent<CharacterController>();
            _cc.radius = 0.28f;
            _cc.height = 1.7f;
            _cc.center = new Vector3(0, 0.85f, 0);
            _cc.stepOffset = 0.25f;
            _cc.skinWidth = 0.02f;
            head = new GameObject("Head").transform;
            head.SetParent(transform, false);
            head.localPosition = new Vector3(0, Eye, 0);
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(head, false);
            cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 80;
            cam.fieldOfView = 72f;   // Godot's Camera3D fov is vertical too
            cam.cullingMask &= ~(1 << TextAtlas.Layer);
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.tag = "MainCamera";
        }

        /// <summary>Sets view from Godot-convention yaw/pitch (radians).</summary>
        public void SetView(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = pitch;
            Apply();
        }

        void Apply()
        {
            transform.rotation = Quaternion.Euler(0, -_yaw * Mathf.Rad2Deg, 0);
            head.localRotation = Quaternion.Euler(-_pitch * Mathf.Rad2Deg, 0, 0);
        }

        public void Teleport(Vector3 feet)
        {
            _cc.enabled = false;
            transform.position = feet;
            _cc.enabled = true;
            _vel = Vector3.zero;
        }

        /// <summary>Frames the cursor has been locked in a row (counted at the end of each frame).</summary>
        public static int LockedFrames { get; private set; }
        /// <summary>Mouse movement steers the view: locked for two full frames, past the recentring warp.</summary>
        public static bool LookReady => Cursor.lockState == CursorLockMode.Locked && LockedFrames >= 2;
        /// <summary>A click counts in play: the cursor was already locked last frame (not the capturing click).</summary>
        public static bool ClickReady => Cursor.lockState == CursorLockMode.Locked && LockedFrames >= 1;

        float _lockYaw, _lockPitch;

        void LateUpdate()
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (locked && LockedFrames == 0) { _lockYaw = _yaw; _lockPitch = _pitch; Debug.Log("[input] cursor captured"); }
            // QA: the view right after a capture is where it was (no jump from the recentring warp)
            if (locked && LockedFrames == 4) Debug.Log($"[input] view change over the capture: {Mathf.Abs(_yaw - _lockYaw) * Mathf.Rad2Deg:0.00} deg yaw, {Mathf.Abs(_pitch - _lockPitch) * Mathf.Rad2Deg:0.00} deg pitch");
            if (!locked && LockedFrames > 0) Debug.Log("[input] cursor released");
            LockedFrames = locked ? LockedFrames + 1 : 0;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            // hold the right mouse button to zoom (playtest 2026-10-26); the view slows down with it
            bool zoomHeld = lookEnabled && inputEnabled && mouse != null && mouse.rightButton.isPressed && LookReady;
            if (zoomHeld || _zoom > 0)
            {
                _zoom = Mathf.MoveTowards(_zoom, zoomHeld ? 1 : 0, Time.unscaledDeltaTime / ZoomTime);
                cam.fieldOfView = Mathf.Lerp(fov, fov / ZoomFactor, Mathf.SmoothStep(0, 1, _zoom));
            }
            if (lookEnabled && mouse != null && LookReady)
            {
                var d = mouse.delta.ReadValue();
                float s = sensitivity * 0.01f * cam.fieldOfView / fov;   // radians per pixel, as in Godot (slower when zoomed)
                _yaw -= d.x * s;
                _pitch = Mathf.Clamp(_pitch + d.y * s * (invertY ? -1 : 1), -1.536f, 1.536f);
            }
            Vector3 wish = Vector3.zero;
            bool crouch = false, sprint = false;
            if (inputEnabled && kb != null)
            {
                float f = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
                float r = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
                wish = (transform.forward * f + transform.right * r);
                if (wish.sqrMagnitude > 1) wish.Normalize();
                float turn = (kb.qKey.isPressed ? 1 : 0) - (kb.eKey.isPressed ? 1 : 0);
                _yaw += turn * TurnSpeed * Time.deltaTime;
                crouch = kb.leftCtrlKey.isPressed || kb.cKey.isPressed;
                sprint = kb.leftShiftKey.isPressed && !crouch;
                if (kb.spaceKey.wasPressedThisFrame && _grounded) _vel.y = 3.2f;
            }
            Apply();
            float speed = crouch ? CrouchSpeed : sprint ? Sprint : Walk;
            var target = wish * speed;
            _vel.x = Mathf.MoveTowards(_vel.x, target.x, Accel * Time.deltaTime * speed);
            _vel.z = Mathf.MoveTowards(_vel.z, target.z, Accel * Time.deltaTime * speed);
            if (_grounded && _vel.y < 0) _vel.y = -0.5f;
            _vel.y -= Gravity * Time.deltaTime;
            var delta = _vel * Time.deltaTime;
            float y0 = transform.position.y;
            _cc.Move(delta);
            _grounded = _cc.isGrounded;
            if (debris != null)
            {
                // ride up onto the parts underfoot; too tall a pile blocks like a wall
                var p = transform.position;
                float ground = debris.Under(p, 0.16f);
                if (p.y < ground - 0.0005f)   // standing on parts: back on top at once (a 3 mm sink band showed as a saw-tooth)
                {
                    float rise = ground - p.y;
                    float horiz = new Vector2(delta.x, delta.z).magnitude;
                    if (rise > StepUp + horiz * MaxSlope && horiz > 0.0005f)
                    {
                        _cc.Move(new Vector3(-delta.x, 0, -delta.z));
                        _vel.x = _vel.z = 0;
                    }
                    else
                    {
                        if (horiz <= 0.0005f) rise = Mathf.Min(rise, Time.deltaTime * 2.5f);   // standing inside parts (teleport/load): pop up onto them
                        _cc.Move(Vector3.up * rise);
                        // the camera eases up instead of popping, by the real climb only: the lift that just undoes
                        // this frame's sink into the parts is no climb (playtest 2026-10-26: the view shook while
                        // standing on parts, more the longer the frame)
                        _stepSmooth -= Mathf.Max(0, transform.position.y - y0);
                        if (_vel.y < 0) _vel.y = 0;
                    }
                }
                if (transform.position.y <= ground + 0.03f) _grounded = true;
            }
            _stepSmooth = Mathf.Lerp(_stepSmooth, 0, Mathf.Min(1, Time.deltaTime * 10));
            _eye = Mathf.Lerp(_eye, crouch ? EyeCrouch : Eye, Mathf.Min(1, Time.deltaTime * 10));
            float h = new Vector2(_vel.x, _vel.z).magnitude;
            if (_grounded && h > 0.3f)
            {
                _bob += Time.deltaTime * h * 2.4f;
                _stepAcc += Time.deltaTime * h;
                if (_stepAcc > 0.75f) { _stepAcc = 0; stepped?.Invoke(); }
            }
            else _bob = Mathf.Lerp(_bob, Mathf.Round(_bob / Mathf.PI) * Mathf.PI, Mathf.Min(1, Time.deltaTime * 6));
            head.localPosition = new Vector3(Mathf.Cos(_bob * 0.5f) * 0.012f, _eye + _stepSmooth + Mathf.Abs(Mathf.Sin(_bob)) * 0.025f, 0);
        }
    }
}

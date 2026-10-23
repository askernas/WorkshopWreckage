using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>Per-frame animation inputs from the robot controller.</summary>
    public struct RobotAnim
    {
        public float dt;
        public float speed01;      // current speed / top speed
        public float speed;        // current speed, m/s (world)
        public float gait;         // 0 = standing still, 1 = full stride (eases in and out)
        public float phase;        // stride phase (radians), settles to a multiple of π when stopped
        public float turnRate;     // degrees per second, + = turning right (clockwise seen from above)
        public float bend;         // 0..1 bending down to pick something up
        public float armReach;     // 0..1 reaching down/forward
        public float armHold;      // 0..1 holding cargo
        public float armLift;      // 0..1 lifting cargo into a destination
        public float altitude;     // hover height above the floor (Hoverbot)
        public bool active;
        public Vector3 lookAt;     // world point the head turns towards (if lookWeight > 0)
        public float lookWeight;
        public float time;
        public float gesture;      // 0..1 progress of a personality gesture (0 = none)
        public float reachHeight;  // world height above the feet that the claws reach down to (pick-up target; 0 = the floor)
        public Vector3 aim;        // world point the claws are raised to (deposit target), weighted by aimWeight
        public float aimWeight;    // 0..1
        public float clawOpen;     // 0..1 grippers open (ready to take or let go of a part)
        public float clawClose;    // 0..1 grippers closed on a part
        public float gripWidth;    // width of the part held between the grippers (world m; 0 = none)
        public Vector3 lookAhead;  // a point ahead on the robot's path (where it is about to go)
        public float armExtend;    // telescopic arms: how far the sleeves are out (world m; models without an aim solve)
        public float lookAheadWeight;
        public Vector3 focus;      // the part or destination being worked on (pick-up, deposit, staging)
        public float focusWeight;
    }

    /// <summary>Base for the five procedural robots (all toy scale, built at ~1 m and
    /// scaled to their configured height). Geometry is authored in Godot-local space
    /// (front = -Z) through Batch. Every primitive belongs to an assembly part. On the
    /// Robot Workbench a robot is genuinely unfinished: parts not yet installed are
    /// simply absent, the bare internal frame shows where a shell is missing, empty
    /// mounting sockets (with loose wires) mark where limbs and heads attach, and an
    /// assembly clamp holds the body up (see BuildAssemblyAids).</summary>
    public abstract partial class RobotModel : MonoBehaviour
    {
        public Transform body, head, carry;
        public abstract float ModelHeight { get; }
        protected HashSet<string> installed = new();
        /// <summary>True while the robot is on the workbench (unfinished or awaiting activation).</summary>
        public bool assembling;
        /// <summary>Whether a missing part's socket is ready to receive it (its mechanical
        /// prerequisites are installed); sockets are only drawn where they are.</summary>
        public System.Func<string, bool> mountReady;
        protected readonly List<Renderer> eyes = new(), beacons = new();
        protected readonly List<Transform> pivots = new();
        readonly List<(Transform pivot, string part, Vector3 normal, float r)> _mounts = new();

        /// <summary>Registers an attachment point: while `part` is missing, an empty
        /// socket facing `normalG` (Godot-local) is drawn at the pivot's origin.</summary>
        protected void Mount(Transform pivot, string part, Vector3 normalG, float r = 0.04f) => _mounts.Add((pivot, part, normalG, r));

        /// <summary>Where the assembly clamp grips the body while on the workbench
        /// (Godot-local, model units, on the robot's back), or null for no clamp.</summary>
        protected virtual Vector3? ClampPoint => null;

        /// <summary>The exposed internal frame drawn while the robot's main shell part is
        /// missing (models override; build into their own pivots with Done).</summary>
        protected virtual void BuildFrame() { }
        static Material _eyeOff, _eyeOn, _beaconOff, _beaconOn;
        protected static Material EyeOffMat => _eyeOff;
        protected static Material BeaconOffMat => _beaconOff;

        protected static Material Paint(Color c, float chip = 0.5f) => EnvMaterials.Metal(c, chip);
        protected static Material Red => EnvMaterials.Metal(new Color(0.72f, 0.1f, 0.07f), 0.45f);
        protected static Material Dark => EnvMaterials.Plain(new Color(0.17f, 0.16f, 0.15f), 0.85f, 0.5f);
        protected static Material Brass => EnvMaterials.Plain(new Color(0.55f, 0.43f, 0.24f), 1f, 0.55f);
        protected static Material Rubber => EnvMaterials.Plain(new Color(0.08f, 0.08f, 0.08f), 0f, 0.25f);
        protected static Material Glow => EnvMaterials.Plain(new Color(1f, 0.8f, 0.45f), 0f, 0.9f, 3f);
        protected static readonly Quaternion AxisX = Batch.R(Vector3.forward, Mathf.PI / 2);
        protected static readonly Quaternion AxisZ = Batch.R(Vector3.right, Mathf.PI / 2);

        public void Init()
        {
            _eyeOff ??= EnvMaterials.Plain(new Color(0.22f, 0.17f, 0.1f), 0.2f, 0.85f);
            _eyeOn ??= EnvMaterials.Plain(new Color(1f, 0.8f, 0.42f), 0f, 0.9f, 2.2f);   // warm lamp; the bezels must still read
            _beaconOff ??= EnvMaterials.Plain(new Color(0.55f, 0.06f, 0.04f), 0.1f, 0.8f);
            _beaconOn ??= EnvMaterials.Plain(new Color(1f, 0.15f, 0.08f), 0f, 0.9f, 5f);
            body = Pivot("Body", transform, Vector3.zero);
            CreatePivots();
        }

        protected Transform Pivot(string n, Transform parent, Vector3 local)
        {
            var t = new GameObject(n).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            pivots.Add(t);
            return t;
        }

        protected abstract void CreatePivots();
        protected abstract void BuildGeometry();
        public abstract void Animate(RobotAnim a);

        /// <summary>Stride phase rate (radians per second) at a given speed. Models whose legs
        /// should not slide override it to match their stride to the ground speed.</summary>
        public virtual float StepRate(float moving01, float speed) => Mathf.Lerp(5f, 8.5f, Mathf.Clamp01(moving01));

        /// <summary>World point between the claws (where a part is gripped), or null.</summary>
        public virtual Vector3? GripPoint => FamilyGripPoint;

        /// <summary>How far in front of the robot (world, flat) the claws come down when it bends to
        /// pick something up at the given height, or null (no such pose).</summary>
        public virtual float? GripReach(float reachHeight) => FamilyGripReach(reachHeight);

        /// <summary>How far in front of the robot (world, flat) the claws are when raised to a height
        /// above its feet (aiming at a destination), or null (no aimed deposit).</summary>
        public virtual float? AimReach(float aimHeight) => FamilyAimReach(aimHeight, 0);

        /// <summary>The same with the arms fully extended (telescopic forearms): the furthest the claws get
        /// in front of the robot at that height, or -1 / null. Equals AimReach without an extension.</summary>
        public virtual float? AimReachMax(float aimHeight) => FamilyAimReach(aimHeight, 2f);

        /// <summary>QA: clears any state the animation carries between frames.</summary>
        public virtual void ResetPose() { }

        readonly List<(Transform t, Vector3 p, Quaternion r, Vector3 sc, bool on)> _snapshot = new();
        /// <summary>Remembers every pivot's pose (to measure a trial pose without showing it).</summary>
        protected void SnapshotPose() { _snapshot.Clear(); foreach (var t in pivots) _snapshot.Add((t, t.localPosition, t.localRotation, t.localScale, t.gameObject.activeSelf)); }
        protected void RestorePose() { foreach (var (t, p, r, sc, on) in _snapshot) { t.localPosition = p; t.localRotation = r; t.localScale = sc; if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on); } }

        /// <summary>Where carried part #i of n sits (parent transform + local position).</summary>
        public virtual (Transform parent, Vector3 local) CarrySlot(int i, int n)
        {
            float spread = 0.12f;
            return (carry, new Vector3((i % 3 - 1) * spread * (n > 1 ? 1 : 0), (i / 3) * 0.08f, 0));
        }

        /// <summary>Rebuilds all geometry; parts not in `have` are left out (and, while
        /// assembling, their empty sockets and the bare frame are shown instead).</summary>
        public void Build(HashSet<string> have)
        {
            installed = have;
            foreach (var p in pivots.Append(transform))   // the clamp hangs on the model root
                for (int i = p.childCount - 1; i >= 0; i--)
                    if (p.GetChild(i).name.StartsWith("G_")) { p.GetChild(i).gameObject.SetActive(false); Destroy(p.GetChild(i).gameObject); }
            eyes.Clear(); beacons.Clear();
            BuildGeometry();
            if (assembling) BuildAssemblyAids();
        }

        protected bool Has(string part) => installed.Contains(part);

        readonly List<(Transform t, float side)> _elbows = new();

        /// <summary>The elbow pivot under an arm pivot (made once, kept across rebuilds; its
        /// geometry is rebuilt like any pivot's), placed at the Godot-local elbow point.</summary>
        protected Transform Elbow(Transform arm, Vector3 posG, float side)
        {
            var e = arm.Find("Elbow");
            if (e == null) { e = Pivot("Elbow", arm, Vector3.zero); _elbows.Add((e, side)); }
            e.localPosition = Batch.G(posG);
            return e;
        }

        /// <summary>The "ready" stance of the concept sheets: forearms raised forward by `bendDeg`
        /// at rest (and optionally rolled back inwards by `rollDeg`, so a forearm can hang from an
        /// arm held out sideways); the arms straighten as they reach or hold, so carrying is unchanged.</summary>
        protected void PoseElbows(RobotAnim a, float bendDeg, float rollDeg = 0)
        {
            float rest = 1 - Mathf.Max(a.armHold, a.armReach);
            foreach (var (e, side) in _elbows) e.localRotation = Quaternion.Euler(-bendDeg * rest, 0, -side * rollDeg * rest);
        }
        /// <summary>The material of a primitive belonging to `part`. A missing part is a
        /// faint ghost while the robot is on the workbench (so the player sees what is still
        /// to find), and is not drawn at all otherwise.</summary>
        protected Material M(string part, Material m) => Has(part) ? Skin(m) : assembling ? Ghost : null;

        /// <summary>A robot's own finish: maps a shared kit material (ToyYellow, Gunmetal, ...) to
        /// the robot's version. Identity unless a model overrides it (Retrobot's polished finish).</summary>
        protected virtual Material Skin(Material m) => m;

        /// <summary>The material of a bolt head given its (already skinned) material; lets a
        /// finish tell fasteners apart from the steel they sit on.</summary>
        protected virtual Material BoltSkin(Material m) => m;

        static Material _ghost;
        protected static Material Ghost
        {
            get
            {
                if (_ghost == null)
                {
                    _ghost = EnvMaterials.NewGhost();
                    _ghost.SetColor("_Color", new Color(0.35f, 0.75f, 1f, 0.16f));
                }
                return _ghost;
            }
        }

        static Material _socket, _wireA, _wireB, _clamp;

        /// <summary>Empty sockets with loose wires for missing parts, the exposed frame and
        /// the workbench clamp. Built as G_ objects so the next Build replaces them.</summary>
        void BuildAssemblyAids()
        {
            _socket ??= EnvMaterials.Plain(new Color(0.24f, 0.25f, 0.26f), 0.9f, 0.5f);
            _wireA ??= EnvMaterials.Plain(new Color(0.75f, 0.12f, 0.08f), 0f, 0.45f);
            _wireB ??= EnvMaterials.Plain(new Color(0.85f, 0.65f, 0.12f), 0f, 0.45f);
            _clamp ??= EnvMaterials.Plain(new Color(0.2f, 0.22f, 0.24f), 0.9f, 0.4f);
            BuildFrame();
            var shown = new HashSet<(Transform, string)>();
            foreach (var (pivot, part, normal, r) in _mounts)
            {
                if (Has(part) || (mountReady != null && !mountReady(part)) || !shown.Add((pivot, part))) continue;
                var b = new Batch();
                var n = normal.normalized;
                var rot = Quaternion.FromToRotation(Vector3.up, Batch.G(n));
                b.Lathe(Vector3.zero, new[] { new Vector2(0, 0), new Vector2(r, 0), new Vector2(r, r * 0.25f), new Vector2(r * 0.7f, r * 0.32f), new Vector2(0, r * 0.32f) }, _socket, rot, 16);
                b.Lathe(n * (r * 0.3f), new[] { new Vector2(0, 0), new Vector2(r * 0.32f, 0), new Vector2(r * 0.32f, r * 0.35f), new Vector2(0, r * 0.4f) }, Brass, rot, 10);
                var side = Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up;
                var t1 = Vector3.Cross(n, side).normalized;
                var t2 = Vector3.Cross(n, t1).normalized;
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI / 2 + Mathf.PI / 4;
                    b.Rivet((t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * r * 0.72f + n * (r * 0.26f), n, r * 0.12f, Brass);
                }
                // two loose wire ends sticking out of the socket
                foreach (var (o, mat, len) in new[] { (0.18f, _wireA, 1.0f), (-0.18f, _wireB, 0.8f) })
                {
                    var dir = (n + t1 * o * 2.5f).normalized;
                    b.Cylinder(n * (r * 0.3f) + t1 * (o * r) + dir * (r * 0.55f * len), r * 0.07f, r * 1.1f * len, mat, Quaternion.FromToRotation(Vector3.up, Batch.G(dir)), 6);
                }
                b.Build(pivot, "G_socket", true);
            }
            if (ClampPoint is Vector3 c)
            {
                // assembly clamp: a stand behind the robot with an arm gripping its back
                var b = new Batch();
                float postZ = c.z + 0.16f;
                b.Box(new Vector3(0, 0.012f, postZ), new Vector3(0.22f, 0.024f, 0.16f), _clamp);
                b.Cylinder(new Vector3(0, c.y / 2, postZ), 0.022f, c.y, _clamp);
                b.Box(new Vector3(0, c.y, (c.z + postZ) / 2 + 0.01f), new Vector3(0.04f, 0.04f, postZ - c.z + 0.02f), _clamp);
                b.Box(new Vector3(0, c.y, c.z + 0.008f), new Vector3(0.12f, 0.07f, 0.016f), Brass);
                b.Lathe(new Vector3(0.05f, c.y, postZ + 0.02f), Disc0(0.028f, 0.012f), Brass, Batch.R(Vector3.right, Mathf.PI / 2), 12);
                b.Build(transform, "G_clamp", true);
            }
        }

        static Vector2[] Disc0(float r, float h) => new[] { new Vector2(0, 0), new Vector2(r, 0), new Vector2(r, h), new Vector2(0, h) };

        /// <summary>Removes the workbench aids (activation).</summary>
        public void EndAssembly()
        {
            assembling = false;
            Build(installed);
        }

        protected static void Done(Batch b, Transform parent) => b.Build(parent, "G_mesh", true);

        protected Renderer Prim(PrimitiveType t, Transform parent, Vector3 posG, Vector3 scale, Quaternion rot, Material m)
        {
            if (m == null) return null;
            var go = GameObject.CreatePrimitive(t);
            Destroy(go.GetComponent<Collider>());
            go.name = "G_prim";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Batch.G(posG);
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.On;
            return r;
        }

        /// <summary>The family head shared by all five robots: a boxy riveted head with one
        /// or two round lamp eyes, red ear discs, two antennas with red balls, a mouth
        /// grille and an optional red beacon on top. Built around the head pivot.</summary>
        protected void HeadKit(Transform pivot, Material paint, Vector3 size, int eyeCount,
            string shell, string eyePart, string antennaL, string antennaR, string earL, string earR, string beacon, bool mouth = true, string eyePartRight = null)
        {
            var b = new Batch();
            float hw = size.x / 2, hh = size.y / 2, hd = size.z / 2;
            var c = new Vector3(0, hh, 0);
            b.Box(c, size, M(shell, paint));
            foreach (var x in new[] { -hw + 0.02f, hw - 0.02f })
                foreach (var y in new[] { 0.03f, size.y - 0.03f })
                    b.Box(new Vector3(x, y, -hd - 0.003f), new Vector3(0.012f, 0.012f, 0.006f), M(shell, Dark));
            if (mouth)
            {
                b.Box(new Vector3(0, size.y * 0.22f, -hd - 0.002f), new Vector3(size.x * 0.34f, size.y * 0.14f, 0.008f), M(shell, Dark));
                for (int k = 0; k < 6; k++) b.Box(new Vector3(-size.x * 0.14f + k * size.x * 0.056f, size.y * 0.22f, -hd - 0.007f), new Vector3(0.006f, size.y * 0.1f, 0.004f), M(shell, Brass));
            }
            b.Box(new Vector3(0, size.y * 0.5f, hd + 0.002f), new Vector3(size.x * 0.4f, size.y * 0.3f, 0.008f), M(shell, Dark));   // back vent
            float er = eyeCount == 1 ? size.y * 0.3f : size.y * 0.22f;
            var eyeXs = eyeCount == 1 ? new[] { 0f } : new[] { -size.x * 0.24f, size.x * 0.24f };
            string EyeOf(float x) => x > 0 && eyePartRight != null ? eyePartRight : eyePart;
            foreach (var x in eyeXs)
            {
                b.Cylinder(new Vector3(x, size.y * 0.6f, -hd - 0.008f), er * 1.15f, 0.02f, M(EyeOf(x), Dark), AxisZ);
                b.Cylinder(new Vector3(x, size.y * 0.6f, -hd - 0.014f), er, 0.012f, M(EyeOf(x), Brass), AxisZ);
            }
            foreach (var (x, ear) in new[] { (-1f, earL), (1f, earR) })
            {
                b.Cylinder(new Vector3(x * (hw + 0.008f), size.y * 0.52f, 0), size.y * 0.26f, 0.03f, M(ear, Red), AxisX);
                b.Cylinder(new Vector3(x * (hw + 0.026f), size.y * 0.52f, 0), size.y * 0.1f, 0.014f, M(ear, Dark), AxisX);
            }
            foreach (var (x, ant) in new[] { (-1f, antennaL), (1f, antennaR) })
            {
                b.Cylinder(new Vector3(x * hw * 0.75f, size.y + 0.05f, 0), 0.006f, 0.1f, M(ant, Dark));
                b.Cylinder(new Vector3(x * hw * 0.75f, size.y + 0.006f, 0), 0.014f, 0.012f, M(ant, Dark));
            }
            if (beacon != null) b.Cylinder(new Vector3(0, size.y + 0.01f, 0), 0.045f, 0.02f, M(beacon, Dark));
            Done(b, pivot);
            foreach (var x in eyeXs)
                if (Has(EyeOf(x)))
                    eyes.Add(Prim(PrimitiveType.Cylinder, pivot, new Vector3(x, size.y * 0.6f, -hd - 0.021f), new Vector3(er * 1.7f, 0.004f, er * 1.7f), AxisZ, _eyeOff));
            foreach (var (x, ant) in new[] { (-1f, antennaL), (1f, antennaR) })
                Prim(PrimitiveType.Sphere, pivot, new Vector3(x * hw * 0.75f, size.y + 0.11f, 0), Vector3.one * 0.045f, Quaternion.identity, M(ant, Red));
            if (beacon != null && Has(beacon))
                beacons.Add(Prim(PrimitiveType.Sphere, pivot, new Vector3(0, size.y + 0.035f, 0), new Vector3(0.075f, 0.06f, 0.075f), Quaternion.identity, _beaconOff));
        }

        public void SetEyes(bool on) { foreach (var r in eyes) if (r != null) r.sharedMaterial = on ? _eyeOn : _eyeOff; }
        public void SetBeacon(bool on) { foreach (var r in beacons) if (r != null) r.sharedMaterial = on ? _beaconOn : _beaconOff; }

        /// <summary>Two-segment toy arm with red claw, hanging from its pivot (Godot-local).</summary>
        protected void ArmKit(Batch b, float side, string part, Material paint, float scale = 1f, string clawPart = null)
        {
            clawPart ??= part;
            float s = scale;
            b.Cylinder(new Vector3(side * 0.012f * s, 0, 0), 0.048f * s, 0.05f * s, M(part, Red), AxisX);
            b.Box(new Vector3(side * 0.008f * s, -0.035f * s, 0), new Vector3(0.065f, 0.07f, 0.085f) * s, M(part, paint));
            b.Box(new Vector3(0, -0.11f * s, 0), new Vector3(0.055f, 0.085f, 0.058f) * s, M(part, paint));
            b.Cylinder(new Vector3(0, -0.165f * s, 0), 0.03f * s, 0.07f * s, M(part, Dark), AxisX);
            b.Box(new Vector3(0, -0.225f * s, 0), new Vector3(0.07f, 0.1f, 0.07f) * s, M(part, paint));
            b.Cylinder(new Vector3(0, -0.285f * s, 0), 0.024f * s, 0.055f * s, M(clawPart, Dark), AxisX);
            foreach (var f in new[] { -1f, 1f })
            {
                b.Box(new Vector3(f * 0.026f * s, -0.335f * s, 0), new Vector3(0.02f, 0.08f, 0.046f) * s, M(clawPart, Red), false, Batch.R(Vector3.forward, f * 0.28f));
                b.Box(new Vector3(f * 0.034f * s, -0.372f * s, 0), new Vector3(0.019f, 0.028f, 0.046f) * s, M(clawPart, Red), false, Batch.R(Vector3.forward, -f * 0.5f));
            }
        }

        /// <summary>Default head behaviour: look towards a point, nod with the gait.</summary>
        protected void LookHead(RobotAnim a, float maxYaw = 60)
        {
            if (head == null) return;
            float yaw = 0;
            if (a.lookWeight > 0)
            {
                var to = a.lookAt - transform.position; to.y = 0;
                yaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, to, Vector3.up), -maxYaw, maxYaw) * a.lookWeight;
            }
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(a.bend * 10 + Mathf.Sin(a.phase * 2) * 2.5f * a.gait, yaw, 0), a.dt * 5);
        }
    }
}

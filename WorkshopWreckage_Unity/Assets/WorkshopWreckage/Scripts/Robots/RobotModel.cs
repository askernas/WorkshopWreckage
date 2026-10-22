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
    }

    /// <summary>Base for the five procedural robots (all toy scale, built at ~1 m and
    /// scaled to their configured height). Geometry is authored in Godot-local space
    /// (front = -Z) through Batch. Every primitive belongs to an assembly part; parts
    /// not yet installed at the repair bay render as translucent ghosts.</summary>
    public abstract class RobotModel : MonoBehaviour
    {
        public Transform body, head, carry;
        public abstract float ModelHeight { get; }
        protected HashSet<string> installed = new();
        protected Material ghost;
        protected readonly List<Renderer> eyes = new(), beacons = new();
        protected readonly List<Transform> pivots = new();
        static Material _eyeOff, _eyeOn, _beaconOff, _beaconOn;

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
            _eyeOn ??= EnvMaterials.Plain(new Color(1f, 0.82f, 0.45f), 0f, 0.9f, 4f);
            _beaconOff ??= EnvMaterials.Plain(new Color(0.55f, 0.06f, 0.04f), 0.1f, 0.8f);
            _beaconOn ??= EnvMaterials.Plain(new Color(1f, 0.15f, 0.08f), 0f, 0.9f, 5f);
            ghost = EnvMaterials.NewGhost();
            ghost.SetColor("_Color", new Color(0.35f, 0.75f, 1f, 0.22f));
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

        /// <summary>Where carried part #i of n sits (parent transform + local position).</summary>
        public virtual (Transform parent, Vector3 local) CarrySlot(int i, int n)
        {
            float spread = 0.12f;
            return (carry, new Vector3((i % 3 - 1) * spread * (n > 1 ? 1 : 0), (i / 3) * 0.08f, 0));
        }

        /// <summary>Rebuilds all geometry; parts not in `have` are ghosted.</summary>
        public void Build(HashSet<string> have)
        {
            installed = have;
            foreach (var p in pivots)
                for (int i = p.childCount - 1; i >= 0; i--)
                    if (p.GetChild(i).name.StartsWith("G_")) Destroy(p.GetChild(i).gameObject);
            eyes.Clear(); beacons.Clear();
            BuildGeometry();
        }

        protected bool Has(string part) => installed.Contains(part);
        protected Material M(string part, Material m) => Has(part) ? m : ghost;

        protected static void Done(Batch b, Transform parent) => b.Build(parent, "G_mesh", true);

        protected Renderer Prim(PrimitiveType t, Transform parent, Vector3 posG, Vector3 scale, Quaternion rot, Material m)
        {
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
            if (beacon != null)
            {
                var r = Prim(PrimitiveType.Sphere, pivot, new Vector3(0, size.y + 0.035f, 0), new Vector3(0.075f, 0.06f, 0.075f), Quaternion.identity, Has(beacon) ? _beaconOff : ghost);
                if (Has(beacon)) beacons.Add(r);
            }
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

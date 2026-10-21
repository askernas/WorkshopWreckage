using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>Procedural Retrobot following resources/retrobot.png: a small, weathered
    /// yellow tin toy (≈1 m tall) with a boxy head, round lamp eyes, red ear discs,
    /// two antennas with red balls, a red beacon, a grilled torso, segmented arms with
    /// red claws, red knee discs and chunky feet. Built from five assembly parts
    /// (head, torso, left arm, right arm, leg assembly); a missing part is drawn as a
    /// translucent ghost so the player can see what is still needed.
    /// Geometry is authored in Godot-local space (front = -Z), like all Batch geometry.</summary>
    public class RetrobotModel : MonoBehaviour
    {
        public const string Head = "head", Torso = "torso", ArmL = "arm_l", ArmR = "arm_r", Legs = "legs";
        public static readonly string[] Parts = { Head, Torso, ArmL, ArmR, Legs };

        public Transform body, upper, upperGeo, head, armL, armR, legL, legR, carry;
        readonly List<Renderer> _eyes = new(), _beacon = new();
        Material _ghost;
        static Material _eyeOff, _eyeOn, _beaconOff, _beaconOn;

        static Material Yellow => EnvMaterials.Metal(new Color(0.84f, 0.62f, 0.17f), 0.55f);
        static Material Red => EnvMaterials.Metal(new Color(0.72f, 0.1f, 0.07f), 0.45f);
        static Material Dark => EnvMaterials.Plain(new Color(0.17f, 0.16f, 0.15f), 0.85f, 0.5f);
        static Material Brass => EnvMaterials.Plain(new Color(0.55f, 0.43f, 0.24f), 1f, 0.55f);
        static Material Rubber => EnvMaterials.Plain(new Color(0.08f, 0.08f, 0.08f), 0f, 0.25f);

        public void Init()
        {
            _eyeOff ??= EnvMaterials.Plain(new Color(0.22f, 0.17f, 0.1f), 0.2f, 0.85f);
            _eyeOn ??= EnvMaterials.Plain(new Color(1f, 0.82f, 0.45f), 0f, 0.9f, 4f);
            _beaconOff ??= EnvMaterials.Plain(new Color(0.55f, 0.06f, 0.04f), 0.1f, 0.8f);
            _beaconOn ??= EnvMaterials.Plain(new Color(1f, 0.15f, 0.08f), 0f, 0.9f, 5f);
            _ghost = EnvMaterials.NewGhost();
            _ghost.SetColor("_Color", new Color(0.35f, 0.75f, 1f, 0.22f));
            body = Pivot("Body", transform, Vector3.zero);
            legL = Pivot("LegL", body, new Vector3(-0.075f, 0.36f, 0));
            legR = Pivot("LegR", body, new Vector3(0.075f, 0.36f, 0));
            // everything above the hips bends forward around the hip pivot
            upper = Pivot("Upper", body, new Vector3(0, 0.36f, 0));
            upperGeo = Pivot("UpperGeo", upper, new Vector3(0, -0.36f, 0));
            armL = Pivot("ArmL", upperGeo, new Vector3(-0.178f, 0.6f, 0));
            armR = Pivot("ArmR", upperGeo, new Vector3(0.178f, 0.6f, 0));
            head = Pivot("Head", upperGeo, new Vector3(0, 0.675f, 0));
            carry = Pivot("Carry", upperGeo, new Vector3(0, 0.46f, 0.33f));   // between the claws when the arms hold forward
        }

        static Transform Pivot(string n, Transform parent, Vector3 local)
        {
            var t = new GameObject(n).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        /// <summary>Rebuilds the geometry: installed parts solid, missing parts ghosted.</summary>
        public void Build(ICollection<string> installed)
        {
            foreach (var p in new[] { body, upperGeo, legL, legR, armL, armR, head })
                for (int i = p.childCount - 1; i >= 0; i--)
                {
                    var c = p.GetChild(i);
                    if (c.name.StartsWith("G_")) Destroy(c.gameObject);
                }
            _eyes.Clear(); _beacon.Clear();
            bool Has(string part) => installed.Contains(part);
            Material M(string part, Material m) => Has(part) ? m : _ghost;

            // torso
            {
                var b = new Batch();
                string P = Torso;
                b.Box(new Vector3(0, 0.39f, 0), new Vector3(0.2f, 0.06f, 0.13f), M(P, Dark));
                b.Box(new Vector3(0, 0.53f, 0), new Vector3(0.28f, 0.25f, 0.2f), M(P, Yellow));
                b.Box(new Vector3(0, 0.575f, -0.102f), new Vector3(0.135f, 0.07f, 0.008f), M(P, Dark));
                for (int k = 0; k < 4; k++) b.Box(new Vector3(0, 0.55f + k * 0.016f, -0.107f), new Vector3(0.12f, 0.006f, 0.006f), M(P, Brass));
                b.Box(new Vector3(0, 0.462f, -0.104f), new Vector3(0.1f, 0.045f, 0.012f), M(P, Yellow));
                b.Box(new Vector3(0, 0.462f, -0.111f), new Vector3(0.05f, 0.012f, 0.004f), M(P, Dark));
                b.Box(new Vector3(0, 0.56f, 0.102f), new Vector3(0.14f, 0.07f, 0.008f), M(P, Dark));   // back grille
                foreach (var x in new[] { -0.12f, 0.12f })
                    foreach (var y in new[] { 0.42f, 0.64f })
                        b.Box(new Vector3(x, y, -0.101f), new Vector3(0.014f, 0.014f, 0.008f), M(P, Dark));   // rivets
                b.Cylinder(new Vector3(0, 0.665f, 0), 0.042f, 0.05f, M(P, Dark));   // neck
                Done(b, upperGeo, P);
            }
            // legs (one assembly part, two pivots)
            foreach (var (leg, s) in new[] { (legL, -1f), (legR, 1f) })
            {
                var b = new Batch();
                string P = Legs;
                var axisX = Batch.R(Vector3.forward, Mathf.PI / 2);
                var axisZ = Batch.R(Vector3.right, Mathf.PI / 2);
                b.Cylinder(new Vector3(0, 0, 0), 0.036f, 0.08f, M(P, Dark), axisX);          // hip joint
                b.Box(new Vector3(0, -0.065f, 0), new Vector3(0.085f, 0.1f, 0.09f), M(P, Yellow));
                b.Cylinder(new Vector3(0, -0.14f, 0), 0.04f, 0.085f, M(P, Dark), axisX);     // knee joint
                b.Cylinder(new Vector3(0, -0.14f, -0.046f), 0.037f, 0.025f, M(P, Red), axisZ);   // knee disc
                b.Cylinder(new Vector3(0, -0.14f, -0.06f), 0.014f, 0.01f, M(P, Brass), axisZ);
                foreach (var dx in new[] { -0.022f, 0.022f }) b.Cylinder(new Vector3(dx, -0.225f, 0.012f), 0.014f, 0.13f, M(P, Dark));   // pistons
                b.Box(new Vector3(0, -0.215f, -0.018f), new Vector3(0.075f, 0.08f, 0.05f), M(P, Yellow));
                b.Box(new Vector3(0, -0.312f, -0.03f), new Vector3(0.11f, 0.065f, 0.19f), M(P, Yellow));   // foot
                b.Box(new Vector3(0, -0.31f, -0.126f), new Vector3(0.07f, 0.024f, 0.006f), M(P, Dark));
                b.Box(new Vector3(0, -0.35f, -0.03f), new Vector3(0.122f, 0.02f, 0.205f), M(P, Rubber));
                b.Box(new Vector3(0, -0.322f, 0.068f), new Vector3(0.1f, 0.04f, 0.03f), M(P, Red));
                Done(b, leg, P);
            }
            // arms
            foreach (var (arm, s, part) in new[] { (armL, -1f, ArmL), (armR, 1f, ArmR) })
            {
                var b = new Batch();
                var axisX = Batch.R(Vector3.forward, Mathf.PI / 2);
                b.Cylinder(new Vector3(s * 0.012f, 0, 0), 0.052f, 0.05f, M(part, Red), axisX);   // shoulder disc
                b.Cylinder(new Vector3(s * 0.04f, 0, 0), 0.02f, 0.012f, M(part, Brass), axisX);
                b.Box(new Vector3(s * 0.01f, -0.035f, 0), new Vector3(0.07f, 0.075f, 0.09f), M(part, Yellow));
                b.Box(new Vector3(0, -0.115f, 0), new Vector3(0.058f, 0.09f, 0.062f), M(part, Yellow));
                b.Cylinder(new Vector3(0, -0.17f, 0), 0.032f, 0.075f, M(part, Dark), axisX);       // elbow
                b.Box(new Vector3(0, -0.235f, 0), new Vector3(0.075f, 0.11f, 0.075f), M(part, Yellow));
                b.Cylinder(new Vector3(0, -0.3f, 0), 0.026f, 0.06f, M(part, Dark), axisX);         // wrist
                foreach (var f in new[] { -1f, 1f })
                {
                    var tilt = Batch.R(Vector3.forward, f * 0.28f);
                    b.Box(new Vector3(f * 0.028f, -0.355f, 0), new Vector3(0.022f, 0.085f, 0.05f), M(part, Red), false, tilt);
                    b.Box(new Vector3(f * 0.036f, -0.395f, 0), new Vector3(0.02f, 0.03f, 0.05f), M(part, Red), false, Batch.R(Vector3.forward, -f * 0.5f));
                }
                Done(b, arm, part);
            }
            // head
            {
                var b = new Batch();
                string P = Head;
                var axisX = Batch.R(Vector3.forward, Mathf.PI / 2);
                var axisZ = Batch.R(Vector3.right, Mathf.PI / 2);
                b.Box(new Vector3(0, 0.13f, 0), new Vector3(0.32f, 0.24f, 0.24f), M(P, Yellow));
                foreach (var x in new[] { -0.075f, 0.075f })
                {
                    b.Cylinder(new Vector3(x, 0.15f, -0.122f), 0.056f, 0.02f, M(P, Dark), axisZ);
                    b.Cylinder(new Vector3(x, 0.15f, -0.128f), 0.047f, 0.012f, M(P, Brass), axisZ);
                }
                b.Box(new Vector3(0, 0.055f, -0.122f), new Vector3(0.11f, 0.036f, 0.008f), M(P, Dark));
                for (int k = 0; k < 6; k++) b.Box(new Vector3(-0.045f + k * 0.018f, 0.055f, -0.127f), new Vector3(0.006f, 0.028f, 0.004f), M(P, Brass));
                foreach (var x in new[] { -1f, 1f })
                {
                    b.Cylinder(new Vector3(x * 0.168f, 0.13f, 0), 0.062f, 0.03f, M(P, Red), axisX);    // ear discs
                    b.Cylinder(new Vector3(x * 0.186f, 0.13f, 0), 0.026f, 0.014f, M(P, Dark), axisX);
                    b.Cylinder(new Vector3(x * 0.12f, 0.3f, 0), 0.006f, 0.1f, M(P, Dark));               // antennas
                    b.Cylinder(new Vector3(x * 0.12f, 0.255f, 0), 0.014f, 0.012f, M(P, Dark));
                }
                foreach (var x in new[] { -0.13f, 0.13f })
                    foreach (var y in new[] { 0.03f, 0.235f })
                        b.Box(new Vector3(x, y, -0.121f), new Vector3(0.012f, 0.012f, 0.006f), M(P, Dark));
                b.Cylinder(new Vector3(0, 0.258f, 0), 0.045f, 0.02f, M(P, Dark));
                b.Box(new Vector3(0, 0.12f, 0.122f), new Vector3(0.13f, 0.08f, 0.008f), M(P, Dark));   // back vent
                Done(b, head, P);

                // separately rendered parts whose material changes at activation
                if (Has(P))
                {
                    foreach (var x in new[] { -0.075f, 0.075f })
                        _eyes.Add(Prim(PrimitiveType.Cylinder, head, new Vector3(x, 0.15f, -0.133f), new Vector3(0.078f, 0.004f, 0.078f), axisZ, _eyeOff));
                    _beacon.Add(Prim(PrimitiveType.Sphere, head, new Vector3(0, 0.285f, 0), new Vector3(0.075f, 0.06f, 0.075f), Quaternion.identity, _beaconOff));
                    foreach (var x in new[] { -0.12f, 0.12f })
                        Prim(PrimitiveType.Sphere, head, new Vector3(x, 0.36f, 0), Vector3.one * 0.045f, Quaternion.identity, Red);
                }
            }
        }

        void Done(Batch b, Transform parent, string part)
        {
            b.Build(parent, "G_" + part, true);
        }

        static Renderer Prim(PrimitiveType t, Transform parent, Vector3 posG, Vector3 scale, Quaternion rot, Material m)
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

        public void SetEyes(bool on) { foreach (var r in _eyes) r.sharedMaterial = on ? _eyeOn : _eyeOff; }
        public void SetBeacon(bool on) { foreach (var r in _beacon) r.sharedMaterial = on ? _beaconOn : _beaconOff; }
    }
}

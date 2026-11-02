using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorkshopWreckage
{
    /// <summary>The robot parts bin's heap under gravity (playtest 2026-11-02: "items placed in the robot parts bin
    /// needs to be affected by gravity ... cannot float in mid air"). Every part in the bin is a rigid body with the
    /// same convex hull, friction and mass as a part on the floor (PileBodies), in a small physics scene of its own
    /// holding only the bin's floor and walls, in the bin's own space (the PartsPool transform: origin on the bin's
    /// floor, y up). A new part is let go just above the heap and falls into place, a part taken out lets the rest
    /// settle, and the views follow the bodies. The bodies stay between refreshes, so a rebuilt view lies exactly
    /// where its part came to rest.</summary>
    public class BinPile : MonoBehaviour
    {
        const float Step = 1 / 240f;
        const int MaxStepsPerFrame = 12;
        const float Timeout = 6f;

        class Body
        {
            public GameObject go;
            public Rigidbody rb;
            public Transform view;
        }

        Scene _scene;
        PhysicsScene _phys;
        readonly Dictionary<int, Body> _bodies = new();
        readonly List<Collider> _walls = new();
        float _acc, _since;
        bool _active;

        /// <summary>True while something in the bin is still moving.</summary>
        public bool Settling => _active;

        /// <summary>The bin: inner half sizes (x, z) and the wall height. The walls stand a little above the rim so
        /// nothing tumbles out over it.</summary>
        public void Init(float halfX, float halfZ, float height)
        {
            if (_scene.IsValid()) return;
            _scene = SceneManager.CreateScene("BinPile" + Time.frameCount + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 6), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _phys = _scene.GetPhysicsScene();
            const float t = 0.1f;
            float h = height + 0.3f;
            Wall(new Vector3(0, -t / 2, 0), new Vector3(2 * halfX + 2 * t, t, 2 * halfZ + 2 * t));   // floor
            Wall(new Vector3(-halfX - t / 2, h / 2, 0), new Vector3(t, h, 2 * halfZ + 2 * t));
            Wall(new Vector3(halfX + t / 2, h / 2, 0), new Vector3(t, h, 2 * halfZ + 2 * t));
            Wall(new Vector3(0, h / 2, -halfZ - t / 2), new Vector3(2 * halfX, h, t));
            Wall(new Vector3(0, h / 2, halfZ + t / 2), new Vector3(2 * halfX, h, t));
        }

        void Wall(Vector3 c, Vector3 size)
        {
            var go = new GameObject("bin");
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.position = c;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            bc.sharedMaterial = PileBodies.Material;
            bc.contactOffset = 0.002f;
            _walls.Add(bc);
        }

        public bool Has(int id) => _bodies.ContainsKey(id);

        /// <summary>The top of the heap (bin space), or the floor.</summary>
        public float Top()
        {
            Physics.SyncTransforms();   // a part added this frame counts too
            return _bodies.Values.Select(b => b.go.GetComponent<Collider>().bounds.max.y).DefaultIfEmpty(0).Max();
        }

        /// <summary>Parts no longer in the bin leave it; if any did, the rest settle into the gap.</summary>
        public void Keep(HashSet<int> ids)
        {
            var gone = _bodies.Keys.Where(id => !ids.Contains(id)).ToList();
            foreach (var id in gone) { DestroyImmediate(_bodies[id].go); _bodies.Remove(id); }
            if (gone.Count > 0) Wake();
        }

        /// <summary>A rebuilt view of a part already in the bin: it lies where its body lies.</summary>
        public void Bind(int id, Transform view)
        {
            var b = _bodies[id];
            b.view = view;
            view.SetLocalPositionAndRotation(b.go.transform.position, b.go.transform.rotation);
        }

        /// <summary>A part put in the bin: let go at pos / rot (bin space), it falls.</summary>
        public void Add(int id, ItemDefinition def, Transform view, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("part" + id);
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * def.worldScale;
            PileBodies.AddCollider(go, def);
            var b = new Body { go = go, rb = PileBodies.AddRigidbody(go, def), view = view };
            _bodies[id] = b;
            view.SetLocalPositionAndRotation(pos, rot);
            Wake();
        }

        void Wake()
        {
            foreach (var b in _bodies.Values) b.rb.WakeUp();
            _active = true;
            _since = 0;
        }

        /// <summary>Settles at once, without showing the fall (loading a game with parts in the bin).</summary>
        public void SettleNow()
        {
            for (int k = 0; _active && k < Timeout / Step; k++)
            {
                _phys.Simulate(Step);
                if (k % 24 == 23 && AllAsleep()) _active = false;
            }
            _active = false;
            Sync();
        }

        bool AllAsleep() => _bodies.Values.All(b => b.rb.IsSleeping());

        void Update()
        {
            if (!_active) return;
            _acc = Mathf.Min(_acc + Time.deltaTime, MaxStepsPerFrame * Step);
            _since += Time.deltaTime;
            while (_acc >= Step) { _phys.Simulate(Step); _acc -= Step; }
            Sync();
            if (AllAsleep() || _since > Timeout) _active = false;
        }

        void Sync()
        {
            foreach (var b in _bodies.Values)
                if (b.view != null) b.view.SetLocalPositionAndRotation(b.go.transform.position, b.go.transform.rotation);
        }

        /// <summary>QA: parts in the bin, how many are at rest, how many have nothing under them (lowered 4 mm,
        /// the part meets neither the floor nor another part pushing it up), the lowest bottom and highest top, and
        /// whether all lie within the walls (bin space).</summary>
        public (int n, int asleep, int air, float low, float high, bool inside) Report(float halfX, float halfZ)
        {
            Physics.SyncTransforms();
            int asleep = 0, air = 0;
            float low = float.MaxValue, high = 0;
            bool inside = true;
            foreach (var b in _bodies.Values)
            {
                if (b.rb.IsSleeping()) asleep++;
                var col = b.go.GetComponent<Collider>();
                var at = b.go.transform.position + Vector3.down * 0.004f;
                bool held = _walls.Concat(_bodies.Values.Where(o => o != b).Select(o => o.go.GetComponent<Collider>())).Any(o =>
                    Physics.ComputePenetration(col, at, b.go.transform.rotation, o, o.transform.position, o.transform.rotation, out var dir, out _) && dir.y > 0.2f);
                if (!held) air++;
                var bb = col.bounds;
                low = Mathf.Min(low, bb.min.y);
                high = Mathf.Max(high, bb.max.y);
                if (bb.min.x < -halfX - 0.01f || bb.max.x > halfX + 0.01f || bb.min.z < -halfZ - 0.01f || bb.max.z > halfZ + 0.01f || bb.min.y < -0.01f) inside = false;
            }
            return (_bodies.Count, asleep, air, _bodies.Count > 0 ? low : 0, high, inside);
        }

        void OnDestroy()
        {
            if (_scene.IsValid()) SceneManager.UnloadSceneAsync(_scene);
        }
    }
}

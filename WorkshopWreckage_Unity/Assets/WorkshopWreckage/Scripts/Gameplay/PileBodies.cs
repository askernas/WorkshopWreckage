using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The physics recipe of a loose part: its convex collider, the shared friction material and its
    /// rigidbody. PileSim and the visual sandbox (VisualSandbox/PileGenerator) both build their bodies here, so a
    /// sandbox pile settles exactly as the workshop's piles do.</summary>
    public static class PileBodies
    {
        static PhysicsMaterial _mat;
        static readonly HashSet<EntityId> _baked = new();

        public static PhysicsMaterial Material => _mat ??= new PhysicsMaterial("parts") { staticFriction = 0.85f, dynamicFriction = 0.7f, bounciness = 0.02f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };

        /// <summary>The part's convex hull at its world scale (go's localScale must already be set).</summary>
        public static MeshCollider AddCollider(GameObject go, ItemDefinition def)
        {
            var mesh = Pegboard.ToolFactory.Has(def.geometryKey) ? Pegboard.ToolFactory.CollisionMesh(def.geometryKey)   // board tools: unbevelled hull
                : MeshLibrary.Get(def.geometryKey, MeshLibrary.LodWorld).mesh;
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            if (_baked.Add(mesh.GetEntityId())) Physics.BakeMesh(mesh.GetEntityId(), true);
            mc.sharedMesh = mesh;
            mc.sharedMaterial = Material;
            var e = ItemDatabase.Extents(def);
            mc.contactOffset = Mathf.Clamp(Mathf.Min(e[0], e[1], e[2]) * 0.08f, 0.0005f, 0.003f);
            return mc;
        }

        public static Rigidbody AddRigidbody(GameObject go, ItemDefinition def)
        {
            var rb = go.AddComponent<Rigidbody>();
            // heavier parts weigh more, but compressed (square root): a stack whose masses differ
            // 100:1 is more than the solver can hold, and the heavy part sinks into the light ones
            rb.mass = Mathf.Sqrt(Mathf.Clamp(def.weightG, 1f, 2000f)) / 100f;
            rb.linearDamping = 0.25f;
            rb.angularDamping = 2.2f;   // rolling resistance: rods and nuts come to rest instead of rolling across the room
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // thin plates must not pass through small parts
            rb.solverIterations = 24;
            rb.solverVelocityIterations = 6;
            rb.maxDepenetrationVelocity = 0.15f;   // parts generated slightly inside each other ease apart, never pop
            rb.sleepThreshold = 0.002f;
            return rb;
        }

        /// <summary>Settling many parts at once (the whole room, a sandbox pile): a coarser solver (a drop in play
        /// keeps the fine one).</summary>
        public static void Bulk(Rigidbody rb)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 10;
            rb.solverVelocityIterations = 2;
            rb.sleepThreshold = 0.006f;
            rb.maxDepenetrationVelocity = 0.3f;
        }
    }
}

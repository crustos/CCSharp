// CCSharp --unity-engine, managed half: the 3D colliders as components that hold their settings.
// ponytail: there is no 3D physics here (no bodies move by these, and no queries see them); they exist for scripts that keep, enable or configure
// them (2DColliderGen's MeshCollider path, Unity's own 3D-or-2D checks).  Upgrade path: a 3D backend beside unity-physics.

using System;

namespace UnityEngine
{
    public class PhysicsMaterial : Object
    {
        public float dynamicFriction = 0.6f;
        public float staticFriction = 0.6f;
        public float bounciness;

        public PhysicsMaterial() { }
        public PhysicsMaterial(string name) { this.name = name; }
    }

    public class Collider : Component
    {
        public bool enabled = true;
        public bool isTrigger;
        public float contactOffset = 0.01f;
        public PhysicsMaterial sharedMaterial;
        public PhysicsMaterial material { get { return sharedMaterial; } set { sharedMaterial = value; } }

        public virtual Bounds bounds { get { return new Bounds(transform.position, Vector3.zero); } }
    }

    public class BoxCollider : Collider
    {
        public Vector3 center;
        public Vector3 size = new Vector3(1f, 1f, 1f);
        public override Bounds bounds { get { return new Bounds(transform.TransformPoint(center), Vector3.Scale(size, transform.lossyScale)); } }
    }

    public class SphereCollider : Collider
    {
        public Vector3 center;
        public float radius = 0.5f;
        public override Bounds bounds { get { float d = 2f * radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)); return new Bounds(transform.TransformPoint(center), new Vector3(d, d, d)); } }
    }

    public class CapsuleCollider : Collider
    {
        public Vector3 center;
        public float radius = 0.5f;
        public float height = 2f;
        public int direction = 1;
    }

    public class MeshCollider : Collider
    {
        public Mesh sharedMesh;
        public bool convex;
    }
}

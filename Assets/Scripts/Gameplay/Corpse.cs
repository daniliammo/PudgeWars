using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The body of a hero that went over the pit edge (or died): a copy of its model in the pose of that moment, made a
    /// ragdoll — belly, chest, head, arms and legs on joints — that flies on with the hero's speed, hits the pit's walls
    /// and floor and rolls down them; hard hits splash blood (<see cref="BloodSplatter"/>) and where the body comes to
    /// rest a pool spreads. The hero itself is hidden meanwhile; at its respawn the body sinks into the ground. Only for
    /// show: every machine of a network game drops its own, from what it saw of the hero.
    /// </summary>
    public class Corpse : MonoBehaviour
    {
        [Tooltip("A hit at least this fast splashes blood, m/s (a fall to the pit floor is about 15).")]
        public float bloodSpeed = 3f;
        [Tooltip("A hit this fast splashes the most.")]
        public float fullBloodSpeed = 14f;
        [Tooltip("Seconds between two splashes of the same body.")]
        public float splashInterval = 0.1f;
        [Tooltip("Size of the pool spreading under the body at rest, metres.")]
        public float poolSize = 1.6f;
        [Tooltip("Seconds the body takes to sink at the respawn.")]
        public float sinkTime = 0.8f;
        [Tooltip("A body nobody removed is gone after this many seconds.")]
        public float maxLifetime = 120f;

        // the Pudge skeleton's bones that get a body; sizes below are for a hero 1.75 m tall (the original's)
        const string PelvisBone = "root", ChestBone = "spine3", NeckBone = "neck1", HeadBone = "head";
        const float ModelSpine = 0.562f; // root to spine3 in the original, metres

        /// <summary>The body's head (its pelvis, or the body itself, if the skeleton has none): the camera follows it.</summary>
        public Transform Head { get; private set; }
        /// <summary>The head's place over the hero's feet when the body took its place (where things over the hero sat).</summary>
        public Vector3 HeadOffset { get; private set; }

        readonly List<Rigidbody> bodies = new();
        Rigidbody pelvis;
        float extraGravity;
        float nextSplash;
        float restingFor;
        bool pooled, sinking;
        static PhysicsMaterial material;

        /// <summary>The hero's body starts flying with <paramref name="velocity"/>; null if the hero has no animated model.</summary>
        public static Corpse Spawn(Pudge hero, Vector3 velocity, float gravity)
        {
            var animator = hero.GetComponentInChildren<Animator>();
            if (animator == null) return null;
            var model = animator.transform;
            var copy = Instantiate(model.gameObject, model.position, model.rotation);
            copy.name = hero.name + " (body)";
            copy.transform.localScale = model.lossyScale;
            var copiedAnimator = copy.GetComponent<Animator>();
            copiedAnimator.enabled = false; // the pose stays as it was copied; physics moves the bones from here
            Destroy(copiedAnimator);
            foreach (var skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>())
                skin.updateWhenOffscreen = true; // the bones fly away from the copy's origin: its bounds must follow them
            var corpse = copy.AddComponent<Corpse>();
            corpse.Build(velocity, gravity);
            corpse.HeadOffset = corpse.Head.position - hero.transform.position;
            return corpse;
        }

        /// <summary>The hero is back: the body sinks into the ground and is gone.</summary>
        public void Sink()
        {
            if (sinking) return;
            sinking = true;
            if (!isActiveAndEnabled)
            {
                Destroy(gameObject);
                return;
            }
            StartCoroutine(SinkAndGo());
        }

        void Build(Vector3 velocity, float gravity)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>()) bones.TryAdd(t.name, t);
            extraGravity = Physics.gravity.magnitude > 0f ? gravity / Physics.gravity.magnitude - 1f : 0f;
            if (material == null)
                material = new PhysicsMaterial("Corpse")
                {
                    dynamicFriction = 0.5f, staticFriction = 0.6f, bounciness = 0.05f,
                    frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum,
                };

            if (!bones.TryGetValue(PelvisBone, out var root) || !bones.TryGetValue(ChestBone, out var chest))
            {
                // not the Pudge skeleton: one rolling capsule for the whole body
                pelvis = AddBody(transform, 100f);
                var capsule = gameObject.AddComponent<CapsuleCollider>();
                capsule.center = Vector3.up * 0.9f;
                capsule.height = 1.8f;
                capsule.radius = 0.5f;
                capsule.sharedMaterial = material;
            }
            else BuildRagdoll(bones, root, chest);
            Head = bones.GetValueOrDefault(HeadBone) ?? (pelvis != null ? pelvis.transform : transform);

            // every part of the body flies as one, turning over forward in its flight
            var flat = new Vector3(velocity.x, 0f, velocity.z);
            var spin = (flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) * Random.Range(2f, 4f) : Vector3.zero)
                       + Random.insideUnitSphere * 1.5f;
            var centre = pelvis.worldCenterOfMass;
            foreach (var body in bodies)
            {
                body.linearVelocity = velocity + Vector3.Cross(spin, body.worldCenterOfMass - centre);
                body.angularVelocity = spin;
            }
            var colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            for (int j = i + 1; j < colliders.Length; j++)
                Physics.IgnoreCollision(colliders[i], colliders[j]); // its own parts overlap: a fat body
            foreach (var body in bodies) body.gameObject.AddComponent<CorpsePart>().corpse = this;
            Destroy(gameObject, maxLifetime);
        }

        void BuildRagdoll(Dictionary<string, Transform> bones, Transform root, Transform chest)
        {
            float s = Vector3.Distance(root.position, chest.position) / ModelSpine;
            var up = (chest.position - root.position).normalized;
            var head = bones.GetValueOrDefault(HeadBone);
            var neck = bones.GetValueOrDefault(NeckBone);
            var ahead = head != null ? Vector3.ProjectOnPlane(head.position - chest.position, up) : Vector3.zero;
            var forward = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.ProjectOnPlane(root.forward, up).normalized;
            var right = Vector3.Cross(up, forward);

            // the belly: a big ball in front of the pelvis; the chest above it, the head forward of the chest
            pelvis = AddBody(root, 35f);
            AddSphere(root, root.position + (up * 0.26f + forward * 0.11f) * s, 0.5f * s);
            var chestBody = AddBody(chest, 25f);
            AddSphere(chest, chest.position + up * 0.17f * s, 0.34f * s);
            Joint(chestBody, pelvis, up, right, -15f, 15f, 20f, 15f);
            if (head != null)
            {
                var headBody = AddBody(head, 6f);
                var centre = neck != null ? head.position + (head.position - neck.position) * 0.8f : head.position + forward * 0.06f * s;
                AddSphere(head, centre, 0.2f * s);
                Joint(headBody, chestBody, up, right, -30f, 30f, 30f, 25f);
            }
            foreach (var side in new[] { "L", "R" })
            {
                Limb(bones, chestBody, $"bicep_{side}", $"elbow_{side}", $"wrist_{side}", 5f, 4f, 0.17f * s, 0.15f * s, 0.35f, forward);
                Limb(bones, pelvis, $"thigh_{side}", $"knee_{side}", $"ankle_{side}", 8f, 5f, 0.2f * s, 0.16f * s, 0.25f, -forward);
            }
        }

        /// <summary>
        /// An arm or a leg: the upper part swings from the body, the lower one bends at the elbow / knee one way only,
        /// toward <paramref name="bendToward"/> (forward for arms, back for legs) as far as it can from the copied pose.
        /// </summary>
        void Limb(Dictionary<string, Transform> bones, Rigidbody parent, string upperName, string lowerName, string endName,
            float upperMass, float lowerMass, float upperRadius, float lowerRadius, float reach, Vector3 bendToward)
        {
            if (!bones.TryGetValue(upperName, out var upper) || !bones.TryGetValue(lowerName, out var lower) ||
                !bones.TryGetValue(endName, out var end)) return;
            var upperDir = lower.position - upper.position;
            var lowerDir = end.position - lower.position;
            var tip = end.position + lowerDir * reach; // the hand / foot
            var upperBody = AddBody(upper, upperMass);
            AddCapsule(upper, upper.position, lower.position, upperRadius);
            var side = Vector3.Cross(upperDir, bendToward);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(upperDir, Vector3.up);
            Joint(upperBody, parent, upperDir, side, -40f, 40f, 70f, 45f);

            var lowerBody = AddBody(lower, lowerMass);
            AddCapsule(lower, lower.position, tip, lowerRadius);
            // turning about upper × lower bends the joint further: a negative twist for the joint (as the knees of Unity's
            // ragdoll wizard: -80..0 about the right axis); straight is the copied bend the other way
            var hinge = Vector3.Cross(upperDir, lowerDir);
            float bent = Vector3.Angle(upperDir, lowerDir);
            if (hinge.sqrMagnitude < 1e-6f || bent < 3f)
            {
                hinge = Vector3.Cross(upperDir, bendToward);
                if (hinge.sqrMagnitude < 1e-6f) hinge = side;
                bent = 0f;
            }
            Joint(lowerBody, upperBody, hinge, lowerDir, -Mathf.Max(0f, 140f - bent), bent, 8f, 8f);
        }

        Rigidbody AddBody(Transform bone, float mass)
        {
            var body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = 10;
            body.maxDepenetrationVelocity = 3f; // a part copied a little into the ground comes out, not flies out
            bodies.Add(body);
            return body;
        }

        static float Scale(Transform t)
        {
            var l = t.lossyScale;
            return Mathf.Max(Mathf.Abs(l.x), Mathf.Abs(l.y), Mathf.Abs(l.z), 1e-6f);
        }

        static void AddSphere(Transform bone, Vector3 centre, float radius)
        {
            var sphere = bone.gameObject.AddComponent<SphereCollider>();
            sphere.center = bone.InverseTransformPoint(centre);
            sphere.radius = radius / Scale(bone);
            sphere.sharedMaterial = material;
        }

        /// <summary>A capsule from <paramref name="a"/> to <paramref name="b"/> (world points), along the bone's nearest axis.</summary>
        static void AddCapsule(Transform bone, Vector3 a, Vector3 b, float radius)
        {
            var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            var local = bone.InverseTransformDirection(b - a);
            var abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            capsule.direction = abs.x >= abs.y && abs.x >= abs.z ? 0 : abs.y >= abs.z ? 1 : 2;
            capsule.center = bone.InverseTransformPoint((a + b) * 0.5f);
            float scale = Scale(bone);
            capsule.radius = radius / scale;
            capsule.height = (Vector3.Distance(a, b) + 2f * radius) / scale;
            capsule.sharedMaterial = material;
        }

        /// <summary>Joins <paramref name="body"/> to <paramref name="parent"/> at its bone; axes are world directions.</summary>
        static void Joint(Rigidbody body, Rigidbody parent, Vector3 twistAxis, Vector3 swingAxis,
            float lowTwist, float highTwist, float swing1, float swing2)
        {
            var joint = body.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parent;
            joint.axis = body.transform.InverseTransformDirection(twistAxis.normalized);
            joint.swingAxis = body.transform.InverseTransformDirection(Vector3.ProjectOnPlane(swingAxis, twistAxis).normalized);
            joint.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
            joint.highTwistLimit = new SoftJointLimit { limit = highTwist };
            joint.swing1Limit = new SoftJointLimit { limit = swing1 };
            joint.swing2Limit = new SoftJointLimit { limit = swing2 };
            joint.enableProjection = true;
            joint.enablePreprocessing = false;
        }

        void FixedUpdate()
        {
            if (sinking) return;
            // heroes fall with their own gravity (Pudge.gravity): the body keeps to it
            if (extraGravity != 0f)
                foreach (var body in bodies)
                    body.AddForce(Physics.gravity * extraGravity, ForceMode.Acceleration);

            if (pooled || pelvis == null) return;
            restingFor = pelvis.linearVelocity.sqrMagnitude < 0.25f ? restingFor + Time.fixedDeltaTime : 0f;
            if (restingFor < 0.4f) return;
            pooled = true;
            BloodSplatter.Pool(pelvis.worldCenterOfMass, poolSize);
        }

        /// <summary>A part of the body hit something.</summary>
        internal void OnImpact(Rigidbody part, Collision collision)
        {
            if (sinking || Time.time < nextSplash || collision.contactCount == 0) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < bloodSpeed) return;
            nextSplash = Time.time + splashInterval;
            var contact = collision.GetContact(0);
            // the side of the surface the body is on, whichever way the contact's normal was given
            var normal = contact.normal;
            if (Vector3.Dot(normal, part.worldCenterOfMass - contact.point) < 0f) normal = -normal;
            var carry = Vector3.ProjectOnPlane(part.linearVelocity, normal);
            BloodSplatter.Splash(contact.point, normal, Mathf.InverseLerp(bloodSpeed, fullBloodSpeed, speed), carry, collision.collider);
        }

        IEnumerator SinkAndGo()
        {
            foreach (var body in bodies)
            {
                if (body == null) continue;
                body.interpolation = RigidbodyInterpolation.None;
                body.isKinematic = true;
            }
            var start = transform.position;
            for (float t = 0f; t < sinkTime; t += Time.deltaTime)
            {
                // kinematic parts follow the copy's root down
                transform.position = start + Vector3.down * (1.2f * t / sinkTime);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}

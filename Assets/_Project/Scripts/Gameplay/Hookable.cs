using System;
using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// A character that can be caught by a hook, dragged, dropped into the pit and killed.
    /// </summary>
    public class Hookable : MonoBehaviour
    {
        static readonly List<Hookable> all = new();
        public static IReadOnlyList<Hookable> All => all;

        public Team team;
        [Tooltip("Collision radius for hook hits, metres (capsule radius 61 cm in the original).")]
        public float radius = 0.6f;
        public float gravity = 20f;

        public bool IsAlive { get; private set; } = true;
        public bool IsHooked => hookedBy != null;
        public bool IsFalling { get; private set; }
        public bool CanBeHooked => IsAlive && !IsHooked && !IsFalling;
        /// <summary>True when the character may move and act on its own.</summary>
        public bool IsControllable => IsAlive && !IsHooked && !IsFalling;
        /// <summary>Horizontal velocity over the last frame, m/s (used by bots to lead their throws).</summary>
        public Vector3 PlanarVelocity { get; private set; }
        /// <summary>Owner of the last hook that caught this character: credited with the kill.</summary>
        public Hookable LastHookedBy { get; private set; }

        public event Action<Hookable> Died;

        HookAbility hookedBy;
        HookAbility ownHook;
        Vector3 velocity, lastPosition;
        PudgeAnimation anim;
        Renderer[] renderers;

        void Awake()
        {
            anim = GetComponent<PudgeAnimation>();
            ownHook = GetComponent<HookAbility>();
            // the hook head and the chain may still be children here (prefab); their visibility belongs to HookAbility
            var all = GetComponentsInChildren<Renderer>(true);
            renderers = ownHook == null ? all : System.Array.FindAll(all, r => !ownHook.Owns(r.transform));
            lastPosition = transform.position;
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public void AttachTo(HookAbility hook)
        {
            hookedBy = hook;
            LastHookedBy = hook.GetComponent<Hookable>();
            // a caught character loses its own hook (and whatever it was pulling)
            if (ownHook != null) ownHook.Cancel();
            anim?.Play(PudgeAnimation.Hooked, 0.05f);
        }

        /// <summary>Lets go of a caught character without killing it (the hook was cancelled).</summary>
        public void Release()
        {
            if (hookedBy == null) return;
            hookedBy = null;
            anim?.Play(PudgeAnimation.Idle);
        }

        /// <summary>Moves the caught character along with the hook head (ground height is kept).</summary>
        public void DragTo(Vector3 position, Vector3 pullDirection)
        {
            transform.position = new Vector3(position.x, transform.position.y, position.z);
            if (pullDirection.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(pullDirection);
        }

        public void StartFalling(Vector3 horizontalVelocity)
        {
            hookedBy = null;
            IsFalling = true;
            velocity = horizontalVelocity;
            if (ownHook != null) ownHook.Cancel();
            anim?.Play(PudgeAnimation.Death, 0.1f);
        }

        void Update()
        {
            if (!IsFalling) return;
            velocity.y -= gravity * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            var arena = Arena.Instance;
            if (arena != null && transform.position.y < arena.killHeight)
                Die();
        }

        void LateUpdate()
        {
            var p = transform.position;
            var v = (p - lastPosition) / Mathf.Max(Time.deltaTime, 1e-5f);
            PlanarVelocity = new Vector3(v.x, 0f, v.z);
            lastPosition = p;
        }

        void Die()
        {
            IsFalling = false;
            IsAlive = false;
            velocity = Vector3.zero;
            SetVisible(false);
            Died?.Invoke(this);
            Arena.Instance?.ReportDeath(this, LastHookedBy);
        }

        public void Respawn(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            lastPosition = position;
            LastHookedBy = null;
            IsAlive = true;
            SetVisible(true);
            anim?.PlayAction(PudgeAnimation.Spawn, 1.2f, 0f);
        }

        void SetVisible(bool visible)
        {
            foreach (var r in renderers) r.enabled = visible;
        }
    }
}

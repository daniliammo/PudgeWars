using System;
using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The hero: a <see cref="Unit"/> that can be caught by a hook, dragged, dropped into the pit and killed. Going over
    /// the edge (or dying) it is hidden and its body (<see cref="Corpse"/>, a ragdoll) flies on in its place, rolls in
    /// the pit and stays there until the respawn.
    /// </summary>
    public class Pudge : Unit
    {
        static readonly List<Pudge> all = new();
        /// <summary>Every hero on the arena.</summary>
        public static IReadOnlyList<Pudge> All => all;

        public float gravity = 20f;
        [Tooltip("Team marker under the feet: allies of the local player (and the player) in this colour.")]
        public Color allyMarkerColor = new(0.2f, 0.9f, 0.3f);
        public Color enemyMarkerColor = new(0.95f, 0.15f, 0.1f);

        public bool IsHooked => hookedBy != null || remoteHooked;
        public bool IsFalling { get; private set; }
        public bool CanBeHooked => IsAlive && !IsHooked && !IsFalling;
        /// <summary>True when the character may move and act on its own.</summary>
        public bool IsControllable => IsAlive && !IsHooked && !IsFalling;
        /// <summary>Horizontal velocity over the last frame, m/s (used by bots to lead their throws).</summary>
        public Vector3 PlanarVelocity { get; private set; }
        /// <summary>Velocity over the last frame, m/s.</summary>
        public Vector3 Velocity { get; private set; }
        /// <summary>The body left by the fall or the death, until the respawn.</summary>
        public Corpse Body => corpse;
        /// <summary>Owner of the last hook that caught this character: credited with the kill.</summary>
        public Pudge LastHookedBy { get; private set; }
        /// <summary>The hook pulling this character now (server / offline; null on a network client).</summary>
        public HookAbility HookedBy => hookedBy;
        /// <summary>An ally's hook holding this character while an enemy's hook pulls it (tug of war).</summary>
        public HookAbility HeldBy { get; internal set; }

        public event Action<Pudge> Died;
        /// <summary>Back in the game at a new place (the network sync teleports instead of interpolating).</summary>
        public event Action<Pudge> Respawned;

        HookAbility hookedBy;
        HookAbility ownHook;
        Vector3 velocity, lastPosition;
        PudgeAnimation anim;
        Renderer[] renderers;
        bool remoteHooked;
        Renderer marker;
        MaterialPropertyBlock markerColor;
        bool? markerAlly;
        Corpse corpse;
        bool visible = true;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        protected override void Awake()
        {
            base.Awake();
            anim = GetComponent<PudgeAnimation>();
            ownHook = GetComponent<HookAbility>();
            // the hook head and the chain may still be children here (prefab); their visibility belongs to HookAbility,
            // the aim lines' to HookAimIndicator: respawn must not switch them back on
            var aim = GetComponentInChildren<HookAimIndicator>(true);
            renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true),
                r => (ownHook == null || !ownHook.Owns(r.transform)) && (aim == null || !r.transform.IsChildOf(aim.transform)));
            lastPosition = transform.position;
            var markerTransform = transform.Find("TeamMarker");
            if (markerTransform != null) marker = markerTransform.GetComponent<Renderer>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            all.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            all.Remove(this);
        }

        void OnDestroy() => RemoveBody();

        public void AttachTo(HookAbility hook)
        {
            hookedBy = hook;
            LastHookedBy = hook.GetComponent<Pudge>();
            // a caught character's own hook breaks off, or keeps its target on a taut chain
            if (ownHook != null) ownHook.OwnerLostControl();
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
            if (ownHook != null) ownHook.OwnerLostControl();
            anim?.Play(PudgeAnimation.Death, 0.1f);
            DropBody(velocity);
        }

        /// <summary>The character walked over the pit edge by itself: it falls, and nobody gets the kill.</summary>
        public void StepIntoPit(Vector3 horizontalVelocity)
        {
            LastHookedBy = null;
            StartFalling(horizontalVelocity);
        }

        protected override void Update()
        {
            base.Update();
            if (!IsFalling || Simulation.IsProxy) return;
            velocity.y -= gravity * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            var arena = Arena.Instance;
            if (arena != null && transform.position.y < arena.killHeight)
                Die();
        }

        void LateUpdate()
        {
            ColorMarker();
            var p = transform.position;
            var v = (p - lastPosition) / Mathf.Max(Time.deltaTime, 1e-5f);
            Velocity = v;
            PlanarVelocity = new Vector3(v.x, 0f, v.z);
            lastPosition = p;
        }

        /// <summary>
        /// The marker shows the side relative to the local player: green for its team, red for the enemy (the
        /// prefab's own colour only says player or bot). Recoloured when the teams change (network game).
        /// </summary>
        void ColorMarker()
        {
            bool ally = team == LocalPlayer.Team;
            if (marker == null || markerAlly == ally) return;
            markerAlly = ally;
            markerColor ??= new MaterialPropertyBlock();
            marker.GetPropertyBlock(markerColor);
            markerColor.SetColor(BaseColor, ally ? allyMarkerColor : enemyMarkerColor);
            marker.SetPropertyBlock(markerColor);
        }

        /// <summary>
        /// Killed by damage (a hook): the body flies on the way the hero was moving. The kill goes to the enemy that
        /// dealt it, else to the last enemy whose hook caught the hero (an ally's hook is nobody's kill).
        /// </summary>
        protected override void OnHealthDepleted(Unit source)
        {
            if (source is Pudge killer && killer.team != team) LastHookedBy = killer;
            hookedBy = null;
            velocity = Velocity;
            anim?.Play(PudgeAnimation.Death, 0.1f);
            Die();
            // its own hook goes on as a dead owner's does (a miss comes back, a catch hangs on a taut chain)
            if (ownHook != null) ownHook.OwnerLostControl();
        }

        void Die()
        {
            IsFalling = false;
            IsAlive = false;
            ClearHealth();
            DropBody(velocity);
            velocity = Vector3.zero;
            Died?.Invoke(this);
            Arena.Instance?.ReportDeath(this, LastHookedBy);
        }

        public void Respawn(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            lastPosition = position;
            LastHookedBy = null;
            IsAlive = true;
            RestoreHealth();
            // a chain still hanging from the dead hero (its hook flew on or held a target) breaks at respawn
            if (ownHook != null) ownHook.Cancel();
            RemoveBody();
            SetVisible(true);
            anim?.PlayAction(PudgeAnimation.Spawn, 1.2f, 0f);
            Respawned?.Invoke(this);
        }

        /// <summary>Network client: the state the server simulates (the position comes with the transform sync).</summary>
        public void ApplyRemoteState(bool alive, bool falling, bool hooked)
        {
            bool wasAlive = IsAlive, wasFalling = IsFalling;
            IsAlive = alive;
            IsFalling = falling;
            remoteHooked = hooked;
            if (!alive) ClearHealth();
            else if (!wasAlive) RestoreHealth();
            // the body drops where the server's hero went over the edge (or died), as fast as it was seen moving
            if ((alive && falling && !wasFalling) || (!alive && wasAlive)) DropBody(Velocity);
            if (alive == wasAlive) return;
            if (alive)
            {
                RemoveBody();
                SetVisible(true);
                lastPosition = transform.position;
            }
            else Died?.Invoke(this);
        }

        /// <summary>The character is hidden, its body flies on with <paramref name="bodyVelocity"/> (once per death).</summary>
        void DropBody(Vector3 bodyVelocity)
        {
            if (corpse == null && visible) corpse = Corpse.Spawn(this, bodyVelocity, gravity);
            SetVisible(false);
        }

        void RemoveBody()
        {
            if (corpse != null) corpse.Sink();
            corpse = null;
        }

        void SetVisible(bool show)
        {
            visible = show;
            foreach (var r in renderers) r.enabled = show;
        }
    }
}

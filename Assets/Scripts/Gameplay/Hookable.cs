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
        [Tooltip("Name shown over the head and in announcements; the object name if empty.")]
        public string nickname;
        [Tooltip("Dota 2 Pudge at level 1: about 670.")]
        public float maxHealth = 670f;
        [Tooltip("Collision radius for hook hits, metres (capsule radius 61 cm in the original).")]
        public float radius = 0.6f;
        public float gravity = 20f;
        [Tooltip("Team marker under the feet: allies of the local player (and the player) in this colour.")]
        public Color allyMarkerColor = new(0.2f, 0.9f, 0.3f);
        public Color enemyMarkerColor = new(0.95f, 0.15f, 0.1f);

        public const string BotPrefix = "Бот ";

        public string Nickname => string.IsNullOrEmpty(nickname) ? name : nickname;
        /// <summary>True for a hero the computer plays (it has a <see cref="BotBrain"/>, on every machine).</summary>
        public bool IsBot { get; private set; }
        /// <summary>Name shown over the head: bots are marked, "Бот Мясник".</summary>
        public string DisplayName => IsBot ? BotPrefix + Nickname : Nickname;
        /// <summary>Hero level; a stub until levels exist: always 1.</summary>
        public int Level => 1;
        public float Health { get; private set; }
        public bool IsAlive { get; private set; } = true;
        public bool IsHooked => hookedBy != null || remoteHooked;
        public bool IsFalling { get; private set; }
        public bool CanBeHooked => IsAlive && !IsHooked && !IsFalling;
        /// <summary>True when the character may move and act on its own.</summary>
        public bool IsControllable => IsAlive && !IsHooked && !IsFalling;
        /// <summary>Horizontal velocity over the last frame, m/s (used by bots to lead their throws).</summary>
        public Vector3 PlanarVelocity { get; private set; }
        /// <summary>Owner of the last hook that caught this character: credited with the kill.</summary>
        public Hookable LastHookedBy { get; private set; }
        /// <summary>The hook pulling this character now (server / offline; null on a network client).</summary>
        public HookAbility HookedBy => hookedBy;
        /// <summary>An ally's hook holding this character while an enemy's hook pulls it (tug of war).</summary>
        public HookAbility HeldBy { get; internal set; }

        public event Action<Hookable> Died;
        /// <summary>Back in the game at a new place (the network sync teleports instead of interpolating).</summary>
        public event Action<Hookable> Respawned;

        HookAbility hookedBy;
        HookAbility ownHook;
        Vector3 velocity, lastPosition;
        PudgeAnimation anim;
        Renderer[] renderers;
        bool remoteHooked;
        Renderer marker;
        MaterialPropertyBlock markerColor;
        bool? markerAlly;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            anim = GetComponent<PudgeAnimation>();
            ownHook = GetComponent<HookAbility>();
            IsBot = GetComponent<BotBrain>() != null;
            // the hook head and the chain may still be children here (prefab); their visibility belongs to HookAbility,
            // the aim lines' to HookAimIndicator: respawn must not switch them back on
            var aim = GetComponentInChildren<HookAimIndicator>(true);
            renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true),
                r => (ownHook == null || !ownHook.Owns(r.transform)) && (aim == null || !r.transform.IsChildOf(aim.transform)));
            lastPosition = transform.position;
            Health = maxHealth;
            var markerTransform = transform.Find("TeamMarker");
            if (markerTransform != null) marker = markerTransform.GetComponent<Renderer>();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public void AttachTo(HookAbility hook)
        {
            hookedBy = hook;
            LastHookedBy = hook.GetComponent<Hookable>();
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
        }

        /// <summary>The character walked over the pit edge by itself: it falls, and nobody gets the kill.</summary>
        public void StepIntoPit(Vector3 horizontalVelocity)
        {
            LastHookedBy = null;
            StartFalling(horizontalVelocity);
        }

        void Update()
        {
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

        void Die()
        {
            IsFalling = false;
            IsAlive = false;
            Health = 0f;
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
            Health = maxHealth;
            // a chain still hanging from the dead hero (its hook flew on or held a target) breaks at respawn
            if (ownHook != null) ownHook.Cancel();
            SetVisible(true);
            anim?.PlayAction(PudgeAnimation.Spawn, 1.2f, 0f);
            Respawned?.Invoke(this);
        }

        /// <summary>Network client: the state the server simulates (the position comes with the transform sync).</summary>
        public void ApplyRemoteState(bool alive, bool falling, bool hooked)
        {
            bool wasAlive = IsAlive;
            IsAlive = alive;
            IsFalling = falling;
            remoteHooked = hooked;
            Health = alive ? maxHealth : 0f;
            if (alive == wasAlive) return;
            SetVisible(alive);
            if (alive) lastPosition = transform.position;
            else Died?.Invoke(this);
        }

        void SetVisible(bool visible)
        {
            foreach (var r in renderers) r.enabled = visible;
        }
    }
}

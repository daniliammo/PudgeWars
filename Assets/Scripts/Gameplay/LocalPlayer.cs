using System;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The hero this machine controls: the one with a <see cref="PlayerInputController"/>. Offline it is the arena's
    /// player from the start; in a network game it appears when the server spawns this player's hero, so the HUD,
    /// the camera and the shop follow <see cref="Changed"/> instead of looking the player up once.
    /// </summary>
    public static class LocalPlayer
    {
        public static PlayerInputController Input { get; private set; }
        public static Hookable Hero { get; private set; }
        /// <summary>The local player's side; <see cref="Team.Player"/> while there is no local hero.</summary>
        public static Team Team => Hero != null ? Hero.team : Team.Player;
        public static event Action Changed;

        internal static void Register(PlayerInputController input)
        {
            Input = input;
            Hero = input.GetComponent<Hookable>();
            Changed?.Invoke();
        }

        internal static void Unregister(PlayerInputController input)
        {
            if (Input != input) return;
            Input = null;
            Hero = null;
            Changed?.Invoke();
        }
    }
}

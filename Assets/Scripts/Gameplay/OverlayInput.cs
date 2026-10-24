namespace PudgeWars.Gameplay
{
    /// <summary>
    /// An overlay window (the network menu) has the keyboard and the mouse: while it is open, typing a nickname
    /// or an address must not move the hero, cast, open the shop or switch the camera.
    /// </summary>
    public static class OverlayInput
    {
        public static bool Captured { get; set; }
    }
}

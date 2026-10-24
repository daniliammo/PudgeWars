namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Who simulates the match. Offline and on the server (host) the game runs here as usual; on a pure network
    /// client the server simulates and this copy is a proxy: it only shows the state it receives and sends the
    /// local player's input (movement, casts, purchases) to the server.
    /// </summary>
    public static class Simulation
    {
        /// <summary>True on a network client that is not also the server.</summary>
        public static bool IsProxy { get; set; }
    }
}

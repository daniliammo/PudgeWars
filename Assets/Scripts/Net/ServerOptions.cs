using System.Globalization;

namespace PudgeWars.Net
{
    /// <summary>Validated dedicated-server arguments, independent of Unity's own command-line flags.</summary>
    public sealed class ServerOptions
    {
        public ushort Port { get; private set; } = 7777;
        public int TeamSize { get; private set; } = 3;
        public bool Bots { get; private set; } = true;
        public float BotDifficulty { get; private set; } = 1f;

        public static bool TryParse(string[] args, out ServerOptions options, out string error)
        {
            options = new ServerOptions();
            error = null;
            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i];
                if (name != "-port" && name != "-teamSize" && name != "-bots" && name != "-botDifficulty") continue;
                if (++i >= args.Length)
                {
                    error = $"Missing value for {name}.";
                    return false;
                }
                string value = args[i];
                switch (name)
                {
                    case "-port" when ushort.TryParse(value, out var port) && port > 0:
                        options.Port = port;
                        break;
                    case "-teamSize" when int.TryParse(value, out var size) && size >= 1 && size <= 5:
                        options.TeamSize = size;
                        break;
                    case "-bots" when value == "0" || value == "1":
                        options.Bots = value == "1";
                        break;
                    case "-botDifficulty" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var difficulty) && difficulty >= 0.25f && difficulty <= 3f:
                        options.BotDifficulty = difficulty;
                        break;
                    default:
                        error = $"Invalid {name} value '{value}'. Expected: port 1..65535, teamSize 1..5, " +
                            "bots 0|1, botDifficulty 0.25..3 (decimal point).";
                        return false;
                }
            }
            return true;
        }
    }
}

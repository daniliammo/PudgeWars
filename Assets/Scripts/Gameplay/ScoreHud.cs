using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    public class ScoreHud : MonoBehaviour
    {
        public Text label;
        [Tooltip("{0} = kills of the local player's team, {1} = its deaths.")]
        public string format = "Счёт: {0}    Смерти: {1}";

        Arena arena;

        void Start()
        {
            arena = Arena.Instance;
            if (arena != null) arena.ScoreChanged += OnScoreChanged;
            LocalPlayer.Changed += Show;
            Show();
        }

        void OnDestroy()
        {
            if (arena != null) arena.ScoreChanged -= OnScoreChanged;
            LocalPlayer.Changed -= Show;
        }

        void OnScoreChanged(int score, int deaths) => Show();

        /// <summary>The score from the local player's side (in a network game it may be the far team).</summary>
        void Show()
        {
            var team = LocalPlayer.Team;
            if (label != null) label.text = string.Format(format, arena != null ? arena.KillsOf(team) : 0, arena != null ? arena.DeathsOf(team) : 0);
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    public class ScoreHud : MonoBehaviour
    {
        public Text label;
        [Tooltip("{0} = bots killed by the player, {1} = player deaths.")]
        public string format = "Счёт: {0}    Смерти: {1}";

        Arena arena;

        void Start()
        {
            arena = Arena.Instance;
            if (arena != null) arena.ScoreChanged += Show;
            Show(arena != null ? arena.Score : 0, arena != null ? arena.Deaths : 0);
        }

        void OnDestroy()
        {
            if (arena != null) arena.ScoreChanged -= Show;
        }

        void Show(int score, int deaths)
        {
            if (label != null) label.text = string.Format(format, score, deaths);
        }
    }
}

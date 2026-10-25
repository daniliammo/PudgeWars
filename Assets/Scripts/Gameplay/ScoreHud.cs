using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Score on the HUD: in the corner the local player's own kills, deaths and rescues, and on a smaller line under
    /// them its team's (the team's bots included); at the top centre the teams' kills as on Dota's top bar — Radiant
    /// green on the left, Dire red on the right, the own team's name in bold. The team line and the top bar are built
    /// at runtime under the same canvas.
    /// </summary>
    public class ScoreHud : MonoBehaviour
    {
        public Text label;
        [Tooltip("{0} = the local player's own kills, {1} = its deaths, {2} = its rescues (У/С/С = убийства / смерти / спасения).")]
        public string format = "У/С/С {0}/{1}/{2}";
        [Tooltip("The same for the local player's whole team, on the smaller line under it.")]
        public string teamFormat = "Команда {0}/{1}/{2}";
        public string radiantName = "RADIANT", direName = "DIRE";
        public Color radiantColor = new(0.35f, 0.85f, 0.3f);
        public Color direColor = new(0.9f, 0.25f, 0.18f);

        /// <summary>The team's kills, deaths and rescues under the own ones.</summary>
        public Text TeamLabel { get; private set; }
        /// <summary>Team kill counts on the top bar.</summary>
        public Text RadiantKills { get; private set; }
        public Text DireKills { get; private set; }

        Arena arena;
        Text radiantTitle, direTitle;

        void Start()
        {
            arena = Arena.Instance;
            BuildTeamBar();
            BuildTeamLabel();
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
            var own = arena != null ? arena.ScoreOf(LocalPlayer.Hero) : default;
            if (label != null) label.text = string.Format(format, own.kills, own.deaths, own.rescues);
            if (TeamLabel != null)
                TeamLabel.text = string.Format(teamFormat, arena != null ? arena.KillsOf(team) : 0, arena != null ? arena.DeathsOf(team) : 0,
                    arena != null ? arena.RescuesOf(team) : 0);
            if (RadiantKills == null) return;
            RadiantKills.text = (arena != null ? arena.KillsOf(Team.Player) : 0).ToString();
            DireKills.text = (arena != null ? arena.KillsOf(Team.Bots) : 0).ToString();
            radiantTitle.text = radiantName;
            direTitle.text = direName;
            MarkOwn(radiantTitle, team == Team.Player);
            MarkOwn(direTitle, team == Team.Bots);
        }

        /// <summary>The own team's name is bold and larger.</summary>
        static void MarkOwn(Text title, bool own)
        {
            title.fontStyle = own ? FontStyle.Bold : FontStyle.Normal;
            title.fontSize = own ? 28 : 22;
        }

        /// <summary>A smaller copy of the corner label right under it.</summary>
        void BuildTeamLabel()
        {
            if (label == null) return;
            var own = label.rectTransform;
            var rt = new GameObject("TeamScoreLine", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(own.parent, false);
            rt.anchorMin = own.anchorMin;
            rt.anchorMax = own.anchorMax;
            rt.pivot = own.pivot;
            rt.sizeDelta = own.sizeDelta;
            rt.anchoredPosition = own.anchoredPosition - new Vector2(0f, label.fontSize * 1.1f);
            TeamLabel = rt.gameObject.AddComponent<Text>();
            TeamLabel.font = label.font;
            TeamLabel.fontSize = Mathf.RoundToInt(label.fontSize * 0.6f);
            TeamLabel.alignment = label.alignment;
            TeamLabel.color = new Color(0.82f, 0.82f, 0.78f);
            TeamLabel.raycastTarget = false;
            TeamLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        void BuildTeamBar()
        {
            var font = label != null ? label.font : null;
            var bar = new GameObject("TeamScore", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(label != null ? label.canvas.transform : transform, false);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = new Vector2(0f, -10f);
            bar.sizeDelta = new Vector2(520f, 76f);
            var bg = bar.gameObject.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.08f, 0.8f);
            bg.raycastTarget = false;

            radiantTitle = Make(bar, "RadiantName", font, 24, new Vector2(-170f, 0f), new Vector2(170f, 40f), TextAnchor.MiddleCenter, radiantColor);
            RadiantKills = Make(bar, "RadiantKills", font, 52, new Vector2(-50f, 0f), new Vector2(90f, 70f), TextAnchor.MiddleRight, radiantColor);
            Make(bar, "Colon", font, 44, Vector2.zero, new Vector2(20f, 70f), TextAnchor.MiddleCenter, Color.white).text = ":";
            DireKills = Make(bar, "DireKills", font, 52, new Vector2(50f, 0f), new Vector2(90f, 70f), TextAnchor.MiddleLeft, direColor);
            direTitle = Make(bar, "DireName", font, 24, new Vector2(170f, 0f), new Vector2(170f, 40f), TextAnchor.MiddleCenter, direColor);
        }

        static Text Make(RectTransform parent, string name, Font font, int size, Vector2 position, Vector2 box, TextAnchor align, Color color)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchoredPosition = position;
            rt.sizeDelta = box;
            var text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            rt.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
            return text;
        }
    }
}

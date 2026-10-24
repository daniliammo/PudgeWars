using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>One cell of the ability bar.</summary>
    public class AbilitySlotView : MonoBehaviour
    {
        public Image frame;
        public Image icon;
        [Tooltip("Radial filled image darkening the icon in proportion to the remaining cooldown.")]
        public Image cooldownOverlay;
        public Text cooldownLabel;
        public Text keyLabel;
        public Button button;
    }
}

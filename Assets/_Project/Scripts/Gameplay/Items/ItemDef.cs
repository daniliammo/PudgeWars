using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>A shop item and the stats it gives its owner.</summary>
    [CreateAssetMenu(menuName = "Pudge Wars/Item", fileName = "Item")]
    public class ItemDef : ScriptableObject
    {
        public string displayName;
        [TextArea] public string description;
        public Sprite icon;
        public int cost;
        [Tooltip("Bonus to ability range, metres (Dota units / 100).")]
        public float castRangeBonus;
        [Tooltip("A hero can own only one (Dota: cast range bonuses of the same item do not stack).")]
        public bool unique = true;
    }
}

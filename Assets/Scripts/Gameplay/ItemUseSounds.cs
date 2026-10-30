using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Dota's sounds of items being used, wherever a hero uses one (<see cref="HeroInventory.AnyUsed"/>, on a network
    /// client too): Tango eating a tree (Resources/Audio/Items/tango), Enchanted Mango eaten (mango), Quelling Blade
    /// chopping (Audio/Trees/chop_NN, one of them at random). The tree's own fall and a planted tree's growth are
    /// <see cref="TerrainTrees"/>' sounds; Arcane Boots' is <see cref="ManaBurst"/>'.
    /// </summary>
    public class ItemUseSounds : MonoBehaviour
    {
        static ItemUseSounds instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (instance != null) return;
            var go = new GameObject("ItemUseSounds");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ItemUseSounds>();
        }

        void OnEnable() => HeroInventory.AnyUsed += OnUsed;

        void OnDisable() => HeroInventory.AnyUsed -= OnUsed;

        static void OnUsed(HeroInventory owner, ItemDef item, Unit target)
        {
            var use = item != null ? item.Active : null;
            if (owner == null || use == null) return;
            var at = owner.transform.position + Vector3.up;
            switch (use.action)
            {
                case ItemAction.EatTree:
                    WorldAudio.PlayAt("Audio/Items/tango", at);
                    break;
                case ItemAction.RestoreMana: // where the one fed eats it
                    WorldAudio.PlayAt("Audio/Items/mango", target != null ? target.transform.position + Vector3.up : at);
                    break;
                case ItemAction.ChopTree:
                    WorldAudio.PlayAt($"Audio/Trees/chop_0{Random.Range(1, 4)}", at);
                    break;
            }
        }
    }
}

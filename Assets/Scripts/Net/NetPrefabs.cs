using PudgeWars.Gameplay;
using UnityEngine;

namespace PudgeWars.Net
{
    /// <summary>
    /// What a network game is made of (Resources/NetPrefabs): the hero prefabs the server spawns for players and
    /// bots, the shop catalog (items travel over the network as indices into it) and the bots' nicknames.
    /// </summary>
    [CreateAssetMenu(menuName = "Pudge Wars/Network Prefabs", fileName = "NetPrefabs")]
    public class NetPrefabs : ScriptableObject
    {
        public const string ResourcePath = "NetPrefabs";

        public GameObject playerHero;
        public GameObject botHero;
        public ItemDef[] catalog = new ItemDef[0];
        public string[] botNames = new string[0];

        public static NetPrefabs Load() => Resources.Load<NetPrefabs>(ResourcePath);

        public int IndexOf(ItemDef item) => System.Array.IndexOf(catalog, item);

        public ItemDef ItemAt(int index) => index >= 0 && index < catalog.Length ? catalog[index] : null;
    }
}

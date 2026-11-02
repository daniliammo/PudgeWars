using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>Explicit build references for runtime SDF fonts and both mobile shader outline variants.</summary>
    public sealed class UiFontLibrary : ScriptableObject
    {
        public Font latin, cyrillic;
        public Material plain, outlined;
    }
}

using System;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>Shared server detection; batch-mode editor tests remain normal game sessions.</summary>
    public static class ServerRuntime
    {
        public static bool IsDedicated
        {
            get
            {
#if UNITY_SERVER && !UNITY_EDITOR
                return true;
#else
                return !Application.isEditor && (Application.isBatchMode ||
                    Array.IndexOf(Environment.GetCommandLineArgs(), "-server") >= 0);
#endif
            }
        }
    }
}

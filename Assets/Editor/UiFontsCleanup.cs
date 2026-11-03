using PudgeWars.Gameplay;
using UnityEditor;

namespace PudgeWars.EditorTools
{
    [InitializeOnLoad]
    public static class UiFontsCleanup
    {
        static UiFontsCleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload += UiFonts.Clear;
            EditorApplication.quitting += UiFonts.Clear;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) UiFonts.ResetLoadedLabels();
            };
        }
    }
}

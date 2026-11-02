using PudgeWars.Gameplay;
using UnityEditor;

namespace PudgeWars.EditorTools
{
    // The saved fields deliberately match the original Text inspector, while rendering uses TMP.
    [CustomEditor(typeof(UiText)), CanEditMultipleObjects]
    public sealed class UiTextEditor : UnityEditor.UI.TextEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            foreach (UiText label in targets) label.ApplyAuthoringSettings();
        }
    }
}

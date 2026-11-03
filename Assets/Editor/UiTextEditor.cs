using PudgeWars.Gameplay;
using UnityEditor;

namespace PudgeWars.EditorTools
{
    // The saved fields deliberately match the original Text inspector, while rendering uses TMP.
    [CustomEditor(typeof(UiText)), CanEditMultipleObjects]
    public sealed class UiTextEditor : UnityEditor.UI.GraphicEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Text"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_FontData"));
            // Graphic's legacy m_Color is not the color used by TMP's glyph vertices.
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_fontColor"), new UnityEngine.GUIContent("Color"));
            RaycastControlsGUI();
            MaskableControlsGUI();
            serializedObject.ApplyModifiedProperties();
            foreach (UiText label in targets) label.ApplyAuthoringSettings();
        }
    }
}

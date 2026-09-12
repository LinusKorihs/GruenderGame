using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(LevelFlowStep))]
public sealed class LevelFlowStepDrawer : PropertyDrawer
{
    private const float Gap = 2f;
    private static readonly GUIContent[] SceneModeTabs =
    {
        new GUIContent("Load Scene"),
        new GUIContent("Runtime Scene")
    };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded)
            return EditorGUIUtility.singleLineHeight + Gap;

        int lines = 8;
        SerializedProperty sceneMode = property.FindPropertyRelative("sceneMode");
        if (sceneMode != null && sceneMode.enumValueIndex == (int)LevelFlowSceneMode.RuntimeScene)
        {
            lines++;
        }

        return EditorGUIUtility.singleLineHeight * lines + Gap * (lines + 1);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Rect line = NextLine(ref position);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;
        DrawProperty(ref position, property, "stepId");
        DrawProperty(ref position, property, "displayName");
        DrawProperty(ref position, property, "stepType");

        SerializedProperty sceneMode = property.FindPropertyRelative("sceneMode");
        Rect tabsRect = NextLine(ref position);
        sceneMode.enumValueIndex = GUI.Toolbar(tabsRect, sceneMode.enumValueIndex, SceneModeTabs);

        if (sceneMode.enumValueIndex == (int)LevelFlowSceneMode.RuntimeScene)
        {
            DrawProperty(ref position, property, "runtimeSceneName");
            DrawProperty(ref position, property, "unloadPreviousRuntimeScene");
            DrawProperty(ref position, property, "sceneName", "Fallback Scene Name");
        }
        else
        {
            DrawProperty(ref position, property, "sceneName");
        }

        DrawProperty(ref position, property, "levelProfile");
        DrawProperty(ref position, property, "staticLayoutProfile");
        EditorGUI.indentLevel--;

        EditorGUI.EndProperty();
    }

    private static void DrawProperty(ref Rect position, SerializedProperty parent, string propertyName, string label = null)
    {
        SerializedProperty child = parent.FindPropertyRelative(propertyName);
        if (child == null)
            return;

        Rect line = NextLine(ref position);
        GUIContent content = label == null ? new GUIContent(child.displayName) : new GUIContent(label);
        EditorGUI.PropertyField(line, child, content, true);
    }

    private static Rect NextLine(ref Rect position)
    {
        Rect line = new Rect(
            position.x,
            position.y,
            position.width,
            EditorGUIUtility.singleLineHeight);

        position.y += EditorGUIUtility.singleLineHeight + Gap;
        return line;
    }
}

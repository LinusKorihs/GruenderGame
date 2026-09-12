using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(LevelFlowStep))]
public sealed class LevelFlowStepDrawer : PropertyDrawer
{
    private const float Gap = 4f;
    private static readonly GUIContent[] SceneModeTabs =
    {
        new GUIContent("Load Scene"),
        new GUIContent("Runtime Scene")
    };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded)
            return EditorGUIUtility.singleLineHeight + Gap;

        float height = EditorGUIUtility.singleLineHeight + Gap;
        height += PropertyHeight(property, "stepId");
        height += PropertyHeight(property, "displayName");
        height += PropertyHeight(property, "stepType");
        height += EditorGUIUtility.singleLineHeight + Gap;

        SerializedProperty sceneMode = property.FindPropertyRelative("sceneMode");
        if (sceneMode != null && sceneMode.enumValueIndex == (int)LevelFlowSceneMode.RuntimeScene)
        {
            height += PropertyHeight(property, "runtimeSceneName");
            height += PropertyHeight(property, "unloadPreviousRuntimeScene");
            height += PropertyHeight(property, "sceneName");
        }
        else
        {
            height += PropertyHeight(property, "sceneName");
        }

        height += PropertyHeight(property, "levelProfile");
        height += PropertyHeight(property, "staticLayoutProfile");
        height += PropertyHeight(property, "atmosphereProfile");
        return height + Gap;
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
        DrawProperty(ref position, property, "atmosphereProfile");
        EditorGUI.indentLevel--;

        EditorGUI.EndProperty();
    }

    private static void DrawProperty(ref Rect position, SerializedProperty parent, string propertyName, string label = null)
    {
        SerializedProperty child = parent.FindPropertyRelative(propertyName);
        if (child == null)
            return;

        Rect line = NextPropertyRect(ref position, child);
        GUIContent content = label == null ? new GUIContent(child.displayName) : new GUIContent(label);
        EditorGUI.PropertyField(line, child, content, true);
    }

    private static float PropertyHeight(SerializedProperty parent, string propertyName)
    {
        SerializedProperty child = parent.FindPropertyRelative(propertyName);
        return child != null ? EditorGUI.GetPropertyHeight(child, true) + Gap : 0f;
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

    private static Rect NextPropertyRect(ref Rect position, SerializedProperty property)
    {
        float height = EditorGUI.GetPropertyHeight(property, true);
        Rect line = new Rect(position.x, position.y, position.width, height);
        position.y += height + Gap;
        return line;
    }
}

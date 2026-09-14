using TMPro;
using UnityEngine;

public sealed class DialogPanelView : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI dialogueText;

    public GameObject PanelRoot => panelRoot != null ? panelRoot : gameObject;
    public TextMeshProUGUI DialogueText => dialogueText;

    public bool IsValid => PanelRoot != null && dialogueText != null;

    public void SetVisible(bool visible)
    {
        ResolveReferences();

        if (visible)
        {
            ActivateHierarchy(transform);
            PanelRoot.SetActive(true);
            return;
        }

        PanelRoot.SetActive(false);
    }

    public void ResolveReferences()
    {
        if (panelRoot == null)
            panelRoot = FindChildGameObject("DialoguePanel") ?? gameObject;

        if (dialogueText == null)
            dialogueText = FindText("DialogueText") ?? FindText("DIalogueText") ?? GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    private GameObject FindChildGameObject(string objectName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && string.Equals(child.name, objectName, System.StringComparison.OrdinalIgnoreCase))
                return child.gameObject;
        }

        return null;
    }

    private TextMeshProUGUI FindText(string objectName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && string.Equals(child.name, objectName, System.StringComparison.OrdinalIgnoreCase))
                return child.GetComponent<TextMeshProUGUI>();
        }

        return null;
    }

    private static void ActivateHierarchy(Transform target)
    {
        if (target == null)
            return;

        if (target.parent != null)
            ActivateHierarchy(target.parent);

        target.gameObject.SetActive(true);
    }
}

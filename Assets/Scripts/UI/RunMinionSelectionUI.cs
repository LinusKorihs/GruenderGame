using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RunMinionSelectionUI : MonoBehaviour
{
    [Serializable]
    public sealed class RowBinding
    {
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private Button minusButton;
        [SerializeField] private Button plusButton;

        public TMP_Text ValueText => valueText;
        public Button MinusButton => minusButton;
        public Button PlusButton => plusButton;

        public bool IsValid => valueText != null && minusButton != null && plusButton != null;

        public void Resolve(Transform root, string rowName)
        {
            if (root == null || string.IsNullOrWhiteSpace(rowName)) return;

            Transform rowRoot = FindChild(root, rowName + " Row");
            Transform searchRoot = rowRoot != null ? rowRoot : root;

            if (valueText == null)
                valueText = FindComponentByName<TMP_Text>(searchRoot, rowName + " Value");
            if (minusButton == null)
                minusButton = FindComponentByName<Button>(searchRoot, rowName + " Minus");
            if (plusButton == null)
                plusButton = FindComponentByName<Button>(searchRoot, rowName + " Plus");
        }
    }

    [SerializeField] private Canvas canvas;
    [SerializeField] private TMP_Text totalText;
    [SerializeField] private Button startButton;
    [SerializeField] private RowBinding meleeRow = new RowBinding();
    [SerializeField] private RowBinding rangedRow = new RowBinding();
    [SerializeField] private RowBinding supportRow = new RowBinding();

    public Canvas Canvas => canvas;
    public TMP_Text TotalText => totalText;
    public Button StartButton => startButton;
    public RowBinding MeleeRow => meleeRow;
    public RowBinding RangedRow => rangedRow;
    public RowBinding SupportRow => supportRow;

    public bool IsValid =>
        canvas != null &&
        totalText != null &&
        startButton != null &&
        meleeRow != null && meleeRow.IsValid &&
        rangedRow != null && rangedRow.IsValid &&
        supportRow != null && supportRow.IsValid;

    public void SetVisible(bool visible)
    {
        ResolveReferences();

        if (visible)
        {
            ActivateHierarchy(transform);
            if (canvas != null)
                canvas.gameObject.SetActive(true);
            else
                gameObject.SetActive(true);

            // Navigation must be built after the hierarchy is active. Otherwise
            // Selectable.IsActive() rejects every button and all links become null.
            RefreshControllerNavigation();
            return;
        }

        gameObject.SetActive(false);
    }

    public void ResolveReferences()
    {
        if (canvas == null)
            canvas = GetComponentInChildren<Canvas>(true);
        if (totalText == null)
            totalText = FindComponentByName<TMP_Text>(transform, "Total");
        if (startButton == null)
            startButton = FindComponentByName<Button>(transform, "StartButton");

        meleeRow ??= new RowBinding();
        rangedRow ??= new RowBinding();
        supportRow ??= new RowBinding();

        meleeRow.Resolve(transform, "Melee");
        rangedRow.Resolve(transform, "Ranged");
        supportRow.Resolve(transform, "Support");
        RefreshControllerNavigation();
    }

    public void RefreshControllerNavigation()
    {
        if (!IsValid) return;

        ConfigureRow(meleeRow, null, rangedRow, startButton);
        ConfigureRow(rangedRow, meleeRow, supportRow, startButton);
        ConfigureRow(supportRow, rangedRow, null, startButton);

        Navigation startNavigation = startButton.navigation;
        startNavigation.mode = Navigation.Mode.Explicit;
        startNavigation.selectOnUp = FirstInteractable(supportRow.PlusButton, supportRow.MinusButton,
            rangedRow.PlusButton, rangedRow.MinusButton, meleeRow.PlusButton, meleeRow.MinusButton);
        startNavigation.selectOnLeft = FirstInteractable(supportRow.MinusButton, supportRow.PlusButton,
            rangedRow.MinusButton, rangedRow.PlusButton, meleeRow.MinusButton, meleeRow.PlusButton);
        startNavigation.selectOnRight = FirstInteractable(supportRow.PlusButton, supportRow.MinusButton,
            rangedRow.PlusButton, rangedRow.MinusButton, meleeRow.PlusButton, meleeRow.MinusButton);
        startButton.navigation = startNavigation;

        SetDown(supportRow.MinusButton, startButton);
        SetDown(supportRow.PlusButton, startButton);
    }

    private static void ConfigureRow(RowBinding row, RowBinding upper, RowBinding lower, Button startButton)
    {
        SetNavigation(
            row.MinusButton,
            FirstInteractable(upper?.MinusButton, upper?.PlusButton),
            FirstInteractable(lower?.MinusButton, lower?.PlusButton, startButton),
            null,
            FirstInteractable(row.PlusButton));
        SetNavigation(
            row.PlusButton,
            FirstInteractable(upper?.PlusButton, upper?.MinusButton),
            FirstInteractable(lower?.PlusButton, lower?.MinusButton, startButton),
            FirstInteractable(row.MinusButton),
            null);
    }

    private static Selectable FirstInteractable(params Selectable[] candidates)
    {
        if (candidates == null) return null;
        for (int i = 0; i < candidates.Length; i++)
        {
            Selectable candidate = candidates[i];
            if (candidate != null && candidate.IsActive() && candidate.IsInteractable())
                return candidate;
        }
        return null;
    }

    private static void SetNavigation(Selectable selectable, Selectable up, Selectable down, Selectable left, Selectable right)
    {
        Navigation navigation = selectable.navigation;
        navigation.mode = Navigation.Mode.Explicit;
        navigation.selectOnUp = up;
        navigation.selectOnDown = down;
        navigation.selectOnLeft = left;
        navigation.selectOnRight = right;
        selectable.navigation = navigation;
    }

    private static void SetDown(Selectable selectable, Selectable down)
    {
        Navigation navigation = selectable.navigation;
        navigation.selectOnDown = down;
        selectable.navigation = navigation;
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    private static T FindComponentByName<T>(Transform root, string objectName) where T : Component
    {
        Transform target = FindChild(root, objectName);
        return target != null ? target.GetComponent<T>() : null;
    }

    private static Transform FindChild(Transform root, string objectName)
    {
        if (root == null || string.IsNullOrWhiteSpace(objectName))
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && string.Equals(child.name, objectName, StringComparison.OrdinalIgnoreCase))
                return child;
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

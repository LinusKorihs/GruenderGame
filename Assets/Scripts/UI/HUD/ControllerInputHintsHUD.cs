using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ControllerInputHintsHUD : MonoBehaviour
{
    private static ControllerInputHintsHUD instance;
    private enum ActiveInputDevice
    {
        KeyboardMouse,
        Gamepad
    }

    [Header("Visibility")]
    [SerializeField] private bool showInputHints = false;
    [SerializeField] private bool showKeyboardHints = true;
    [SerializeField] private bool showGamepadHints = true;
    [SerializeField, Min(0.1f)] private float inputSwitchCooldown = 0.2f;

    [Header("Layout")]
    [SerializeField] private Vector2 anchoredPosition = new Vector2(-28f, -48f);
    [SerializeField] private float panelWidth = 330f;
    [SerializeField] private Color panelColor = new Color(0.06f, 0.075f, 0.09f, 0.82f);
    [SerializeField] private Color primaryTextColor = new Color(0.94f, 0.96f, 0.98f, 1f);
    [SerializeField] private Color secondaryTextColor = new Color(0.67f, 0.72f, 0.78f, 1f);
    [SerializeField] private Color gamepadAccentColor = new Color(0.33f, 0.78f, 1f, 1f);
    [SerializeField] private Color keyboardAccentColor = new Color(0.93f, 0.78f, 0.35f, 1f);

    private RectTransform panel;
    private TMP_Text titleText;
    private TMP_Text hintsText;
    private ActiveInputDevice activeDevice;
    private float nextDeviceSwitchTime;

    private void Awake()
    {
        instance = this;
        BuildUI();
        activeDevice = Gamepad.current != null && Keyboard.current == null
            ? ActiveInputDevice.Gamepad
            : ActiveInputDevice.KeyboardMouse;
        Refresh();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public static void ToggleCurrent()
    {
        if (instance == null)
            instance = FindFirstObjectByType<ControllerInputHintsHUD>(FindObjectsInactive.Include);
        if (instance == null) return;

        instance.showInputHints = !instance.showInputHints;
        instance.Refresh();
    }

    public static void HideInputHints()
    {
        if (instance == null)
            instance = FindFirstObjectByType<ControllerInputHintsHUD>(FindObjectsInactive.Include);
        if (instance == null) return;

        if (instance.showInputHints == false)
            return;
        instance.showInputHints = false;
        instance.Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextDeviceSwitchTime)
            return;

        if (HasMeaningfulGamepadInput())
        {
            SetActiveDevice(ActiveInputDevice.Gamepad);
            return;
        }

        if (HasMeaningfulKeyboardMouseInput())
            SetActiveDevice(ActiveInputDevice.KeyboardMouse);
    }

    private void SetActiveDevice(ActiveInputDevice device)
    {
        if (activeDevice == device)
            return;

        activeDevice = device;
        nextDeviceSwitchTime = Time.unscaledTime + inputSwitchCooldown;
        Refresh();
    }

    private void Refresh()
    {
        if (panel == null || titleText == null || hintsText == null)
            return;

        bool visible = showInputHints &&
                       (activeDevice == ActiveInputDevice.Gamepad ? showGamepadHints : showKeyboardHints);
        panel.gameObject.SetActive(visible);
        if (!visible)
            return;

        if (activeDevice == ActiveInputDevice.Gamepad)
        {
            titleText.text = "CONTROLLER";
            titleText.color = gamepadAccentColor;
            hintsText.text =
                Key("LS", gamepadAccentColor) + " Move        " + Key("RS", gamepadAccentColor) + " Cursor\n" +
                Key("B", gamepadAccentColor) + " Command     " + Key("A", gamepadAccentColor) + " Call / Interact\n" +
                Key("Y", gamepadAccentColor) + " Dismiss     " + Key("LB/RB", gamepadAccentColor) + " Minion type\n" +
                Key("X", gamepadAccentColor) + " Dodge       " + Key("LS", gamepadAccentColor) + " Punch\n" +
                Key("RT", gamepadAccentColor) + " Lock on     " + Key("R3", gamepadAccentColor) + " Camera";
        }
        else
        {
            titleText.text = "CONTROLS";
            titleText.color = keyboardAccentColor;
            hintsText.text =
                Key("WASD", keyboardAccentColor) + " Move       " + Key("Mouse", keyboardAccentColor) + " Cursor\n" +
                Key("LMB", keyboardAccentColor) + " Command    " + Key("R", keyboardAccentColor) + " Call\n" +
                Key("T", keyboardAccentColor) + " Dismiss    " + Key("Q / E", keyboardAccentColor) + " Minion type\n" +
                Key("Shift", keyboardAccentColor) + " Dodge     " + Key("Space", keyboardAccentColor) + " Punch\n" +
                Key("MMB", keyboardAccentColor) + " Lock on    " + Key("Tab", keyboardAccentColor) + " Camera";
        }
    }

    private string Key(string label, Color color)
    {
        return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b>[{label}]</b></color>";
    }

    private void BuildUI()
    {
        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            enabled = false;
            return;
        }

        Transform existing = canvas.transform.Find("Input Hints");
        if (existing != null)
        {
            panel = existing as RectTransform;
            titleText = existing.Find("Title")?.GetComponent<TMP_Text>();
            hintsText = existing.Find("Hints")?.GetComponent<TMP_Text>();
            if (panel != null && titleText != null && hintsText != null)
                return;
        }

        GameObject panelObject = new GameObject("Input Hints", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelObject.layer = canvas.gameObject.layer;
        panelObject.transform.SetParent(canvas.transform, false);
        panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = Vector2.one;
        panel.anchorMax = Vector2.one;
        panel.pivot = Vector2.one;
        panel.anchoredPosition = anchoredPosition;
        panel.sizeDelta = new Vector2(panelWidth, 0f);

        Image background = panelObject.GetComponent<Image>();
        background.color = panelColor;
        background.raycastTarget = false;

        VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 13, 15);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = panelObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        titleText = CreateText("Title", panel, 18f, FontStyles.Bold);
        titleText.characterSpacing = 2f;
        titleText.color = gamepadAccentColor;

        hintsText = CreateText("Hints", panel, 17f, FontStyles.Normal);
        hintsText.color = primaryTextColor;
        hintsText.lineSpacing = 11f;
    }

    private TMP_Text CreateText(string objectName, Transform parent, float fontSize, FontStyles fontStyle)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.layer = parent.gameObject.layer;
        textObject.transform.SetParent(parent, false);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.color = secondaryTextColor;

        LayoutElement layout = textObject.GetComponent<LayoutElement>();
        layout.minWidth = panelWidth - 36f;
        return text;
    }

    private static bool HasMeaningfulGamepadInput()
    {
        Gamepad gamepad = Gamepad.current;
        if (gamepad == null)
            return false;

        return gamepad.buttonSouth.wasPressedThisFrame ||
               gamepad.buttonNorth.wasPressedThisFrame ||
               gamepad.buttonEast.wasPressedThisFrame ||
               gamepad.buttonWest.wasPressedThisFrame ||
               gamepad.leftShoulder.wasPressedThisFrame ||
               gamepad.rightShoulder.wasPressedThisFrame ||
               gamepad.leftStickButton.wasPressedThisFrame ||
               gamepad.rightStickButton.wasPressedThisFrame ||
               gamepad.startButton.wasPressedThisFrame ||
               gamepad.selectButton.wasPressedThisFrame ||
               gamepad.dpad.ReadValue().sqrMagnitude > 0.25f ||
               gamepad.leftStick.ReadValue().sqrMagnitude > 0.16f ||
               gamepad.rightStick.ReadValue().sqrMagnitude > 0.16f ||
               gamepad.leftTrigger.ReadValue() > 0.35f ||
               gamepad.rightTrigger.ReadValue() > 0.35f;
    }

    private static bool HasMeaningfulKeyboardMouseInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            return true;

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return false;

        return mouse.leftButton.wasPressedThisFrame ||
               mouse.rightButton.wasPressedThisFrame ||
               mouse.middleButton.wasPressedThisFrame ||
               mouse.scroll.ReadValue().sqrMagnitude > 0.01f ||
               mouse.delta.ReadValue().sqrMagnitude > 4f;
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ControllerMenuNavigation
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeSelectionStyling()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        GameObject backHandler = new GameObject("Controller Menu Back Handler");
        Object.DontDestroyOnLoad(backHandler);
        backHandler.AddComponent<ControllerMenuBackHandler>();
    }

    private sealed class ControllerMenuBackHandler : MonoBehaviour
    {
        private void Update()
        {
            bool cancelPressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                                 (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
            if (!cancelPressed || EventSystem.current == null)
                return;

            GameObject selected = EventSystem.current.currentSelectedGameObject;
            Button button = selected != null ? selected.GetComponent<Button>() : null;
            if (button == null)
                return;

            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) != "ShowMainMenu")
                    continue;

                button.onClick.Invoke();
                return;
            }
        }
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _ = scene;
        _ = mode;
        ApplyVisibleSelectedColors(null);
    }

    public static IEnumerator FocusNextFrame(Transform menuRoot, Selectable preferred = null)
    {
        yield return null;
        Focus(menuRoot, preferred);
    }

    public static void Focus(Transform menuRoot, Selectable preferred = null)
    {
        EnsureEventSystem();
        ApplyVisibleSelectedColors(menuRoot);

        Selectable target = IsUsable(preferred) ? preferred : FindTopCentralSelectable(menuRoot);
        if (target == null || EventSystem.current == null)
            return;

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(target.gameObject);
        target.Select();
    }

    private static void ApplyVisibleSelectedColors(Transform menuRoot)
    {
        Button[] buttons = menuRoot != null
            ? menuRoot.GetComponentsInChildren<Button>(true)
            : Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null) continue;

            ColorBlock colors = button.colors;
            colors.selectedColor = Color.Lerp(colors.normalColor, Color.black, 0.48f);
            colors.highlightedColor = Color.Lerp(colors.normalColor, Color.black, 0.32f);
            colors.pressedColor = Color.Lerp(colors.normalColor, Color.black, 0.62f);
            colors.colorMultiplier = Mathf.Max(1f, colors.colorMultiplier);
            button.colors = colors;
        }
    }

    private static Selectable FindTopCentralSelectable(Transform menuRoot)
    {
        if (menuRoot == null)
            return null;

        Selectable[] candidates = menuRoot.GetComponentsInChildren<Selectable>(true);
        Selectable best = null;
        float bestY = float.NegativeInfinity;
        float bestCenterDistance = float.PositiveInfinity;
        float screenCenterX = Screen.width * 0.5f;

        for (int i = 0; i < candidates.Length; i++)
        {
            Selectable candidate = candidates[i];
            if (!IsUsable(candidate))
                continue;

            Vector3 screenPosition = candidate.transform is RectTransform rect
                ? RectTransformUtility.WorldToScreenPoint(null, rect.position)
                : candidate.transform.position;

            float centerDistance = Mathf.Abs(screenPosition.x - screenCenterX);
            bool clearlyHigher = screenPosition.y > bestY + 4f;
            bool sameRowAndMoreCentral = Mathf.Abs(screenPosition.y - bestY) <= 4f && centerDistance < bestCenterDistance;
            if (!clearlyHigher && !sameRowAndMoreCentral)
                continue;

            best = candidate;
            bestY = screenPosition.y;
            bestCenterDistance = centerDistance;
        }

        return best;
    }

    private static bool IsUsable(Selectable selectable)
    {
        return selectable != null && selectable.IsActive() && selectable.IsInteractable() && selectable.gameObject.activeInHierarchy;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }
}

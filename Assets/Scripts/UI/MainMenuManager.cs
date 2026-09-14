using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems; // Wichtig für Controller-Auswahl!

public class MainMenuManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject controlsPanel;
    [SerializeField] private GameObject creditsPanel;

    [Header("Erste Buttons für Controller-Fokus")]
    [SerializeField] private GameObject startButton;
    [SerializeField] private GameObject controlsBackButton;
    [SerializeField] private GameObject creditsBackButton;

    [Header("Szenen-Name")]
    [SerializeField] private string gameSceneName = "GameScene";

    void Start()
    {
        ShowMainMenu();
    }

    // --- BUTTON-FUNKTIONEN ---

    public void PlayGame()
    {
        // Lädt die eigentliche Spielszene
        SceneManager.LoadScene(gameSceneName);
    }

    public void ShowControls()
    {
        mainMenuPanel.SetActive(false);
        creditsPanel.SetActive(false);
        controlsPanel.SetActive(true);

        // Setzt den Controller-Fokus auf den Zurück-Button
        SetSelected(controlsBackButton);
    }

    public void ShowCredits()
    {
        mainMenuPanel.SetActive(false);
        controlsPanel.SetActive(false);
        creditsPanel.SetActive(true);

        // Setzt den Controller-Fokus auf den Zurück-Button
        SetSelected(creditsBackButton);
    }

    public void ShowMainMenu()
    {
        controlsPanel.SetActive(false);
        creditsPanel.SetActive(false);
        mainMenuPanel.SetActive(true);

        // Setzt den Controller-Fokus zurück auf den Start-Button
        SetSelected(startButton);
    }

    public void QuitGame()
    {
        Debug.Log("Spiel beendet.");
        Application.Quit();
    }

    // Hilfsmethode, damit die Auswahl für Gamepads/Tastatur nicht verloren geht
    private void SetSelected(GameObject buttonToSelect)
    {
        if (EventSystem.current != null && buttonToSelect != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(buttonToSelect);
        }
    }
}
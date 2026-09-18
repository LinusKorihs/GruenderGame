using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class UIButtonSound : MonoBehaviour
{
    [SerializeField, Tooltip("Optional sound played whenever this enabled UI button is clicked or submitted.")]
    private SoundCue clickSound = new SoundCue("UI.Button.Click", 0f);

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (button == null)
            button = GetComponent<Button>();

        button.onClick.AddListener(PlayClickSound);
    }

    private void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(PlayClickSound);
    }

    private void PlayClickSound()
    {
        clickSound.Play(transform);
    }
}

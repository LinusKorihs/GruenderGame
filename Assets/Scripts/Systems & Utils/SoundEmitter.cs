using UnityEngine;

[DisallowMultipleComponent]
public sealed class SoundEmitter : MonoBehaviour
{
    [Header("Sound")]
    [SerializeField, Tooltip("ID from the central Audio Settings ScriptableObject.")]
    private string soundId;
    [SerializeField] private bool playOnEnable;
    public string SoundId => soundId;
    private AudioSource activeSource;

    private void OnEnable()
    {
        if (playOnEnable)
            Play();
    }

    [ContextMenu("Play Sound")]
    public void Play()
    {
        if (SoundManager.Instance != null)
            activeSource = SoundManager.Instance.Play(this);
    }

    public void Stop()
    {
        if (activeSource != null)
            activeSource.Stop();
    }
}

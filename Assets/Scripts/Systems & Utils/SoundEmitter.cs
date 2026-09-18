using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public sealed class SoundEmitter : MonoBehaviour
{
    [Header("Sound")]
    [SerializeField, Tooltip("ID from the central SoundManager library. Used when Clip Override is empty.")]
    private string soundId;
    [SerializeField, Tooltip("Optional local override. Leave empty to use the central Sound ID.")]
    private AudioClip clip;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField, Range(0.1f, 3f)] private float pitch = 1f;
    [SerializeField] private bool loop;
    [SerializeField] private bool playOnEnable;
    [SerializeField] private AudioMixerGroup outputGroup;

    [Header("Position / Strength")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
    [SerializeField, Min(0.01f)] private float minDistance = 1f;
    [SerializeField, Min(0.01f)] private float maxDistance = 25f;
    [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

    public AudioClip Clip => clip;
    public string SoundId => soundId;

    private void OnEnable()
    {
        if (playOnEnable)
            Play();
    }

    [ContextMenu("Play Sound")]
    public void Play()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.Play(this);
    }

    public void Stop()
    {
        AudioSource source = GetComponent<AudioSource>();
        if (source != null)
            source.Stop();
    }

    public void ApplyTo(AudioSource source, AudioClip resolvedClip, AudioMixerGroup fallbackGroup)
    {
        source.clip = resolvedClip;
        source.volume = volume;
        source.pitch = pitch;
        source.loop = loop;
        source.playOnAwake = false;
        source.spatialBlend = spatialBlend;
        source.minDistance = minDistance;
        source.maxDistance = Mathf.Max(minDistance, maxDistance);
        source.rolloffMode = rolloffMode;
        source.outputAudioMixerGroup = outputGroup != null ? outputGroup : fallbackGroup;
    }
}

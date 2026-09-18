using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public sealed class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField, Tooltip("Optional AudioMixer routing for all SFX. Empty routes sounds directly to Unity's Master/AudioListener output.")]
    private AudioMixerGroup defaultSfxGroup;
    [SerializeField] private List<SoundEntry> sounds = new List<SoundEntry>();

    [Serializable]
    private struct SoundEntry
    {
        [SerializeField] public string id;
        [SerializeField] public AudioClip clip;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public AudioSource Play(SoundEmitter emitter)
    {
        if (emitter == null)
            return null;

        AudioClip clip = emitter.Clip != null ? emitter.Clip : ResolveClip(emitter.SoundId);
        if (clip == null)
        {
            Debug.LogWarning($"[SoundManager] No sound found for '{emitter.SoundId}' on {emitter.name}.", emitter);
            return null;
        }

        AudioSource source = emitter.GetComponent<AudioSource>();
        if (source == null)
            source = emitter.gameObject.AddComponent<AudioSource>();

        emitter.ApplyTo(source, clip, defaultSfxGroup);
        source.Play();
        return source;
    }

    public AudioClip ResolveClip(string soundId)
    {
        if (string.IsNullOrWhiteSpace(soundId))
            return null;

        for (int i = 0; i < sounds.Count; i++)
        {
            if (string.Equals(sounds[i].id, soundId, StringComparison.OrdinalIgnoreCase))
                return sounds[i].clip;
        }

        return null;
    }
}

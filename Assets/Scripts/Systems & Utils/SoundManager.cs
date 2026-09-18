using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public sealed class SoundManager : MonoBehaviour
{
    private const string MasterVolumeKey = "Audio.MasterVolume";
    private const string SfxVolumeKey = "Audio.SfxVolume";
    private const string MusicVolumeKey = "Audio.MusicVolume";
    public static SoundManager Instance { get; private set; }

    [SerializeField, Tooltip("Central designer-owned audio library. If empty, Resources/Audio/SO_AudioSettings is loaded.")]
    private AudioSettingsProfile audioProfile;
    [SerializeField, Tooltip("Optional fallback AudioMixer routing when an entry has no Output Group.")]
    private AudioMixerGroup defaultSfxGroup;
    [SerializeField] private bool playThemeOnStart = true;

    private AudioSource musicSource;
    private Coroutine bossMusicRoutine;
    private string activeMusicLayerId;
    private readonly HashSet<string> warnedMissingSounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioSource> loopSources = new Dictionary<string, AudioSource>();
    private static bool warnedMissingManager;
    private float masterVolume = 1f;
    private float sfxVolume = 1f;
    private float musicVolume = 1f;
    private AudioListener fallbackListener;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // The duplicate can live on PF_LevelSystem. Destroying its GameObject
            // would remove the complete level flow, loader and atmosphere setup.
            // Keep the host intact and remove only this redundant component.
            enabled = false;
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;

        if (audioProfile == null)
            audioProfile = Resources.Load<AudioSettingsProfile>("Audio/SO_AudioSettings");

        if (audioProfile != null)
        {
            masterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, audioProfile.masterVolume);
            sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, audioProfile.sfxVolume);
            musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, audioProfile.musicVolume);
        }

        RefreshFallbackListener();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            PlayerPrefs.Save();
            Instance = null;
        }
    }

    private void LateUpdate()
    {
        // While the temporary listener is active, keep checking until the real
        // gameplay camera has spawned. Afterwards sceneLoaded handles transitions.
        if (fallbackListener == null || fallbackListener.enabled)
            RefreshFallbackListener();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshFallbackListener();
    }

    private void RefreshFallbackListener()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        bool hasActiveSceneListener = false;

        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null || listener == fallbackListener)
                continue;

            if (listener.enabled && listener.gameObject.activeInHierarchy)
            {
                hasActiveSceneListener = true;
                break;
            }
        }

        if (fallbackListener == null)
        {
            fallbackListener = GetComponent<AudioListener>();
            if (fallbackListener == null)
                fallbackListener = gameObject.AddComponent<AudioListener>();
        }

        fallbackListener.enabled = !hasActiveSceneListener;
    }

    public AudioSource Play(SoundEmitter emitter)
    {
        if (emitter == null)
            return null;
        return Play(new SoundCue(emitter.SoundId), emitter.transform);
    }

    private void Start()
    {
        if (playThemeOnStart)
            PlayStartupMusic();
    }

    public AudioSource Play(SoundCue cue, Transform origin = null)
    {
        if (cue == null || !cue.IsConfigured)
            return null;

        if (!TryResolveDefinition(cue.SoundId, out AudioDefinition definition))
        {
            WarnMissingOnce(cue.SoundId, origin);
            return null;
        }

        GameObject host = new GameObject($"SFX_{definition.clip.name}");
        if (origin != null)
            host.transform.position = origin.position;

        AudioSource source = host.AddComponent<AudioSource>();
        ApplyDefinition(source, definition, false);
        source.volume = EffectiveBusVolume(definition);
        source.PlayOneShot(definition.clip, Mathf.Clamp(definition.volume, 0f, 2f));

        float lifetime = definition.clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch));
        Destroy(host, lifetime + 0.1f);
        return source;
    }

    public static bool TryPlay(SoundCue cue, Transform origin = null)
    {
        if (Instance == null)
        {
            if (!warnedMissingManager)
            {
                warnedMissingManager = true;
                Debug.LogWarning("[SoundManager] A sound was requested, but no SoundManager exists. This warning is shown only once.", origin);
            }
            return false;
        }

        if (cue == null || !cue.IsConfigured)
        {
            Instance.WarnMissingOnce("<empty sound slot>", origin);
            return false;
        }

        return Instance.Play(cue, origin) != null;
    }

    public static bool TryPlayId(string soundId, Transform origin = null, float spatialBlend = 1f)
    {
        return TryPlay(new SoundCue(soundId, spatialBlend), origin);
    }

    public static bool TryPlayConfiguredId(string soundId, Transform origin = null, float spatialBlend = 1f)
    {
        if (Instance == null || string.IsNullOrWhiteSpace(soundId) ||
            !Instance.TryResolveDefinition(soundId, out AudioDefinition definition) || definition.clip == null)
            return false;

        return Instance.Play(new SoundCue(soundId, spatialBlend), origin) != null;
    }

    public static void StopMusic()
    {
        if (Instance == null)
            return;

        if (Instance.bossMusicRoutine != null)
        {
            Instance.StopCoroutine(Instance.bossMusicRoutine);
            Instance.bossMusicRoutine = null;
        }

        if (Instance.musicSource != null)
            Instance.musicSource.Stop();

        Instance.activeMusicLayerId = null;
    }

    public static void EnsureStartupMusicPlaying()
    {
        if (Instance == null)
            return;

        if (Instance.musicSource != null && Instance.musicSource.isPlaying)
            return;

        StopMusic();
        Instance.PlayStartupMusic();
    }

    private void PlayStartupMusic()
    {
        PlayMusic(audioProfile != null ? audioProfile.startupMusicId : "Music.Theme", true);
    }

    public void PlayMusic(string soundId, bool loop)
    {
        if (!TryResolveDefinition(soundId, out AudioDefinition definition))
        {
            WarnMissingOnce(soundId, this);
            return;
        }

        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
        }

        musicSource.Stop();
        ApplyDefinition(musicSource, definition, loop);
        musicSource.Play();
    }

    public void PlayBossStart()
    {
        PlayMusicForLayer("boss");
    }

    public static void ApplyMusicForLayer(string layerId)
    {
        if (Instance != null)
            Instance.PlayMusicForLayer(layerId);
    }

    public void PlayMusicForLayer(string layerId)
    {
        if (!string.IsNullOrWhiteSpace(activeMusicLayerId) &&
            string.Equals(activeMusicLayerId, layerId, StringComparison.OrdinalIgnoreCase))
            return;

        if (audioProfile == null || !audioProfile.TryGetLayerMusic(layerId, out LayerMusicRule rule))
        {
            WarnMissingOnce($"Music rule for layer '{layerId}'", this);
            return;
        }

        if (rule.keepPreviousMusic)
            return;

        activeMusicLayerId = layerId;
        if (bossMusicRoutine != null)
            StopCoroutine(bossMusicRoutine);
        bossMusicRoutine = StartCoroutine(PlayMusicRuleRoutine(rule));
    }

    private System.Collections.IEnumerator PlayMusicRuleRoutine(LayerMusicRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.introSoundId))
        {
            float introDuration = ResolveDuration(rule.introSoundId);
            PlayMusic(rule.introSoundId, false);
            if (introDuration > 0f)
                yield return new WaitForSecondsRealtime(introDuration);
        }

        while (true)
        {
            float mainDuration = ResolveDuration(rule.mainSoundId);
            PlayMusic(rule.mainSoundId, !rule.loopMainWithOutro);
            if (!rule.loopMainWithOutro)
                break;
            if (mainDuration > 0f)
                yield return new WaitForSecondsRealtime(mainDuration);

            float endDuration = ResolveDuration(rule.outroSoundId);
            PlayMusic(rule.outroSoundId, false);
            if (endDuration > 0f)
                yield return new WaitForSecondsRealtime(endDuration);

            if (mainDuration <= 0f && endDuration <= 0f)
                break;
        }

        bossMusicRoutine = null;
    }

    public void PlayBossWin()
    {
        if (bossMusicRoutine != null)
        {
            StopCoroutine(bossMusicRoutine);
            bossMusicRoutine = null;
        }
        if (musicSource != null)
            musicSource.Stop();
        string winId = audioProfile != null ? audioProfile.bossWinSoundId : "Music.Boss.Win";
        PlayMusic(winId, false);
    }

    public static void SetLoop(string soundId, Transform owner, bool shouldPlay, float spatialBlend = 1f)
    {
        if (Instance == null || owner == null || string.IsNullOrWhiteSpace(soundId))
            return;

        Instance.SetLoopInternal(soundId, owner, shouldPlay, spatialBlend);
    }

    private void SetLoopInternal(string soundId, Transform owner, bool shouldPlay, float spatialBlend)
    {
        string key = $"{owner.GetInstanceID()}:{soundId}";
        if (loopSources.TryGetValue(key, out AudioSource existing) && existing == null)
        {
            loopSources.Remove(key);
            existing = null;
        }

        if (!shouldPlay)
        {
            if (existing != null && existing.isPlaying)
                existing.Pause();
            return;
        }

        if (existing != null)
        {
            if (!existing.isPlaying)
                existing.UnPause();
            return;
        }

        if (!TryResolveDefinition(soundId, out AudioDefinition definition))
        {
            WarnMissingOnce(soundId, owner);
            return;
        }

        GameObject host = new GameObject($"Loop_{soundId}");
        host.transform.SetParent(owner, false);
        AudioSource source = host.AddComponent<AudioSource>();
        ApplyDefinition(source, definition, true);
        source.Play();
        loopSources[key] = source;
    }

    public static void SetMasterVolume(float value)
    {
        if (Instance == null) return;
        Instance.masterVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MasterVolumeKey, Instance.masterVolume);
        Instance.RefreshPersistentSourceVolumes();
    }

    public static void SetSfxVolume(float value)
    {
        if (Instance == null) return;
        Instance.sfxVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(SfxVolumeKey, Instance.sfxVolume);
        Instance.RefreshPersistentSourceVolumes();
    }

    public static void SetMusicVolume(float value)
    {
        if (Instance == null) return;
        Instance.musicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MusicVolumeKey, Instance.musicVolume);
        Instance.RefreshPersistentSourceVolumes();
    }

    public float MasterVolume => masterVolume;
    public float SfxVolume => sfxVolume;
    public float MusicVolume => musicVolume;

    public void SetMasterVolumeFromUI(float value) => SetMasterVolume(value);
    public void SetSfxVolumeFromUI(float value) => SetSfxVolume(value);
    public void SetMusicVolumeFromUI(float value) => SetMusicVolume(value);

    private void RefreshPersistentSourceVolumes()
    {
        if (musicSource != null && musicSource.clip != null && TryResolveDefinitionByClip(musicSource.clip, out AudioDefinition musicDefinition))
            musicSource.volume = EffectiveVolume(musicDefinition);

        foreach (AudioSource source in loopSources.Values)
        {
            if (source != null && source.clip != null && TryResolveDefinitionByClip(source.clip, out AudioDefinition definition))
                source.volume = EffectiveVolume(definition);
        }
    }

    private void ApplyDefinition(AudioSource source, AudioDefinition definition, bool loop)
    {
        source.clip = definition.clip;
        source.volume = EffectiveVolume(definition);
        source.pitch = Mathf.Clamp(definition.pitch, 0.1f, 3f);
        source.loop = loop;
        source.playOnAwake = false;
        source.spatialBlend = definition.bus == AudioBus.Music ? 0f : definition.spatialBlend;
        source.minDistance = Mathf.Max(0.01f, definition.minDistance);
        source.maxDistance = Mathf.Max(source.minDistance, definition.maxDistance);
        source.rolloffMode = definition.rolloffMode;
        source.outputAudioMixerGroup = definition.outputGroup != null ? definition.outputGroup : defaultSfxGroup;
    }

    private float EffectiveVolume(AudioDefinition definition)
    {
        return Mathf.Clamp(definition.volume, 0f, 2f) * EffectiveBusVolume(definition);
    }

    private float EffectiveBusVolume(AudioDefinition definition)
    {
        float busVolume = definition.bus == AudioBus.Music ? musicVolume : sfxVolume;
        return masterVolume * busVolume;
    }

    private void WarnMissingOnce(string soundId, UnityEngine.Object context)
    {
        string key = string.IsNullOrWhiteSpace(soundId) ? "<empty>" : soundId;
        if (!warnedMissingSounds.Add(key))
            return;

        Debug.LogWarning($"[SoundManager] No sound configured for '{key}'. This warning is shown only once.", context);
    }

    public AudioClip ResolveClip(string soundId)
    {
        return TryResolveDefinition(soundId, out AudioDefinition definition) ? definition.clip : null;
    }

    private float ResolveDuration(string soundId)
    {
        return TryResolveDefinition(soundId, out AudioDefinition definition)
            ? definition.clip.length / Mathf.Max(0.01f, Mathf.Abs(definition.pitch))
            : 0f;
    }

    private bool TryResolveDefinition(string soundId, out AudioDefinition definition)
    {
        if (audioProfile != null && audioProfile.TryGetSound(soundId, out definition) && definition.clip != null)
            return true;

        definition = null;
        return false;
    }

    private bool TryResolveDefinitionByClip(AudioClip clip, out AudioDefinition definition)
    {
        if (audioProfile != null)
        {
            for (int i = 0; i < audioProfile.sounds.Count; i++)
            {
                AudioDefinition candidate = audioProfile.sounds[i];
                if (candidate != null && candidate.clip == clip)
                {
                    definition = candidate;
                    return true;
                }
            }
        }

        definition = null;
        return false;
    }
}

[Serializable]
public sealed class SoundCue
{
    [SerializeField, Tooltip("ID from the central Audio Settings ScriptableObject.")]
    private string soundId;

    public string SoundId => soundId;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(soundId);

    public SoundCue() { }

    public SoundCue(string soundId, float spatialBlend = 1f)
    {
        this.soundId = soundId;
    }

    public void Play(Transform origin = null)
    {
        SoundManager.TryPlay(this, origin);
    }

}

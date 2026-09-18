using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public enum AudioBus
{
    Sfx,
    Music
}

[Serializable]
public sealed class AudioDefinition
{
    public string id;
    public AudioClip clip;
    public AudioBus bus = AudioBus.Sfx;
    [Range(0f, 2f), Tooltip("Per-sound volume multiplier. 1 = original level, 2 = up to twice as loud.")]
    public float volume = 1f;
    [Range(0.1f, 3f)] public float pitch = 1f;
    [Range(0f, 1f)] public float spatialBlend = 1f;
    [Min(0.01f)] public float minDistance = 1f;
    [Min(0.01f)] public float maxDistance = 25f;
    public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
    public AudioMixerGroup outputGroup;
}

[Serializable]
public sealed class LayerMusicRule
{
    [Tooltip("Must match LevelFlowStep.stepId, for example Start, level_01, level_02 or boss.")]
    public string layerId;
    [Tooltip("When enabled, the music that is already playing continues without restarting.")]
    public bool keepPreviousMusic = true;
    public string introSoundId;
    public string mainSoundId;
    public string outroSoundId;
    [Tooltip("Repeats Main -> Outro -> Main. If disabled, Main loops by itself after the optional intro.")]
    public bool loopMainWithOutro;
}

[CreateAssetMenu(menuName = "SO/Audio/Audio Settings", fileName = "SO_AudioSettings")]
public sealed class AudioSettingsProfile : ScriptableObject
{
    [Header("Default volume used before the Settings UI overrides it")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float sfxVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 1f;

    [Header("Startup / special music")]
    public string startupMusicId = "Music.Theme";
    public string bossWinSoundId = "Music.Boss.Win";

    [Header("All music and sound effects")]
    public List<AudioDefinition> sounds = new List<AudioDefinition>();

    [Header("Music per Level Flow layer")]
    public List<LayerMusicRule> layerMusic = new List<LayerMusicRule>();

    public bool TryGetSound(string id, out AudioDefinition definition)
    {
        for (int i = 0; i < sounds.Count; i++)
        {
            AudioDefinition candidate = sounds[i];
            if (candidate != null && string.Equals(candidate.id, id, StringComparison.OrdinalIgnoreCase))
            {
                definition = candidate;
                return true;
            }
        }

        definition = null;
        return false;
    }

    public bool TryGetLayerMusic(string layerId, out LayerMusicRule rule)
    {
        for (int i = 0; i < layerMusic.Count; i++)
        {
            LayerMusicRule candidate = layerMusic[i];
            if (candidate != null && string.Equals(candidate.layerId, layerId, StringComparison.OrdinalIgnoreCase))
            {
                rule = candidate;
                return true;
            }
        }

        rule = null;
        return false;
    }
}

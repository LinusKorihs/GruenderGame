using System.Collections.Generic;
using UnityEngine;

public sealed class SupportSlowTarget : MonoBehaviour
{
    private const float DefaultSlowPerSource = 1f / 6f;
    private const float DefaultMaxSlow = 0.5f;
    private const float DefaultPulseDuration = 0.35f;

    private readonly List<SlowSource> sources = new List<SlowSource>();

    public float CurrentSlowFraction => CalculateCurrentSlowFraction();
    public float CurrentMoveSpeedMultiplier => Mathf.Max(0.1f, 1f - CurrentSlowFraction);

    public static void ApplyPulse(Transform target, Object source)
    {
        ApplyPulse(target, source, DefaultSlowPerSource, DefaultMaxSlow, DefaultPulseDuration);
    }

    public static void ApplyPulse(Transform target, Object source, float slowPerSource, float maxSlow, float duration)
    {
        if (target == null || source == null) return;

        GameObject receiverObject = ResolveReceiverObject(target);
        if (receiverObject == null) return;

        SupportSlowTarget receiver = receiverObject.GetComponent<SupportSlowTarget>();
        if (receiver == null)
        {
            receiver = receiverObject.AddComponent<SupportSlowTarget>();
        }

        receiver.AddOrRefreshSource(source.GetInstanceID(), slowPerSource, maxSlow, duration);
    }

    public static float GetMoveSpeedMultiplier(Component target)
    {
        if (target == null) return 1f;

        SupportSlowTarget receiver = target.GetComponent<SupportSlowTarget>();
        if (receiver == null) receiver = target.GetComponentInChildren<SupportSlowTarget>();
        if (receiver == null) receiver = target.GetComponentInParent<SupportSlowTarget>();

        return receiver != null ? receiver.CurrentMoveSpeedMultiplier : 1f;
    }

    public static float ApplyMoveSpeed(Component target, float baseSpeed)
    {
        return Mathf.Max(0f, baseSpeed) * GetMoveSpeedMultiplier(target);
    }

    private static GameObject ResolveReceiverObject(Transform target)
    {
        CombatantStats stats = target.GetComponentInParent<CombatantStats>();
        if (stats != null) return stats.gameObject;

        return target.gameObject;
    }

    private void Update()
    {
        PruneExpiredSources();
    }

    private void AddOrRefreshSource(int sourceId, float slowPerSource, float maxSlow, float duration)
    {
        float now = Time.time;
        float expiresAt = now + Mathf.Max(0.05f, duration);

        for (int i = 0; i < sources.Count; i++)
        {
            SlowSource source = sources[i];
            if (source.SourceId != sourceId) continue;

            source.ExpiresAt = expiresAt;
            source.SlowFraction = Mathf.Max(0f, slowPerSource);
            source.MaxSlowFraction = Mathf.Clamp01(maxSlow);
            sources[i] = source;
            return;
        }

        sources.Add(new SlowSource
        {
            SourceId = sourceId,
            ExpiresAt = expiresAt,
            SlowFraction = Mathf.Max(0f, slowPerSource),
            MaxSlowFraction = Mathf.Clamp01(maxSlow)
        });
    }

    private float CalculateCurrentSlowFraction()
    {
        PruneExpiredSources();

        float slow = 0f;
        float maxSlow = DefaultMaxSlow;

        for (int i = 0; i < sources.Count; i++)
        {
            slow += sources[i].SlowFraction;
            maxSlow = Mathf.Max(maxSlow, sources[i].MaxSlowFraction);
        }

        return Mathf.Min(Mathf.Clamp01(maxSlow), slow);
    }

    private void PruneExpiredSources()
    {
        float now = Time.time;
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            if (sources[i].ExpiresAt <= now)
            {
                sources.RemoveAt(i);
            }
        }
    }

    private struct SlowSource
    {
        public int SourceId;
        public float ExpiresAt;
        public float SlowFraction;
        public float MaxSlowFraction;
    }
}

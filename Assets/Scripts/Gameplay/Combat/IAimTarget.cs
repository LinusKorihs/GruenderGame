/// <summary>
/// Implemented by enemies that want projectiles to aim at a specific point rather than
/// the root transform (e.g. a burrower that is mostly underground).
/// </summary>
public interface IAimTarget
{
    /// <summary>
    /// Returns the world-space Transform projectiles should home toward.
    /// Must never return null — fall back to the object's own Transform if needed.
    /// </summary>
    UnityEngine.Transform GetAimTransform();
}

/// <summary>Shared aim-point resolution for commands, combat checks and projectiles.</summary>
public static class CombatTargetUtility
{
    public static UnityEngine.Transform GetAimTransform(UnityEngine.Transform target)
    {
        if (target == null) return null;

        IAimTarget aimTarget = target.GetComponent<IAimTarget>() ?? target.GetComponentInParent<IAimTarget>();
        UnityEngine.Transform aimTransform = aimTarget?.GetAimTransform();
        return aimTransform != null ? aimTransform : target;
    }

    public static UnityEngine.Vector3 GetAimPosition(UnityEngine.Transform target)
    {
        UnityEngine.Transform aimTransform = GetAimTransform(target);
        return aimTransform != null ? aimTransform.position : UnityEngine.Vector3.zero;
    }
}

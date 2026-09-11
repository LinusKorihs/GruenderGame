using UnityEngine;
using UnityEngine.InputSystem;

public static class PlayerRootResolver
{
    public static GameObject FromCommander(PlayerMinionCommander commander)
    {
        return commander != null ? FromTransform(commander.transform) : FindAny();
    }

    public static GameObject FromGameObject(GameObject candidate)
    {
        return candidate != null ? FromTransform(candidate.transform) : FindAny();
    }

    public static GameObject FromTransform(Transform source)
    {
        if (source == null) return FindAny();

        PlayerInput input = source.GetComponentInParent<PlayerInput>();
        if (input != null) return input.gameObject;

        PlayerBrain brain = source.GetComponentInParent<PlayerBrain>();
        if (brain != null) return brain.gameObject;

        Transform current = source;
        while (current != null)
        {
            if (HasPlayerTag(current.gameObject) &&
                current.GetComponentInChildren<PlayerMinionCommander>(true) != null)
            {
                return current.gameObject;
            }

            current = current.parent;
        }

        PlayerMinionCommander commander = source.GetComponentInParent<PlayerMinionCommander>();
        if (commander != null) return commander.gameObject;

        return source.gameObject;
    }

    public static GameObject FindAny()
    {
        PlayerInput[] inputs = Object.FindObjectsByType<PlayerInput>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < inputs.Length; i++)
        {
            if (inputs[i] != null && IsPlayerCandidate(inputs[i].gameObject))
            {
                return inputs[i].gameObject;
            }
        }

        PlayerBrain brain = Object.FindFirstObjectByType<PlayerBrain>();
        if (brain != null) return brain.gameObject;

        PlayerMinionCommander commander = Object.FindFirstObjectByType<PlayerMinionCommander>();
        if (commander != null) return FromCommander(commander);

        try
        {
            GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null) return FromTransform(taggedPlayer.transform);
        }
        catch
        {
            // The Player tag is not guaranteed in stripped test scenes.
        }

        return null;
    }

    public static Transform BodyTransform(GameObject player)
    {
        GameObject root = FromGameObject(player);
        if (root == null) return null;

        PlayerMovementCC movement = root.GetComponentInChildren<PlayerMovementCC>(true);
        if (movement != null && movement.BodyTransform != null)
        {
            return movement.BodyTransform;
        }

        CharacterController controller = root.GetComponentInChildren<CharacterController>(true);
        if (controller != null)
        {
            return controller.transform;
        }

        PlayerMinionCommander commander = root.GetComponentInChildren<PlayerMinionCommander>(true);
        if (commander != null && commander.transform.parent != null)
        {
            return commander.transform.parent;
        }

        return root.transform;
    }

    private static bool IsPlayerCandidate(GameObject candidate)
    {
        if (candidate == null) return false;
        return HasPlayerTag(candidate)
               || candidate.GetComponent<PlayerBrain>() != null
               || candidate.GetComponentInChildren<PlayerMinionCommander>(true) != null;
    }

    private static bool HasPlayerTag(GameObject candidate)
    {
        if (candidate == null) return false;

        try
        {
            return candidate.CompareTag("Player");
        }
        catch
        {
            return false;
        }
    }
}

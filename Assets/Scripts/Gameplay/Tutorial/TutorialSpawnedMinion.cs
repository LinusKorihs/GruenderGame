using UnityEngine;

[DisallowMultipleComponent]
public sealed class TutorialSpawnedMinion : MonoBehaviour
{
    public TutorialMinionSpawner Owner { get; private set; }

    public void Initialize(TutorialMinionSpawner owner)
    {
        Owner = owner;
    }
}

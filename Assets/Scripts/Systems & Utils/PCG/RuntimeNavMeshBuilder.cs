using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class RuntimeNavMeshBuilder : MonoBehaviour
{
    [Header("Surface")]
    [SerializeField] private NavMeshSurface surface;
    [SerializeField] private bool createSurfaceOnGeneratedRoot = true;
    [SerializeField] private bool buildChildSurfaces;

    [Header("Collection")]
    [SerializeField] private bool useGeneratedLayers = true;
    [SerializeField] private LayerMask layerMask = ~0;
    [SerializeField] private int defaultArea;

    [Header("Debug")]
    [SerializeField] private bool log;

    public void Build(Transform generatedRoot)
    {
        if (generatedRoot == null)
        {
            Debug.LogWarning($"{name}: cannot build NavMesh without generated root.", this);
            return;
        }

        Physics.SyncTransforms();

        if (buildChildSurfaces)
        {
            Debug.LogWarning(
                "[PCG NavMesh] Separate child surfaces cannot mark areas of another surface as Not Walkable. " +
                "Building one combined surface and using the child surface settings as modifiers instead.",
                this);
        }

        NavMeshSurface targetSurface = ResolveSurface(generatedRoot);
        if (targetSurface == null)
        {
            Debug.LogWarning($"{name}: no NavMeshSurface available for runtime build.", this);
            return;
        }

        int referencedSurfaces = PrepareChildSurfaceReferences(generatedRoot, targetSurface);
        ConfigureSurface(targetSurface);
        targetSurface.BuildNavMesh();

        if (log)
        {
            Debug.Log(
                $"[PCG NavMesh] Built one combined surface under {generatedRoot.name}. " +
                $"ReferencedChildSurfaces={referencedSurfaces}.",
                this);
        }
    }

    private static int PrepareChildSurfaceReferences(
        Transform generatedRoot,
        NavMeshSurface targetSurface)
    {
        NavMeshSurface[] childSurfaces = generatedRoot.GetComponentsInChildren<NavMeshSurface>(true);
        int referencedCount = 0;
        int notWalkableArea = NavMesh.GetAreaFromName("Not Walkable");

        for (int i = 0; i < childSurfaces.Length; i++)
        {
            NavMeshSurface childSurface = childSurfaces[i];
            if (childSurface == null || childSurface == targetSurface) continue;

            // Keep the authored component enabled, but remove its separately registered
            // NavMeshData so it cannot overlap the combined generated surface.
            childSurface.RemoveData();
            referencedCount++;

            if (childSurface.defaultArea != notWalkableArea) continue;

            NavMeshModifier modifier = childSurface.GetComponent<NavMeshModifier>();
            if (modifier == null)
            {
                modifier = childSurface.gameObject.AddComponent<NavMeshModifier>();
            }

            modifier.overrideArea = true;
            modifier.area = notWalkableArea;
        }

        return referencedCount;
    }

    private NavMeshSurface ResolveSurface(Transform generatedRoot)
    {
        if (surface != null)
        {
            if (surface.transform == generatedRoot)
                return surface;

            surface = null;
        }

        NavMeshSurface existing = generatedRoot.GetComponent<NavMeshSurface>();
        if (existing != null)
        {
            surface = existing;
            return surface;
        }

        if (!createSurfaceOnGeneratedRoot) return null;

        surface = generatedRoot.gameObject.AddComponent<NavMeshSurface>();
        return surface;
    }

    private void ConfigureSurface(NavMeshSurface targetSurface)
    {
        targetSurface.collectObjects = CollectObjects.Children;
        // Collider collection avoids render-only geometry. MeshColliders still require
        // their imported shared meshes to have Read/Write enabled for runtime baking.
        targetSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        targetSurface.defaultArea = defaultArea;
        targetSurface.layerMask = useGeneratedLayers ? ResolveGeneratedLayerMask() : layerMask;
    }

    private LayerMask ResolveGeneratedLayerMask()
    {
        int generated = LayerMask.NameToLayer("Generated");
        int capGenerated = LayerMask.NameToLayer("Cap(Generated)");

        int mask = 0;
        if (generated >= 0) mask |= 1 << generated;
        if (capGenerated >= 0) mask |= 1 << capGenerated;

        return mask != 0 ? mask : layerMask;
    }
}

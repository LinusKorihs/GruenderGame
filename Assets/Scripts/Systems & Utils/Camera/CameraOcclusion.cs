using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(CapsuleCollider), typeof(Rigidbody))]
public class CameraOcclusion : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private CameraCMSettings settings;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform target;

    private sealed class OccluderState
    {
        public bool originalEnabled;
        public Material[] originalSharedMaterials;
        public MaterialPropertyBlock[] originalPropertyBlocks;
        public readonly HashSet<Material> temporaryMaterials = new();
        public bool appliedMakeTransparent;
        public float appliedAlpha;
    }

    private CapsuleCollider capsule;
    private Rigidbody rb;

    private readonly HashSet<Renderer> activeOccluders = new();
    private readonly HashSet<Renderer> scanRenderers = new();
    private readonly Dictionary<Renderer, OccluderState> occluderStates = new();
    private readonly Dictionary<Renderer, int> lastSeenFrame = new();
    private readonly Collider[] scanHits = new Collider[128];

    private const int DebugProbeIntervalFrames = 30;
    private int lastProbeLogFrame = -DebugProbeIntervalFrames;
    private int lastTriggerStayLogFrame = -DebugProbeIntervalFrames;
    private int lastProbeColliderHits = -1;
    private int lastProbeRendererHits = -1;
    private int lastProbeMissingRenderers = -1;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int TransparencyId = Shader.PropertyToID("_Transparency");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int BlendId = Shader.PropertyToID("_Blend");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    private Vector3 gizmoStart;
    private Vector3 gizmoEnd;

    private LayerMask OccluderMask => settings.occluderMask;
    private bool MakeTransparent => settings.makeTransparent;
    private float TransparentAlpha => settings.transparentAlpha;
    private bool HideWhenTransparencyUnsupported => settings.hideWhenTransparencyUnsupported;
    private float Radius => settings.occlusionRadius;
    private float PaddingFromCamera => settings.paddingFromCamera;
    private float PaddingFromTarget => settings.paddingFromTarget;
    private bool DebugEnabled => settings.debugEnabled;
    private bool DrawGizmos => settings.drawGizmos;
    private int StaleFramesToRestore => settings.staleFramesToRestore;

    private void Awake()
    {
        capsule = GetComponent<CapsuleCollider>();
        rb = GetComponent<Rigidbody>();

        capsule.isTrigger = true;
        capsule.direction = 2;
        capsule.radius = Radius;

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        if (!cameraTransform) cameraTransform = Camera.main ? Camera.main.transform : null;

        if (DebugEnabled)
        {
            Debug.Log($"[CameraOcclusion] Awake on '{name}'. cameraTransform={(cameraTransform ? cameraTransform.name : "NULL")} target={(target ? target.name : "NULL")}");
        }
    }

    private void FixedUpdate()
    {
        if (!cameraTransform || !target) return;

        Vector3 camPos = cameraTransform.position;
        Vector3 targetPos = target.position;

        Vector3 dir = targetPos - camPos;
        float dist = dir.magnitude;
        if (dist <= 0.001f) return;

        Vector3 dirN = dir / dist;

        Vector3 start = camPos + dirN * PaddingFromCamera;
        Vector3 end = targetPos - dirN * PaddingFromTarget;

        float paddedDist = Vector3.Distance(start, end);
        if (paddedDist <= 0.001f) return;

        gizmoStart = start;
        gizmoEnd = end;

        capsule.radius = Radius;
        capsule.height = Mathf.Max(paddedDist + 2f * Radius, 2f * Radius);

        Vector3 mid = (start + end) * 0.5f;
        Quaternion rot = Quaternion.LookRotation((end - start).normalized, Vector3.up);

        rb.MovePosition(mid);
        rb.MoveRotation(rot);

        ScanOcclusionCapsule(start, end);
        CleanupStaleOccluders();
        RefreshActiveOccluders();
    }

    private void OnTriggerEnter(Collider other)
    {
        LogTriggerProbe(other, "ENTER");
    }

    private void OnTriggerStay(Collider other)
    {
        if (Time.frameCount - lastTriggerStayLogFrame < DebugProbeIntervalFrames)
            return;

        lastTriggerStayLogFrame = Time.frameCount;
        LogTriggerProbe(other, "STAY");
    }

    private void OnTriggerExit(Collider other)
    {
        LogTriggerProbe(other, "EXIT");
    }

    private void OnDisable()
    {
        if (DebugEnabled)
            Debug.Log($"[CameraOcclusion] OnDisable - restoring {activeOccluders.Count} occluders.");

        foreach (Renderer rend in new List<Renderer>(activeOccluders))
            RestoreOne(rend);

        activeOccluders.Clear();
        occluderStates.Clear();
        lastSeenFrame.Clear();
        scanRenderers.Clear();
    }

    private void ApplyOcclusion(Renderer rend)
    {
        if (!rend) return;

        if (!activeOccluders.Add(rend))
        {
            RefreshOcclusion(rend);
            return;
        }

        OccluderState state = CaptureState(rend);
        occluderStates[rend] = state;
        ApplyOcclusionState(rend, state);
    }

    private OccluderState CaptureState(Renderer rend)
    {
        Material[] materials = rend.sharedMaterials;
        int materialCount = materials != null ? materials.Length : 0;
        MaterialPropertyBlock[] propertyBlocks = new MaterialPropertyBlock[materialCount];

        for (int i = 0; i < materialCount; i++)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            rend.GetPropertyBlock(block, i);
            propertyBlocks[i] = block;
        }

        return new OccluderState
        {
            originalEnabled = rend.enabled,
            originalSharedMaterials = materials,
            originalPropertyBlocks = propertyBlocks
        };
    }

    private void RefreshActiveOccluders()
    {
        foreach (Renderer rend in new List<Renderer>(activeOccluders))
            RefreshOcclusion(rend);
    }

    private void RefreshOcclusion(Renderer rend)
    {
        if (!rend)
        {
            activeOccluders.Remove(rend);
            if (!ReferenceEquals(rend, null))
            {
                if (occluderStates.TryGetValue(rend, out OccluderState destroyedState))
                    ReleaseTemporaryMaterials(destroyedState);
                occluderStates.Remove(rend);
                lastSeenFrame.Remove(rend);
            }
            return;
        }

        if (!occluderStates.TryGetValue(rend, out OccluderState state))
            return;

        bool modeChanged = state.appliedMakeTransparent != MakeTransparent;
        bool alphaChanged = MakeTransparent && !Mathf.Approximately(state.appliedAlpha, TransparentAlpha);
        if (!modeChanged && !alphaChanged)
            return;

        RestoreVisualState(rend, state);
        ApplyOcclusionState(rend, state);
    }

    private void ApplyOcclusionState(Renderer rend, OccluderState state)
    {
        state.appliedMakeTransparent = MakeTransparent;
        state.appliedAlpha = TransparentAlpha;

        if (!MakeTransparent)
        {
            rend.enabled = false;

            if (DebugEnabled) Debug.Log($"[CameraOcclusion] HIDE -> '{rend.name}' (renderer.enabled=false)");
            return;
        }

        if (state.originalSharedMaterials == null || state.originalSharedMaterials.Length == 0)
        {
            if (DebugEnabled) Debug.LogWarning($"[CameraOcclusion] '{rend.name}' has no material. Keeping original visibility.");
            ApplyUnsupportedTransparencyFallback(rend, state);
            return;
        }

        rend.enabled = state.originalEnabled;

        Material[] runtimeMaterials = rend.materials;
        for (int i = 0; i < runtimeMaterials.Length; i++)
        {
            Material original = i < state.originalSharedMaterials.Length ? state.originalSharedMaterials[i] : null;
            if (runtimeMaterials[i] != null && runtimeMaterials[i] != original)
                state.temporaryMaterials.Add(runtimeMaterials[i]);
        }
        bool applied = false;
        bool assignedReplacementMaterial = false;

        for (int i = 0; i < runtimeMaterials.Length; i++)
        {
            Material material = runtimeMaterials[i];
            if (!material) continue;

            Material sourceMaterial = i < state.originalSharedMaterials.Length ? state.originalSharedMaterials[i] : material;
            if (ApplyTransparentMaterial(rend, i, material, sourceMaterial, TransparentAlpha))
            {
                applied = true;
                continue;
            }

            if (TryCreateOcclusionReplacementMaterial(material, out Material replacementMaterial))
            {
                state.temporaryMaterials.Add(replacementMaterial);
                if (ApplyTransparentMaterial(rend, i, replacementMaterial, replacementMaterial, TransparentAlpha))
                {
                    runtimeMaterials[i] = replacementMaterial;
                    assignedReplacementMaterial = true;
                    applied = true;

                    if (DebugEnabled)
                        Debug.Log($"[CameraOcclusion] SWAP -> '{rend.name}' slot={i} sourceShader='{material.shader.name}' replacement='{replacementMaterial.name}' alpha={TransparentAlpha}");
                }
            }
        }

        if (assignedReplacementMaterial)
            rend.materials = runtimeMaterials;

        if (!applied)
        {
            if (DebugEnabled)
            {
                string fallbackAction = HideWhenTransparencyUnsupported
                    ? "Hiding renderer fallback."
                    : "Keeping original visibility.";
                Debug.LogWarning($"[CameraOcclusion] Shader on '{rend.name}' has no supported color/alpha property. {fallbackAction} materials={DescribeMaterials(runtimeMaterials)}");
            }

            ApplyUnsupportedTransparencyFallback(rend, state);
            return;
        }

        if (DebugEnabled)
            Debug.Log($"[CameraOcclusion] FADE -> '{rend.name}' alpha={TransparentAlpha}");
    }

    private static bool ApplyTransparentMaterial(Renderer rend, int materialIndex, Material material, Material sourceMaterial, float alpha)
    {
        bool applied = false;
        int colorId;
        if (material.HasProperty(BaseColorId))
            colorId = BaseColorId;
        else if (material.HasProperty(ColorId))
            colorId = ColorId;
        else
            colorId = 0;

        if (colorId != 0)
        {
            Color color = sourceMaterial != null && sourceMaterial.HasProperty(colorId)
                ? sourceMaterial.GetColor(colorId)
                : material.GetColor(colorId);
            color.a = alpha;

            material.SetColor(colorId, color);

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            rend.GetPropertyBlock(block, materialIndex);
            block.SetColor(colorId, color);
            rend.SetPropertyBlock(block, materialIndex);

            applied = true;
        }

        if (material.HasProperty(AlphaId))
        {
            material.SetFloat(AlphaId, alpha);
            applied = true;
        }

        if (material.HasProperty(OpacityId))
        {
            material.SetFloat(OpacityId, alpha);
            applied = true;
        }

        if (material.HasProperty(TransparencyId))
        {
            material.SetFloat(TransparencyId, 1f - alpha);
            applied = true;
        }

        if (applied) ConfigureTransparentMaterial(material);
        return applied;
    }

    private static string DescribeMaterials(Material[] materials)
    {
        if (materials == null || materials.Length == 0)
            return "none";

        List<string> descriptions = new();
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (!material)
            {
                descriptions.Add($"slot{i}: NULL");
                continue;
            }

            string shaderName = material.shader ? material.shader.name : "NULL";
            descriptions.Add($"slot{i}: material='{material.name}' shader='{shaderName}'");
        }

        return string.Join(", ", descriptions);
    }

    private bool TryCreateOcclusionReplacementMaterial(Material sourceMaterial, out Material replacementMaterial)
    {
        replacementMaterial = null;

        if (!sourceMaterial || !sourceMaterial.shader || settings == null || settings.occlusionMaterialSwaps == null)
            return false;

        string sourceShaderName = sourceMaterial.shader.name;
        for (int i = 0; i < settings.occlusionMaterialSwaps.Length; i++)
        {
            CameraOcclusionMaterialSwap swap = settings.occlusionMaterialSwaps[i];
            if (string.IsNullOrWhiteSpace(swap.sourceShaderName) || !swap.replacementMaterial)
                continue;

            if (!string.Equals(sourceShaderName, swap.sourceShaderName, StringComparison.Ordinal))
                continue;

            replacementMaterial = new Material(swap.replacementMaterial)
            {
                name = $"{sourceMaterial.name}_OcclusionInstance"
            };
            return true;
        }

        return false;
    }

    private void ApplyUnsupportedTransparencyFallback(Renderer rend, OccluderState state)
    {
        if (HideWhenTransparencyUnsupported)
        {
            rend.enabled = false;
            if (DebugEnabled) Debug.Log($"[CameraOcclusion] HIDE fallback -> '{rend.name}' (renderer.enabled=false)");
            return;
        }

        RestoreVisualState(rend, state);
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");

        if (material.HasProperty(SurfaceId)) material.SetFloat(SurfaceId, 1f);
        if (material.HasProperty(BlendId)) material.SetFloat(BlendId, 0f);
        if (material.HasProperty(ModeId)) material.SetFloat(ModeId, 2f);
        if (material.HasProperty(SrcBlendId)) material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
        if (material.HasProperty(DstBlendId)) material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty(ZWriteId)) material.SetFloat(ZWriteId, 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void RestoreOne(Renderer rend)
    {
        if (!rend)
        {
            activeOccluders.Remove(rend);
            if (!ReferenceEquals(rend, null))
            {
                if (occluderStates.TryGetValue(rend, out OccluderState destroyedState))
                    ReleaseTemporaryMaterials(destroyedState);
                occluderStates.Remove(rend);
            }
            return;
        }

        if (!activeOccluders.Remove(rend))
        {
            if (DebugEnabled) Debug.Log($"[CameraOcclusion] Restore skipped (not active) -> '{rend.name}'");
            return;
        }

        if (occluderStates.TryGetValue(rend, out OccluderState state))
        {
            RestoreVisualState(rend, state);
            occluderStates.Remove(rend);
        }
        else
        {
            rend.SetPropertyBlock(null);
        }

        if (DebugEnabled) Debug.Log($"[CameraOcclusion] RESTORE -> '{rend.name}' (enabled={rend.enabled})");
    }

    private static void RestoreVisualState(Renderer rend, OccluderState state)
    {
        if (state.originalSharedMaterials != null)
            rend.sharedMaterials = state.originalSharedMaterials;

        if (state.originalPropertyBlocks != null)
        {
            int materialCount = state.originalSharedMaterials != null ? state.originalSharedMaterials.Length : state.originalPropertyBlocks.Length;
            for (int i = 0; i < materialCount && i < state.originalPropertyBlocks.Length; i++)
                rend.SetPropertyBlock(state.originalPropertyBlocks[i], i);
        }
        else
        {
            rend.SetPropertyBlock(null);
        }

        rend.enabled = state.originalEnabled;
        ReleaseTemporaryMaterials(state);
    }

    private static void ReleaseTemporaryMaterials(OccluderState state)
    {
        foreach (Material temporaryMaterial in state.temporaryMaterials)
        {
            if (temporaryMaterial != null)
                Destroy(temporaryMaterial);
        }
        state.temporaryMaterials.Clear();
    }

    private static bool IsInMask(int layer, LayerMask mask) => (mask.value & (1 << layer)) != 0;

    private void OnDrawGizmos()
    {
        if (!DrawGizmos) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(gizmoStart, gizmoEnd);

        Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
        Gizmos.DrawWireSphere(gizmoStart, Radius);
        Gizmos.DrawWireSphere(gizmoEnd, Radius);
    }

    private void MarkSeen(Renderer rend)
    {
        lastSeenFrame[rend] = Time.frameCount;
    }

    private void CleanupStaleOccluders()
    {
        List<Renderer> toRestore = new();

        foreach (Renderer rend in activeOccluders)
        {
            if (!rend)
            {
                toRestore.Add(rend);
                continue;
            }

            if (!lastSeenFrame.TryGetValue(rend, out int last))
            {
                toRestore.Add(rend);
                continue;
            }

            if (Time.frameCount - last > StaleFramesToRestore)
                toRestore.Add(rend);
        }

        for (int i = 0; i < toRestore.Count; i++)
        {
            Renderer rend = toRestore[i];
            if (!rend)
            {
                activeOccluders.Remove(rend);
                if (!ReferenceEquals(rend, null))
                {
                    if (occluderStates.TryGetValue(rend, out OccluderState destroyedState))
                        ReleaseTemporaryMaterials(destroyedState);
                    occluderStates.Remove(rend);
                    lastSeenFrame.Remove(rend);
                }
                continue;
            }

            lastSeenFrame.Remove(rend);
            RestoreOne(rend);

            if (DebugEnabled) Debug.Log($"[CameraOcclusion] FAILSAFE RESTORE -> '{rend.name}' (stale)");
        }
    }

    private void ScanOcclusionCapsule(Vector3 start, Vector3 end)
    {
        scanRenderers.Clear();

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            start,
            end,
            Radius,
            scanHits,
            OccluderMask,
            QueryTriggerInteraction.Ignore);

        int colliderHits = 0;
        int rendererHits = 0;
        int missingRenderers = 0;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = scanHits[i];
            scanHits[i] = null;

            if (!hit || hit == capsule)
                continue;

            colliderHits++;

            Renderer rend = FindOccluderRenderer(hit);
            if (!rend)
            {
                missingRenderers++;
                continue;
            }

            if (target != null && rend.transform.IsChildOf(target.root))
                continue;

            if (!scanRenderers.Add(rend))
                continue;

            MarkSeen(rend);
            ApplyOcclusion(rend);
            rendererHits++;
        }

        LogScanProbe(hitCount, colliderHits, rendererHits, missingRenderers);
    }

    private Renderer FindOccluderRenderer(Collider other)
    {
        if (!other)
            return null;

        if (other.TryGetComponent(out Renderer directRenderer))
            return directRenderer;

        Renderer parentRenderer = other.GetComponentInParent<Renderer>();
        if (parentRenderer)
            return parentRenderer;

        return other.GetComponentInChildren<Renderer>();
    }

    private void LogTriggerProbe(Collider other, string phase)
    {
        if (!DebugEnabled || !other || !IsInMask(other.gameObject.layer, OccluderMask))
            return;

        Renderer rend = FindOccluderRenderer(other);
        Debug.Log($"[CameraOcclusion] TRIGGER {phase} '{other.name}' -> '{(rend ? rend.name : "NO_RENDERER")}'");
    }

    private void LogScanProbe(int rawHitCount, int colliderHits, int rendererHits, int missingRenderers)
    {
        if (!DebugEnabled)
            return;

        bool changed =
            colliderHits != lastProbeColliderHits ||
            rendererHits != lastProbeRendererHits ||
            missingRenderers != lastProbeMissingRenderers;

        bool intervalElapsed = Time.frameCount - lastProbeLogFrame >= DebugProbeIntervalFrames;
        bool bufferFilled = rawHitCount >= scanHits.Length;

        if (!changed && !intervalElapsed && !bufferFilled)
            return;

        lastProbeLogFrame = Time.frameCount;
        lastProbeColliderHits = colliderHits;
        lastProbeRendererHits = rendererHits;
        lastProbeMissingRenderers = missingRenderers;

        Debug.Log($"[CameraOcclusion] PROBE colliders={colliderHits} renderers={rendererHits} missingRenderers={missingRenderers} active={activeOccluders.Count} alpha={TransparentAlpha} mask={OccluderMask.value}");

        if (bufferFilled)
            Debug.LogWarning($"[CameraOcclusion] PROBE hit buffer is full ({scanHits.Length}). Increase buffer size if occluders are missed.");
    }
}

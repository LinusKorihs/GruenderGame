using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class LevelFogOfWarController : MonoBehaviour
{
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int BlendId = Shader.PropertyToID("_Blend");
    private static readonly int CullId = Shader.PropertyToID("_Cull");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int FogColorId = Shader.PropertyToID("_FogColor");
    private static readonly int FogTargetId = Shader.PropertyToID("_FogTarget");
    private static readonly int FogVisibleRadiusId = Shader.PropertyToID("_FogVisibleRadius");
    private static readonly int FogFadeRadiusId = Shader.PropertyToID("_FogFadeRadius");
    private static readonly int FogVisibleAlphaId = Shader.PropertyToID("_FogVisibleAlpha");
    private static readonly int FogUnexploredAlphaId = Shader.PropertyToID("_FogUnexploredAlpha");
    private static readonly int FogScreenRadiusId = Shader.PropertyToID("_FogScreenRadius");
    private static readonly int FogScreenSoftnessId = Shader.PropertyToID("_FogScreenSoftness");

    [SerializeField] private LevelFogOfWarProfile defaultProfile;
    [SerializeField] private Transform revealTarget;
    [SerializeField] private Camera revealCamera;
    [SerializeField] private bool applyDefaultOnStart;
    [SerializeField] private bool liveApplyProfileChanges = true;
    [SerializeField] private bool logFogOfWar;

    private LevelFogOfWarProfile currentProfile;
    private Transform currentRuntimeSceneRoot;
    private GameObject overlayObject;
    private MeshRenderer overlayRenderer;
    private RawImage overlayImage;
    private Material screenFogMaterial;
    private Texture2D fogTexture;
    private Color32[] pixels;
    private bool[] explored;
    private int textureResolution;
    private float updateTimer;
    private bool textureDirty = true;
    private Vector2 lastScreenRevealCenter = new Vector2(-10f, -10f);
    private float lastScreenRevealRadius = -1f;
#if UNITY_EDITOR
    private bool editorApplyQueued;
#endif

    public LevelFogOfWarProfile DefaultProfile => defaultProfile;
    public Transform RevealTarget => revealTarget;
    public Camera RevealCamera => revealCamera;

    private void OnEnable()
    {
        LevelFogOfWarProfile.ProfileChanged += HandleProfileChanged;
    }

    private void OnDisable()
    {
        LevelFogOfWarProfile.ProfileChanged -= HandleProfileChanged;
    }

    private void Start()
    {
        if (applyDefaultOnStart)
        {
            Apply(defaultProfile, null);
        }
    }

    private void Update()
    {
        if (currentProfile == null || !currentProfile.enabled || !currentProfile.updateEveryFrame)
            return;

        updateTimer -= Time.deltaTime;
        if (updateTimer > 0f)
            return;

        updateTimer = currentProfile.updateInterval;
        RevealAtTarget();
    }

    private void OnValidate()
    {
        if (Application.isPlaying && liveApplyProfileChanges)
        {
            QueueEditorApplyCurrentFogOfWar();
        }
    }

    [ContextMenu("Apply Current Fog Of War")]
    public void ApplyCurrentFogOfWar()
    {
        Apply(currentProfile != null ? currentProfile : defaultProfile, currentRuntimeSceneRoot);
    }

    [ContextMenu("Clear Fog Of War")]
    public void ClearFog()
    {
        if (currentProfile == null)
            currentProfile = defaultProfile;

        if (currentProfile == null)
            return;

        EnsureTexture(currentProfile);
        if (screenFogMaterial != null)
        {
            textureDirty = true;
            RevealAtTarget();
            return;
        }

        if (explored == null)
            return;

        for (int i = 0; i < explored.Length; i++)
        {
            explored[i] = false;
        }

        textureDirty = true;
        RefreshTexture(currentProfile);
    }

    public void SetRevealTarget(Transform target)
    {
        revealTarget = target;
    }

    public void SetRevealCamera(Camera camera)
    {
        revealCamera = camera;
    }

    public void ClearRuntimeObjects()
    {
        if (overlayObject != null)
        {
            DestroyRuntime(overlayObject);
        }

        if (fogTexture != null)
        {
            DestroyRuntime(fogTexture);
        }

        if (screenFogMaterial != null)
        {
            DestroyRuntime(screenFogMaterial);
        }

        overlayObject = null;
        overlayRenderer = null;
        overlayImage = null;
        screenFogMaterial = null;
        fogTexture = null;
        pixels = null;
        explored = null;
        textureResolution = 0;
        currentRuntimeSceneRoot = null;
        textureDirty = true;
        lastScreenRevealCenter = new Vector2(-10f, -10f);
        lastScreenRevealRadius = -1f;
    }

    public void Apply(LevelFogOfWarProfile profile, Transform runtimeSceneRoot)
    {
        LevelFogOfWarProfile activeProfile = profile != null ? profile : defaultProfile;
        if (activeProfile == null)
        {
            Log("No Fog of War profile assigned.");
            return;
        }

        currentProfile = activeProfile;
        currentRuntimeSceneRoot = runtimeSceneRoot;
        textureDirty = true;

        if (!activeProfile.enabled)
        {
            SetOverlayVisible(false);
            return;
        }

        EnsureOverlay(activeProfile, runtimeSceneRoot);
        EnsureTexture(activeProfile);

        if (activeProfile.clearOnApply)
        {
            ClearExplored();
        }

        ResolveRevealTarget(activeProfile);
        if (revealTarget == null && activeProfile.hideOverlayUntilRevealTargetFound)
        {
            SetOverlayVisible(false);
            Log($"Waiting for reveal target before showing '{activeProfile.name}'.");
            return;
        }

        SetOverlayVisible(true);
        RevealAtTarget();
        Log($"Applied '{activeProfile.name}'.");
    }

    public void RevealAtTarget()
    {
        if (currentProfile == null)
            return;

        ResolveRevealTarget(currentProfile);
        if (revealTarget == null)
        {
            if (currentProfile.hideOverlayUntilRevealTargetFound)
            {
                SetOverlayVisible(false);
                return;
            }

            RefreshTexture(currentProfile);
            return;
        }

        SetOverlayVisible(true);
        RevealAtWorldPosition(revealTarget.position);
    }

    public void RevealAtWorldPosition(Vector3 worldPosition)
    {
        if (currentProfile == null)
            return;

        if (currentProfile.renderMode == LevelFogOfWarRenderMode.ScreenOverlay)
        {
            if (screenFogMaterial != null)
            {
                UpdateScreenFogMaterial(worldPosition, currentProfile);
                return;
            }

            if (fogTexture == null || explored == null)
                return;

            RevealAtScreenPosition(worldPosition);
            return;
        }

        if (fogTexture == null || explored == null)
            return;

        Vector2 uv = WorldToTextureUv(currentProfile, worldPosition);
        int centerX = Mathf.RoundToInt(uv.x * (textureResolution - 1));
        int centerY = Mathf.RoundToInt(uv.y * (textureResolution - 1));
        float pixelsPerWorldUnit = textureResolution / Mathf.Max(currentProfile.worldSize.x, currentProfile.worldSize.y);
        int revealPixels = Mathf.CeilToInt((currentProfile.revealRadius + currentProfile.edgeSoftness) * pixelsPerWorldUnit);

        for (int y = centerY - revealPixels; y <= centerY + revealPixels; y++)
        {
            if (y < 0 || y >= textureResolution)
                continue;

            for (int x = centerX - revealPixels; x <= centerX + revealPixels; x++)
            {
                if (x < 0 || x >= textureResolution)
                    continue;

                float worldDistance = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, centerY)) / pixelsPerWorldUnit;
                if (worldDistance <= currentProfile.revealRadius + currentProfile.edgeSoftness)
                {
                    explored[y * textureResolution + x] = true;
                }
            }
        }

        RefreshTexture(currentProfile, centerX, centerY, pixelsPerWorldUnit);
    }

    private void EnsureOverlay(LevelFogOfWarProfile profile, Transform runtimeSceneRoot)
    {
        if (profile.renderMode == LevelFogOfWarRenderMode.ScreenOverlay)
        {
            EnsureScreenOverlay(profile);
            return;
        }

        EnsureWorldOverlay(profile, runtimeSceneRoot);
    }

    private void EnsureScreenOverlay(LevelFogOfWarProfile profile)
    {
        if (overlayObject != null && overlayImage == null)
        {
            DestroyRuntime(overlayObject);
            overlayObject = null;
            overlayRenderer = null;
        }

        if (overlayObject == null)
        {
            overlayObject = new GameObject("Runtime_FogOfWarOverlay");
            overlayObject.transform.SetParent(transform, false);

            Canvas canvas = overlayObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = profile.screenSortingOrder;

            CanvasScaler scaler = overlayObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            overlayObject.AddComponent<GraphicRaycaster>().enabled = false;

            GameObject imageObject = new GameObject("Fog Mask");
            imageObject.transform.SetParent(overlayObject.transform, false);
            overlayImage = imageObject.AddComponent<RawImage>();
            overlayImage.raycastTarget = false;
            RectTransform rectTransform = overlayImage.rectTransform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        Canvas existingCanvas = overlayObject.GetComponent<Canvas>();
        if (existingCanvas != null)
        {
            existingCanvas.sortingOrder = profile.screenSortingOrder;
        }

        overlayImage ??= overlayObject.GetComponentInChildren<RawImage>(true);
        overlayRenderer = null;

        if (profile.useDepthTextureFog && EnsureScreenFogMaterial())
            return;

        ClearScreenFogMaterial();
        if (overlayImage != null)
        {
            overlayImage.material = null;
            overlayImage.texture = fogTexture;
        }
    }

    private void EnsureWorldOverlay(LevelFogOfWarProfile profile, Transform runtimeSceneRoot)
    {
        ClearScreenFogMaterial();

        if (overlayObject != null && overlayRenderer == null)
        {
            DestroyRuntime(overlayObject);
            overlayObject = null;
            overlayImage = null;
        }

        if (overlayObject == null)
        {
            overlayObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            overlayObject.name = "Runtime_FogOfWarOverlay";
            Collider collider = overlayObject.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyRuntime(collider);
            }
        }

        if (runtimeSceneRoot != null && runtimeSceneRoot.gameObject.scene.IsValid())
        {
            if (overlayObject.scene != runtimeSceneRoot.gameObject.scene)
            {
                overlayObject.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(overlayObject, runtimeSceneRoot.gameObject.scene);
            }

            overlayObject.transform.SetParent(runtimeSceneRoot, true);
        }
        else if (overlayObject.transform.parent != transform)
        {
            overlayObject.transform.SetParent(transform, true);
        }

        overlayObject.transform.position = profile.worldCenter + Vector3.up * profile.overlayHeight;
        overlayObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        overlayObject.transform.localScale = new Vector3(profile.worldSize.x, profile.worldSize.y, 1f);

        overlayRenderer = overlayObject.GetComponent<MeshRenderer>();
        overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = false;

        if (overlayRenderer.sharedMaterial == null)
        {
            overlayRenderer.sharedMaterial = CreateFogMaterial();
        }
    }

    private void EnsureTexture(LevelFogOfWarProfile profile)
    {
        if (profile.renderMode == LevelFogOfWarRenderMode.ScreenOverlay && screenFogMaterial != null)
        {
            if (fogTexture != null)
            {
                DestroyRuntime(fogTexture);
            }

            fogTexture = null;
            pixels = null;
            explored = null;
            textureResolution = 0;
            return;
        }

        int targetResolution = Mathf.Clamp(profile.textureResolution, 64, 1024);
        if (fogTexture != null && textureResolution == targetResolution)
            return;

        if (fogTexture != null)
        {
            DestroyRuntime(fogTexture);
        }

        textureResolution = targetResolution;
        fogTexture = new Texture2D(textureResolution, textureResolution, TextureFormat.RGBA32, false)
        {
            name = "Runtime Fog Of War Mask",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        pixels = new Color32[textureResolution * textureResolution];
        explored = new bool[pixels.Length];
        textureDirty = true;
        lastScreenRevealCenter = new Vector2(-10f, -10f);
        lastScreenRevealRadius = -1f;

        Material material = overlayRenderer != null ? overlayRenderer.sharedMaterial : null;
        if (material != null)
        {
            SetTextureIfPresent(material, BaseMapId, fogTexture);
            SetTextureIfPresent(material, MainTexId, fogTexture);
        }

        if (overlayImage != null)
        {
            overlayImage.texture = fogTexture;
            overlayImage.color = Color.white;
        }
    }

    private bool EnsureScreenFogMaterial()
    {
        if (overlayImage == null)
            return false;

        Shader shader = Resources.Load<Shader>("LevelFlow/SH_PlayerDistanceFog");
        if (shader == null)
        {
            shader = Shader.Find("Hidden/Axolite/PlayerDistanceFog");
        }

        if (shader == null)
        {
            Log("Depth fog shader was not found. Falling back to texture fog.");
            return false;
        }

        if (screenFogMaterial == null || screenFogMaterial.shader != shader)
        {
            ClearScreenFogMaterial();
            screenFogMaterial = new Material(shader)
            {
                name = "Runtime Player Distance Fog",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        overlayImage.material = screenFogMaterial;
        overlayImage.texture = Texture2D.whiteTexture;
        overlayImage.color = Color.white;
        return true;
    }

    private void ClearScreenFogMaterial()
    {
        if (screenFogMaterial == null)
            return;

        DestroyRuntime(screenFogMaterial);
        screenFogMaterial = null;
    }

    private void UpdateScreenFogMaterial(Vector3 worldPosition, LevelFogOfWarProfile profile)
    {
        if (screenFogMaterial == null || profile == null)
            return;

        Camera camera = ResolveRevealCamera();
        if (camera != null)
        {
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData != null)
            {
                cameraData.requiresDepthTexture = true;
            }
        }

        float screenSoftness = Mathf.Clamp01(profile.edgeSoftness / Mathf.Max(profile.revealRadius + profile.edgeSoftness, 0.01f));
        screenFogMaterial.SetColor(FogColorId, profile.fogColor);
        screenFogMaterial.SetVector(FogTargetId, new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, 1f));
        screenFogMaterial.SetFloat(FogVisibleRadiusId, Mathf.Max(0f, profile.revealRadius));
        screenFogMaterial.SetFloat(FogFadeRadiusId, Mathf.Max(0.001f, profile.edgeSoftness));
        screenFogMaterial.SetFloat(FogVisibleAlphaId, profile.visibleAlpha);
        screenFogMaterial.SetFloat(FogUnexploredAlphaId, profile.unexploredAlpha);
        screenFogMaterial.SetFloat(FogScreenRadiusId, profile.screenRevealRadius);
        screenFogMaterial.SetFloat(FogScreenSoftnessId, Mathf.Max(0.02f, screenSoftness * profile.screenRevealRadius));
    }

    private void RevealAtScreenPosition(Vector3 worldPosition)
    {
        Camera camera = ResolveRevealCamera();
        if (camera == null)
        {
            RefreshTexture(currentProfile);
            return;
        }

        if (currentProfile.usePlayerDistanceFog && currentProfile.projectScreenFogOnWorldPlane && RefreshDistanceFogTexture(camera, worldPosition, currentProfile))
        {
            return;
        }

        Vector3 viewportPosition = camera.WorldToViewportPoint(worldPosition);
        if (viewportPosition.z < 0f)
        {
            RefreshTexture(currentProfile);
            return;
        }

        Vector2 screenCenter = currentProfile.lockScreenRevealToViewCenter
            ? new Vector2(0.5f, 0.5f)
            : new Vector2(Mathf.Clamp01(viewportPosition.x), Mathf.Clamp01(viewportPosition.y));

        float normalizedRadius = currentProfile.usePlayerDistanceFog
            ? currentProfile.screenRevealRadius
            : EstimateScreenRevealRadius(camera, worldPosition, currentProfile);

        if (!textureDirty &&
            Vector2.Distance(lastScreenRevealCenter, screenCenter) < currentProfile.screenUpdateThreshold &&
            Mathf.Abs(lastScreenRevealRadius - normalizedRadius) < 0.001f)
        {
            return;
        }

        lastScreenRevealCenter = screenCenter;
        lastScreenRevealRadius = normalizedRadius;

        int centerX = Mathf.RoundToInt(screenCenter.x * (textureResolution - 1));
        int centerY = Mathf.RoundToInt(screenCenter.y * (textureResolution - 1));
        float pixelsPerNormalizedUnit = textureResolution;
        RefreshTexture(currentProfile, centerX, centerY, pixelsPerNormalizedUnit, normalizedRadius);
    }

    private bool RefreshDistanceFogTexture(Camera camera, Vector3 targetPosition, LevelFogOfWarProfile profile)
    {
        if (camera == null || profile == null || fogTexture == null || pixels == null)
            return false;

        float planeY = targetPosition.y + profile.screenDistancePlaneYOffset;
        Plane revealPlane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        Color fogColor = profile.fogColor;
        float fullVisibilityRadius = Mathf.Max(0f, profile.revealRadius);
        float fadeEndRadius = fullVisibilityRadius + Mathf.Max(0.001f, profile.edgeSoftness);
        Vector2 targetXZ = new Vector2(targetPosition.x, targetPosition.z);

        for (int y = 0; y < textureResolution; y++)
        {
            float viewportY = (y + 0.5f) / textureResolution;

            for (int x = 0; x < textureResolution; x++)
            {
                float viewportX = (x + 0.5f) / textureResolution;
                Ray ray = camera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
                float alpha = profile.unexploredAlpha;

                if (revealPlane.Raycast(ray, out float enter))
                {
                    Vector3 worldPoint = ray.GetPoint(enter);
                    float distance = Vector2.Distance(targetXZ, new Vector2(worldPoint.x, worldPoint.z));

                    if (distance <= fullVisibilityRadius)
                    {
                        alpha = profile.visibleAlpha;
                    }
                    else if (distance <= fadeEndRadius)
                    {
                        float edgeT = Mathf.InverseLerp(fullVisibilityRadius, fadeEndRadius, distance);
                        alpha = Mathf.Lerp(profile.visibleAlpha, profile.unexploredAlpha, edgeT);
                    }
                }

                pixels[y * textureResolution + x] = new Color(fogColor.r, fogColor.g, fogColor.b, alpha);
            }
        }

        fogTexture.SetPixels32(pixels);
        fogTexture.Apply(false);
        textureDirty = false;
        return true;
    }

    private void RefreshTexture(
        LevelFogOfWarProfile profile,
        int visibleCenterX = -1,
        int visibleCenterY = -1,
        float pixelsPerWorldUnit = 1f,
        float screenRevealRadiusOverride = -1f)
    {
        if (fogTexture == null || pixels == null || explored == null)
            return;

        Color fogColor = profile.fogColor;
        for (int y = 0; y < textureResolution; y++)
        {
            for (int x = 0; x < textureResolution; x++)
            {
                int index = y * textureResolution + x;
                float alpha = explored[index] ? profile.exploredAlpha : profile.unexploredAlpha;

                if (visibleCenterX >= 0 && visibleCenterY >= 0)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(visibleCenterX, visibleCenterY)) / pixelsPerWorldUnit;
                    float revealRadius = screenRevealRadiusOverride > 0f ? screenRevealRadiusOverride : profile.revealRadius;
                    float edgeSoftness = screenRevealRadiusOverride > 0f
                        ? Mathf.Max(0.01f, profile.edgeSoftness / Mathf.Max(profile.revealRadius, 0.01f) * screenRevealRadiusOverride)
                        : profile.edgeSoftness;

                    if (distance <= revealRadius)
                    {
                        alpha = profile.visibleAlpha;
                    }
                    else if (distance <= revealRadius + edgeSoftness && edgeSoftness > 0f)
                    {
                        float edgeT = Mathf.InverseLerp(revealRadius, revealRadius + edgeSoftness, distance);
                        alpha = Mathf.Lerp(profile.visibleAlpha, alpha, edgeT);
                    }
                }

                pixels[index] = new Color(fogColor.r, fogColor.g, fogColor.b, alpha);
            }
        }

        fogTexture.SetPixels32(pixels);
        fogTexture.Apply(false);
        textureDirty = false;
    }

    private void ClearExplored()
    {
        if (explored == null)
            return;

        for (int i = 0; i < explored.Length; i++)
        {
            explored[i] = false;
        }

        textureDirty = true;
    }

    private static float EstimateScreenRevealRadius(Camera camera, Vector3 worldPosition, LevelFogOfWarProfile profile)
    {
        if (camera == null || profile == null)
            return 0.28f;

        Vector3 center = camera.WorldToViewportPoint(worldPosition);
        Vector3 edge = camera.WorldToViewportPoint(worldPosition + Vector3.right * profile.revealRadius);
        float projectedRadius = Vector2.Distance(new Vector2(center.x, center.y), new Vector2(edge.x, edge.y));
        if (projectedRadius <= 0.001f || float.IsNaN(projectedRadius) || float.IsInfinity(projectedRadius))
        {
            projectedRadius = profile.screenRevealRadius;
        }

        return Mathf.Clamp(projectedRadius, 0.02f, 1f);
    }

    private void ResolveRevealTarget(LevelFogOfWarProfile profile)
    {
        if (revealTarget != null || profile == null || !profile.autoFindPlayerByTag)
            return;

        GameObject resolvedPlayer = PlayerRootResolver.FindAny();
        if (resolvedPlayer != null)
        {
            Transform bodyTransform = PlayerRootResolver.BodyTransform(resolvedPlayer);
            revealTarget = bodyTransform != null ? bodyTransform : resolvedPlayer.transform;
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.playerTag))
            return;

        try
        {
            GameObject player = GameObject.FindGameObjectWithTag(profile.playerTag);
            if (player != null)
            {
                revealTarget = player.transform;
            }
        }
        catch (UnityException)
        {
            Log($"Player tag '{profile.playerTag}' does not exist.");
        }
    }

    private Camera ResolveRevealCamera()
    {
        if (revealCamera != null && revealCamera.isActiveAndEnabled)
            return revealCamera;

        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.isActiveAndEnabled)
        {
            revealCamera = mainCamera;
            return revealCamera;
        }

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Camera bestCamera = null;
        float bestDepth = float.NegativeInfinity;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
                continue;

            if (bestCamera == null || candidate.depth > bestDepth)
            {
                bestCamera = candidate;
                bestDepth = candidate.depth;
            }
        }

        revealCamera = bestCamera;
        return revealCamera;
    }

    private Vector2 WorldToTextureUv(LevelFogOfWarProfile profile, Vector3 worldPosition)
    {
        Vector3 min = profile.worldCenter - new Vector3(profile.worldSize.x * 0.5f, 0f, profile.worldSize.y * 0.5f);
        float u = Mathf.InverseLerp(min.x, min.x + profile.worldSize.x, worldPosition.x);
        float v = Mathf.InverseLerp(min.z, min.z + profile.worldSize.y, worldPosition.z);
        return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
    }

    private static Material CreateFogMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader)
        {
            name = "Runtime Fog Of War Overlay",
            renderQueue = 3000
        };

        SetColorIfPresent(material, BaseColorId, Color.white);
        SetColorIfPresent(material, ColorId, Color.white);
        SetFloatIfPresent(material, SurfaceId, 1f);
        SetFloatIfPresent(material, BlendId, 0f);
        SetFloatIfPresent(material, CullId, 0f);
        SetFloatIfPresent(material, SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        SetFloatIfPresent(material, DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, ZWriteId, 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHAPREMULTIPLY_OFF");
        return material;
    }

    private static void SetTextureIfPresent(Material material, int propertyId, Texture texture)
    {
        if (material != null && material.HasProperty(propertyId))
        {
            material.SetTexture(propertyId, texture);
        }
    }

    private static void SetColorIfPresent(Material material, int propertyId, Color value)
    {
        if (material != null && material.HasProperty(propertyId))
        {
            material.SetColor(propertyId, value);
        }
    }

    private static void SetFloatIfPresent(Material material, int propertyId, float value)
    {
        if (material != null && material.HasProperty(propertyId))
        {
            material.SetFloat(propertyId, value);
        }
    }

    private void SetOverlayVisible(bool visible)
    {
        if (overlayObject != null)
        {
            overlayObject.SetActive(visible);
        }
    }

    private static void DestroyRuntime(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void OnDrawGizmosSelected()
    {
        LevelFogOfWarProfile profile = currentProfile != null ? currentProfile : defaultProfile;
        if (profile == null || !profile.drawDebugGizmos)
            return;

        Gizmos.color = new Color(0f, 0f, 0f, 0.45f);
        Vector3 center = profile.worldCenter + Vector3.up * profile.overlayHeight;
        Gizmos.DrawWireCube(center, new Vector3(profile.worldSize.x, 0.1f, profile.worldSize.y));

        if (revealTarget != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(revealTarget.position, profile.revealRadius);
            Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(revealTarget.position, profile.revealRadius + profile.edgeSoftness);
        }
    }

    private void HandleProfileChanged(LevelFogOfWarProfile changedProfile)
    {
        if (!Application.isPlaying || !liveApplyProfileChanges || changedProfile == null || changedProfile != currentProfile)
            return;

        QueueEditorApplyCurrentFogOfWar();
    }

    private void Log(string message)
    {
        if (logFogOfWar)
        {
            Debug.Log($"[Fog Of War] {message}", this);
        }
    }

    private void QueueEditorApplyCurrentFogOfWar()
    {
#if UNITY_EDITOR
        if (editorApplyQueued)
            return;

        editorApplyQueued = true;
        EditorApplication.delayCall += ApplyQueuedEditorFogOfWar;
#endif
    }

#if UNITY_EDITOR
    private void ApplyQueuedEditorFogOfWar()
    {
        editorApplyQueued = false;
        if (this == null || !Application.isPlaying || !liveApplyProfileChanges)
            return;

        Apply(currentProfile != null ? currentProfile : defaultProfile, currentRuntimeSceneRoot);
    }
#endif
}

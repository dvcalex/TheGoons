using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Displays the existing desktop UI as a physical panel without duplicating its contents.</summary>
[RequireComponent(typeof(Canvas))]
public sealed class SharedWorldSpaceCanvas : MonoBehaviour
{
    public Vector2 referenceResolution = new Vector2(1920, 1080);
    [Min(0.2f)] public float widthMetres = 1.6f;
    [Min(0.3f)] public float distanceMetres = 1.5f;
    public float heightOffset = -0.1f;

    public bool IsWorldSpace { get; private set; }
    public Camera Viewer { get; private set; }
    Canvas canvas;
    RectTransform rect;
    CanvasScaler scaler;
    TrackedDeviceRaycaster raycaster;
    RenderMode originalMode;
    Camera originalCamera;
    Vector3 originalPosition, originalScale;
    Quaternion originalRotation;
    Vector2 originalSize, originalAnchorMin, originalAnchorMax, originalPivot;
    bool originalScalerEnabled, originalRaycasterEnabled;
    float originalScaleFactor;

    public void Present(Camera viewer)
    {
        if (viewer == null) return;
        if (!IsWorldSpace)
        {
            canvas = GetComponent<Canvas>();
            rect = (RectTransform)transform;
            scaler = GetComponent<CanvasScaler>();
            originalMode = canvas.renderMode;
            originalCamera = canvas.worldCamera;
            originalPosition = rect.localPosition;
            originalRotation = rect.localRotation;
            originalScale = rect.localScale;
            originalSize = rect.sizeDelta;
            originalAnchorMin = rect.anchorMin;
            originalAnchorMax = rect.anchorMax;
            originalPivot = rect.pivot;
            originalScaleFactor = canvas.scaleFactor;
            originalScalerEnabled = scaler != null && scaler.enabled;
            if (scaler != null) scaler.enabled = false;
            raycaster = GetComponent<TrackedDeviceRaycaster>();
            originalRaycasterEnabled = raycaster != null && raycaster.enabled;
            if (raycaster == null) raycaster = gameObject.AddComponent<TrackedDeviceRaycaster>();
            raycaster.checkFor3DOcclusion = false;
            raycaster.checkFor2DOcclusion = false;
            raycaster.maxDistance = 10;
            raycaster.enabled = true;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.scaleFactor = 1;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = referenceResolution;
            float scale = widthMetres / Mathf.Max(1, referenceResolution.x);
            // The canvas should be a scene root or have unit-scale ancestors.
            rect.localScale = Vector3.one * scale;
            IsWorldSpace = true;
        }
        Viewer = viewer;
        canvas.worldCamera = viewer;
        Recenter();
    }

    [ContextMenu("Recenter VR panel")]
    public void Recenter()
    {
        if (!IsWorldSpace || Viewer == null) return;
        Vector3 forward = Vector3.ProjectOnPlane(Viewer.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();
        rect.SetPositionAndRotation(Viewer.transform.position + forward * distanceMetres + Vector3.up * heightOffset,
            Quaternion.LookRotation(forward, Vector3.up));
    }

    public void RestoreDesktop()
    {
        if (!IsWorldSpace || canvas == null) return;
        IsWorldSpace = false;
        Viewer = null;
        canvas.renderMode = originalMode;
        canvas.worldCamera = originalCamera;
        rect.anchorMin = originalAnchorMin;
        rect.anchorMax = originalAnchorMax;
        rect.pivot = originalPivot;
        rect.sizeDelta = originalSize;
        rect.localPosition = originalPosition;
        rect.localRotation = originalRotation;
        rect.localScale = originalScale;
        canvas.scaleFactor = originalScaleFactor;
        if (scaler != null) scaler.enabled = originalScalerEnabled;
        if (raycaster != null) raycaster.enabled = originalRaycasterEnabled;
    }

    void OnDisable() => RestoreDesktop();
}

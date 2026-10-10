using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

/// <summary>Local pre-connection tracking and shared-menu presentation; no network player is spawned for the menu.</summary>
public sealed class VrMenuPresenter : MonoBehaviour
{
    public RelayManager relay;
    public GameObject hostPlayerPrefab;
    public SharedWorldSpaceCanvas[] panels;
    public VrUiInput uiInput;
    public Camera desktopMenuCamera;
    [Tooltip("Join controls are desktop-only; the VR player is the host.")]
    public GameObject[] desktopOnlyControls;
    [Tooltip("Editor-only preview. Builds always detect a running XR display.")]
    public bool previewVrInEditor;
    public bool IsVr { get; private set; }
    public XROrigin CurrentOrigin { get; private set; }

    XROrigin menuOrigin;
    AudioListener desktopListener;
    bool cameraWasEnabled, listenerWasEnabled, recenterHeld, positioned;
    int placementDelay;
    bool[] desktopControlStates;
    readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

    IEnumerator Start()
    {
        // Let RelayManager initialize its desktop screens first.
        yield return null;
        while (enabled)
        {
            if (!IsVr && WantsVr()) EnterVr();
            yield return new WaitForSecondsRealtime(0.2f);
        }
    }

    bool WantsVr()
    {
#if UNITY_EDITOR
        if (previewVrInEditor) return true;
#endif
        displays.Clear();
        SubsystemManager.GetSubsystems(displays);
        foreach (var display in displays) if (display.running) return true;
        return false;
    }

    void EnterVr()
    {
        if (hostPlayerPrefab == null || uiInput == null || relay == null) return;
        var template = hostPlayerPrefab.GetComponentInChildren<XROrigin>(true);
        if (template == null) return;
        // Clone only the tracking child, never the NetworkObject or gameplay scripts.
        menuOrigin = Instantiate(template, Vector3.zero, Quaternion.identity);
        menuOrigin.name = "Local VR Menu Rig";
        menuOrigin.gameObject.SetActive(true);
        foreach (var collider in menuOrigin.GetComponentsInChildren<Collider>()) collider.enabled = false;
#if UNITY_EDITOR
        if (previewVrInEditor) menuOrigin.Camera.transform.localPosition = Vector3.up * 1.65f;
#endif
        CurrentOrigin = menuOrigin;
        if (desktopMenuCamera != null)
        {
            cameraWasEnabled = desktopMenuCamera.enabled;
            desktopMenuCamera.enabled = false;
            desktopListener = desktopMenuCamera.GetComponent<AudioListener>();
            listenerWasEnabled = desktopListener != null && desktopListener.enabled;
            if (desktopListener != null) desktopListener.enabled = false;
        }
        desktopControlStates = new bool[desktopOnlyControls.Length];
        for (int i = 0; i < desktopOnlyControls.Length; i++)
        {
            if (desktopOnlyControls[i] == null) continue;
            desktopControlStates[i] = desktopOnlyControls[i].activeSelf;
            desktopOnlyControls[i].SetActive(false);
        }
        relay.screen_Name.SetActive(false);
        relay.screen_Connecting.SetActive(false);
        relay.screen_JoinHost.SetActive(true);
        IsVr = true;
        BindOrigin(CurrentOrigin);
    }

    void BindOrigin(XROrigin origin)
    {
        uiInput.Activate(origin.CameraFloorOffsetObject.transform);
        foreach (var panel in panels) if (panel != null) panel.Present(origin.Camera);
        positioned = false;
        placementDelay = 2;
    }

    void TryUseSpawnedPlayer()
    {
        var manager = NetworkManager.Singleton;
        var player = manager != null && manager.IsClient ? manager.LocalClient?.PlayerObject : null;
        var origin = player != null ? player.GetComponentInChildren<XROrigin>(true) : null;
        if (origin == null || origin == CurrentOrigin || !origin.gameObject.activeInHierarchy) return;
        // Preserve the tracking origin at the menu-to-game transition.
        if (menuOrigin != null)
        {
            origin.transform.SetPositionAndRotation(menuOrigin.transform.position, menuOrigin.transform.rotation);
            menuOrigin.gameObject.SetActive(false);
            Destroy(menuOrigin.gameObject);
            menuOrigin = null;
        }
        CurrentOrigin = origin;
        BindOrigin(origin);
    }

    void LateUpdate()
    {
        if (!IsVr || CurrentOrigin == null) return;
        TryUseSpawnedPlayer();
        if (placementDelay > 0) { --placementDelay; return; }
        var hmd = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.Head);
        bool tracked;
        bool headReady = hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out tracked) && tracked;
#if UNITY_EDITOR
        headReady |= previewVrInEditor;
#endif
        // Wait for a real head pose before initial placement; keep the panel stable afterwards.
        if (!positioned && headReady) { Recenter(); positioned = true; }
        var left = XRController.leftHand?.TryGetChildControl<ButtonControl>("secondaryButton");
        var right = XRController.rightHand?.TryGetChildControl<ButtonControl>("secondaryButton");
        bool held = (left != null && left.isPressed) || (right != null && right.isPressed);
        if (held && !recenterHeld) Recenter();
        recenterHeld = held;
    }

    public void Recenter()
    {
        foreach (var panel in panels) if (panel != null) panel.Recenter();
    }

    void OnDisable()
    {
        if (!IsVr) return;
        IsVr = false;
        if (uiInput != null) uiInput.Deactivate();
        foreach (var panel in panels) if (panel != null) panel.RestoreDesktop();
        if (menuOrigin != null) Destroy(menuOrigin.gameObject);
        if (desktopMenuCamera != null) desktopMenuCamera.enabled = cameraWasEnabled;
        if (desktopListener != null) desktopListener.enabled = listenerWasEnabled;
        for (int i = 0; i < desktopOnlyControls.Length; i++)
            if (desktopOnlyControls[i] != null) desktopOnlyControls[i].SetActive(desktopControlStates[i]);
    }
}

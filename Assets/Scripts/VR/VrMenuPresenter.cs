using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
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

    public bool IsVr
    {
        get;
        private set;
    }

    public XROrigin CurrentOrigin
    {
        get;
        private set;
    }

    private XROrigin menuOrigin;
    private AudioListener desktopListener;
    private bool cameraWasEnabled;
    private bool listenerWasEnabled;
    private bool recenterHeld;
    private bool toggleHeld;
    private bool menuVisible = true;
    private bool positioned;
    private int placementDelay;
    private bool[] desktopControlStates;
    private readonly List<SharedWorldSpaceCanvas> gameplayPanels = new List<SharedWorldSpaceCanvas>();
    private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    private readonly List<XRInputSubsystem> trackingSubsystems = new List<XRInputSubsystem>();

    private IEnumerator Start()
    {
        GameManager.InstanceChanged += BindGameplayPanels;

        // Let RelayManager initialize its desktop screens first.
        yield return null;
        while (enabled)
        {
            if (!IsVr && WantsVr())
            {
                EnterVr();
            }

            yield return new WaitForSecondsRealtime(0.2f);
        }
    }

    private bool WantsVr()
    {
#if UNITY_EDITOR
        if (previewVrInEditor)
        {
            return true;
        }

#endif
        displays.Clear();
        SubsystemManager.GetSubsystems(displays);
        foreach (var display in displays)
        {
            if (display.running)
            {
                return true;
            }
        }

        return false;
    }

    private void EnterVr()
    {
        if (hostPlayerPrefab == null || uiInput == null || relay == null)
        {
            return;
        }

        var template = hostPlayerPrefab.GetComponentInChildren<XROrigin>(true);
        if (template == null)
        {
            return;
        }

        // Clone only the tracking child, never the NetworkObject or gameplay scripts.
        menuOrigin = Instantiate(template, Vector3.zero, Quaternion.identity);
        menuOrigin.name = "Local VR Menu Rig";
        menuOrigin.gameObject.SetActive(true);
        foreach (var collider in menuOrigin.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

#if UNITY_EDITOR
        if (previewVrInEditor)
        {
            menuOrigin.Camera.transform.localPosition = Vector3.up * 1.65f;
        }

#endif
        CurrentOrigin = menuOrigin;
        if (desktopMenuCamera != null)
        {
            cameraWasEnabled = desktopMenuCamera.enabled;
            desktopMenuCamera.enabled = false;
            desktopListener = desktopMenuCamera.GetComponent<AudioListener>();
            listenerWasEnabled = desktopListener != null && desktopListener.enabled;
            if (desktopListener != null)
            {
                desktopListener.enabled = false;
            }
        }

        desktopControlStates = new bool[desktopOnlyControls.Length];
        for (int i = 0; i < desktopOnlyControls.Length; i++)
        {
            if (desktopOnlyControls[i] == null)
            {
                continue;
            }

            desktopControlStates[i] = desktopOnlyControls[i].activeSelf;
            desktopOnlyControls[i].SetActive(false);
        }

        relay.screen_Name.SetActive(false);
        relay.screen_Connecting.SetActive(false);
        relay.screen_JoinHost.SetActive(true);
        IsVr = true;
        BindOrigin(CurrentOrigin);
    }

    private void BindOrigin(XROrigin origin)
    {
        uiInput.Activate(origin.CameraFloorOffsetObject.transform);
        foreach (var panel in panels)
        {
            if (panel != null)
            {
                panel.Present(origin.Camera);
                panel.SetVisible(menuVisible);
            }
        }

        BindGameplayPanels(GameManager._instance);
        positioned = false;
        placementDelay = 2;
    }

    private void TryUseSpawnedPlayer()
    {
        var manager = NetworkManager.Singleton;
        var player = manager != null && manager.IsClient ? manager.LocalClient?.PlayerObject : null;
        var origin = player != null ? player.GetComponentInChildren<XROrigin>(true) : null;
        if (origin == null || origin == CurrentOrigin || !origin.gameObject.activeInHierarchy)
        {
            return;
        }

        // The spawned rig owns its tracking frame; do not copy the menu world transform.
        if (menuOrigin != null)
        {
            menuOrigin.gameObject.SetActive(false);
            Destroy(menuOrigin.gameObject);
            menuOrigin = null;
        }

        CurrentOrigin = origin;
        BindOrigin(origin);
    }

    private void BindGameplayPanels(GameManager game)
    {
        if (!IsVr || CurrentOrigin == null || game == null)
        {
            return;
        }

        foreach (var canvas in game.GetComponentsInChildren<Canvas>(true))
        {
            if (!canvas.isRootCanvas)
            {
                continue;
            }

            var panel = canvas.GetComponent<SharedWorldSpaceCanvas>();
            if (panel == null)
            {
                panel = canvas.gameObject.AddComponent<SharedWorldSpaceCanvas>();
            }

            if (!gameplayPanels.Contains(panel))
            {
                gameplayPanels.Add(panel);
            }

            panel.Present(CurrentOrigin.Camera);
            panel.SetVisible(menuVisible);
        }
    }

    private void LateUpdate()
    {
        if (!IsVr || CurrentOrigin == null)
        {
            return;
        }

        TryUseSpawnedPlayer();
        if (placementDelay > 0)
        {
            --placementDelay;
            return;
        }

        var hmd = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.Head);
        bool tracked;
        bool headReady = hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out tracked) && tracked;
#if UNITY_EDITOR
        headReady |= previewVrInEditor;
#endif
        // Wait for a real head pose before initial placement; keep the panel stable afterwards.
        if (!positioned && headReady)
        {
            Recenter();
            positioned = true;
        }

        var left = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        var right = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool togglePressed);
        right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool held);

        if (Application.isFocused && togglePressed && !toggleHeld)
        {
            ToggleMenu();
        }

        if (Application.isFocused && headReady && held && !recenterHeld)
        {
            Calibrate();
        }

        toggleHeld = togglePressed;
        recenterHeld = held;

        UpdatePanelVisibility();
    }

    private void UpdatePanelVisibility()
    {
        var manager = NetworkManager.Singleton;
        bool connected = manager != null && manager.IsListening;
        gameplayPanels.RemoveAll(panel => panel == null);

        // The gameplay panel replaces the connection screen once a session starts.
        foreach (var panel in panels)
        {
            if (panel != null)
            {
                panel.SetVisible(menuVisible && (!connected || gameplayPanels.Count == 0));
            }
        }

        foreach (var panel in gameplayPanels)
        {
            panel.SetVisible(menuVisible && connected);
        }

        if (relay.lobbyCodeDisplay != null)
        {
            relay.lobbyCodeDisplay.gameObject.SetActive(connected);
        }
    }

    public void ToggleMenu()
    {
        if (!IsVr)
        {
            return;
        }

        menuVisible = !menuVisible;
        UpdatePanelVisibility();

        if (menuVisible)
        {
            Recenter();
        }
    }

    public void Calibrate()
    {
        if (!IsVr || CurrentOrigin == null)
        {
            return;
        }

        trackingSubsystems.Clear();
        SubsystemManager.GetSubsystems(trackingSubsystems);
        bool recentered = false;
        foreach (var subsystem in trackingSubsystems)
        {
            if (subsystem.running && subsystem.TryRecenter())
            {
                recentered = true;
            }
        }

        if (!recentered)
        {
            Debug.Log("[VR] Tracking recenter is unavailable in this tracking mode. Use the headset's system recenter control. UI panels will still recenter.", this);
        }

        // Wait for the updated tracking pose before placing panels again.
        positioned = false;
        placementDelay = 2;
    }

    public void Recenter()
    {
        foreach (var panel in panels)
        {
            if (panel != null)
            {
                panel.Recenter();
            }
        }

        foreach (var panel in gameplayPanels)
        {
            if (panel != null)
            {
                panel.Recenter();
            }
        }
    }

    private void OnDisable()
    {
        if (!IsVr)
        {
            return;
        }

        IsVr = false;
        menuVisible = true;
        toggleHeld = false;
        recenterHeld = false;
        if (uiInput != null)
        {
            uiInput.Deactivate();
        }

        foreach (var panel in panels)
        {
            if (panel != null)
            {
                panel.RestoreDesktop();
            }
        }

        foreach (var panel in gameplayPanels)
        {
            if (panel != null)
            {
                panel.RestoreDesktop();
            }
        }

        gameplayPanels.Clear();

        if (menuOrigin != null)
        {
            Destroy(menuOrigin.gameObject);
        }

        if (desktopMenuCamera != null)
        {
            desktopMenuCamera.enabled = cameraWasEnabled;
        }

        if (desktopListener != null)
        {
            desktopListener.enabled = listenerWasEnabled;
        }

        for (int i = 0; i < desktopOnlyControls.Length; i++)
        {
            if (desktopOnlyControls[i] != null)
            {
                desktopOnlyControls[i].SetActive(desktopControlStates[i]);
            }
        }
    }

    private void OnDestroy()
    {
        GameManager.InstanceChanged -= BindGameplayPanels;
    }
}

# Shared desktop and VR menus

AlexScene uses the same Canvas, buttons, text, and callbacks for both player types. The saved scene stays in Screen Space Overlay so the UI remains easy to edit.

AlexScene is a temporary startup and networking test scene, not the finished gameplay map. The Quest VR Host build profile loads it first. Inside this scene, a local VR menu rig starts tracking before a network connection exists; clicking Host starts hosting and spawns Host_Player in the same test scene. There is currently no separate bootstrap/loading scene and no transition to a finished gameplay scene. Desktop can use this same test scene with mouse UI, but the global/desktop build list still starts with SampleScene. Later, both VR and desktop players should enter the same finished gameplay map through host-controlled Netcode scene loading; that map and transition have not been implemented.

## Quest test

In Unity's **File > Build Profiles**, select **Quest VR Host**, switch to that profile, and use **Build** or **Build And Run**. The profile asset is `Assets/Settings/Quest VR Host.asset`. It targets Android/APK and overrides its scene list to start in AlexScene, preserving the other profiles/global scene order. Android OpenXR and Meta Quest Support are enabled, with the Oculus Touch controller profile.

On the headset:

1. A local tracking rig displays the existing menu as a floating panel before any network connection.
2. Name entry and desktop Join/code-entry controls are skipped for the VR host.
3. Point either controller's cyan ray at **Host**, then squeeze and release its trigger.
4. **B or Y** recenters the panel in front of your current head position. It otherwise stays fixed in the world.
5. On successful hosting, the menu disappears and tracking transfers to the networked Host_Player. The temporary menu rig is removed.

AlexScene currently has RelayManager's **Enable Online** off, preserving its local-network testing behavior. Enable Online when you want a Relay lobby. Failed host startup returns to the menu for retry. No name is assigned to the VR host.

Desktop players retain screen-space UI, mouse input, name entry, and the Join/code-entry flow. This change does not alter NetworkPlayer's host/client selection logic.

## Editing and reuse

- **Canvas / SharedWorldSpaceCanvas:** reference layout 1920 x 1080; panel width 1.6 m; distance 1.5 m; height offset -0.1 m. Existing button bindings and layout children are preserved.
- **NetworkManager / VrMenuPresenter:** references the shared panels, Host_Player prefab, lobby camera and controls hidden only in VR. Additional shared root canvases can be added to Panels; assign distinct placement offsets if you display several simultaneously.
- **NetworkManager / VrUiInput:** extends the existing InputSystemUIInputModule with two tracked pointers. It uses a runtime copy of the UI action asset and restores the original configuration when disabled. No XRI package or replacement interaction system is required.
- **Host_Player / LocalVrPlayer:** enables the tracked rig only for its network owner, preventing another player's camera and tracking from activating locally.
- The pointer material is a serialized URP asset so it is included in player builds.

`Preview Vr In Editor` is an optional presentation preview on VrMenuPresenter. It is off in the saved scene and ignored in builds. It gives the menu camera an initial eye height without a headset; it does not simulate controllers or replace headset testing. Automatic VR selection uses a running XR display, not the network host flag or Android platform alone.

The physics arms have not been connected in this UI pass. Text entry and a VR keyboard are not included; the VR host has no editable fields in this menu.

## Validation

Validated in Unity 6000.4.9f1 using synthetic Input System devices and the real UI input module:

- Desktop mouse press/release invokes the existing name Submit button and opens the next screen.
- VR preview converts the original canvas, skips name entry, and hides Join/code entry.
- Right controller aim/trigger invokes the original Host button and starts the local network host.
- Handoff leaves one camera and one AudioListener, destroys the temporary rig, and rebinds UI tracking and the canvas camera to Host_Player.
- A remote-owned Host_Player leaves its XR Origin inactive and does not add a local camera.
- Left controller aim/trigger clicks a UI button after handoff.
- Head movement leaves the panel anchored; recenter moves it; disabling presentation restores Screen Space Overlay, CanvasScaler, and the original UI actions.
- Android player script compilation generated Assembly-CSharp.dll with the VR components. Android OpenXR validation has no required errors; it reports optional SSAO/performance and input-latency recommendations.

These are editor/simulated-input checks. No APK was built or tested on the headset in this pass.

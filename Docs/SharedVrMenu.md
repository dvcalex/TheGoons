# Shared desktop and VR menus

StartupScene uses the same Canvas, buttons, text, and callbacks for both player types. The saved scene stays in Screen Space Overlay so the UI remains easy to edit.

StartupScene is the shared startup/menu scene for Quest and desktop. The Quest player presses Host, receives a Relay join code, and loads GameplayScene through Netcode. Desktop players submit a name and join with that code; Netcode synchronizes them into the same GameplayScene, including clients that join after the map has loaded. StartupScene stays loaded additively for its networking services, shared menu, and player data. Its test geometry and lighting are disabled during the transition. GameplayScene contains the gameplay environment without a second menu or NetworkManager.

## Quest test

In Unity's **File > Build Profiles**, select **Quest VR Host**, switch to that profile, and use **Build** or **Build And Run**. The profile asset is `Assets/Settings/Quest VR Host.asset`. It targets Android/APK and overrides its scene list to start in StartupScene, and include GameplayScene. The global desktop scene list also starts in StartupScene. Android OpenXR and Meta Quest Support are enabled, with the Oculus Touch controller profile.

On the headset:

1. A local tracking rig displays the existing menu as a floating panel before any network connection.
2. Name entry and desktop Join/code-entry controls are skipped for the VR host.
3. Point either controller's cyan ray at **Host**, then squeeze and release its trigger.
4. **B or Y** recenters the panel in front of your current head position. It otherwise stays fixed in the world.
5. On successful hosting, the menu disappears and tracking transfers to the networked Host_Player. The temporary menu rig is removed.

StartupScene has RelayManager's **Enable Online** enabled. Both devices need internet access and use Unity Relay's join code; no LAN IP entry is needed. The host code remains visible on the canvas after the Host menu disappears. Failed startup or client connection returns to the menu for retry. No name is assigned to the VR host.

Desktop players retain screen-space UI, mouse input, name entry, and the Join/code-entry flow. The server spawns Host_Player for the host and DesktopPlayer for joining clients once GameplayScene is loaded, then removes the connection placeholder. Initial positions are separated; dedicated scene spawn points can be added later.

## Editing and reuse

- **Canvas / SharedWorldSpaceCanvas:** reference layout 1920 x 1080; panel width 1.6 m; distance 1.5 m; height offset -0.1 m. Existing button bindings and layout children are preserved.
- **NetworkManager / VrMenuPresenter:** references the shared panels, Host_Player prefab, lobby camera and controls hidden only in VR. Additional shared root canvases can be added to Panels; assign distinct placement offsets if you display several simultaneously.
- **NetworkManager / VrUiInput:** extends the existing InputSystemUIInputModule with two tracked pointers. It uses a runtime copy of the UI action asset and restores the original configuration when disabled. No XRI package or replacement interaction system is required.
- **Host_Player / LocalVrPlayer:** enables the tracked rig only for its network owner, preventing another player's camera and tracking from activating locally. It sends the host's head and controller world poses at 30 Hz. Desktop replicas display interpolated head/hand placeholders with no tracking camera or gameplay colliders.
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

## Shared-scene connection validation

The editor host test loaded GameplayScene, selected it as the active scene, spawned Host_Player, removed the NetworkPlayer placeholder, and preserved one NetworkManager, one camera and one AudioListener. VR menu tracking handed off to the spawned player. A remote-owned host replica kept XR tracking disabled and displayed three placeholders at the supplied network poses. These checks do not substitute for a Quest-to-desktop session.

To test on devices, build/run Quest VR Host, press Host, then open StartupScene in the desktop editor (or use a desktop build), enter a name, and join using the code shown in the headset. Both should be in GameplayScene; the desktop should see the host's head and hands, and the headset should see the desktop character. Use the same project revision on both devices. RigPhysics interaction and shared grabbing/throwing are separate work from this connection pass.

The configured online Relay host was also exercised in the editor: allocation and join-code creation succeeded, the host started, GameplayScene loaded, and the console reported no errors. Android player script compilation completed with 85 assemblies. No updated APK or two-device session was run by the agent.

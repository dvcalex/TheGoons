# Testing Host_Player with Quest Link

## Editor workflow

1. Open Meta Horizon Link on the PC and connect the headset. Meta is already registered as this PC's active OpenXR runtime.
2. In the headset, launch Quest Link and enter the PC Link environment. A USB/ADB connection or a connected status in the PC app alone is not enough.
3. Open Assets/Scenes/AlexScene.unity in Unity. The active platform for this setup is Windows. Editor Play mode uses the Standalone XR configuration.
4. Press Play after Link is active. In VR, point a controller at the floating Host button and squeeze/release its trigger; VR skips name entry. Without XR, the desktop Game view retains name entry and mouse clicks. AlexScene currently uses local hosting (Enable Online is off), so no Relay account/session is needed for this test. See [Shared VR menu](SharedVrMenu.md).
5. Host_Player spawns through NetworkPlayer. The menu camera is destroyed on connection. The headset camera and the two small controller cubes should now track.
6. If Link was started after Play, stop Play and start it again to retry XR initialization.

## Configuration checked and corrected

- OpenXR loader and Initialize XR on Startup are enabled for Standalone and Android.
- Windows uses Single Pass Instanced rendering and now enables the Oculus Touch Controller Profile. Android already enabled that profile.
- Host_Player has an assigned XROrigin camera and Camera Offset, with three enabled Input System TrackedPoseDrivers.
- Pose and tracking-state actions now use Pass Through instead of Button.
- XROrigin requests Floor tracking; Camera Offset starts at zero. The device-mode fallback height is 1.65 m.
- The headset camera is tagged MainCamera; both stereo eyes and URP XR rendering are enabled.
- Host_Player is registered in the network prefab list. NetworkPlayer's existing VR_Override is false, selecting Host_Player when hosting.

## Validation and remaining hardware check (2026-10-10)

A local Play-mode host test successfully spawned one XR Origin and left one enabled camera. OpenXR project validation returned no issues. The scene and NetworkPlayer prefab were not edited during this setup.

Headset tracking could not be verified: the native OpenXR diagnostic report reached Meta's Oculus runtime (1.208.0), but xrGetSystem returned XR_ERROR_FORM_FACTOR_UNAVAILABLE. There was no active XR display and no headset/controller devices. This means the runtime did not make a headset available for that attempt; it does not establish that editor testing is unsupported. Complete the Link connection and retry the editor workflow above.

No APK was built. The current Android configuration has not been validated by a device build. The physics rig is not connected to Host_Player in this pass.

References:
- https://developers.meta.com/vr/documentation/unity/unity-link/
- https://docs.unity.cn/Packages/com.unity.xr.meta-openxr@2.1/manual/get-started/link.html
- https://registry.khronos.org/OpenXR/specs/1.0/man/html/xrGetSystem.html

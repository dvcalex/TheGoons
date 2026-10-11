# TrollRig

## Testing without VR input

VRTest contains a Physics Test Pose object with RigPhysicsTestPose. It supplies valid stationary head/hand targets to the existing motors and disables thumbstick travel for this test. In Play mode, expand TrollRig's TrackingFloor and move/rotate the generated Test targets in the Scene view to exercise tracking and contacts. Targets start at the authored physical poses and move with the tracking floor. Disable Physics Test Pose to release the motors and restore device input. Host_Player does not include this test driver.

Without this driver or tracked controllers, PilotPoseStream reports invalid tracking and ServoLimb deliberately releases its drives and gravity support. Hanging arms in that state do not indicate insufficient motor strength.

VRTest includes Physics Test Ground, a solid 40 m platform with its top at y=0, supporting the gravity-driven chassis. In Play mode use the Scene view, select a generated Test target under TrackingFloor, press W to move or E to rotate it, and drag the gizmo. Move targets rather than the physical fists. The test driver holds the pose automatically; keyboard/gamepad locomotion is not enabled in this test.

Drag `Assets/Prefabs/TrollRig.prefab` into a test scene. This is the assembled VR rig prefab: troll head, both visual arms and hands, and the existing physics, input, grabbing, and IK components. Use this prefab as the integration target for Host_Player. Its source models, materials, and tuning profiles remain shared asset dependencies.

The upper arms and forearms are tapered capsule meshes. They are children of the existing visual arm bones, driven by HumanoidArmIK. Mirrored curled hands are children of the physical fists. Fingers are fixed geometry, not individually animated. No new runtime scripts or colliders are required.

The smooth troll head is attached to Physical Head. TrackedHeadMotor drives this dynamic rigidbody toward the headset with bounded, damped forces and torque. Shoulder anchors follow the physical head, and the existing wrist servos follow the controllers. Host_Player hides only the head visual for its owner, leaving the physical collider active. This is a floating-head-and-arms prototype, not a complete humanoid body.

Original physics shapes are retained: visual surfaces, especially arm bulges and fingers, do not exactly match collision surfaces. Test reach, grip alignment, and first-person camera visibility in your actual desktop/VR player setup before replacing a gameplay prefab.

Models contains the editable source FBX parts. Materials reuse Assets/Art/TrollHead. The TrollRig prefab also depends on the existing RigPhysics scripts, profiles and audio. Keep those folders when moving this prototype.

Validation: inspected a Unity render; both visual IK chains solved three wrist poses; rigidbody, collider and joint counts match the original rig. This is not a headset/play-mode physics validation.

## Host_Player integration

`Assets/Prefabs/Host_Player.prefab` contains a nested TrollRig instance. Its XR Origin is a child of the rig's TrackingFloor, at unit scale with a zero local pose. TrackingRootFollower carries the camera with the physical chassis; SurfacePullMotor retains the rig's existing hand-contact locomotion.

OpenXRTransformInput on XR Origin reads Camera, Left Controller and Right Controller in Camera Offset space. Both PilotPoseStreams use scripted input in this instance, so the adapter is the only pose writer. Keep the tracked controllers enabled; their old placeholder cubes are disabled.

LocalVrPlayer activates the physics rig and XR Origin only for the owner and hides the local head visual. Remote players still see the existing replicated head/hand markers, now sampled from the physical head and fists rather than the raw tracked controllers. The existing packet layout and 30 Hz send rate are unchanged. This integration does not replicate the complete troll mesh, individual limb rigidbodies, grabs or effects. Use the existing host/network debug flow to spawn the player. Moving the Host_Player container during simulation is not a teleport procedure for its separate rigidbodies.

Validation: C# build and serialized prefab-reference checks passed. Unity play-mode and headset tracking, contact, grip and camera comfort still need testing.

## Giant arms (3x)

TrollRig now has upper-arm and forearm joint lengths of 1.95 m each (3.9 m total reach), 0.27 m arm collision radii, and 0.42 m fist collision radii. The physical and visual arms and hands are enlarged around their existing shoulder mounts. Root, chassis, head, tracking floor, and visual-bone scales are not globally enlarged. Spawn at unit root scale.

The saved rest pose opens the fists to 1.3 m center-to-center, avoiding the interpenetration caused by simply scaling the original pose. All six structural joint anchors coincide and the arm colliders clear the spawn floor. Joint projection tolerances and collider contact offsets are scaled with arm geometry.

Profiles/TrollArms3x.asset is the rig-level profile. Left and Right profiles have separate controller pivots so each amplified target equals shoulder + 3 * (the original target - shoulder). Head translation remains one-to-one and tracking-loss thresholds remain in real controller metres. All per-arm consumers share that arm's profile. The original DragDrop profile is unchanged.

Linear target/speed and servo force caps are 3x; angular servo/guide torque and angular limit springs/damping are 9x to account for the larger lever arms and inertia. Grab search and break distances are enlarged. Body/limb masses, damage, maximum grabbable mass, and throw caps retain the existing gameplay values; this is not a 27x density/mass scaling. Body locomotion speed caps remain unchanged.

The three TrollArms3x profiles enable responsiveArmDrives. Wrist position and rotation use ConfigurableJoint acceleration drives: PhysX accounts for effective mass/inertia rather than relying on our estimate of the articulated arm. Spring coefficients are per unit mass in this mode. Force/torque limits scale upward with actual arm and supported object mass relative to the authored arm mass, retaining bounded drives and impact recovery. Gravity support remains active while tracking is valid. Other profiles retain the original force drives.

All arm rigidbodies now receive the configured solver settings; TrollRig uses 24 position and 8 velocity iterations. Structural wrist joints allow free angular motion so the wrist rotation servo can match the controller without fighting the old 80-degree wrist limits. Shoulder/elbow limits, physical reach, collision response, and tracking-loss release remain in place. Hands can still be blocked by the environment or an unreachable target; these changes do not teleport them through obstacles.

Responsive drive validation: C# compilation and prefab/profile references checked. Holding the arms out, moving quickly, gripping a weighted prop and recovering from contact still require headset play-mode testing.

Validated: joint alignment, 3x physical/visual lengths, non-overlapping rest fists, floor clearance, controller mapping under head translation, and unit tracking/root scales. An editor-only isolated physics attempt did not establish stable runtime tracking and also failed on the unmodified baseline rig; do not treat it as a passed play-mode test. Live desktop/VR motion, grabs and contact recovery still need play-mode validation.

## Physics travel and tracking

Left thumbstick moves along the headset's forward/right plane, including pitch: look up and push forward to climb, or look down to descend. ThumbstickSlewMotor on TrollRig requests forces through HullMotor: default speed 2 m/s, acceleration 3 m/s², braking 4 m/s², response 0.35 seconds, deadzone 0.2. Diagonal input has the same speed cap. Release the stick to brake in all three axes and hold altitude. After losing tracking or pausing, release it before travel can resume.

Chassis remains the physical transport capsule. Its TrollTravel physics material removes ground friction so gentle acceleration does not stick against the floor. While slewing is armed with valid tracking, the motor compensates for chassis gravity with force so it can travel vertically. Tracking loss or hand-based pulling releases that support. Collisions still apply. TrackingRootFollower carries the camera with the chassis while room-scale head motion stays one-to-one. The separate physical head follows that tracked pose, so head movement never feeds itself back into the camera origin. Head collision does not forcibly move the user's camera away from their tracked position.

SurfacePullMotor takes priority while an arm is planted. Thumbstick braking resumes when it releases. Existing grip, strike, throw and contact effects remain available.

VrUiInput shows each controller's ray and cursor only while its tracked pointer hits UI. Both visuals disappear on a miss or tracking loss; click handling still uses the existing InputSystemUIInputModule.

Headset checks still required: lean/crouch and turn while stationary; travel in all four directions and release; push against a wall; plant a hand and pull; remove/re-wear the headset with the stick held; point onto/off menu panels; connect a desktop client and compare its markers to the physical fists. C# compilation and serialized reference checks do not validate comfort or collision feel.

## VR menu controls

Press the left controller's secondary button (Y on Quest) to show or hide the shared VR panels. Opening brings them in front of you. Hidden panels block neither pointer rays nor clicks; their UI objects remain active so connection and game services can continue running. Desktop presentation is restored when VR presentation stops.

Press the right controller's secondary button (B on Quest) to request XR tracking recenter. Panels reposition after the tracking pose updates. Recenter support depends on the runtime and tracking-origin mode; if it is unavailable, the console directs you to the headset's system recenter control. This does not calibrate avatar height or arm reach. VrMenuPresenter also exposes ToggleMenu and Calibrate for UI button callbacks. Controls require the existing VR menu presenter, so the standalone VRTest physics scene does not provide them.

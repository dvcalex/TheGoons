# TrollRig

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

Validated: joint alignment, 3x physical/visual lengths, non-overlapping rest fists, floor clearance, controller mapping under head translation, and unit tracking/root scales. An editor-only isolated physics attempt did not establish stable runtime tracking and also failed on the unmodified baseline rig; do not treat it as a passed play-mode test. Live desktop/VR motion, grabs and contact recovery still need play-mode validation.

## Physics travel and tracking

Left thumbstick moves horizontally relative to the headset. ThumbstickSlewMotor on TrollRig requests forces through HullMotor: default speed 2 m/s, acceleration 3 m/s², braking 4 m/s², response 0.35 seconds, deadzone 0.2. Diagonal input has the same speed cap. Release the stick to brake. After losing tracking or pausing, release it before travel can resume. Looking directly up or down falls back to tracking-space forward.

Chassis remains the grounded transport capsule. Its TrollTravel physics material removes ground friction so gentle acceleration does not stick against the floor. Gravity and collisions still apply; this is horizontal travel, not flight. TrackingRootFollower carries the camera with the chassis while room-scale head motion stays one-to-one. The separate physical head follows that tracked pose, so head movement never feeds itself back into the camera origin. Head collision does not forcibly move the user's camera away from their tracked position.

SurfacePullMotor takes priority while an arm is planted. Thumbstick braking resumes when it releases. Existing grip, strike, throw and contact effects remain available.

VrUiInput shows each controller's ray and cursor only while its tracked pointer hits UI. Both visuals disappear on a miss or tracking loss; click handling still uses the existing InputSystemUIInputModule.

Headset checks still required: lean/crouch and turn while stationary; travel in all four directions and release; push against a wall; plant a hand and pull; remove/re-wear the headset with the stick held; point onto/off menu panels; connect a desktop client and compare its markers to the physical fists. C# compilation and serialized reference checks do not validate comfort or collision feel.

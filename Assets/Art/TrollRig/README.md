# TrollRig

Drag `Assets/Prefabs/TrollRig.prefab` into a test scene. This is the assembled VR rig prefab: troll head, both visual arms and hands, and the existing physics, input, grabbing, and IK components. Use this prefab as the integration target for Host_Player. Its source models, materials, and tuning profiles remain shared asset dependencies.

The upper arms and forearms are tapered capsule meshes. They are children of the existing visual arm bones, driven by HumanoidArmIK. Mirrored curled hands are children of the physical fists. Fingers are fixed geometry, not individually animated. No new runtime scripts or colliders are required.

The smooth troll head is attached to Chassis as an optional visual. It follows Chassis, not the headset directly. Hide Troll Head Visual when using these arms in a first-person player if the head obstructs the camera. This is a floating-head-and-arms prototype, not a complete humanoid body.

Original physics shapes are retained: visual surfaces, especially arm bulges and fingers, do not exactly match collision surfaces. Test reach, grip alignment, and first-person camera visibility in your actual desktop/VR player setup before replacing a gameplay prefab.

Models contains the editable source FBX parts. Materials reuse Assets/Art/TrollHead. The TrollRig prefab also depends on the existing RigPhysics scripts, profiles and audio. Keep those folders when moving this prototype.

Validation: inspected a Unity render; both visual IK chains solved three wrist poses; rigidbody, collider and joint counts match the original rig. This is not a headset/play-mode physics validation.

## Host_Player integration

`Assets/Prefabs/Host_Player.prefab` contains a nested TrollRig instance. Its XR Origin is a child of the rig's TrackingFloor, at unit scale with a zero local pose. TrackingRootFollower carries the camera with the physical chassis; SurfacePullMotor retains the rig's existing hand-contact locomotion.

OpenXRTransformInput on XR Origin reads Camera, Left Controller and Right Controller in Camera Offset space. Both PilotPoseStreams use scripted input in this instance, so the adapter is the only pose writer. Keep the tracked controllers enabled; their old placeholder cubes are disabled.

LocalVrPlayer activates the physics rig and XR Origin only for the owner and hides the local head visual. Remote players still see the existing replicated head/hand markers; this integration does not replicate the physical troll limbs, grabs or effects. Use the existing host/network debug flow to spawn the player. Moving the Host_Player container during simulation is not a teleport procedure for its separate rigidbodies.

Validation: C# build and serialized prefab-reference checks passed. Unity play-mode and headset tracking, contact, grip and camera comfort still need testing.

## Giant arms (3x)

TrollRig now has upper-arm and forearm joint lengths of 1.95 m each (3.9 m total reach), 0.27 m arm collision radii, and 0.42 m fist collision radii. The physical and visual arms and hands are enlarged around their existing shoulder mounts. Root, chassis, head, tracking floor, and visual-bone scales are not globally enlarged. Spawn at unit root scale.

The saved rest pose opens the fists to 1.3 m center-to-center, avoiding the interpenetration caused by simply scaling the original pose. All six structural joint anchors coincide and the arm colliders clear the spawn floor. Joint projection tolerances and collider contact offsets are scaled with arm geometry.

Profiles/TrollArms3x.asset is the rig-level profile. Left and Right profiles have separate controller pivots so each amplified target equals shoulder + 3 * (the original target - shoulder). Head translation remains one-to-one and tracking-loss thresholds remain in real controller metres. All per-arm consumers share that arm's profile. The original DragDrop profile is unchanged.

Linear target/speed and servo force caps are 3x; angular servo/guide torque and angular limit springs/damping are 9x to account for the larger lever arms and inertia. Grab search and break distances are enlarged. Body/limb masses, damage, maximum grabbable mass, and throw caps retain the existing gameplay values; this is not a 27x density/mass scaling. Body locomotion speed caps remain unchanged.

Validated: joint alignment, 3x physical/visual lengths, non-overlapping rest fists, floor clearance, controller mapping under head translation, and unit tracking/root scales. An editor-only isolated physics attempt did not establish stable runtime tracking and also failed on the unmodified baseline rig; do not treat it as a passed play-mode test. Live desktop/VR motion, grabs and contact recovery still need play-mode validation.

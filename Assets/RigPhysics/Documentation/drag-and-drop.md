# Drag-and-drop mech kit

Original Unity physics prefabs, code and primitive geometry. Unity 6000.3 or newer.
These are reusable mechanics assets with simple placeholder visuals.

## Import and play

1. Double-click **MechDragDrop.unitypackage** and import all checked assets into your game.
2. Open `Assets/CleanRoomPhysics/DragDrop/Prefabs`. Drag **MechRig** onto your scene's floor. Its root is the spawn point at floor height. Move and rotate it before Play; keep root scale at `(1,1,1)`.
3. Drag in **LightCrate**, **HeavyCrate**, **PunchTarget** and **ClimbRail** wherever you need them. Place crates above the floor; the target root sits on the floor. Put rails within hand reach, about 1.3 metres high.
4. For a project with existing camera-based movement, use the **Existing OpenXR rig** setup below. For the prototype's physics-driven climbing, parent your XR Origin under **TrackingFloor**, reset its local pose/scale, use floor tracking, and disable competing locomotion. Your game supplies its own camera and XR loader.

Importing and compiling need **no OpenXR or Input System packages**. Live headset input still requires your game's working XR setup. The prefab uses Unity's built-in XR device API. With no headset the arms remain untracked; they do not animate from mouse input.

## Existing OpenXR rig (camera-based movement)

1. Keep your working XR Origin, tracked camera, controllers and camera-based locomotion. Place **MechRig** as a separate scene root at the player's starting floor position and facing. Keep both rigs at unit scale.
2. Add **OpenXRTransformInput** to MechRig. Assign **Rig**, **Head**, **Left Controller**, **Right Controller**, and their common **Tracking Space** parent (usually Camera Offset). Use the tracked controller poses, not the physical fists or an interactor's attach point.
3. Disable **SurfacePullMotor** on MechRig when your game owns locomotion. Leave the arm physics, joints, grip components and head-root followers enabled. Do not parent the external XR Origin under TrackingFloor in this setup.
4. Press Play with your headset. The adapter reads tracked transforms plus Unity XR grip/tracking flags. Moving the camera or its origin carries both physical shoulder anchors and wrist targets. Leaning and crouching move the shoulders; looking up/down does not rotate the whole body.

The adapter has no XRI/Input System assembly dependency and does not create a second camera or change your OpenXR settings. For custom input, uncheck **Read Device Input** and call `FeedPoses(leftGrip01, rightGrip01, headValid, leftValid, rightValid)` after updating your tracked transforms. Do not run another pose writer simultaneously. Reopen grips after tracking recovery. Disabling the adapter invalidates its input.

**HeadTrackedArmRoot** is serialized on each fist. Its Shoulder From Floor value preserves the authored shoulder placement relative to a reference head at 1.65 m; edit `referenceHeadPosition` on both PilotPoseStreams for a different reference height. Head displacement is applied at real-world scale even with amplified arm reach. The actual structural joint anchors move with the shoulder mounts; the dynamic limbs still collide and resist objects. Root speed is capped at 8 m/s during tracking jumps. This is arm-root following, not a full-body character controller or teleport handler. Use your game's teleport procedure to relocate all physical rig bodies together for large instantaneous moves.

## Included prefabs

| Asset | Ready-made behavior |
|---|---|
| MechRig | Physical chassis, two jointed arms, tracked hand inputs, grabbing, throws, strikes, surface pulling; all internal references assigned |
| LightCrate | 5 kg dynamic throwable with motion snapshot for strike detection |
| HeavyCrate | 15 kg version of the crate |
| PunchTarget | Articulated physical target with sample health, stagger and recovery |
| ClimbRail | Static collidable rail; move/rotate/resize as needed |

The kit profile is `DragDrop/Profiles/DragDrop.asset`; it is separate from the demo's Baseline. Duplicate it before tuning. Assign a replacement profile consistently to the rig's individual components, inputs and target; the MechRig root field is a convenient reference, not a propagating setter.

## Your own inputs and damage

For custom controller input, call `rig.UseExternalInput(true)` before supplying poses. Every render update call `rig.leftHand.FeedHead(time, headPosition, headValid)` and `rig.leftHand.Feed(time, handPosition, handRotation, grip01, handValid)`; repeat for the right hand. Use `Time.unscaledTimeAsDouble` and positions in metres relative to `rig.trackingFloor`. Feed invalid tracking when input is unavailable. Hand reach is scaled by the profile; head/camera movement stays at real-world scale. Start with open grips after tracking recovery.

The root **On Strike (float)** Inspector event supplies proposed damage for feedback hooks. For receiver-specific damage, subscribe to each `ImpactResponseSolver.Resolved` event and use the `StrikePacket.receiver`, `point`, `quality` and `damage` fields. Remove the target's `ReactionBody` if your own system applies damage. Add `ContactMotionSnapshot` to the exact Rigidbody GameObject of every custom moving strike receiver.

## Project compatibility

- Scenery and props use **Default (0)**; mech colliders use the built-in **Ignore Raycast (2)**. The kit profile interacts with layer 0. Your collision matrix must allow 0/2 collisions. If your game uses other layers, remap the profile masks. Static pulling excludes Rigidbody surfaces; moving platform anchors are unsupported.
- Internal mech collisions are disabled per collider pair, per instance. The kit does not change the global collision matrix, timestep, gravity, XR settings, camera, lighting or render pipeline. Recommended starting timestep is 1/90 s; set it yourself after checking your game's physics budget.
- Materials use the Built-in Standard shader. For URP/HDRP, convert the three kit materials with your pipeline's material converter or assign your own materials. Meshes/colliders are ordinary Unity primitives and can be reskinned without changing the physics hierarchy.
- Import this into a fresh game project or one without MechLab sources. Do not import it back into the full Prototype/demo project or alongside a second copy of its scripts. It uses the original script GUIDs and paths. Earlier `MechSystems` imports have a nested assembly with XR dependencies; remove that old `Runtime/MechLab.Runtime.asmdef` when replacing it with this standalone core kit and remove old optional `PulseFeedback` sources if retaining no OpenXR package.
- Stop Play mode before editing the rig. Do not move its container during simulation: the separate dynamic bodies already own their world poses. Spawn it with `Instantiate(prefab, spawnPosition, spawnRotation)` before physics begins. Put no scaling ancestors above it.

## Arm feel and placeholder effects

Powered arms support their own weight and the held load. `armGravitySupport` controls support; wrist/anchor inertia includes the held object's offset. Keep the object dynamic to retain collisions and resistance. Gripping searches while the grip is held, so pressing slightly before reaching a prop works. Throws use a 0.1-second peak-weighted physical-object history; speed limits still apply.

`MechanicalFeedback` on the wrists and crates supplies original metal impacts, clamp-close, release and swing sounds, pooled sparks and a wrist flash. Replace its AudioClip/Material fields in the Inspector. The original WAV files and spark material are in `DragDrop/Resources/MechLab`. A scene AudioListener is required. Subscribe to `MechanicalFeedback.Pulse(amplitude, seconds)` for your XR haptic adapter; the full prototype already connects this to OpenXR. Two fists can physically collide, while other same-rig collision pairs remain ignored. Allow their layer pair in your own project's physics matrix.

## Humanoid hands and elbow IK

Hands now carry an `InteractionSurface` set to Flesh. Tag weapons/props Metal; a contact involving Flesh plays the flesh impact without sparks. Replace the Flesh Impact/Grab/Release fields on `MechanicalFeedback` to customize the skin sounds.

Move the saved elbow hints beneath the shoulder mounts to adjust arm posture. `PhysicalArmIK` guides the collision arm with bounded torque; `HumanoidArmIK` drives the separate visual bone chain toward the physical wrist. To replace the placeholder with a model, assign its upper-arm, forearm and hand bones plus the physical wrist target/hint. A valid Humanoid Animator can fill the bones through **Bind humanoid arm bones** in the component context menu. Adjust Hand Rotation Offset for your model. This preserves bone lengths, supports generic hierarchies, and needs no Animation Rigging package. Use uniform skeleton scale. Full-body and finger IK are not included.

## Export again

In the source project, use **Mech Lab / Export drag-and-drop asset kit**. It bakes five serialized prefabs, preserves existing kit material/profile edits and exports `Builds/MechDragDrop.unitypackage`. The root `MechLab.Core.asmdef` is exported instead of the demo's nested runtime assembly. `Tools/export_drag_drop.ps1` can build the kit in an isolated project and place the package, loose Assets folder and zip in the repository's `Exports/MechDragDrop` directory.

Prefab/reference checks and automated physics checks do not establish real headset comfort, hardware compatibility, or matching another game's feel. Test these in your game's XR rig.

# TrollRig

Use `Assets/Prefabs/Host_Player.prefab` for the networked VR host. Its owner enables the nested `Assets/Prefabs/TrollRig.prefab` and binds the XR tracking space. Remote players render the real head and Scout arms from three head/hand poses and two byte-sized grip values. Their XR Origin, motors, colliders and contact effects stay disabled; the arm solver stays active.

## Runtime

- `VrRigController`: reads XR poses directly, maps hand reach, drives the physical head, and moves the chassis with the left thumbstick.
- `VrPhysicsHand` on each fist: applies PID forces/torque, handles grip-to-grab and release. The Rigidbody stays dynamic and collides with the world.
- `VrPidMotor` / `VrPidController`: the force controller adapted from Jalopy-Jetstream. Integral gain defaults to zero so contacts do not accumulate stored force.
- `ScoutArms`: two-bone visual arms and analog grip-driven finger curl.
- `TrollContactEffects`: pooled contact particles, with no gameplay control.

The TrollRig prefab no longer uses the legacy MechLab arm joints, proxies, elbow physics, surface-pulling motor, or its large tuning profiles. The shared MechLab library remains for other assets. Arm bones are visual only; the physical bodies are the chassis, head and two hands.

There is no desktop VR test mode, fake tracking, or automatic diagnostic overlay. VRTest retains its ground and rig placement only; use the real Host_Player with tracked controllers to drive the rig.

## Controls

Left thumbstick travels along the headset's forward/right plane, including up/down. Release to brake and hold altitude. Tracking loss releases the motors and requires a neutral stick before travel resumes. Grip squeezes/grabs; release lets the prop retain its physical velocity. Grabbable objects must have a dynamic Rigidbody on a layer included in each hand's Grabbable Layers mask, within the configured mass limit. One hand owns a prop at a time.

Y toggles the shared VR UI. B requests runtime tracking recenter and repositions the menu; support depends on the headset's tracking mode. These actions are provided by VrMenuPresenter in the normal startup flow.

Tune reach, forward offset and movement on VrRigController. Tune force response on VrPhysicsHand.motor. Distances and linear motion settings are authored at scale 1 and automatically follow the TrollRig root scale.

At scale 1, hands use 5x head-relative reach plus a 2.5 m forward offset, limited to 7 m from each shoulder. A controller 0.4 m ahead of your head requests roughly 4.5 m of forward reach. Both hands use the same mapping, so there are no mirrored lateral offsets that cross them.

## Scout arm asset

`Assets/Art/SK_Scout.fbx` is preserved unchanged. It contains arm geometry and a skeleton, but Unity imports it with no skin weights or bind poses. `ScoutArms.prefab` and `ScoutArms_Skinned.asset` are derived assets: 860 vertices bound to 36 arm/finger bones, with upper arms and forearms extended to 3.6 m each. The original hands retain their proportions. Skin weights were generated from bone-segment proximity; they are an initial binding, not artist-authored weights.

Each hand has 15 finger joints. Grip smoothly adds local finger flexion; thumb flexion is reduced. Visual wrists follow the physical hands, and shoulders follow headset yaw. The original capsule arms and fixed curled-hand meshes are removed from TrollRig. The source model had no supplied texture; the derived material uses a plain skin tone.

Validated in Unity: four rigidbodies, no permanent arm joints, intact Host_Player references, open/closed finger deformation, visual wrist alignment, PID settling for 8 kg and 80 kg bodies, and a hand blocked by a static wall. Compilation also passed. A fresh headset build is still needed to judge comfort, reach and grab feel.

## Hand alignment and contact

Select LeftFist or RightFist in TrollRig prefab isolation. On VrPhysicsHand, Use Controller Aim makes the open fingers follow the same pointing direction as the controller ray. Controller Rotation Offset adds pitch/yaw/roll in that frame; change it to tune your controller grip. The offset rotates the Rigidbody target, collider and visual together. The baked Scout handRotationOffset only converts the model's bone axes and should normally stay unchanged.

Each hand has a forward-facing capsule covering its palm and knuckle region instead of a wrist-centred sphere. Grab Point is inside the palm; the yellow selection gizmo shows its search radius, and the cyan ray shows physical hand forward. Grabs select the closest prop surface to the palm and anchor both bodies at that contact without snapping the prop to the wrist. Contacted props remain within grab range with the current defaults. Individual fingers are visual, not separate physics colliders.

## Scaling in the editor

Open `Assets/Prefabs/TrollRig.prefab` in prefab isolation, select the root, and set Transform Scale X, Y and Z to the same positive value. The current scale is **12, 12, 12**. Keep Host_Player and the XR Origin at local scale 1; the nested rig supplies the scale. Change scale before entering Play mode, rather than resizing active Rigidbody bodies. Scene instances with scale overrides must have those overrides reverted to inherit the prefab scale.

Mesh geometry, arm bones, colliders, local palm anchors and tracked poses inherit the transform scale. Runtime code scales shoulder/floor offsets, forward reach, reach limits, slew speed/acceleration, linear PID acceleration limits, hand speed, grab radius, contact effects, UI size/distance/rays and remote pose markers. Do not multiply the Inspector tuning values again. The dimensionless 5x reach multiplier, rotation settings, grip thresholds and timing stay unchanged. At scale 12, the 2.5 m forward bias becomes 30 world metres and the 7 m shoulder reach limit becomes 84 world metres.

Body masses and maximum grabbable mass intentionally retain their gameplay tuning; scaling does not apply a 1728x density-based mass increase. Grab break force scales linearly with acceleration and break torque quadratically with acceleration and lever length. Unity recalculates automatic inertia from the enlarged colliders. Gravity stays at the world's gravity.

Uniform root scaling is required: nonuniform or negative scale is not supported. Unity's Transform alone cannot scale scripted world-space distances, which is why those values explicitly read the rig's scale. No additional editor component is required.

## Networked desktop grabs

DesktopPlayer has a `PlayerGrab` component on its network root. The VR host selects nearby Player-layer capsules, including kinematic network replicas. It replicates the holding hand and attachment offset once when a grab starts, then releases with a capped throw velocity. A second hand cannot take an already held player. Tracking loss, hand disable or host despawn releases the grab.

The desktop owner keeps authority over its movement body. While held, a PID motor follows the existing replicated hand pose; walking, jumping, dashing and manual ragdoll toggles stop. The player can still look around, and world collisions remain active. Its existing NetworkTransform replicates movement to everyone else. This avoids an ownership handoff, a networked joint, and another continuous pose stream. Network latency can add some follow lag. Already-ragdolled players are not grabbable by this capsule-based system.

Host hand impacts also send a capped knockback to the desktop owner. `Maximum Throw Speed` on PlayerGrab limits both throws and hits (25 m/s by default). Grabbing uses the hands' existing Grabbable Layers mask, which includes Player. Both host and clients must use the updated scripts and DesktopPlayer prefab.

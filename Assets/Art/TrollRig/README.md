# TrollRig

Use `Assets/Prefabs/Host_Player.prefab` for the networked VR host. Its owner enables the nested `Assets/Prefabs/TrollRig.prefab` and binds the XR tracking space. Remote players still receive the same three head/hand poses.

## Runtime

- `VrRigController`: reads XR poses directly, maps hand reach, drives the physical head, and moves the chassis with the left thumbstick.
- `VrPhysicsHand` on each fist: applies PID forces/torque, handles grip-to-grab and release. The Rigidbody stays dynamic and collides with the world.
- `VrPidMotor` / `VrPidController`: the force controller adapted from Jalopy-Jetstream. Integral gain defaults to zero so contacts do not accumulate stored force.
- `TrollContactEffects`: pooled contact particles, with no gameplay control.

The TrollRig prefab no longer uses the legacy MechLab arm joints, proxies, elbow physics, surface-pulling motor, or its large tuning profiles. The shared MechLab library remains for other assets. Arm bones are visual only; the physical bodies are the chassis, head and two hands.

There is no desktop VR test mode, fake tracking, or automatic diagnostic overlay. VRTest retains its ground and rig placement only; use the real Host_Player with tracked controllers to drive the rig.

## Controls

Left thumbstick travels along the headset's forward/right plane, including up/down. Release to brake and hold altitude. Tracking loss releases the motors and requires a neutral stick before travel resumes. Grip squeezes/grabs; release lets the prop retain its physical velocity. Grabbable objects must have a dynamic Rigidbody on layer 10, within the configured mass limit. One hand owns a prop at a time.

Y toggles the shared VR UI. B requests runtime tracking recenter and repositions the menu; support depends on the headset's tracking mode. These actions are provided by VrMenuPresenter in the normal startup flow.

Tune reach, forward offset and movement on VrRigController. Tune force response on VrPhysicsHand.motor. The root uses unit scale; change these explicit reach settings instead of scaling the XR Origin hierarchy.

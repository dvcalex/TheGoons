# DesktopPlayer movement

Open `Assets/Scenes/AlexScene.unity`, press Play, and click Host in the existing
NetworkDebug UI. NetworkPlayer spawns DesktopPlayer through the existing flow
(the current NetworkPlayer prefab has VR_Override enabled). The scene contains
a crouch tunnel, a small jump platform, a ramp, and a pushable box, without an
extra scene-placed player. No other scene is changed. Controls use the existing input asset: WASD, mouse,
Left Shift to sprint, C to crouch, and Space to jump. Tap Space for a shorter hop.

## Implementation

`PlayerMovement` reads input in Update and applies forces in FixedUpdate. There
is no CharacterController, movement teleport, MovePosition, or direct velocity
assignment during locomotion. ResetVelocity/MoveTo remain explicit respawn APIs.
The motor capsule stays upright; yaw follows mouse aim through a bounded torque
servo. Mouse aim itself stays immediate, independent of physical yaw lag.

`VelocityServo` adapts the PID approach in Jalopy-Jetstream to a velocity target:
proportional velocity correction, filtered derivative on measurement, bounded
acceleration, and conditional integral accumulation. Integral gain defaults to
zero so walls and knockback don't accumulate a delayed movement surge. Ground
movement follows the support plane and accounts for support Rigidbody velocity.
Air control has a lower acceleration limit and preserves momentum without input.
Grounded state is actual support; coyote time only grants jump eligibility.

`DesktopRagdoll` builds a placeholder torso, head, two arms, and two legs with
Rigidbody/ConfigurableJoint components. The total is seven simulated bodies per
local player including the motor. The body parts have physical colliders and
driven angular poses; footsteps and lean come from measured velocity. They react
to objects and knockback, and their pose drives soften during stun. This is an
assisted active ragdoll with a stable capsule, not a fully collapsing humanoid.
Replacing its primitive meshes with a humanoid rig is a separate art/rigging step.

All self collisions are ignored per instance. The capsule material has no bounce
or friction so movement supplies deliberate braking without wall sticking. The
camera follows capsule crouch height rather than a wobbling head. Local body
meshes cast shadows but don't obstruct the first-person view.

## First tuning knobs

| Setting | Default | Effect |
| --- | --- | --- |
| Walk / sprint speed | 4.8 / 7.2 m/s | Overall pace |
| Velocity gain | 18 | Higher reaches target speed sooner |
| Ground acceleration / braking | 48 / 65 m/s² | Start/stop strength |
| Direction change acceleration | 75 m/s² | Responsiveness on reversals |
| Air acceleration | 12 m/s² | Air steering authority |
| Jump height | 0.85 m | Small held jump; tap jumps are lower |
| Coyote / buffer time | 0.12 / 0.14 s | Forgiveness near ledges and landing |
| Crouch height / transition | 1.1 m / 7 m/s | Fit and duck responsiveness |
| Pose spring / damper | 110 / 12 | Firmness versus wobble |
| Stride angle | 28° | Amount of limb swing |

Sprint stamina is optional and defaults off so initial feel testing is consistent.
There is no automatic stair teleport; jump over sharp steps. Rounded capsule and
physical slopes handle ordinary uneven surfaces.

## Networking boundary

No new RPCs, NetworkVariables, limb NetworkTransforms, or replication code were
added. Existing root Netcode components remain on the prefab. Player.cs retains
its original Start/IsOwner initialization. PlayerMovement remains a NetworkBehaviour
and movement/camera control uses the existing IsOwner checks. Only the owner applies
forces. Remote decorative body parts are kinematic with colliders disabled.

For later additions to the existing network implementation, keep one authoritative root body and send its
position, yaw, and velocity plus compact crouch/movement/action state. Reconstruct
gait and pose locally from these values instead of synchronizing six limbs. Use
interpolated root snapshots at a separately tuned send rate; transmit discrete
jump, stun, and hit events when they occur. Root collision/hit outcomes should
come from the chosen authority. Physical ragdolls are not deterministic, so input
alone is not sufficient for identical simulations across clients.

Only consider temporary extra pose snapshots for gameplay-significant collapse or
grabbing. Quantization, change thresholds, interest management, and a lower send
rate can be evaluated when extending networking. The existing transport and
root synchronization settings have not been retuned here.

## Verification

The optional editor helpers were removed. Earlier physics checks covered acceleration,
stopping, reversal, jumps, air momentum, crouch clearance, knockback recovery, and
joint stability. Two isolated Unity editors passed 38 host/client integration checks
using the existing local spawn/RPC flow: owner input, braking, crouching, jumping,
remote camera cleanup, remote kinematic bodies, and root movement replication in
both directions. No runtime errors were observed in that integration run.
Numerical checks are useful limits; the final feel still needs a hands-on playthrough.

## Unity MCP

A Unity MCP bridge can expose scene editing, prefab work, console access, and
test execution to an MCP client. The package is installed locally and privately
excluded from Git. The Unity editor was invoked in batch mode for verification.
One option is [MCP for Unity](https://github.com/CoplayDev/unity-mcp).
Its documented Package Manager Git URL is
`https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`.
After installation, use `Window > MCP for Unity` to configure the editor bridge
and client connection. Keep the connection scoped to this project while the
other agent is working in my-repo.

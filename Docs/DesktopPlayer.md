# DesktopPlayer movement

Open `Assets/Scenes/StartupScene.unity`, press Play, and click Host in the existing
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
Rigidbody/ConfigurableJoint components. Each client simulates six cosmetic bodies
for every visible player, attached to a local kinematic anchor. The detached
simulation hierarchy avoids inheriting network root rotation a second time.
Only the main capsule collides with gameplay objects; cosmetic colliders are
disabled and joints cannot feed forces back into the capsule. Driven angular
poses, footsteps, and lean follow root motion, with softer drives during stun.
Remote gait uses observed root displacement rather than remote Rigidbody velocity.
This is an assisted active ragdoll with a stable capsule, not a fully collapsing humanoid.
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

Existing root Netcode components remain on the prefab. Player.cs retains
its original Start/IsOwner initialization. PlayerMovement remains a NetworkBehaviour
and movement/camera control uses the existing IsOwner checks. Only the owner applies
forces to the gameplay capsule. Both owned and remote decorative body parts are
dynamic, with colliders disabled. Their local kinematic anchor follows the root
position received through the existing NetworkTransform. Dedicated servers skip
cosmetic simulation. No limb transforms are transmitted.

PlayerMovement owns one owner-written NetworkVariable for a seven-byte pose payload
(plus Netcode overhead): continuous float yaw, byte crouch amount, byte flags for
grounded/stunned, and signed byte head pitch. Updates are limited to 20/sec and
unchanged values are not transmitted. Yaw is quantized to half a degree, crouch
to 1/255, and pitch to approximately 0.7 degrees. Full current state is available
to late joiners. Remote capsule height/center and local joint targets follow this
state, including crouch blocked by a ceiling rather than just crouch input.

DesktopPlayer's NetworkTransform sends position only. The compact pose carries
physical capsule yaw as a continuous scalar; remote interpolation preserves turn
direction across 360-degree boundaries or snapshots spanning over half a turn.
This avoids combining NetworkTransform rotation smoothing with pose smoothing.
Remote root yaw and head pitch use a short smoothing response; local mouse aim
and force-based owner rotation retain their existing behavior.

Keep DesktopPlayer's serialized NetworkObject prefab hash in sync with Unity's
validated asset identity. Its old serialized hash differed from the main editor's
recalculated value, causing NetworkConfig mismatch with Multiplayer Play Mode
Player 2. Saving the validated prefab and reloading Player 2 resolves that stale
asset discrepancy; ForceSamePrefabs remains enabled.

For later additions to the existing network implementation, keep one authoritative root body and send its
position and velocity plus compact movement/action state. Reconstruct
gait and pose locally from these values instead of synchronizing six limbs. Use
interpolated root snapshots at a separately tuned send rate; transmit discrete
jump, stun, and hit events when they occur. Root collision/hit outcomes should
come from the chosen authority. Physical ragdolls are not deterministic, so input
alone is not sufficient for identical simulations across clients.

Only consider temporary extra pose snapshots for gameplay-significant collapse or
grabbing. Quantization, change thresholds, interest management, and a lower send
rate can be evaluated when extending networking. The existing transport and
position synchronization settings have not been retuned here.

## Verification

The optional editor helpers were removed. Earlier physics checks covered acceleration,
stopping, reversal, jumps, air momentum, crouch clearance, knockback recovery, and
joint stability. Two isolated Unity editors passed 70 host/client integration checks
using the existing local spawn/RPC flow: owner input, braking, crouching, jumping,
remote camera cleanup, remote kinematic capsules with locally simulated limbs,
and root movement replication in both directions. Actual 180-degree physics turns
kept meshes aligned and limbs close to both capsules. Limb impulses did not move
the gameplay capsule. Remote crouching/standing, head pitch, grounded/stun flags,
and recovery passed on both peers. A simulated 0.7-second pose delivery gap
produced 331 degrees between snapshots without reversing remote rotation.
No runtime errors were observed in that integration run.
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

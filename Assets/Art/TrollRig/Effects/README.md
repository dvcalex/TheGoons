# Troll contact effects

Two small effects are wired to both hands of `../Prefabs/TrollRig.prefab`:

- **TrollImpact**: a brief burst on a sufficiently fast hand contact.
- **TrollDust**: a small dust puff when the hand strikes a static upward-facing surface.

`TrollContactEffects` only handles presentation. The existing seven rigidbodies, seven colliders, six joints, models, arm profiles, input, grabbing, damage and audio are retained. Each hand reuses one instance of each effect. Disable this component to remove the added visuals.

Tune `minimumSpeed`, `cooldownSeconds` and `effectScale` on either hand. Defaults are 1.4 m/s, 0.12 seconds and 0.45. No continuous idle particles or new models were added.

The textures, impact quad and initial particle data come from the local licensed UNDERDOGS installation: `MP - Blunt Hit Small FX` (resources.assets object 17099) and the `Dust` child (7301) of `Drag On Floor`. Source records are retained in SourceData. Materials use the existing URP Particles/Unlit shader; this is a rendering adaptation, not the original compiled shaders. Dust size, color, lifetime and emission are reduced for this rig; repeated impact bursts are trimmed to a single contact burst.

The existing `Assets/RigPhysics` is already the reusable mechanics kit. Keep its per-arm TrollArms3x profiles, `OpenXRTransformInput`, `HumanoidArmIK`, `TetherGrip`, and `ImpactResponseSolver` together; do not import a second copy of MechLab.Core. External input must retain unit tracking-space scale and real controller metres. Reach amplification stays in the per-arm profiles.

The importer decodes packed texture channels into alpha masks and reapplies particle colors, gradients and curves through Unity's public API to accommodate the source/editor version difference. Original source images remain in SourceData.

Validation: Unity compiles without errors; both hand references and material shaders are valid; an isolated particle simulation emits 15 impact particles and 5 dust particles at 40 ms. The rendered preview was inspected (Logs/troll-contact-effects.png). Rig counts remain seven rigidbodies, seven colliders and six joints. Headset contact testing remains to be done in the project's player scene.

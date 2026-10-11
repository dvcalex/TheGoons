using System;
using UnityEngine;

namespace MechLab
{
    // Package-independent sound/visual feedback; XR haptics subscribe through PulseFeedback.
    [RequireComponent(typeof(AudioSource))]
    public sealed class MechanicalFeedback : MonoBehaviour
    {
        public MechTuning tuning;
        public AudioClip impactClip;
        public AudioClip grabClip;
        public AudioClip releaseClip;
        public AudioClip swingClip;
        public AudioClip fleshImpactClip;
        public AudioClip fleshGrabClip;
        public AudioClip fleshReleaseClip;

        public InteractionKind lastInteraction
        {
            get;
            private set;
        }

        public Material sparkMaterial;
        public event Action<float, float> Pulse;

        public int contactPulses
        {
            get;
            private set;
        }

        private AudioSource speaker;
        private ServoLimb limb;
        private TetherGrip grip;
        private Renderer shell;
        private MaterialPropertyBlock properties;
        private Color shellColor;
        private float nextContact;
        private float nextSwing;
        private float flashUntil;
        private bool swingArmed = true;
        private readonly LineRenderer[] sparks = new LineRenderer[8];
        private readonly Vector3[] origins = new Vector3[8];
        private readonly Vector3[] velocities = new Vector3[8];
        private float sparkStart = -100f;

        private void Awake()
        {
            speaker = GetComponent<AudioSource>();
            speaker.playOnAwake = false;
            speaker.spatialBlend = 1f;
            speaker.minDistance = 1f;
            speaker.maxDistance = 22f;
            limb = GetComponent<ServoLimb>();
            grip = GetComponent<TetherGrip>();
            if (!tuning && limb)
            {
                tuning = limb.tuning;
            }

            if (!impactClip)
            {
                impactClip = Resources.Load<AudioClip>("MechLab/MetalImpact");
            }

            if (!grabClip)
            {
                grabClip = Resources.Load<AudioClip>("MechLab/ClampClose");
            }

            if (!releaseClip)
            {
                releaseClip = Resources.Load<AudioClip>("MechLab/ClampRelease");
            }

            if (!swingClip)
            {
                swingClip = Resources.Load<AudioClip>("MechLab/ArmSwing");
            }

            if (!fleshImpactClip)
            {
                fleshImpactClip = Resources.Load<AudioClip>("MechLab/FleshImpact");
            }

            if (!fleshGrabClip)
            {
                fleshGrabClip = Resources.Load<AudioClip>("MechLab/FleshGrip");
            }

            if (!fleshReleaseClip)
            {
                fleshReleaseClip = Resources.Load<AudioClip>("MechLab/FleshRelease");
            }

            shell = GetComponent<Renderer>();
            properties = new MaterialPropertyBlock();
            if (shell && shell.sharedMaterial)
            {
                shellColor = shell.sharedMaterial.color;
            }

            if (!sparkMaterial)
            {
                sparkMaterial = Resources.Load<Material>("MechLab/Spark");
            }

            if (sparkMaterial)
            {
                for (int i = 0; i < sparks.Length; i++)
                {
                    var node = new GameObject("Pooled contact spark");
                    node.transform.SetParent(transform, false);
                    var line = node.AddComponent<LineRenderer>();
                    line.sharedMaterial = sparkMaterial;
                    line.positionCount = 2;
                    line.startWidth = .012f;
                    line.endWidth = .002f;
                    line.useWorldSpace = true;
                    line.enabled = false;
                    sparks[i] = line;
                }
            }
        }

        private void OnEnable()
        {
            if (grip)
            {
                grip.GripChanged += GripChanged;
            }
        }

        private void OnDisable()
        {
            if (grip)
            {
                grip.GripChanged -= GripChanged;
            }

            foreach (var line in sparks)
            {
                if (line)
                {
                    line.enabled = false;
                }
            }
        }

        private void GripChanged(bool holding)
        {
            bool flesh = InteractionSurface.Resolve(this) == InteractionKind.Flesh;
            Play(
                holding ? (flesh ? fleshGrabClip : grabClip) : (flesh ? fleshReleaseClip : releaseClip),
                .65f,
                1f
            );
            Pulse?.Invoke(holding ? .3f : .15f, holding ? .045f : .025f);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!enabled || Time.time < nextContact || collision.contactCount == 0)
            {
                return;
            }

            // The hand owns a hand/object contact's feedback, avoiding a doubled sound.
            if (!limb && collision.rigidbody && collision.rigidbody.GetComponent<ServoLimb>())
            {
                return;
            }

            if (limb && !limb.operational)
            {
                return;
            }

            float speed = collision.relativeVelocity.magnitude;
            if (speed < .45f)
            {
                return;
            }

            var contact = collision.GetContact(0);
            float strength = Mathf.InverseLerp(.4f, 7f, speed);
            Contact(
                strength,
                contact.point,
                contact.normal,
                true,
                InteractionSurface.Resolve(contact.otherCollider),
                InteractionSurface.Resolve(contact.thisCollider)
            );
            if (!limb)
            {
                TetherGrip.RelayContact(GetComponent<Rigidbody>(), strength, contact.point, contact.normal);
            }

            if (limb && collision.rigidbody && collision.rigidbody.GetComponent<ServoLimb>())
            {
                limb.DisruptAfterImpact(strength);
            }
        }

        public void Contact(
            float strength,
            Vector3 point,
            Vector3 normal,
            bool playSound = true,
            InteractionKind otherSurface = InteractionKind.Metal,
            InteractionKind? sourceSurface = null
        )
        {
            if (Time.time < nextContact)
            {
                return;
            }

            nextContact = Time.time + .075f;
            contactPulses++;
            strength = Mathf.Clamp01(strength);
            lastInteraction = InteractionSurface.Pair(sourceSurface ?? InteractionSurface.Resolve(this), otherSurface);
            bool flesh = lastInteraction == InteractionKind.Flesh;
            if (playSound)
            {
                Play(
                    flesh ? fleshImpactClip : impactClip,
                    Mathf.Lerp(.24f, 1f, Mathf.Sqrt(strength)),
                    Mathf.Lerp(1.12f, .78f, strength)
                );
            }

            Pulse?.Invoke(
                Mathf.Lerp(.12f, tuning ? tuning.maximumHapticAmplitude : .8f, Mathf.Sqrt(strength)),
                Mathf.Lerp(.025f, .085f, strength)
            );
            flashUntil = flesh ? -1f : Time.time + .045f;
            sparkStart = flesh ? -100f : Time.time;
            for (int i = 0; i < sparks.Length; i++)
            {
                float a = i * 2.39996f;
                Vector3 spread = new Vector3(Mathf.Cos(a), Mathf.Sin(a), Mathf.Cos(a * 1.7f));
                origins[i] = point;
                velocities[i] = (normal + spread * .9f) * Mathf.Lerp(.6f, 2.8f, strength);
            }
        }

        private void Play(AudioClip clip, float gain, float pitch)
        {
            if (!speaker || !clip)
            {
                return;
            }

            speaker.pitch = pitch;
            speaker.PlayOneShot(clip, gain * (tuning ? tuning.audioGain : .6f));
        }

        private void Update()
        {
            if (limb && limb.operational && limb.wrist)
            {
                float speed = (limb.wrist.linearVelocity - limb.chassis.GetPointVelocity(limb.wrist.position)).magnitude;
                if (speed < 1.2f)
                {
                    swingArmed = true;
                }

                if (swingArmed && speed > 2.2f && Time.time > nextSwing)
                {
                    Play(
                        swingClip,
                        Mathf.InverseLerp(1f, 7f, speed) * .7f,
                        Mathf.Lerp(.8f, 1.2f, speed / 12f)
                    );
                    swingArmed = false;
                    nextSwing = Time.time + .2f;
                }
            }

            if (shell && limb)
            {
                shell.GetPropertyBlock(properties);
                Color color = Time.time < flashUntil ? new Color(1f, .75f, .32f) : shellColor;
                properties.SetColor("_Color", color);
                properties.SetColor("_BaseColor", color);
                shell.SetPropertyBlock(properties);
            }

            float age = Time.time - sparkStart;
            for (int i = 0; i < sparks.Length; i++)
            {
                if (sparks[i])
                {
                    sparks[i].enabled = age >= 0f && age < .13f;
                    if (!sparks[i].enabled)
                    {
                        continue;
                    }

                    Vector3 point = origins[i] + velocities[i] * age + Vector3.down * (age * age * 3f);
                    sparks[i].SetPosition(0, point);
                    sparks[i].SetPosition(1, point - velocities[i] * .022f);
                }
            }
        }
    }
}

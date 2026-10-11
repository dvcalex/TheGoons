using UnityEngine;
using MechLab;

namespace TrollPresentation
{
    // Presentation only: no forces, damage, input, or changes to the rig's colliders.
    public sealed class TrollContactEffects : MonoBehaviour
    {
        public GameObject impactPrefab;
        public GameObject dustPrefab;

        [Min(0f)] public float minimumSpeed = 1.4f;
        [Min(0.02f)] public float cooldownSeconds = 0.12f;
        [Min(0.01f)] public float effectScale = 0.45f;

        private ServoLimb limb;
        private GameObject impact;
        private GameObject dust;
        private float nextAllowed;

        private void Awake()
        {
            limb = GetComponent<ServoLimb>();
        }

        private void OnCollisionEnter(Collision contact)
        {
            if (!enabled || !limb || !limb.operational || contact.contactCount == 0)
            {
                return;
            }

            if (contact.relativeVelocity.sqrMagnitude < minimumSpeed * minimumSpeed)
            {
                return;
            }

            ContactPoint hit = contact.GetContact(0);
            bool groundContact = hit.normal.y > 0.5f && !contact.rigidbody;

            Emit(hit.point, hit.normal, groundContact);
        }

        public void Emit(Vector3 point, Vector3 normal, bool groundContact)
        {
            if (!isActiveAndEnabled || Time.time < nextAllowed)
            {
                return;
            }

            GameObject prefab;
            GameObject instance;
            Vector3 effectDirection;
            string effectName;

            if (groundContact)
            {
                prefab = dustPrefab;
                instance = dust;
                effectDirection = Vector3.up;
                effectName = "Troll contact - dust";
            }
            else
            {
                prefab = impactPrefab;
                instance = impact;
                effectDirection = Vector3.forward;
                effectName = "Troll contact - impact";
            }

            if (!prefab)
            {
                return;
            }

            nextAllowed = Time.time + cooldownSeconds;

            if (!instance)
            {
                instance = Instantiate(prefab);
                instance.name = effectName;

                if (groundContact)
                {
                    dust = instance;
                }
                else
                {
                    impact = instance;
                }
            }

            instance.SetActive(true);
            instance.transform.SetPositionAndRotation(
                point + normal * 0.025f,
                Quaternion.FromToRotation(effectDirection, normal));
            instance.transform.localScale = Vector3.one * effectScale;

            foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>())
            {
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.Play(false);
            }
        }

        private void OnDisable()
        {
            if (impact)
            {
                impact.SetActive(false);
            }

            if (dust)
            {
                dust.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (impact)
            {
                Destroy(impact);
            }

            if (dust)
            {
                Destroy(dust);
            }
        }
    }
}

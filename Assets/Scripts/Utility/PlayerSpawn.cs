using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerSpawn : MonoBehaviour
{
    public static PlayerSpawn _instance;

    [Min(0.1f)] public float spacing = 2f;
    [Min(0.05f)] public float groundClearance = 0.25f;
    [Min(1f)] public float probeHeight = 100f;
    [Min(1f)] public float probeDepth = 500f;
    [Range(1, 10)] public int searchRings = 6;

    private struct Reservation
    {
        public Vector3 position;
        public float expires;
    }

    private readonly List<Reservation> reservations = new List<Reservation>();
    private readonly RaycastHit[] groundHits = new RaycastHit[64];
    private readonly Collider[] overlaps = new Collider[64];

    private void Awake()
    {
        _instance = this;
    }

    // Called by the server before instantiation. Reserving immediately keeps
    // simultaneous joins apart, even before their colliders enter physics.
    public bool TryGetPosition(GameObject prefab, out Vector3 position)
    {
        position = Vector3.zero;
        PlayerMovement movement = prefab.GetComponentInChildren<PlayerMovement>(true);
        if (movement == null || movement.capsule == null)
        {
            return false;
        }

        CapsuleCollider capsule = movement.capsule;
        Vector3 scale = capsule.transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(movement.standingHeight * Mathf.Abs(scale.y), radius * 2f);
        Vector3 centerOffset = Quaternion.Inverse(prefab.transform.rotation)
            * (capsule.transform.TransformPoint(capsule.center) - prefab.transform.position);
        float step = Mathf.Max(spacing, radius * 2f + groundClearance * 2f);

        for (int i = reservations.Count - 1; i >= 0; i--)
        {
            if (reservations[i].expires <= Time.unscaledTime)
            {
                reservations.RemoveAt(i);
            }
        }

        Physics.SyncTransforms();
        PhysicsScene physics = gameObject.scene.GetPhysicsScene();

        for (int ring = 0; ring <= searchRings; ring++)
        {
            for (int x = -ring; x <= ring; x++)
            {
                for (int z = -ring; z <= ring; z++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != ring)
                    {
                        continue;
                    }

                    Vector3 candidate = transform.position + new Vector3(x * step, 0f, z * step);
                    if (!TryFindGround(physics, candidate, movement, out Vector3 ground))
                    {
                        continue;
                    }

                    if (IsReserved(ground, step))
                    {
                        continue;
                    }

                    Vector3 bottom = ground + Vector3.up * (radius + groundClearance);
                    Vector3 top = ground + Vector3.up * (height - radius + groundClearance);
                    int count = physics.OverlapCapsule(bottom, top, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
                    if (count > 0)
                    {
                        continue;
                    }

                    position = ground + Vector3.up * (height * 0.5f + groundClearance) - centerOffset;
                    reservations.Add(new Reservation { position = ground, expires = Time.unscaledTime + 3f });
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryFindGround(PhysicsScene physics, Vector3 candidate, PlayerMovement movement, out Vector3 ground)
    {
        ground = Vector3.zero;
        Vector3 start = candidate + Vector3.up * probeHeight;
        int count = physics.Raycast(start, Vector3.down, groundHits, probeHeight + probeDepth,
            movement.groundMask, QueryTriggerInteraction.Ignore);
        if (count == groundHits.Length)
        {
            return false;
        }

        float nearest = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider.gameObject.scene != gameObject.scene
                || hit.collider.GetComponentInParent<Player>() != null
                || Physics.GetIgnoreLayerCollision(movement.gameObject.layer, hit.collider.gameObject.layer))
            {
                continue;
            }

            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
            {
                continue;
            }

            if (Vector3.Angle(hit.normal, Vector3.up) > movement.maximumGroundAngle || hit.distance >= nearest)
            {
                continue;
            }

            nearest = hit.distance;
            ground = hit.point;
        }

        return nearest < float.PositiveInfinity;
    }

    private bool IsReserved(Vector3 ground, float distance)
    {
        foreach (Reservation reservation in reservations)
        {
            Vector3 difference = ground - reservation.position;
            difference.y = 0f;
            if (difference.sqrMagnitude < distance * distance - 0.001f)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }
}

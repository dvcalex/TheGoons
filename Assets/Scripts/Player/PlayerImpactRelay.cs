using UnityEngine;

// The torso receives collision callbacks while the movement capsule is disabled.
public sealed class PlayerImpactRelay : MonoBehaviour
{
    public Player player;

    private void OnCollisionEnter(Collision collision)
    {
        if (player == null || collision.transform.IsChildOf(player.transform))
        {
            return;
        }

        player.ApplyImpact(collision, GetComponent<Rigidbody>());
    }
}

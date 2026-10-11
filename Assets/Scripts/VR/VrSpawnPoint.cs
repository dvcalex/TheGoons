using UnityEngine;
using UnityEngine.SceneManagement;

// Position is the VR tracking floor; the blue arrow sets its forward direction.
public sealed class VrSpawnPoint : MonoBehaviour
{
    public static VrSpawnPoint FindInScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return null;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            VrSpawnPoint spawn = root.GetComponentInChildren<VrSpawnPoint>();
            if (spawn != null)
            {
                return spawn;
            }
        }

        return null;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawRay(transform.position, transform.forward * 3f);
    }
}

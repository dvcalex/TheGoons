using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SharedGameSession : MonoBehaviour
{
    public const string GameScene = "GameplayScene";
    private NetworkManager manager;
    private Scene startup;

    private void Awake()
    {
        manager = GetComponent<NetworkManager>();
        startup = gameObject.scene;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    public void LoadGame()
    {
        if (!manager.IsServer || SceneManager.GetSceneByName(GameScene).isLoaded)
        {
            return;
        }

        manager.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
        var status = manager.SceneManager.LoadScene(GameScene, LoadSceneMode.Additive);
        if (status != SceneEventProgressStatus.Started)
        {
            throw new System.InvalidOperationException("Could not load " + GameScene + ": " + status);
        }
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != GameScene)
        {
            return;
        }

        SceneManager.SetActiveScene(scene);
        // Keep an editor preview camera in the map, but use only player cameras in a session.
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "Scene Preview Camera")
            {
                root.SetActive(false);
            }
        }

        // Keep startup services and UI; hide the old test geometry and lighting.
        foreach (var root in startup.GetRootGameObjects())
        {
            if (root.GetComponent<NetworkObject>() != null
                || root.GetComponent<PlayerData>() != null
                || root.GetComponent<NetworkManager>() != null
                || root.GetComponentInChildren<Canvas>(true) != null
                || root.GetComponent<UnityEngine.EventSystems.EventSystem>() != null)
            {
                continue;
            }

            root.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
    }
}

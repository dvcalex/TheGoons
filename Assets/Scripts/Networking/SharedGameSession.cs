using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public sealed class SharedGameSession : MonoBehaviour
{
    public const string GameScene = "GameplayScene";
    private NetworkManager manager;
    private Scene startup;
    private bool mapReady;
    private bool resetting;

    public bool MapReady
    {
        get { return mapReady; }
    }

    private void Awake()
    {
        manager = GetComponent<NetworkManager>();
        startup = gameObject.scene;
        mapReady = SceneManager.GetSceneByName(GameScene).isLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    public void LoadGame()
    {
        if (!manager.IsServer || SceneManager.GetSceneByName(GameScene).isLoaded)
        {
            return;
        }

        manager.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
        manager.SceneManager.OnLoadEventCompleted -= MapLoaded;
        manager.SceneManager.OnLoadEventCompleted += MapLoaded;
        mapReady = false;
        var status = manager.SceneManager.LoadScene(GameScene, LoadSceneMode.Additive);
        if (status != SceneEventProgressStatus.Started)
        {
            throw new System.InvalidOperationException("Could not load " + GameScene + ": " + status);
        }
    }

    private void MapLoaded(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
    {
        if (sceneName == GameScene)
        {
            mapReady = true;
        }
    }

    public void RestartGame()
    {
        if (manager.IsServer && !resetting)
        {
            StartCoroutine(RestartRoutine());
        }
    }

    private IEnumerator RestartRoutine()
    {
        resetting = true;
        mapReady = false;
        bool unloaded = false;
        bool loaded = false;
        bool success = false;

        NetworkSceneManager.OnEventCompletedDelegateHandler onUnload = (name, mode, completed, timedOut) =>
        {
            if (name == GameScene)
            {
                unloaded = true;
            }
        };
        NetworkSceneManager.OnEventCompletedDelegateHandler onLoad = (name, mode, completed, timedOut) =>
        {
            if (name == GameScene)
            {
                loaded = true;
            }
        };

        manager.SceneManager.OnUnloadEventCompleted += onUnload;
        manager.SceneManager.OnLoadEventCompleted += onLoad;
        try
        {
            // Fresh desktop objects reset health, ragdolls, abilities and spectators.
            // Keep the VR host and its camera alive while the map is reloaded.
            foreach (var client in new List<NetworkClient>(manager.ConnectedClients.Values))
            {
                NetworkObject player = client.PlayerObject;
                if (player != null && player.GetComponent<LocalVrPlayer>() == null)
                {
                    player.Despawn();
                }
            }

            Scene scene = SceneManager.GetSceneByName(GameScene);
            if (scene.isLoaded)
            {
                if (manager.SceneManager.UnloadScene(scene) != SceneEventProgressStatus.Started)
                {
                    Debug.LogError("Could not unload GameplayScene for the next round.");
                    yield break;
                }

                yield return new WaitUntil(() => unloaded || !manager.IsServer);
                yield return null;
            }

            if (!manager.IsServer)
            {
                yield break;
            }

            if (manager.SceneManager.LoadScene(GameScene, LoadSceneMode.Additive) != SceneEventProgressStatus.Started)
            {
                Debug.LogError("Could not reload GameplayScene for the next round.");
                yield break;
            }

            yield return new WaitUntil(() => loaded || !manager.IsServer);
            yield return null;
            success = manager.IsServer;
        }
        finally
        {
            if (manager.SceneManager != null)
            {
                manager.SceneManager.OnUnloadEventCompleted -= onUnload;
                manager.SceneManager.OnLoadEventCompleted -= onLoad;
            }

            resetting = false;
            mapReady = success;
            if (GameManager._instance != null)
            {
                GameManager._instance.FinishRoundReset(success);
            }
        }

        if (!success)
        {
            yield break;
        }

        foreach (var client in new List<NetworkClient>(manager.ConnectedClients.Values))
        {
            if (client.PlayerObject == null)
            {
                manager.SpawnManager.InstantiateAndSpawn(manager.NetworkConfig.PlayerPrefab.GetComponent<NetworkObject>(),
                    client.ClientId, false, true);
            }
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
                || root.GetComponent<Unity.XR.CoreUtils.XROrigin>() != null
                || root.GetComponent<AudioManager>() != null
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
        if (manager != null && manager.SceneManager != null)
        {
            manager.SceneManager.OnLoadEventCompleted -= MapLoaded;
        }
    }
}

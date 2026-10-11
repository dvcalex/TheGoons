using UnityEngine;
using UnityEngine.SceneManagement;

// Attach to the parent whose children should be removed when gameplay starts.
public sealed class DeleteChildrenOnGameplayLoad : MonoBehaviour
{
    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;

        Scene gameplayScene = SceneManager.GetSceneByName(SharedGameSession.GameScene);
        if (gameplayScene.isLoaded)
        {
            DeleteChildren();
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SharedGameSession.GameScene)
        {
            DeleteChildren();
        }
    }

    private void DeleteChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }
    }
}

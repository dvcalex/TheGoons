using UnityEngine;

public class PlayerSpawn : MonoBehaviour
{
    public static PlayerSpawn _instance;

    void Awake()
    {
        _instance = this;
    }
}

using UnityEngine;

public class PlayerData : MonoBehaviour
{
    public static PlayerData _instance;

    public string playerName;

    private void Awake()
    {
        if(_instance == null)
            _instance = this;
        else
            Destroy(this);
    }
}

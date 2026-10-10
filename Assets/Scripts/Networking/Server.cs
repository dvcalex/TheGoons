using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
using System;

public class Server : NetworkBehaviour
{
    public static Server _instance;
    
    private List<Player> _players = new List<Player>();

    public Action onPlayerListUpdated;

    private void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        onPlayerListUpdated += PlayerListUpdated;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        onPlayerListUpdated -= PlayerListUpdated;
    }

    private void Awake()
    {
        if (_instance == null)
            _instance = this;
        else
            Destroy(gameObject);
    }

    private void OnClientConnected(ulong obj)
    {
        Debug.Log($"Client connected: {obj}");
    }

    private void OnClientDisconnected(ulong obj)
    {
        Debug.Log($"Client disconnected: {obj}");
    }

    private void PlayerListUpdated()
    {
        Debug.Log($"Player list updated: {_players.Count} players connected.");

        if(IsServer)
            SyncPlayers_RPC(GetPlayerIds());
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ConnectPlayer_RPC(ulong networkObjId)
    {
        NetworkObject obj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkObjId];

        Player player = obj.GetComponent<Player>();

        if(_players.Contains(_players.Find(p => p.NetworkObjectId == networkObjId)))
            return;

        _players.Add(player);
        onPlayerListUpdated?.Invoke();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DisconnectPlayer_RPC(ulong networkObjId)
    {
        NetworkObject obj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkObjId];

        Player player = obj.GetComponent<Player>();

        if (!_players.Contains(_players.Find(p => p.NetworkObjectId == networkObjId)))
            return;

        _players.Remove(player);
        onPlayerListUpdated?.Invoke();
    }

    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
    public void SyncPlayers_RPC(ulong[] playerIds)
    {
        if (IsServer) // Should never be encountered, purely a last resort check to prevent an infintie recursive loop
            return;

        _players.Clear();

        foreach (ulong id in playerIds)
        {
            NetworkObject obj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[id];
            Player player = obj.GetComponent<Player>();
            _players.Add(player);
        }

        onPlayerListUpdated?.Invoke();
    }

    // ---------- Helper Methods ----------

    public List<Player> GetAllPlayers() => _players;
    
    public List<Player> GetClientPlayers()
    {
        List<Player> resultSet = new List<Player>();

        resultSet.AddRange(_players.FindAll(p => p.OwnerClientId != NetworkManager.ServerClientId));

        return resultSet;
    }

    public Player GetHostPlayer() => _players.Find(p => p.OwnerClientId == NetworkManager.ServerClientId);

    public int GetPlayerCount() => _players.Count;
    public ulong[] GetPlayerIds()
    {
        ulong[] playerIds = new ulong[_players.Count];

        for (int i = 0; i < _players.Count; i++)
            playerIds[i] = _players[i].NetworkObjectId;

        return playerIds;
    }


}
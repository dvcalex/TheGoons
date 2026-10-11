using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Each peer learns the roster from the player objects Netcode spawns/despawns.
public class Server : NetworkBehaviour
{
    public static Server _instance;
    public List<Player> _players = new List<Player>();
    public Action onPlayerListUpdated;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
    }

    public void RegisterPlayer(Player player)
    {
        _players.RemoveAll(entry => entry == null);
        if (player == null || _players.Contains(player))
        {
            return;
        }

        _players.Add(player);
        onPlayerListUpdated?.Invoke();
    }

    public void UnregisterPlayer(Player player)
    {
        if (_players.Remove(player))
        {
            onPlayerListUpdated?.Invoke();
        }
    }

    public List<Player> GetAllPlayers()
    {
        _players.RemoveAll(player => player == null);
        return _players;
    }

    public List<Player> GetClientPlayers()
    {
        List<Player> players = new List<Player>();
        foreach (Player player in GetAllPlayers())
        {
            if (player.OwnerClientId != Unity.Netcode.NetworkManager.ServerClientId)
            {
                players.Add(player);
            }
        }

        return players;
    }

    public Player GetHostPlayer()
    {
        foreach (Player player in GetAllPlayers())
        {
            if (player.OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId)
            {
                return player;
            }
        }

        return null;
    }

    public int GetPlayerCount()
    {
        return GetAllPlayers().Count;
    }

    public override void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }

        base.OnDestroy();
    }
}

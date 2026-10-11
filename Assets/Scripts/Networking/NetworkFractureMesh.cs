using LibreFracture;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkFractureMesh : NetworkBehaviour
{
    public Transform[] registry;

    public List<int> trackedChunks = new List<int>();

    public override void OnNetworkSpawn()
    {
        ChunkNode[] nodes = GetComponentsInChildren<ChunkNode>();

        registry = new Transform[nodes.Length];

        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].GetComponent<Rigidbody>() != null)
            {
                if(!IsServer)
                    nodes[i].GetComponent<Rigidbody>().isKinematic = true;

                registry[i] = nodes[i].transform;
            }
            
        }

        if (IsServer)
        {
            NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
        {
            NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;
        }
        trackedChunks.Clear();
        base.OnNetworkDespawn();
    }

    private void OnNetworkTick()
    {
        if (!IsSpawned || !IsServer || !NetworkManager.IsListening)
        {
            return;
        }

        List<int> chunksToAdd = new List<int>();
        List<int> chunksToRemove = new List<int>();

        for(int i = 0; i < registry.Length; i++)
        {
            if (registry[i] == null)
                continue;

            if (!trackedChunks.Contains(i) && registry[i].gameObject.GetComponent<Rigidbody>() != null && registry[i].gameObject.GetComponent<Rigidbody>().linearVelocity.magnitude > 0.5f)
            {
                trackedChunks.Add(i);
                chunksToAdd.Add(i);
            }
        }

        for (int i = trackedChunks.Count - 1; i >= 0; i--)
        {
            if(registry[trackedChunks[i]] == null || (registry[trackedChunks[i]].gameObject.GetComponent<Rigidbody>() != null && registry[trackedChunks[i]].gameObject.GetComponent<Rigidbody>().linearVelocity.magnitude < 0.5f))
            {
                chunksToRemove.Add(trackedChunks[i]);
                trackedChunks.Remove(trackedChunks[i]);
                continue;
            }
        }

        if (chunksToAdd.Count > 0)
            AddChunks_RPC(chunksToAdd.ToArray());

        if (chunksToRemove.Count > 0)
            RemoveChunks_RPC(chunksToRemove.ToArray());

        if (trackedChunks.Count > 0)
        {
            SyncData_RPC(GetTrackedPositions(), GetTrackedRotations());
        }
    }

    public Vector3[] GetTrackedPositions()
    {
        Vector3[] positions = new Vector3[trackedChunks.Count];
        for(int i = 0; i < trackedChunks.Count; i++)
        {
            if (registry[trackedChunks[i]] == null)
                continue;
            positions[i] = registry[trackedChunks[i]].position;
        }
        return positions;
    }

    public Vector3[] GetTrackedRotations()
    {
        Vector3[] rotations = new Vector3[trackedChunks.Count];
        for (int i = 0; i < trackedChunks.Count; i++)
        {
            if (registry[trackedChunks[i]] == null)
                continue;
            rotations[i] = registry[trackedChunks[i]].rotation.eulerAngles;
        }
        return rotations;
    }

    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
    public void AddChunks_RPC(int[] chunkIndex)
    {
        for (int i = 0; i < chunkIndex.Length; i++)
        {
            if (!trackedChunks.Contains(chunkIndex[i]))
                trackedChunks.Add(chunkIndex[i]);
        }
    }

    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
    public void RemoveChunks_RPC(int[] chunkIndex)
    {
        for(int i = 0; i < chunkIndex.Length; i++)
        {
            if(trackedChunks.Contains(chunkIndex[i]))
                trackedChunks.Remove(chunkIndex[i]);
        }
    }

    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
    public void SyncData_RPC(Vector3[] positions, Vector3[] rotations)
    {
        for(int i = 0; i < trackedChunks.Count; i++)
        {
            if (registry[trackedChunks[i]] == null)
                continue;

            registry[trackedChunks[i]].position = positions[i];
            registry[trackedChunks[i]].rotation = Quaternion.Euler(rotations[i]);
        }
    }
}

public class TrackedChunk
{
    public int index;
}
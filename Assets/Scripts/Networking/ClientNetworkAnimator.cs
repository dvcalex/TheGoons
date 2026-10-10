using Unity.Netcode;
using UnityEngine;
using Unity;
using Unity.Netcode.Components;

public class ClientNetworkAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
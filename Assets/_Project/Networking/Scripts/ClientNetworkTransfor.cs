using Unity.Netcode.Components;
using UnityEngine;

[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    // This tells Netcode that the Owner (the Client), not the Server, 
    // is the boss of where this object is located.
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
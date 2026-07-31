using Unity.Netcode.Components;

// This script allows the Owner (the Client) to sync animations to the Server
public class OwnerNetworkAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative()
    {
        return false; 
    }
}
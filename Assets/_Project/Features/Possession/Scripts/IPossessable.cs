using UnityEngine;

public interface IPossessable
{
    void OnPossess(ulong clientId);
    void OnRelease();

    Transform GetCameraTarget();
    void Possess(GirlPossession girl);
    void Release();
}
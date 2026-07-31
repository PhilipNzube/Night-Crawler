using UnityEngine;

public class ExplorerPossessable : MonoBehaviour, IPossessable
{
    public bool isDead = false;
    public Transform cameraTarget;

    private GirlPossession _girlRef;
    private Animator _animator;

    void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
    }

    public void OnDeath()
    {
        isDead = true;
        gameObject.layer = LayerMask.NameToLayer("Possessable");

        if (_animator)
            _animator.SetTrigger("Die");
    }

    public void Possess(GirlPossession girl)
    {
        if (!isDead) return;

        _girlRef = girl;
        Debug.Log("Girl possessed dead explorer: " + name);

        // Disable physics / movement on the corpse if needed
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = true;
    }

    public void Release()
    {
        Debug.Log("Girl released explorer body.");
        _girlRef = null;
    }

    public void OnPossess(ulong clientId)
    {
        // Interface requirement
    }

    public void OnRelease()
    {
        // Interface requirement
        Release();
    }

    public Transform GetCameraTarget()
    {
        return cameraTarget != null ? cameraTarget : transform;
    }
}

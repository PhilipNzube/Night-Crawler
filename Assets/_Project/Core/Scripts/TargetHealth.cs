using UnityEngine;
using Unity.Netcode;

public class TargetHealth : NetworkBehaviour, IDamageReceiver
{
    [Header("Data (ScriptableObject)")]
    public EntityStats stats;
    
    public bool destroyOnDeath = true;
    
    [Header("Runtime Variables (Synced)")]
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> isCorpse = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> isOccupied = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Animator _animator;

    void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && stats != null)
        {
            currentHealth.Value = stats.maxHealth;
        }
    }

    // This must only be called on the Server
    public void TakeDamage(float amount, bool isSoulAttack = false)
    {
        if (!IsServer) return;

        currentHealth.Value -= amount;
        Debug.Log($"{gameObject.name} took {amount} {(isSoulAttack ? "SOUL" : "PHYSICAL")} damage. Remaining: {currentHealth.Value}");

        // Visual feedback for everyone
        FlashRedClientRpc(isSoulAttack);

        if (currentHealth.Value <= 0f)
        {
            Die();
        }
    }

    [ClientRpc]
    private void FlashRedClientRpc(bool isSoulAttack)
    {
        // 1. Trigger the appropriate Animation (If it exists)
        if (_animator != null)
        {
            string triggerName = isSoulAttack ? "SoulHit" : "Hit";
            
            // Safety check for parameter existence
            foreach (var p in _animator.parameters)
            {
                if (p.name == triggerName)
                {
                    _animator.SetTrigger(triggerName);
                    break;
                }
            }
        }

        // 2. Visual Flash (Maybe blue/purple for soul?)
        StartCoroutine(isSoulAttack ? FlashSoulRoutine() : FlashRedRoutine());
    }

    private System.Collections.IEnumerator FlashSoulRoutine()
    {
        Debug.Log($"[AUDIO-VFX] Triggering Soul Flash (Purple) on {gameObject.name}");
        if (TryGetComponent<Renderer>(out Renderer r))
        {
            Color originalColor = r.material.color;
            r.material.color = new Color(0.5f, 0f, 1f); // Purple soul flash
            yield return new WaitForSeconds(0.15f);
            r.material.color = originalColor;
        }
    }

    private System.Collections.IEnumerator FlashRedRoutine()
    {
        Debug.Log($"[AUDIO-VFX] Triggering Physical Flash (Red) on {gameObject.name}");
        if (TryGetComponent<Renderer>(out Renderer r))
        {
            Color originalColor = r.material.color;
            r.material.color = Color.red;
            yield return new WaitForSeconds(0.1f);
            r.material.color = originalColor;
        }
    }

    private void Die()
    {
        Debug.Log($"{gameObject.name} has died.");
        
        // Notify GameManager so it can check win/loss conditions
        if (IsServer && GameManager.Instance != null)
        {
            GameManager.Instance.OnEntityDeath(GetComponent<NetworkObject>());
        }

        if (IsServer)
        {
            isCorpse.Value = true;
            // Delay despawning so the Girl has time to possess the body
            StartCoroutine(CorpseCleanupRoutine());
        }
    }

    private System.Collections.IEnumerator CorpseCleanupRoutine()
    {
        // Wait 30 seconds before final cleanup
        yield return new WaitForSeconds(30f);
        
        // Only despawn if the Girl isn't currently hiding inside it!
        if (!isOccupied.Value && IsServer)
        {
            GetComponent<NetworkObject>().Despawn();
        }
    }

    public void OnPossessed() 
    { 
        if (IsServer) isOccupied.Value = true; 
    }

}
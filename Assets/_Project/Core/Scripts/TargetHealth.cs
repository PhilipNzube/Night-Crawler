using UnityEngine;
using Unity.Netcode;

public class TargetHealth : NetworkBehaviour, IDamageReceiver
{
    [Header("Health Settings")]
    [Tooltip("Base maximum health for this character. Can be configured uniquely on each character prefab.")]
    public float baseMaxHealth = 100f;

    [Header("Data (ScriptableObject)")]
    public EntityStats stats;
    
    public bool destroyOnDeath = true;
    
    [Header("Runtime Variables (Synced)")]
    public NetworkVariable<float> maxHealth = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> isCorpse = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> isOccupied = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float CurrentHealth => currentHealth.Value;
    public float MaxHealth => maxHealth.Value > 0 ? maxHealth.Value : ((stats != null && stats.maxHealth > 0) ? stats.maxHealth : baseMaxHealth);

    private Animator _animator;

    void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            float targetMax = (stats != null && stats.maxHealth > 0) ? stats.maxHealth : (baseMaxHealth > 0 ? baseMaxHealth : 100f);
            maxHealth.Value = targetMax;
            currentHealth.Value = targetMax;
            Debug.Log($"[TargetHealth] Initialized '{gameObject.name}' with {targetMax} Max HP.");
        }

        // Subscribe so local client can react to future changes
        currentHealth.OnValueChanged += OnHealthValueChanged;
        isCorpse.OnValueChanged += OnCorpseStateChanged;

        // If spawned already as corpse, apply visual/tag states
        if (isCorpse.Value)
        {
            ApplyCorpseState();
        }

        // Notify UI immediately with the current value so health bars populate on spawn
        // (OnValueChanged doesn't fire if the value didn't change after spawn)
        StartCoroutine(BroadcastInitialHealth());
    }

    private System.Collections.IEnumerator BroadcastInitialHealth()
    {
        // Wait one frame for HUD / listeners to subscribe first
        yield return null;
        OnHealthValueChanged(currentHealth.Value, currentHealth.Value);
    }

    private void OnHealthValueChanged(float previous, float current)
    {
        // Subclasses or HealthBar components can subscribe to currentHealth.OnValueChanged directly,
        // but if they need a C# event, broadcast via this relay.
    }

    private void OnCorpseStateChanged(bool previous, bool current)
    {
        if (current)
        {
            ApplyCorpseState();

            // Only show death screen if the local player is truly this character
            // (prevents server host adopting an orphaned object from falsely showing YOU DIED)
            bool isGirl = GetComponent<GirlStealth>() != null || GetComponent<GirlMaterialController>() != null;
            bool isLocalCharacter = (NetworkManager.Singleton != null && 
                                     NetworkManager.Singleton.LocalClient != null && 
                                     NetworkManager.Singleton.LocalClient.PlayerObject == GetComponent<NetworkObject>());

            if (isLocalCharacter && DeathUI.Instance != null)
            {
                DeathUI.Instance.ShowDeathScreen("YOU DIED", isGirl 
                    ? "The Vengeful Spirit has been banished." 
                    : "Your soul has fallen. Allies can still loot your body.");
            }

            if (isLocalCharacter && PlayerHUD.Instance != null)
            {
                PlayerHUD.Instance.HandleLocalPlayerDied();
            }
        }
    }

    private void ApplyCorpseState()
    {
        // Disable Player tag so AI/sensors don't target dead bodies
        gameObject.tag = "Untagged";

        // Disable CharacterController to allow ragdoll physics & prevent movement
        if (TryGetComponent<CharacterController>(out var cc))
        {
            cc.enabled = false;
        }

        // Preserve dead body in scene for looting even if player disconnects
        if (TryGetComponent<NetworkObject>(out var netObj))
        {
            netObj.DontDestroyWithOwner = true;
        }

        // Disable overhead player name tag (without deactivating the character GameObject!)
        var nameTags = GetComponentsInChildren<PlayerNameTag>(true);
        foreach (var nt in nameTags)
        {
            nt.enabled = false;
            if (nt.nameText != null) nt.nameText.gameObject.SetActive(false);
            if (nt.nameTextUGUI != null) nt.nameTextUGUI.gameObject.SetActive(false);
            if (nt.gameObject != gameObject)
            {
                nt.gameObject.SetActive(false);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthValueChanged;
        isCorpse.OnValueChanged -= OnCorpseStateChanged;
    }

    // This must only be called on the Server
    public void TakeDamage(float amount, bool isSoulAttack = false)
    {
        if (!IsServer) return;

        currentHealth.Value = Mathf.Clamp(currentHealth.Value - amount, 0f, MaxHealth);
        Debug.Log($"{gameObject.name} took {amount} {(isSoulAttack ? "SOUL" : "PHYSICAL")} damage. Remaining: {currentHealth.Value}/{MaxHealth}");

        // Visual feedback for everyone
        FlashRedClientRpc(isSoulAttack);

        if (currentHealth.Value <= 0f)
        {
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (!IsServer) return;
        currentHealth.Value = Mathf.Clamp(currentHealth.Value + amount, 0f, MaxHealth);
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

        // If possessed, auto-exit possession after brief death animation!
        if (TryGetComponent<PlayerPossessableNet>(out var possessable) && possessable.isPossessed.Value)
        {
            Debug.Log($"[TargetHealth] Possessed character {gameObject.name} died! Triggering possession death auto-exit.");
            possessable.HandlePossessedTargetDied();
        }
        
        // Disable player tag, character controller, and name tag
        ApplyCorpseState();

        // Notify GameManager so it can check win/loss conditions & broadcast death message
        if (IsServer && GameManager.Instance != null)
        {
            GameManager.Instance.OnEntityDeath(GetComponent<NetworkObject>());
        }

        // Trigger ragdoll if NetworkRagdollController is present
        if (TryGetComponent<NetworkRagdollController>(out var ragdoll))
        {
            ragdoll.TriggerRagdollDeath();
        }

        if (IsServer)
        {
            isCorpse.Value = true;
            // The corpse is NOT destroyed or despawned so other players can loot it
            if (TryGetComponent<CorpseLootableNet>(out var lootable))
            {
                lootable.HandleDeath();
            }
        }
    }

    public void OnPossessed() 
    { 
        if (IsServer) isOccupied.Value = true; 
    }

}
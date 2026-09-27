using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace NightCrawler.Monsters
{
    /// <summary>
    /// SOLID — SRP: Manages server-authoritative random dead monster spawning.
    /// Auto-discovers all DeadSpawnPoints in the scene and handles network instantiation
    /// when the Girl requests a dead summon from GirlMonsterSummonHUD.
    /// </summary>
    public class DeadSpawnManager : NetworkBehaviour
    {
        public static DeadSpawnManager Instance { get; private set; }

        [Header("Audio")]
        [Tooltip("Fallback 2D broadcast sound played if the summoned monster has no specific spawnSound assigned.")]
        public AudioClip globalSummonSound;

        [Header("Network State")]
        public NetworkVariable<int> totalMonstersSummoned = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly List<DeadSpawnPoint> _registeredSpawnPoints = new List<DeadSpawnPoint>();
        private AudioSource _audioSource;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 0f; // 2D broadcast sound
            }
        }

        private void Start()
        {
            DiscoverSpawnPoints();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (_registeredSpawnPoints.Count == 0)
            {
                DiscoverSpawnPoints();
            }
        }

        /// <summary>
        /// Finds all DeadSpawnPoints placed in the scene.
        /// </summary>
        public void DiscoverSpawnPoints()
        {
            _registeredSpawnPoints.Clear();
            DeadSpawnPoint[] found = FindObjectsByType<DeadSpawnPoint>(FindObjectsSortMode.None);
            if (found != null && found.Length > 0)
            {
                _registeredSpawnPoints.AddRange(found);
                Debug.Log($"[DeadSpawnManager] Discovered {_registeredSpawnPoints.Count} DeadSpawnPoints in scene.");
            }
            else
            {
                Debug.LogWarning("[DeadSpawnManager] No DeadSpawnPoint components found in scene! Monsters will spawn near scene center.");
            }
        }

        /// <summary>
        /// Request from the Girl client to spawn a monster by index.
        /// Definitions and prefabs are resolved directly from GirlMonsterSummonHUD.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RequestSpawnMonsterServerRpc(int monsterIndex, ulong summonerClientId)
        {
            if (!IsServer) return;

            MonsterDefinitionSO def = null;
            if (GirlMonsterSummonHUD.Instance != null)
            {
                def = GirlMonsterSummonHUD.Instance.GetMonsterDefinition(monsterIndex);
            }

            if (def == null || def.monsterPrefab == null)
            {
                Debug.LogError($"[DeadSpawnManager] Failed to spawn monster: No MonsterDefinitionSO or monsterPrefab found for index {monsterIndex} in GirlMonsterSummonHUD!");
                return;
            }

            GameObject prefabToSpawn = def.monsterPrefab;
            string monsterName = def.monsterName;

            // Ensure spawn points are discovered
            if (_registeredSpawnPoints.Count == 0)
            {
                DiscoverSpawnPoints();
            }

            // Pick a random spawn point
            Vector3 spawnPos = Vector3.zero;
            Quaternion spawnRot = Quaternion.identity;

            if (_registeredSpawnPoints.Count > 0)
            {
                var pt = _registeredSpawnPoints[UnityEngine.Random.Range(0, _registeredSpawnPoints.Count)];
                if (pt != null)
                {
                    spawnPos = pt.transform.position;
                    spawnRot = pt.transform.rotation;
                }
            }
            else
            {
                spawnPos = new Vector3(UnityEngine.Random.Range(-5f, 5f), 1f, UnityEngine.Random.Range(-5f, 5f));
            }

            // Snap to ground
            if (Physics.Raycast(spawnPos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 20f, ~LayerMask.GetMask("UI", "Ignore Raycast")))
            {
                spawnPos = hit.point + Vector3.up * 0.05f;
            }

            // Sample nearest NavMesh point so NavMeshAgent places cleanly
            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit navHit, 5.0f, NavMesh.AllAreas))
            {
                spawnPos = navHit.position;
            }

            // Instantiate and network spawn
            GameObject spawnedObj = Instantiate(prefabToSpawn, spawnPos, spawnRot);
            if (spawnedObj.TryGetComponent<NetworkObject>(out var netObj))
            {
                netObj.Spawn(true);
            }
            else
            {
                Debug.LogWarning($"[DeadSpawnManager] Spawned monster {monsterName} lacks NetworkObject component!");
            }

            totalMonstersSummoned.Value++;
            Debug.Log($"[DeadSpawnManager] Client {summonerClientId} successfully rose '{monsterName}' at {spawnPos}! Total summoned: {totalMonstersSummoned.Value}");

            // Broadcast sound and notification to all clients
            BroadcastMonsterSummonedClientRpc(monsterIndex, monsterName);
        }

        [ClientRpc]
        private void BroadcastMonsterSummonedClientRpc(int monsterIndex, string monsterName)
        {
            AudioClip soundToPlay = null;
            if (GirlMonsterSummonHUD.Instance != null)
            {
                var def = GirlMonsterSummonHUD.Instance.GetMonsterDefinition(monsterIndex);
                if (def != null && def.spawnSound != null)
                {
                    soundToPlay = def.spawnSound;
                }
            }

            if (soundToPlay == null)
            {
                soundToPlay = globalSummonSound;
            }

            if (soundToPlay != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(soundToPlay);
            }

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification($"THE SHADOWS WRITHE: A {monsterName} has risen from the dead!", 4.5f);
            }
        }

        /// <summary>
        /// Server-authoritative command dispatcher for all active monsters in the subterranean mine.
        /// commandIndex: 0 = Hunt (Seek and Destroy), 1 = Recall (To My Side).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void CommandAllMonstersServerRpc(int commandIndex, ulong summonerClientId)
        {
            if (!IsServer) return;

            // Resolve summoning Girl's transform
            Transform girlTransform = null;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients.TryGetValue(summonerClientId, out var client) && client.PlayerObject != null)
            {
                girlTransform = client.PlayerObject.transform;
            }
            else if (GameManager.Instance != null && GameManager.Instance.GirlTransform != null)
            {
                girlTransform = GameManager.Instance.GirlTransform;
            }

            var ais = FindObjectsByType<MonsterAI>(FindObjectsSortMode.None);
            int commandedCount = 0;
            var cmd = (commandIndex == 1) ? MonsterAI.Command.Follow : MonsterAI.Command.Hunt;

            foreach (var ai in ais)
            {
                if (ai == null || ai.gameObject == null) continue;
                if (ai.currentState == MonsterAI.AIState.Dead) continue;
                if (ai.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0)) continue;
                if (ai.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead) continue;

                ai.SetCommand(cmd, girlTransform);
                commandedCount++;
            }

            Debug.Log($"[DeadSpawnManager] Dispatched command {(commandIndex == 1 ? "RECALL" : "HUNT")} to {commandedCount} monsters from client {summonerClientId}.");

            BroadcastMonsterCommandClientRpc(commandIndex, commandedCount, summonerClientId);
        }

        [ClientRpc]
        private void BroadcastMonsterCommandClientRpc(int commandIndex, int monsterCount, ulong summonerClientId)
        {
            bool isLocalSummoner = (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == summonerClientId);

            if (monsterCount <= 0)
            {
                if (isLocalSummoner && NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowNotification("No creatures in the mine to command!", 2.5f);
                }
                return;
            }

            string message;
            if (commandIndex == 1) // Recall
            {
                message = isLocalSummoner
                    ? $"TO MY SIDE! {monsterCount} creature(s) returning to guard you!"
                    : "THE SHADOWS RETREAT: The Vengeful Spirit has recalled her minions!";
            }
            else // Hunt
            {
                message = isLocalSummoner
                    ? $"SEEK AND DESTROY! Dispatched {monsterCount} creature(s) to hunt!"
                    : "THE TUNNELS ECHO: The monsters have been unleashed to hunt!";
            }

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification(message, 3.5f);
            }
        }
    }
}

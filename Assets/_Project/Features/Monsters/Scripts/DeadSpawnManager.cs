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

            Debug.Log($"[DeadSpawnManager] Client {summonerClientId} successfully rose '{monsterName}' at {spawnPos}!");

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
    }
}

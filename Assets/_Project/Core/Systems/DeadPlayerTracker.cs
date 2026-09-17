using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace NightCrawler.Systems
{
    [Serializable]
    public class DeadPlayerRecord
    {
        public ulong clientId;
        public string playerName;
        public Vector3 deathPosition;
        public bool isLooted;

        public DeadPlayerRecord(ulong clientId, string playerName, Vector3 deathPosition)
        {
            this.clientId = clientId;
            this.playerName = playerName;
            this.deathPosition = deathPosition;
            this.isLooted = false;
        }
    }

    /// <summary>
    /// SOLID — SRP: Tracks deceased players throughout the match across all network clients.
    /// Used by the Girl's pact interface to select dead players for "Loot a body" pacts,
    /// even though corpses no longer have active in-world floating nametags.
    /// </summary>
    public class DeadPlayerTracker : NetworkBehaviour
    {
        public static DeadPlayerTracker Instance { get; private set; }

        private static readonly List<DeadPlayerRecord> s_DeadPlayers = new List<DeadPlayerRecord>();

        public static event Action<DeadPlayerRecord> OnPlayerDied;
        public static event Action<ulong> OnPlayerCorpseLooted;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            s_DeadPlayers.Clear();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
            s_DeadPlayers.Clear();
        }

        public static IReadOnlyList<DeadPlayerRecord> GetDeadPlayers()
        {
            return s_DeadPlayers.AsReadOnly();
        }

        public static DeadPlayerRecord GetDeadPlayer(ulong clientId)
        {
            return s_DeadPlayers.Find(p => p.clientId == clientId);
        }

        /// <summary>
        /// Registers that a player died. Called on the server (e.g. by GameManager or TargetHealth).
        /// </summary>
        public void RegisterDeath(ulong clientId, string playerName, Vector3 deathPosition)
        {
            if (!IsServer) return;

            RegisterDeathClientRpc(clientId, playerName, deathPosition);
        }

        [ClientRpc]
        private void RegisterDeathClientRpc(ulong clientId, string playerName, Vector3 deathPosition)
        {
            if (s_DeadPlayers.Exists(p => p.clientId == clientId)) return;

            var record = new DeadPlayerRecord(clientId, playerName, deathPosition);
            s_DeadPlayers.Add(record);

            Debug.Log($"[DeadPlayerTracker] Registered dead player: {playerName} (ID: {clientId}) at {deathPosition}. Total dead: {s_DeadPlayers.Count}");
            OnPlayerDied?.Invoke(record);
        }

        /// <summary>
        /// Marks that a dead player's corpse was looted.
        /// </summary>
        public void MarkCorpseLooted(ulong deadClientId)
        {
            if (!IsServer) return;
            MarkCorpseLootedClientRpc(deadClientId);
        }

        [ClientRpc]
        private void MarkCorpseLootedClientRpc(ulong deadClientId)
        {
            var record = s_DeadPlayers.Find(p => p.clientId == deadClientId);
            if (record != null)
            {
                record.isLooted = true;
                Debug.Log($"[DeadPlayerTracker] Marked corpse of {record.playerName} as looted.");
                OnPlayerCorpseLooted?.Invoke(deadClientId);
            }
        }
    }
}

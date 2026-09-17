using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace NightCrawler.Economy
{
    /// <summary>
    /// SOLID — SRP: Manages disconnect grace periods for connected clients.
    /// Rules:
    /// - When a client disconnects, a 75-second grace timer begins.
    /// - Reconnection within the window cancels the timer with zero penalty.
    /// - If the grace period expires without reconnection, the player is marked as abandoned.
    /// - Disconnect penalty is strictly their staked amount (no extra deductions).
    /// </summary>
    public class DisconnectGraceManager : MonoBehaviour
    {
        public static DisconnectGraceManager Instance { get; private set; }

        [Header("Grace Settings")]
        [Tooltip("Grace period in seconds before a disconnected client is marked as abandoned (60-90s).")]
        public float gracePeriodSeconds = 75f;

        private readonly Dictionary<ulong, Coroutine> _activeGraceTimers = new Dictionary<ulong, Coroutine>();

        public static event Action<ulong, float> OnGraceTimerStarted;
        public static event Action<ulong> OnGraceTimerResolved;
        public static event Action<ulong> OnGraceTimerExpired;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientReconnected;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientReconnected;
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            // Only server manages authoritative grace timers
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            // Host disconnect is handled separately by HostDisconnectUI
            if (clientId == NetworkManager.ServerClientId) return;

            // If match already resolved, no penalty timer needed
            if (MatchEconomyManager.Instance != null && MatchEconomyManager.Instance.isMatchResolved.Value) return;

            Debug.Log($"[DisconnectGraceManager] Client {clientId} disconnected. Starting {gracePeriodSeconds:0}s grace timer.");
            if (_activeGraceTimers.TryGetValue(clientId, out var existing))
            {
                StopCoroutine(existing);
            }

            _activeGraceTimers[clientId] = StartCoroutine(GraceTimerRoutine(clientId));
            OnGraceTimerStarted?.Invoke(clientId, gracePeriodSeconds);
        }

        private void HandleClientReconnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            if (_activeGraceTimers.TryGetValue(clientId, out var timer))
            {
                StopCoroutine(timer);
                _activeGraceTimers.Remove(clientId);
                Debug.Log($"[DisconnectGraceManager] Client {clientId} reconnected within grace window! No penalty incurred.");
                OnGraceTimerResolved?.Invoke(clientId);
            }
        }

        private IEnumerator GraceTimerRoutine(ulong clientId)
        {
            yield return new WaitForSeconds(gracePeriodSeconds);

            _activeGraceTimers.Remove(clientId);
            Debug.LogWarning($"[DisconnectGraceManager] Client {clientId} failed to reconnect within grace window. Marking as abandoned.");

            // Notify MatchEconomyManager to forfeit their stake
            if (MatchEconomyManager.Instance != null)
            {
                MatchEconomyManager.Instance.HandlePlayerForfeit(clientId);
            }

            OnGraceTimerExpired?.Invoke(clientId);
        }
    }
}

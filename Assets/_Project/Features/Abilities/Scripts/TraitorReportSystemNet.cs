using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using NightCrawler.Economy;

namespace NightCrawler.Systems
{
    /// <summary>
    /// Network-serialized representation of an active or resolved Traitor Incident Report.
    /// </summary>
    [Serializable]
    public struct TraitorReportEntry : INetworkSerializable, IEquatable<TraitorReportEntry>
    {
        public ulong reportId;
        public ulong accuserClientId;
        public ulong accusedClientId;
        public ulong carrierClientId;
        public FixedString64Bytes accuserName;
        public FixedString64Bytes accusedName;
        public bool isDestroyed;
        public bool isAudited;
        public bool isGuilty;
        public int seizedDealMoney;

        public bool Equals(TraitorReportEntry other) => reportId == other.reportId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref reportId);
            serializer.SerializeValue(ref accuserClientId);
            serializer.SerializeValue(ref accusedClientId);
            serializer.SerializeValue(ref carrierClientId);
            serializer.SerializeValue(ref accuserName);
            serializer.SerializeValue(ref accusedName);
            serializer.SerializeValue(ref isDestroyed);
            serializer.SerializeValue(ref isAudited);
            serializer.SerializeValue(ref isGuilty);
            serializer.SerializeValue(ref seizedDealMoney);
        }
    }

    /// <summary>
    /// SOLID — SRP: Server-authoritative Traitor Complaint / Incident Report system.
    /// Manages:
    /// 1. Lodging official treason complaints with HQ against suspicious players.
    /// 2. Physical dossier carrier tracking (dossier stays with accuser, drops on corpse, or gets looted).
    /// 3. Evidence destruction (carrier can shred/burn the dossier before HQ audit).
    /// 4. End-of-match HQ financial forensics (checks accused bank accounts for illicit deal earnings).
    /// 5. Guilty / Not Guilty verdicts, credit rewards, false accusation fines, and persistent Liar/Traitor strikes.
    /// </summary>
    public class TraitorReportSystemNet : NetworkBehaviour
    {
        public static TraitorReportSystemNet Instance { get; private set; }

        public const int FalseAccusationFine = 15;
        public const int MinimumTraitorBounty = 20;

        // Active network reports list synchronized across all clients
        public NetworkList<TraitorReportEntry> activeReports;

        public static event Action<TraitorReportEntry> OnReportFiled;
        public static event Action<TraitorReportEntry> OnReportDestroyed;
        public static event Action<TraitorReportEntry> OnVerdictReceived;
        public static event Action<string> OnReportError;
        public static event Action OnReportsStateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            activeReports = new NetworkList<TraitorReportEntry>(
                new List<TraitorReportEntry>(),
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        public override void OnNetworkSpawn()
        {
            activeReports.OnListChanged += HandleReportsListChanged;
        }

        public override void OnNetworkDespawn()
        {
            activeReports.OnListChanged -= HandleReportsListChanged;
            if (Instance == this) Instance = null;
        }

        private void HandleReportsListChanged(NetworkListEvent<TraitorReportEntry> changeEvent)
        {
            OnReportsStateChanged?.Invoke();
        }

        // =========================================================================
        //  Report Queries
        // =========================================================================

        public bool HasActiveReportAgainst(ulong accusedClientId)
        {
            for (int i = 0; i < activeReports.Count; i++)
            {
                var r = activeReports[i];
                if (!r.isDestroyed && r.accusedClientId == accusedClientId)
                {
                    return true;
                }
            }
            return false;
        }

        public bool PlayerCarriesDossier(ulong clientId)
        {
            for (int i = 0; i < activeReports.Count; i++)
            {
                var r = activeReports[i];
                if (!r.isDestroyed && r.carrierClientId == clientId)
                {
                    return true;
                }
            }
            return false;
        }

        public int GetCarriedDossiersCount(ulong clientId)
        {
            int count = 0;
            for (int i = 0; i < activeReports.Count; i++)
            {
                var r = activeReports[i];
                if (!r.isDestroyed && r.carrierClientId == clientId)
                {
                    count++;
                }
            }
            return count;
        }

        public TraitorReportEntry? GetFirstCarriedReport(ulong clientId)
        {
            for (int i = 0; i < activeReports.Count; i++)
            {
                var r = activeReports[i];
                if (!r.isDestroyed && r.carrierClientId == clientId)
                {
                    return r;
                }
            }
            return null;
        }

        // =========================================================================
        //  Filing Reports (Client -> Server)
        // =========================================================================

        public void RequestFileReport(ulong accusedClientId)
        {
            if (NetworkManager.Singleton == null) return;
            RequestFileReportServerRpc(accusedClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestFileReportServerRpc(ulong accusedClientId, RpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;

            // Cannot report self
            if (senderId == accusedClientId)
            {
                NotifyReportErrorClientRpc(senderId, "You cannot file a treason complaint against yourself.");
                return;
            }

            // Cannot report if game has already ended
            if (GameManager.Instance != null && GameManager.Instance.gameEnded.Value)
            {
                NotifyReportErrorClientRpc(senderId, "The mission has concluded. Reports cannot be lodged after extraction.");
                return;
            }

            // Enforce: Only ONE active report can exist for the same accused player
            for (int i = 0; i < activeReports.Count; i++)
            {
                var existing = activeReports[i];
                if (!existing.isDestroyed && existing.accusedClientId == accusedClientId)
                {
                    NotifyReportErrorClientRpc(senderId, $"An official treason dossier has already been lodged with HQ against {existing.accusedName}.");
                    return;
                }
            }

            string accuserName = ResolvePlayerName(senderId);
            string accusedName = ResolvePlayerName(accusedClientId);

            var newEntry = new TraitorReportEntry
            {
                reportId = (ulong)(activeReports.Count + 1),
                accuserClientId = senderId,
                accusedClientId = accusedClientId,
                carrierClientId = senderId, // Accuser initially carries physical dossier
                accuserName = new FixedString64Bytes(accuserName),
                accusedName = new FixedString64Bytes(accusedName),
                isDestroyed = false,
                isAudited = false,
                isGuilty = false,
                seizedDealMoney = 0
            };

            activeReports.Add(newEntry);
            Debug.Log($"[TraitorReportSystemNet] Report #{newEntry.reportId} logged by {accuserName} against {accusedName}.");

            NotifyReportFiledClientRpc(newEntry);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyReportFiledClientRpc(TraitorReportEntry entry)
        {
            OnReportFiled?.Invoke(entry);
            OnReportsStateChanged?.Invoke();

            if (NetworkManager.Singleton == null) return;
            ulong localId = NetworkManager.Singleton.LocalClientId;

            if (localId == entry.accusedClientId)
            {
                // High-priority urgent alert to the accused
                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowStyledWarning(
                        "TRAITOR REPORT LODGED",
                        $"HQ treason dossier filed against you by <b>{entry.accuserName}</b>! They hold the physical evidence. Eliminate them to intercept the dossier!",
                        new Color(1f, 0.25f, 0.25f, 1f),
                        7.0f,
                        isPulsing: true
                    );
                }
            }
            else if (localId == entry.accuserClientId)
            {
                // Notice to accuser
                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowNotification(
                        "DOSSIER LODGED WITH HQ",
                        $"Official complaint filed against <b>{entry.accusedName}</b>. You carry the physical dossier. Survive or do not let it be destroyed!",
                        new Color(0.2f, 0.85f, 1f, 1f),
                        6.0f
                    );
                }
            }
            else
            {
                // General match announcement
                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowNotification(
                        $"HQ INVESTIGATION: {entry.accuserName} lodged a Traitor Report against {entry.accusedName}."
                    );
                }
            }
        }

        // =========================================================================
        //  Destroying Reports (Client -> Server)
        // =========================================================================

        public void RequestDestroyReport(ulong accusedClientId)
        {
            if (NetworkManager.Singleton == null) return;
            RequestDestroyReportServerRpc(accusedClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestDestroyReportServerRpc(ulong accusedClientId, RpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;

            for (int i = 0; i < activeReports.Count; i++)
            {
                var entry = activeReports[i];
                if (!entry.isDestroyed && entry.accusedClientId == accusedClientId)
                {
                    // Must be carrying the dossier to destroy it
                    if (entry.carrierClientId != senderId)
                    {
                        NotifyReportErrorClientRpc(senderId, "You do not physically hold this report dossier to destroy it.");
                        return;
                    }

                    entry.isDestroyed = true;
                    activeReports[i] = entry;

                    Debug.Log($"[TraitorReportSystemNet] Report against {entry.accusedName} incinerated by client {senderId}.");
                    NotifyReportDestroyedClientRpc(entry);
                    return;
                }
            }

            NotifyReportErrorClientRpc(senderId, "No active dossier found to destroy.");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyReportDestroyedClientRpc(TraitorReportEntry entry)
        {
            OnReportDestroyed?.Invoke(entry);
            OnReportsStateChanged?.Invoke();

            if (NetworkManager.Singleton == null) return;
            ulong localId = NetworkManager.Singleton.LocalClientId;

            if (localId == entry.accusedClientId)
            {
                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowNotification(
                        "CHARGES DROPPED",
                        "The physical treason dossier against you was destroyed! HQ investigation aborted.",
                        new Color(0.2f, 1f, 0.4f, 1f),
                        6.0f
                    );
                }
            }
            else
            {
                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowNotification(
                        $"EVIDENCE DESTROYED: The Traitor Report against {entry.accusedName} was incinerated."
                    );
                }
            }
        }

        // =========================================================================
        //  Corpse Looting Transfer
        // =========================================================================

        public void TransferDossiersServer(ulong deceasedClientId, ulong looterClientId)
        {
            if (!IsServer) return;

            string looterName = ResolvePlayerName(looterClientId);

            for (int i = 0; i < activeReports.Count; i++)
            {
                var entry = activeReports[i];
                if (!entry.isDestroyed && entry.carrierClientId == deceasedClientId)
                {
                    entry.carrierClientId = looterClientId;
                    activeReports[i] = entry;

                    Debug.Log($"[TraitorReportSystemNet] Dossier against {entry.accusedName} looted by {looterName}.");
                    NotifyDossierTransferredClientRpc(entry, looterClientId);
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyDossierTransferredClientRpc(TraitorReportEntry entry, ulong looterClientId)
        {
            OnReportsStateChanged?.Invoke();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == looterClientId)
            {
                if (NotificationManager.Instance != null)
                {
                    string destroyKey = KeybindingManager.GetBoundKeyString("DestroyReport", "J");
                    NotificationManager.Instance.ShowNotification(
                        "TRAITOR DOSSIER ACQUIRED",
                        $"You scavenged the physical evidence against <b>{entry.accusedName}</b>. Press [{destroyKey}] to incinerate it, or hold it until extraction!",
                        new Color(1f, 0.85f, 0.2f, 1f),
                        6.5f
                    );
                }
            }
        }

        // =========================================================================
        //  HQ Financial Audit (Match End Resolution)
        // =========================================================================

        public void AuditPendingReportsServer()
        {
            if (!IsServer) return;

            for (int i = 0; i < activeReports.Count; i++)
            {
                var entry = activeReports[i];
                if (entry.isDestroyed || entry.isAudited) continue;

                // Check financial records via MatchEconomyManager
                bool isGuilty = false;
                int dealMoneyFound = 0;

                if (MatchEconomyManager.Instance != null)
                {
                    // Check accused player's match record
                    var record = MatchEconomyManager.Instance.GetPlayerRecord(entry.accusedClientId);
                    if (record != null)
                    {
                        // Did they complete a deal and get money separate from their stake/win?
                        if (record.pactCompleted && record.pactNetCreditsEarned > 0)
                        {
                            isGuilty = true;
                            dealMoneyFound = record.pactNetCreditsEarned;
                        }
                    }
                }

                entry.isAudited = true;
                entry.isGuilty = isGuilty;
                entry.seizedDealMoney = dealMoneyFound;
                activeReports[i] = entry;

                Debug.Log($"[TraitorReportSystemNet] HQ Audit for {entry.accusedName}: Guilty={isGuilty}, DealMoney={dealMoneyFound}.");

                // Execute Financial Rewards and Penalties on Server
                ResolveAuditConsequences(entry);

                // Broadcast verdict modal trigger
                BroadcastAuditVerdictClientRpc(entry);
            }
        }

        private void ResolveAuditConsequences(TraitorReportEntry entry)
        {
            if (!IsServer) return;

            if (entry.isGuilty)
            {
                // 1. Accuser Reward (seized deal money + minimum bounty)
                int reward = Mathf.Max(MinimumTraitorBounty, entry.seizedDealMoney);
                GrantAccuserRewardClientRpc(entry.accuserClientId, reward);

                // 2. Accused Conviction (Traitor tag applied)
                ApplyTraitorConvictionClientRpc(entry.accusedClientId);
            }
            else
            {
                // Accuser False Accusation Fine (credits fine + Liar tag applied)
                ApplyFalseAccusationPenaltyClientRpc(entry.accuserClientId, FalseAccusationFine);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastAuditVerdictClientRpc(TraitorReportEntry entry)
        {
            OnVerdictReceived?.Invoke(entry);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GrantAccuserRewardClientRpc(ulong accuserClientId, int rewardCredits)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != accuserClientId) return;

            if (CloudCharacterSaveManager.Instance != null)
            {
                CloudCharacterSaveManager.Instance.AddCredits(rewardCredits);
                // Redemption: Clears 1 strike off disciplinary record
                if (CloudCharacterSaveManager.Instance.LiarStrikes > 0)
                {
                    CloudCharacterSaveManager.Instance.ClearLiarStrike(1);
                }
                else if (CloudCharacterSaveManager.Instance.TraitorStrikes > 0)
                {
                    CloudCharacterSaveManager.Instance.ClearTraitorStrike(1);
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ApplyTraitorConvictionClientRpc(ulong accusedClientId)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != accusedClientId) return;

            if (CloudCharacterSaveManager.Instance != null)
            {
                CloudCharacterSaveManager.Instance.AddTraitorStrike(1);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ApplyFalseAccusationPenaltyClientRpc(ulong accuserClientId, int fineCredits)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != accuserClientId) return;

            if (CloudCharacterSaveManager.Instance != null)
            {
                CloudCharacterSaveManager.Instance.TryDeductCredits(fineCredits);
                CloudCharacterSaveManager.Instance.AddLiarStrike(1);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyReportErrorClientRpc(ulong targetClientId, FixedString128Bytes errorMsg)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != targetClientId) return;
            OnReportError?.Invoke(errorMsg.ToString());
        }

        private string ResolvePlayerName(ulong clientId)
        {
            if (PlayerNameManager.HasSavedName())
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == clientId)
                    return PlayerNameManager.GetPlayerName();
            }
            return $"Operative {clientId}";
        }
    }
}

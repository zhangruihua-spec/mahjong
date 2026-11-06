using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using GamePlay.Client.Controller;
using GamePlay.Server.Model;
using GamePlay.Server.Model.Events;
using Mahjong.Model;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using GamePlay.SinglePlayer;

namespace GamePlay.Server.Controller.GameState
{
    public class PointTransferState : ServerState, IOnEventCallback
    {
        public IList<PointTransfer> PointTransferList;
        public IList<SettlementDetail> SettlementDetails;
        public bool NextRound;
        public bool ExtraRound;
        public bool KeepSticks;
        public string Reason;
        public bool ContinueCurrentRound;
        public int ContinuePlayerIndex;
        public bool ContinueTurnDoraAfterDiscard;
        private bool[] responds;
        private float firstTime;
        private float serverTimeOut;

        public override void OnServerStateEnter()
        {
            Debug.Log($"[Server] Transfers: {string.Join(", ", PointTransferList)}");
            PhotonNetwork.AddCallbackTarget(this);
            // var names = players.Select(player => player.PlayerName).ToArray();
            var names = CurrentRoundStatus.PlayerNames;
            var totalPlayers = SinglePlayerMode.Enabled ? CurrentRoundStatus.TotalPlayers : players.Count;
            responds = new bool[totalPlayers];
            if (SinglePlayerMode.Enabled)
            {
                for (int i = 0; i < responds.Length; i++)
                    responds[i] = SinglePlayerMode.IsAi(i);
            }
            // update points of each player
            var pointsBefore = CurrentRoundStatus.Points.ToArray();
            foreach (var transfer in PointTransferList)
            {
                ChangePoints(transfer);
            }
            if (SinglePlayerMode.Enabled)
            {
                for (int playerIndex = 0; playerIndex < totalPlayers; playerIndex++)
                {
                    var change = CurrentRoundStatus.Points[playerIndex] - pointsBefore[playerIndex];
                    Debug.Log($"[结算追踪][最终分数] 玩家={CurrentRoundStatus.GetPlayerName(playerIndex)}"
                        + $"(索引{playerIndex})，结算前={pointsBefore[playerIndex]}，变化={change:+#;-#;0}，"
                        + $"结算后={CurrentRoundStatus.Points[playerIndex]}");
                }
            }
            var info = new EventMessages.PointTransferInfo
            {
                PlayerNames = names,
                Points = CurrentRoundStatus.Points,
                PointTransfers = PointTransferList.ToArray(),
                SmileSettlements = CurrentRoundStatus.PendingSmileSettlements.ToArray(),
                SettlementDetails = SettlementDetails == null
                    ? new SettlementDetail[0]
                    : SettlementDetails.ToArray(),
                LaiziKongCounts = Enumerable.Range(0, totalPlayers)
                    .Select(CurrentRoundStatus.GetLaiziKongs).ToArray(),
                Reason = Reason,
                IsRoundEnd = !ContinueCurrentRound
            };
            if (SinglePlayerMode.Enabled)
                ClientBehaviour.Instance.RpcPointTransfer(info);
            else
                ClientBehaviour.Instance.photonView.RPC("RpcPointTransfer", RpcTarget.AllBufferedViaServer, info);
            firstTime = Time.time;
            // 结算可能依次展示详细表和分数变化，每个阶段现在最多 15 秒。
            // 本地状态机必须覆盖两个完整阶段，不能在客户端倒计时结束前先开下一局。
            serverTimeOut = ServerConstants.ServerPointTransferTimeOut + 28f;
        }

        public override void OnServerStateExit()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public override void OnStateUpdate()
        {
            if (responds.All(r => r))
            {
                Debug.Log("[Server] All players has responded, start next round");
                StartNewRound();
                return;
            }
            if (Time.time - firstTime > serverTimeOut)
            {
                // time out
                Debug.Log("[Server] Server time out, start next round");
                StartNewRound();
                return;
            }
        }

        private void StartNewRound()
        {
            if (ContinueCurrentRound)
            {
                ServerBehaviour.Instance.DrawTile(ContinuePlayerIndex, true, ContinueTurnDoraAfterDiscard);
                return;
            }
            if (CheckIfGameEnds())
                ServerBehaviour.Instance.GameEnd();
            else
                ServerBehaviour.Instance.RoundStart(NextRound, ExtraRound, KeepSticks);
        }

        /// <summary>
        /// Check if game ends. When the game ends, return true, otherwise return false
        /// </summary>
        /// <returns></returns>
        private bool CheckIfGameEnds()
        {
            // check if allow zero or negative points
            int lowestPoint = CurrentRoundStatus.Points.Min();
            switch (CurrentRoundStatus.GameSettings.PointsToGameEnd)
            {
                case PointsToGameEnd.Zero:
                    if (lowestPoint <= 0) return true;
                    break;
                case PointsToGameEnd.Negative:
                    if (lowestPoint < 0) return true;
                    break;
            }
            if (CurrentRoundStatus.GameForceEnd) return true;
            var isAllLast = CurrentRoundStatus.IsAllLast;
            if (!isAllLast) return false;
            // is all last
            var maxPoint = CurrentRoundStatus.Points.Max();
            if (NextRound) // if next round
            {
                return maxPoint >= CurrentRoundStatus.GameSettings.FirstPlacePoints;
            }
            else // if not next -- same oya
            {
                if (maxPoint < CurrentRoundStatus.GameSettings.FirstPlacePoints)
                {
                    return false;
                }
                int playerIndex = System.Array.IndexOf(CurrentRoundStatus.Points, maxPoint);
                if (playerIndex == CurrentRoundStatus.OyaPlayerIndex) // last oya is top
                {
                    return CurrentRoundStatus.GameSettings.GameEndsWhenAllLastTop;
                }
                return false;
            }
        }

        private void ChangePoints(PointTransfer transfer)
        {
            CurrentRoundStatus.ChangePoints(transfer.To, transfer.Amount);
            if (transfer.From >= 0)
                CurrentRoundStatus.ChangePoints(transfer.From, -transfer.Amount);
        }

        private void OnNextRoundEvent(int index)
        {
            responds[index] = true;
        }

        public void OnEvent(EventData photonEvent)
        {
            var code = photonEvent.Code;
            var info = photonEvent.CustomData;
            Debug.Log($"{GetType().Name} receives event code: {code} with content {info}");
            switch (code)
            {
                case EventMessages.NextRoundEvent:
                    OnNextRoundEvent((int)info);
                    break;
            }
        }
    }
}

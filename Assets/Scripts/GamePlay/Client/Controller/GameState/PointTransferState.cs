using System.Linq;
using GamePlay.Client.Model;
using GamePlay.Server.Model;
using Mahjong.Logic;
using UnityEngine;

namespace GamePlay.Client.Controller.GameState
{
    public class PointTransferState : ClientState
    {
        public string[] PlayerNames;
        public int[] Points;
        public PointTransfer[] PointTransfers;
        public SmileSettlement[] SmileSettlements;
        public SettlementDetail[] SettlementDetails;
        public int[] LaiziKongCounts;
        public string Reason;
        public bool IsRoundEnd;

        public override void OnClientStateEnter()
        {
            // 每局只记录一次；点笑等中途结算不会写入记录。
            if (IsRoundEnd)
            {
                GameRecordStore.CommitRound(System.DateTime.Now, Reason, PlayerNames,
                    SettlementDetails, LaiziKongCounts, SmileSettlements, Points);
            }
            CurrentRoundStatus.UpdatePoints(Points);
            Debug.Log($"Current points: {string.Join(",", CurrentRoundStatus.Points)}");
            var pointAndPlace = MahjongLogic.SortPointsAndPlaces(Points);
            var places = pointAndPlace.Select(v => v.Value).ToArray();
            controller.PointTransferManager.SetTransfer(CurrentRoundStatus, places, PlayerNames,
                PointTransfers, SmileSettlements, SettlementDetails, LaiziKongCounts, Reason, () =>
            {
                ClientBehaviour.Instance.NextRound();
            });
        }

        public override void OnClientStateExit()
        {
            controller.PointTransferManager.Close();
        }

        public override void OnStateUpdate()
        {
        }
    }
}

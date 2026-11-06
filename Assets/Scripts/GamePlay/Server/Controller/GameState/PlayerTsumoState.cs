using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using GamePlay.Client.Controller;
using GamePlay.Server.Model;
using GamePlay.Server.Model.Events;
using Mahjong.Model;
using Mahjong.Logic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using GamePlay.SinglePlayer;

namespace GamePlay.Server.Controller.GameState
{
    /// <summary>
    /// 自摸后的服务端结算状态。
    /// 将本局累计的胡牌、点笑、回头笑、闷笑和四癞子喜钱合并后，一次性交给结算界面展示。
    /// </summary>
    public class PlayerTsumoState : ServerState, IOnEventCallback
    {
        public int TsumoPlayerIndex;
        public Tile WinningTile;
        public MahjongSet MahjongSet;
        public PointInfo TsumoPointInfo;
        private IList<PointTransfer> transfers;
        private IList<SettlementDetail> settlementDetails;
        private bool[] responds;
        private float serverTimeOut;
        private float firstTime;
        private const float ServerMaxTimeOut = 10;

        public override void OnServerStateEnter()
        {
            PhotonNetwork.AddCallbackTarget(this);
            int totalPlayers = SinglePlayerMode.Enabled ? CurrentRoundStatus.TotalPlayers : players.Count;
            int multiplier = gameSettings.GetMultiplier(CurrentRoundStatus.IsDealer(TsumoPlayerIndex), totalPlayers);
            if (SinglePlayerMode.Enabled)
            {
                CurrentRoundStatus.LogAction(TsumoPlayerIndex, "自摸胡牌", WinningTile,
                    $"底分={CurrentRoundStatus.GameSettings.BasePoint}，开始统一结算");
                Debug.Log($"[结算追踪][胡牌] 胡牌玩家={CurrentRoundStatus.GetPlayerName(TsumoPlayerIndex)}"
                    + $"(索引{TsumoPlayerIndex})，方式=自摸，胡牌={WinningTile}");
                for (int playerIndex = 0; playerIndex < totalPlayers; playerIndex++)
                    Debug.Log($"[结算追踪][癞子杠汇总] 玩家={CurrentRoundStatus.GetPlayerName(playerIndex)}"
                        + $"(索引{playerIndex})，累计={CurrentRoundStatus.GetLaiziKongs(playerIndex)}个");
            }
            // 笑牌在发生时只记录，不立即弹出结算页；胡牌后再按付款双方的癞子杠数量统一翻倍。
            var smileTransfers = SinglePlayerMode.Enabled
                ? CurrentRoundStatus.PendingSmileTransfers.Select(ApplyLaiziMultiplier).ToList()
                : new List<PointTransfer>();
            var smileNetForWinner = smileTransfers
                .Where(t => t.To == TsumoPlayerIndex).Sum(t => t.Amount)
                - smileTransfers.Where(t => t.From == TsumoPlayerIndex).Sum(t => t.Amount);
            settlementDetails = new List<SettlementDetail>();
            var selfDrawTotal = 0;
            if (SinglePlayerMode.Enabled)
            {
                for (int playerIndex = 0; playerIndex < totalPlayers; playerIndex++)
                {
                    if (playerIndex == TsumoPlayerIndex) continue;
                    var combinedLaiziKongs = GetCombinedLaiziKongs(playerIndex, TsumoPlayerIndex);
                    var winAmount = CurrentRoundStatus.GameSettings.BasePoint * 4
                        * GetLaiziMultiplier(playerIndex, TsumoPlayerIndex);
                    selfDrawTotal += winAmount;
                    Debug.Log($"[结算追踪][自摸付款] {CurrentRoundStatus.GetPlayerName(playerIndex)}(索引{playerIndex})"
                        + $" -> {CurrentRoundStatus.GetPlayerName(TsumoPlayerIndex)}(索引{TsumoPlayerIndex})，"
                        + $"基础分={CurrentRoundStatus.GameSettings.BasePoint * 4}，双方癞子杠={combinedLaiziKongs}个，"
                        + $"倍率=×{1 << combinedLaiziKongs}，实际={winAmount}");
                    settlementDetails.Add(new SettlementDetail
                    {
                        From = playerIndex,
                        To = TsumoPlayerIndex,
                        Amount = winAmount,
                        Reason = combinedLaiziKongs > 0
                            ? $"自摸（双方赖子杠共{combinedLaiziKongs}个，×{1 << combinedLaiziKongs}）"
                            : "自摸",
                        Tile = WinningTile
                    });
                }
                foreach (var detail in CurrentRoundStatus.PendingSmileDetails)
                {
                    var combinedLaiziKongs = GetCombinedLaiziKongs(detail.From, detail.To);
                    var smileAmount = detail.Amount * GetLaiziMultiplier(detail.From, detail.To);
                    Debug.Log($"[结算追踪][笑牌付款] 类型={detail.Reason}，牌={detail.Tile}，"
                        + $"{CurrentRoundStatus.GetPlayerName(detail.From)}(索引{detail.From})"
                        + $" -> {CurrentRoundStatus.GetPlayerName(detail.To)}(索引{detail.To})，"
                        + $"基础分={detail.Amount}，双方癞子杠={combinedLaiziKongs}个，"
                        + $"倍率=×{1 << combinedLaiziKongs}，实际={smileAmount}");
                    settlementDetails.Add(new SettlementDetail
                    {
                        From = detail.From,
                        To = detail.To,
                        Amount = smileAmount,
                        Reason = combinedLaiziKongs > 0
                            ? $"{detail.Reason}（双方赖子杠共{combinedLaiziKongs}个，×{1 << combinedLaiziKongs}）"
                            : detail.Reason,
                        Tile = detail.Tile
                    });
                }
            }
            // 四癞子喜钱是独立固定项，不参与普通的癞子倍数。
            var fourLaiziBonusTransfers = new List<PointTransfer>();
            if (SinglePlayerMode.Enabled)
                CurrentRoundStatus.AppendFourLaiziBonusSettlements(
                    fourLaiziBonusTransfers, settlementDetails, MahjongSet.LaiziTile);
            var fourLaiziBonusNetForWinner = fourLaiziBonusTransfers
                .Where(t => t.To == TsumoPlayerIndex).Sum(t => t.Amount)
                - fourLaiziBonusTransfers.Where(t => t.From == TsumoPlayerIndex).Sum(t => t.Amount);
            var netInfo = new NetworkPointInfo
            {
                Fu = TsumoPointInfo.Fu,
                YakuValues = SinglePlayerMode.Enabled
                    ? System.Array.Empty<YakuValue>()
                    : TsumoPointInfo.YakuList.ToArray(),
                Dora = SinglePlayerMode.Enabled ? 0 : TsumoPointInfo.Dora,
                UraDora = SinglePlayerMode.Enabled ? 0 : TsumoPointInfo.UraDora,
                RedDora = SinglePlayerMode.Enabled ? 0 : TsumoPointInfo.RedDora,
                IsQTJ = SinglePlayerMode.Enabled || TsumoPointInfo.IsQTJ
            };
            var info = new EventMessages.TsumoInfo
            {
                TsumoPlayerIndex = TsumoPlayerIndex,
                TsumoPlayerName = CurrentRoundStatus.GetPlayerName(TsumoPlayerIndex),
                TsumoHandData = CurrentRoundStatus.HandData(TsumoPlayerIndex),
                WinningTile = WinningTile,
                DoraIndicators = MahjongSet.DoraIndicators,
                UraDoraIndicators = MahjongSet.UraDoraIndicators,
                IsRichi = CurrentRoundStatus.RichiStatus(TsumoPlayerIndex),
                TsumoPointInfo = netInfo,
                TotalPoints = SinglePlayerMode.Enabled
                    ? selfDrawTotal + smileNetForWinner + fourLaiziBonusNetForWinner
                    : TsumoPointInfo.BasePoint * multiplier,
                SettlementPlayerNames = CurrentRoundStatus.PlayerNames,
                SettlementDetails = settlementDetails.ToArray(),
                LaiziKongCounts = SinglePlayerMode.Enabled
                    ? Enumerable.Range(0, totalPlayers)
                        .Select(CurrentRoundStatus.GetLaiziKongs).ToArray()
                    : new int[0],
                SmileSettlements = SinglePlayerMode.Enabled
                    ? CurrentRoundStatus.PendingSmileSettlements.ToArray()
                    : new SmileSettlement[0]
            };
            // 单机模式直接调用本地客户端；联网模式才通过 Photon RPC 广播。
            if (SinglePlayerMode.Enabled)
                ClientBehaviour.Instance.RpcTsumo(info);
            else
                ClientBehaviour.Instance.photonView.RPC("RpcTsumo", RpcTarget.AllBufferedViaServer, info);
            // 生成自摸转账：除胡牌者外，其余三家分别向胡牌者付款。
            transfers = new List<PointTransfer>();
            for (int playerIndex = 0; playerIndex < totalPlayers; playerIndex++)
            {
                if (playerIndex == TsumoPlayerIndex) continue;
                int amount = SinglePlayerMode.Enabled
                    ? CurrentRoundStatus.GameSettings.BasePoint * 4
                        * GetLaiziMultiplier(playerIndex, TsumoPlayerIndex)
                    : TsumoPointInfo.BasePoint;
                if (!SinglePlayerMode.Enabled && CurrentRoundStatus.IsDealer(playerIndex)) amount *= 2;
                int extraPoints = SinglePlayerMode.Enabled ? 0 : CurrentRoundStatus.ExtraPoints;
                transfers.Add(new PointTransfer
                {
                    From = playerIndex,
                    To = TsumoPlayerIndex,
                    Amount = amount + extraPoints
                });
            }
            // 保留原联网规则的立直棒入口；单机规则下通常为 0。
            transfers.Add(new PointTransfer
            {
                From = -1,
                To = TsumoPlayerIndex,
                Amount = CurrentRoundStatus.RichiSticksPoints
            });
            // 把行牌过程中累计的笑牌付款并入本次最终结算。
            foreach (var smileTransfer in smileTransfers)
                transfers.Add(smileTransfer);
            // 四癞子喜钱最后追加，明确避开上面的倍率计算。
            foreach (var bonusTransfer in fourLaiziBonusTransfers)
                transfers.Add(bonusTransfer);
            if (SinglePlayerMode.Enabled)
                Debug.Log($"[结算追踪][最终转账列表] {string.Join("；", transfers.Select(FormatTransfer))}");
            responds = new bool[totalPlayers];
            if (SinglePlayerMode.Enabled)
            {
                for (int i = 0; i < responds.Length; i++)
                    responds[i] = SinglePlayerMode.IsAi(i);
            }
            // determine server time out
            serverTimeOut = ServerMaxTimeOut + ServerConstants.ServerTimeBuffer;
            firstTime = Time.time;
        }

        /// <summary>返回一笔转账中付款方与收款方累计杠出的癞子数量。</summary>
        private int GetCombinedLaiziKongs(int from, int to)
        {
            if (!SinglePlayerMode.Enabled) return 0;
            var total = 0;
            if (from >= 0 && from < CurrentRoundStatus.TotalPlayers)
                total += CurrentRoundStatus.GetLaiziKongs(from);
            if (to >= 0 && to < CurrentRoundStatus.TotalPlayers)
                total += CurrentRoundStatus.GetLaiziKongs(to);
            return total;
        }

        /// <summary>每杠一个癞子翻一倍，所以倍率为 2 的“双方癞子杠总数”次方。</summary>
        private int GetLaiziMultiplier(int from, int to)
        {
            return 1 << GetCombinedLaiziKongs(from, to);
        }

        private PointTransfer ApplyLaiziMultiplier(PointTransfer transfer)
        {
            transfer.Amount *= GetLaiziMultiplier(transfer.From, transfer.To);
            return transfer;
        }

        private string FormatTransfer(PointTransfer transfer)
        {
            var fromName = transfer.From >= 0
                ? CurrentRoundStatus.GetPlayerName(transfer.From)
                : "系统";
            var toName = transfer.To >= 0
                ? CurrentRoundStatus.GetPlayerName(transfer.To)
                : "系统";
            return $"{fromName}({transfer.From}) -> {toName}({transfer.To})：{transfer.Amount}";
        }

        public override void OnServerStateExit()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public override void OnStateUpdate()
        {
            if (Time.time - firstTime > serverTimeOut || responds.All(r => r))
            {
                PointTransfer();
                return;
            }
        }

        private void PointTransfer()
        {
            var next = CurrentRoundStatus.OyaPlayerIndex != TsumoPlayerIndex;
            var smileReason = CurrentRoundStatus.PendingSmileReason;
            var reason = string.IsNullOrEmpty(smileReason)
                ? "胡牌结算"
                : "胡牌 + " + smileReason + "结算";
            // 胡牌面板已显示完整明细，后续状态只播放加减分动画，避免重复生成一张结算表。
            ServerBehaviour.Instance.PointTransfer(transfers, next, !next, false, reason);
        }

        private void OnClientReadyEvent(int index)
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
                case EventMessages.ClientReadyEvent:
                    OnClientReadyEvent((int)photonEvent.CustomData);
                    break;
            }
        }
    }
}

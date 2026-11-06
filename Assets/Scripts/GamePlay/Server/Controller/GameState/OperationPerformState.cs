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
    public class OperationPerformState : ServerState, IOnEventCallback
    {
        public int CurrentPlayerIndex;
        public int DiscardPlayerIndex;
        public OutTurnOperation Operation;
        public MahjongSet MahjongSet;
        public bool ContinueAfterSpecialKong;
        private bool turnDoraAfterDiscard;
        private float firstSendTime;
        private float serverTimeOut;
        private float aiActionTime;
        private bool aiActed;
        private InTurnOperation[] postPongOperations;

        public override void OnServerStateEnter()
        {
            PhotonNetwork.AddCallbackTarget(this);
            // 三张前位闷笑已经在 PlayerKongState 中完成牌数更新，此处只负责继续出牌。
            if (ContinueAfterSpecialKong)
            {
                CurrentRoundStatus.CurrentPlayerIndex = CurrentPlayerIndex;
                CurrentRoundStatus.LogAction(CurrentPlayerIndex, "三张闷笑后出牌", null,
                    "不从牌堆尾部补牌，直接选择一张普通牌打出");
            }
            else
            {
                // update hand data
                UpdateRoundStatus();
                if (SinglePlayerMode.Enabled)
                {
                    var action = Operation.Type == OutTurnOperationType.Pong ? "碰"
                        : Operation.Type == OutTurnOperationType.Kong ? "点笑"
                        : Operation.Type == OutTurnOperationType.Chow ? "吃"
                        : Operation.Type.ToString();
                    CurrentRoundStatus.LogAction(CurrentPlayerIndex, action, Operation.Meld.First,
                        $"来源玩家={CurrentRoundStatus.GetPlayerName(DiscardPlayerIndex)}"
                        + $"(索引{DiscardPlayerIndex})，来源牌={Operation.Tile}，操作={Operation.Type}");
                }
            }
            RecordLaiziPreviousPong();
            postPongOperations = GetPostPongOperations();
            if (SinglePlayerMode.Enabled && Operation.Type == OutTurnOperationType.Pong)
                CurrentRoundStatus.LogAction(CurrentPlayerIndex, "碰后操作候选", Operation.Meld.First,
                    $"候选=[{string.Join("|", postPongOperations.Select(op => op.Type))}]");
            // send messages
            if (SinglePlayerMode.Enabled)
            {
                ClientBehaviour.Instance.RpcOperationPerform(GetInfo(SinglePlayerMode.HumanPlayerIndex));
            }
            else
            for (int i = 0; i < players.Count; i++)
            {
                var info = GetInfo(i);
                var player = CurrentRoundStatus.GetPlayer(i);
                ClientBehaviour.Instance.photonView.RPC("RpcOperationPerform", player, info);
            }
            KongOperation();
            firstSendTime = Time.time;
            aiActionTime = Time.time + SinglePlayerMode.RandomAiDelay();
            serverTimeOut = gameSettings.BaseTurnTime + CurrentRoundStatus.MaxBonusTurnTime + ServerConstants.ServerTimeBuffer;
        }

        private void RecordLaiziPreviousPong()
        {
            if (!SinglePlayerMode.Enabled
                || Operation.Type != OutTurnOperationType.Pong
                || !Operation.Meld.First.EqualsIgnoreColor(MahjongSet.LaiziPreviousTile))
                return;
            var amount = CurrentRoundStatus.GameSettings.BasePoint * 2;
            var transfers = new List<PointTransfer>
            {
                new PointTransfer
                {
                    From = DiscardPlayerIndex,
                    To = CurrentPlayerIndex,
                    Amount = amount
                }
            };
            CurrentRoundStatus.AddPendingSmileSettlement(
                "前位碰", CurrentPlayerIndex, Operation.Meld.First, transfers);
            Debug.Log($"[结算追踪][赖子前位碰] 癞子={MahjongSet.LaiziTile}，"
                + $"前位牌={MahjongSet.LaiziPreviousTile}，"
                + $"{CurrentRoundStatus.GetPlayerName(DiscardPlayerIndex)}(索引{DiscardPlayerIndex})"
                + $" -> {CurrentRoundStatus.GetPlayerName(CurrentPlayerIndex)}(索引{CurrentPlayerIndex})，"
                + $"基础分={amount}");
        }

        private EventMessages.OperationPerformInfo GetInfo(int index)
        {
            if (index == CurrentPlayerIndex)
            {
                return new EventMessages.OperationPerformInfo
                {
                    PlayerIndex = CurrentPlayerIndex,
                    OperationPlayerIndex = CurrentPlayerIndex,
                    Operation = Operation,
                    HandData = CurrentRoundStatus.HandData(CurrentPlayerIndex),
                    BonusTurnTime = CurrentRoundStatus.GetBonusTurnTime(CurrentPlayerIndex),
                    Rivers = CurrentRoundStatus.Rivers,
                    InTurnOperations = postPongOperations,
                    MahjongSetData = MahjongSet.Data
                };
            }
            else
            {
                return new EventMessages.OperationPerformInfo
                {
                    PlayerIndex = index,
                    OperationPlayerIndex = CurrentPlayerIndex,
                    Operation = Operation,
                    HandData = new PlayerHandData
                    {
                        HandTiles = new Tile[CurrentRoundStatus.HandTiles(CurrentPlayerIndex).Length],
                        OpenMelds = CurrentRoundStatus.OpenMelds(CurrentPlayerIndex)
                    },
                    Rivers = CurrentRoundStatus.Rivers,
                    MahjongSetData = MahjongSet.Data
                };
            }
        }

        private void UpdateRoundStatus()
        {
            CurrentRoundStatus.CurrentPlayerIndex = CurrentPlayerIndex;
            // update hand tiles and open melds
            CurrentRoundStatus.RemoveFromRiver(DiscardPlayerIndex);
            CurrentRoundStatus.AddMeld(CurrentPlayerIndex, Operation.Meld);
            CurrentRoundStatus.RemoveTile(CurrentPlayerIndex, Operation.Meld);
            turnDoraAfterDiscard = Operation.Type == OutTurnOperationType.Kong;
        }

        private void KongOperation()
        {
            if (Operation.Type != OutTurnOperationType.Kong) return;
            if (SinglePlayerMode.Enabled)
            {
                var amount = CurrentRoundStatus.GameSettings.BasePoint * 2;
                var transfers = new List<PointTransfer>
                {
                    new PointTransfer
                    {
                        From = DiscardPlayerIndex,
                        To = CurrentPlayerIndex,
                        Amount = amount
                    }
                };
                var canContinue = MahjongSet.TilesRemain > 0;
                CurrentRoundStatus.AddPendingSmileSettlement(
                    "点笑", CurrentPlayerIndex, Operation.Meld.First, transfers);
                if (canContinue)
                    ServerBehaviour.Instance.DrawTile(CurrentPlayerIndex, true, turnDoraAfterDiscard);
                else
                    ServerBehaviour.Instance.ExhaustiveDrawSettlement(false);
                return;
            }
            ServerBehaviour.Instance.DrawTile(CurrentPlayerIndex, true, turnDoraAfterDiscard);
        }

        private InTurnOperation[] GetPostPongOperations()
        {
            var operations = new List<InTurnOperation>
            {
                new InTurnOperation { Type = InTurnOperationType.Discard }
            };
            if (!SinglePlayerMode.Enabled
                || (!ContinueAfterSpecialKong && Operation.Type != OutTurnOperationType.Pong))
                return operations.ToArray();

            var laizi = MahjongSet.LaiziTile;
            var actualLaizi = CurrentRoundStatus.HandTiles(CurrentPlayerIndex)
                .FirstOrDefault(tile => tile.EqualsIgnoreColor(laizi));
            if (actualLaizi.Rank > 0)
            {
                operations.Add(new InTurnOperation
                {
                    Type = InTurnOperationType.Bei,
                    Tile = actualLaizi
                });
            }
            return operations.ToArray();
        }

        private void HandlePostPongLaiziKong(EventMessages.InTurnOperationInfo info)
        {
            if (info.PlayerIndex != CurrentPlayerIndex
                || info.Operation.Type != InTurnOperationType.Bei
                || postPongOperations == null
                || !postPongOperations.Any(op => op.Type == InTurnOperationType.Bei))
                return;

            var actualLaizi = CurrentRoundStatus.HandTiles(CurrentPlayerIndex)
                .FirstOrDefault(tile => tile.EqualsIgnoreColor(MahjongSet.LaiziTile));
            if (actualLaizi.Rank <= 0) return;

            CurrentRoundStatus.LogAction(CurrentPlayerIndex, "碰后操作选择", actualLaizi,
                $"选择={info.Operation.Type}");
            CurrentRoundStatus.RemoveTile(CurrentPlayerIndex, actualLaizi);
            CurrentRoundStatus.AddToRiver(CurrentPlayerIndex, actualLaizi, false, true);
            CurrentRoundStatus.AddLaiziKong(CurrentPlayerIndex, actualLaizi);
            CurrentRoundStatus.SortHandTiles(CurrentPlayerIndex);
            CurrentRoundStatus.SetBonusTurnTime(CurrentPlayerIndex, info.BonusTurnTime);
            ServerBehaviour.Instance.DrawTile(CurrentPlayerIndex, true, false);
        }

        private void OnDiscardTileEvent(EventMessages.DiscardTileInfo info)
        {
            if (info.PlayerIndex != CurrentRoundStatus.CurrentPlayerIndex)
            {
                Debug.Log($"[Server] It is not player {info.PlayerIndex}'s turn to discard a tile, ignoring this message");
                return;
            }
            // Change to discardTileState
            ServerBehaviour.Instance.DiscardTile(
                info.PlayerIndex, info.Tile, info.IsRichiing,
                info.DiscardingLastDraw, info.BonusTurnTime, turnDoraAfterDiscard);
        }

        public override void OnServerStateExit()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public override void OnStateUpdate()
        {
            if (SinglePlayerMode.IsAi(CurrentPlayerIndex) && !aiActed && Time.time >= aiActionTime)
            {
                aiActed = true;
                if (postPongOperations != null
                    && postPongOperations.Any(op => op.Type == InTurnOperationType.Bei))
                {
                    var postPongDecision = SinglePlayerMode.ChooseInTurnOperation(
                        CurrentRoundStatus, CurrentPlayerIndex, postPongOperations,
                        null, MahjongSet.LaiziTile);
                    if (postPongDecision.Type == InTurnOperationType.Bei)
                    {
                        CurrentRoundStatus.LogAction(CurrentPlayerIndex, "AI碰后决策",
                            postPongDecision.Tile, "选择=Bei（癞子杠）");
                        HandlePostPongLaiziKong(new EventMessages.InTurnOperationInfo
                        {
                            PlayerIndex = CurrentPlayerIndex,
                            Operation = postPongDecision,
                            BonusTurnTime = 0
                        });
                        return;
                    }
                }
                var tiles = CurrentRoundStatus.HandTiles(CurrentPlayerIndex);
                var discard = SinglePlayerMode.ChooseDiscard(CurrentRoundStatus, CurrentPlayerIndex,
                    null, Operation.ForbiddenTiles, MahjongSet.LaiziTile);
                ServerBehaviour.Instance.DiscardTile(CurrentPlayerIndex, discard, false, false, 0,
                    turnDoraAfterDiscard);
                return;
            }
            // time out: auto discard
            if (Time.time - firstSendTime > serverTimeOut)
            {
                // 碰后超时也按完整规则选一张非癞子牌，不能把最右侧癞子直接打出。
                var discard = SinglePlayerMode.Enabled
                    ? SinglePlayerMode.ChooseDiscard(CurrentRoundStatus, CurrentPlayerIndex,
                        null, Operation.ForbiddenTiles, MahjongSet.LaiziTile)
                    : CurrentRoundStatus.HandTiles(CurrentPlayerIndex).Last();
                if (discard.Rank > 0)
                    ServerBehaviour.Instance.DiscardTile(CurrentPlayerIndex, discard, false,
                        false, 0, turnDoraAfterDiscard);
            }
        }

        public void OnEvent(EventData photonEvent)
        {
            var code = photonEvent.Code;
            var info = photonEvent.CustomData;
            Debug.Log($"{GetType().Name} receives event code: {code} with content {info}");
            switch (code)
            {
                case EventMessages.DiscardTileEvent:
                    OnDiscardTileEvent((EventMessages.DiscardTileInfo)info);
                    break;
                case EventMessages.InTurnOperationEvent:
                    HandlePostPongLaiziKong((EventMessages.InTurnOperationInfo)info);
                    break;
            }
        }
    }
}

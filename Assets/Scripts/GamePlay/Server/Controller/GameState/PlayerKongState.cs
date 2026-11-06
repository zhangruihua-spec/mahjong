using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using GamePlay.Client.Controller;
using GamePlay.Server.Model;
using GamePlay.Server.Model.Events;
using Mahjong.Logic;
using Mahjong.Model;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using GamePlay.SinglePlayer;

namespace GamePlay.Server.Controller.GameState
{
    public class PlayerKongState : ServerState, IOnEventCallback
    {
        public int CurrentPlayerIndex;
        public MahjongSet MahjongSet;
        public OpenMeld Kong;
        private bool[] responds;
        private OutTurnOperation[] outTurnOperations;
        private float firstTime;
        private float serverTimeOut;

        public override void OnServerStateEnter()
        {
            PhotonNetwork.AddCallbackTarget(this);
            // update hand tiles and open melds
            UpdateRoundStatus();
            responds = new bool[players.Count];
            outTurnOperations = new OutTurnOperation[players.Count];
            // send messages
            if (SinglePlayerMode.Enabled)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    var choices = GetKongOperations(i);
                    if (SinglePlayerMode.IsAi(i))
                    {
                        responds[i] = true;
                        outTurnOperations[i] = SinglePlayerMode.ChooseOutTurnOperation(choices);
                    }
                    else ClientBehaviour.Instance.RpcKong(GetInfo(i));
                }
            }
            else
            for (int i = 0; i < players.Count; i++)
            {
                var info = GetInfo(i);
                var player = CurrentRoundStatus.GetPlayer(i);
                ClientBehaviour.Instance.photonView.RPC("RpcKong", player, info);
            }
            firstTime = Time.time;
            serverTimeOut = CurrentRoundStatus.MaxBonusTurnTime + gameSettings.BaseTurnTime + ServerConstants.ServerTimeBuffer;
        }

        private void UpdateRoundStatus()
        {
            if (!CurrentRoundStatus.LastDraw.HasValue)
            {
                Debug.LogWarning("Kong state entered without a pending draw; skipping tile commit.");
                return;
            }
            var lastDraw = (Tile)CurrentRoundStatus.LastDraw;
            CurrentRoundStatus.LastDraw = null;
            CurrentRoundStatus.AddTile(CurrentPlayerIndex, lastDraw);
            if (Kong.IsAdded) // add kong
            {
                CurrentRoundStatus.AddKong(CurrentPlayerIndex, Kong);
                CurrentRoundStatus.RemoveTile(CurrentPlayerIndex, Kong.Extra);
            }
            else // self kong
            {
                CurrentRoundStatus.AddMeld(CurrentPlayerIndex, Kong);
                CurrentRoundStatus.RemoveTile(CurrentPlayerIndex, Kong);
            }
            CurrentRoundStatus.SortHandTiles();
            if (SinglePlayerMode.Enabled)
            {
                var action = Kong.IsAdded ? "回头笑" : "闷笑";
                CurrentRoundStatus.LogAction(CurrentPlayerIndex, action, Kong.First,
                    $"杠牌={string.Join("", Kong.Tiles)}，"
                    + $"是否赖子前位特殊杠={Kong.IsLaiziPreviousKong}，"
                    + (Kong.IsLaiziPreviousKong
                        ? "三张特殊闷笑不补牌，下一步直接出一张"
                        : "下一步从牌堆尾部补摸"));
            }
            // turn dora if this is a self kong
            if (Kong.Side == MeldSide.Self)
                MahjongSet.TurnDora();
        }

        private EventMessages.KongInfo GetInfo(int index)
        {
            if (index == CurrentPlayerIndex)
            {
                return new EventMessages.KongInfo
                {
                    PlayerIndex = CurrentPlayerIndex,
                    KongPlayerIndex = CurrentPlayerIndex,
                    HandData = CurrentRoundStatus.HandData(CurrentPlayerIndex),
                    BonusTurnTime = CurrentRoundStatus.GetBonusTurnTime(CurrentPlayerIndex),
                    Operations = GetKongOperations(CurrentPlayerIndex),
                    MahjongSetData = MahjongSet.Data
                };
            }
            else
            {
                return new EventMessages.KongInfo
                {
                    PlayerIndex = index,
                    KongPlayerIndex = CurrentPlayerIndex,
                    HandData = new PlayerHandData
                    {
                        HandTiles = new Tile[CurrentRoundStatus.HandTiles(CurrentPlayerIndex).Length],
                        OpenMelds = CurrentRoundStatus.OpenMelds(CurrentPlayerIndex)
                    },
                    BonusTurnTime = CurrentRoundStatus.GetBonusTurnTime(CurrentPlayerIndex),
                    Operations = GetKongOperations(index),
                    MahjongSetData = MahjongSet.Data
                };
            }
        }

        private OutTurnOperation[] GetKongOperations(int playerIndex)
        {
            var operations = new List<OutTurnOperation>();
            operations.Add(new OutTurnOperation
            {
                Type = OutTurnOperationType.Skip
            });
            if (playerIndex == CurrentPlayerIndex) return operations.ToArray();
            // rob kong test
            TestRobKong(playerIndex, operations);
            return operations.ToArray();
        }

        private Tile GetTileFromKong()
        {
            if (Kong.Side == MeldSide.Self) return Kong.First;
            return Kong.Tile;
        }

        private void TestRobKong(int playerIndex, IList<OutTurnOperation> operations)
        {
            if (SinglePlayerMode.Enabled) return;
            var tile = GetTileFromKong();
            var point = GetRongInfo(playerIndex, tile);
            if (!gameSettings.CheckConstraint(point)) return;
            if (Kong.Side == MeldSide.Self)
            {
                // handle self kong
                if (gameSettings.AllowGswsRobConcealedKong &&
                    point.YakuList.Any(yaku => yaku.Name.StartsWith("国士无双")))
                {
                    operations.Add(new OutTurnOperation
                    {
                        Type = OutTurnOperationType.Rong,
                        Tile = tile,
                        HandData = CurrentRoundStatus.HandData(playerIndex)
                    });
                }
            }
            else
            {
                // handle added kong
                operations.Add(new OutTurnOperation
                {
                    Type = OutTurnOperationType.Rong,
                    Tile = tile,
                    HandData = CurrentRoundStatus.HandData(playerIndex)
                });
            }
        }

        private PointInfo GetRongInfo(int playerIndex, Tile tile)
        {
            var baseHandStatus = HandStatus.RobKong;
            var allTiles = MahjongSet.AllTiles;
            var doraTiles = MahjongSet.DoraIndicators.Select(
                indicator => MahjongLogic.GetDoraTile(indicator, allTiles)).ToArray();
            var uraDoraTiles = MahjongSet.UraDoraIndicators.Select(
                indicator => MahjongLogic.GetDoraTile(indicator, allTiles)).ToArray();
            var beiDora = CurrentRoundStatus.GetBeiDora(playerIndex);
            var point = ServerMahjongLogic.GetPointInfo(
                playerIndex, CurrentRoundStatus, tile, baseHandStatus,
                doraTiles, uraDoraTiles, beiDora, gameSettings);
            return point;
        }

        public override void OnServerStateExit()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public override void OnStateUpdate()
        {
            // check operations: if all operations are skip, let the current player to draw his lingshang
            // if some one claimed rong, transfer to TurnEndState handling rong operations
            if (Time.time - firstTime > serverTimeOut)
            {
                for (int i = 0; i < responds.Length; i++)
                {
                    if (responds[i]) continue;
                    // players[i].BonusTurnTime = 0;
                    outTurnOperations[i] = new OutTurnOperation { Type = OutTurnOperationType.Skip };
                    NextState();
                    return;
                }
            }
            if (responds.All(r => r))
            {
                Debug.Log("[Server] Server received all operation response, handling results.");
                NextState();
            }
        }

        private void NextState()
        {
            if (outTurnOperations.All(op => op.Type == OutTurnOperationType.Skip))
            {
                // no one claimed a rob kong
                var turnDoraAfterDiscard = Kong.Side != MeldSide.Self;
                CurrentRoundStatus.BreakOneShotsAndFirstTurn();
                // If the live wall is exhausted, there is no replacement
                // tile to draw after the kong. Start the next hand cleanly
                // instead of allowing DrawLingShang to throw.
                var canContinue = MahjongSet.TilesRemain > 0;
                if (SinglePlayerMode.Enabled && (Kong.IsAdded || Kong.Side == MeldSide.Self))
                {
                    var reason = Kong.IsAdded ? "回头笑" : "闷笑";
                    var amount = CurrentRoundStatus.GameSettings.BasePoint * (Kong.IsAdded ? 2 : 4);
                    var transfers = new List<PointTransfer>();
                    for (int i = 0; i < CurrentRoundStatus.TotalPlayers; i++)
                    {
                        if (i == CurrentPlayerIndex) continue;
                        transfers.Add(new PointTransfer { From = i, To = CurrentPlayerIndex, Amount = amount });
                    }
                    CurrentRoundStatus.AddPendingSmileSettlement(
                        reason, CurrentPlayerIndex, Kong.First, transfers);
                }
                // 癞子前一位三张组成的特殊闷笑不再补牌；直接出一张即可恢复标准牌数。
                if (SinglePlayerMode.Enabled && Kong.IsLaiziPreviousKong)
                {
                    ServerBehaviour.Instance.DiscardAfterLaiziPreviousKong(CurrentPlayerIndex);
                    return;
                }
                if (!canContinue)
                {
                    ServerBehaviour.Instance.ExhaustiveDrawSettlement(false);
                    return;
                }
                ServerBehaviour.Instance.DrawTile(CurrentPlayerIndex, true, turnDoraAfterDiscard);
                return;
            }
            if (outTurnOperations.Any(op => op.Type == OutTurnOperationType.Rong))
            {
                var discardingTile = GetTileFromKong();
                ServerBehaviour.Instance.TurnEnd(CurrentPlayerIndex, discardingTile, false, outTurnOperations, true, false);
                return;
            }
            Debug.LogError($"[Server] Logically cannot reach here, operations are {string.Join("|", outTurnOperations)}");
        }

        private void OnOutTurnOperationEvent(EventMessages.OutTurnOperationInfo info)
        {
            var index = info.PlayerIndex;
            if (responds[index]) return;
            responds[index] = true;
            outTurnOperations[index] = info.Operation;
            CurrentRoundStatus.SetBonusTurnTime(index, info.BonusTurnTime);
        }

        public void OnEvent(EventData photonEvent)
        {
            var code = photonEvent.Code;
            var info = photonEvent.CustomData;
            Debug.Log($"{GetType().Name} receives event code: {code} with content {info}");
            switch (code)
            {
                case EventMessages.OutTurnOperationEvent:
                    OnOutTurnOperationEvent((EventMessages.OutTurnOperationInfo)info);
                    break;
            }
        }
    }
}

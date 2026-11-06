using System.Linq;
using Common.StateMachine;
using Common.StateMachine.Interfaces;
using GamePlay.Client.Controller.GameState;
using GamePlay.Client.Model;
using GamePlay.Server.Model;
using Mahjong.Model;
using Photon.Pun;
using Photon.Realtime;
using GamePlay.Server.Model.Events;
using UnityEngine;
using GamePlay.SinglePlayer;
using GamePlay.Server.Controller;

namespace GamePlay.Client.Controller
{
    public class ClientBehaviour : MonoBehaviourPunCallbacks
    {
        public static ClientBehaviour Instance { get; private set; }
        private ClientRoundStatus CurrentRoundStatus;
        private ViewController controller;
        private bool localDiscardPending;
        public IStateMachine StateMachine { get; private set; }

        public override void OnEnable()
        {
            base.OnEnable();
            Debug.Log("ClientBehaviour.OnEnable() is called");
            Instance = this;
            StateMachine = new StateMachine();
        }

        private void Start()
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                var player = PhotonNetwork.LocalPlayer;
                PhotonNetwork.RaiseEvent(
                    EventMessages.LoadCompleteEvent, player.ActorNumber,
                    EventMessages.ToMaster, EventMessages.SendReliable);
            }
            controller = ViewController.Instance;
        }

        [PunRPC]
        public void RpcGamePrepare(EventMessages.GamePrepareInfo info)
        {
            CurrentRoundStatus = new ClientRoundStatus(info.PlayerIndex, info.GameSetting);
            var prepareState = new GamePrepareState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                Points = info.Points,
                Names = info.PlayerNames
            };
            StateMachine.ChangeState(prepareState);
        }

        [PunRPC]
        public void RpcRoundStart(EventMessages.RoundStartInfo info)
        {
            localDiscardPending = false;
            var startState = new RoundStartState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                LocalPlayerHandTiles = info.InitialHandTiles,
                OyaPlayerIndex = info.OyaPlayerIndex,
                Dice = info.Dice,
                Field = info.Field,
                Extra = info.Extra,
                RichiSticks = info.RichiSticks,
                MahjongSetData = info.MahjongSetData,
                Points = info.Points
            };
            StateMachine.ChangeState(startState);
        }

        [PunRPC]
        public void RpcDrawTile(EventMessages.DrawTileInfo info)
        {
            if (info.DrawPlayerIndex == CurrentRoundStatus.LocalPlayerIndex)
                localDiscardPending = false;
            var drawState = new PlayerDrawState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                PlayerIndex = info.DrawPlayerIndex,
                Tile = info.Tile,
                BonusTurnTime = info.BonusTurnTime,
                Zhenting = info.Zhenting,
                MahjongSetData = info.MahjongSetData,
                Operations = info.Operations,
                HandTiles = info.HandTiles,
                Rivers = info.Rivers
            };
            StateMachine.ChangeState(drawState);
        }

        [PunRPC]
        public void RpcKong(EventMessages.KongInfo message)
        {
            var kongState = new PlayerKongState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                KongPlayerIndex = message.KongPlayerIndex,
                HandData = message.HandData,
                BonusTurnTime = message.BonusTurnTime,
                Operations = message.Operations,
                MahjongSetData = message.MahjongSetData
            };
            StateMachine.ChangeState(kongState);
        }

        [PunRPC]
        public void RpcBeiDora(EventMessages.BeiDoraInfo message)
        {
            var beiDoraState = new PlayerBeiDoraState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                BeiDoraPlayerIndex = message.BeiDoraPlayerIndex,
                BeiDoras = message.BeiDoras,
                HandData = message.HandData,
                BonusTurnTime = message.BonusTurnTime,
                Operations = message.Operations,
                MahjongSetData = message.MahjongSetData
            };
            StateMachine.ChangeState(beiDoraState);
        }

        [PunRPC]
        public void RpcDiscardOperation(EventMessages.DiscardOperationInfo info)
        {
            var discardOperationState = new PlayerDiscardOperationState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                CurrentPlayerIndex = info.CurrentTurnPlayerIndex,
                IsRichiing = info.IsRichiing,
                DiscardingLastDraw = info.DiscardingLastDraw,
                Tile = info.Tile,
                BonusTurnTime = info.BonusTurnTime,
                Zhenting = info.Zhenting,
                Operations = info.Operations,
                HandTiles = info.HandTiles,
                Rivers = info.Rivers
            };
            StateMachine.ChangeState(discardOperationState);
        }

        [PunRPC]
        public void RpcTurnEnd(EventMessages.TurnEndInfo info)
        {
            var turnEndState = new PlayerTurnEndState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                PlayerIndex = info.PlayerIndex,
                ChosenOperationType = info.ChosenOperationType,
                Operations = info.Operations,
                Points = info.Points,
                RichiStatus = info.RichiStatus,
                RichiSticks = info.RichiSticks,
                Zhenting = info.Zhenting,
                MahjongSetData = info.MahjongSetData
            };
            StateMachine.ChangeState(turnEndState);
        }

        [PunRPC]
        public void RpcOperationPerform(EventMessages.OperationPerformInfo info)
        {
            if (info.OperationPlayerIndex == CurrentRoundStatus.LocalPlayerIndex)
                localDiscardPending = false;
            var operationState = new PlayerOperationPerformState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                PlayerIndex = info.PlayerIndex,
                OperationPlayerIndex = info.OperationPlayerIndex,
                Operation = info.Operation,
                HandData = info.HandData,
                BonusTurnTime = info.BonusTurnTime,
                Rivers = info.Rivers,
                InTurnOperations = info.InTurnOperations,
                MahjongSetData = info.MahjongSetData
            };
            StateMachine.ChangeState(operationState);
        }

        [PunRPC]
        public void RpcTsumo(EventMessages.TsumoInfo info)
        {
            var tsumoState = new PlayerTsumoState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                TsumoPlayerIndex = info.TsumoPlayerIndex,
                TsumoPlayerName = info.TsumoPlayerName,
                TsumoHandData = info.TsumoHandData,
                WinningTile = info.WinningTile,
                DoraIndicators = info.DoraIndicators,
                UraDoraIndicators = info.UraDoraIndicators,
                IsRichi = info.IsRichi,
                TsumoPointInfo = info.TsumoPointInfo,
                TotalPoints = info.TotalPoints,
                SettlementPlayerNames = info.SettlementPlayerNames,
                SettlementDetails = info.SettlementDetails,
                LaiziKongCounts = info.LaiziKongCounts,
                SmileSettlements = info.SmileSettlements
            };
            StateMachine.ChangeState(tsumoState);
        }

        [PunRPC]
        public void RpcRong(EventMessages.RongInfo message)
        {
            var rongState = new PlayerRongState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                RongPlayerIndices = message.RongPlayerIndices,
                RongPlayerNames = message.RongPlayerNames,
                HandData = message.HandData,
                WinningTile = message.WinningTile,
                DoraIndicators = message.DoraIndicators,
                UraDoraIndicators = message.UraDoraIndicators,
                RongPlayerRichiStatus = message.RongPlayerRichiStatus,
                RongPointInfos = message.RongPointInfos,
                TotalPoints = message.TotalPoints
            };
            StateMachine.ChangeState(rongState);
        }

        [PunRPC]
        public void RpcRoundDraw(EventMessages.RoundDrawInfo info)
        {
            var roundDrawState = new RoundDrawState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                RoundDrawType = info.RoundDrawType,
                WaitingData = info.WaitingData
            };
            StateMachine.ChangeState(roundDrawState);
        }

        [PunRPC]
        public void RpcPointTransfer(EventMessages.PointTransferInfo message)
        {
            var transferState = new PointTransferState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                PlayerNames = message.PlayerNames,
                Points = message.Points,
                PointTransfers = message.PointTransfers,
                SmileSettlements = message.SmileSettlements,
                SettlementDetails = message.SettlementDetails,
                LaiziKongCounts = message.LaiziKongCounts,
                Reason = message.Reason,
                IsRoundEnd = message.IsRoundEnd
            };
            StateMachine.ChangeState(transferState);
        }

        [PunRPC]
        public void RpcGameEnd(EventMessages.GameEndInfo message) {
            var endState = new GameEndState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                PlayerNames = message.PlayerNames,
                Points = message.Points,
                Places = message.Places
            };
            StateMachine.ChangeState(endState);
        }

        public void ClientReady()
        {
            PhotonNetwork.RaiseEvent(
                EventMessages.ClientReadyEvent, CurrentRoundStatus.LocalPlayerIndex,
                EventMessages.ToMaster, EventMessages.SendReliable);
        }

        public void NextRound()
        {
            PhotonNetwork.RaiseEvent(
                EventMessages.NextRoundEvent, CurrentRoundStatus.LocalPlayerIndex,
                EventMessages.ToMaster, EventMessages.SendReliable);
        }

        public void OnDiscardTile(Tile tile, bool isLastDraw)
        {
            if (IsForbiddenSinglePlayerLaiziDiscard(tile)) return;
            int bonusTimeLeft = controller.TurnTimeController.StopCountDown();
            OnDiscardTile(tile, isLastDraw, bonusTimeLeft);
        }

        public void OnDiscardTile(Tile tile, bool isLastDraw, int bonusTimeLeft)
        {
            if (IsForbiddenSinglePlayerLaiziDiscard(tile)) return;
            if (SinglePlayerMode.Enabled)
            {
                if (localDiscardPending) return;
                if (ServerBehaviour.Instance == null
                    || ServerBehaviour.Instance.CurrentRoundStatus == null
                    || ServerBehaviour.Instance.CurrentRoundStatus.CurrentPlayerIndex
                        != CurrentRoundStatus.LocalPlayerIndex)
                {
                    Debug.LogWarning("Ignoring discard input because it is not the local player's turn.");
                    return;
                }
                localDiscardPending = true;
            }
            Debug.Log($"Sending request of discarding tile {tile}");
            var info = new EventMessages.DiscardTileInfo
            {
                PlayerIndex = CurrentRoundStatus.LocalPlayerIndex,
                IsRichiing = CurrentRoundStatus.IsRichiing,
                DiscardingLastDraw = isLastDraw,
                Tile = tile,
                BonusTurnTime = bonusTimeLeft
            };
            var localDiscardState = new LocalDiscardState {
                CurrentRoundStatus = CurrentRoundStatus,
                CurrentPlayerIndex = CurrentRoundStatus.LocalPlayerIndex,
                IsRichiing = CurrentRoundStatus.IsRichiing,
                DiscardingLastDraw = isLastDraw,
                Tile = tile
            };
            StateMachine.ChangeState(localDiscardState);
            if (SinglePlayerMode.Enabled)
            {
                // Offline play runs in the same process: update the local UI
                // first, then enter the rule state directly without Photon
                // event dispatch latency.
                ServerBehaviour.Instance.DiscardTile(
                    info.PlayerIndex, info.Tile, info.IsRichiing,
                    info.DiscardingLastDraw, info.BonusTurnTime, false);
                return;
            }
            PhotonNetwork.RaiseEvent(
                EventMessages.DiscardTileEvent, info,
                EventMessages.ToMaster, EventMessages.SendReliable);
        }

        /// <summary>客户端先拦截癞子普通出牌，避免点击或拖动后提前切换到本地弃牌状态。</summary>
        private bool IsForbiddenSinglePlayerLaiziDiscard(Tile tile)
        {
            if (!SinglePlayerMode.Enabled || CurrentRoundStatus == null) return false;
            var laizi = CurrentRoundStatus.MahjongSetData.LaiziTile;
            if (laizi.Rank <= 0 || !tile.EqualsIgnoreColor(laizi)) return false;
            Debug.LogWarning($"[规则追踪][禁止打癞子] 癞子={tile}不能普通打出，"
                + "请保留在手牌中或点击杠癞子按钮。");
            return true;
        }

        private void OnInTurnOperationTaken(InTurnOperation operation, int bonusTurnTime)
        {
            var info = new EventMessages.InTurnOperationInfo
            {
                PlayerIndex = CurrentRoundStatus.LocalPlayerIndex,
                Operation = operation,
                BonusTurnTime = bonusTurnTime
            };
            PhotonNetwork.RaiseEvent(
                EventMessages.InTurnOperationEvent, info,
                EventMessages.ToMaster, EventMessages.SendReliable);
        }

        public void OnSkipOutTurnOperation(int bonusTurnTime)
        {
            OnOutTurnOperationTaken(new OutTurnOperation { Type = OutTurnOperationType.Skip }, bonusTurnTime);
        }

        public void OnOutTurnOperationTaken(OutTurnOperation operation, int bonusTurnTime)
        {
            var info = new EventMessages.OutTurnOperationInfo
            {
                PlayerIndex = CurrentRoundStatus.LocalPlayerIndex,
                Operation = operation,
                BonusTurnTime = bonusTurnTime
            };
            PhotonNetwork.RaiseEvent(
                EventMessages.OutTurnOperationEvent, info,
                EventMessages.ToMaster, EventMessages.SendReliable);
        }

        public void OnInTurnSkipButtonClicked()
        {
            Debug.Log("In turn skip button clicked, hide buttons");
            controller.InTurnPanelManager.Close();
        }

        public void OnTsumoButtonClicked(InTurnOperation operation)
        {
            if (operation.Type != InTurnOperationType.Tsumo)
            {
                Debug.LogError($"Cannot send a operation with type {operation.Type} within OnTsumoButtonClicked method");
                return;
            }
            int bonusTimeLeft = controller.TurnTimeController.StopCountDown();
            Debug.Log($"Sending request of tsumo operation with bonus turn time {bonusTimeLeft}");
            OnInTurnOperationTaken(operation, bonusTimeLeft);
            controller.InTurnPanelManager.Close();
        }

        public void OnRichiButtonClicked(InTurnOperation operation)
        {
            if (operation.Type != InTurnOperationType.Richi)
            {
                Debug.LogError($"Cannot send a operation with type {operation.Type} within OnRichiButtonClicked method");
                return;
            }
            // show richi selection panel
            Debug.Log($"Showing richi selection panel, candidates: {string.Join(",", operation.RichiAvailableTiles)}");
            CurrentRoundStatus.SetRichiing(true);
            controller.HandPanelManager.SetCandidates(operation.RichiAvailableTiles);
        }

        public void OnInTurnKongButtonClicked(InTurnOperation[] operationOptions)
        {
            if (operationOptions == null || operationOptions.Length == 0)
            {
                Debug.LogError("The operations are null or empty in OnInTurnKongButtonClicked method, this should not happen.");
                return;
            }
            if (!operationOptions.All(op => op.Type == InTurnOperationType.Kong))
            {
                Debug.LogError("There are incompatible type within OnInTurnKongButtonClicked method");
                return;
            }
            if (operationOptions.Length == 1)
            {
                int bonusTimeLeft = controller.TurnTimeController.StopCountDown();
                Debug.Log($"Sending request of in turn kong operation with bonus turn time {bonusTimeLeft}");
                // Close the old panel before dispatch. Offline events execute
                // synchronously and may display the next operation panel
                // before this method returns.
                controller.InTurnPanelManager.Close();
                OnInTurnOperationTaken(operationOptions[0], bonusTimeLeft);
                return;
            }
            // show kong selection panel here
            controller.InTurnPanelManager.ShowBackButton();
            var meldOptions = operationOptions.Select(op => op.Meld);
            controller.MeldSelectionManager.SetMeldOptions(meldOptions.ToArray(), meld =>
            {
                int bonusTimeLeft = controller.TurnTimeController.StopCountDown();
                Debug.Log($"Sending request of in turn kong operation with bonus turn time {bonusTimeLeft}");
                controller.InTurnPanelManager.Close();
                OnInTurnOperationTaken(new InTurnOperation
                {
                    Type = InTurnOperationType.Kong,
                    Meld = meld
                }, bonusTimeLeft);
                controller.MeldSelectionManager.Close();
            });
        }

        public void OnInTurnButtonClicked(InTurnOperation operation)
        {
            Debug.Log($"Requesting to proceed operation: {operation}");
            int bonusTimeLeft = controller.TurnTimeController.StopCountDown();
            // In offline mode RaiseEvent is synchronous. Close only the old
            // panel first, otherwise this line would close the newly-created
            // panel after a laizi-kong replacement draw.
            controller.InTurnPanelManager.Close();
            OnInTurnOperationTaken(operation, bonusTimeLeft);
        }

        public void OnInTurnBackButtonClicked(InTurnOperation[] operations)
        {
            controller.InTurnPanelManager.SetOperations(operations);
            controller.MeldSelectionManager.Close();
            controller.HandPanelManager.RemoveCandidates();
            CurrentRoundStatus.SetRichiing(false);
        }

        public void OnOutTurnBackButtonClicked(OutTurnOperation[] operations)
        {
            controller.OutTurnPanelManager.SetOperations(operations);
            controller.MeldSelectionManager.Close();
        }

        public void OnOutTurnButtonClicked(OutTurnOperation operation)
        {
            int bonusTimeLeft = controller.TurnTimeController.StopCountDown();
            Debug.Log($"Sending request of operation {operation} with bonus turn time {bonusTimeLeft}");
            OnOutTurnOperationTaken(operation, bonusTimeLeft);
            controller.OutTurnPanelManager.Close();
        }

        public void OnChowButtonClicked(OutTurnOperation[] operationOptions, OutTurnOperation[] originalOperations)
        {
            if (operationOptions == null || operationOptions.Length == 0)
            {
                Debug.LogError("The operations are null or empty in OnChowButtonClicked method, this should not happen.");
                return;
            }
            if (!operationOptions.All(op => op.Type == OutTurnOperationType.Chow))
            {
                Debug.LogError("There are incompatible type within OnChowButtonClicked method");
                return;
            }
            if (operationOptions.Length == 1)
            {
                int bonusTimeLeft = controller.TurnTimeController.StopCountDown();
                Debug.Log($"Sending request of chow operation with bonus turn time {bonusTimeLeft}");
                OnOutTurnOperationTaken(operationOptions[0], bonusTimeLeft);
                controller.OutTurnPanelManager.Close();
                return;
            }
            // chow selection logic here
            controller.OutTurnPanelManager.ShowBackButton();
            var meldOptions = operationOptions.Select(op => op.Meld);
            controller.MeldSelectionManager.SetMeldOptions(meldOptions.ToArray(), meld =>
            {
                int bonusTimeLeft = controller.TurnTimeController.StopCountDown();
                Debug.Log($"Sending request of in turn kong operation with bonus turn time {bonusTimeLeft}");
                OnOutTurnOperationTaken(new OutTurnOperation
                {
                    Type = OutTurnOperationType.Chow,
                    Meld = meld
                }, bonusTimeLeft);
                controller.InTurnPanelManager.Close();
                controller.MeldSelectionManager.Close();
            });
        }

        public void OnPongButtonClicked(OutTurnOperation[] operationOptions, OutTurnOperation[] originalOperations)
        {
            if (operationOptions == null || operationOptions.Length == 0)
            {
                Debug.LogError("The operations are null or empty in OnPongButtonClicked method, this should not happen.");
                return;
            }
            if (!operationOptions.All(op => op.Type == OutTurnOperationType.Pong))
            {
                Debug.LogError("There are incompatible type within OnPongButtonClicked method");
                return;
            }
            if (operationOptions.Length == 1)
            {
                int bonusTimeLeft = controller.TurnTimeController.StopCountDown();
                Debug.Log($"Sending request of pong operation with bonus turn time {bonusTimeLeft}");
                OnOutTurnOperationTaken(operationOptions[0], bonusTimeLeft);
                controller.OutTurnPanelManager.Close();
                return;
            }
            // pong selection logic here
            controller.OutTurnPanelManager.ShowBackButton();
            var meldOptions = operationOptions.Select(op => op.Meld);
            controller.MeldSelectionManager.SetMeldOptions(meldOptions.ToArray(), meld =>
            {
                int bonusTimeLeft = controller.TurnTimeController.StopCountDown();
                Debug.Log($"Sending request of pong operation with bonus turn time {bonusTimeLeft}");
                OnOutTurnOperationTaken(new OutTurnOperation
                {
                    Type = OutTurnOperationType.Pong,
                    Meld = meld
                }, bonusTimeLeft);
                controller.InTurnPanelManager.Close();
                controller.MeldSelectionManager.Close();
            });
        }

        public override void OnLeftRoom()
        {
            // todo
        }

        public override void OnLeftLobby()
        {
            // todo
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            // todo
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            // todo
        }
    }
}

using System.Linq;
using GamePlay.Server.Model;
using Mahjong.Model;
using UnityEngine;
using UnityEngine.Assertions;
using GamePlay.Client.View;
using GamePlay.SinglePlayer;

namespace GamePlay.Client.Controller.GameState
{
    public class PlayerOperationPerformState : ClientState
    {
        public int PlayerIndex;
        public int OperationPlayerIndex;
        public OutTurnOperation Operation;
        public PlayerHandData HandData;
        public int BonusTurnTime;
        public RiverData[] Rivers;
        public InTurnOperation[] InTurnOperations;
        public MahjongSetData MahjongSetData;

        public override void OnClientStateEnter()
        {
			switch (Operation.Type)
			{
				case OutTurnOperationType.Chow: GameSoundManager.Chow(OperationPlayerIndex); break;
				case OutTurnOperationType.Pong: GameSoundManager.Pong(OperationPlayerIndex); break;
				case OutTurnOperationType.Kong: GameSoundManager.KongOrSmile(OperationPlayerIndex); break;
				case OutTurnOperationType.Rong: GameSoundManager.Hu(OperationPlayerIndex); break;
			}
            if (CurrentRoundStatus.LocalPlayerIndex == OperationPlayerIndex)
                HandleLocalPlayerOperation();
            else
                HandleOtherPlayerOperation();
        }

        private void HandleLocalPlayerOperation()
        {
            // update local data
            CurrentRoundStatus.SetCurrentPlaceIndex(OperationPlayerIndex);
            int placeIndex = CurrentRoundStatus.CurrentPlaceIndex;
            Assert.IsTrue(placeIndex == 0);
            SetRoundStatusData();
            // Status notifications may rebuild the hand and apply discard
            // restrictions. Unlock after that refresh so the player can
            // immediately click or drag a legal tile after pong.
            controller.HandPanelManager.UnlockTiles();
            // update ui elements
            controller.TableTilesManager.SetMelds(placeIndex, HandData.OpenMelds);
            // if not kong, start timer
            if (Operation.Type != OutTurnOperationType.Kong)
            {
                controller.TurnTimeController.StartCountDown(CurrentRoundStatus.GameSetting.BaseTurnTime, BonusTurnTime, () =>
                {
                    Debug.Log("Time out, automatically discard rightmost tile");
                    CurrentRoundStatus.SetRichiing(false);
                    var laizi = MahjongSetData.LaiziTile;
                    var tile = SinglePlayerMode.Enabled && laizi.Rank > 0
                        ? HandData.HandTiles.LastOrDefault(
                            candidate => !candidate.EqualsIgnoreColor(laizi))
                        : HandData.HandTiles[HandData.HandTiles.Length - 1];
                    if (tile.Rank > 0)
                        ClientBehaviour.Instance.OnDiscardTile(tile, false, 0);
                });
            }
            if (InTurnOperations != null
                && InTurnOperations.Any(op => op.Type == InTurnOperationType.Bei))
                controller.InTurnPanelManager.SetOperations(InTurnOperations);
        }

        private void EnableAllTiles()
        {
            CurrentRoundStatus.SetForbiddenTiles(null);
        }

        private void HandleOtherPlayerOperation()
        {
            controller.HandPanelManager.LockTiles();
            // update local data
            CurrentRoundStatus.SetCurrentPlaceIndex(OperationPlayerIndex);
            int placeIndex = CurrentRoundStatus.CurrentPlaceIndex;
            Debug.Log($"Hand tile count of player {OperationPlayerIndex}: {HandData.HandTiles.Length}");
            CurrentRoundStatus.SetHandTiles(placeIndex, HandData.HandTiles.Length);
            CurrentRoundStatus.SetMahjongSetData(MahjongSetData);
            SetRiverData();
            // update ui elements
            controller.TableTilesManager.SetMelds(placeIndex, HandData.OpenMelds);
        }

        private void SetRoundStatusData()
        {
            CurrentRoundStatus.CheckLocalHandTiles(HandData.HandTiles);
            CurrentRoundStatus.SetMahjongSetData(MahjongSetData);
            SetRiverData();
            CurrentRoundStatus.CalculatePossibleWaitingTiles();
            var forbiddenTiles = Operation.ForbiddenTiles == null
                ? new System.Collections.Generic.List<Tile>()
                : Operation.ForbiddenTiles.ToList();
            if (SinglePlayerMode.Enabled && MahjongSetData.LaiziTile.Rank > 0
                && forbiddenTiles.All(tile =>
                    !tile.EqualsIgnoreColor(MahjongSetData.LaiziTile)))
                forbiddenTiles.Add(MahjongSetData.LaiziTile);
            CurrentRoundStatus.SetForbiddenTiles(forbiddenTiles);
        }

        private void SetRiverData()
        {
            for (int playerIndex = 0; playerIndex < Rivers.Length; playerIndex++)
            {
                int placeIndex = CurrentRoundStatus.GetPlaceIndex(playerIndex);
                CurrentRoundStatus.SetRiverData(placeIndex, Rivers[playerIndex]);
            }
        }

        public override void OnClientStateExit()
        {
            EnableAllTiles();
        }

        public override void OnStateUpdate()
        {
        }
    }
}

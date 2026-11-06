using System.Collections;
using GamePlay.Client.View;
using GamePlay.Server.Model;
using Mahjong.Model;
using UnityEngine;

namespace GamePlay.Client.Controller.GameState
{
    public class PlayerDiscardOperationState : ClientState
    {
        public int CurrentPlayerIndex;
        public bool IsRichiing;
        public bool DiscardingLastDraw;
        public Tile Tile;
        public int BonusTurnTime;
        public bool Zhenting;
        public OutTurnOperation[] Operations;
        public Tile[] HandTiles;
        public RiverData[] Rivers;
        public override void OnClientStateEnter()
        {
			GameSoundManager.Discard(Tile, CurrentPlayerIndex);
            if (CurrentPlayerIndex == CurrentRoundStatus.LocalPlayerIndex)
                HandleLocalDiscard();
            else
                HandleOtherDiscard();
        }

        private void HandleLocalDiscard()
        {
            // if local, check data accuracy, including hand tiles and river tiles
            // check hand tiles
            CurrentRoundStatus.CheckLocalHandTiles(HandTiles);
            // set river tiles
            for (int playerIndex = 0; playerIndex < Rivers.Length; playerIndex++)
            {
                int placeIndex = CurrentRoundStatus.GetPlaceIndex(playerIndex);
                CurrentRoundStatus.SetRiverData(placeIndex, Rivers[playerIndex]);
            }
            // set zhenting status
            CurrentRoundStatus.SetZhenting(Zhenting);
            // skipping this out turn operation, since the current turn is the turn of local player.
            ClientBehaviour.Instance.OnSkipOutTurnOperation(BonusTurnTime);
        }

        private void HandleOtherDiscard()
        {
            CurrentRoundStatus.SetCurrentPlaceIndex(CurrentPlayerIndex);
            int placeIndex = CurrentRoundStatus.CurrentPlaceIndex;
            // update hand tiles
            CurrentRoundStatus.CheckLocalHandTiles(HandTiles);
            CurrentRoundStatus.SetZhenting(Zhenting);
            if (IsRichiing)
                controller.ShowEffect(placeIndex, PlayerEffectManager.Type.Richi);
            controller.StartCoroutine(UpdateHandData(placeIndex, DiscardingLastDraw, Tile, Rivers));
        }

        private IEnumerator UpdateHandData(int currentPlaceIndex, bool discardingLastDraw, Tile tile, RiverData[] rivers)
        {
            // A discard made immediately after a pong has no drawn tile to
            // replace the discarded concealed tile.  The operation snapshot
            // is sent before that mandatory discard, so reduce the cached
            // concealed count here.  On an ordinary draw/discard turn the
            // drawn tile either gets discarded or moves into the hand, and
            // the concealed count therefore stays unchanged.
            var hadDrawnTile = CurrentRoundStatus.GetLastDraw(currentPlaceIndex).HasValue;
            if (!hadDrawnTile)
            {
                var concealedCount = CurrentRoundStatus.GetTileCount(currentPlaceIndex);
                if (concealedCount > 0)
                    CurrentRoundStatus.SetHandTiles(currentPlaceIndex, concealedCount - 1);
            }
            CurrentRoundStatus.ClearLastDraws();
            controller.TableTilesManager.DiscardTile(currentPlaceIndex, discardingLastDraw);
            Debug.Log($"Playing player (place: {currentPlaceIndex}) discarding animation");
            yield return new WaitForEndOfFrame();
            for (int playerIndex = 0; playerIndex < rivers.Length; playerIndex++)
            {
                int placeIndex = CurrentRoundStatus.GetPlaceIndex(playerIndex);
                CurrentRoundStatus.SetRiverData(placeIndex, rivers[playerIndex]);
            }
            var panelShown = controller.ShowOutTurnPanels(Operations, BonusTurnTime);
            if (panelShown)
                controller.TableTilesManager.ShineLastTile(currentPlaceIndex);
        }

        public override void OnClientStateExit()
        {
            controller.OutTurnPanelManager.Close();
            controller.TableTilesManager.ShineOff();
        }

        public override void OnStateUpdate()
        {
        }
    }
}

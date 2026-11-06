using System.Linq;
using GamePlay.Server.Model;
using Mahjong.Model;
using UnityEngine;
using UnityEngine.Assertions;
using GamePlay.Client.View;
using GamePlay.SinglePlayer;

namespace GamePlay.Client.Controller.GameState
{
    public class PlayerDrawState : ClientState
    {
        public int PlayerIndex;
        public Tile Tile;
        public int BonusTurnTime;
        public bool Zhenting;
        public MahjongSetData MahjongSetData;
        public InTurnOperation[] Operations;
        public Tile[] HandTiles;
        public RiverData[] Rivers;

        public override void OnClientStateEnter()
        {
            GameSoundManager.Draw();
            if (CurrentRoundStatus.LocalPlayerIndex == PlayerIndex)
                HandleLocalPlayerDraw();
            else
                HandleOtherPlayerDraw();
        }

        private void HandleLocalPlayerDraw()
        {
            controller.HandPanelManager.UnlockTiles();
            CurrentRoundStatus.SetCurrentPlaceIndex(PlayerIndex);
            var placeIndex = CurrentRoundStatus.CurrentPlaceIndex;
            Assert.IsTrue(placeIndex == 0);
            if (Rivers != null && Rivers.Length > 0)
                CurrentRoundStatus.SetRiverData(placeIndex, Rivers[PlayerIndex]);
            if (HandTiles != null)
            {
                // A few offline state transitions can include the just-drawn
                // tile in the hand snapshot as well as DrawTile.Tile. Remove
                // that one snapshot copy so it is rendered only in the drawn
                // slot and not twice.
                var snapshot = new System.Collections.Generic.List<Tile>(HandTiles);
                if (Tile.Rank > 0 && snapshot.Count % 3 == 2)
                {
                    var duplicate = snapshot.FindIndex(t => t.EqualsConsiderColor(Tile));
                    if (duplicate >= 0) snapshot.RemoveAt(duplicate);
                }
                CurrentRoundStatus.CheckLocalHandTiles(snapshot);
            }
            CurrentRoundStatus.DrawTile(placeIndex, Tile);
            CurrentRoundStatus.SetMahjongSetData(MahjongSetData);
            // 癞子可以留在手牌或点击“杠”处理，但点击、拖动时不能作为普通牌打出。
            CurrentRoundStatus.SetForbiddenTiles(SinglePlayerMode.Enabled
                && MahjongSetData.LaiziTile.Rank > 0
                    ? new[] { MahjongSetData.LaiziTile }
                    : null);
            CurrentRoundStatus.SetZhenting(Zhenting);
            EnsureLaiziKongOperation();
            controller.ShowInTurnPanels(Operations, BonusTurnTime);
        }

        private void EnsureLaiziKongOperation()
        {
            if (!SinglePlayerMode.Enabled || MahjongSetData.LaiziTile.Rank <= 0)
                return;

            var drawnLaizi = Tile.Rank > 0 && Tile.EqualsIgnoreColor(MahjongSetData.LaiziTile);
            var heldLaizi = HandTiles == null
                ? default(Tile)
                : HandTiles.FirstOrDefault(tile => tile.EqualsIgnoreColor(MahjongSetData.LaiziTile));
            if (!drawnLaizi && heldLaizi.Rank <= 0)
                return;

            var operations = Operations == null
                ? new System.Collections.Generic.List<InTurnOperation>()
                : Operations.ToList();
            // 同时可杠四张相同牌时，优先普通杠，客户端不再补出“杠癞子”按钮。
            // 四张癞子本身组成的杠仍然保留癞子杠选项。
            var hasNormalKong = operations.Any(operation =>
                operation.Type == InTurnOperationType.Kong
                && operation.Meld.Tiles != null
                && operation.Meld.Tiles.Length > 0
                && !operation.Meld.Tiles.All(tile =>
                    tile.EqualsIgnoreColor(MahjongSetData.LaiziTile)));
            if (hasNormalKong) return;
            if (operations.All(operation => operation.Type != InTurnOperationType.Discard))
                operations.Insert(0, new InTurnOperation { Type = InTurnOperationType.Discard });
            if (operations.All(operation => operation.Type != InTurnOperationType.Bei))
            {
                operations.Add(new InTurnOperation
                {
                    Type = InTurnOperationType.Bei,
                    Tile = drawnLaizi ? Tile : heldLaizi
                });
                Debug.Log($"[规则追踪][客户端补齐癞子杠提示] 摸牌={Tile}，癞子={MahjongSetData.LaiziTile}");
            }
            Operations = operations.ToArray();
        }

        private void HandleOtherPlayerDraw()
        {
            controller.HandPanelManager.LockTiles();
            CurrentRoundStatus.SetCurrentPlaceIndex(PlayerIndex);
            int placeIndex = CurrentRoundStatus.CurrentPlaceIndex;
            if (Rivers != null && PlayerIndex < Rivers.Length)
                CurrentRoundStatus.SetRiverData(placeIndex, Rivers[PlayerIndex]);
            CurrentRoundStatus.DrawTile(placeIndex);
            CurrentRoundStatus.SetMahjongSetData(MahjongSetData);
            Debug.Log($"LastDraws: {string.Join(",", CurrentRoundStatus.LastDraws)}");
        }

        public override void OnClientStateExit()
        {
            CurrentRoundStatus.ClearPossibleWaitingTiles();
            controller.InTurnPanelManager.Close();
        }

        public override void OnStateUpdate()
        {
        }
    }
}

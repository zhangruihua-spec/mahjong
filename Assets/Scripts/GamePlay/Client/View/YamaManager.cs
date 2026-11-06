using System.Collections.Generic;
using Common.Interfaces;
using GamePlay.Client.Model;
using Mahjong.Logic;
using Mahjong.Model;
using UnityEngine;
using Utils;
using GamePlay.SinglePlayer;

namespace GamePlay.Client.View
{
    public class YamaManager : MonoBehaviour, IObserver<ClientRoundStatus>
    {
        public static readonly IDictionary<int, int> IndexToYama = new Dictionary<int, int> {
            {0, 0}, {1, 3}, {2, 2}, {3, 1}
        };
        private static readonly IDictionary<GamePlayers, int[]> TileCountOnWall = new Dictionary<GamePlayers, int[]> {
            {GamePlayers.Four, new int[] {27, 27, 27, 27}},
            {GamePlayers.Three, new int[] {28, 26, 28, 26}},
            {GamePlayers.Two, new int[] {20, 20, 20, 20}}
        };

        [SerializeField] private Transform[] Walls;
        private Transform standaloneLaiziIndicator;
        private bool initialDealAnimating;
        private ClientRoundStatus initialDealStatus;
        private int initialDealOpenYamaIndex;
        private int initialDealDrawn;

        private void UpdateTiles(ClientRoundStatus status)
        {
            HideUnusedTiles(status.GameSetting.GamePlayers);
            var yamaIndex = GetYamaIndex(status.Dice, status.OyaPlayerIndex, status.Places);
            UpdateYama(yamaIndex, status);
        }

        private void HideUnusedTiles(GamePlayers gamePlayers)
        {
            for (int yamaIndex = 0; yamaIndex < Walls.Length; yamaIndex++)
            {
                int count = GetYamaTotalTiles(yamaIndex, gamePlayers);
                for (int i = 0; i < Walls[yamaIndex].childCount; i++)
                {
                    var t = Walls[yamaIndex].GetChild(i);
                    t.gameObject.SetActive(i < count);
                    if (i < count) TurnTileFaceDown(t);
                }
            }
        }

        private static int GetYamaIndex(int dice, int oya, int[] places)
        {
            if (dice <= 0 || oya < 0 || oya >= 4 || places == null || places.Length != 4)
                throw new System.ArgumentException();
            var openSideOffset = (dice - 1) % 4;
            var openIndex = (oya + openSideOffset) % 4;
            var index = System.Array.FindIndex(places, i => i == openIndex);
            return IndexToYama[index];
        }

        private static int GetYamaTotalTiles(int yamaIndex, GamePlayers gamePlayers)
        {
            return TileCountOnWall[gamePlayers][yamaIndex];
        }

        private void UpdateYama(int openYamaIndex, ClientRoundStatus status)
        {
            var dice = status.Dice;
            var lingShangTilesCount = status.GameSetting.LingshangTilesCount;
            var setData = status.MahjongSetData;
            // drawn tiles
            for (int i = 0; i < setData.TilesDrawn; i++)
            {
                var t = GetTileAt(openYamaIndex, dice * 2 + i, status);
                DrawTile(t);
            }
            if (SinglePlayerMode.Enabled)
            {
                var drawStartIndex = dice * 2;
                // Every kong draws from the live wall tail. Unlike the old
                // riichi dead wall this has no four-tile visual limit.  The
                // tail must be measured backwards from the tile immediately
                // before the locked indicator, using the same circular wall
                // origin as normal draws.  Otherwise visible tiles remain on
                // the table after the logical wall reaches zero.
                for (int i = 0; i < setData.LingShangDrawn; i++)
                    DrawTile(GetTileAt(openYamaIndex, drawStartIndex - 2 - i, status));
                // The final locked tile is the laizi indicator. Keep it face
                // up on the last stack; it is not part of the drawable wall.
                if (setData.LaiziIndicator.Rank > 0)
                {
                    // Always use the physical last tile of the whole wall.
                    // Do not rotate this location with the dice/open side: the
                    // single-player rule displays the indicator on the last
                    // stack of the complete four-sided wall.
                    // Show it separately in the upper-left corner for now.
                    var indicator = GetTileAt(openYamaIndex, drawStartIndex - 1, status);
                    // Keep the wall tile in place and render a single detached
                    // copy in the upper-left corner, avoiding a visible stack
                    // of neighbouring wall tiles at that location.
                    if (standaloneLaiziIndicator == null)
                    {
                        standaloneLaiziIndicator = Instantiate(indicator, indicator.parent);
                        standaloneLaiziIndicator.name = "LaiziIndicatorStandalone";
						// The copied tile keeps the wall tile's small world scale.
						// Enlarge the detached indicator so its face is readable in the HUD.
						standaloneLaiziIndicator.localScale = indicator.localScale * 1.8f;
                    }
                    standaloneLaiziIndicator.gameObject.SetActive(true);
					standaloneLaiziIndicator.localScale = indicator.localScale * 1.8f;
                    TurnTileFaceUp(standaloneLaiziIndicator, setData.LaiziIndicator);
                    indicator.gameObject.SetActive(false);
                    if (Camera.main != null)
                    {
                        var depth = Mathf.Abs(Camera.main.transform.position.y);
                        standaloneLaiziIndicator.position = Camera.main.ScreenToWorldPoint(
                            new Vector3(70f, Screen.height - 70f, depth));
                    }
                    // At the standalone screen position the wall rotation
                    // would show the tile back; use the normal face-up
                    // orientation used by hand tiles instead.
                    // Set world rotation because the tile remains parented to
                    // its wall (whose rotation differs from the hand panel).
                    // Copy the actual local player's hand-tile world rotation;
                    // this guarantees the indicator uses exactly the same
                    // orientation as a readable tile in the player's hand.
                    var tableTiles = FindObjectOfType<TableTilesManager>();
                    var river = tableTiles != null && tableTiles.RiverManagers != null
                        && tableTiles.RiverManagers.Length > 0
                        ? tableTiles.RiverManagers[0]
                        : null;
                    if (river != null && river.transform.childCount > 0)
                        standaloneLaiziIndicator.rotation = river.transform.GetChild(0).rotation;
                    else
                        standaloneLaiziIndicator.rotation = MahjongConstants.FaceUpOnWall;
                }
                return;
            }
            // legacy multiplayer lingshang tiles
            for (int i = 0; i < setData.LingShangDrawn; i++)
            {
                var s = dice - i / 2;
                var t = GetTileAt(openYamaIndex, (s - 1) * 2 + i % 2, status);
                DrawTile(t);
            }
            // dora tiles
            var length = setData.DoraIndicators == null ? 0 : setData.DoraIndicators.Length;
            for (int i = 0; i < length; i++)
            {
                var s = dice - lingShangTilesCount / 2 - i;
                var t = GetTileAt(openYamaIndex, (s - 1) * 2, status);
                TurnTileFaceUp(t, setData.DoraIndicators[i]);
            }
        }

        private Transform GetTileAt(int openYamaIndex, int index, ClientRoundStatus status)
        {
            var totalTiles = status.MahjongSetData.TotalTiles;
            if (index < 0) index += totalTiles;
            int yamaIndex = openYamaIndex;
            var gamePlayers = status.GameSetting.GamePlayers;
            while (index >= GetYamaTotalTiles(yamaIndex, gamePlayers))
            {
                index -= GetYamaTotalTiles(yamaIndex, gamePlayers);
                yamaIndex++;
                if (yamaIndex >= 4) yamaIndex -= 4;
            }
            return Walls[yamaIndex].GetChild(index);
        }

        public void ResetAllTiles()
        {
            foreach (var wall in Walls)
            {
                wall.TraversalChildren(t =>
                {
                    t.gameObject.SetActive(true);
                    TurnTileFaceDown(t);
                });
            }
        }

        public void BeginInitialDealAnimation(ClientRoundStatus status)
        {
            initialDealAnimating = true;
            initialDealStatus = status;
            initialDealDrawn = 0;
            ResetAllTiles();
            HideUnusedTiles(status.GameSetting.GamePlayers);
            initialDealOpenYamaIndex = GetYamaIndex(status.Dice, status.OyaPlayerIndex, status.Places);
            if (standaloneLaiziIndicator != null)
                standaloneLaiziIndicator.gameObject.SetActive(false);
        }

        public void SetInitialDealDrawn(int drawnCount)
        {
            if (!initialDealAnimating || initialDealStatus == null) return;
            var target = Mathf.Clamp(drawnCount, initialDealDrawn,
                initialDealStatus.MahjongSetData.TilesDrawn);
            for (int i = initialDealDrawn; i < target; i++)
            {
                var tile = GetTileAt(initialDealOpenYamaIndex,
                    initialDealStatus.Dice * 2 + i, initialDealStatus);
                DrawTile(tile);
            }
            initialDealDrawn = target;
        }

        public void EndInitialDealAnimation(ClientRoundStatus status)
        {
            initialDealAnimating = false;
            initialDealStatus = null;
            UpdateTiles(status);
        }

        private static void DrawTile(Transform t)
        {
            t.gameObject.SetActive(false);
        }

        private static void TurnTileFaceUp(Transform t, Tile tile)
        {
            t.gameObject.SetActive(true);
            t.localRotation = MahjongConstants.FaceUpOnWall;
            var tileInstance = t.GetComponent<TileInstance>();
            tileInstance.SetTile(tile);
        }

        private static void TurnTileFaceDown(Transform t)
        {
            t.localRotation = MahjongConstants.FaceDownOnWall;
        }

        public void UpdateStatus(ClientRoundStatus subject)
        {
            if (subject == null || subject.Dice == 0 || subject.OyaPlayerIndex < 0) return;
            if (initialDealAnimating)
            {
                initialDealStatus = subject;
                return;
            }
            UpdateTiles(subject);
        }
    }
}

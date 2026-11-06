using System;
using System.Collections.Generic;
using Mahjong.Model.Exceptions;
using UnityEngine;
using Utils;
using GamePlay.SinglePlayer;


namespace Mahjong.Model
{
    [Serializable]
    public class MahjongSet
    {
        private GameSetting setting;
        private List<Tile> allTiles;
        private int tilesDrawn = 0;
        private int lingShangDrawn = 0;
        private int doraTurned = 0;
        private int tailDrawn = 0;
        private Tile laiziIndicator;
        private Tile laiziTile;

        public MahjongSet(GameSetting setting, IEnumerable<Tile> tiles)
        {
            this.setting = setting;
            allTiles = new List<Tile>(tiles);
            Debug.Log($"In current settings, total count of all tiles is {allTiles.Count}");
            for (int i = 0; i < setting.redTiles.Length; i++)
            {
                var red = setting.redTiles[i];
                var index = allTiles.FindIndex(t => t.EqualsIgnoreColor(red) && !t.IsRed);
                if (index < 0)
                {
                    Debug.LogWarning($"Not enough tile of {red}");
                    continue;
                }
                allTiles[index] = new Tile(red.Suit, red.Rank, true);
            }
        }

        /// <summary>
        /// Shuffle and reset the mahjong set to its original state, then turn dora tiles of the initial count and return an array containing the initial doras.
        /// </summary>
        /// <returns>Array of initial dora tiles</returns>
        public Tile[] Reset()
        {
            allTiles.Shuffle();
            tilesDrawn = 0;
            lingShangDrawn = 0;
            doraTurned = 0;
            tailDrawn = 0;
            if (SinglePlayerMode.Enabled)
            {
                // The first tile in the shuffled wall is turned face-up as the
                // laizi indicator after the initial deal. It remains locked on
                // the wall and is never drawn during normal play.
                laiziIndicator = allTiles[0];
                laiziTile = new Tile(laiziIndicator.Suit, laiziIndicator.Rank == 9 ? 1 : laiziIndicator.Rank + 1);
                Debug.Log($"Laizi indicator: {laiziIndicator}, laizi: {laiziTile}");
            }
            var doraList = new List<Tile>();
            for (int i = 0; !SinglePlayerMode.Enabled && i < setting.InitialDora; i++)
            {
                doraList.Add(TurnDora());
            }
            return doraList.ToArray();
        }

        /// <summary>
        /// Draw a new tile on the wall, throw a exception when there are none
        /// </summary>
        /// <returns>The next tile</returns>
        public Tile DrawTile()
        {
            if (TilesRemain <= 0) throw new NoMoreTilesException("There are no more tiles to drawn!");
            return allTiles[tilesDrawn++ + (SinglePlayerMode.Enabled ? 1 : 0)];
        }

        /// <summary>
        /// Peek the next tile on the wall without drawing it, throw a exception when there are none
        /// </summary>
        /// <returns>The next tile</returns>
        public Tile PeekTile()
        {
            if (TilesRemain <= 0) throw new NoMoreTilesException("There are no more tiles to drawn!");
            return allTiles[tilesDrawn + (SinglePlayerMode.Enabled ? 1 : 0)];
        }

        /// <summary>
        /// Draw a lingshang tile from the other side of the wall, throw a exception when there are none
        /// </summary>
        /// <returns>The lingshang tile</returns>
        public Tile DrawLingShang()
        {
            if (!SinglePlayerMode.Enabled && LingShangDrawn >= setting.LingshangTilesCount)
                throw new NoMoreTilesException("There are no more LingShang tiles to drawn!");
            if (SinglePlayerMode.Enabled)
            {
                if (TilesRemain <= 0) throw new NoMoreTilesException("There are no more tail tiles to draw!");
                lingShangDrawn++;
                // The indicator is locked at index 0.  Tail replacement
                // draws therefore start at the actual final tile.  The old
                // "-2" was inherited from the riichi dead wall and left one
                // drawable tile stranded while allowing the front and tail
                // cursors to meet at the wrong position.
                return allTiles[allTiles.Count - 1 - tailDrawn++];
            }
            lingShangDrawn++;
            return allTiles[allTiles.Count - lingShangDrawn];
        }

        /// <summary>
        /// Turn a new dora indicator, throw a exception when there are none
        /// </summary>
        /// <returns>The new dora indicator</returns>
        public Tile TurnDora()
        {
            if (SinglePlayerMode.Enabled) return default(Tile);
            if (doraTurned >= setting.MaxDora) throw new NoMoreTilesException("There are no more dora indicators to turn.");
            var firstDora = setting.LingshangTilesCount; // index from tail of list
            var currentDora = firstDora + DoraTurned * 2;
            doraTurned++;
            return allTiles[allTiles.Count - 1 - currentDora];
        }

        /// <summary>
        /// Returns current turned dora indicators
        /// </summary>
        /// <value>Array of dora indicators</value>
        public Tile[] DoraIndicators
        {
            get
            {
                if (SinglePlayerMode.Enabled) return new Tile[0];
                var firstDora = setting.LingshangTilesCount;
                var doraTiles = new Tile[DoraTurned];
                for (int i = 0; i < doraTiles.Length; i++)
                {
                    var currentDora = firstDora + i * 2;
                    doraTiles[i] = allTiles[allTiles.Count - 1 - currentDora];
                }
                return doraTiles;
            }
        }

        /// <summary>
        /// Returns current turned ura-dora indicators
        /// </summary>
        /// <value>Array of ura-dora indicators</value>
        public Tile[] UraDoraIndicators
        {
            get
            {
                if (SinglePlayerMode.Enabled) return new Tile[0];
                var firstUraDora = setting.LingshangTilesCount + 1;
                var uraDoraTiles = new Tile[DoraTurned];
                for (int i = 0; i < uraDoraTiles.Length; i++)
                {
                    var currentUraDora = firstUraDora + i * 2;
                    uraDoraTiles[i] = allTiles[allTiles.Count - 1 - currentUraDora];
                }
                return uraDoraTiles;
            }
        }

        private void SetTiles(IList<Tile> tiles)
        {
            Debug.Log("Cheating...");
            for (int i = 0; i < tiles.Count; i++)
            {
                allTiles[tilesDrawn + i + (SinglePlayerMode.Enabled ? 1 : 0)] = tiles[i];
            }
        }

        private void SetTilesReverse(IList<Tile> tiles)
        {
            Debug.Log("Cheating...");
            for (int i = 0; i < tiles.Count; i++)
            {
                allTiles[allTiles.Count - lingShangDrawn - 1 - i] = tiles[i];
            }
        }

        public IList<Tile> AllTiles => allTiles.AsReadOnly();
        public int TilesDrawn => tilesDrawn;
        public int DoraTurned => doraTurned;
        public int LingShangDrawn => lingShangDrawn;
        public int TilesRemain => allTiles.Count - tilesDrawn - tailDrawn - (SinglePlayerMode.Enabled ? 1 : 0);
        public Tile LaiziIndicator => laiziIndicator;
        public Tile LaiziTile => laiziTile;
        // The face-up indicator is also the tile immediately preceding the
        // laizi. For example, indicator 9-sou makes 1-sou the laizi, so
        // 9-sou is the special preceding tile for this hand.
        public Tile LaiziPreviousTile => laiziIndicator;

        /// <summary>
        /// 返回当前仍可从牌墙正面或尾部摸到的各牌数量；不包含锁定的癞子指示牌和已经摸走的牌。
        /// 仅供单机出牌提示统计实际剩余胡牌张数。
        /// </summary>
        public int[] DrawableTileCounts
        {
            get
            {
                var counts = new int[34];
                if (!SinglePlayerMode.Enabled) return counts;
                var firstDrawableIndex = tilesDrawn + 1;
                var lastDrawableIndex = allTiles.Count - 1 - tailDrawn;
                for (int index = firstDrawableIndex; index <= lastDrawableIndex; index++)
                {
                    var tileIndex = Tile.GetIndex(allTiles[index]);
                    if (tileIndex >= 0 && tileIndex < counts.Length)
                        counts[tileIndex]++;
                }
                return counts;
            }
        }

        public MahjongSetData Data
        {
            get
            {
                return new MahjongSetData
                {
                    TilesDrawn = TilesDrawn,
                    DoraTurned = DoraTurned,
                    LingShangDrawn = LingShangDrawn,
                    TilesRemain = TilesRemain,
                    LaiziIndicator = LaiziIndicator,
                    LaiziTile = LaiziTile,
                    DoraIndicators = DoraIndicators,
                    TotalTiles = allTiles.Count
                };
            }
        }
    }

    [Serializable]
    public struct MahjongSetData
    {
        public int TilesDrawn;
        public int DoraTurned;
        public int LingShangDrawn;
        public int TilesRemain;
        public Tile LaiziIndicator;
        public Tile LaiziTile;
        public Tile[] DoraIndicators;
        public int TotalTiles;

        public override string ToString()
        {
            return $"TilesDrawn: {TilesDrawn}\n"
                + $"DoraTurned: {DoraTurned}\n"
                + $"LingShangDrawn: {LingShangDrawn}\n"
                + $"TilesRemain: {TilesRemain}\n"
                + $"DoraIndicators: {string.Join("", DoraIndicators)}\n"
                + $"TotalTiles: {TotalTiles}";
        }
    }
}

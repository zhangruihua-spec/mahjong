using System;

namespace Mahjong.Model
{
    [Serializable]
    public struct OpenMeld
    {
        public Meld Meld;
        public Tile Tile;
        public MeldSide Side;
        public Tile Extra;
        public bool IsAdded;
        // 这副碰不能再补成回头笑。两种来源：手里已有三张却选择碰而不是点笑；
        // 或者自摸到第四张时可以回头笑，这一手没有杠。
        public bool KongLocked;
        // Three playable copies of the laizi indicator/preceding tile count
        // as a concealed kong because the fourth copy is locked face-up as
        // the indicator on the wall.
        public bool IsLaiziPreviousKong;

        public MeldType Type => Meld.Type;
        public Tile First => Meld.First;
        public bool IsKong => Meld.IsKong;
        public bool Revealed => Meld.Revealed;
        public Tile[] Tiles => Meld.Tiles;

        public Tile[] GetForbiddenTiles(Tile tile)
        {
            return Meld.GetForbiddenTiles(tile);
        }

        public OpenMeld AddToKong(Tile extra)
        {
            return new OpenMeld
            {
                Meld = Meld.AddToKong(extra),
                Tile = Tile,
                Side = Side,
                Extra = extra,
                IsAdded = true
                ,KongLocked = KongLocked,
                IsLaiziPreviousKong = IsLaiziPreviousKong
            };
        }

        public override string ToString()
        {
            return $"{Meld}/{Tile}/{Side}";
        }
    }

    public enum MeldSide
    {
        Left, Opposite, Right, Self
    }
}

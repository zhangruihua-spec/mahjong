using System;
using System.Text;

namespace Mahjong.Model
{
    [Serializable]
    public struct RiverTile
    {
        public Tile Tile;
        public bool IsRichi;
        public bool IsGone;
        public bool IsLaiziKong;
        public bool IsLaizi;

        public override string ToString()
        {
            var builder = new StringBuilder(Tile.ToString());
            if (IsRichi)
                builder.Append("R");
            if (IsGone)
                builder.Append("G");
            if (IsLaiziKong)
                builder.Append("L");
            else if (IsLaizi)
                builder.Append("Z");
            return builder.ToString();
        }
    }

    [Serializable]
    public struct RiverData
    {
        public RiverTile[] River;

        public override string ToString()
        {
            return string.Join("", River);
        }
    }
}

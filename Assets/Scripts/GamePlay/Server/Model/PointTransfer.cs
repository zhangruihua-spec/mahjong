using System;
using Mahjong.Model;

namespace GamePlay.Server.Model
{
    [Serializable]
    public struct PointTransfer
    {
        public int From;
        public int To;
        public int Amount;

        public override string ToString() {
            return $"PointTransfer from player {From} to player {To} with amount of {Amount}";
        }
    }

    [Serializable]
    public struct SmileSettlement
    {
        public int PlayerIndex;
        // Only point-smile (点笑) has a discarding/source player. A value of
        // -1 means the smile was completed from the player's own hand/draw.
        public int SourcePlayerIndex;
        public string Type;
        public Tile Tile;
    }

    [Serializable]
    public struct SettlementDetail
    {
        public int From;
        public int To;
        public int Amount;
        public string Reason;
        public Tile Tile;
    }
}

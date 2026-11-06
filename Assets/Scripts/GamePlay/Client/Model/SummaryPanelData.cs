using Mahjong.Model;
using GamePlay.Server.Model;

namespace GamePlay.Client.Model
{
    public struct SummaryPanelData
    {
        public PlayerHandInfo HandInfo;
        public PointInfo PointInfo;
        public int TotalPoints;
        public string PlayerName;
        public string[] SettlementPlayerNames;
        public SettlementDetail[] SettlementDetails;
        public int[] LaiziKongCounts;
        public SmileSettlement[] SmileSettlements;
    }
}

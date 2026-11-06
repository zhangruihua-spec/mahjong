using System.Collections.Generic;
using GamePlay.Server.Model;

namespace GamePlay.Client.Model
{
    /// <summary>一局结束后的结算记录，用于“游戏记录”面板翻页查看。</summary>
    [System.Serializable]
    public class GameRecord
    {
        public System.DateTime Time;
        public string Reason;
        public string[] PlayerNames;
        public SettlementDetail[] SettlementDetails;
        public int[] LaiziKongCounts;
        public SmileSettlement[] SmileSettlements;
        public int[] Points;
    }

    /// <summary>
    /// 保存当前牌局中每一局的结算记录。数据只保留在本地客户端，
    /// 每次开始新游戏时清空。
    /// </summary>
    public static class GameRecordStore
    {
        private static readonly List<GameRecord> records = new List<GameRecord>();
        private static GameRecord pendingSettlement;

        public static IList<GameRecord> Records
        {
            get { return records; }
        }

        public static void Reset()
        {
            records.Clear();
            pendingSettlement = null;
        }

        /// <summary>
        /// 胡牌信息面板会先展示结算表，而之后的分数结算状态消息里可能不再携带
        /// 结算明细。这里先把已经展示过的明细暂存下来，等本局正式结束时再合并成一条记录。
        /// </summary>
        public static void SetPendingSettlement(System.DateTime time, string[] playerNames,
            SettlementDetail[] details, int[] laiziKongCounts, SmileSettlement[] smileSettlements)
        {
            if (playerNames == null || details == null || details.Length == 0) return;
            pendingSettlement = new GameRecord
            {
                Time = time,
                PlayerNames = Clone(playerNames),
                SettlementDetails = Clone(details),
                LaiziKongCounts = Clone(laiziKongCounts),
                SmileSettlements = Clone(smileSettlements)
            };
        }

        /// <summary>每局结算完成时调用一次，把本局记录追加到记录列表。</summary>
        public static void CommitRound(System.DateTime time, string reason, string[] playerNames,
            SettlementDetail[] details, int[] laiziKongCounts, SmileSettlement[] smileSettlements,
            int[] points)
        {
            var record = new GameRecord
            {
                Time = time,
                Reason = reason,
                PlayerNames = Clone(playerNames),
                SettlementDetails = Clone(details),
                LaiziKongCounts = Clone(laiziKongCounts),
                SmileSettlements = Clone(smileSettlements),
                Points = Clone(points)
            };

            if (pendingSettlement != null)
            {
                if (record.PlayerNames == null || record.PlayerNames.Length == 0)
                    record.PlayerNames = pendingSettlement.PlayerNames;
                if (record.SettlementDetails == null || record.SettlementDetails.Length == 0)
                    record.SettlementDetails = pendingSettlement.SettlementDetails;
                if (record.LaiziKongCounts == null || record.LaiziKongCounts.Length == 0)
                    record.LaiziKongCounts = pendingSettlement.LaiziKongCounts;
                if (record.SmileSettlements == null || record.SmileSettlements.Length == 0)
                    record.SmileSettlements = pendingSettlement.SmileSettlements;
                // 优先使用胡牌面板展示结算表的时间，它更接近本局真正结束的时刻。
                if (pendingSettlement.Time != default(System.DateTime))
                    record.Time = pendingSettlement.Time;
                pendingSettlement = null;
            }

            records.Add(record);
        }

        private static T[] Clone<T>(T[] source)
        {
            if (source == null) return null;
            var copy = new T[source.Length];
            System.Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}

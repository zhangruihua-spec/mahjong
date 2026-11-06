using System.Collections;
using GamePlay.Client.Controller;
using GamePlay.Client.Model;
using GamePlay.Client.View.SubManagers;
using Mahjong.Logic;
using Mahjong.Model;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using GamePlay.SinglePlayer;
using System.Linq;
using System.Collections.Generic;
using GamePlay.Server.Model;

namespace GamePlay.Client.View
{
    /// <summary>
    /// 胡牌信息与中文结算表的界面管理器。
    /// 单机模式隐藏旧日麻役种/宝牌区域，只保留普通胡牌牌面和自定义收支明细。
    /// </summary>
    public class PointSummaryPanelManager : MonoBehaviour
    {
        public Text PlayerNameText;
        public SummaryHandTileManager HandTileManager;
        public WinTypeManager WinTypeManager;
        public DoraPanelManager DoraPanelManager;
        public PointInfoManager PointInfoManager;
        public YakuRankManager YakuRankManager;
        public Button ConfirmButton;
        public CountDownController ConfirmCountDownController;
        private WaitForSeconds waiting = new WaitForSeconds(MahjongConstants.SummaryPanelDelayTime);
        private GameObject settlementDetailsPanel;

        public void ShowPanel(SummaryPanelData data, UnityAction callback)
        {
            ConfirmButton.onClick.RemoveAllListeners();
            ConfirmButton.onClick.AddListener(() =>
            {
                ConfirmCountDownController.StopCountDown();
                callback();
            });
            gameObject.SetActive(true);
            PlayerNameText.text = data.PlayerName;
            ShowSettlementDetails(data.SettlementPlayerNames, data.SettlementDetails,
                data.LaiziKongCounts, data.SmileSettlements);
            HandTileManager.SetHandTiles(data.HandInfo.HandTiles, data.HandInfo.OpenMelds, data.HandInfo.WinningTile);
            WinTypeManager.SetType(data.HandInfo.IsTsumo);
            if (SinglePlayerMode.Enabled)
            {
                // 单机规则只有固定普通胡牌，隐藏旧日麻的役种、宝牌、符番和满贯等级区域。
                if (DoraPanelManager != null) DoraPanelManager.gameObject.SetActive(false);
                if (PointInfoManager != null) PointInfoManager.gameObject.SetActive(false);
                if (YakuRankManager != null) YakuRankManager.gameObject.SetActive(false);
                StartCoroutine(SinglePlayerSummaryCoroutine(callback));
            }
            else
            {
                var uraDora = data.HandInfo.IsRichi ? data.HandInfo.UraDoraIndicators : null;
                DoraPanelManager.SetDoraIndicators(data.HandInfo.DoraIndicators, uraDora);
                // 联网旧规则仍使用役种列表、总点数和牌型等级。
                StartCoroutine(YakuListCoroutine(data.PointInfo, data.TotalPoints, data.HandInfo.IsRichi, callback));
            }
        }

        private IEnumerator SinglePlayerSummaryCoroutine(UnityAction callback)
        {
            // 普通胡牌牌面保持展示，但不调用已禁用的日麻计分组件。
            yield return waiting;
            ConfirmCountDownController.StartCountDown(
                MahjongConstants.SummaryPanelWaitingTime + 10, callback);
        }

        private void ShowSettlementDetails(string[] playerNames, SettlementDetail[] details,
            int[] laiziKongCounts, SmileSettlement[] smileSettlements)
        {
            if (settlementDetailsPanel != null) Destroy(settlementDetailsPanel);
            var font = PlayerNameText != null && PlayerNameText.font != null
                ? PlayerNameText.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // 胡牌面板先展示结算表，后续分数结算消息可能不再携带这些明细；
            // 暂存一份，供本局正式结束时写入“游戏记录”。
            GameRecordStore.SetPendingSettlement(System.DateTime.Now, playerNames, details,
                laiziKongCounts, smileSettlements);
            settlementDetailsPanel = CreateSettlementTable(transform, font, playerNames, details,
                laiziKongCounts, smileSettlements);
        }

        /// <summary>运行时创建四家结算表，分别汇总各类收入、支出、净变化和原因明细。</summary>
        public static GameObject CreateSettlementTable(Transform parent, Font font,
            string[] playerNames, SettlementDetail[] details, int[] laiziKongCounts,
            IList<SmileSettlement> smileSettlements, string titleText = "本局结算明细")
        {
            if (details == null || playerNames == null) return null;

            var columnWidths = new[]
            {
                100f, 55f, 120f, 55f, 55f,
                85f, 85f, 85f, 85f,
                85f, 85f, 85f, 85f,
                78f, 78f,
                95f
            };
            var tableWidth = columnWidths.Sum();

            var panel = new GameObject("SettlementDetails", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.transform.SetAsLastSibling();
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.42f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(tableWidth + 40f, 345f);
            FitSettlementTableToParent(rect, parent, tableWidth + 40f);
            panel.GetComponent<Image>().color = new Color(0.025f, 0.065f, 0.11f, 0.96f);
            CreateSettlementTitle(panel.transform, font, titleText, tableWidth);

            CreateSettlementRow(panel.transform, 50f, 42f,
                new[]
                {
                    "玩家", "癞子杠", "点笑/前位碰", "回头笑", "闷笑",
                    "点笑\n收入", "回头笑\n收入", "闷笑\n收入", "胡牌\n收入",
                    "点笑\n支出", "回头笑\n支出", "闷笑\n支出", "胡牌\n支出",
                    "喜钱\n收入", "喜钱\n支出",
                    "净变化"
                },
                columnWidths, font, true, new Color(0.08f, 0.22f, 0.36f, 1f),
                Enumerable.Repeat(Color.white, columnWidths.Length).ToArray());

            var smiles = smileSettlements ?? new SmileSettlement[0];

            for (int playerIndex = 0; playerIndex < playerNames.Length; playerIndex++)
            {
                var income = details.Where(d => d.To == playerIndex).ToArray();
                var expense = details.Where(d => d.From == playerIndex).ToArray();
                var incomeTotal = income.Sum(d => d.Amount);
                var expenseTotal = expense.Sum(d => d.Amount);
                var net = incomeTotal - expenseTotal;
                var pointSmileIncome = SumCategory(income, SettlementCategory.PointSmile);
                var backSmileIncome = SumCategory(income, SettlementCategory.BackSmile);
                var concealedSmileIncome = SumCategory(income, SettlementCategory.ConcealedSmile);
                var celebrationIncome = SumCategory(income, SettlementCategory.FourLaiziBonus);
                var winIncome = incomeTotal - pointSmileIncome - backSmileIncome
                    - concealedSmileIncome - celebrationIncome;
                var pointSmileExpense = SumCategory(expense, SettlementCategory.PointSmile);
                var backSmileExpense = SumCategory(expense, SettlementCategory.BackSmile);
                var concealedSmileExpense = SumCategory(expense, SettlementCategory.ConcealedSmile);
                var celebrationExpense = SumCategory(expense, SettlementCategory.FourLaiziBonus);
                var winExpense = expenseTotal - pointSmileExpense - backSmileExpense
                    - concealedSmileExpense - celebrationExpense;
                var incomeReasons = FormatReasons(income, playerNames, true);
                var expenseReasons = FormatReasons(expense, playerNames, false);
                var laiziKongCount = laiziKongCounts != null && playerIndex < laiziKongCounts.Length
                    ? laiziKongCounts[playerIndex]
                    : 0;
                var playerSmiles = smiles.Where(s => s.PlayerIndex == playerIndex).ToArray();
                var pointSmiles = playerSmiles.Where(s => s.Type == "点笑").ToArray();
                var previousPongs = playerSmiles.Where(s => s.Type == "前位碰").ToArray();
                var backSmileCount = playerSmiles.Count(s => s.Type == "回头笑");
                var concealedSmileCount = playerSmiles.Count(s => s.Type == "闷笑");
                var sourceSmileText = FormatPointSmileSources(
                    pointSmiles.Concat(previousPongs).ToArray(), playerNames);
                var reasonParts = new List<string>();
                if (!string.IsNullOrEmpty(incomeReasons)) reasonParts.Add($"收入：{incomeReasons}");
                if (!string.IsNullOrEmpty(expenseReasons)) reasonParts.Add($"支出：{expenseReasons}");
                var reasonText = reasonParts.Count > 0 ? string.Join("；", reasonParts.ToArray()) : "无收支";
                var rowColors = new[]
                {
                    Color.white,
                    laiziKongCount > 0 ? new Color(1f, 0.78f, 0.24f) : new Color(0.72f, 0.76f, 0.8f),
                    pointSmiles.Length + previousPongs.Length > 0
                        ? new Color(1f, 0.85f, 0.3f) : new Color(0.72f, 0.76f, 0.8f),
                    backSmileCount > 0 ? new Color(0.35f, 1f, 0.48f) : new Color(0.72f, 0.76f, 0.8f),
                    concealedSmileCount > 0 ? new Color(0.35f, 1f, 0.48f) : new Color(0.72f, 0.76f, 0.8f),
                    IncomeColor(pointSmileIncome),
                    IncomeColor(backSmileIncome),
                    IncomeColor(concealedSmileIncome),
                    IncomeColor(winIncome),
                    ExpenseColor(pointSmileExpense),
                    ExpenseColor(backSmileExpense),
                    ExpenseColor(concealedSmileExpense),
                    ExpenseColor(winExpense),
                    IncomeColor(celebrationIncome),
                    ExpenseColor(celebrationExpense),
                    net > 0 ? new Color(0.35f, 1f, 0.48f)
                        : net < 0 ? new Color(1f, 0.43f, 0.4f) : Color.white
                };
                var rowBackground = playerIndex % 2 == 0
                    ? new Color(0.035f, 0.105f, 0.17f, 0.98f)
                    : new Color(0.045f, 0.13f, 0.205f, 0.98f);
                var rowTop = 92f + playerIndex * 58f;
                CreateSettlementRow(panel.transform, rowTop, 30f,
                    new[]
                    {
                        playerNames[playerIndex] ?? $"玩家{playerIndex + 1}",
                        laiziKongCount > 0 ? $"{laiziKongCount}次" : "无",
                        sourceSmileText,
                        backSmileCount > 0 ? $"{backSmileCount}次" : "无",
                        concealedSmileCount > 0 ? $"{concealedSmileCount}次" : "无",
                        FormatIncome(pointSmileIncome),
                        FormatIncome(backSmileIncome),
                        FormatIncome(concealedSmileIncome),
                        FormatIncome(winIncome),
                        FormatExpense(pointSmileExpense),
                        FormatExpense(backSmileExpense),
                        FormatExpense(concealedSmileExpense),
                        FormatExpense(winExpense),
                        FormatIncome(celebrationIncome),
                        FormatExpense(celebrationExpense),
                        net > 0 ? $"+{net}" : net.ToString()
                    },
                    columnWidths, font, false, rowBackground, rowColors);
                CreateSettlementDetailRow(panel.transform, font, rowTop + 30f, 28f,
                    tableWidth, reasonText, playerIndex % 2 == 0
                        ? new Color(0.025f, 0.08f, 0.13f, 0.98f)
                        : new Color(0.032f, 0.095f, 0.155f, 0.98f));
            }
            return panel;
        }

        private static void FitSettlementTableToParent(RectTransform tableRect,
            Transform parent, float requestedWidth)
        {
            // 喜钱等额外收支列会让表格超过 1280 宽度，因此按父 Canvas 等比缩放，避免名称和末尾列被裁切。
            var availableWidth = 0f;
            var parentRect = parent as RectTransform;
            if (parentRect != null)
                availableWidth = parentRect.rect.width;
            if (availableWidth <= 1f)
            {
                var canvas = parent.GetComponentInParent<Canvas>();
                var canvasRect = canvas == null ? null : canvas.GetComponent<RectTransform>();
                if (canvasRect != null)
                    availableWidth = canvasRect.rect.width;
            }
            if (availableWidth <= 1f || requestedWidth <= 1f) return;

            const float horizontalSafeMargin = 32f;
            var fitScale = Mathf.Min(1f,
                Mathf.Max(0.1f, (availableWidth - horizontalSafeMargin) / requestedWidth));
            tableRect.localScale = new Vector3(fitScale, fitScale, 1f);
        }

        private static void CreateSettlementTitle(Transform parent, Font font, string titleText,
            float tableWidth)
        {
            var titleObject = new GameObject("SettlementTitle", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            titleObject.transform.SetParent(parent, false);
            var titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(20f, -10f);
            titleRect.sizeDelta = new Vector2(tableWidth, 34f);
            var title = titleObject.GetComponent<Text>();
            title.font = font;
            title.fontSize = 24;
            title.fontStyle = FontStyle.Bold;
            title.alignment = TextAnchor.MiddleLeft;
            title.color = Color.white;
            title.text = string.IsNullOrEmpty(titleText) ? "本局结算明细" : titleText;
        }

        private static void CreateSettlementRow(Transform parent, float top, float height,
            string[] values, float[] widths, Font font, bool isHeader, Color background,
            Color[] textColors)
        {
            var rowObject = new GameObject(isHeader ? "TableHeader" : "TableRow",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            rowObject.transform.SetParent(parent, false);
            var rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(20f, -top);
            rowRect.sizeDelta = new Vector2(widths.Sum(), height);
            rowObject.GetComponent<Image>().color = background;

            var x = 0f;
            for (int i = 0; i < values.Length && i < widths.Length; i++)
            {
                if (i > 0)
                {
                    var separator = new GameObject("Separator", typeof(RectTransform),
                        typeof(CanvasRenderer), typeof(Image));
                    separator.transform.SetParent(rowObject.transform, false);
                    var separatorRect = separator.GetComponent<RectTransform>();
                    separatorRect.anchorMin = separatorRect.anchorMax = new Vector2(0f, 0.5f);
                    separatorRect.pivot = new Vector2(0.5f, 0.5f);
                    separatorRect.anchoredPosition = new Vector2(x, 0f);
                    separatorRect.sizeDelta = new Vector2(1f, height - 4f);
                    separator.GetComponent<Image>().color = new Color(0.35f, 0.55f, 0.7f, 0.55f);
                }

                var textObject = new GameObject("Column" + i, typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text));
                textObject.transform.SetParent(rowObject.transform, false);
                var textRect = textObject.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0f, 0f);
                textRect.anchorMax = new Vector2(0f, 1f);
                textRect.pivot = new Vector2(0f, 0.5f);
                textRect.anchoredPosition = new Vector2(x + 8f, 0f);
                textRect.sizeDelta = new Vector2(widths[i] - 16f, 0f);
                var text = textObject.GetComponent<Text>();
                text.font = font;
                text.fontSize = isHeader ? (widths[i] <= 68f ? 13 : 15)
                    : i == values.Length - 1 ? 14
                    : widths[i] <= 75f ? 15
                    : i == 2 ? 13
                    : 17;
                text.fontStyle = isHeader || i == 0 ? FontStyle.Bold : FontStyle.Normal;
                text.alignment = i == values.Length - 1
                    ? TextAnchor.MiddleLeft
                    : TextAnchor.MiddleCenter;
                text.color = i < textColors.Length ? textColors[i] : Color.white;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.text = values[i];
                x += widths[i];
            }
        }

        private static void CreateSettlementDetailRow(Transform parent, Font font, float top,
            float height, float width, string detail, Color background)
        {
            var rowObject = new GameObject("SettlementDetailRow", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            rowObject.transform.SetParent(parent, false);
            var rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(20f, -top);
            rowRect.sizeDelta = new Vector2(width, height);
            rowObject.GetComponent<Image>().color = background;

            var textObject = new GameObject("SettlementDetailText", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(rowObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 1f);
            textRect.offsetMax = new Vector2(-8f, -1f);
            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = 11;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = new Color(0.9f, 0.94f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = $"结算明细：{detail}";
        }

        private static string FormatReasons(IEnumerable<SettlementDetail> details,
            string[] playerNames, bool isIncome)
        {
            return string.Join("，", details
                .GroupBy(d => new
                {
                    d.Reason,
                    Counterpart = d.Reason != null
                        && (d.Reason.StartsWith("点笑") || d.Reason.StartsWith("前位碰"))
                        ? (isIncome ? d.From : d.To)
                        : -1
                })
                .Select(group =>
                {
                    if (group.Key.Counterpart < 0)
                        return $"{group.Key.Reason} {group.Sum(d => d.Amount)}";
                    var counterpart = group.Key.Counterpart;
                    var counterpartName = counterpart >= 0 && counterpart < playerNames.Length
                        ? playerNames[counterpart]
                        : $"玩家{counterpart + 1}";
                    var relation = isIncome
                        ? $"{counterpartName}打出"
                        : $"打给{counterpartName}";
                    return $"{group.Key.Reason}（{relation}） {group.Sum(d => d.Amount)}";
                }).ToArray());
        }

        private enum SettlementCategory
        {
            PointSmile,
            BackSmile,
            ConcealedSmile,
            FourLaiziBonus
        }

        private static int SumCategory(IEnumerable<SettlementDetail> details,
            SettlementCategory category)
        {
            return details.Where(detail => IsCategory(detail.Reason, category)).Sum(detail => detail.Amount);
        }

        private static bool IsCategory(string reason, SettlementCategory category)
        {
            if (string.IsNullOrEmpty(reason)) return false;
            switch (category)
            {
                case SettlementCategory.PointSmile:
                    return reason.StartsWith("点笑") || reason.StartsWith("前位碰");
                case SettlementCategory.BackSmile:
                    return reason.StartsWith("回头笑");
                case SettlementCategory.ConcealedSmile:
                    return reason.StartsWith("闷笑");
                case SettlementCategory.FourLaiziBonus:
                    return reason.StartsWith("四癞子喜钱");
                default:
                    return false;
            }
        }

        private static string FormatIncome(int amount)
        {
            return amount > 0 ? $"+{amount}" : "0";
        }

        private static string FormatExpense(int amount)
        {
            return amount > 0 ? $"-{amount}" : "0";
        }

        private static Color IncomeColor(int amount)
        {
            return amount > 0 ? new Color(0.35f, 1f, 0.48f) : new Color(0.72f, 0.76f, 0.8f);
        }

        private static Color ExpenseColor(int amount)
        {
            return amount > 0 ? new Color(1f, 0.43f, 0.4f) : new Color(0.72f, 0.76f, 0.8f);
        }

        private static string FormatPointSmileSources(IList<SmileSettlement> pointSmiles,
            string[] playerNames)
        {
            if (pointSmiles == null || pointSmiles.Count == 0) return "无";
            return string.Join("\n", pointSmiles.GroupBy(smile => smile.Type)
                .Select(group =>
                {
                    var sources = group.Select(smile => smile.SourcePlayerIndex >= 0
                            && smile.SourcePlayerIndex < playerNames.Length
                            ? playerNames[smile.SourcePlayerIndex]
                            : "未知玩家")
                        .Where(name => !string.IsNullOrEmpty(name))
                        .Distinct()
                        .ToArray();
                    var sourceText = sources.Length == 0 ? "" : $"：{string.Join("、", sources)}";
                    return $"{group.Key}{group.Count()}次{sourceText}";
                }).ToArray());
        }

        private IEnumerator YakuListCoroutine(PointInfo point, int totalPoints, bool richi, UnityAction callback)
        {
            yield return PointInfoManager.SetPointInfo(point, totalPoints, richi);
            yield return waiting;
            YakuRankManager.ShowYakuRank(point);
            ConfirmCountDownController.StartCountDown(MahjongConstants.SummaryPanelWaitingTime + 10, callback);
        }

        public int StopCountDown()
        {
            return ConfirmCountDownController.StopCountDown();
        }

        public void Close()
        {
            if (settlementDetailsPanel != null)
            {
                Destroy(settlementDetailsPanel);
                settlementDetailsPanel = null;
            }
            gameObject.SetActive(false);
        }
    }
}

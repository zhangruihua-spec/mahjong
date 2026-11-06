using GamePlay.Client.Model;
using GamePlay.Server.Model;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GamePlay.Client.View
{
    /// <summary>
    /// 左上角“游戏记录”按钮和分页记录面板。
    /// 每页展示一局的结算表，并额外显示该局的结束时间和结算原因。
    /// 界面全部在运行时创建，不需要修改场景或预制体。
    /// </summary>
    public class GameRecordPanelManager : MonoBehaviour
    {
        private static GameRecordPanelManager instance;

        private Font font;
        private GameObject panel;
        private RectTransform contentRect;
        private Text headerText;
        private Text pageText;
        private Button previousButton;
        private Button nextButton;
        private GameObject currentTable;
        private int pageIndex;

        /// <summary>在指定 Canvas 下创建记录按钮和面板；重复调用不会生成多份。</summary>
        public static void EnsureCreated(Canvas canvas, Font font)
        {
            if (canvas == null) return;
            if (instance != null)
            {
                if (font != null) instance.font = font;
                instance.transform.SetAsLastSibling();
                return;
            }
            var existing = FindObjectOfType<GameRecordPanelManager>();
            if (existing != null)
            {
                instance = existing;
                if (font != null) instance.font = font;
                instance.transform.SetAsLastSibling();
                return;
            }

            var root = new GameObject("GameRecordUI", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            root.transform.SetAsLastSibling();
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var manager = root.AddComponent<GameRecordPanelManager>();
            manager.font = font;
            manager.Build();
            instance = manager;
        }

        private void Build()
        {
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            CreateRecordButton();
            CreatePanel();
        }

        private void CreateRecordButton()
        {
            var button = CreateButton(transform, "GameRecordButton", "游戏记录",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(40f, -136f), new Vector2(150f, 42f), Show);
            button.GetComponent<Image>().color = new Color(0.05f, 0.18f, 0.32f, 0.96f);
        }

        private void CreatePanel()
        {
            panel = new GameObject("GameRecordPanel", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);

            // 顶部标题：显示页码、时间、结算原因。
            headerText = CreateText(panel.transform, "Header", "游戏记录",
                TextAnchor.MiddleLeft, 28, Color.white);
            var headerRect = headerText.GetComponent<RectTransform>();
            headerRect.anchorMin = headerRect.anchorMax = new Vector2(0f, 1f);
            headerRect.pivot = new Vector2(0f, 1f);
            headerRect.anchoredPosition = new Vector2(48f, -28f);
            headerRect.sizeDelta = new Vector2(1040f, 48f);
            headerText.fontStyle = FontStyle.Bold;

            var closeButton = CreateButton(panel.transform, "CloseButton", "关闭",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-40f, -28f), new Vector2(110f, 46f), Hide);
            closeButton.GetComponent<Image>().color = new Color(0.35f, 0.1f, 0.1f, 0.96f);

            // 中间区域留给结算表，底部留给翻页按钮。
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(panel.transform, false);
            contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(0f, 86f);
            contentRect.offsetMax = new Vector2(0f, -90f);

            previousButton = CreateButton(panel.transform, "PreviousButton", "上一页",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(40f, 26f), new Vector2(130f, 46f), () => ChangePage(-1));
            nextButton = CreateButton(panel.transform, "NextButton", "下一页",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-40f, 26f), new Vector2(130f, 46f), () => ChangePage(1));

            pageText = CreateText(panel.transform, "PageText", string.Empty,
                TextAnchor.MiddleCenter, 24, Color.white);
            var pageRect = pageText.GetComponent<RectTransform>();
            pageRect.anchorMin = pageRect.anchorMax = new Vector2(0.5f, 0f);
            pageRect.pivot = new Vector2(0.5f, 0f);
            pageRect.anchoredPosition = new Vector2(0f, 26f);
            pageRect.sizeDelta = new Vector2(360f, 46f);

            panel.SetActive(false);
        }

        private void Show()
        {
            if (panel == null) return;
            transform.SetAsLastSibling();
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            var count = GameRecordStore.Records.Count;
            // 默认打开最新一局，玩家通常更关心刚刚结束的牌局。
            pageIndex = count > 0 ? count - 1 : 0;
            RefreshPage();
        }

        private void Hide()
        {
            if (panel != null) panel.SetActive(false);
            DestroyCurrentTable();
        }

        private void ChangePage(int delta)
        {
            var count = GameRecordStore.Records.Count;
            if (count == 0) return;
            pageIndex = Mathf.Clamp(pageIndex + delta, 0, count - 1);
            RefreshPage();
        }

        private void RefreshPage()
        {
            DestroyCurrentTable();
            var records = GameRecordStore.Records;
            var count = records.Count;
            if (count == 0)
            {
                headerText.text = "游戏记录";
                pageText.text = "暂无游戏记录";
                previousButton.interactable = false;
                nextButton.interactable = false;
                return;
            }

            pageIndex = Mathf.Clamp(pageIndex, 0, count - 1);
            var record = records[pageIndex];
            var timeText = record.Time == default(System.DateTime)
                ? string.Empty
                : record.Time.ToString("yyyy-MM-dd HH:mm:ss");
            var reasonText = string.IsNullOrEmpty(record.Reason) ? string.Empty : record.Reason;
            var parts = new System.Collections.Generic.List<string>
            {
                $"第 {pageIndex + 1} / {count} 局"
            };
            if (!string.IsNullOrEmpty(timeText)) parts.Add(timeText);
            if (!string.IsNullOrEmpty(reasonText)) parts.Add(reasonText);
            headerText.text = string.Join("    ", parts.ToArray());

            pageText.text = $"第 {pageIndex + 1} / {count} 页";
            previousButton.interactable = pageIndex > 0;
            nextButton.interactable = pageIndex < count - 1;

            var isExhaustiveDraw = record.Reason == "荒牌结算" || record.Reason == "牌堆摸完结算";
            var hasDetails = record.PlayerNames != null && record.SettlementDetails != null
                && (record.SettlementDetails.Length > 0 || isExhaustiveDraw);
            if (!hasDetails)
            {
                var emptyText = CreateText(contentRect, "NoDetails", "本局无结算明细",
                    TextAnchor.MiddleCenter, 28, new Color(0.8f, 0.84f, 0.9f, 1f));
                var emptyRect = emptyText.GetComponent<RectTransform>();
                emptyRect.anchorMin = emptyRect.anchorMax = new Vector2(0.5f, 0.5f);
                emptyRect.anchoredPosition = Vector2.zero;
                emptyRect.sizeDelta = new Vector2(600f, 60f);
                currentTable = emptyText.gameObject;
                return;
            }

            currentTable = PointSummaryPanelManager.CreateSettlementTable(contentRect, font,
                record.PlayerNames, record.SettlementDetails, record.LaiziKongCounts,
                record.SmileSettlements, isExhaustiveDraw ? "荒牌结算明细" : "本局结算明细");
        }

        private void DestroyCurrentTable()
        {
            if (currentTable == null) return;
            currentTable.SetActive(false);
            Destroy(currentTable);
            currentTable = null;
        }

        private Button CreateButton(Transform parent, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position,
            Vector2 size, UnityAction onClick)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.08f, 0.22f, 0.36f, 0.96f);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = CreateText(buttonObject.transform, "Text", label,
                TextAnchor.MiddleCenter, 20, Color.white);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 2f);
            textRect.offsetMax = new Vector2(-4f, -2f);
            return button;
        }

        private Text CreateText(Transform parent, string name, string value,
            TextAnchor alignment, int fontSize, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}

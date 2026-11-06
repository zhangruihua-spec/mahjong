using Common.Interfaces;
using GamePlay.Client.Model;
using UnityEngine;
using UnityEngine.UI;

namespace GamePlay.Client.View
{
    public class LocalSettingManager : MonoBehaviour, IObserver<ClientLocalSettings>
    {
        private ClientLocalSettings LocalSetting;
        public Toggle Li;
        public Toggle He;
        public Toggle Ming;
        public Toggle Qie;
        private GameObject helpPage;

        private void Awake()
        {
			CreateAudioToggles();
			// 暂时隐藏左侧“理/和/鸣/切”辅助面板和右上角帮助入口。
			// 功能与创建方法仍然保留，之后需要时可直接恢复。
			HideLegacyAssistPanel();
        }

		private void HideLegacyAssistPanel()
		{
			var ownGraphic = GetComponent<Graphic>();
			if (ownGraphic != null) ownGraphic.enabled = false;
			for (int i = 0; i < transform.childCount; ++i)
				transform.GetChild(i).gameObject.SetActive(false);
		}

		private void CreateAudioToggles()
		{
			CreateAudioToggle("SoundEffectsToggle", "音效", -82f,
				GameSoundManager.SoundEffectsEnabled, GameSoundManager.SetSoundEffectsEnabled);
			CreateAudioToggle("MusicToggle", "音乐", -140f,
				GameSoundManager.MusicEnabled, GameSoundManager.SetMusicEnabled);
		}

		private void CreateAudioToggle(string objectName, string label, float y,
			bool initialValue, UnityEngine.Events.UnityAction<bool> changed)
		{
			var canvas = GetComponentInParent<Canvas>();
			var toggleObject = new GameObject(objectName, typeof(RectTransform),
				typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
			toggleObject.transform.SetParent(canvas != null ? canvas.transform : transform, false);
			toggleObject.transform.SetAsLastSibling();
			var rect = toggleObject.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
			rect.pivot = new Vector2(1f, 1f);
			rect.anchoredPosition = new Vector2(-22f, y);
			rect.sizeDelta = new Vector2(150f, 48f);
			var background = toggleObject.GetComponent<Image>();
			background.color = new Color(0.04f, 0.16f, 0.25f, 0.95f);

			var markObject = new GameObject("Checkmark", typeof(RectTransform),
				typeof(CanvasRenderer), typeof(Image));
			markObject.transform.SetParent(toggleObject.transform, false);
			var markRect = markObject.GetComponent<RectTransform>();
			markRect.anchorMin = markRect.anchorMax = new Vector2(0f, 0.5f);
			markRect.pivot = new Vector2(0f, 0.5f);
			markRect.anchoredPosition = new Vector2(12f, 0f);
			markRect.sizeDelta = new Vector2(25f, 25f);
			var mark = markObject.GetComponent<Image>();
			mark.color = new Color(0.2f, 0.85f, 0.45f, 1f);

			var text = CreateText(toggleObject.transform, "", 23, TextAnchor.MiddleCenter,
				Color.white, Vector2.zero, Vector2.one, new Vector2(38f, 0f), Vector2.zero);
			var toggle = toggleObject.GetComponent<Toggle>();
			toggle.targetGraphic = background;
			toggle.graphic = mark;
			toggle.isOn = initialValue;
			System.Action<bool> refreshLabel = value => text.text = label + "：" + (value ? "开" : "关");
			refreshLabel(initialValue);
			toggle.onValueChanged.AddListener(value =>
			{
				refreshLabel(value);
				changed(value);
			});
		}

        private Font UiFont
        {
            get
            {
                var label = Li == null ? null : Li.GetComponentInChildren<Text>(true);
                return label != null && label.font != null
                    ? label.font
                    : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        private void CreateHelpButton()
        {
            var buttonObject = new GameObject("LocalSettingHelpButton", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            // Parent the button directly to the full-screen canvas so its
            // position is independent of the narrow settings strip at left.
            var canvas = GetComponentInParent<Canvas>();
            buttonObject.transform.SetParent(canvas != null ? canvas.transform : transform, false);
            buttonObject.transform.SetAsLastSibling();
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-22f, -22f);
            rect.sizeDelta = new Vector2(105f, 48f);
            buttonObject.GetComponent<Image>().color = new Color(0.04f, 0.16f, 0.25f, 0.95f);
            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() =>
            {
                // Keep the help page above the hand and every other HUD panel.
                helpPage.transform.SetAsLastSibling();
                helpPage.SetActive(true);
            });
            CreateText(buttonObject.transform, "帮助", 25, TextAnchor.MiddleCenter,
                Color.white, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private void CreateHelpPage()
        {
            helpPage = new GameObject("LocalSettingHelpPage", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            // The settings widget is rendered below the hand UI.  Parent the
            // modal directly to the canvas so hand tiles cannot cover it.
            var canvas = GetComponentInParent<Canvas>();
            helpPage.transform.SetParent(canvas != null ? canvas.transform : transform, false);
            helpPage.transform.SetAsLastSibling();
            var rect = helpPage.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(820f, 570f);
            helpPage.GetComponent<Image>().color = new Color(0.025f, 0.08f, 0.14f, 0.97f);

            CreateText(helpPage.transform, "操作辅助功能说明", 36, TextAnchor.MiddleCenter,
                new Color(1f, 0.82f, 0.25f), new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(20f, -78f), new Vector2(-20f, -18f));

            const string content =
                "理｜整理手牌\n"
                + "出牌后立即按照万、筒、条和点数整理手牌。当前单机模式默认会即时刷新和整理。\n\n"
                + "和｜自动胡牌\n"
                + "自己摸到可以胡的牌时，自动选择自摸，不需要再手动点击胡牌按钮。\n\n"
                + "鸣｜不鸣牌\n"
                + "开启后自动跳过别人出牌时的碰牌和点笑提示。当前规则不能吃牌；关闭后由玩家自己选择碰或点笑。\n\n"
                + "切｜自动摸切\n"
                + "轮到自己并且只有出牌操作时，自动打出刚摸到的牌。如果还有胡牌、杠牌或赖子杠操作，会先保留操作选择。";
            var contentText = CreateText(helpPage.transform, content, 22, TextAnchor.UpperLeft,
                Color.white, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(48f, 105f), new Vector2(-48f, -95f));
            contentText.horizontalOverflow = HorizontalWrapMode.Wrap;
            contentText.verticalOverflow = VerticalWrapMode.Truncate;
            contentText.lineSpacing = 1.05f;
            contentText.resizeTextForBestFit = true;
            contentText.resizeTextMinSize = 18;
            contentText.resizeTextMaxSize = 22;

            var closeObject = new GameObject("CloseButton", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            closeObject.transform.SetParent(helpPage.transform, false);
            var closeRect = closeObject.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 0f);
            closeRect.pivot = new Vector2(0.5f, 0f);
            closeRect.anchoredPosition = new Vector2(0f, 22f);
            closeRect.sizeDelta = new Vector2(180f, 55f);
            closeObject.GetComponent<Image>().color = new Color(0.08f, 0.35f, 0.55f, 1f);
            closeObject.GetComponent<Button>().onClick.AddListener(() => helpPage.SetActive(false));
            CreateText(closeObject.transform, "关闭", 27, TextAnchor.MiddleCenter,
                Color.white, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            helpPage.SetActive(false);
        }

        private Text CreateText(Transform parent, string value, int size, TextAnchor alignment,
            Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var textObject = new GameObject("Text", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var text = textObject.GetComponent<Text>();
            text.font = UiFont;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            return text;
        }
        public void OnLiChanged(bool value)
        {
            if (LocalSetting == null) return;
            LocalSetting.Li = value;
        }

        public void OnHeChanged(bool value)
        {
            if (LocalSetting == null) return;
            LocalSetting.He = value;
        }

        public void OnMingChanged(bool value)
        {
            if (LocalSetting == null) return;
            LocalSetting.Ming = value;
        }

        public void OnQieChanged(bool value)
        {
            if (LocalSetting == null) return;
            LocalSetting.Qie = value;
        }

        public void UpdateStatus(ClientLocalSettings subject)
        {
            LocalSetting = subject;
            Li.isOn = subject.Li;
            He.isOn = subject.He;
            Ming.isOn = subject.Ming;
            Qie.isOn = subject.Qie;
        }
    }
}

using System.Collections.Generic;
using Common.Interfaces;
using GamePlay.Client.Model;
using GamePlay.Client.Controller;
using Mahjong.Model;
using UnityEngine;
using UnityEngine.UI;

namespace GamePlay.Client.View
{
    public class TableTilesManager : MonoBehaviour, IObserver<ClientRoundStatus>
    {
        public PlayerHandManager[] HandManagers;
        public OpenMeldManager[] OpenManagers;
        public PlayerRiverManager[] RiverManagers;
        public PlayerBeiDoraManager[] BeiManagers;
        private bool initialDealAnimating;
        private ClientRoundStatus currentStatus;
		private Text laiziDescription;

        private void Awake()
        {
            // 把四个副露区域的座位信息互相绑定。点笑牌组需要据此把箭头
            // 指向真正打出第四张牌的玩家，而不是只按牌组朝向猜测。
            if (OpenManagers == null) return;
            for (int placeIndex = 0; placeIndex < OpenManagers.Length; placeIndex++)
            {
                if (OpenManagers[placeIndex] != null)
                    OpenManagers[placeIndex].ConfigureSeat(placeIndex, OpenManagers);
            }
        }

		private void Start()
		{
			// 左右人物装饰暂时不显示；资源和创建方法保留，之后可恢复。
			CreateLaiziDescription();
			CreateGameRecordUI();
			if (currentStatus != null) UpdateLaiziDescription(currentStatus);
		}

		private void CreateGameRecordUI()
		{
			// The table is nested under a lower sorting-order canvas in the Mahjong
			// scene. Creating the button there makes it visible, but another HUD
			// canvas receives the pointer first. Always use the topmost active canvas.
			Canvas canvas = null;
			foreach (var candidate in FindObjectsOfType<Canvas>())
			{
				if (!candidate.isActiveAndEnabled) continue;
				var group = candidate.GetComponent<CanvasGroup>();
				if (group != null && (!group.interactable || !group.blocksRaycasts)) continue;
				if (canvas == null || candidate.sortingOrder > canvas.sortingOrder)
					canvas = candidate;
			}
			if (canvas == null) return;
			if (canvas.GetComponent<GraphicRaycaster>() == null)
				canvas.gameObject.AddComponent<GraphicRaycaster>();
			var font = laiziDescription == null ? null : laiziDescription.font;
			if (font == null)
				font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			GameRecordPanelManager.EnsureCreated(canvas, font);
		}

		private void CreateTableGirls()
		{
			var canvas = GetComponentInParent<Canvas>();
			if (canvas == null) canvas = FindObjectOfType<Canvas>();
			if (canvas == null) return;
			CreateTableGirl(canvas.transform, "GirlAutoLeft",
				"Textures/UIElements/Girl_Auto", true, new Vector2(205f, 217f), new Vector2(12f, 36f));
			CreateTableGirl(canvas.transform, "GirlWinRight",
				"Textures/UIElements/Girl_Win", false, new Vector2(180f, 272f), new Vector2(-12f, 30f));
		}

		private static void CreateTableGirl(Transform parent, string objectName,
			string resourcePath, bool left, Vector2 size, Vector2 position)
		{
			var texture = Resources.Load<Texture2D>(resourcePath);
			if (texture == null)
			{
				Debug.LogWarning("找不到桌面人物图片: Resources/" + resourcePath);
				return;
			}
			var imageObject = new GameObject(objectName, typeof(RectTransform),
				typeof(CanvasRenderer), typeof(RawImage));
			imageObject.transform.SetParent(parent, false);
			// 放到桌面画面上层；RawImage 不接收射线，不会挡住手牌和按钮点击。
			imageObject.transform.SetAsLastSibling();
			var rect = imageObject.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(left ? 0f : 1f, .5f);
			rect.pivot = new Vector2(left ? 0f : 1f, .5f);
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
			var image = imageObject.GetComponent<RawImage>();
			image.texture = texture;
			image.color = Color.white;
			image.raycastTarget = false;
		}

		private void CreateLaiziDescription()
		{
			var canvas = GetComponentInParent<Canvas>();
			if (canvas == null) canvas = FindObjectOfType<Canvas>();
			if (canvas == null) return;
			var labelObject = new GameObject("LaiziDescription", typeof(RectTransform),
				typeof(CanvasRenderer), typeof(Text), typeof(Outline));
			labelObject.transform.SetParent(canvas.transform, false);
			labelObject.transform.SetAsLastSibling();
			var rect = labelObject.GetComponent<RectTransform>();
			// 癞子说明放在左上角，和右侧的音效/音乐开关分开，避免遮挡。
			rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);
			rect.anchoredPosition = new Vector2(40f, -18f);
			rect.sizeDelta = new Vector2(250f, 42f);
			laiziDescription = labelObject.GetComponent<Text>();
			laiziDescription.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			laiziDescription.fontSize = 24;
			laiziDescription.fontStyle = FontStyle.Bold;
			laiziDescription.alignment = TextAnchor.MiddleLeft;
			laiziDescription.color = new Color(1f, .82f, .2f, 1f);
			laiziDescription.text = "癞子：未确定";
			var outline = labelObject.GetComponent<Outline>();
			outline.effectColor = new Color(0f, 0f, 0f, .9f);
			outline.effectDistance = new Vector2(2f, -2f);
		}

		private void UpdateLaiziDescription(ClientRoundStatus status)
		{
			if (laiziDescription == null) return;
			var tile = status.MahjongSetData.LaiziTile;
			laiziDescription.text = tile.Rank > 0
				? "癞子：" + FormatTileName(tile)
				: "癞子：未确定";
		}

		private static string FormatTileName(Tile tile)
		{
			string[] numbers = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
			if (tile.Suit == Suit.M) return numbers[tile.Rank] + "万";
			if (tile.Suit == Suit.P) return numbers[tile.Rank] + "筒";
			if (tile.Suit == Suit.S) return numbers[tile.Rank] + "条";
			string[] honors = { "", "东风", "南风", "西风", "北风", "白板", "发财", "红中" };
			return tile.Rank >= 1 && tile.Rank < honors.Length ? honors[tile.Rank] : tile.ToStringIgnoreColor();
		}

        private void UpdateHands(ClientRoundStatus status)
        {
            if (initialDealAnimating) return;
            for (int placeIndex = 0; placeIndex < HandManagers.Length; placeIndex++)
            {
                var hand = HandManagers[placeIndex];
                // The local player's complete hand is already rendered by
                // HandPanelManager at the bottom of the screen. Hide the
                // duplicate 3D concealed hand beneath it entirely.
                hand.Count = placeIndex == 0 ? 0 : status.GetTileCount(placeIndex);
                // The local player's drawn tile is rendered by the bottom UI
                // HandPanelManager. Do not also render its 3D table copy,
                // otherwise an orange-backed tile appears behind it.
                hand.LastDraw = placeIndex == 0 ? (Tile?)null : status.GetLastDraw(placeIndex);
            }
            HandManagers[0].HandTiles = null;
        }

        public void BeginInitialDealAnimation()
        {
            initialDealAnimating = true;
            for (int placeIndex = 0; placeIndex < HandManagers.Length; placeIndex++)
            {
                HandManagers[placeIndex].Count = 0;
                HandManagers[placeIndex].HandTiles = null;
                HandManagers[placeIndex].LastDraw = null;
                HandManagers[placeIndex].StandUp();
            }
        }

        public void SetInitialDealCount(int placeIndex, int count)
        {
            if (!initialDealAnimating || placeIndex < 0 || placeIndex >= HandManagers.Length) return;
            HandManagers[placeIndex].Count = count;
        }

        public void EndInitialDealAnimation(ClientRoundStatus status)
        {
            initialDealAnimating = false;
            UpdateHands(status);
        }

        public void SetMelds(int placeIndex, IList<OpenMeld> melds)
        {
            OpenManagers[placeIndex].SetMelds(melds);
            // 副露通常在状态通知之后才刷新，更新完成后再按最新公开牌重算一次推荐。
            if (currentStatus != null && ViewController.Instance != null)
                ViewController.Instance.HandPanelManager.RefreshRecommendation(currentStatus);
        }

        /// <summary>返回四家所有公开副露中的牌，用于扣除已经不可能从牌墙摸到的牌。</summary>
        public IEnumerable<Tile> GetVisibleMeldTiles()
        {
            if (OpenManagers == null) yield break;
            foreach (var manager in OpenManagers)
            {
                if (manager == null) continue;
                foreach (var meld in manager.CurrentMelds)
                    foreach (var tile in meld.Tiles)
                        yield return tile;
            }
        }

        public void ClearMelds(int placeIndex)
        {
            OpenManagers[placeIndex].ClearMelds();
        }

        public void ClearMelds()
        {
            System.Array.ForEach(OpenManagers, m => m.ClearMelds());
        }

        private void UpdateRivers(ClientRoundStatus status)
        {
            for (int placeIndex = 0; placeIndex < RiverManagers.Length; placeIndex++)
            {
                var manager = RiverManagers[placeIndex];
                manager.LaiziTile = status.MahjongSetData.LaiziTile;
                manager.RiverTiles = status.GetRiverTiles(placeIndex);
            }
        }

        private void UpdateBeiDoras(ClientRoundStatus status)
        {
            if (status.BeiDoras == null) return;
            for (int placeIndex = 0; placeIndex < BeiManagers.Length; placeIndex++)
            {
                var manager = BeiManagers[placeIndex];
                manager.SetBeiDoras(status.BeiDoras[placeIndex]);
            }
        }

        public void DiscardTile(int placeIndex, bool discardingLastDraw)
        {
            if (placeIndex == 0) return;
            HandManagers[placeIndex].DiscardTile(discardingLastDraw);
        }

        public void SetHandTiles(int placeIndex, IList<Tile> handTiles)
        {
            if (placeIndex == 0)
            {
                HandManagers[placeIndex].Count = 0;
                HandManagers[placeIndex].HandTiles = null;
                return;
            }
            HandManagers[placeIndex].HandTiles = handTiles;
        }

        public void StandUp(int placeIndex)
        {
            HandManagers[placeIndex].StandUp();
        }

        public void StandUp()
        {
            System.Array.ForEach(HandManagers, manager => manager.StandUp());
        }

        public void OpenUp(int placeIndex)
        {
            HandManagers[placeIndex].OpenUp();
        }

        public void OpenUp()
        {
            System.Array.ForEach(HandManagers, m => m.OpenUp());
        }

        public void CloseDown(int placeIndex)
        {
            HandManagers[placeIndex].CloseDown();
        }

        public void CloseDown()
        {
            System.Array.ForEach(HandManagers, m => m.CloseDown());
        }


        public void ShineLastTile(int placeIndex)
        {
            var lastTile = RiverManagers[placeIndex].GetLastTile();
            if (lastTile == null) return;
            lastTile.Shine();
        }

        public void ShineOff(int placeIndex)
        {
            RiverManagers[placeIndex].ShineOff();
        }

        public void ShineOff()
        {
            for (int placeIndex = 0; placeIndex < RiverManagers.Length; placeIndex++)
            {
                ShineOff(placeIndex);
            }
        }

        public void UpdateStatus(ClientRoundStatus subject)
        {
            if (subject == null) return;
            currentStatus = subject;
            UpdateHands(subject);
            UpdateRivers(subject);
            UpdateBeiDoras(subject);
			UpdateLaiziDescription(subject);
        }
    }
}

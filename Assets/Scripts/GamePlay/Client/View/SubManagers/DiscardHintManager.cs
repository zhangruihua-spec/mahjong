using System.Collections.Generic;
using Mahjong.Model;
using Managers;
using UnityEngine;
using UnityEngine.UI;
using GamePlay.Client.Model;
using GamePlay.Client.Controller;

namespace GamePlay.Client.View.SubManagers
{
    public class DiscardHintManager : MonoBehaviour
    {
        private const float TileWidth = 62;
        private const float Gap = 10;
        public RectTransform Background;
        public Image[] WaitingTiles;

        public void SetWaitingTiles(IList<Tile> list, ClientRoundStatus status = null)
        {
            var manager = ResourceManager.Instance;
            var count = list.Count;
            // Reserve a separate row below the tile faces for the large counts.
            Background.sizeDelta = new Vector2(count * TileWidth + (count + 1) * Gap, 200f);
            var layout = Background.GetComponent<HorizontalLayoutGroup>();
            if (layout != null) layout.padding.bottom = 80;
            for (int i = 0; i < count; i++)
            {
                WaitingTiles[i].sprite = manager.GetTileSprite(list[i]);
                WaitingTiles[i].gameObject.SetActive(true);
                SetRemainingCount(WaitingTiles[i], GetRemainingCount(list[i], status));
            }
            for (int i = count; i < WaitingTiles.Length; i++)
            {
                WaitingTiles[i].gameObject.SetActive(false);
            }
        }

        private int GetRemainingCount(Tile tile, ClientRoundStatus status)
        {
            if (status == null) return 0;

            var used = 0;
            if (status.LocalPlayerHandTiles != null)
                foreach (var handTile in status.LocalPlayerHandTiles)
                    if (handTile.EqualsIgnoreColor(tile)) used++;
            var lastDraw = status.GetLastDraw(0);
            // Some snapshots already contain the separately displayed draw.
            if (lastDraw.HasValue && lastDraw.Value.EqualsIgnoreColor(tile)
                && (status.LocalPlayerHandTiles == null || status.LocalPlayerHandTiles.Count % 3 != 2)) used++;
            if (status.Rivers != null)
                foreach (var river in status.Rivers)
                    if (river.River != null)
                        foreach (var riverTile in river.River)
                            if (!riverTile.IsGone && riverTile.Tile.EqualsIgnoreColor(tile)) used++;

            var table = ViewController.Instance == null ? null : ViewController.Instance.TableTilesManager;
            if (table != null)
                foreach (var meldTile in table.GetVisibleMeldTiles())
                    if (meldTile.EqualsIgnoreColor(tile)) used++;

            if (status.MahjongSetData.LaiziIndicator.Rank > 0
                && status.MahjongSetData.LaiziIndicator.EqualsIgnoreColor(tile)) used++;

            return Mathf.Clamp(4 - used, 0, 4);
        }

        private void SetRemainingCount(Image tileImage, int count)
        {
            var label = tileImage.transform.Find("RemainingCount");
            if (label == null)
            {
                var labelObject = new GameObject("RemainingCount", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                labelObject.transform.SetParent(tileImage.transform, false);
                label = labelObject.transform;
                var rect = label.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, -40f);
                rect.sizeDelta = new Vector2(TileWidth, 80f);
                var text = label.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 54;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                // 剩余张数使用红色，和牌面信息区分开。
                text.color = Color.red;
                text.raycastTarget = false;
                var outline = labelObject.AddComponent<Outline>();
                outline.effectColor = Color.black;
                outline.effectDistance = new Vector2(1f, -1f);
            }
            // Apply to reused labels too, including labels created before a reload.
            label.gameObject.layer = tileImage.gameObject.layer;
            label.gameObject.SetActive(true);
            var countRect = label.GetComponent<RectTransform>();
            countRect.anchorMin = countRect.anchorMax = new Vector2(0.5f, 0f);
            countRect.pivot = new Vector2(0.5f, 0.5f);
            countRect.anchoredPosition = new Vector2(0f, -40f);
            countRect.sizeDelta = new Vector2(TileWidth, 80f);
            label.SetAsLastSibling();
            var countText = label.GetComponent<Text>();
            countText.enabled = true;
            countText.horizontalOverflow = HorizontalWrapMode.Overflow;
            countText.verticalOverflow = VerticalWrapMode.Overflow;
            countText.resizeTextForBestFit = false;
            countText.text = count.ToString();
            countText.fontSize = 54;
            countText.color = Color.red;
        }

        public void Show()
        {
            Background.gameObject.SetActive(true);
        }

        public void Close()
        {
            Background.gameObject.SetActive(false);
        }
    }
}

using System.Collections.Generic;
using Common.Interfaces;
using System.Linq;
using GamePlay.Client.Model;
using GamePlay.Client.View.Elements;
using GamePlay.Client.View.SubManagers;
using Mahjong.Model;
using UnityEngine;
using UnityEngine.UI;
using GamePlay.SinglePlayer;
using GamePlay.Client.Controller;
using GamePlay.Server.Controller;

namespace GamePlay.Client.View
{
    public class HandPanelManager : MonoBehaviour, IObserver<ClientRoundStatus>
    {
        private const float Width = 64;
        // Keep the newly drawn tile clearly separated from the concealed hand.
        private const float DefaultLastDrawX = 460;
        [SerializeField] private HandTile[] handTiles;
        [SerializeField] private HandTile lastDrawTile;
        public RectTransform LastDrawRect;
        public Image Zhenting;
        public DiscardHintManager DiscardHintManager;
        private string recommendationSignature;
        private SinglePlayerMode.HintDiscardAnalysis cachedHintAnalysis;
        private Text winningOutsHintText;
        private bool winningHintSuppressed;

        public void SetWinningHintSuppressed(bool suppressed)
        {
            winningHintSuppressed = suppressed;
            if (!suppressed) return;
            HideWinningOutsHint();
            foreach (var handTile in handTiles) handTile.SetRecommendationArrow(false);
            lastDrawTile.SetRecommendationArrow(false);
        }

        public IList<HandTile> HandTiles => handTiles;
        public HandTile LastDrawTile => lastDrawTile;

        private void Awake()
        {
            if (Zhenting != null) Zhenting.gameObject.SetActive(false);
            CreateWinningOutsHint();
        }

        /// <summary>在手牌上方创建“推荐弃牌及剩余可胡张数”文字，不依赖额外场景预制体。</summary>
        private void CreateWinningOutsHint()
        {
            var canvas = GetComponentInParent<Canvas>();
            var sourceText = canvas == null
                ? null
                : canvas.GetComponentsInChildren<Text>(true).FirstOrDefault(text => text.font != null);
            var hintObject = new GameObject("WinningOutsHint", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            // Keep the hint directly under the screen canvas. The hand panel
            // is anchored to the bottom edge and its small rect can place or
            // clip a child label outside the visible area.
            hintObject.transform.SetParent(canvas != null ? canvas.transform : transform, false);
            hintObject.transform.SetAsLastSibling();
            var hintCanvas = hintObject.GetComponent<Canvas>();
            hintCanvas.overrideSorting = true;
            hintCanvas.sortingOrder = canvas != null ? canvas.sortingOrder + 100 : 100;
            var rect = hintObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 185f);
            rect.sizeDelta = new Vector2(1600f, 80f);

            winningOutsHintText = hintObject.GetComponent<Text>();
            winningOutsHintText.font = sourceText != null
                ? sourceText.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            winningOutsHintText.fontSize = 30;
            winningOutsHintText.fontStyle = FontStyle.Bold;
            winningOutsHintText.alignment = TextAnchor.MiddleCenter;
            winningOutsHintText.color = new Color(1f, 0.9f, 0.25f);
            winningOutsHintText.raycastTarget = false;
            winningOutsHintText.horizontalOverflow = HorizontalWrapMode.Overflow;
            winningOutsHintText.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = hintObject.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);
            hintObject.SetActive(false);
        }

        private int ShowHandTiles(ClientRoundStatus status)
        {
            var tiles = status.LocalPlayerHandTiles;
            int length = tiles == null ? 0 : tiles.Count;
            var lastDraw = status.GetLastDraw(0);

            // A concealed hand waiting to discard contains 1 (mod 3) tiles,
            // with the newly drawn tile rendered separately. Some offline
            // synchronization paths already include that draw in the hand
            // snapshot (2 mod 3), which used to render a second card behind
            // the standalone draw. Skip exactly that duplicate for display.
            var duplicateIndex = -1;
            if (lastDraw != null && length % 3 == 2)
            {
                var drawn = (Tile)lastDraw;
                for (int i = length - 1; i >= 0; i--)
                {
                    if (tiles[i].EqualsConsiderColor(drawn))
                    {
                        duplicateIndex = i;
                        break;
                    }
                }
            }

            int visibleLength = Mathf.Min(length - (duplicateIndex >= 0 ? 1 : 0), handTiles.Length);
            int displayIndex = 0;
            for (int i = 0; i < length && displayIndex < visibleLength; i++)
            {
                if (i == duplicateIndex) continue;
                handTiles[displayIndex].gameObject.SetActive(true);
                handTiles[displayIndex].SetTile(tiles[i]);
                displayIndex++;
            }
            for (int i = visibleLength; i < handTiles.Length; i++)
            {
                handTiles[i].gameObject.SetActive(false);
            }
            return visibleLength;
        }

        private void ShowLastDraw(int count, ClientRoundStatus status)
        {
            var lastDraw = status.GetLastDraw(0);
            if (lastDraw == null)
                lastDrawTile.gameObject.SetActive(false);
            else
            {
                lastDrawTile.SetTile((Tile)lastDraw);
                lastDrawTile.gameObject.SetActive(true);
            }
            int diff = handTiles.Length - count;
            LastDrawRect.anchoredPosition = new Vector2(DefaultLastDrawX - diff * Width, 0);
        }

        private void ToggleForbiddens(ClientRoundStatus status)
        {
            // A newly entered discard state must not inherit disabled tiles
            // from an earlier candidate/forbidden selection. Restore every
            // tile first, then apply only the restrictions for this action.
            RemoveCandidates();
            if (status.ForbiddenTiles == null)
                return;
            var forbiddens = status.ForbiddenTiles;
            for (int i = 0; i < handTiles.Length; i++)
            {
                var instance = handTiles[i];
                if (forbiddens.Any(forbidden => forbidden.EqualsIgnoreColor(instance.Tile)))
                    instance.TurnOff();
            }
            if (lastDrawTile.interactable
                && forbiddens.Any(forbidden => forbidden.EqualsIgnoreColor(lastDrawTile.Tile)))
                lastDrawTile.TurnOff();
        }

        /// <summary>
        /// 每次轮到真人出牌时只标记一张推荐弃牌（包括摸牌、碰后和笑后补牌）；出牌后立即清除箭头。
        /// </summary>
        private void UpdateRecommendationArrow(ClientRoundStatus status)
        {
            for (int i = 0; i < handTiles.Length; i++)
                handTiles[i].SetRecommendationArrow(false);
            lastDrawTile.SetRecommendationArrow(false);
            HideWinningOutsHint();

            if (winningHintSuppressed || !SinglePlayerMode.Enabled || status.CurrentPlaceIndex != 0
                || status.LocalPlayerHandTiles == null)
            {
                recommendationSignature = null;
                cachedHintAnalysis = default(SinglePlayerMode.HintDiscardAnalysis);
                return;
            }

            var lastDraw = status.GetLastDraw(0);
            // 正常摸牌/补牌时有独立的 lastDraw；碰牌后虽然没有摸牌，但暗牌数量为 3n+2，
            // 同样表示现在必须由真人打出一张牌。
            var mustDiscard = lastDraw.HasValue || status.LocalPlayerHandTiles.Count % 3 == 2;
            if (!mustDiscard)
            {
                recommendationSignature = null;
                cachedHintAnalysis = default(SinglePlayerMode.HintDiscardAnalysis);
                return;
            }

            var totalConcealedBeforeDiscard = status.LocalPlayerHandTiles.Count
                + (lastDraw.HasValue ? 1 : 0);
            var openMeldCount = Mathf.Clamp((14 - totalConcealedBeforeDiscard) / 3, 0, 4);

            var visibleCounts = BuildVisibleTileCounts(status);
            var hintKnownCounts = BuildHintKnownCounts(visibleCounts);
            var forbiddenText = status.ForbiddenTiles == null
                ? string.Empty
                : string.Join(",", status.ForbiddenTiles.Select(Tile.GetIndex));
            var signature = string.Join(",", status.LocalPlayerHandTiles.Select(Tile.GetIndex))
                + "|" + (lastDraw.HasValue ? Tile.GetIndex(lastDraw.Value).ToString() : "-")
                + "|" + openMeldCount + "|" + forbiddenText
                + "|" + string.Join(",", hintKnownCounts);
            SinglePlayerMode.HintDiscardAnalysis analysis;
            if (signature == recommendationSignature)
            {
                analysis = cachedHintAnalysis;
            }
            else
            {
                analysis = SinglePlayerMode.AnalyzeHintDiscard(
                    status.LocalPlayerHandTiles,
                    lastDraw,
                    status.MahjongSetData.LaiziTile,
                    status.MahjongSetData.LaiziIndicator,
                    status.ForbiddenTiles,
                    openMeldCount,
                    hintKnownCounts);
                recommendationSignature = signature;
                cachedHintAnalysis = analysis;
            }
            var recommended = analysis.RecommendedDiscard;
            ShowWinningOutsHint(analysis);
            if (!recommended.HasValue) return;

            // 摸到的牌有独立的右侧牌位；如果推荐的是暗牌，则标记第一张相同的手牌。
            if (lastDraw.HasValue && lastDraw.Value.EqualsIgnoreColor(recommended.Value))
            {
                lastDrawTile.SetRecommendationArrow(true);
                return;
            }
            for (int i = 0; i < handTiles.Length; i++)
            {
                if (!handTiles[i].gameObject.activeSelf) continue;
                if (!handTiles[i].Tile.EqualsIgnoreColor(recommended.Value)) continue;
                handTiles[i].SetRecommendationArrow(true);
                return;
            }
        }

        private void ShowWinningOutsHint(SinglePlayerMode.HintDiscardAnalysis analysis)
        {
            if (winningOutsHintText == null) return;
            var discardText = analysis.RecommendedDiscard.HasValue
                ? $"建议打 {analysis.RecommendedDiscard.Value}　"
                : string.Empty;
            if (analysis.WinningTiles != null && analysis.WinningTiles.Length > 0)
            {
                var tiles = string.Join("、", analysis.WinningTiles
                    .Where(tile => tile.CopiesLeft >= 0)
                    .Select(tile => $"{tile.Tile}×{tile.CopiesLeft}"));
                winningOutsHintText.text = $"{discardText}可胡牌剩余：{tiles}（共{analysis.WinningOuts}张）";
                winningOutsHintText.color = new Color(0.35f, 1f, 0.55f);
            }
            else
            {
                winningOutsHintText.text = $"{discardText}当前可胡牌剩余：0张";
                winningOutsHintText.color = new Color(1f, 0.9f, 0.25f);
            }
            winningOutsHintText.gameObject.SetActive(true);
            winningOutsHintText.transform.SetAsLastSibling();
        }

        private void HideWinningOutsHint()
        {
            if (winningOutsHintText != null)
                winningOutsHintText.gameObject.SetActive(false);
        }

        /// <summary>碰牌或笑牌的桌面副露刷新后，使用最新公开信息重新计算推荐。</summary>
        public void RefreshRecommendation(ClientRoundStatus status)
        {
            if (status != null) UpdateRecommendationArrow(status);
        }

        private int[] BuildVisibleTileCounts(ClientRoundStatus status)
        {
            var counts = new int[34];
            System.Action<Tile> add = tile =>
            {
                var index = Tile.GetIndex(tile);
                if (index >= 0 && index < counts.Length) counts[index]++;
            };

            foreach (var tile in status.LocalPlayerHandTiles) add(tile);
            var lastDraw = status.GetLastDraw(0);
            if (lastDraw.HasValue) add(lastDraw.Value);

            // 被碰走的河牌已经进入副露区，不能在两个区域重复统计。
            if (status.Rivers != null)
            {
                foreach (var riverData in status.Rivers)
                {
                    if (riverData.River == null) continue;
                    foreach (var riverTile in riverData.River)
                        if (!riverTile.IsGone) add(riverTile.Tile);
                }
            }

            var table = ViewController.Instance == null ? null : ViewController.Instance.TableTilesManager;
            if (table != null)
                foreach (var tile in table.GetVisibleMeldTiles()) add(tile);

            // 翻开的癞子指示牌被锁死在牌墙尾部，也不能再摸到。
            if (status.MahjongSetData.LaiziIndicator.Rank > 0)
                add(status.MahjongSetData.LaiziIndicator);
            return counts;
        }

        /// <summary>
        /// 单机模式直接读取本地牌墙的实际剩余构成，再换算为已经不在牌墙中的数量。
        /// 因此胡牌张数不会把 AI 手牌里的牌误认为仍可摸到；取不到牌墙时才退回桌面可见估算。
        /// </summary>
        private int[] BuildHintKnownCounts(int[] visibleCounts)
        {
            var drawableCounts = SinglePlayerMode.Enabled && ServerBehaviour.Instance != null
                ? ServerBehaviour.Instance.DrawableTileCounts
                : null;
            if (drawableCounts == null || drawableCounts.Length < 27)
                return visibleCounts;

            var knownCounts = new int[visibleCounts.Length];
            for (int index = 0; index < knownCounts.Length; index++)
            {
                var drawable = index < drawableCounts.Length ? drawableCounts[index] : 0;
                knownCounts[index] = Mathf.Max(visibleCounts[index], 4 - drawable);
            }
            return knownCounts;
        }

        public void SetCandidates(IList<Tile> candidates)
        {
            for (int i = 0; i < handTiles.Length; i++)
            {
                SetCandidate(handTiles[i], candidates);
            }
            SetCandidate(lastDrawTile, candidates);
        }

        public void RemoveCandidates()
        {
            for (int i = 0; i < handTiles.Length; i++)
            {
                RemoveCandidate(handTiles[i]);
            }
            RemoveCandidate(lastDrawTile);
        }

        private void SetCandidate(HandTile instance, IList<Tile> candidates)
        {
            var tile = instance.Tile;
            if (candidates.Contains(tile))
                instance.TurnOn();
            else
                instance.TurnOff();
        }

        private void RemoveCandidate(HandTile instance)
        {
            instance.TurnOn();
        }

        public void LockTiles()
        {
            for (int i = 0; i < handTiles.Length; i++)
            {
                handTiles[i].SetLock(true);
            }
        }

        public void UnlockTiles()
        {
            for (int i = 0; i < handTiles.Length; i++)
            {
                handTiles[i].SetLock(false);
            }
        }

        public void Show()
        {
            gameObject.SetActive(true);
        }

        public void UpdateStatus(ClientRoundStatus subject)
        {
            if (subject == null) return;
            var count = ShowHandTiles(subject);
            ShowLastDraw(count, subject);
            ToggleForbiddens(subject);
            UpdateRecommendationArrow(subject);
            // Furiten is not used by the offline self-draw-only ruleset.
            if (Zhenting != null) Zhenting.gameObject.SetActive(false);
            if (subject.PossibleWaitingTiles == null)
                DiscardHintManager.Close();
        }
    }
}

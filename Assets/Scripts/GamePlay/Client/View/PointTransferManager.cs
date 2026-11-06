using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Common.Interfaces;
using Mahjong.Logic;
using GamePlay.Client.Model;
using GamePlay.Client.View.SubManagers;
using GamePlay.Client.Controller;
using static GamePlay.Client.View.SubManagers.PlayerPointTransferManager;
using GamePlay.Server.Model;
using System.Collections;
using Managers;

namespace GamePlay.Client.View
{
    /// <summary>
    /// 一局结束后的结算流程控制器。
    /// 单机模式分两阶段展示：先显示详细收支表，再显示四家分数增减动画。
    /// </summary>
    public class PointTransferManager : MonoBehaviour, IObserver<ClientRoundStatus>
    {
        public PlayerPointTransferManager[] SubManagers;
        public Button ConfirmButton;
        public CountDownController ConfirmCountDownController;
        private GameObject smilePanel;
        private GameObject settlementDetailsPanel;
        private bool changingStage;

        public void SetTransfer(ClientRoundStatus CurrentRoundStatus, int[] places, string[] playerNames,
            IList<PointTransfer> transfers, IList<SmileSettlement> smileSettlements,
            SettlementDetail[] settlementDetails, int[] laiziKongCounts, string reason,
            UnityAction callback)
        {
            gameObject.SetActive(true);
            changingStage = false;
            HidePlayerTransferPanels();
            if (smilePanel != null)
            {
                Destroy(smilePanel);
                smilePanel = null;
            }

            // 荒牌即使没有任何转账，也必须展示结算页，不能直接跳入下一局。
            var isExhaustiveDraw = reason == "荒牌结算" || reason == "牌堆摸完结算";
            var hasSettlementStage = settlementDetails != null
                && (settlementDetails.Length > 0 || isExhaustiveDraw);
            if (hasSettlementStage)
            {
                ShowSettlementDetails(playerNames, settlementDetails, laiziKongCounts,
                    smileSettlements, reason);
                ConfigureStageButton("查看分数变化", () => ShowPointChangeStage(
                    CurrentRoundStatus, places, playerNames, transfers, smileSettlements,
                    reason, callback));
                return;
            }

            ShowPointChangeStage(CurrentRoundStatus, places, playerNames, transfers,
                smileSettlements, reason, callback);
        }

        /// <summary>进入第二阶段，将每笔转账转换成各玩家头像旁的加减分动画。</summary>
        private void ShowPointChangeStage(ClientRoundStatus currentRoundStatus, int[] places,
            string[] playerNames, IList<PointTransfer> transfers,
            IList<SmileSettlement> smileSettlements, string reason, UnityAction callback)
        {
            if (changingStage) return;
            changingStage = true;
            ConfirmCountDownController.StopCountDown();
            if (settlementDetailsPanel != null)
            {
                Destroy(settlementDetailsPanel);
                settlementDetailsPanel = null;
            }

            var points = currentRoundStatus.Points;
            for (int placeIndex = 0; placeIndex < SubManagers.Length; placeIndex++)
            {
                int playerIndex = currentRoundStatus.GetPlayerIndex(placeIndex);
                if (!IsValidPlayer(playerIndex, currentRoundStatus.TotalPlayers)) continue;
                // 同一笔转账在付款方显示负数，在收款方显示正数。
                var localTransfers = new List<Transfer>();
                foreach (var transfer in transfers)
                {
                    if (transfer.From == playerIndex)
                    {
                        localTransfers.Add(new Transfer
                        {
                            Type = GetTransferType(transfer.From, transfer.To),
                            Amount = -transfer.Amount
                        });
                    }
                    if (transfer.To == playerIndex)
                    {
                        localTransfers.Add(new Transfer
                        {
                            Type = Type.None,
                            Amount = transfer.Amount
                        });
                    }
                }
                SubManagers[placeIndex].SetTransfers(points[placeIndex], localTransfers);
                var place = System.Array.Find(places, p => p == playerIndex);
                SubManagers[placeIndex].SetPlace(place);
            }
            ShowSmileSettlements(currentRoundStatus, smileSettlements);
            StartCoroutine(ShowPlacesAnimation(currentRoundStatus, places));
            ConfigureStageButton(string.IsNullOrEmpty(reason) ? "确认" : reason + "  确认", callback);
        }

        /// <summary>统一配置确认按钮和倒计时；手动确认与超时确认必须执行同一个回调。</summary>
        private void ConfigureStageButton(string label, UnityAction callback)
        {
            ConfirmButton.onClick.RemoveAllListeners();
            var buttonText = ConfirmButton.GetComponentInChildren<Text>();
            if (buttonText != null)
                buttonText.text = label;
            ConfirmButton.onClick.AddListener(() =>
            {
                ConfirmCountDownController.StopCountDown();
                callback();
            });
            // 基础等待 5 秒，再额外增加 10 秒；详细表和分数变化两个阶段都使用同样时长。
            ConfirmCountDownController.StartCountDown(MahjongConstants.SummaryPanelWaitingTime + 10, callback);
        }

        private void HidePlayerTransferPanels()
        {
            foreach (var manager in SubManagers)
                manager.gameObject.SetActive(false);
        }

        /// <summary>创建中文结算表，列出胡牌、点笑、回头笑、闷笑和喜钱的收入与支出。</summary>
        private void ShowSettlementDetails(string[] playerNames, SettlementDetail[] details,
            int[] laiziKongCounts, IList<SmileSettlement> smileSettlements, string reason)
        {
            if (settlementDetailsPanel != null) Destroy(settlementDetailsPanel);
            var isExhaustiveDraw = reason == "荒牌结算" || reason == "牌堆摸完结算";
            if (details == null || (details.Length == 0 && !isExhaustiveDraw)) return;
            var buttonText = ConfirmButton == null
                ? null
                : ConfirmButton.GetComponentInChildren<Text>(true);
            var font = buttonText != null && buttonText.font != null
                ? buttonText.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            settlementDetailsPanel = PointSummaryPanelManager.CreateSettlementTable(transform, font,
                playerNames, details, laiziKongCounts, smileSettlements,
                isExhaustiveDraw ? "荒牌结算明细" : "本局结算明细");
        }

        /// <summary>把笑牌牌面及类型标记显示到实际执行笑牌的玩家头像附近。</summary>
        private void ShowSmileSettlements(ClientRoundStatus status, IList<SmileSettlement> settlements)
        {
            if (smilePanel != null) Destroy(smilePanel);
            if (settlements == null || settlements.Count == 0) return;

            smilePanel = new GameObject("SmileSettlementTiles", typeof(RectTransform));
            smilePanel.transform.SetParent(transform, false);
            var panelRect = smilePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            smilePanel.transform.SetAsLastSibling();

            var buttonText = ConfirmButton.GetComponentInChildren<Text>(true);
            var font = buttonText == null ? null : buttonText.font;
            if (font == null)
            {
                var existingTexts = GetComponentsInChildren<Text>(true);
                foreach (var existingText in existingTexts)
                {
                    if (existingText.font == null) continue;
                    font = existingText.font;
                    break;
                }
            }
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var totalsByPlayer = new Dictionary<int, int>();
            var shownByPlayer = new Dictionary<int, int>();
            foreach (var settlement in settlements)
            {
                if (!totalsByPlayer.ContainsKey(settlement.PlayerIndex))
                    totalsByPlayer[settlement.PlayerIndex] = 0;
                totalsByPlayer[settlement.PlayerIndex]++;
            }
            var canvas = GetComponentInParent<Canvas>();
            var eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            for (int i = 0; i < settlements.Count; i++)
            {
                var settlement = settlements[i];
                if (!IsValidPlayer(settlement.PlayerIndex, status.TotalPlayers)) continue;
                var placeIndex = status.GetPlaceIndex(settlement.PlayerIndex);
                if (placeIndex < 0 || placeIndex >= SubManagers.Length) continue;

                var targetRect = SubManagers[placeIndex].GetComponent<RectTransform>();
                var screenPoint = RectTransformUtility.WorldToScreenPoint(
                    eventCamera, targetRect.TransformPoint(targetRect.rect.center));
                Vector2 localPoint;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        panelRect, screenPoint, eventCamera, out localPoint))
                    continue;

                var shown = shownByPlayer.ContainsKey(settlement.PlayerIndex)
                    ? shownByPlayer[settlement.PlayerIndex]
                    : 0;
                shownByPlayer[settlement.PlayerIndex] = shown + 1;
                var total = totalsByPlayer[settlement.PlayerIndex];
                var horizontalOffset = (shown - (total - 1) * 0.5f) * 62f;

                var item = new GameObject("Smile_" + settlement.Type, typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                item.transform.SetParent(smilePanel.transform, false);
                var itemRect = item.GetComponent<RectTransform>();
                itemRect.anchorMin = itemRect.anchorMax = new Vector2(0.5f, 0.5f);
                itemRect.anchoredPosition = localPoint + new Vector2(horizontalOffset, 3f);
                var hasSource = IsValidPlayer(settlement.SourcePlayerIndex, status.TotalPlayers);
                itemRect.sizeDelta = new Vector2(hasSource ? 92f : 58f, hasSource ? 108f : 92f);
                item.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

                var tileObject = new GameObject("Tile", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                tileObject.transform.SetParent(item.transform, false);
                var tileRect = tileObject.GetComponent<RectTransform>();
                tileRect.anchorMin = tileRect.anchorMax = new Vector2(0.5f, 0.5f);
                tileRect.anchoredPosition = new Vector2(0f, 10f);
                tileRect.sizeDelta = new Vector2(34f, 53f);
                tileObject.GetComponent<Image>().sprite = ResourceManager.Instance.GetTileSprite(settlement.Tile);

                var labelObject = new GameObject("Type", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text));
                labelObject.transform.SetParent(item.transform, false);
                var labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
                labelRect.anchoredPosition = new Vector2(0f, hasSource ? -35f : -30f);
                labelRect.sizeDelta = new Vector2(hasSource ? 90f : 58f, hasSource ? 38f : 24f);
                var label = labelObject.GetComponent<Text>();
                label.font = font;
                label.fontSize = hasSource ? 13 : 17;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.yellow;
                if (hasSource)
                {
                    var sourcePlaceIndex = status.GetPlaceIndex(settlement.SourcePlayerIndex);
                    var sourceName = sourcePlaceIndex >= 0
                        ? status.GetPlayerName(sourcePlaceIndex)
                        : $"玩家{settlement.SourcePlayerIndex + 1}";
                    label.text = $"{settlement.Type}\n{sourceName} 打出";
                }
                else
                {
                    label.text = settlement.Type;
                }
            }
        }

        private const float Gap = 1f;
        private WaitForSeconds waiting = new WaitForSeconds(Gap);

        private IEnumerator ShowPlacesAnimation(ClientRoundStatus status, int[] places)
        {
            for (int i = 0; i < places.Length; i++)
            {
                var playerIndex = places[i];
                var placeIndex = status.GetPlaceIndex(playerIndex);
                SubManagers[placeIndex].ShowPlace();
                yield return waiting;
            }
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

        /// <summary>按玩家座次计算付款方向，仅用于选择分数动画的进入方向。</summary>
        private static Type GetTransferType(int from, int to)
        {
            if (from < 0) return Type.None;
            int diff = to - from;
            if (diff < 0) diff += 4;
            switch (diff)
            {
                case 1:
                    return Type.Right;
                case 2:
                    return Type.Straight;
                case 3:
                    return Type.Left;
                default:
                    return Type.None;
            }
        }

        private void OnDisable()
        {
            changingStage = false;
            ConfirmCountDownController.StopCountDown();
            if (smilePanel != null)
            {
                Destroy(smilePanel);
                smilePanel = null;
            }
            if (settlementDetailsPanel != null)
            {
                Destroy(settlementDetailsPanel);
                settlementDetailsPanel = null;
            }
            HidePlayerTransferPanels();
        }

        private bool IsValidPlayer(int index, int totalPlayers)
        {
            return index >= 0 && index < totalPlayers;
        }

        public void UpdateStatus(ClientRoundStatus subject)
        {
            if (subject == null) return;
            for (int placeIndex = 0; placeIndex < SubManagers.Length; placeIndex++)
            {
                int playerIndex = subject.GetPlayerIndex(placeIndex);
                if (IsValidPlayer(playerIndex, subject.TotalPlayers))
                {
                    SubManagers[placeIndex].gameObject.SetActive(true);
                    SubManagers[placeIndex].PlayerName = subject.GetPlayerName(placeIndex);
                }
                else
                    SubManagers[placeIndex].gameObject.SetActive(false);
            }
        }
    }
}

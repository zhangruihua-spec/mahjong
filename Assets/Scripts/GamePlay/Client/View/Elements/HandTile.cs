using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;
using Mahjong.Model;
using GamePlay.Client.Model;
using GamePlay.Client.View.SubManagers;
using GamePlay.Client.Controller;
using Common.Interfaces;
using Managers;

namespace GamePlay.Client.View.Elements
{
    [RequireComponent(typeof(Image))]
    public class HandTile : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IObserver<ClientRoundStatus>
    {
        public bool IsLastDraw;
        public bool interactable = true;
        public Tile Tile => tile;
        private Image image;
        private RectTransform rect;
        private Tile tile;
        private bool locked = false;
        private IDictionary<Tile, IList<Tile>> waitingTiles;
        private ClientRoundStatus currentStatus;
        private DiscardHintManager hintManager;
        private Vector2 dragStartScreenPosition;
        private Vector2 dragStartAnchoredPosition;
        private bool dragging;
        private int suppressClickUntilFrame = -1;
        private GameObject recommendationArrow;
        private RectTransform recommendationArrowRect;
        private const float DragDiscardDistance = 70f;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();
            image = GetComponent<Image>();
            hintManager = ViewController.Instance.HandPanelManager.DiscardHintManager;
        }

        private void OnEnable()
        {
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 0);
        }

        private void OnDisable()
        {
            // Stop pending tweens before Unity destroys/disables the UI target.
            // Otherwise DOTween can try to evaluate Image.color on an invalid
            // target and report: "Tween startup failed (NULL target/property)".
            if (image != null) image.DOKill();
            if (rect != null) rect.DOKill();
            SetRecommendationArrow(false);
        }

        public void SetTile(Tile tile)
        {
            gameObject.SetActive(true);
            this.tile = tile;
            var sprite = ResourceManager.Instance.GetTileSprite(tile);
            if (image == null) image = GetComponent<Image>();
            image.sprite = sprite;
        }

        /// <summary>
        /// 显示或隐藏“推荐出牌”箭头。箭头作为子物体创建，不参与射线检测，
        /// 因此不会影响原有点击和拖动出牌操作。
        /// </summary>
        public void SetRecommendationArrow(bool visible)
        {
            if (!visible)
            {
                if (recommendationArrowRect != null)
                {
                    recommendationArrowRect.DOKill();
                    recommendationArrowRect.anchoredPosition = new Vector2(0f, 72f);
                }
                if (recommendationArrow != null) recommendationArrow.SetActive(false);
                return;
            }

            EnsureRecommendationArrow();
            recommendationArrow.SetActive(true);
            recommendationArrowRect.DOKill();
            recommendationArrowRect.anchoredPosition = new Vector2(0f, 72f);
            recommendationArrowRect.DOAnchorPosY(60f, 0.42f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        private void EnsureRecommendationArrow()
        {
            if (recommendationArrow != null) return;
            recommendationArrow = new GameObject("RecommendationArrow", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            recommendationArrow.transform.SetParent(transform, false);
            recommendationArrowRect = recommendationArrow.GetComponent<RectTransform>();
            recommendationArrowRect.anchorMin = recommendationArrowRect.anchorMax = new Vector2(0.5f, 0.5f);
            recommendationArrowRect.pivot = new Vector2(0.5f, 0.5f);
            recommendationArrowRect.sizeDelta = new Vector2(58f, 32f);
            recommendationArrowRect.anchoredPosition = new Vector2(0f, 72f);
            var label = recommendationArrow.GetComponent<Text>();
            label.text = "▼";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.15f, 0.75f, 1f, 1f);
            label.raycastTarget = false;
            var outline = recommendationArrow.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0.15f, 0.35f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            recommendationArrow.SetActive(false);
        }

        private void OnDestroy()
        {
            if (recommendationArrowRect != null) recommendationArrowRect.DOKill();
        }

        public void TurnOff()
        {
            interactable = false;
            if (rect != null) rect.DOAnchorPosY(0, AnimationDuration);
            if (image != null) image.DOColor(Color.gray, AnimationDuration);
        }

        public void TurnOn()
        {
            interactable = true;
            if (image != null) image.DOColor(Color.white, AnimationDuration);
        }

        public void SetLock(bool locked)
        {
            this.locked = locked;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Time.frameCount <= suppressClickUntilFrame) return;
            if (!interactable || locked) return;
            ClientBehaviour.Instance.OnDiscardTile(tile, IsLastDraw);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!interactable || locked) return;
            dragging = true;
            rect.DOKill();
            dragStartScreenPosition = eventData.position;
            dragStartAnchoredPosition = rect.anchoredPosition;
            rect.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging) return;
            var parentRect = rect.parent as RectTransform;
            Vector2 localPoint;
            if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, eventData.position, eventData.pressEventCamera, out localPoint))
            {
                rect.anchoredPosition = localPoint;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging) return;
            dragging = false;
            suppressClickUntilFrame = Time.frameCount + 1;

            // The table is above the player's hand. Releasing at least this
            // far upward counts as a discard; shorter drags simply snap back.
            var discard = eventData.position.y - dragStartScreenPosition.y >= DragDiscardDistance;
            rect.anchoredPosition = dragStartAnchoredPosition;
            if (discard && interactable && !locked)
                ClientBehaviour.Instance.OnDiscardTile(tile, IsLastDraw);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!interactable) return;
            rect.DOAnchorPosY(20, AnimationDuration);
            if (waitingTiles != null && waitingTiles.ContainsKey(Tile))
            {
                var tiles = waitingTiles[Tile];
                hintManager.SetWaitingTiles(tiles, currentStatus);
                hintManager.Show();
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!interactable) return;
            if (dragging) return;
            rect.DOAnchorPosY(0, AnimationDuration);
            hintManager.Close();
        }

        public void UpdateStatus(ClientRoundStatus subject)
        {
            currentStatus = subject;
            waitingTiles = subject.PossibleWaitingTiles;
        }

        private const float AnimationDuration = 0.5f;
    }
}

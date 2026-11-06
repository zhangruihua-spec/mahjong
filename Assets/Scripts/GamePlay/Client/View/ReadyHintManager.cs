using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Common.Interfaces;
using Mahjong.Model;
using GamePlay.Client.Model;
using GamePlay.Client.View.SubManagers;

namespace GamePlay.Client.View
{
    public class ReadyHintManager : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IObserver<ClientRoundStatus>
    {
        public Image ReadyImage;
        public DiscardHintManager DiscardHintManager;
        private IList<Tile> waitingList;
        private ClientRoundStatus currentStatus;
        private bool isShowing;

        public void OnPointerEnter(PointerEventData eventData)
        {
            ShowWaitingTiles();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!isShowing) DiscardHintManager.Close();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (isShowing)
            {
                isShowing = false;
                DiscardHintManager.Close();
            }
            else
            {
                isShowing = true;
                ShowWaitingTiles();
            }
        }

        private void ShowWaitingTiles()
        {
            if (waitingList == null || waitingList.Count == 0) return;
            DiscardHintManager.SetWaitingTiles(waitingList, currentStatus);
            DiscardHintManager.Show();
        }

        public void UpdateStatus(ClientRoundStatus subject)
        {
            if (subject == null) return;
            currentStatus = subject;
            waitingList = subject.WaitingTiles;
            if (waitingList != null && waitingList.Count > 0)
            {
                ReadyImage.gameObject.SetActive(true);
                if (isShowing) ShowWaitingTiles();
            }
            else
            {
                ReadyImage.gameObject.SetActive(false);
                isShowing = false;
                DiscardHintManager.Close();
            }
        }
    }
}

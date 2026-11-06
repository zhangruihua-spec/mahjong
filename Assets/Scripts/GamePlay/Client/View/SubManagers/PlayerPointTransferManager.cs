using System.Collections;
using System.Collections.Generic;
using GamePlay.Client.Controller;
using UnityEngine;
using UnityEngine.UI;

namespace GamePlay.Client.View.SubManagers
{
    public class PlayerPointTransferManager : MonoBehaviour
    {
        [SerializeField] private Text PlayerNameText;
        [SerializeField] private NumberPanelController PointController;
        [SerializeField] private NumberChangeController ChangeController;
        [SerializeField] private PlayerPlaceController PlaceController;
        [SerializeField] private Image LeftArrow;
        [SerializeField] private Image StraightArrow;
        [SerializeField] private Image RightArrow;

        [HideInInspector] public string PlayerName;

        private void Update()
        {
            PlayerNameText.text = PlayerName;
        }

        public void SetTransfers(int point, IList<Transfer> transfers)
        {
            Debug.Log($"{name} is setting point to {point}, transfers are {string.Join(";", transfers)}");
            gameObject.SetActive(true);
            HideTransferArrows();
            int totalTransfer = 0;
            foreach (var transfer in transfers)
            {
                totalTransfer += transfer.Amount;
            }
            StartCoroutine(SetAnimation(point, totalTransfer));
        }

        public void SetPlace(int place)
        {
            PlaceController.SetPlace(place);
        }

        public void ShowPlace()
        {
            PlaceController.Show();
        }

        private void HideTransferArrows()
        {
            // The three full-screen arrows overlap when several players pay
            // at once and obscure both the table and the settlement details.
            // The persistent signed point change below carries the same
            // information much more clearly.
            if (LeftArrow != null) LeftArrow.gameObject.SetActive(false);
            if (StraightArrow != null) StraightArrow.gameObject.SetActive(false);
            if (RightArrow != null) RightArrow.gameObject.SetActive(false);
        }

        private const float Duration = 1.5f;
        private const int Ticks = 50;

        private IEnumerator SetAnimation(int point, int totalTransfer)
        {
            var waiting = new WaitForSeconds(Duration / Ticks);
            int gap = totalTransfer / Ticks;
            int currentPoint = point - totalTransfer;
            for (int tick = 0; tick <= Ticks; tick++)
            {
                PointController.SetNumber(currentPoint);
                ChangeController.SetNumber(point - currentPoint);
                currentPoint += gap;
                yield return waiting;
            }
            PointController.SetNumber(point);
            // Keep the final net change visible for the lifetime of the
            // settlement panel. Previously it disappeared after 1.5 seconds,
            // leaving only a confusing collection of direction arrows.
            if (totalTransfer == 0)
                ChangeController.Close();
            else
                ChangeController.SetNumber(totalTransfer);
        }

        private void OnDisable()
        {
            HideTransferArrows();
            ChangeController.Close();
        }

        public enum Type
        {
            Left, Straight, Right, None
        }

        public struct Transfer
        {
            public Type Type;
            public int Amount;

            public override string ToString()
            {
                return $"Type: {Type}, Amount: {Amount}";
            }
        }
    }
}

using System.Linq;
using GamePlay.Client.Controller;
using GamePlay.Server.Model;
using UnityEngine;
using UnityEngine.UI;

namespace GamePlay.Client.View
{
    public class InTurnPanelManager : MonoBehaviour
    {
        public Button TsumoButton;
        public Button RichiButton;
        public Button DrawButton;
        public Button KongButton;
        public Button BeiButton;
        public Button SkipButton;
        public Button BackButton;

        public void SetOperations(InTurnOperation[] operations)
        {
            // A replacement draw can enter this method synchronously while
            // the previous laizi-kong click is still unwinding. Reset the old
            // panel first so the same operation button can be shown again.
            Close();
            if (operations.All(op => op.Type == InTurnOperationType.Discard))
            {
                Debug.Log("There no other operations than discard that can be taken.");
                ClientBehaviour.Instance.OnInTurnSkipButtonClicked();
                return;
            }
            SkipButton.onClick.RemoveAllListeners();
            SkipButton.gameObject.SetActive(true);
            SkipButton.onClick.AddListener(ClientBehaviour.Instance.OnInTurnSkipButtonClicked);
            BackButton.onClick.RemoveAllListeners();
            BackButton.gameObject.SetActive(false);
            if (operations.Any(op => op.Type == InTurnOperationType.Tsumo))
            {
                TsumoButton.onClick.RemoveAllListeners();
                TsumoButton.gameObject.SetActive(true);
                var tsumoOperation = System.Array.Find(operations, op => op.Type == InTurnOperationType.Tsumo);
                TsumoButton.onClick.AddListener(() => ClientBehaviour.Instance.OnTsumoButtonClicked(tsumoOperation));
            }
            if (operations.Any(op => op.Type == InTurnOperationType.Richi))
            {
                RichiButton.onClick.RemoveAllListeners();
                RichiButton.gameObject.SetActive(true);
                var richiOperation = System.Array.Find(operations, op => op.Type == InTurnOperationType.Richi);
                RichiButton.onClick.AddListener(() =>
                {
                    ClientBehaviour.Instance.OnRichiButtonClicked(richiOperation);
                    Close();
                    BackButton.gameObject.SetActive(true);
                    BackButton.onClick.AddListener(() => ClientBehaviour.Instance.OnInTurnBackButtonClicked(operations));
                });
            }
            if (operations.Any(op => op.Type == InTurnOperationType.Kong))
            {
                KongButton.onClick.RemoveAllListeners();
                KongButton.gameObject.SetActive(true);
                SetOperationLabel(KongButton, "杠\n(四张同牌)", new Color(0.35f, 0.8f, 1f));
                var kongOperations = System.Array.FindAll(operations, op => op.Type == InTurnOperationType.Kong);
                KongButton.onClick.AddListener(() =>
                {
                    ClientBehaviour.Instance.OnInTurnKongButtonClicked(kongOperations);
                    Close();
                    BackButton.onClick.AddListener(() => ClientBehaviour.Instance.OnInTurnBackButtonClicked(operations));
                });
            }
            if (operations.Any(op => op.Type == InTurnOperationType.RoundDraw))
            {
                DrawButton.onClick.RemoveAllListeners();
                DrawButton.gameObject.SetActive(true);
                var drawOperation = System.Array.Find(operations, op => op.Type == InTurnOperationType.RoundDraw);
                DrawButton.onClick.AddListener(() => ClientBehaviour.Instance.OnInTurnButtonClicked(drawOperation));
            }
            if (operations.Any(op => op.Type == InTurnOperationType.Bei))
            {
                BeiButton.onClick.RemoveAllListeners();
                BeiButton.gameObject.SetActive(true);
                SetOperationLabel(BeiButton, "杠癞子", new Color(1f, 0.68f, 0.18f));
                var beiOperation = System.Array.Find(operations, op => op.Type == InTurnOperationType.Bei);
                BeiButton.onClick.AddListener(() =>
                {
                    // 真人点击“杠赖子”时立即播放，避免状态快速切换吞掉音效。
                    GameSoundManager.Play("gang");
                    ClientBehaviour.Instance.OnInTurnButtonClicked(beiOperation);
                });
            }
        }

        public void ShowBackButton()
        {
            BackButton.gameObject.SetActive(true);
        }

        private static void SetOperationLabel(Button button, string label, Color color)
        {
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null) text.text = label;
            var image = button.GetComponent<Image>();
            if (image != null) image.color = color;
        }

        public void Close()
        {
            TsumoButton.gameObject.SetActive(false);
            RichiButton.gameObject.SetActive(false);
            DrawButton.gameObject.SetActive(false);
            KongButton.gameObject.SetActive(false);
            BeiButton.gameObject.SetActive(false);
            SkipButton.gameObject.SetActive(false);
            BackButton.gameObject.SetActive(false);
        }
    }
}

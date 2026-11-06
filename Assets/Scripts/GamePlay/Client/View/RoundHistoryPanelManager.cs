using System.Linq;
using System.Text;
using GamePlay.Server.Controller;
using UnityEngine;
using UnityEngine.UI;

namespace GamePlay.Client.View
{
    /// <summary>显示当前单局的服务端行动记录。把 Button、Panel 和 Text 拖到 Inspector 即可使用。</summary>
    public class RoundHistoryPanelManager : MonoBehaviour
    {
        public Button ShowButton;
        public GameObject Panel;
        public Text Content;
        public Button CloseButton;

        private void Awake()
        {
            if (ShowButton != null) ShowButton.onClick.AddListener(ShowHistory);
            if (CloseButton != null) CloseButton.onClick.AddListener(HideHistory);
            if (Panel != null) Panel.SetActive(false);
        }

        public void ShowHistory()
        {
            if (Content == null || ServerBehaviour.Instance == null
                || ServerBehaviour.Instance.CurrentRoundStatus == null) return;
            var history = ServerBehaviour.Instance.CurrentRoundStatus.History;
            var builder = new StringBuilder();
            foreach (var item in history)
            {
                builder.Append(item.Sequence).Append(". ")
                    .Append(item.PlayerName).Append(" ")
                    .Append(item.Action);
                if (!string.IsNullOrEmpty(item.Tile) && item.Tile != "无")
                    builder.Append(" ").Append(item.Tile);
                if (!string.IsNullOrEmpty(item.Detail))
                    builder.Append("（").Append(item.Detail).Append("）");
                builder.AppendLine();
            }
            Content.text = builder.Length == 0 ? "本局暂无记录" : builder.ToString();
            if (Panel != null) Panel.SetActive(true);
        }

        public void HideHistory()
        {
            if (Panel != null) Panel.SetActive(false);
        }
    }
}

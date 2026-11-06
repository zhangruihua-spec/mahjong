using Mahjong.Model;
using Managers;
using UnityEngine;

namespace GamePlay.Client.View
{
    [RequireComponent(typeof(MeshRenderer))]
    public class TileInstance : MonoBehaviour
    {
        private static readonly Color LaiziKongColor = new Color(1f, 0.72f, 0.18f, 1f);
        private static readonly Color LaiziKongEmission = new Color(0.42f, 0.2f, 0.015f, 1f);
        public Tile Tile;
        public Canvas Canvas;
        private MeshRenderer meshRenderer;
        private bool isLaiziKongHighlighted;

        private void OnEnable()
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        public void SetTile(Tile tile)
        {
            if (tile.Rank == 0)
            {
                gameObject.SetActive(false);
                return;
            }
            gameObject.SetActive(true);
            Tile = tile;
            var material = meshRenderer.material;
            material.color = isLaiziKongHighlighted ? LaiziKongColor : Color.white;
            material.mainTexture = ResourceManager.Instance?.GetTileTexture(tile);
        }

        public void SetLaiziKongHighlight(bool highlighted)
        {
            isLaiziKongHighlighted = highlighted;
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            var material = meshRenderer.material;
            material.color = highlighted ? LaiziKongColor : Color.white;
            if (!material.HasProperty("_EmissionColor")) return;
            if (highlighted)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", LaiziKongEmission);
            }
            else
            {
                material.SetColor("_EmissionColor", Color.black);
                material.DisableKeyword("_EMISSION");
            }
        }

        public void Shine()
        {
            Canvas.gameObject.SetActive(true);
        }

        public void ShineOff()
        {
            Canvas.gameObject.SetActive(false);
        }
    }
}

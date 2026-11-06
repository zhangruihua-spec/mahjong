using System;
using Mahjong.Logic;
using Mahjong.Model;
using UnityEngine;

namespace GamePlay.Client.View
{
    public class MeldInstance : MonoBehaviour
    {
        public OpenMeld OpenMeld;
        private TileInstance[] instances;
        private GameObject pointSmileSourceArrow;
        private Mesh pointSmileArrowMesh;
        private Material pointSmileArrowMaterial;

        private void OnEnable()
        {
            instances = new TileInstance[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
            {
                var t = transform.GetChild(i);
                instances[i] = t.GetComponent<TileInstance>();
            }
        }

        public void SetMeld(OpenMeld meld, Vector3 sourceDirection)
        {
            OpenMeld = meld;
            if (meld.Side == MeldSide.Self)
                SelfKong();
            else if (meld.IsAdded)
                AddedKong();
            else
                OtherMeld();

            UpdatePointSmileSourceArrow(sourceDirection);
        }

        /// <summary>
        /// 只为点笑（别人打出第四张形成的明杠）显示来源箭头。
        /// 闷笑、回头笑、普通碰都不会显示，避免桌面提示过于杂乱。
        /// </summary>
        private void UpdatePointSmileSourceArrow(Vector3 sourceDirection)
        {
            bool isPointSmile = OpenMeld.IsKong && !OpenMeld.IsAdded &&
                                OpenMeld.Side != MeldSide.Self;
            if (!isPointSmile || sourceDirection.sqrMagnitude < 0.000001f)
            {
                if (pointSmileSourceArrow != null)
                    pointSmileSourceArrow.SetActive(false);
                return;
            }

            EnsurePointSmileSourceArrow();

            // 取当前可见牌的中心，箭头放在牌面上方一点，不遮住牌面内容。
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool foundVisibleTile = false;
            foreach (var instance in instances)
            {
                if (instance == null || !instance.gameObject.activeSelf) continue;
                var position = instance.transform.localPosition;
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
                foundVisibleTile = true;
            }

            var center = foundVisibleTile ? (min + max) * 0.5f : Vector3.zero;
            // 抬到牌组上方，保持小尺寸，避免箭头遮挡牌面和桌面。
            center.y = 0.075f;
            pointSmileSourceArrow.transform.localPosition = center;
            pointSmileSourceArrow.transform.localScale = Vector3.one;
            pointSmileSourceArrow.transform.localRotation =
                Quaternion.LookRotation(sourceDirection.normalized, Vector3.up);
            pointSmileSourceArrow.SetActive(true);
        }

        /// <summary>动态生成一个小型青蓝色箭头，不需要改动三套点笑 Prefab。</summary>
        private void EnsurePointSmileSourceArrow()
        {
            if (pointSmileSourceArrow != null) return;

            pointSmileSourceArrow = new GameObject("PointSmileSourceArrow");
            pointSmileSourceArrow.transform.SetParent(transform, false);

            var meshFilter = pointSmileSourceArrow.AddComponent<MeshFilter>();
            var meshRenderer = pointSmileSourceArrow.AddComponent<MeshRenderer>();

            // 箭头默认沿本地 +Z 方向，之后再旋转到出牌者所在方向。
            pointSmileArrowMesh = new Mesh { name = "PointSmileSourceArrowMesh" };
            pointSmileArrowMesh.vertices = new[]
            {
                new Vector3(-0.0035f, 0, -0.025f),
                new Vector3( 0.0035f, 0, -0.025f),
                new Vector3( 0.0035f, 0,  0.006f),
                new Vector3( 0.0110f, 0,  0.006f),
                new Vector3( 0.0000f, 0,  0.029f),
                new Vector3(-0.0110f, 0,  0.006f),
                new Vector3(-0.0035f, 0,  0.006f)
            };
            pointSmileArrowMesh.triangles = new[]
            {
                0, 2, 1,
                0, 6, 2,
                5, 4, 3
            };
            pointSmileArrowMesh.RecalculateNormals();
            pointSmileArrowMesh.RecalculateBounds();
            meshFilter.sharedMesh = pointSmileArrowMesh;

            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            pointSmileArrowMaterial = new Material(shader)
            {
                name = "PointSmileSourceArrowMaterial",
                color = new Color(1.00f, 0.16f, 0.03f, 1.00f)
            };
            meshRenderer.sharedMaterial = pointSmileArrowMaterial;
        }

        private void OnDestroy()
        {
            if (pointSmileArrowMesh != null) Destroy(pointSmileArrowMesh);
            if (pointSmileArrowMaterial != null) Destroy(pointSmileArrowMaterial);
        }

        private void SelfKong()
        {
            for (int i = 0; i < OpenMeld.Tiles.Length; i++)
            {
                instances[i].SetTile(OpenMeld.Tiles[i]);
            }
            // A laizi-preceding concealed smile uses the three playable
            // copies; the fourth copy is the locked indicator. Hide the
            // unused fourth prefab slot instead of showing a phantom tile.
            for (int i = OpenMeld.Tiles.Length; i < instances.Length; i++)
                instances[i].gameObject.SetActive(false);
        }

        private void OtherMeld()
        {
            var index = Array.FindIndex(OpenMeld.Tiles, tile => tile.EqualsConsiderColor(OpenMeld.Tile));
            int tileCount = 1;
            for (int i = 0; i < OpenMeld.Tiles.Length; i++)
            {
                if (i == index) continue;
                instances[tileCount++].SetTile(OpenMeld.Tiles[i]);
            }
            instances[0].SetTile(OpenMeld.Tile);
            for (int i = OpenMeld.Tiles.Length; i < instances.Length; i++)
            {
                instances[i].gameObject.SetActive(false);
            }
        }

        public void AddedKong()
        {
            var index1 = Array.FindIndex(OpenMeld.Tiles, tile => tile.EqualsConsiderColor(OpenMeld.Tile));
            var index2 = Array.FindIndex(OpenMeld.Tiles, tile => tile.EqualsConsiderColor(OpenMeld.Extra));
            int tileCount = 1;
            for (int i = 0; i < OpenMeld.Tiles.Length; i++)
            {
                if (i == index1 || i == index2) continue;
                instances[tileCount++].SetTile(OpenMeld.Tiles[i]);
            }
            instances[0].SetTile(OpenMeld.Tile);
            instances[3].SetTile(OpenMeld.Extra);
        }

        public float MeldWidth
        {
            get
            {
                if (OpenMeld.Side == MeldSide.Self)
                    return OpenMeld.IsLaiziPreviousKong
                        ? 3 * MahjongConstants.TileWidth
                        : 4 * MahjongConstants.TileWidth;
                if (OpenMeld.IsKong && !OpenMeld.IsAdded)
                    return 3 * MahjongConstants.TileWidth + MahjongConstants.TileHeight;
                return 2 * MahjongConstants.TileWidth + MahjongConstants.TileHeight;
            }
        }
    }
}

using System.Collections.Generic;
using Mahjong.Model;
using UnityEngine;
using Utils;

namespace GamePlay.Client.View
{
    public class OpenMeldManager : MonoBehaviour
    {
        private const float Gap = 0.005f;
        public GameObject[] ChowPongPrefabs;
        public GameObject[] OpenKongPrefabs;
        public GameObject SelfKongPrefabs;
        private float offset = 0;
        private readonly List<OpenMeld> currentMelds = new List<OpenMeld>();
        private int seatIndex = -1;
        private OpenMeldManager[] tableManagers;

        /// <summary>当前桌面已经公开的碰牌和笑牌，供出牌提示统计真实剩余牌数。</summary>
        public IList<OpenMeld> CurrentMelds => currentMelds.AsReadOnly();

        /// <summary>记录当前副露属于哪个座位，并保存四家的桌面位置。</summary>
        public void ConfigureSeat(int ownerSeatIndex, OpenMeldManager[] managers)
        {
            seatIndex = ownerSeatIndex;
            tableManagers = managers;
        }

        public void SetMelds(IList<OpenMeld> melds)
        {
            transform.DestroyAllChildren();
            offset = 0;
            currentMelds.Clear();
            if (melds == null) return;
            foreach (var meld in melds)
            {
                AddMeldVisual(meld);
                currentMelds.Add(meld);
            }
        }

        public void AddMeld(OpenMeld meld)
        {
            AddMeldVisual(meld);
            currentMelds.Add(meld);
        }

        private void AddMeldVisual(OpenMeld meld)
        {
            var prefab = SelectPrefab(meld);
            var meldObj = Instantiate(prefab, transform);
            var meldInstance = meldObj.GetComponent<MeldInstance>();
            meldObj.transform.localPosition = new Vector3(0, 0, -offset);
            meldInstance.SetMeld(meld, GetSourceDirection(meld, meldObj.transform));
            offset += meldInstance.MeldWidth + Gap;
        }

        /// <summary>
        /// 将点笑来源玩家的世界位置换算成当前副露的本地方向。
        /// 这样四个座位无论如何旋转，箭头都会朝向实际出牌者。
        /// </summary>
        private Vector3 GetSourceDirection(OpenMeld meld, Transform meldTransform)
        {
            if (!meld.IsKong || meld.IsAdded || meld.Side == MeldSide.Self ||
                tableManagers == null || seatIndex < 0 || tableManagers.Length < 4)
                return Vector3.zero;

            int seatOffset;
            switch (meld.Side)
            {
                // MeldSide 已经按副露所属玩家的牌桌视角编码：Right 是
                // 右手侧来源，Opposite 是对面，Left 是左手侧来源。
                case MeldSide.Right:
                    seatOffset = 1;
                    break;
                case MeldSide.Opposite:
                    seatOffset = 2;
                    break;
                case MeldSide.Left:
                    seatOffset = 3;
                    break;
                default:
                    return Vector3.zero;
            }

            var sourceManager = tableManagers[(seatIndex + seatOffset) % 4];
            if (sourceManager == null) return Vector3.zero;

            var worldDirection = sourceManager.transform.position - meldTransform.position;
            if (worldDirection.sqrMagnitude < 0.000001f) return Vector3.zero;

            var localDirection = meldTransform.InverseTransformDirection(worldDirection.normalized);
            // 箭头只允许指向牌桌的四个正方向，避免因为座位布局或
            // 摄像机角度造成斜向箭头。点笑来源只会落在左、右或对面。
            localDirection.y = 0;
            if (localDirection.sqrMagnitude < 0.000001f) return Vector3.zero;

            if (Mathf.Abs(localDirection.x) > Mathf.Abs(localDirection.z))
                return localDirection.x >= 0f ? Vector3.right : Vector3.left;

            return localDirection.z >= 0f ? Vector3.forward : Vector3.back;
        }

        public void ClearMelds()
        {
            transform.DestroyAllChildren();
            currentMelds.Clear();
        }

        private GameObject SelectPrefab(OpenMeld meld)
        {
            if (meld.Side == MeldSide.Self)
                return SelfKongPrefabs;
            if (meld.IsKong && !meld.IsAdded)
                return OpenKongPrefabs[(int)meld.Side];
            return ChowPongPrefabs[(int)meld.Side];
        }
    }
}

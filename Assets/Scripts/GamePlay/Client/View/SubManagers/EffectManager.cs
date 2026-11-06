using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Type = GamePlay.Client.View.PlayerEffectManager.Type;
using Mahjong.Logic;

namespace GamePlay.Client.View.SubManagers
{
    public class EffectManager : MonoBehaviour
    {
        public Image RichiImage;
        public Image ChowImage;
        public Image PongImage;
        public Image KongImage;
        public Image BeiImage;
        public Image TsumoImage;
        public Image RongImage;

        private readonly IDictionary<Type, Image> dict = new Dictionary<Type, Image>();

        private void Awake()
        {
            dict.Clear();
            foreach (Type e in Enum.GetValues(typeof(Type)))
            {
                var fieldInfo = GetType().GetField($"{e}Image");
                var image = (Image)fieldInfo.GetValue(this);
                dict.Add(e, image);
            }
        }

        public float StartAnimation(Type type)
        {
            var image = dict[type];
            // 杠赖子使用较强的入场动画：放大、旋转、回弹后再淡出，避免只闪一下看不清。
            var isBei = type == Type.Bei;
            var fadeDuration = isBei ? MahjongConstants.FadeDuration * 0.85f : MahjongConstants.FadeDuration;
            var baseScale = image.rectTransform.localScale;
            var baseRotation = image.rectTransform.localEulerAngles;
            image.DOKill();
            image.rectTransform.DOKill();
            image.rectTransform.localScale = isBei ? baseScale * 0.72f : baseScale;
            image.rectTransform.localEulerAngles = isBei
                ? baseRotation + new Vector3(0f, 0f, -10f)
                : baseRotation;
            var sequence = DOTween.Sequence();
            sequence.Append(image.DOFade(1, fadeDuration))
                .Join(image.rectTransform.DOScale(isBei ? baseScale * 1.12f : baseScale, fadeDuration * 1.15f)
                    .SetEase(Ease.OutBack))
                .Join(image.rectTransform.DORotate(isBei
                    ? baseRotation + new Vector3(0f, 0f, 4f)
                    : baseRotation, fadeDuration * 1.15f).SetEase(Ease.OutBack))
                .Append(image.rectTransform.DOScale(baseScale, fadeDuration * 0.25f).SetEase(Ease.InOutSine))
                .AppendInterval(isBei ? fadeDuration * 0.35f : 0f)
                .Append(image.DOFade(0, fadeDuration))
                .OnComplete(() =>
                {
                    image.rectTransform.localScale = baseScale;
                    image.rectTransform.localEulerAngles = baseRotation;
                });
            return sequence.Duration();
        }
    }
}

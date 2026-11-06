using System.Collections;
using System.Collections.Generic;
using GamePlay.SinglePlayer;
using GamePlay.Client.View;
using Mahjong.Logic;
using Mahjong.Model;
using UnityEngine;

namespace GamePlay.Client.Controller.GameState
{
    /// <summary>客户端开局展示状态，负责清空上一局桌面并播放“一墩两张”的初始发牌动画。</summary>
    public class RoundStartState : ClientState
    {
        public IList<Tile> LocalPlayerHandTiles;
        public int OyaPlayerIndex;
        public int Dice;
        public int Field;
        public int Extra;
        public int RichiSticks;
        public MahjongSetData MahjongSetData;
        public int[] Points;
        private Coroutine initialDealCoroutine;
        private bool initialDealVisualFinished;

        public override void OnClientStateEnter()
        {
			GameSoundManager.PlayMusic("playingInGame");
			GameSoundManager.Play("duijukaishi");
            // 同步真人玩家的初始手牌数据，但动画完成前暂不展开显示。
            CurrentRoundStatus.NewRound(OyaPlayerIndex, Dice, Field, Extra, RichiSticks);
            this.controller.HandPanelManager.SetWinningHintSuppressed(false);
            CurrentRoundStatus.SetMahjongSetData(MahjongSetData);
            CurrentRoundStatus.CheckLocalHandTiles(LocalPlayerHandTiles);
            CurrentRoundStatus.SetZhenting(false);
            // 同步三个 AI 的暗牌数量。
            for (int placeIndex = 0; placeIndex < 4; placeIndex++)
            {
                int playerIndex = CurrentRoundStatus.GetPlayerIndex(placeIndex);
                if (playerIndex < CurrentRoundStatus.TotalPlayers)
                    CurrentRoundStatus.SetHandTiles(placeIndex, LocalPlayerHandTiles.Count);
                else
                    CurrentRoundStatus.SetHandTiles(placeIndex, 0);
            }
            // 同步四家当前总分。
            CurrentRoundStatus.UpdatePoints(Points);
            // 更新局数、庄家和场风等桌面状态。
            var controller = ViewController.Instance;
            // 按新的牌墙数据重建桌面牌堆。
            controller.YamaManager.ResetAllTiles();
            // 先将所有手牌保持盖住状态。
            controller.TableTilesManager.StandUp();
            // 清除上一局留下的碰、笑等副露显示。
            controller.TableTilesManager.ClearMelds();
            if (SinglePlayerMode.Enabled)
            {
                // 发牌过程中所有手牌保持盖住；52 张初始牌全部离开牌墙后，再一次性展开真人手牌。
                controller.HandPanelManager.gameObject.SetActive(false);
                controller.TableTilesManager.BeginInitialDealAnimation();
                controller.YamaManager.BeginInitialDealAnimation(CurrentRoundStatus);
                initialDealCoroutine = controller.StartCoroutine(PlayInitialDealAnimation());
                return;
            }
            // 联网模式通知服务端客户端已准备；单机模式直接在本地继续流程。
            ClientBehaviour.Instance.ClientReady();
        }

        public override void OnClientStateExit()
        {
            if (initialDealCoroutine != null)
            {
                controller.StopCoroutine(initialDealCoroutine);
                initialDealCoroutine = null;
            }
            FinishInitialDealVisual();
            controller.HandPanelManager.Show();
            controller.HandPanelManager.UnlockTiles();
        }

        /// <summary>六轮每家拿一墩两张，最后一轮每家再拿一张，共 13 张。</summary>
        private IEnumerator PlayInitialDealAnimation()
        {
            var counts = new int[4];
            var wallDrawn = 0;

            // 前六轮按座次每家取一墩两张，共 12 张。
            for (int round = 0; round < 6; round++)
            {
                for (int offset = 0; offset < CurrentRoundStatus.TotalPlayers; offset++)
                {
                    var playerIndex = (OyaPlayerIndex + offset) % CurrentRoundStatus.TotalPlayers;
                    var placeIndex = CurrentRoundStatus.GetPlaceIndex(playerIndex);
                    counts[placeIndex] += 2;
                    wallDrawn += 2;
                    controller.TableTilesManager.SetInitialDealCount(placeIndex, counts[placeIndex]);
                    controller.YamaManager.SetInitialDealDrawn(wallDrawn);
                    yield return new WaitForSeconds(MahjongConstants.InitialDealStepDelay);
                }
            }

            // 最后一轮每家取一张散牌，达到 13 张初始手牌。
            for (int offset = 0; offset < CurrentRoundStatus.TotalPlayers; offset++)
            {
                var playerIndex = (OyaPlayerIndex + offset) % CurrentRoundStatus.TotalPlayers;
                var placeIndex = CurrentRoundStatus.GetPlaceIndex(playerIndex);
                counts[placeIndex]++;
                wallDrawn++;
                controller.TableTilesManager.SetInitialDealCount(placeIndex, counts[placeIndex]);
                controller.YamaManager.SetInitialDealDrawn(wallDrawn);
                yield return new WaitForSeconds(MahjongConstants.InitialDealStepDelay);
            }

            yield return new WaitForSeconds(MahjongConstants.InitialDealRevealDelay);
            FinishInitialDealVisual();
            initialDealCoroutine = null;
        }

        /// <summary>结束发牌动画并立即刷新、排序和展开真人手牌。</summary>
        private void FinishInitialDealVisual()
        {
            if (initialDealVisualFinished || !SinglePlayerMode.Enabled) return;
            initialDealVisualFinished = true;
            controller.YamaManager.EndInitialDealAnimation(CurrentRoundStatus);
            controller.TableTilesManager.EndInitialDealAnimation(CurrentRoundStatus);
            controller.HandPanelManager.Show();
            controller.HandPanelManager.UpdateStatus(CurrentRoundStatus);
        }

        public override void OnStateUpdate()
        {
        }
    }
}

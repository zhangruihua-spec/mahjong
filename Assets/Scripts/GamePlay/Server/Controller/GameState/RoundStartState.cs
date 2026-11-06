using System.Linq;
using GamePlay.Client.Controller;
using GamePlay.Server.Model;
using GamePlay.Server.Model.Events;
using Mahjong.Logic;
using Mahjong.Model;
using Photon.Pun;
using UnityEngine;
using GamePlay.SinglePlayer;


namespace GamePlay.Server.Controller.GameState
{
    /// <summary>
    /// 服务端开局状态：掷骰、为四家生成 13 张初始牌，并确定庄家和癞子指示牌数据。
    /// 客户端发牌动画结束后才让庄家摸第 14 张，避免画面提前进入正常行牌。
    /// </summary>
    public class RoundStartState : ServerState
    {
        public MahjongSet MahjongSet;
        public bool NextRound;
        public bool ExtraRound;
        public bool KeepSticks;
        private bool[] responds;
        private float firstTime;
        public override void OnServerStateEnter()
        {
            MahjongSet.Reset();
            // 掷骰并重置本局数据。
            var dice = Random.Range(CurrentRoundStatus.GameSettings.DiceMin, CurrentRoundStatus.GameSettings.DiceMax + 1);
            CurrentRoundStatus.NextRound(dice, NextRound, ExtraRound, KeepSticks);
            // 按真实发牌顺序生成四家的初始手牌。
            DrawInitial();
            Debug.Log("[Server] Initial tiles distribution done");
            if (SinglePlayerMode.Enabled)
            {
                var handSummary = string.Join("；", Enumerable.Range(0, CurrentRoundStatus.TotalPlayers)
                    .Select(i => $"{CurrentRoundStatus.GetPlayerName(i)}({i})暗牌={CurrentRoundStatus.HandTiles(i).Length}"));
                Debug.Log($"[牌堆追踪][开局发牌后] 总牌={MahjongSet.AllTiles.Count}，癞子指示牌锁定=1，"
                    + $"正面已摸={MahjongSet.TilesDrawn}，尾部已补={MahjongSet.LingShangDrawn}，"
                    + $"可摸剩余={MahjongSet.TilesRemain}；{handSummary}");
            }
            CurrentRoundStatus.SortHandTiles();
            if (SinglePlayerMode.Enabled)
            {
                Debug.Log($"[行动日志][本局开始] 庄家={CurrentRoundStatus.GetPlayerName(CurrentRoundStatus.OyaPlayerIndex)}"
                    + $"(索引{CurrentRoundStatus.OyaPlayerIndex})，癞子指示牌={MahjongSet.LaiziIndicator}，"
                    + $"癞子={MahjongSet.LaiziTile}，可摸剩余={MahjongSet.TilesRemain}");
                for (int i = 0; i < CurrentRoundStatus.TotalPlayers; i++)
                    CurrentRoundStatus.LogAction(i, "开局手牌", null,
                        i == CurrentRoundStatus.OyaPlayerIndex ? "庄家" : "闲家");
            }
            CurrentRoundStatus.SetBonusTurnTime(gameSettings.BonusTurnTime);
            responds = new bool[players.Count];
            if (SinglePlayerMode.Enabled)
            {
                var index = SinglePlayerMode.HumanPlayerIndex;
                ClientBehaviour.Instance.RpcRoundStart(new EventMessages.RoundStartInfo
                {
                    PlayerIndex = index,
                    Field = CurrentRoundStatus.Field,
                    Dice = CurrentRoundStatus.Dice,
                    Extra = CurrentRoundStatus.Extra,
                    RichiSticks = CurrentRoundStatus.RichiSticks,
                    OyaPlayerIndex = CurrentRoundStatus.OyaPlayerIndex,
                    Points = CurrentRoundStatus.Points.ToArray(),
                    InitialHandTiles = CurrentRoundStatus.HandTiles(index),
                    MahjongSetData = MahjongSet.Data
                });
                firstTime = Time.time;
                return;
            }
            var room = PhotonNetwork.CurrentRoom;
            for (int index = 0; index < players.Count; index++)
            {
                var tiles = CurrentRoundStatus.HandTiles(index);
                Debug.Log($"[Server] Hand tiles of player {index}: {string.Join("", tiles)}");
                var info = new EventMessages.RoundStartInfo
                {
                    PlayerIndex = index,
                    Field = CurrentRoundStatus.Field,
                    Dice = CurrentRoundStatus.Dice,
                    Extra = CurrentRoundStatus.Extra,
                    RichiSticks = CurrentRoundStatus.RichiSticks,
                    OyaPlayerIndex = CurrentRoundStatus.OyaPlayerIndex,
                    Points = CurrentRoundStatus.Points.ToArray(),
                    InitialHandTiles = tiles,
                    MahjongSetData = MahjongSet.Data
                };
                var player = CurrentRoundStatus.GetPlayer(index);
                ClientBehaviour.Instance.photonView.RPC("RpcRoundStart", player, info);
            }
            firstTime = Time.time;
        }

        public override void OnStateUpdate()
        {
            if (SinglePlayerMode.Enabled)
            {
                // 本地客户端仍在播放一墩两张的动画时保持开局状态；全部展开后庄家才摸第 14 张。
                if (Time.time - firstTime >= MahjongConstants.InitialDealServerDelay)
                    ServerNextState();
                return;
            }
            if (responds.All(r => r) || Time.time - firstTime >= ServerConstants.ServerTimeOut)
            {
                ServerNextState();
                return;
            }
        }

        private void ServerNextState()
        {
            ServerBehaviour.Instance.DrawTile(CurrentRoundStatus.OyaPlayerIndex);
        }

        public override void OnServerStateExit()
        {
        }

        /// <summary>生成每家 13 张牌：前三轮每家四张，最后一轮每家一张。</summary>
        private void DrawInitial()
        {
            for (int round = 0; round < MahjongConstants.InitialDrawRound; round++)
            {
                // 每轮每家抓四张，连续三轮后各有 12 张。
                for (int index = 0; index < players.Count; index++)
                {
                    for (int i = 0; i < MahjongConstants.TilesEveryRound; i++)
                    {
                        var tile = MahjongSet.DrawTile();
                        CurrentRoundStatus.AddTile(index, tile);
                    }
                }
            }
            // 最后一轮每家补一张，达到 13 张。
            for (int index = 0; index < players.Count; index++)
            {
                for (int i = 0; i < MahjongConstants.TilesLastRound; i++)
                {
                    var tile = MahjongSet.DrawTile();
                    CurrentRoundStatus.AddTile(index, tile);
                }
            }
        }
    }
}

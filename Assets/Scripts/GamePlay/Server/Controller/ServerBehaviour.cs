using System.Collections.Generic;
using Common.StateMachine;
using Common.StateMachine.Interfaces;
using GamePlay.Server.Controller.GameState;
using GamePlay.Server.Model;
using Mahjong.Model;
using Photon.Pun;
using PUNLobby;
using UnityEngine;
using UnityEngine.SceneManagement;
using Utils;
using GamePlay.SinglePlayer;

namespace GamePlay.Server.Controller
{
    /// <summary>
    /// 对局服务端状态机入口。单机模式仍复用这套本地状态机保证规则流程一致，
    /// 但不会依赖远程服务器通信；仅在联网模式下使用 Photon RPC。
    /// </summary>
    public class ServerBehaviour : MonoBehaviourPun
    {
        public SceneField lobbyScene;
        [HideInInspector] public GameSetting GameSettings;
        public IStateMachine StateMachine { get; private set; }
        private MahjongSet mahjongSet;
        public ServerRoundStatus CurrentRoundStatus = null;
        public static ServerBehaviour Instance { get; private set; }
        /// <summary>单机提示使用的实际可摸牌墙构成，不向联网客户端公开。</summary>
        public int[] DrawableTileCounts => mahjongSet == null ? null : mahjongSet.DrawableTileCounts;

        private void OnEnable()
        {
            Debug.Log("[Server] ServerBehaviour.OnEnable() is called");
            if (!PhotonNetwork.IsConnected)
            {
                SceneManager.LoadScene(lobbyScene);
                return;
            }
            if (!PhotonNetwork.IsMasterClient) return;
            Instance = this;
            StateMachine = new StateMachine();
            ReadSetting();
            WaitForOthersLoading();
        }

        private void Start()
        {
            if (!PhotonNetwork.IsMasterClient)
                Destroy(gameObject);
        }

        private void Update()
        {
            StateMachine.UpdateState();
        }

        private void ReadSetting()
        {
            var room = PhotonNetwork.CurrentRoom;
            // var setting = (string)room.CustomProperties[SettingKeys.SETTING];
            // GameSettings = JsonUtility.FromJson<GameSetting>(setting);
            GameSettings = (GameSetting)room.CustomProperties[SettingKeys.SETTING];
            // 单机局统一使用当前规则设定的底分，避免旧房间配置仍残留此前的 5 分底。
            if (SinglePlayerMode.Enabled)
            {
                GameSettings.InitialPoints = SinglePlayerMode.DefaultInitialPoints;
                GameSettings.BasePoint = SinglePlayerMode.DefaultBasePoint;
            }
        }

        private void WaitForOthersLoading()
        {
            var waitingState = new WaitForLoadingState
            {
                TotalPlayers = GameSettings.MaxPlayer
            };
            StateMachine.ChangeState(waitingState);
        }

        public void GamePrepare()
        {
            CurrentRoundStatus = SinglePlayerMode.Enabled
                ? new ServerRoundStatus(GameSettings, SinglePlayerMode.PlayerNames)
                : new ServerRoundStatus(GameSettings, PhotonNetwork.PlayerList);
            mahjongSet = new MahjongSet(GameSettings, GameSettings.GetAllTiles());
            var prepareState = new GamePrepareState
            {
                CurrentRoundStatus = CurrentRoundStatus,
            };
            StateMachine.ChangeState(prepareState);
        }

        public void GameAbort()
        {
            // TODO：实现客户端加载失败后的中止流程，并安全返回大厅。
            Debug.LogError("The game aborted, this part is still under construction");
        }

        public void RoundStart(bool next, bool extra, bool keepSticks)
        {
            var startState = new RoundStartState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                MahjongSet = mahjongSet,
                NextRound = next,
                ExtraRound = extra,
                KeepSticks = keepSticks
            };
            StateMachine.ChangeState(startState);
        }

        /// <summary>进入摸牌状态；isLingShang 为 true 时从牌墙尾部补摸。</summary>
        public void DrawTile(int playerIndex, bool isLingShang = false, bool turnDoraAfterDiscard = false)
        {
            // 所有普通摸牌和尾部补牌都经过这里。最后一张已经被摸走时直接进入荒牌结算，
            // 防止癞子杠、碰后杠或旧的继续回合路径再次调用 DrawLingShang 而抛异常。
            if (mahjongSet == null)
            {
                Debug.LogError("[牌堆追踪] 牌墙尚未初始化，无法执行摸牌。");
                return;
            }
            if (SinglePlayerMode.Enabled && mahjongSet.TilesRemain <= 0)
            {
                Debug.Log($"[牌堆追踪][阻止无牌补摸] 玩家索引={playerIndex}，"
                    + $"请求类型={(isLingShang ? "牌堆尾部补牌" : "正常摸牌")}，"
                    + $"可摸剩余={mahjongSet.TilesRemain}，改为荒牌结算");
                ExhaustiveDrawSettlement(false);
                return;
            }
            var drawState = new PlayerDrawTileState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                CurrentPlayerIndex = playerIndex,
                MahjongSet = mahjongSet,
                IsLingShang = isLingShang,
                TurnDoraAfterDiscard = turnDoraAfterDiscard
            };
            StateMachine.ChangeState(drawState);
        }

        public void DiscardTile(int playerIndex, Tile tile, bool isRichiing, bool discardLastDraw, int bonusTurnTime, bool turnDoraAfterDiscard)
        {
            // 最终规则校验：单机局的癞子只能留在手中或通过 Bei 操作杠出，
            // 无论点击、拖动、AI 还是超时路径，都不允许把癞子当作普通弃牌。
            if (SinglePlayerMode.Enabled && mahjongSet != null
                && mahjongSet.LaiziTile.Rank > 0
                && tile.EqualsIgnoreColor(mahjongSet.LaiziTile))
            {
                Debug.LogWarning($"[规则追踪][禁止打癞子] 玩家={CurrentRoundStatus.GetPlayerName(playerIndex)}"
                    + $"(索引{playerIndex}) 尝试普通打出癞子={tile}，请求已拒绝；"
                    + "请选择杠癞子或打出其他牌。");
                return;
            }
            var discardState = new PlayerDiscardTileState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                CurrentPlayerIndex = playerIndex,
                DiscardTile = tile,
                IsRichiing = isRichiing,
                DiscardLastDraw = discardLastDraw,
                BonusTurnTime = bonusTurnTime,
                MahjongSet = mahjongSet,
                TurnDoraAfterDiscard = turnDoraAfterDiscard
            };
            StateMachine.ChangeState(discardState);
        }

        public void TurnEnd(int playerIndex, Tile discardingTile, bool isRichiing, OutTurnOperation[] operations,
            bool isRobKong, bool turnDoraAfterDiscard)
        {
            var turnEndState = new TurnEndState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                CurrentPlayerIndex = playerIndex,
                DiscardingTile = discardingTile,
                IsRichiing = isRichiing,
                Operations = operations,
                MahjongSet = mahjongSet,
                IsRobKong = isRobKong,
                TurnDoraAfterDiscard = turnDoraAfterDiscard
            };
            StateMachine.ChangeState(turnEndState);
        }

        public void PerformOutTurnOperation(int newPlayerIndex, OutTurnOperation operation)
        {
            var operationPerformState = new OperationPerformState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                CurrentPlayerIndex = newPlayerIndex,
                DiscardPlayerIndex = CurrentRoundStatus.CurrentPlayerIndex,
                Operation = operation,
                MahjongSet = mahjongSet
            };
            StateMachine.ChangeState(operationPerformState);
        }

        /// <summary>
        /// 癞子前一位只有三张可用牌，三张闷笑后不补摸，直接让当前玩家打出一张。
        /// 复用碰后出牌状态来显示手牌、癞子杠按钮和出牌倒计时，但不再重复处理副露。
        /// </summary>
        public void DiscardAfterLaiziPreviousKong(int playerIndex)
        {
            var discardState = new OperationPerformState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                CurrentPlayerIndex = playerIndex,
                DiscardPlayerIndex = -1,
                Operation = new OutTurnOperation { Type = OutTurnOperationType.Skip },
                MahjongSet = mahjongSet,
                ContinueAfterSpecialKong = true
            };
            StateMachine.ChangeState(discardState);
        }

        public void Tsumo(int currentPlayerIndex, Tile winningTile, PointInfo pointInfo)
        {
            var tsumoState = new PlayerTsumoState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                TsumoPlayerIndex = currentPlayerIndex,
                WinningTile = winningTile,
                MahjongSet = mahjongSet,
                TsumoPointInfo = pointInfo
            };
            StateMachine.ChangeState(tsumoState);
        }

        public void Rong(int currentPlayerIndex, Tile winningTile, int[] rongPlayerIndices, PointInfo[] rongPointInfos)
        {
            var rongState = new PlayerRongState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                CurrentPlayerIndex = currentPlayerIndex,
                RongPlayerIndices = rongPlayerIndices,
                WinningTile = winningTile,
                MahjongSet = mahjongSet,
                RongPointInfos = rongPointInfos
            };
            StateMachine.ChangeState(rongState);
        }

        public void Kong(int playerIndex, OpenMeld kong)
        {
            var kongState = new PlayerKongState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                CurrentPlayerIndex = playerIndex,
                MahjongSet = mahjongSet,
                Kong = kong
            };
            StateMachine.ChangeState(kongState);
        }

        public void RoundDraw(RoundDrawType type)
        {
            var drawState = new RoundDrawState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                RoundDrawType = type
            };
            StateMachine.ChangeState(drawState);
        }

        public void BeiDora(int playerIndex)
        {
            var beiState = new PlayerBeiDoraState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                CurrentPlayerIndex = playerIndex,
                MahjongSet = mahjongSet
            };
            StateMachine.ChangeState(beiState);
        }

        /// <summary>进入统一分数结算状态，结算面板确认完成后才允许切换下一局。</summary>
        public void PointTransfer(IList<PointTransfer> transfers, bool next, bool extra, bool keepSticks,
            string reason = "胡牌结算", bool continueCurrentRound = false, int continuePlayerIndex = -1,
            bool continueTurnDoraAfterDiscard = false,
            IList<SettlementDetail> settlementDetails = null)
        {
            var transferState = new PointTransferState
            {
                CurrentRoundStatus = CurrentRoundStatus,
                NextRound = next,
                ExtraRound = extra,
                KeepSticks = keepSticks,
                PointTransferList = transfers,
                SettlementDetails = settlementDetails,
                Reason = reason,
                ContinueCurrentRound = continueCurrentRound,
                ContinuePlayerIndex = continuePlayerIndex,
                ContinueTurnDoraAfterDiscard = continueTurnDoraAfterDiscard
            };
            StateMachine.ChangeState(transferState);
        }

        /// <summary>牌墙真正摸完后的荒牌结算；即使无人收支，也显示一次完整结算过程。</summary>
        public void ExhaustiveDrawSettlement(bool extraRound)
        {
            var transfers = new List<PointTransfer>();
            var details = new List<SettlementDetail>();
            foreach (var detail in CurrentRoundStatus.PendingSmileDetails)
            {
                var combinedLaiziKongs = CurrentRoundStatus.GetLaiziKongs(detail.From)
                    + CurrentRoundStatus.GetLaiziKongs(detail.To);
                var multiplier = 1 << combinedLaiziKongs;
                var amount = detail.Amount * multiplier;
                transfers.Add(new PointTransfer
                {
                    From = detail.From,
                    To = detail.To,
                    Amount = amount
                });
                details.Add(new SettlementDetail
                {
                    From = detail.From,
                    To = detail.To,
                    Amount = amount,
                    Reason = combinedLaiziKongs > 0
                        ? $"{detail.Reason}（双方赖子杠共{combinedLaiziKongs}个，×{multiplier}）"
                        : detail.Reason,
                    Tile = detail.Tile
                });
                Debug.Log($"[结算追踪][牌堆摸完笑牌付款] 类型={detail.Reason}，牌={detail.Tile}，"
                    + $"{CurrentRoundStatus.GetPlayerName(detail.From)}(索引{detail.From})"
                    + $" -> {CurrentRoundStatus.GetPlayerName(detail.To)}(索引{detail.To})，"
                    + $"基础分={detail.Amount}，双方癞子杠={combinedLaiziKongs}个，"
                    + $"倍率=×{multiplier}，实际={amount}");
            }
            // 即使最终荒牌，已经杠满四个癞子的玩家仍然可以获得喜钱。
            CurrentRoundStatus.AppendFourLaiziBonusSettlements(
                transfers, details, mahjongSet.LaiziTile);
            Debug.Log($"[结算追踪][牌堆摸完] 笑牌转账={transfers.Count}笔，先显示结算面板，确认后开始下一局");
            PointTransfer(transfers, true, extraRound, false, "荒牌结算",
                false, -1, false, details);
        }

        public void GameEnd()
        {
            var gameEndState = new GameEndState
            {
                CurrentRoundStatus = CurrentRoundStatus
            };
            StateMachine.ChangeState(gameEndState);
        }
    }
}

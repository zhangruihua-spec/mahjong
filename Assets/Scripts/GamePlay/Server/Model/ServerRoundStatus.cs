using System;
using System.Collections.Generic;
using System.Linq;
using Mahjong.Logic;
using Mahjong.Model;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Utils;
using GamePlay.SinglePlayer;

namespace GamePlay.Server.Model
{
    /// <summary>
    /// 服务端单局数据模型：保存四家手牌、副露、河牌、分数、当前行动者及本局待结算事件。
    /// 笑牌分数先暂存在这里，直到胡牌或荒牌时再与最终结果统一结算。
    /// </summary>
    [Serializable]
    public class ServerRoundStatus
    {
        [Serializable]
        public class RoundHistoryEntry
        {
            public int Sequence;
            public int PlayerIndex;
            public string PlayerName;
            public string Action;
            public string Tile;
            public string Detail;
            public int HandCount;
            public int RiverCount;
            public int LaiziKongCount;
        }

        private int currentPlayerIndex = -1;
        private int oya = -1;
        private int field = 0;
        private int dice;
        private int extra;
        private int richiSticks;
        private int kongClaimed;
        private int[] bonusTurnTime;
        private List<Tile>[] handTiles;
        private List<OpenMeld>[] openMelds;
        private int[] points;
        private Tile? lastDraw = null;
        private bool[] richiStatus;
        private bool[] oneShotStatus;
        private bool firstTurn;
        private int turnCount;
        private List<RiverTile>[] rivers;
        private bool[] tempZhenting;
        private bool[] discardZhenting;
        private bool[] richiZhenting;
        private int[] beiDoras;
        private int[] laiziKongs;
        private List<PointTransfer> pendingSmileTransfers = new List<PointTransfer>();
        private List<string> pendingSmileReasons = new List<string>();
        private List<SmileSettlement> pendingSmileSettlements = new List<SmileSettlement>();
        private List<SettlementDetail> pendingSmileDetails = new List<SettlementDetail>();
        private List<RoundHistoryEntry> history = new List<RoundHistoryEntry>();
        private IList<int> playerActorNumbers;
        private IDictionary<int, string> playerNames;

        public ServerRoundStatus(GameSetting gameSettings, IList<Player> players)
        {
            GameSettings = gameSettings;
            points = new int[TotalPlayers];
            playerActorNumbers = players.Select(p => p.ActorNumber).ToList();
            playerNames = players.ToDictionary(p => p.ActorNumber, p => p.NickName);
        }

        public ServerRoundStatus(GameSetting gameSettings, string[] names)
        {
            GameSettings = gameSettings;
            points = new int[TotalPlayers];
            playerActorNumbers = Enumerable.Range(0, TotalPlayers).ToList();
            playerNames = playerActorNumbers.ToDictionary(index => index,
                index => index < names.Length ? names[index] : $"AI {index}");
        }

        public GameSetting GameSettings { get; }

        public int CurrentPlayerIndex
        {
            get
            {
                return currentPlayerIndex;
            }
            set
            {
                currentPlayerIndex = value;
            }
        }

        public IList<PointTransfer> PendingSmileTransfers => pendingSmileTransfers.AsReadOnly();
        public string PendingSmileReason => string.Join(" + ", pendingSmileReasons.Distinct().ToArray());
        public IList<SmileSettlement> PendingSmileSettlements => pendingSmileSettlements.AsReadOnly();
        public IList<SettlementDetail> PendingSmileDetails => pendingSmileDetails.AsReadOnly();
        public IList<RoundHistoryEntry> History => history.AsReadOnly();

        /// <summary>
        /// 记录一次笑牌及其基础转账。此处不修改玩家总分，癞子倍数也留到最终结算时计算。
        /// </summary>
        public void AddPendingSmileSettlement(string reason, int playerIndex, Tile tile,
            IEnumerable<PointTransfer> transfers)
        {
            var transferList = transfers == null ? new List<PointTransfer>() : transfers.ToList();
            var hasDiscardSource = reason == "点笑" || reason == "前位碰";
            var sourcePlayerIndex = hasDiscardSource && transferList.Count > 0
                ? transferList[0].From
                : -1;
            if (!string.IsNullOrEmpty(reason)) pendingSmileReasons.Add(reason);
            pendingSmileSettlements.Add(new SmileSettlement
            {
                PlayerIndex = playerIndex,
                SourcePlayerIndex = sourcePlayerIndex,
                Type = reason,
                Tile = tile
            });
            if (transferList.Count > 0)
            {
                foreach (var transfer in transferList)
                {
                    pendingSmileTransfers.Add(transfer);
                    pendingSmileDetails.Add(new SettlementDetail
                    {
                        From = transfer.From,
                        To = transfer.To,
                        Amount = transfer.Amount,
                        Reason = reason,
                        Tile = tile
                    });
                    if (SinglePlayerMode.Enabled)
                    {
                        var fromName = transfer.From >= 0 && transfer.From < TotalPlayers
                            ? GetPlayerName(transfer.From)
                            : "系统";
                        var toName = transfer.To >= 0 && transfer.To < TotalPlayers
                            ? GetPlayerName(transfer.To)
                            : "系统";
                        Debug.Log($"[结算追踪][笑牌] 类型={reason}，牌={tile}，笑牌玩家={GetPlayerName(playerIndex)}(索引{playerIndex})，"
                            + $"付款={fromName}(索引{transfer.From}) -> 收款={toName}(索引{transfer.To})，"
                            + $"基础分={transfer.Amount}，最终分在胡牌时按双方癞子杠次数计算");
                    }
                }
            }
        }
        public Player GetPlayer(int playerIndex)
        {
            if (SinglePlayerMode.Enabled)
                return PhotonNetwork.LocalPlayer;
            var room = PhotonNetwork.CurrentRoom;
            if (room == null) throw new ArgumentException("This should not happen");
            int actorNumber = playerActorNumbers[playerIndex];
            return room.Players[actorNumber];
        }
        public IList<int> PlayerActorNumbers => playerActorNumbers;
        public int OyaPlayerIndex => oya;
        public int Field => field;
        public int Dice => dice;
        public int Extra => extra;
        public int ExtraPoints => Extra * GameSettings.ExtraRoundBonusPerPlayer;
        public int RichiSticks => richiSticks;
        public int RichiSticksPoints => RichiSticks * GameSettings.RichiMortgagePoints;
        public Tile? LastDraw
        {
            get { return lastDraw; }
            set
            {
                if (default(Tile).Equals(value)) lastDraw = null;
                else lastDraw = value;
            }
        }
        public bool RichiStatus(int playerIndex)
        {
            return richiStatus[playerIndex];
        }
        public bool[] RichiStatusArray
        {
            get
            {
                var array = new bool[TotalPlayers];
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] = richiStatus[i];
                }
                return array;
            }
        }
        public bool OneShotStatus(int playerIndex)
        {
            return oneShotStatus[playerIndex];
        }
        public bool IsZhenting(int playerIndex)
        {
            return tempZhenting[playerIndex] || discardZhenting[playerIndex] || richiZhenting[playerIndex];
        }
        public bool FirstTurn => firstTurn;
        public int TotalPlayers => GameSettings.MaxPlayer;
        public string[] PlayerNames => playerActorNumbers.Select(id => playerNames[id]).ToArray();
        public int KongClaimed => kongClaimed;
        public int MaxBonusTurnTime => bonusTurnTime.Max();

        public string GetPlayerName(int index)
        {
            return playerNames[playerActorNumbers[index]];
        }

        public void ClaimKong()
        {
            kongClaimed++;
        }

        public void ShufflePlayers()
        {
            playerActorNumbers.Shuffle();
        }

        public int GetBonusTurnTime(int index)
        {
            CheckRange(index);
            return bonusTurnTime[index];
        }

        public void SetBonusTurnTime(int value)
        {
            for (int i = 0; i < bonusTurnTime.Length; i++)
                SetBonusTurnTime(i, value);
        }

        public void SetBonusTurnTime(int index, int value)
        {
            CheckRange(index);
            bonusTurnTime[index] = value;
        }

        public Tile[] HandTiles(int index)
        {
            CheckRange(index);
            return handTiles[index].ToArray();
        }

        public OpenMeld[] OpenMelds(int index)
        {
            CheckRange(index);
            return openMelds[index].ToArray();
        }

        public Meld[] Melds(int index)
        {
            CheckRange(index);
            return openMelds[index].Select(open => open.Meld).ToArray();
        }

        public PlayerHandData HandData(int index)
        {
            CheckRange(index);
            return new PlayerHandData
            {
                HandTiles = HandTiles(index),
                OpenMelds = OpenMelds(index)
            };
        }

        public RiverData[] Rivers
        {
            get
            {
                var rivers = new RiverData[TotalPlayers];
                for (int i = 0; i < rivers.Length; i++)
                {
                    rivers[i] = new RiverData
                    {
                        River = this.rivers[i].ToArray()
                    };
                }
                return rivers;
            }
        }

        public int[] Points => points;

        public int GetPoints(int index)
        {
            return points[index];
        }

        public void SetPoints(int index, int point)
        {
            points[index] = point;
        }

        public void ChangePoints(int index, int amount)
        {
            points[index] += amount;
        }

        public void AddTile(int index, Tile tile)
        {
            CheckRange(index);
            handTiles[index].Add(tile);
        }

        public void RemoveTile(int index, Tile tile)
        {
            CheckRange(index);
            var i = handTiles[index].FindIndex(t => t.EqualsConsiderColor(tile));
            if (i < 0) return;
            handTiles[index].RemoveAt(i);
        }

        public void RemoveTile(int index, OpenMeld meld)
        {
            CheckRange(index);
            var tiles = handTiles[index];
            int p = System.Array.FindIndex(meld.Tiles, tile => tile.EqualsConsiderColor(meld.Tile));
            for (int i = 0; i < meld.Tiles.Length; i++)
            {
                if (i == p) continue;
                RemoveTile(index, meld.Tiles[i]);
            }
        }

        /// <summary>把牌加入玩家出牌区，并保留癞子/癞子杠标记，供桌面高亮显示。</summary>
        public void AddToRiver(int index, Tile tile, bool richi = false, bool isLaiziKong = false,
            bool isLaizi = false)
        {
            CheckRange(index);
            rivers[index].Add(new RiverTile
            {
                Tile = tile,
                IsRichi = richi,
                IsGone = false,
                IsLaiziKong = isLaiziKong,
                IsLaizi = isLaizi || isLaiziKong
            });
        }

        public void RemoveFromRiver(int index)
        {
            CheckRange(index);
            var river = rivers[index];
            if (river.Count == 0)
            {
                Debug.LogWarning($"There are no tiles in river {index}");
                return;
            }
            var tile = river[river.Count - 1];
            river[river.Count - 1] = new RiverTile
            {
                Tile = tile.Tile,
                IsRichi = tile.IsRichi,
                IsGone = true,
                IsLaiziKong = tile.IsLaiziKong,
                IsLaizi = tile.IsLaizi
            };
        }

        public void AddMeld(int index, OpenMeld meld)
        {
            CheckRange(index);
            openMelds[index].Add(meld);
        }

        public void AddKong(int index, OpenMeld kong)
        {
            CheckRange(index);
            int i = openMelds[index].FindIndex(meld => meld.Type == MeldType.Triplet && meld.First.EqualsIgnoreColor(kong.First));
            if (i < 0)
            {
                Debug.LogError($"Pong of {kong} not found, this should not happen");
                return;
            }
            openMelds[index][i] = kong;
        }

        /// <summary>本回合可以回头笑却没有杠时，锁定对应的碰，之后不能再补杠。</summary>
        public void LockDeclinedAddKong(int index, Tile tile)
        {
            CheckRange(index);
            for (int i = 0; i < openMelds[index].Count; i++)
            {
                var meld = openMelds[index][i];
                if (meld.Type != MeldType.Triplet || meld.IsKong || meld.KongLocked) continue;
                if (!tile.EqualsIgnoreColor(meld.First)) continue;
                meld.KongLocked = true;
                openMelds[index][i] = meld;
                LogAction(index, "放弃回头笑", tile, "这一手没有杠，该碰之后不能再补杠");
            }
        }

        public void TryRichi(int index, bool isRichiing)
        {
            CheckRange(index);
            if (!isRichiing) return;
            if (richiStatus[index])
            {
                Debug.LogError($"Player {index} has already richied, therefore he cannot richi again.");
                return;
            }
            richiStatus[index] = true;
            oneShotStatus[index] = true;
            richiSticks++;
            points[index] -= GameSettings.RichiMortgagePoints;
        }

        public void BreakTempZhenting(int playerIndex)
        {
            CheckRange(playerIndex);
            tempZhenting[playerIndex] = false;
        }

        public void UpdateTempZhenting(int discardPlayerIndex, Tile discardTile)
        {
            // 联网旧规则：检查其他玩家是否因放过该牌进入临时振听。
            for (int playerIndex = 0; playerIndex < GameSettings.MaxPlayer; playerIndex++)
            {
                if (playerIndex == discardPlayerIndex) continue;
                if (MahjongLogic.HasWin(handTiles[playerIndex], null, discardTile))
                    tempZhenting[playerIndex] = true;
            }
        }

        public void UpdateDiscardZhenting()
        {
            for (int i = 0; i < GameSettings.MaxPlayer; i++)
                UpdateDiscardZhenting(i);
        }

        public void UpdateDiscardZhenting(int playerIndex)
        {
            discardZhenting[playerIndex] = MahjongLogic.TestDiscardZhenting(handTiles[playerIndex], rivers[playerIndex]);
        }

        public void UpdateRichiZhenting(Tile discardTile)
        {
            for (int i = 0; i < GameSettings.MaxPlayer; i++)
                UpdateRichiZhenting(i, discardTile);
        }

        public void UpdateRichiZhenting(int playerIndex, Tile discardTile)
        {
            if (!RichiStatus(playerIndex) || richiZhenting[playerIndex]) return;
            if (MahjongLogic.HasWin(handTiles[playerIndex], null, discardTile))
                richiZhenting[playerIndex] = true;
        }

        public void CheckOneShot(int index)
        {
            if (oneShotStatus[index]) oneShotStatus[index] = false;
        }

        public void BreakOneShotsAndFirstTurn()
        {
            for (int i = 0; i < oneShotStatus.Length; i++)
            {
                oneShotStatus[i] = false;
                firstTurn = false;
            }
        }

        public void CheckFirstTurn(int playerIndex)
        {
            CheckRange(playerIndex);
            if (playerIndex == OyaPlayerIndex)
            {
                if (turnCount > 0)
                {
                    firstTurn = false;
                    return;
                }
                turnCount++;
            }
        }

        public bool CheckFourWinds()
        {
            if (!GameSettings.Allow4WindDraw) return false;
            if (!FirstTurn) return false;
            if (TotalPlayers < 4) return false;
            // 每次出牌后都会检查，但首巡尚未出牌的玩家河牌为空；四家没有全部出过第一张牌时不能判四风连打。
            if (rivers == null || rivers.Any(river => river == null || river.Count == 0))
                return false;
            var first = rivers[0][0].Tile;
            for (int i = 1; i < TotalPlayers; i++)
            {
                var current = rivers[i][0].Tile;
                if (!current.EqualsIgnoreColor(first)) return false;
            }
            return true;
        }

        public bool CheckFourRichis()
        {
            if (!GameSettings.Allow4RichiDraw) return false;
            if (TotalPlayers < 4) return false;
            return richiStatus.All(r => r);
        }

        public bool CheckFourKongs()
        {
            if (!GameSettings.Allow4KongDraw) return false;
            if (KongClaimed < 4) return false;
            // check if 4 kongs are in same person
            for (int i = 0; i < TotalPlayers; i++)
            {
                var melds = openMelds[i];
                var kongCount = melds.Count(meld => meld.IsKong);
                if (kongCount >= 4) return false;
            }
            return true;
        }

        public bool CheckThreeRongs(OutTurnOperation[] operations)
        {
            if (!GameSettings.Allow3RongDraw) return false;
            if (TotalPlayers < 4) return false;
            var count = operations.Count(op => op.Type == OutTurnOperationType.Rong);
            return count == 3;
        }

        public int GetBeiDora(int index)
        {
            CheckRange(index);
            return beiDoras[index];
        }

        public int GetLaiziKongs(int index)
        {
            CheckRange(index);
            return laiziKongs[index];
        }

        /// <summary>为本局杠满四个癞子的玩家追加“每家支付喜钱”的独立结算项。</summary>
        public void AppendFourLaiziBonusSettlements(ICollection<PointTransfer> transfers,
            ICollection<SettlementDetail> details, Tile laiziTile)
        {
            if (!SinglePlayerMode.Enabled || transfers == null || details == null) return;

            // 当前底分 10 时杠满四个癞子，另外三家各付 400 喜钱。
            // 喜钱始终按底分的 40 倍计算，但喜钱本身不再被癞子杠翻倍。
            var amountPerPlayer = GameSettings.BasePoint * 40;
            for (int receiver = 0; receiver < TotalPlayers; receiver++)
            {
                if (laiziKongs[receiver] < 4) continue;
                for (int payer = 0; payer < TotalPlayers; payer++)
                {
                    if (payer == receiver) continue;
                    transfers.Add(new PointTransfer
                    {
                        From = payer,
                        To = receiver,
                        Amount = amountPerPlayer
                    });
                    details.Add(new SettlementDetail
                    {
                        From = payer,
                        To = receiver,
                        Amount = amountPerPlayer,
                        Reason = $"四癞子喜钱（底分{GameSettings.BasePoint}，每家{amountPerPlayer}）",
                        Tile = laiziTile
                    });
                    Debug.Log($"[结算追踪][四癞子喜钱] {GetPlayerName(payer)}(索引{payer})"
                        + $" -> {GetPlayerName(receiver)}(索引{receiver})，"
                        + $"底分={GameSettings.BasePoint}，固定喜钱={amountPerPlayer}（不再翻倍）");
                }
            }
        }

        /// <summary>输出单机对局追踪日志，便于核对每家手牌数、操作和癞子杠累计次数。</summary>
        public void LogAction(int index, string action, Tile? tile = null, string detail = null)
        {
            CheckRange(index);
            var tileText = tile.HasValue ? tile.Value.ToString() : "无";
            history.Add(new RoundHistoryEntry
            {
                Sequence = history.Count + 1,
                PlayerIndex = index,
                PlayerName = GetPlayerName(index),
                Action = action,
                Tile = tileText,
                Detail = detail,
                HandCount = handTiles[index].Count,
                RiverCount = rivers[index].Count,
                LaiziKongCount = laiziKongs[index]
            });
            if (!SinglePlayerMode.Enabled) return;
            var handText = string.Join("", handTiles[index]);
            var meldText = openMelds[index].Count > 0
                ? string.Join("|", openMelds[index])
                : "无";
            var detailText = string.IsNullOrEmpty(detail) ? string.Empty : $"，{detail}";
            Debug.Log($"[行动日志][{action}] 玩家={GetPlayerName(index)}(索引{index})，牌={tileText}，"
                + $"暗牌数={handTiles[index].Count}，暗牌=[{handText}]，副露=[{meldText}]，"
                + $"河牌数={rivers[index].Count}，癞子杠累计={laiziKongs[index]}个{detailText}");
        }

        /// <summary>记录玩家杠出一个癞子；癞子移牌和尾部补摸由对应游戏状态负责。</summary>
        public void AddLaiziKong(int index, Tile tile)
        {
            CheckRange(index);
            laiziKongs[index]++;
            if (SinglePlayerMode.Enabled)
            {
                Debug.Log($"[结算追踪][癞子杠] 玩家={GetPlayerName(index)}(索引{index})，牌={tile}，"
                    + $"本局累计癞子杠={laiziKongs[index]}个");
                LogAction(index, "癞子杠", tile, "癞子已移出手牌，下一步从牌堆尾部补摸");
            }
        }

        public int[] GetBeiDoras()
        {
            return beiDoras;
        }

        public void ClearBeiDoras()
        {
            for (int index = 0; index < beiDoras.Length; index++)
            {
                beiDoras[index] = 0;
            }
        }

        public void AddBeiDoras(int index)
        {
            CheckRange(index);
            beiDoras[index]++;
        }

        public bool IsAllLast => GameSettings.IsAllLast(OyaPlayerIndex, Field, TotalPlayers);

        public bool GameForceEnd => GameSettings.GameForceEnd(OyaPlayerIndex, Field, TotalPlayers);

        public void SortHandTiles(int index)
        {
            CheckRange(index);
            handTiles[index].Sort();
        }

        public void SortHandTiles()
        {
            for (int i = 0; i < GameSettings.MaxPlayer; i++)
            {
                SortHandTiles(i);
            }
        }

        public bool IsDealer(int index)
        {
            return index == OyaPlayerIndex;
        }

        /// <summary>重置一局内的临时数据，并根据参数决定换庄、连庄以及是否保留立直棒。</summary>
        public void NextRound(int dice, bool next = true, bool extra = false, bool keepSticks = false)
        {
            this.dice = dice;
            if (next)
            {
                oya++;
                if (oya >= GameSettings.MaxPlayer)
                {
                    oya -= GameSettings.MaxPlayer;
                    field++;
                }
            }
            if (extra) this.extra++;
            if (!keepSticks) richiSticks = 0;
            Debug.Log($"Entering next round, total players: {GameSettings.MaxPlayer}, current player index: {oya}, dice: {dice}, extra: {this.extra}");
            kongClaimed = 0;
            bonusTurnTime = new int[GameSettings.MaxPlayer];
            handTiles = new List<Tile>[GameSettings.MaxPlayer];
            openMelds = new List<OpenMeld>[GameSettings.MaxPlayer];
            rivers = new List<RiverTile>[GameSettings.MaxPlayer];
            richiStatus = new bool[GameSettings.MaxPlayer];
            oneShotStatus = new bool[GameSettings.MaxPlayer];
            tempZhenting = new bool[GameSettings.MaxPlayer];
            discardZhenting = new bool[GameSettings.MaxPlayer];
            richiZhenting = new bool[GameSettings.MaxPlayer];
            beiDoras = new int[GameSettings.MaxPlayer];
            laiziKongs = new int[GameSettings.MaxPlayer];
            pendingSmileTransfers.Clear();
            pendingSmileReasons.Clear();
            pendingSmileSettlements.Clear();
            pendingSmileDetails.Clear();
            firstTurn = true;
            turnCount = 0;
            for (int i = 0; i < GameSettings.MaxPlayer; i++)
            {
                handTiles[i] = new List<Tile>();
                openMelds[i] = new List<OpenMeld>();
                rivers[i] = new List<RiverTile>();
            }
        }

        private void CheckRange(int index)
        {
            if (index < 0 || index >= GameSettings.MaxPlayer)
                throw new IndexOutOfRangeException($"Player index out of range, should be within {0} to {GameSettings.MaxPlayer - 1}");
        }

        public override string ToString()
        {
            var list = new List<string>();
            for (int i = 0; i < handTiles.Length; i++)
            {
                list.Add($"Hand: {string.Join("", handTiles[i])}, "
                    + $"Open: {string.Join(",", openMelds[i])}, "
                    + $"River: {string.Join("", rivers[i])}");
            }
            return string.Join("\n", list);
        }
    }
}

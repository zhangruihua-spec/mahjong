using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using GamePlay.Client.Controller;
using GamePlay.Server.Model;
using GamePlay.Server.Model.Events;
using Mahjong.Logic;
using Mahjong.Model;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using GamePlay.SinglePlayer;

namespace GamePlay.Server.Controller.GameState
{
    /// <summary>
    /// 玩家摸牌后的回合状态：从牌墙取牌、计算可用按钮，并等待真人或 AI 选择自摸、笑牌、癞子杠或出牌。
    /// 补杠后的牌统一从牌墙尾部摸取。
    /// </summary>
    public class PlayerDrawTileState : ServerState, IOnEventCallback
    {
        public int CurrentPlayerIndex;
        public MahjongSet MahjongSet;
        public bool IsLingShang;
        public bool TurnDoraAfterDiscard;
        private Tile justDraw;
        private float firstSendTime;
        private float serverTimeOut;
        private PointInfo tsumoPointInfo;
        private InTurnOperation[] availableOperations;
        private float aiActionTime;
        private bool aiActed;

        public override void OnServerStateEnter()
        {
            PhotonNetwork.AddCallbackTarget(this);
            var remainBeforeDraw = MahjongSet.TilesRemain;
            if (SinglePlayerMode.Enabled)
                Debug.Log($"[牌堆追踪][摸牌前] 玩家={CurrentRoundStatus.GetPlayerName(CurrentPlayerIndex)}"
                    + $"(索引{CurrentPlayerIndex})，类型={(IsLingShang ? "牌堆尾部补牌" : "正常摸牌")}，"
                    + $"可摸剩余={remainBeforeDraw}，正面已摸={MahjongSet.TilesDrawn}，"
                    + $"尾部已补={MahjongSet.LingShangDrawn}，癞子指示牌锁定=1，"
                    + $"该玩家暗牌={CurrentRoundStatus.HandTiles(CurrentPlayerIndex).Length}");
            if (IsLingShang)
                justDraw = MahjongSet.DrawLingShang();
            else
                justDraw = MahjongSet.DrawTile();
            if (SinglePlayerMode.Enabled)
                Debug.Log($"[牌堆追踪][摸牌后] 玩家={CurrentRoundStatus.GetPlayerName(CurrentPlayerIndex)}"
                    + $"(索引{CurrentPlayerIndex})，摸到={justDraw}，可摸剩余={MahjongSet.TilesRemain}，"
                    + $"正面已摸={MahjongSet.TilesDrawn}，尾部已补={MahjongSet.LingShangDrawn}");
            CurrentRoundStatus.CurrentPlayerIndex = CurrentPlayerIndex;
            CurrentRoundStatus.LastDraw = justDraw;
            CurrentRoundStatus.CheckFirstTurn(CurrentPlayerIndex);
            CurrentRoundStatus.BreakTempZhenting(CurrentPlayerIndex);
            Debug.Log($"[Server] Distribute a tile {justDraw} to current turn player {CurrentPlayerIndex}, "
                + $"first turn: {CurrentRoundStatus.FirstTurn}.");
            availableOperations = GetOperations(CurrentPlayerIndex);
            if (SinglePlayerMode.Enabled)
            {
                Debug.Log($"[规则追踪][摸牌操作候选] 玩家={CurrentRoundStatus.GetPlayerName(CurrentPlayerIndex)}"
                    + $"(索引{CurrentPlayerIndex})，摸牌={justDraw}，癞子={MahjongSet.LaiziTile}，"
                    + $"是否尾部补牌={IsLingShang}，候选={string.Join("|", availableOperations.Select(op => op.Type))}");
                CurrentRoundStatus.LogAction(CurrentPlayerIndex, "摸牌", justDraw,
                    $"类型={(IsLingShang ? "牌堆尾部补牌" : "正常摸牌")}，"
                    + $"是否癞子={justDraw.EqualsIgnoreColor(MahjongSet.LaiziTile)}，"
                    + $"候选=[{string.Join("|", availableOperations.Select(op => op.Type))}]，"
                    + $"可摸剩余={MahjongSet.TilesRemain}（该牌尚未并入暗牌）");
            }
            if (SinglePlayerMode.IsAi(CurrentPlayerIndex) && availableOperations.Any(op => op.Type == InTurnOperationType.Tsumo))
            {
                HandleTsumo(availableOperations.First(op => op.Type == InTurnOperationType.Tsumo));
                return;
            }
            if (SinglePlayerMode.Enabled)
            {
                var human = SinglePlayerMode.HumanPlayerIndex;
                ClientBehaviour.Instance.RpcDrawTile(new EventMessages.DrawTileInfo
                {
                    PlayerIndex = human,
                    DrawPlayerIndex = CurrentPlayerIndex,
                    Tile = CurrentPlayerIndex == human ? justDraw : default(Tile),
                    BonusTurnTime = CurrentRoundStatus.GetBonusTurnTime(human),
                    Zhenting = CurrentRoundStatus.IsZhenting(human),
                    Operations = CurrentPlayerIndex == human ? availableOperations : null,
                    HandTiles = CurrentPlayerIndex == human
                        ? CurrentRoundStatus.HandTiles(CurrentPlayerIndex)
                        : null,
                    Rivers = CurrentRoundStatus.Rivers,
                    MahjongSetData = MahjongSet.Data
                });
                firstSendTime = Time.time;
                aiActionTime = Time.time + SinglePlayerMode.RandomAiDelay();
                serverTimeOut = gameSettings.BaseTurnTime + CurrentRoundStatus.GetBonusTurnTime(CurrentPlayerIndex)
                    + ServerConstants.ServerTimeBuffer;
                return;
            }
            var room = PhotonNetwork.CurrentRoom;
            for (int i = 0; i < players.Count; i++)
            {
                if (i == CurrentPlayerIndex) continue;
                var info = new EventMessages.DrawTileInfo
                {
                    PlayerIndex = i,
                    DrawPlayerIndex = CurrentPlayerIndex,
                    MahjongSetData = MahjongSet.Data
                };
                var player = CurrentRoundStatus.GetPlayer(i);
                ClientBehaviour.Instance.photonView.RPC("RpcDrawTile", player, info);
            }
            var currentPlayer = CurrentRoundStatus.GetPlayer(CurrentPlayerIndex);
            ClientBehaviour.Instance.photonView.RPC("RpcDrawTile", currentPlayer, new EventMessages.DrawTileInfo
            {
                PlayerIndex = CurrentPlayerIndex,
                DrawPlayerIndex = CurrentPlayerIndex,
                Tile = justDraw,
                BonusTurnTime = CurrentRoundStatus.GetBonusTurnTime(CurrentPlayerIndex),
                Zhenting = CurrentRoundStatus.IsZhenting(CurrentPlayerIndex),
                Operations = availableOperations,
                MahjongSetData = MahjongSet.Data
            });
            firstSendTime = Time.time;
            serverTimeOut = gameSettings.BaseTurnTime
                + CurrentRoundStatus.GetBonusTurnTime(CurrentPlayerIndex)
                + ServerConstants.ServerTimeBuffer;
        }

        /// <summary>根据摸牌后的完整手牌生成本回合可执行操作列表。</summary>
        private InTurnOperation[] GetOperations(int playerIndex)
        {
            var operations = new List<InTurnOperation> { new InTurnOperation { Type = InTurnOperationType.Discard } };
            // 检查自摸。
            TestTsumo(playerIndex, justDraw, operations);
            var handTiles = CurrentRoundStatus.HandTiles(playerIndex);
            var openMelds = CurrentRoundStatus.Melds(playerIndex);
            // 检查旧规则的九种幺九流局（单机配置已关闭）。
            Test9Orphans(handTiles, operations);
            // 检查立直（单机模式不使用）。
            TestRichi(playerIndex, handTiles, openMelds, operations);
            // 检查闷笑和回头笑。
            TestKongs(playerIndex, handTiles, operations);
            // 单机模式下 Bei 操作被复用为“杠癞子”。
            TestBei(playerIndex, handTiles, operations);
            return operations.ToArray();
        }

        /// <summary>检查普通自摸，并额外应用“最多一个癞子且癞子必须组成顺子”的单机限制。</summary>
        private void TestTsumo(int playerIndex, Tile tile, IList<InTurnOperation> operations)
        {
            var baseHandStatus = HandStatus.Tsumo;
            // 牌墙最后一张摸到后，保留旧规则的海底状态标记。
            if (MahjongSet.TilesRemain <= 0)
                baseHandStatus |= HandStatus.Haidi;
            // 笑牌后从尾部补摸时，保留岭上状态标记。
            if (IsLingShang) baseHandStatus |= HandStatus.Lingshang;
            var allTiles = MahjongSet.AllTiles;
            var doraTiles = MahjongSet.DoraIndicators.Select(
                indicator => MahjongLogic.GetDoraTile(indicator, allTiles)).ToArray();
            var uraDoraTiles = MahjongSet.UraDoraIndicators.Select(
                indicator => MahjongLogic.GetDoraTile(indicator, allTiles)).ToArray();
            var beiDora = CurrentRoundStatus.GetBeiDora(playerIndex);
            tsumoPointInfo = ServerMahjongLogic.GetPointInfo(
                playerIndex, CurrentRoundStatus, tile, baseHandStatus,
                doraTiles, uraDoraTiles, beiDora, gameSettings);
            // 单机模式只看标准牌型，不需要立直、役种或番数门槛。
            var handTiles = CurrentRoundStatus.HandTiles(playerIndex);
            var openMelds = CurrentRoundStatus.Melds(playerIndex);
            var canWin = MahjongLogic.HasWin(handTiles, openMelds, tile);
            if (SinglePlayerMode.Enabled)
            {
                var laizi = MahjongSet.LaiziTile;
                var laiziCount = handTiles
                    .Count(handTile => handTile.EqualsIgnoreColor(laizi));
                if (tile.EqualsIgnoreColor(laizi)) laiziCount++;
                var standardWin = MahjongLogic.HasStandardWin(handTiles, openMelds, tile);
                var laiziInSequence = laiziCount == 0
                    || MahjongLogic.HasStandardWinWithTileInSequence(
                        handTiles, openMelds, tile, laizi);
                canWin = laiziCount <= 1 && standardWin && laiziInSequence;
                Debug.Log($"[规则追踪][单机胡牌判定] 玩家={CurrentRoundStatus.GetPlayerName(playerIndex)}"
                    + $"(索引{playerIndex})，暗牌=[{string.Join("", handTiles)}]，摸牌={tile}，"
                    + $"癞子={laizi}，癞子数={laiziCount}，标准牌型={standardWin}，"
                    + $"癞子在顺子中={laiziInSequence}，最终可胡={canWin}");
            }
            if (canWin && (SinglePlayerMode.Enabled || gameSettings.CheckConstraint(tsumoPointInfo)))
            {
                operations.Add(new InTurnOperation
                {
                    Type = InTurnOperationType.Tsumo,
                    Tile = justDraw
                });
            }
        }

        private void Test9Orphans(Tile[] handTiles, IList<InTurnOperation> operations)
        {
            if (!gameSettings.Allow9OrphanDraw) return;
            if (!CurrentRoundStatus.FirstTurn) return;
            if (MahjongLogic.Test9KindsOfOrphans(handTiles, justDraw))
            {
                operations.Add(new InTurnOperation
                {
                    Type = InTurnOperationType.RoundDraw
                });
            }
        }

        private void TestRichi(int playerIndex, Tile[] handTiles, Meld[] openMelds, IList<InTurnOperation> operations)
        {
            if (SinglePlayerMode.Enabled) return;
            var alreadyRichied = CurrentRoundStatus.RichiStatus(playerIndex);
            if (alreadyRichied) return;
            var availability = gameSettings.AllowRichiWhenPointsLow || CurrentRoundStatus.GetPoints(playerIndex) >= gameSettings.RichiMortgagePoints;
            if (!availability) return;
            IList<Tile> availableTiles;
            if (MahjongLogic.TestRichi(handTiles, openMelds, justDraw, gameSettings.AllowRichiWhenNotReady, out availableTiles))
            {
                operations.Add(new InTurnOperation
                {
                    Type = InTurnOperationType.Richi,
                    RichiAvailableTiles = availableTiles.ToArray()
                });
            }
        }

        private void TestKongs(int playerIndex, Tile[] handTiles, IList<InTurnOperation> operations)
        {
            // 杠牌后必须从牌墙尾部补一张；最后一张已经摸走时不能再生成闷笑或回头笑按钮。
            if (SinglePlayerMode.Enabled && MahjongSet.TilesRemain <= 0) return;
            if (CurrentRoundStatus.KongClaimed == MahjongConstants.MaxKongs) return; // 全局已出现四次普通杠时不再生成杠候选
            if (SinglePlayerMode.Enabled
                && justDraw.EqualsIgnoreColor(MahjongSet.LaiziPreviousTile))
            {
                var previousTiles = handTiles
                    .Where(tile => tile.EqualsIgnoreColor(MahjongSet.LaiziPreviousTile))
                    .ToList();
                if (previousTiles.Count == 2)
                {
                    previousTiles.Add(justDraw);
                    operations.Add(new InTurnOperation
                    {
                        Type = InTurnOperationType.Kong,
                        Meld = new OpenMeld
                        {
                            Meld = new Meld(false, previousTiles.ToArray()),
                            Side = MeldSide.Self,
                            KongLocked = true,
                            IsLaiziPreviousKong = true
                        }
                    });
                    Debug.Log($"[规则追踪][赖子前位闷笑提示] 玩家={CurrentRoundStatus.GetPlayerName(playerIndex)}"
                        + $"(索引{playerIndex})，癞子={MahjongSet.LaiziTile}，前位牌={MahjongSet.LaiziPreviousTile}");
                }
            }
            var alreadyRichied = CurrentRoundStatus.RichiStatus(playerIndex);
            if (alreadyRichied)
            {
                // 联网旧规则：立直后只允许不改变听牌结构的暗杠。
                var richiKongs = MahjongLogic.GetRichiKongs(handTiles, justDraw);
                if (richiKongs.Any())
                {
                    foreach (var kong in richiKongs)
                    {
                        operations.Add(new InTurnOperation
                        {
                            Type = InTurnOperationType.Kong,
                            Meld = kong
                        });
                    }
                }
            }
            else
            {
                // 1. 四张相同牌组成闷笑。
                var selfKongs = MahjongLogic.GetSelfKongs(handTiles, justDraw);
                if (selfKongs.Any())
                {
                    foreach (var kong in selfKongs)
                    {
                        operations.Add(new InTurnOperation
                        {
                            Type = InTurnOperationType.Kong,
                            Meld = kong
                        });
                    }
                }
                // 2. 已碰的牌，并且第四张就是本回合刚摸到的牌时，组成回头笑。
                //    手里原有的同牌不会再提示；上一手放弃过的碰已被锁定。
                var addKongs = MahjongLogic.GetAddKongs(
                    CurrentRoundStatus.OpenMelds(playerIndex), justDraw);
                if (addKongs.Any())
                {
                    foreach (var kong in addKongs)
                    {
                        operations.Add(new InTurnOperation
                        {
                            Type = InTurnOperationType.Kong,
                            Meld = kong
                        });
                    }
                }
            }
        }

        /// <summary>单机模式下检测可选的癞子杠；手中只有一个癞子也允许玩家自行选择是否杠出。</summary>
        public void TestBei(int playerIndex, Tile[] handTiles, IList<InTurnOperation> operations)
        {
            if (SinglePlayerMode.Enabled)
            {
                // 癞子杠同样必须补牌；牌墙为空时保留最后摸到的牌，只允许自摸或正常出牌。
                if (MahjongSet.TilesRemain <= 0) return;
                var laizi = MahjongSet.LaiziTile;
                // 同时能杠四张相同牌时优先普通杠；四张癞子本身组成的杠不算普通杠，
                // 否则会挡住“四癞子喜钱”。
                var hasNormalKong = operations.Any(op => op.Type == InTurnOperationType.Kong
                    && op.Meld.Tiles != null
                    && op.Meld.Tiles.Length > 0
                    && !op.Meld.Tiles.All(tile => tile.EqualsIgnoreColor(laizi)));
                if (hasNormalKong) return;
                // 无论是本回合刚摸到，还是原本就在手中的癞子，都可以主动杠出；第一张也允许杠。
                var heldLaiziTile = handTiles.FirstOrDefault(tile => tile.EqualsIgnoreColor(laizi));
                var drawnLaizi = justDraw.EqualsIgnoreColor(laizi);
                if ((heldLaiziTile.Rank > 0 || drawnLaizi)
                    && operations.All(op => op.Type != InTurnOperationType.Bei))
                    operations.Add(new InTurnOperation
                    {
                        Type = InTurnOperationType.Bei,
                        // 优先指定刚补摸的癞子，确保连续摸到癞子时可以连续杠，不受客户端手牌快照刷新影响。
                        Tile = drawnLaizi ? justDraw : heldLaiziTile
                    });
                return;
            }
            if (!gameSettings.AllowBeiDora) return;
            var beiTile = new Tile(Suit.Z, 4);
            int bei = handTiles.Count(tile => tile.EqualsIgnoreColor(beiTile));
            if (bei > 0)
            {
                operations.Add(new InTurnOperation
                {
                    Type = InTurnOperationType.Bei
                });
            }
            else if (justDraw.EqualsIgnoreColor(beiTile))
            {
                operations.Add(new InTurnOperation
                {
                    Type = InTurnOperationType.Bei
                });
            }
        }

        private void OnDiscardTileEvent(EventMessages.DiscardTileInfo info)
        {
            if (info.PlayerIndex != CurrentRoundStatus.CurrentPlayerIndex)
            {
                Debug.Log($"[Server] It is not player {info.PlayerIndex}'s turn to discard a tile, ignoring this message");
                return;
            }
            // 本回合能回头笑却选择出牌时，锁定该碰。
            LockDeclinedAddKongs();
            // 进入出牌状态，由该状态立即更新手牌和桌面河牌。
            ServerBehaviour.Instance.DiscardTile(
                info.PlayerIndex, info.Tile, info.IsRichiing,
                info.DiscardingLastDraw, info.BonusTurnTime, TurnDoraAfterDiscard);
        }

        private void OnInTurnOperationEvent(EventMessages.InTurnOperationInfo info)
        {
            if (info.PlayerIndex != CurrentRoundStatus.CurrentPlayerIndex)
            {
                Debug.Log($"[Server] It is not player {info.PlayerIndex}'s turn to perform a in turn operation, ignoring this message");
                return;
            }
            // 按按钮类型分派到对应规则处理。
            var operation = info.Operation;
            if (SinglePlayerMode.Enabled)
                CurrentRoundStatus.LogAction(info.PlayerIndex, "回合内操作选择", operation.Tile.Rank > 0
                    ? (Tile?)operation.Tile : justDraw, $"选择={operation.Type}");
            switch (operation.Type)
            {
                case InTurnOperationType.Tsumo:
                    HandleTsumo(operation);
                    break;
                case InTurnOperationType.Kong:
                    HandleKong(operation);
                    break;
                case InTurnOperationType.RoundDraw:
                    HandleRoundDraw(operation);
                    break;
                case InTurnOperationType.Bei:
                    HandleBei(operation);
                    break;
                default:
                    Debug.LogError($"[Server] This type of in turn operation should not be sent to server.");
                    break;
            }
        }

        private void HandleTsumo(InTurnOperation operation)
        {
            int playerIndex = CurrentRoundStatus.CurrentPlayerIndex;
            ServerBehaviour.Instance.Tsumo(playerIndex, operation.Tile, tsumoPointInfo);
        }

        private void HandleKong(InTurnOperation operation)
        {
            int playerIndex = CurrentRoundStatus.CurrentPlayerIndex;
            var kong = operation.Meld;
            Debug.Log($"Server is handling the operation of player {playerIndex} of claiming kong {kong}");
            ServerBehaviour.Instance.Kong(playerIndex, kong);
        }

        private void HandleRoundDraw(InTurnOperation operation)
        {
            int playerIndex = CurrentRoundStatus.CurrentPlayerIndex;
            Debug.Log($"Player {playerIndex} has claimed 9-orphans");
            ServerBehaviour.Instance.RoundDraw(RoundDrawType.NineOrphans);
        }

        /// <summary>执行癞子杠：先合并本次摸牌，再移除一个真实癞子，放入出牌区并从牌墙尾部补牌。</summary>
        private void HandleBei(InTurnOperation operation)
        {
            int playerIndex = CurrentRoundStatus.CurrentPlayerIndex;
            if (SinglePlayerMode.Enabled)
            {
                // 先把刚摸牌并入暗牌，再精确移除一个癞子；这样杠旧癞子时不会误删本回合摸到的普通牌。
                CurrentRoundStatus.AddTile(playerIndex, justDraw);
                CurrentRoundStatus.LastDraw = null;
                // 必须移除手中真实的 Tile 实例，直接构造一张牌可能因赤牌颜色标记不同而匹配失败。
                var actualLaizi = CurrentRoundStatus.HandTiles(playerIndex)
                    .FirstOrDefault(tile => tile.EqualsIgnoreColor(MahjongSet.LaiziTile));
                if (actualLaizi.Rank <= 0)
                {
                    Debug.LogError($"Player {playerIndex} requested a laizi kong but no laizi exists in hand.");
                    return;
                }
                CurrentRoundStatus.RemoveTile(playerIndex, actualLaizi);
                CurrentRoundStatus.AddToRiver(playerIndex, actualLaizi, false, true);
                CurrentRoundStatus.SortHandTiles();
                CurrentRoundStatus.AddLaiziKong(playerIndex, actualLaizi);
                ServerBehaviour.Instance.DrawTile(playerIndex, true, false);
                return;
            }
            Debug.Log($"Player {playerIndex} has claimed a bei-dora");
            ServerBehaviour.Instance.BeiDora(playerIndex);
        }

        public override void OnStateUpdate()
        {
            if (SinglePlayerMode.IsAi(CurrentPlayerIndex) && !aiActed && Time.time >= aiActionTime)
            {
                aiActed = true;
                var operation = SinglePlayerMode.ChooseInTurnOperation(CurrentRoundStatus,
                    CurrentPlayerIndex, availableOperations, justDraw, MahjongSet.LaiziTile);
                Debug.Log($"[规则追踪][AI摸牌决策] 玩家={CurrentRoundStatus.GetPlayerName(CurrentPlayerIndex)}"
                    + $"(索引{CurrentPlayerIndex})，选择={operation.Type}，摸牌={justDraw}");
                CurrentRoundStatus.LogAction(CurrentPlayerIndex, "AI回合内决策", justDraw,
                    $"候选=[{string.Join("|", availableOperations.Select(op => op.Type))}]，选择={operation.Type}");
                if (operation.Type == InTurnOperationType.Tsumo)
                    HandleTsumo(operation);
                else if (operation.Type == InTurnOperationType.Kong)
                    HandleKong(operation);
                else if (operation.Type == InTurnOperationType.Bei)
                    HandleBei(operation);
                else
                {
                    var isRichiing = operation.Type == InTurnOperationType.Richi;
                    var discard = isRichiing && operation.RichiAvailableTiles != null &&
                        operation.RichiAvailableTiles.Length > 0
                        ? operation.RichiAvailableTiles[0]
                        : SinglePlayerMode.ChooseDiscard(CurrentRoundStatus, CurrentPlayerIndex,
                            justDraw, null, MahjongSet.LaiziTile);
                    LockDeclinedAddKongs();
                    ServerBehaviour.Instance.DiscardTile(CurrentPlayerIndex, discard, isRichiing,
                        discard.EqualsConsiderColor(justDraw), 0, TurnDoraAfterDiscard);
                }
                return;
            }
            // 真人超时后自动打出本回合摸到的牌，防止状态机停住。
            if (Time.time - firstSendTime > serverTimeOut)
            {
                // 强制出牌不触发立直，并且必须避开癞子；摸到癞子时改打手中的其他牌。
                var discard = SinglePlayerMode.Enabled
                    ? SinglePlayerMode.ChooseDiscard(CurrentRoundStatus, CurrentPlayerIndex,
                        justDraw, null, MahjongSet.LaiziTile)
                    : (Tile)CurrentRoundStatus.LastDraw;
                if (discard.Rank > 0)
                {
                    LockDeclinedAddKongs();
                    ServerBehaviour.Instance.DiscardTile(CurrentPlayerIndex, discard, false,
                        discard.EqualsConsiderColor(justDraw), 0, TurnDoraAfterDiscard);
                }
            }
        }

        /// <summary>本回合给出了回头笑却没有杠。锁定对应的碰，下一轮不能再补。</summary>
        private void LockDeclinedAddKongs()
        {
            if (availableOperations == null) return;
            foreach (var operation in availableOperations)
            {
                if (operation.Type != InTurnOperationType.Kong || !operation.Meld.IsAdded) continue;
                CurrentRoundStatus.LockDeclinedAddKong(CurrentPlayerIndex, operation.Meld.First);
            }
        }

        public override void OnServerStateExit()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
            CurrentRoundStatus.CheckOneShot(CurrentPlayerIndex);
        }

        public void OnEvent(EventData photonEvent)
        {
            var code = photonEvent.Code;
            var info = photonEvent.CustomData;
            Debug.Log($"{GetType().Name} receives event code: {code} with content {info}");
            switch (code)
            {
                case EventMessages.DiscardTileEvent:
                    OnDiscardTileEvent((EventMessages.DiscardTileInfo)info);
                    break;
                case EventMessages.InTurnOperationEvent:
                    OnInTurnOperationEvent((EventMessages.InTurnOperationInfo)info);
                    break;
            }
        }
    }
}

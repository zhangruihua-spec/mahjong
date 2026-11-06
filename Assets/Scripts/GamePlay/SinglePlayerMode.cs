using System;
using System.Collections.Generic;
using System.Linq;
using GamePlay.Server.Model;
using Mahjong.Model;
using UnityEngine;

namespace GamePlay.SinglePlayer
{
    /// <summary>
    /// 单机模式的统一入口：负责创建默认规则、区分真人与 AI，并集中处理 AI 的出牌和操作选择。
    /// 这里仅负责“作出选择”，真正的摸牌、出牌和结算仍由服务器状态机执行。
    /// </summary>
    public static class SinglePlayerMode
    {
        /// <summary>返回 2～5 秒的随机思考时间，让三个 AI 的操作节奏更接近真人。</summary>
        public static float RandomAiDelay()
        {
            return UnityEngine.Random.Range(2f, 5f);
        }
        public const int HumanPlayerIndex = 0;
        public const int PlayerCount = 4;
        public const int DefaultInitialPoints = 10000;
        public const int DefaultBasePoint = 10;
        public static bool Enabled { get; set; } = true;

        public static readonly string[] PlayerNames =
        {
            "Player", "AI - East", "AI - South", "AI - West"
        };

        /// <summary>创建潜江麻将单机局的默认配置：四人、初始 10000 分、底分 10、禁吃可碰。</summary>
        public static GameSetting CreateDefaultSettings()
        {
            return new GameSetting
            {
                GameMode = GameMode.Normal,
                GamePlayers = GamePlayers.Four,
                RoundCount = RoundCount.ES,
                MinimumFanConstraintType = MinimumFanConstraintType.One,
                PointsToGameEnd = PointsToGameEnd.Negative,
                GameEndsWhenAllLastTop = true,
                InitialPoints = DefaultInitialPoints,
                FirstPlacePoints = 30000,
                BasePoint = DefaultBasePoint,
                AllowHint = true,
                InitialDoraCount = InitialDoraCount.One,
                RichiMortgagePoints = 1000,
                ExtraRoundBonusPerPlayer = 300,
                NotReadyPunishPerPlayer = 1000,
                FalseRichiPunishPerPlayer = 1000,
                AllowMultipleRong = true,
                Allow3RongDraw = true,
                Allow4RichiDraw = false,
                Allow4KongDraw = false,
                Allow4WindDraw = false,
                Allow9OrphanDraw = false,
                AllowChows = false,
                AllowPongs = true,
                BaseTurnTime = 15,
                BonusTurnTime = 20
            };
        }

        public static bool IsAi(int playerIndex) => Enabled && playerIndex != HumanPlayerIndex;

        /// <summary>
        /// 为 AI 选择弃牌。先比较向听数，再比较有效进张、牌型结构和孤张紧迫度，避免单纯随机出牌。
        /// </summary>
        public static Tile ChooseDiscard(ServerRoundStatus status, int playerIndex, Tile? drawnTile = null,
            Tile[] forbiddenTiles = null, Tile? knownLaiziTile = null)
        {
            var tiles = status.HandTiles(playerIndex).ToList();
            if (drawnTile.HasValue) tiles.Add(drawnTile.Value);
            var laizi = knownLaiziTile.HasValue && knownLaiziTile.Value.Rank > 0
                ? knownLaiziTile.Value
                : GetKnownLaizi(status, playerIndex);
            // 单机规则：癞子可以保留或执行“癞子杠”，但绝不能作为普通弃牌打出。
            var allowed = tiles.Where(tile =>
                (laizi.Rank <= 0 || !tile.EqualsIgnoreColor(laizi))
                && (forbiddenTiles == null
                    || !forbiddenTiles.Any(forbidden => forbidden.EqualsIgnoreColor(tile))))
                .ToArray();
            if (allowed.Length == 0)
            {
                Debug.LogError($"[规则追踪][禁止打癞子] 玩家={status.GetPlayerName(playerIndex)}"
                    + "没有可供普通打出的非癞子牌，应改为执行癞子杠。");
                return default(Tile);
            }

            var openMeldCount = status.OpenMelds(playerIndex).Length;
            var candidates = allowed
                .GroupBy(Tile.GetIndex)
                .Select(group => group.First())
                .Select(tile => EvaluateDiscard(tile, tiles, openMeldCount, laizi))
                .OrderBy(result => result.Shanten)
                .ThenByDescending(result => result.Ukeire)
                .ThenByDescending(result => result.ShapeScore)
                .ThenByDescending(result => result.DiscardUrgency)
                .ToArray();
            var best = candidates[0];
            var candidateSummary = string.Join("；", candidates.Take(6)
                .Select(result => string.Format("{0}:向听{1}/进张{2}/形{3}",
                    result.Tile, result.Shanten, result.Ukeire, result.ShapeScore)).ToArray());
            Debug.Log($"[AI策略][出牌评估] 玩家={status.GetPlayerName(playerIndex)}(索引{playerIndex})，"
                + $"选择={best.Tile}，向听={best.Shanten}，有效进张={best.Ukeire}，牌型分={best.ShapeScore}；"
                + $"候选=[{candidateSummary}]");
            return best.Tile;
        }

        /// <summary>
        /// 为真人玩家计算“当前最建议打出的牌”。按照潜江麻将的实际胡牌约束，依次比较：
        /// 规则向听数、合法自摸张数、真实有效进张、进张种类、癞子成句条件和牌型质量。
        /// visibleTileCounts 包含手牌与桌面公开牌，用来排除已经看见、实际不可能再摸到的牌。
        /// </summary>
        public static Tile? ChooseHintDiscard(IList<Tile> handTiles, Tile? drawnTile,
            Tile laiziTile, Tile laiziPreviousTile, IList<Tile> forbiddenTiles = null, int openMeldCount = 0,
            int[] visibleTileCounts = null)
        {
            return AnalyzeHintDiscard(handTiles, drawnTile, laiziTile, laiziPreviousTile,
                forbiddenTiles, openMeldCount, visibleTileCounts).RecommendedDiscard;
        }

        /// <summary>
        /// 返回最佳弃牌以及打出后所有符合本地自摸规则的胡牌张数。
        /// 每种牌的剩余数量按“四张减去手牌和桌面已公开牌”计算。
        /// </summary>
        public static HintDiscardAnalysis AnalyzeHintDiscard(IList<Tile> handTiles, Tile? drawnTile,
            Tile laiziTile, Tile laiziPreviousTile, IList<Tile> forbiddenTiles = null, int openMeldCount = 0,
            int[] visibleTileCounts = null)
        {
            if (handTiles == null) return default(HintDiscardAnalysis);
            var tiles = handTiles.ToList();
            if (drawnTile.HasValue) tiles.Add(drawnTile.Value);
            if (tiles.Count == 0) return default(HintDiscardAnalysis);

            // 提示箭头同样不能推荐癞子；癞子只能保留或通过专用按钮杠出。
            var allowed = tiles.Where(tile =>
                (laiziTile.Rank <= 0 || !tile.EqualsIgnoreColor(laiziTile))
                && (forbiddenTiles == null
                    || !forbiddenTiles.Any(forbidden => forbidden.EqualsIgnoreColor(tile))))
                .ToList();
            if (allowed.Count == 0) return default(HintDiscardAnalysis);
            var knownCounts = visibleTileCounts == null
                ? ToCounts(tiles)
                : (int[])visibleTileCounts.Clone();
            var ownCounts = ToCounts(tiles);
            for (int index = 0; index < 27; index++)
                knownCounts[index] = Math.Max(knownCounts[index], ownCounts[index]);
            var candidates = allowed
                .GroupBy(Tile.GetIndex)
                .Select(group => group.First())
                .Select(tile => EvaluateHintDiscard(tile, tiles, openMeldCount,
                    laiziTile, laiziPreviousTile, knownCounts))
                .OrderBy(result => result.RuleShanten)
                .ThenByDescending(result => result.WinningOuts)
                .ThenByDescending(result => result.StrategicScore)
                .ThenByDescending(result => result.Ukeire)
                .ThenByDescending(result => result.UkeireKinds)
                .ThenByDescending(result => result.LaiziSequenceScore)
                .ThenByDescending(result => result.SpecialRuleScore)
                .ThenByDescending(result => result.ShapeScore)
                .ThenByDescending(result => result.DiscardUrgency)
                .ToArray();
            if (candidates.Length == 0) return default(HintDiscardAnalysis);
            var summary = string.Join("；", candidates.Take(8).Select(result =>
                $"{result.Tile}:规则向听{result.RuleShanten}/胡{result.WinningOuts}/"
                + $"进张{result.Ukeire}({result.UkeireKinds}种)/综合{result.StrategicScore}/破坏{result.StructureBreakPenalty}"));
            Debug.Log($"[出牌提示][综合评估] 推荐={candidates[0].Tile}，候选=[{summary}]");
            var best = candidates[0];
            return new HintDiscardAnalysis
            {
                RecommendedDiscard = best.Tile,
                RuleShanten = best.RuleShanten,
                WinningOuts = best.WinningOuts,
                WinningTiles = best.WinningTiles ?? new WinningTileRemaining[0]
            };
        }

        /// <summary>按单机规则评估打出一张牌后的真实进张质量。</summary>
        private static HintDiscardEvaluation EvaluateHintDiscard(Tile discard, IList<Tile> tiles,
            int openMeldCount, Tile laiziTile, Tile laiziPreviousTile, int[] knownCounts)
        {
            var remainingHand = RemoveOne(tiles, discard);
            var baseShanten = CalculateShanten(remainingHand, openMeldCount);
            var ruleShanten = CalculateRuleShanten(remainingHand, openMeldCount, laiziTile, baseShanten);
            var winningOuts = 0;
            var winningTiles = new List<WinningTileRemaining>();
            var ukeire = 0;
            var ukeireKinds = 0;

            // 只遍历万、筒、条 27 种牌，并按桌面真实未见张数加权。
            for (int index = 0; index < 27; index++)
            {
                var copiesLeft = Math.Max(0, 4 - knownCounts[index]);
                var testHand = new List<Tile>(remainingHand) { Tile.GetTile(index) };
                var legalWin = IsLegalSinglePlayerWin(testHand, openMeldCount, laiziTile);
                if (legalWin)
                {
                    winningOuts += copiesLeft;
                    winningTiles.Add(new WinningTileRemaining
                    {
                        Tile = Tile.GetTile(index),
                        CopiesLeft = copiesLeft
                    });
                }
                if (copiesLeft == 0) continue;
                var nextBaseShanten = CalculateShanten(testHand, openMeldCount);
                var nextRuleShanten = legalWin
                    ? -1
                    : CalculateRuleShanten(testHand, openMeldCount, laiziTile,
                        Math.Max(0, nextBaseShanten));
                if (nextRuleShanten < ruleShanten)
                {
                    ukeire += copiesLeft;
                    ukeireKinds++;
                }
            }

            var laiziSequenceScore = CalculateLaiziSequenceScore(remainingHand, laiziTile);
            var specialRuleScore = CalculateSpecialRuleScore(remainingHand, laiziPreviousTile);
            var shapeScore = CalculateShapeScore(remainingHand, laiziTile);
            var structureBreakPenalty = CalculateStructureBreakPenalty(discard, tiles);
            // 向听相同的情况下，不再让少量进张差绝对压过对子、刻子和笑牌胚子。
            // 实际进张仍然最重要，但要与牌型完整度和本地计分规则一起比较。
            var strategicScore = ukeire * 4 + ukeireKinds * 3
                + shapeScore + laiziSequenceScore + specialRuleScore
                - structureBreakPenalty;
            return new HintDiscardEvaluation
            {
                Tile = discard,
                RuleShanten = ruleShanten,
                WinningOuts = winningOuts,
                WinningTiles = winningTiles.ToArray(),
                Ukeire = ukeire,
                UkeireKinds = ukeireKinds,
                LaiziSequenceScore = laiziSequenceScore,
                SpecialRuleScore = specialRuleScore,
                ShapeScore = shapeScore,
                StructureBreakPenalty = structureBreakPenalty,
                StrategicScore = strategicScore,
                DiscardUrgency = CalculateDiscardUrgency(discard, tiles, laiziTile)
            };
        }

        /// <summary>
        /// 计算打牌对重复牌结构造成的损失。对子既能作将，也能碰牌；刻子还能形成点笑或闷笑，
        /// 因此不能仅为少量进张就随意拆掉。三对以上时对子略有冗余，惩罚会适当降低。
        /// </summary>
        private static int CalculateStructureBreakPenalty(Tile discard, IList<Tile> tiles)
        {
            var sameCount = tiles.Count(tile => tile.EqualsIgnoreColor(discard));
            if (sameCount < 2) return 0;
            if (sameCount >= 4) return 60;
            if (sameCount == 3) return 44;
            var pairKinds = tiles.GroupBy(Tile.GetIndex).Count(group => group.Count() >= 2);
            return pairKinds >= 3 ? 16 : 28;
        }

        public static InTurnOperation ChooseInTurnOperation(InTurnOperation[] operations)
        {
            foreach (var type in new[] { InTurnOperationType.Tsumo, InTurnOperationType.Kong,
                         InTurnOperationType.Bei, InTurnOperationType.Richi })
                if (operations.Any(op => op.Type == type))
                    return operations.First(op => op.Type == type);
            return new InTurnOperation { Type = InTurnOperationType.Discard };
        }

        /// <summary>选择当前回合操作：能自摸时直接胡，其次处理普通笑，再按牌型决定是否杠癞子。</summary>
        public static InTurnOperation ChooseInTurnOperation(ServerRoundStatus status, int playerIndex,
            InTurnOperation[] operations, Tile? drawnTile, Tile laiziTile)
        {
            var tsumo = operations.FirstOrDefault(op => op.Type == InTurnOperationType.Tsumo);
            if (operations.Any(op => op.Type == InTurnOperationType.Tsumo)) return tsumo;

            // 普通闷笑或回头笑既能获得笑牌收益，又能从牌墙尾部补牌，优先级高于可选的癞子杠。
            // 注意：四张癞子本身组成的杠不参与“普通杠优先”，否则会挡住四癞子喜钱。
            var kong = operations.FirstOrDefault(op => op.Type == InTurnOperationType.Kong
                && !IsLaiziOnlyKong(op, laiziTile));
            if (kong.Type == InTurnOperationType.Kong) return kong;

            var bei = operations.FirstOrDefault(op => op.Type == InTurnOperationType.Bei);
            if (operations.Any(op => op.Type == InTurnOperationType.Bei)
                && ShouldKongLaizi(status, playerIndex, drawnTile, laiziTile))
                return bei;

            // 没有普通杠可选时，如果只剩癞子四张组成的杠，也执行杠，避免有杠却直接弃牌。
            var anyKong = operations.FirstOrDefault(op => op.Type == InTurnOperationType.Kong);
            if (anyKong.Type == InTurnOperationType.Kong) return anyKong;

            return new InTurnOperation { Type = InTurnOperationType.Discard };
        }

        public static OutTurnOperation ChooseOutTurnOperation(OutTurnOperation[] operations)
        {
            foreach (var type in new[] { OutTurnOperationType.Rong, OutTurnOperationType.Kong,
                         OutTurnOperationType.Pong, OutTurnOperationType.Chow })
                if (operations.Any(op => op.Type == type))
                    return operations.First(op => op.Type == type);
            return new OutTurnOperation { Type = OutTurnOperationType.Skip };
        }

        /// <summary>响应别人打出的牌；依次判断点笑和碰，当前单机规则不会接炮胡牌。</summary>
        public static OutTurnOperation ChooseOutTurnOperation(ServerRoundStatus status, int playerIndex,
            OutTurnOperation[] operations, Tile laiziPreviousTile)
        {
            var rong = operations.FirstOrDefault(op => op.Type == OutTurnOperationType.Rong);
            if (operations.Any(op => op.Type == OutTurnOperationType.Rong)) return rong;

            // 点笑会记录结算收入并获得一次尾部补牌，因此当前规则下 AI 会优先执行点笑。
            var kong = operations.FirstOrDefault(op => op.Type == OutTurnOperationType.Kong);
            if (operations.Any(op => op.Type == OutTurnOperationType.Kong)) return kong;

            var pongCandidates = operations.Where(op => op.Type == OutTurnOperationType.Pong).ToArray();
            foreach (var pong in pongCandidates)
            {
                var hasPreviousTileReward = laiziPreviousTile.Rank > 0
                    && pong.Tile.EqualsIgnoreColor(laiziPreviousTile);
                if (ShouldPong(status, playerIndex, pong, hasPreviousTileReward))
                    return pong;
            }
            return new OutTurnOperation { Type = OutTurnOperationType.Skip };
        }

        private static Tile GetKnownLaizi(ServerRoundStatus status, int playerIndex)
        {
            // 服务器侧的弃牌接口没有直接传入 MahjongSet，所以尝试从河牌中已标亮或已杠出的牌反推癞子。
            // 如果仍无法确认，则返回无效牌；普通向听数计算不依赖癞子信息，仍可正常工作。
            foreach (var river in status.Rivers)
            {
                if (river.River == null) continue;
                foreach (var riverTile in river.River)
                    if (riverTile.IsLaizi || riverTile.IsLaiziKong)
                        return riverTile.Tile;
            }
            return default(Tile);
        }

        /// <summary>判断该杠是否完全由癞子牌组成；这种杠不参与“普通杠优先”的判断。</summary>
        private static bool IsLaiziOnlyKong(InTurnOperation operation, Tile laiziTile)
        {
            if (operation.Type != InTurnOperationType.Kong) return false;
            var tiles = operation.Meld.Tiles;
            return tiles != null && tiles.Length > 0
                && tiles.All(tile => tile.EqualsIgnoreColor(laiziTile));
        }

        private static bool ShouldKongLaizi(ServerRoundStatus status, int playerIndex,
            Tile? drawnTile, Tile laiziTile)
        {
            if (laiziTile.Rank <= 0) return false;
            var tiles = status.HandTiles(playerIndex).ToList();
            if (drawnTile.HasValue) tiles.Add(drawnTile.Value);
            var count = tiles.Count(tile => tile.EqualsIgnoreColor(laiziTile));
            // 持有两张及以上癞子时优先杠掉，确保最终胡牌时手中最多只保留一张。
            if (count >= 2)
            {
                Debug.Log($"[AI策略][癞子杠] 玩家={status.GetPlayerName(playerIndex)}，"
                    + $"癞子={laiziTile}，持有={count}，选择杠（保留数量不能超过1）");
                return true;
            }
            if (count == 0) return false;

            // 癞子不能变牌，只按自身牌面参与组牌；已经成句或有顺子搭子时尽量保留。
            var canCompleteSequence = CanParticipateInCompleteSequence(laiziTile, tiles);
            var hasSequenceSupport = HasSequenceSupport(laiziTile, tiles);
            var shouldKong = !canCompleteSequence && !hasSequenceSupport;
            Debug.Log($"[AI策略][癞子杠] 玩家={status.GetPlayerName(playerIndex)}，癞子={laiziTile}，"
                + $"已成顺子可能={canCompleteSequence}，顺子搭子={hasSequenceSupport}，"
                + $"选择={(shouldKong ? "杠" : "保留")}");
            return shouldKong;
        }

        /// <summary>比较碰牌前后的向听数和牌型分，只有明显改善牌型或触发前位碰收益时才碰。</summary>
        private static bool ShouldPong(ServerRoundStatus status, int playerIndex,
            OutTurnOperation pong, bool hasPreviousTileReward)
        {
            var hand = status.HandTiles(playerIndex).ToList();
            var openMeldCount = status.OpenMelds(playerIndex).Length;
            var beforeShanten = CalculateShanten(hand, openMeldCount);
            var beforeShape = CalculateShapeScore(hand, default(Tile));

            var afterCall = new List<Tile>(hand);
            for (int removed = 0; removed < 2; removed++)
            {
                var index = afterCall.FindIndex(tile => tile.EqualsIgnoreColor(pong.Tile));
                if (index < 0) return false;
                afterCall.RemoveAt(index);
            }

            var allowedDiscards = afterCall.Where(tile => pong.ForbiddenTiles == null
                || !pong.ForbiddenTiles.Any(forbidden => forbidden.EqualsIgnoreColor(tile)))
                .GroupBy(Tile.GetIndex).Select(group => group.First()).ToArray();
            if (allowedDiscards.Length == 0) allowedDiscards = afterCall.ToArray();
            var bestAfter = allowedDiscards.Select(discard =>
            {
                var remaining = RemoveOne(afterCall, discard);
                return new
                {
                    Shanten = CalculateShanten(remaining, openMeldCount + 1),
                    Shape = CalculateShapeScore(remaining, default(Tile))
                };
            }).OrderBy(result => result.Shanten).ThenByDescending(result => result.Shape).First();

            var accept = bestAfter.Shanten < beforeShanten
                || (bestAfter.Shanten == beforeShanten
                    && (hasPreviousTileReward || bestAfter.Shape >= beforeShape + 6));
            Debug.Log($"[AI策略][碰牌评估] 玩家={status.GetPlayerName(playerIndex)}，牌={pong.Tile}，"
                + $"碰前向听={beforeShanten}/牌型分={beforeShape}，"
                + $"碰后向听={bestAfter.Shanten}/牌型分={bestAfter.Shape}，"
                + $"前位碰收益={hasPreviousTileReward}，选择={(accept ? "碰" : "过")}");
            return accept;
        }

        private static DiscardEvaluation EvaluateDiscard(Tile discard, IList<Tile> tiles,
            int openMeldCount, Tile laiziTile)
        {
            var remaining = RemoveOne(tiles, discard);
            var shanten = CalculateShanten(remaining, openMeldCount);
            return new DiscardEvaluation
            {
                Tile = discard,
                Shanten = shanten,
                Ukeire = CalculateUkeire(remaining, openMeldCount, shanten),
                ShapeScore = CalculateShapeScore(remaining, laiziTile),
                DiscardUrgency = CalculateDiscardUrgency(discard, tiles, laiziTile)
            };
        }

        private static List<Tile> RemoveOne(IList<Tile> tiles, Tile target)
        {
            var result = new List<Tile>(tiles);
            var index = result.FindIndex(tile => tile.EqualsConsiderColor(target));
            if (index < 0) index = result.FindIndex(tile => tile.EqualsIgnoreColor(target));
            if (index >= 0) result.RemoveAt(index);
            return result;
        }

        private static int CalculateUkeire(IList<Tile> hand, int openMeldCount, int currentShanten)
        {
            var counts = ToCounts(hand);
            var result = 0;
            // 单机牌墙只有万、筒、条三门数牌，因此只统计前 27 种牌的有效进张。
            for (int index = 0; index < 27; index++)
            {
                if (counts[index] >= 4) continue;
                var test = new List<Tile>(hand) { Tile.GetTile(index) };
                if (CalculateShanten(test, openMeldCount) < currentShanten)
                    result += 4 - counts[index];
            }
            return result;
        }

        private static int CalculateShanten(IList<Tile> tiles, int openMeldCount)
        {
            var counts = ToCounts(tiles);
            var minimum = 8;
            SearchShanten(counts, Math.Min(4, openMeldCount), 0, 0, ref minimum);
            return minimum;
        }

        private static void SearchShanten(int[] counts, int melds, int pairs, int partials,
            ref int minimum)
        {
            var index = Array.FindIndex(counts, count => count > 0);
            if (index < 0)
            {
                var usablePartials = Math.Min(partials, Math.Max(0, 4 - melds));
                minimum = Math.Min(minimum, 8 - melds * 2 - usablePartials - Math.Min(1, pairs));
                return;
            }

            // 先尝试把当前牌当作孤张跳过；牌组互相重叠时，这条分支可保证搜索到更优拆分。
            counts[index]--;
            SearchShanten(counts, melds, pairs, partials, ref minimum);
            counts[index]++;

            if (melds < 4 && counts[index] >= 3)
            {
                counts[index] -= 3;
                SearchShanten(counts, melds + 1, pairs, partials, ref minimum);
                counts[index] += 3;
            }
            if (melds < 4 && IsNumbered(index) && RankIndex(index) <= 6
                && counts[index + 1] > 0 && counts[index + 2] > 0)
            {
                counts[index]--; counts[index + 1]--; counts[index + 2]--;
                SearchShanten(counts, melds + 1, pairs, partials, ref minimum);
                counts[index]++; counts[index + 1]++; counts[index + 2]++;
            }
            if (counts[index] >= 2)
            {
                counts[index] -= 2;
                if (pairs == 0)
                    SearchShanten(counts, melds, 1, partials, ref minimum);
                SearchShanten(counts, melds, pairs, partials + 1, ref minimum);
                counts[index] += 2;
            }
            if (IsNumbered(index) && RankIndex(index) <= 7 && counts[index + 1] > 0)
            {
                counts[index]--; counts[index + 1]--;
                SearchShanten(counts, melds, pairs, partials + 1, ref minimum);
                counts[index]++; counts[index + 1]++;
            }
            if (IsNumbered(index) && RankIndex(index) <= 6 && counts[index + 2] > 0)
            {
                counts[index]--; counts[index + 2]--;
                SearchShanten(counts, melds, pairs, partials + 1, ref minimum);
                counts[index]++; counts[index + 2]++;
            }
        }

        private static int[] ToCounts(IList<Tile> tiles)
        {
            var counts = new int[34];
            foreach (var tile in tiles)
            {
                var index = Tile.GetIndex(tile);
                if (index >= 0 && index < counts.Length) counts[index]++;
            }
            return counts;
        }

        /// <summary>按对子、刻子、相邻牌、嵌张和中张价值计算牌型质量分，分数越高越值得保留。</summary>
        private static int CalculateShapeScore(IList<Tile> tiles, Tile laiziTile)
        {
            var counts = ToCounts(tiles);
            var score = 0;
            for (int index = 0; index < counts.Length; index++)
            {
                var count = counts[index];
                if (count == 0) continue;
                if (count >= 2) score += 10;
                if (count >= 3) score += 18;
                if (!IsNumbered(index)) continue;
                var rank = RankIndex(index);
                if (rank > 0) score += count * counts[index - 1] * 4;
                if (rank < 8) score += count * counts[index + 1] * 4;
                if (rank > 1) score += count * counts[index - 2] * 2;
                if (rank < 7) score += count * counts[index + 2] * 2;
                score += count * (4 - Math.Abs(4 - rank));
            }
            if (laiziTile.Rank > 0 && tiles.Any(tile => tile.EqualsIgnoreColor(laiziTile)))
            {
                if (CanParticipateInCompleteSequence(laiziTile, tiles)) score += 30;
                else if (HasSequenceSupport(laiziTile, tiles)) score += 12;
                else score -= 8;
            }
            return score;
        }

        private static int CalculateDiscardUrgency(Tile tile, IList<Tile> hand, Tile laiziTile)
        {
            if (laiziTile.Rank > 0 && tile.EqualsIgnoreColor(laiziTile)) return -100;
            var same = hand.Count(other => other.EqualsIgnoreColor(tile));
            var connections = hand.Count(other => other.Suit == tile.Suit
                && !other.EqualsConsiderColor(tile)
                && Math.Abs(other.Rank - tile.Rank) <= 2);
            var urgency = -same * 18 - connections * 6;
            if (connections == 0) urgency += tile.IsYaojiu ? 35 : 18;
            else if (tile.Rank == 1 || tile.Rank == 9) urgency += 8;
            return urgency;
        }

        private static bool CanParticipateInCompleteSequence(Tile tile, IList<Tile> hand)
        {
            if (tile.Suit == Suit.Z) return false;
            for (int start = Math.Max(1, tile.Rank - 2); start <= Math.Min(tile.Rank, 7); start++)
            {
                var complete = true;
                for (int rank = start; rank <= start + 2; rank++)
                {
                    if (rank == tile.Rank) continue;
                    if (!hand.Any(other => other.Suit == tile.Suit && other.Rank == rank))
                    {
                        complete = false;
                        break;
                    }
                }
                if (complete) return true;
            }
            return false;
        }

        private static bool HasSequenceSupport(Tile tile, IList<Tile> hand)
        {
            if (tile.Suit == Suit.Z) return false;
            return hand.Any(other => other.Suit == tile.Suit
                && !other.EqualsIgnoreColor(tile)
                && Math.Abs(other.Rank - tile.Rank) <= 2);
        }

        /// <summary>
        /// 在普通向听数上叠加潜江麻将的癞子限制：手中最多一张癞子，并且留下的癞子必须参与顺子。
        /// </summary>
        private static int CalculateRuleShanten(IList<Tile> hand, int openMeldCount,
            Tile laiziTile, int baseShanten)
        {
            if (laiziTile.Rank <= 0) return baseShanten;
            var laiziCount = hand.Count(tile => tile.EqualsIgnoreColor(laiziTile));
            if (laiziCount > 1)
                return Math.Max(baseShanten, laiziCount - 1);
            if (laiziCount == 0) return baseShanten;

            // 一张癞子已经有完整两张邻牌时不增加距离；只有一个搭子时至少还差一步，完全孤立时至少差两步。
            var sequenceDistance = CanParticipateInCompleteSequence(laiziTile, hand)
                ? 0
                : HasSequenceSupport(laiziTile, hand) ? 1 : 2;
            return Math.Max(baseShanten, sequenceDistance);
        }

        private static int CalculateLaiziSequenceScore(IList<Tile> hand, Tile laiziTile)
        {
            if (laiziTile.Rank <= 0) return 0;
            var count = hand.Count(tile => tile.EqualsIgnoreColor(laiziTile));
            if (count == 0) return 0;
            if (count > 1) return -100 * (count - 1);
            if (CanParticipateInCompleteSequence(laiziTile, hand)) return 80;
            if (HasSequenceSupport(laiziTile, hand)) return 30;
            return -30;
        }

        /// <summary>
        /// 癞子前一位牌有额外规则价值：两张等待自摸第三张可成特殊闷笑，三张本身应优先保留执行笑牌。
        /// 该分数只在向听和进张相同的候选之间起作用，不会牺牲明显更快的胡牌速度。
        /// </summary>
        private static int CalculateSpecialRuleScore(IList<Tile> hand, Tile laiziPreviousTile)
        {
            if (laiziPreviousTile.Rank <= 0) return 0;
            var count = hand.Count(tile => tile.EqualsIgnoreColor(laiziPreviousTile));
            if (count >= 3) return 90;
            if (count == 2) return 45;
            return 0;
        }

        /// <summary>精确验证一副牌是否满足“普通自摸、最多一张癞子且癞子必须在顺子中”。</summary>
        private static bool IsLegalSinglePlayerWin(IList<Tile> concealedTiles,
            int openMeldCount, Tile laiziTile)
        {
            var neededConcealedMelds = 4 - Math.Min(4, openMeldCount);
            if (concealedTiles.Count != neededConcealedMelds * 3 + 2) return false;
            var laiziCount = laiziTile.Rank > 0
                ? concealedTiles.Count(tile => tile.EqualsIgnoreColor(laiziTile))
                : 0;
            if (laiziCount > 1) return false;
            if (CalculateShanten(concealedTiles, openMeldCount) != -1) return false;
            if (laiziCount == 0) return true;

            var counts = ToCounts(concealedTiles);
            var laiziIndex = Tile.GetIndex(laiziTile);
            for (int pairIndex = 0; pairIndex < 27; pairIndex++)
            {
                if (counts[pairIndex] < 2) continue;
                counts[pairIndex] -= 2;
                if (SearchCompleteMelds(counts, neededConcealedMelds, false, laiziIndex))
                {
                    counts[pairIndex] += 2;
                    return true;
                }
                counts[pairIndex] += 2;
            }
            return false;
        }

        /// <summary>回溯拆分全部暗牌，并确认唯一癞子确实被某个顺子使用。</summary>
        private static bool SearchCompleteMelds(int[] counts, int meldsLeft,
            bool laiziUsedInSequence, int laiziIndex)
        {
            var index = Array.FindIndex(counts, 0, 27, count => count > 0);
            if (index < 0) return meldsLeft == 0 && laiziUsedInSequence;
            if (meldsLeft <= 0) return false;

            if (counts[index] >= 3)
            {
                counts[index] -= 3;
                if (SearchCompleteMelds(counts, meldsLeft - 1,
                        laiziUsedInSequence, laiziIndex))
                {
                    counts[index] += 3;
                    return true;
                }
                counts[index] += 3;
            }

            if (RankIndex(index) <= 6 && counts[index + 1] > 0 && counts[index + 2] > 0)
            {
                counts[index]--;
                counts[index + 1]--;
                counts[index + 2]--;
                var usesLaizi = laiziUsedInSequence
                    || (laiziIndex >= index && laiziIndex <= index + 2);
                if (SearchCompleteMelds(counts, meldsLeft - 1, usesLaizi, laiziIndex))
                {
                    counts[index]++;
                    counts[index + 1]++;
                    counts[index + 2]++;
                    return true;
                }
                counts[index]++;
                counts[index + 1]++;
                counts[index + 2]++;
            }
            return false;
        }

        private static bool IsNumbered(int index) => index >= 0 && index < 27;
        private static int RankIndex(int index) => index % 9;

        private struct DiscardEvaluation
        {
            public Tile Tile;
            public int Shanten;
            public int Ukeire;
            public int ShapeScore;
            public int DiscardUrgency;
        }

        private struct HintDiscardEvaluation
        {
            public Tile Tile;
            public int RuleShanten;
            public int WinningOuts;
            public WinningTileRemaining[] WinningTiles;
            public int Ukeire;
            public int UkeireKinds;
            public int StrategicScore;
            public int StructureBreakPenalty;
            public int LaiziSequenceScore;
            public int SpecialRuleScore;
            public int ShapeScore;
            public int DiscardUrgency;
        }

        public struct HintDiscardAnalysis
        {
            public Tile? RecommendedDiscard;
            public int RuleShanten;
            public int WinningOuts;
            public WinningTileRemaining[] WinningTiles;
        }

        public struct WinningTileRemaining
        {
            public Tile Tile;
            public int CopiesLeft;
        }
    }
}

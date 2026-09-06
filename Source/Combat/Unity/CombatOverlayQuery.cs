using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Computes combat overlay highlight cells from the current combat selection.
    /// Presentation and visual layer ownership stay with CombatMapOverlayPresenter/AtlasTilePresentationView.
    /// </summary>
    public sealed class CombatOverlayQuery
    {
        // The three monster intent/chase overlay queries are each O(monsters 횞 cells 횞 log cells)
        // (every preview re-scans the whole map) and run together every RefreshView. They depend only
        // on the monster intent signature (see CombatState.ComputeMonsterIntentOverlaySignature), the
        // map, and the reveal-all flag, so we compute the previews once per change and cache the three
        // derived highlight lists. This collapses the per-build 3횞 redundant preview rebuild into one
        // and skips recompute entirely when nothing changed between refreshes.
        private bool hasIntentCache;
        private CombatState cachedState;
        private HexMapData cachedMap;
        private bool cachedIncludeUnrevealed;
        private long cachedSignature;
        private List<HexCoord> cachedMoveCells;
        private List<HexCoord> cachedAttackCells;
        private List<HexCoord> cachedChaseCells;
        private List<HexCoord> cachedSelfBuffCells;
        private List<HexCoord> cachedSummonCells;
        private IReadOnlyList<CombatOverlayIconAnnotation> cachedStatusIconCells;

        public IEnumerable<HexCoord> GetMonsterIntentMoveHighlightCells(CombatState state, HexMapData loadedMap, bool includeUnrevealedMonsters = false, string hoveredMonsterId = null)
        {
            if (!CanQuery(state, loadedMap))
            {
                return Enumerable.Empty<HexCoord>();
            }

            if (!string.IsNullOrEmpty(hoveredMonsterId))
            {
                return state.GetMonsterIntentPreviews(includeUnrevealedMonsters)
                    .Where(preview => preview.MonsterId == hoveredMonsterId)
                    .SelectMany(MoveIntentCellsOf)
                    .Where(coord => IsOverlayWalkable(loadedMap, coord, state.TerrainTraits))
                    .Distinct()
                    .ToList();
            }

            EnsureMonsterIntentCache(state, loadedMap, includeUnrevealedMonsters);
            return cachedMoveCells;
        }

        /// <summary>
        /// 이 예고가 「내가 갈 자리」로 그릴 칸들 — 걸어갈 칸 + <b>공격 뒤 전진할 칸</b>(2026-09-01 #19).
        ///
        /// <para>🔑 전진을 같은 어휘로 그리는 이유: 플레이어가 읽는 것은 「이 놈이 어디까지 오는가」
        /// 하나이고, 걷기와 전진은 그 답의 앞뒤 반쪽이다. 두 어휘로 나누면 「두 칸 걸어온 것」과
        /// 「한 칸 걷고 때린 뒤 한 칸 더 온 것」이 화면에서 다르게 읽혀 오히려 헷갈린다.
        /// 좌표는 규칙 집행과 같은 술어가 계산하므로 예고와 실제가 갈라질 수 없다.</para>
        /// </summary>
        private static IEnumerable<HexCoord> MoveIntentCellsOf(MonsterIntentPreview preview)
        {
            if (preview.WillMove)
            {
                yield return preview.PredictedMoveCoord;
            }

            if (preview.AdvanceAfterAttackCoord.HasValue)
            {
                yield return preview.AdvanceAfterAttackCoord.Value;
            }
        }

        public IEnumerable<HexCoord> GetMonsterIntentAttackHighlightCells(CombatState state, HexMapData loadedMap, bool includeUnrevealedMonsters = false, string hoveredMonsterId = null)
        {
            if (!CanQuery(state, loadedMap))
            {
                return Enumerable.Empty<HexCoord>();
            }

            // Hover narrows the overlay to a single monster. This is a transient, change-driven path
            // (recomputed only when the hovered monster changes), so it bypasses the aggregate cache.
            if (!string.IsNullOrEmpty(hoveredMonsterId))
            {
                return state.GetMonsterIntentPreviews(includeUnrevealedMonsters)
                    .Where(preview => preview.MonsterId == hoveredMonsterId)
                    .SelectMany(preview => preview.AttackRangeCoords)
                    .Where(coord => IsOverlayWalkable(loadedMap, coord, state.TerrainTraits))
                    .Distinct()
                    .ToList();
            }

            EnsureMonsterIntentCache(state, loadedMap, includeUnrevealedMonsters);
            return cachedAttackCells;
        }

        public IEnumerable<HexCoord> GetMonsterChaseHighlightCells(CombatState state, HexMapData loadedMap, bool includeUnrevealedMonsters = false, string hoveredMonsterId = null)
        {
            if (!CanQuery(state, loadedMap))
            {
                return Enumerable.Empty<HexCoord>();
            }

            if (!string.IsNullOrEmpty(hoveredMonsterId))
            {
                var monsterCoords = state.GetMonsterIntentPreviews(includeUnrevealedMonsters)
                    .Where(preview => preview.MonsterId == hoveredMonsterId)
                    .Select(preview => preview.CurrentCoord)
                    .Distinct()
                    .ToList();
                var chaseRange = state.Config.EnemyChaseRange;
                return loadedMap.AllCells
                    .Where(cell => IsOverlayWalkable(loadedMap, cell.Coord, state.TerrainTraits) && monsterCoords.Any(coord => coord.DistanceTo(cell.Coord) <= chaseRange))
                    .Select(cell => cell.Coord)
                    .Distinct()
                    .ToList();
            }

            EnsureMonsterIntentCache(state, loadedMap, includeUnrevealedMonsters);
            return cachedChaseCells;
        }

        /// <summary>
        /// 자기부여 패턴을 예고한 몬스터의 제자리 footprint 칸(2026-08-20 #10).
        ///
        /// <para>🔑 공격 예고(<see cref="GetMonsterIntentAttackHighlightCells"/>)와 <b>배타적</b>이다:
        /// 자기부여 턴에는 <c>AttackRangeCoords</c>가 비어 붉은 위험 해치가 아예 뜨지 않으므로, 유저
        /// 눈에는 "이번 턴 이 몬스터는 아무것도 안 한다"로 읽혔다. 여기서 그 공백을 메운다.</para>
        ///
        /// <para>⚠️보행 가능 필터(<c>IsOverlayWalkable</c>)를 걸지 <b>않는다</b> — 이건 "여기로 갈 수
        /// 있다"는 힌트가 아니라 "저 몬스터가 저기서 세진다"는 사실이고, 몬스터가 실제로 서 있는
        /// 칸이므로 접근 가능 여부와 무관하게 그려져야 한다.</para>
        /// </summary>
        public IEnumerable<HexCoord> GetMonsterSelfBuffHighlightCells(CombatState state, HexMapData loadedMap, bool includeUnrevealedMonsters = false, string hoveredMonsterId = null)
        {
            if (!CanQuery(state, loadedMap))
            {
                return Enumerable.Empty<HexCoord>();
            }

            if (!string.IsNullOrEmpty(hoveredMonsterId))
            {
                return BuildSelfBuffCells(
                    state,
                    state.GetMonsterIntentPreviews(includeUnrevealedMonsters)
                        .Where(preview => preview.MonsterId == hoveredMonsterId)
                        .ToList());
            }

            EnsureMonsterIntentCache(state, loadedMap, includeUnrevealedMonsters);
            return cachedSelfBuffCells;
        }

        /// <summary>
        /// 소환 예고 칸(요괴 §4-4 · 「여기 나타남」). 자기부여 예고와 같은 이유로 <b>보행 가능 필터를
        /// 걸지 않는다</b> — 이건 "여기로 갈 수 있다"가 아니라 "여기에 적이 선다"는 사실이다.
        /// (규칙층이 이미 맵 안 칸만 돌려준다.)
        /// </summary>
        public IEnumerable<HexCoord> GetMonsterSummonHighlightCells(CombatState state, HexMapData loadedMap, bool includeUnrevealedMonsters = false, string hoveredMonsterId = null)
        {
            if (!CanQuery(state, loadedMap))
            {
                return Enumerable.Empty<HexCoord>();
            }

            if (!string.IsNullOrEmpty(hoveredMonsterId))
            {
                return BuildSummonCells(state.GetMonsterIntentPreviews(includeUnrevealedMonsters)
                    .Where(preview => preview.MonsterId == hoveredMonsterId)
                    .ToList());
            }

            EnsureMonsterIntentCache(state, loadedMap, includeUnrevealedMonsters);
            return cachedSummonCells;
        }

        private static List<HexCoord> BuildSummonCells(IReadOnlyList<MonsterIntentPreview> previews)
        {
            return previews
                .Where(preview => !preview.IsIntentHidden)
                .SelectMany(preview => preview.SummonCoords)
                .Distinct()
                .ToList();
        }

        private static List<HexCoord> BuildSelfBuffCells(CombatState state, IReadOnlyList<MonsterIntentPreview> previews)
        {
            return previews
                .Where(preview => preview.IsSelfBuffIntent && !preview.IsIntentHidden)
                .SelectMany(preview => state.GetMonsterOccupiedCoords(preview.MonsterId))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Per-tile status-effect icon annotations telegraphed by monster attack patterns. Shares the
        /// same monster-intent signature cache as the move/attack/chase highlight queries, so the three
        /// preview-derived outputs and these icon annotations are computed from a single
        /// <see cref="CombatState.GetMonsterIntentPreviews"/> call per change.
        /// </summary>
        public IReadOnlyList<CombatOverlayIconAnnotation> GetMonsterAttackStatusIconCells(CombatState state, HexMapData loadedMap, bool includeUnrevealedMonsters = false, string hoveredMonsterId = null)
        {
            if (!CanQuery(state, loadedMap))
            {
                return System.Array.Empty<CombatOverlayIconAnnotation>();
            }

            // Mirror the attack-highlight hover narrowing so the telegraphed status icons stay in sync
            // with the single-monster attack overlay (bypasses the aggregate cache while hovering).
            if (!string.IsNullOrEmpty(hoveredMonsterId))
            {
                var hoveredPreviews = state.GetMonsterIntentPreviews(includeUnrevealedMonsters)
                    .Where(preview => preview.MonsterId == hoveredMonsterId)
                    .ToList();
                return BuildMonsterAttackStatusIconCells(hoveredPreviews);
            }

            EnsureMonsterIntentCache(state, loadedMap, includeUnrevealedMonsters);
            return cachedStatusIconCells;
        }

        /// <summary>
        /// Pure projection from monster intent previews to per-tile telegraph icon annotations.
        /// A preview that telegraphs neither a status effect nor knockback contributes nothing;
        /// effects and the knockback distance from multiple previews that overlap a tile are merged
        /// (effects preserve first-seen order without duplicates; for knockback the first non-zero
        /// distance wins — 부호가 방향이라 OR로 뭉갤 수 없고, 한 칸에 밀치기와 끌어당김이 겹치면
        /// 먼저 본 예고의 방향을 지킨다).
        /// </summary>
        public static IReadOnlyList<CombatOverlayIconAnnotation> BuildMonsterAttackStatusIconCells(IReadOnlyList<MonsterIntentPreview> previews)
        {
            if (previews == null || previews.Count == 0)
            {
                return System.Array.Empty<CombatOverlayIconAnnotation>();
            }

            var orderedCoords = new List<HexCoord>();
            var effectsByCoord = new Dictionary<HexCoord, List<ActiveEffect>>();
            var knockbackByCoord = new Dictionary<HexCoord, int>();
            var curseByCoord = new Dictionary<HexCoord, bool>();
            foreach (var preview in previews)
            {
                // 미지(D-4): 예고가 통째로 비어 있으므로 위 경로로는 아이콘이 하나도 안 뜬다.
                // 대신 몬스터 <b>자기 좌표</b>에 '?' 하나를 얹어 "가려져 있다"는 사실만 알린다.
                // 맵 오버레이가 공격 예고 셀이 아닌 곳에 아이콘을 그리는 유일한 경우다.
                //
                // 🔴 판정은 <b>「미지」 상태이상 축</b>이어야 한다(2026-09-05). 합친 술어
                // (IsIntentHidden)를 읽으면 은신 몬스터의 자기 좌표에 물음표가 서서, 마커를
                // 감춰 놓고 그 자리를 표식으로 알려 주는 꼴이 된다 — 「저기 누가 있는지조차
                // 모른다」가 은신이고, 물음표는 「보이는 놈이 뭘 할지 모른다」의 어휘다.
                if (preview.IsIntentHidden && !preview.IsIntentVeiledByStatus)
                {
                    continue;
                }

                if (preview.IsIntentHidden)
                {
                    if (!effectsByCoord.TryGetValue(preview.CurrentCoord, out var hiddenEffects))
                    {
                        hiddenEffects = new List<ActiveEffect>();
                        effectsByCoord[preview.CurrentCoord] = hiddenEffects;
                        knockbackByCoord[preview.CurrentCoord] = 0;
                        curseByCoord[preview.CurrentCoord] = false;
                        orderedCoords.Add(preview.CurrentCoord);
                    }

                    if (hiddenEffects.All(effect => effect.Kind != StatusEffectKind.Unknown))
                    {
                        hiddenEffects.Add(new ActiveEffect(
                            EffectType.Duration,
                            StatusEffectKind.Unknown,
                            string.Empty,
                            remainingTurns: 1,
                            amount: 0,
                            sourceRef: preview.MonsterId));
                    }

                    continue;
                }

                var hasEffects = preview.AttackPatternStatusEffects != null && preview.AttackPatternStatusEffects.Count > 0;
                // 순수 피해 공격은 여기서 걸러진다 — 피해량은 타일이 아니라 몬스터 네임플레이트의
                // 의도 배지가 들고 있다(실플레이 피드백 ②). 타일에 남는 것은 상태이상·넉백처럼
                // "이 칸에 서면 무슨 일이 생기는가"를 말하는 표식뿐이다.
                if (!hasEffects && !preview.HasKnockback && !preview.InjectsCurse)
                {
                    continue;
                }

                foreach (var coord in preview.AttackRangeCoords)
                {
                    if (!effectsByCoord.TryGetValue(coord, out var effects))
                    {
                        effects = new List<ActiveEffect>();
                        effectsByCoord[coord] = effects;
                        knockbackByCoord[coord] = 0;
                        curseByCoord[coord] = false;
                        orderedCoords.Add(coord);
                    }

                    if (hasEffects)
                    {
                        foreach (var kind in preview.AttackPatternStatusEffects)
                        {
                            if (effects.All(effect => effect.Kind != kind))
                            {
                                effects.Add(new ActiveEffect(
                                    EffectType.Duration,
                                    kind,
                                    string.Empty,
                                    preview.AttackPatternStatusEffectDurationTurns,
                                    preview.AttackPatternStatusEffectAmount,
                                    preview.AttackPatternId));
                            }
                        }
                    }

                    if (preview.HasKnockback && knockbackByCoord[coord] == 0)
                    {
                        // 부호(방향)를 그대로 싣는다 — 타일 표식이 밀치기/끌어당김을 갈라 그린다.
                        knockbackByCoord[coord] = preview.AttackPatternKnockbackDistance;
                    }

                    // 저주는 넉백과 달리 방향도 크기도 없는 한 비트라 겹치면 그냥 OR한다 —
                    // 두 예고가 같은 칸을 덮으면 "덱이 더러워진다"는 사실은 어느 쪽이든 같다.
                    if (preview.InjectsCurse)
                    {
                        curseByCoord[coord] = true;
                    }

                }
            }

            if (orderedCoords.Count == 0)
            {
                return System.Array.Empty<CombatOverlayIconAnnotation>();
            }

            var annotations = new CombatOverlayIconAnnotation[orderedCoords.Count];
            for (var i = 0; i < orderedCoords.Count; i++)
            {
                var coord = orderedCoords[i];
                annotations[i] = new CombatOverlayIconAnnotation(
                    coord,
                    effectsByCoord[coord],
                    knockbackByCoord[coord],
                    injectsCurse: curseByCoord[coord]);
            }

            return annotations;
        }

        private void EnsureMonsterIntentCache(CombatState state, HexMapData loadedMap, bool includeUnrevealedMonsters)
        {
            var signature = state.ComputeMonsterIntentOverlaySignature(includeUnrevealedMonsters);
            if (hasIntentCache &&
                ReferenceEquals(cachedState, state) &&
                ReferenceEquals(cachedMap, loadedMap) &&
                cachedIncludeUnrevealed == includeUnrevealedMonsters &&
                cachedSignature == signature)
            {
                return;
            }

            var previews = state.GetMonsterIntentPreviews(includeUnrevealedMonsters);

            cachedMoveCells = previews
                .SelectMany(MoveIntentCellsOf)
                .Where(coord => IsOverlayWalkable(loadedMap, coord, state.TerrainTraits))
                .Distinct()
                .ToList();

            cachedAttackCells = previews
                .SelectMany(preview => preview.AttackRangeCoords)
                .Where(coord => IsOverlayWalkable(loadedMap, coord, state.TerrainTraits))
                .Distinct()
                .ToList();

            var monsterCoords = previews
                .Select(preview => preview.CurrentCoord)
                .Distinct()
                .ToList();
            var chaseRange = state.Config.EnemyChaseRange;
            cachedChaseCells = loadedMap.AllCells
                .Where(cell => IsOverlayWalkable(loadedMap, cell.Coord, state.TerrainTraits) && monsterCoords.Any(coord => coord.DistanceTo(cell.Coord) <= chaseRange))
                .Select(cell => cell.Coord)
                .Distinct()
                .ToList();

            cachedStatusIconCells = BuildMonsterAttackStatusIconCells(previews);
            cachedSelfBuffCells = BuildSelfBuffCells(state, previews);
            cachedSummonCells = BuildSummonCells(previews);

            cachedState = state;
            cachedMap = loadedMap;
            cachedIncludeUnrevealed = includeUnrevealedMonsters;
            cachedSignature = signature;
            hasIntentCache = true;
        }

        public IEnumerable<HexCoord> GetSelectedTargetHighlightCells(
            CombatState state,
            HexMapData loadedMap,
            CombatSelectionState selection)
        {
            return GetSelectedTargetHighlightCells(
                state,
                loadedMap,
                selection.TargetCardKind,
                selection.TargetCardKey);
        }

        public IEnumerable<HexCoord> GetSelectedTargetHighlightCells(
            CombatState state,
            HexMapData loadedMap,
            CombatCardKind? selectedTargetCardKind,
            string selectedTargetCardId)
        {
            if (!CanQuery(state, loadedMap) || !selectedTargetCardKind.HasValue)
            {
                return Enumerable.Empty<HexCoord>();
            }

            switch (selectedTargetCardKind.Value)
            {
                case CombatCardKind.Attack:
                    return GetSelectedAttackRangeHighlightCells(state, loadedMap, selectedTargetCardId);
                case CombatCardKind.Scout:
                    return GetScoutTargetHighlightCells(state, loadedMap, selectedTargetCardId);
                case CombatCardKind.Investigate:
                    return GetInvestigateTargetHighlightCells(state, loadedMap);
                case CombatCardKind.FieldObject:
                    return GetFieldObjectTargetHighlightCells(state, loadedMap, selectedTargetCardId);
                default:
                    return Enumerable.Empty<HexCoord>();
            }
        }

        public IEnumerable<HexCoord> GetSelectedAttackRangeHighlightCells(
            CombatState state,
            HexMapData loadedMap,
            string selectedTargetCardId)
        {
            if (!CanQuery(state, loadedMap))
            {
                return Enumerable.Empty<HexCoord>();
            }

            var range = state.GetCombatCards()
                .Where(card => !card.IsDiscarded && card.Kind == CombatCardKind.Attack && MatchesCardKey(card, selectedTargetCardId))
                .Select(card => card.Range)
                .DefaultIfEmpty(0)
                .FirstOrDefault();

            return loadedMap.AllCells
                .Where(cell => IsOverlayWalkable(loadedMap, cell.Coord, state.TerrainTraits) && state.PlayerCoord.DistanceTo(cell.Coord) <= range)
                .Select(cell => cell.Coord)
                .ToList();
        }

        public IEnumerable<HexCoord> GetSelectedEffectAreaHighlightCells(
            CombatState state,
            HexMapData loadedMap,
            CombatSelectionState selection,
            HexCoord? hoveredCoord = null)
        {
            if (!CanQuery(state, loadedMap) || selection.Mode == CombatSelectionMode.None)
            {
                return Enumerable.Empty<HexCoord>();
            }

            var card = state.GetCombatCards()
                .FirstOrDefault(candidate => !candidate.IsDiscarded && MatchesCardKey(candidate, selection.CardKey));
            if (string.IsNullOrEmpty(card.Id) || card.AreaRadius <= 0)
            {
                return Enumerable.Empty<HexCoord>();
            }

            if (selection.IsMove && card.Kind == CombatCardKind.Move && IsRandomAreaMove(card))
            {
                return GetWalkableDisk(loadedMap, state.PlayerCoord, card.AreaRadius, state.TerrainTraits);
            }

            if (!selection.TargetCardKind.HasValue)
            {
                return Enumerable.Empty<HexCoord>();
            }

            // Self-centred area cards show their actual affected footprint as soon as the card is picked.
            if (selection.TargetCardKind.Value == CombatCardKind.Attack && IsSelfCenteredAreaAttack(card))
            {
                return GetWalkableDisk(loadedMap, state.PlayerCoord, card.AreaRadius, state.TerrainTraits);
            }

            // Tile-targeted area cards keep the blue overlay as the castable range, then preview the
            // yellow effect footprint around the hovered target tile.
            if (hoveredCoord.HasValue && IsValidAreaCenter(state, loadedMap, selection.TargetCardKind.Value, card, hoveredCoord.Value))
            {
                return GetWalkableDisk(loadedMap, hoveredCoord.Value, card.AreaRadius, state.TerrainTraits);
            }

            return Enumerable.Empty<HexCoord>();
        }

        private static bool IsRandomAreaMove(CombatCardSnapshot card)
        {
            return card.TargetMode == CardTargetMode.RandomReachable ||
                   string.Equals(card.Id, SeoulPlayup.Combat.Runtime.Cards.CardIds.RandomJourney, System.StringComparison.Ordinal);
        }

        private static bool IsSelfCenteredAreaAttack(CombatCardSnapshot card)
        {
            return card.TargetMode == CardTargetMode.SelfArea ||
                   string.Equals(card.Id, SeoulPlayup.Combat.Runtime.Cards.CardIds.Sweep, System.StringComparison.Ordinal);
        }

        private static bool IsValidAreaCenter(
            CombatState state,
            HexMapData loadedMap,
            CombatCardKind kind,
            CombatCardSnapshot card,
            HexCoord center)
        {
            if (!IsOverlayWalkable(loadedMap, center, state.TerrainTraits))
            {
                return false;
            }

            switch (kind)
            {
                case CombatCardKind.Attack:
                    return state.ValidateAttackTarget(center, card.SelectionKey).IsValid ||
                           state.ValidateAttackTarget(center, card.Id).IsValid;
                case CombatCardKind.Scout:
                    return state.ValidateScoutTarget(center, card.SelectionKey).IsValid ||
                           state.ValidateScoutTarget(center, card.Id).IsValid;
                case CombatCardKind.FieldObject:
                    return state.ValidateFieldObjectTarget(center, card.SelectionKey).IsValid ||
                           state.ValidateFieldObjectTarget(center, card.Id).IsValid;
                default:
                    return false;
            }
        }

        private static IEnumerable<HexCoord> GetWalkableDisk(HexMapData loadedMap, HexCoord center, int radius, HexTerrainTraits traits)
        {
            return loadedMap.AllCells
                .Where(cell => IsOverlayWalkable(loadedMap, cell.Coord, traits) && center.DistanceTo(cell.Coord) <= radius)
                .Select(cell => cell.Coord)
                .ToList();
        }

        public IEnumerable<HexCoord> GetScoutTargetHighlightCells(CombatState state, HexMapData loadedMap, string selectedTargetCardId = "")
        {
            if (!CanQuery(state, loadedMap))
            {
                return Enumerable.Empty<HexCoord>();
            }

            return loadedMap.AllCells
                .Where(cell => state.ValidateScoutTarget(cell.Coord, selectedTargetCardId).IsValid)
                .Select(cell => cell.Coord)
                .ToList();
        }

        public IEnumerable<HexCoord> GetFieldObjectTargetHighlightCells(CombatState state, HexMapData loadedMap, string selectedTargetCardId)
        {
            if (!CanQuery(state, loadedMap))
            {
                return Enumerable.Empty<HexCoord>();
            }

            return loadedMap.AllCells
                .Where(cell => state.ValidateFieldObjectTarget(cell.Coord, selectedTargetCardId).IsValid)
                .Select(cell => cell.Coord)
                .ToList();
        }

        public IEnumerable<HexCoord> GetInvestigateTargetHighlightCells(CombatState state, HexMapData loadedMap)
        {
            if (!CanQuery(state, loadedMap))
            {
                return Enumerable.Empty<HexCoord>();
            }

            return loadedMap.AllCells
                .Where(cell => state.ValidateInvestigateTarget(cell.Coord).IsValid)
                .Select(cell => cell.Coord)
                .ToList();
        }

        private static bool CanQuery(CombatState state, HexMapData loadedMap)
        {
            // Runtime validation methods read from CombatState.Map, while overlay projection enumerates
            // LoadedMap cells. The controller constructs both from the same map; reject mismatched inputs
            // so the extracted query keeps that former controller invariant explicit.
            return state != null && loadedMap != null && ReferenceEquals(state.Map, loadedMap);
        }

        // Inaccessible tiles (water, or cut off from every neighbor by a 2+ height gap) are never valid
        // overlay cells: a unit can never stand there, so range/highlight overlays must exclude them just
        // like placement does — same shared predicate (HexAccessibility, 2026-08-20 #4).
        private static bool IsOverlayWalkable(HexMapData map, HexCoord coord, HexTerrainTraits traits)
        {
            return map != null && HexAccessibility.IsAccessible(map, coord, traits ?? HexTerrainTraits.Default);
        }

        private static bool MatchesCardKey(CombatCardSnapshot card, string cardKey)
        {
            return string.IsNullOrEmpty(cardKey) ||
                   string.Equals(card.InstanceId, cardKey, System.StringComparison.Ordinal) ||
                   string.Equals(card.Id, cardKey, System.StringComparison.Ordinal);
        }
    }
}

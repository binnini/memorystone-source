using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.Combat.Unity
{
    public sealed class CombatOverlayPresentationBuilder
    {
        public CombatOverlayPresentation Build(CombatOverlayPresentationRequest request)
        {
            var states = new List<CombatOverlayLayerState>();
            var annotations = new List<CombatOverlayIconAnnotation>();
            if (request.State == null || request.LoadedMap == null)
            {
                return new CombatOverlayPresentation(states, annotations);
            }

            // 결계는 선택 상태·페이즈·디버그 토글과 무관하게 봉인되어 있는 동안 항상 그린다. 다른 오버레이는
            // "지금 무엇을 고르는 중인가"의 힌트지만 결계는 판의 사실이라, 카드를 집었다고 사라지면 안 된다.
            var arenaBoundary = request.State.SealedBossArenaBoundaryCoords;
            Add(states, HexOverlayLayer.BossArenaBoundary, arenaBoundary.Count > 0, arenaBoundary);

            // P6(§13.4 C-4) 보스 점유 오버레이. 1페이즈 보스는 모델이 2칸만 덮으면서 7칸을 막으므로(§11.3)
            // 이 표시 없이는 "어디에 설 수 있는지"를 읽을 수 없다. 결계와 같은 이유로 선택 상태와 무관하게
            // 보스가 살아 있는 동안 항상 그린다. 단 암시야는 뚫지 않는다(#22): 컨트롤러가 몬스터 본체와
            // 같은 가시성 게이트로 거른 좌표를 넘기면 그것을 쓰고, 안 넘기면(테스트·레거시 호출) 전량을 쓴다.
            var bossFootprint = request.BossFootprintCoords ?? request.State.BossFootprintCoords;
            Add(states, HexOverlayLayer.BossFootprint, bossFootprint.Count > 0, bossFootprint);

            // 취약 부위(§20-A-7). 결계·점유와 같은 이유로 선택 상태와 무관하게 항상 그린다 —
            // 카드를 조준하는 중에 "어디를 때려야 2배인지"가 사라지면 정확히 그때 필요한 정보가 없다.
            // <b>판명 중에만</b> 나온다: 미판명이면 GetBossWeakSpots가 빈 목록이므로 여기서 분기하지 않는다.
            var weakSpots = request.State.GetBossWeakSpots();
            if (weakSpots.Count > 0)
            {
                // 마지막 턴이면 저채도 변형으로 만료 임박을 알린다. 보스가 여럿이어도 스타일은 하나이므로
                // "하나라도 이번 턴에 만료되면 임박"으로 본다(현행 저작에 보스는 한 마리다).
                var expiring = weakSpots.Any(spot => spot.IsExpiringThisTurn);
                states.Add(new CombatOverlayLayerState(
                    HexOverlayLayer.BossWeakSpot,
                    weakSpots.Select(spot => spot.Coord),
                    CombatOverlayTheme.ResolveBossWeakSpotStyle(expiring)));
            }

            var showMonsterIntentOverlays = request.State.Phase == CombatPhase.PlayerMovement ||
                                            request.State.Phase == CombatPhase.PlayerAction;

            // 주기 함정 예고(C-11 / D-13). 결계와 같은 이유로 **선택 상태와 무관하게** 그린다 —
            // 카드를 조준하는 중에 위험 표시가 사라지면 정확히 그때 밟는다. 다만 플레이어 페이즈에만
            // 띄운다: 이 함정은 이번 턴 몬스터 행동 직전에 터지므로, 그 뒤에는 예고가 아니라 잔상이다.
            // 레이어는 MonsterAttackIntent를 공유한다. 이름만 몬스터일 뿐 실체는 "이 칸은 이번 턴 위험"이라는
            // 붉은 사선 위험 표지이고, 전용 레이어를 새로 만들면 테마·렌더·씬 저작이 전부 따라온다.
            var armedTrapCells = showMonsterIntentOverlays
                ? request.State.GetArmedPeriodicTrapCells()
                : System.Array.Empty<HexCoord>();
            // 전멸기 예고(§13.5)도 같은 레이어에 합류한다 — 주기 함정과 같은 이유("이 칸은 위험" 어휘
            // 공유, 전용 레이어 신설 비용 회피)이고 같은 페이즈 게이트를 쓴다(폭발은 몬스터 행동에 터지므로
            // 그 뒤에는 잔상이다).
            var annihilationTelegraphs = showMonsterIntentOverlays
                ? request.State.GetBossAnnihilationTelegraphs()
                : System.Array.Empty<BossAnnihilationTelegraphState>();
            var annihilationCells = annihilationTelegraphs.Count == 0
                ? (IReadOnlyList<HexCoord>)System.Array.Empty<HexCoord>()
                : annihilationTelegraphs.SelectMany(telegraph => telegraph.Cells).Distinct().ToList();
            // 철조각 사슬 예고(§21.8 제안 2)도 같은 위험 어휘다. 죽은 철조각의 가닥은 상태 쪽 필터가
            // 이미 걸러 준다 — 예고 후 철조각을 부수면 그 가닥의 붉은 칸이 즉시 꺼진다(파괴 보상).
            var scrapChainCells = showMonsterIntentOverlays
                ? request.State.GetBossScrapChainTelegraphCells()
                : System.Array.Empty<HexCoord>();
            // 2026-09-05 결정 5: 사슬 예고는 더 이상 위험 해치에 <b>합치지 않는다</b> — 전용 레이어(전격 노랑·135°)로
            // 그려 패턴 공격 예고와 갈린다. 페이즈 게이트(플레이어 페이즈)는 같다.
            Add(states, HexOverlayLayer.BossScrapChainTelegraph, scrapChainCells.Count > 0, scrapChainCells);
            // 철조각 살포 예고(2026-09-05 · 후속 #1로 전용 레이어): 종전엔 소환 예고(청록 점유)를 빌렸으나 사용자
            // 판정은 「공격 예고처럼 붉게」 — 위험 해치(MonsterAttackIntent)에 합치지는 않는다(그 칸은 당장 아프지
            // 않다 · 어휘가 다르면 레이어가 다르다). 같은 플레이어 페이즈 게이트. 봉인 시점에 첫 살포도 예고된다.
            var propVolleyCells = showMonsterIntentOverlays
                ? request.State.GetBossPropVolleyTelegraphCells()
                : System.Array.Empty<HexCoord>();
            Add(states, HexOverlayLayer.BossPropVolleyTelegraph, propVolleyCells.Count > 0, propVolleyCells);
            var dangerCells = annihilationCells.Count == 0
                ? armedTrapCells
                : (IReadOnlyList<HexCoord>)armedTrapCells.Concat(annihilationCells).Distinct().ToList();
            // 빈 목록일 때는 레이어 자체를 만들지 않는다 — 빈 레이어 상태를 흘리면 "이 레이어가 켜져 있다"를
            // 보는 소비자(디버그 토글 억제 테스트 포함)가 오해한다.
            Add(states, HexOverlayLayer.MonsterAttackIntent, dangerCells.Count > 0, dangerCells);

            // 부서진 땅(2026-09-01 #18). 🔑 위 위험 해치와 달리 <b>페이즈·선택 상태와 무관하게</b>
            // 그린다 — 예고는 이번 턴의 일이라 지나면 잔상이지만, 부서진 지형은 몇 턴이고 남아 있는
            // 판의 사실이다. 조준 중에 사라지면 정확히 그때 밟는다(결계·주기 함정과 같은 논거).
            var rupturedCells = request.State.GetRupturedGroundCells();
            Add(states, HexOverlayLayer.RupturedGround, rupturedCells.Count > 0, rupturedCells);

            // 뒤끝 호버(2026-09-01 #3): 「여기서 잡으면 대가가 따른다」를 판에 그린다.
            // 🔑 선택 상태·페이즈와 무관하다 — 손을 얹은 동안만 뜨는 <b>질문의 답</b>이라,
            //    다른 오버레이처럼 상황에 따라 켜지고 꺼지면 오히려 못 읽는다.
            var traitReachCells = request.HoveredTraitReachCoords;
            Add(states, HexOverlayLayer.AftermathReach, traitReachCells.Count > 0, traitReachCells);

            // 철조각 폭발 범위 호버(2026-09-05 결정 4): 손을 얹은 기물의 저작 반경 원판. 툴팁의 「반경 N칸」과
            // <b>같은 저작값</b>(TryGetBossPropBlastAuthoring)을 읽으므로 글자와 그림이 갈라질 수 없다.
            var blastCells = ResolveHoveredBossPropBlastCells(request);
            Add(states, HexOverlayLayer.BossPropBlastReach, blastCells.Count > 0, blastCells);
            if (request.IsMoveSelectionActive)
            {
                // Keep the full reachable fill even on threatened tiles: the softened danger hatch is
                // layered on top so a tile reads as "movable AND dangerous" at once (instead of dropping
                // the movement cue, which made threatened-but-reachable tiles look impassable).
                Add(states, HexOverlayLayer.Reachable, request.ShowPlayerMovementOverlay, request.ReachableCoords);
                Add(states, HexOverlayLayer.PlayerActionEffectArea, request.ShowPlayerMovementOverlay, request.SelectedEffectAreaHighlightCoords);
                AddMonsterIntentOverlays(states, annotations, request, showMonsterIntentOverlays);
            }
            else if (request.SelectedTargetCardKind.HasValue)
            {
                Add(states, HexOverlayLayer.AttackRange, request.ShowPlayerActionOverlay, request.SelectedTargetHighlightCoords);
                Add(states, HexOverlayLayer.PlayerActionRange, request.ShowPlayerActionOverlay, request.SelectedTargetHighlightCoords);
                Add(states, HexOverlayLayer.PlayerActionEffectArea, request.ShowPlayerActionOverlay, request.SelectedEffectAreaHighlightCoords);
            }
            else
            {
                AddMonsterIntentOverlays(states, annotations, request, showMonsterIntentOverlays);
            }

            // 전멸기 경고 아이콘은 위험 해치와 같은 페이즈 게이트만 쓰고 선택 상태·디버그 토글과는 무관하다
            // (해치 자체와 같은 이유 — 조준 중에 경고가 사라지면 정확히 그때 밟는다). 상태이상 아이콘이
            // 이미 붙은 칸이면 같은 타일에 그룹이 둘 생기지 않도록 합쳐 준다.
            MergeAnnihilationWarnings(annotations, annihilationTelegraphs, request.State.PlayerCoord);

            // 안전지대 후보(§20-B-6). 위험 해치와 같은 페이즈 게이트를 쓴다(폭발은 몬스터 행동에
            // 터지므로 그 뒤에는 잔상이다). <b>미판별 후보만</b> 그린다 — 판별된 진짜는 "예고가 없다"가
            // 이미 안전을 뜻하고(별도 색 불필요), 판별된 가짜는 `?`가 걷히며 아래 붉은 예고가 드러난다.
            var candidates = showMonsterIntentOverlays
                ? request.State.GetBossSafeZoneCandidates()
                : System.Array.Empty<BossSafeZoneCandidateState>();
            var unknownCandidates = candidates
                .Where(candidate => candidate.Kind == BossSafeZoneCandidateKind.Unknown)
                .Select(candidate => candidate.Coord)
                .Distinct()
                .ToList();
            Add(states, HexOverlayLayer.BossSafeZoneCandidate, unknownCandidates.Count > 0, unknownCandidates);
            MergeSafeZoneCandidateMarks(annotations, unknownCandidates);

            // 정찰로 진짜라고 판명된 안전지대(§28 W5)는 초록 확정 채움으로 갈린다 — 후보 청록은
            // "모른다"는 색이고, 이 초록은 "확실히 안전하다"는 판정의 색이다.
            var confirmedSafeZones = candidates
                .Where(candidate => candidate.Kind == BossSafeZoneCandidateKind.Real)
                .Select(candidate => candidate.Coord)
                .ToList();
            Add(states, HexOverlayLayer.BossSafeZoneConfirmed, confirmedSafeZones.Count > 0, confirmedSafeZones);
            MergeSafeZoneConfirmedMarks(annotations, confirmedSafeZones);

            MergeWeakSpotMarks(annotations, request.State.GetBossWeakSpots());

            return new CombatOverlayPresentation(states, annotations);
        }

        /// <summary>
        /// 전멸기 예고에 경고 아이콘 주석을 <b>예고당 하나만</b> 얹는다.
        ///
        /// ⚠️ 칸마다 붙이면 안 된다: 전멸기는 아레나 대부분(실측 51칸)을 덮으므로 칸당 아이콘은 화면을
        /// 노란 칩으로 도배해 정작 그 아래 위험 해치를 가린다. 표식의 일은 "이건 평범한 공격 예고가
        /// 아니다"를 한 번 말하는 것이고, 언제·얼마나·어떻게 피하나는 호버 툴팁이 맡는다.
        ///
        /// 위치는 <b>플레이어가 서 있는 칸</b>(예고 안일 때) — 결정이 일어나는 자리다. 이미 안전지대에
        /// 있으면 가장 가까운 예고 칸에 붙어 "저 선 너머가 위험"으로 읽힌다.
        ///
        /// 같은 좌표에 이미 주석이 있으면 <b>교체가 아니라 병합</b>한다 — 아이콘 렌더러는 주석 하나당
        /// 타일 그룹 하나를 만들므로, 같은 칸에 둘을 흘리면 그룹이 겹쳐 그려진다.
        /// </summary>
        private static void MergeAnnihilationWarnings(
            List<CombatOverlayIconAnnotation> annotations,
            IReadOnlyList<BossAnnihilationTelegraphState> telegraphs,
            HexCoord playerCoord)
        {
            if (telegraphs == null || telegraphs.Count == 0)
            {
                return;
            }

            foreach (var telegraph in telegraphs)
            {
                if (telegraph.TurnsRemaining <= 0 || telegraph.Cells.Count == 0)
                {
                    continue;
                }

                var marker = telegraph.Cells.Contains(playerCoord)
                    ? playerCoord
                    : telegraph.Cells
                        .OrderBy(coord => coord.DistanceTo(playerCoord))
                        .ThenBy(coord => coord)
                        .First();

                var existing = annotations.FindIndex(annotation => annotation.Coord.Equals(marker));
                if (existing >= 0)
                {
                    annotations[existing] = new CombatOverlayIconAnnotation(
                        marker,
                        annotations[existing].StatusEffects,
                        annotations[existing].KnockbackDistance,
                        telegraph.TurnsRemaining,
                        telegraph.Damage);
                    continue;
                }

                annotations.Add(new CombatOverlayIconAnnotation(
                    marker,
                    System.Array.Empty<ActiveEffect>(),
                    knockbackDistance: 0,
                    annihilationTurnsRemaining: telegraph.TurnsRemaining,
                    annihilationDamage: telegraph.Damage));
            }
        }

        /// <summary>
        /// 미판별 후보 칸마다 <c>?</c> 주석을 붙인다(§20-B-6). 전멸기 경고와 달리 <b>칸마다</b> 붙이는
        /// 것이 맞다 — 후보는 셋뿐이고, "저 셋 중 어디로 갈 것인가"가 정확히 칸 단위 결정이다.
        ///
        /// 같은 좌표에 이미 주석이 있으면 <b>교체가 아니라 병합</b>한다(전멸기 경고와 같은 이유 —
        /// 아이콘 렌더러는 주석 하나당 타일 그룹 하나를 만들므로 둘을 흘리면 그룹이 겹쳐 그려진다).
        /// </summary>
        /// <summary>취약 부위 칸에 아이콘+툴팁 표식을 싣는다(§28 W4). 판명 중에만 GetBossWeakSpots가
        /// 값을 주므로 표식도 그때만 나온다 — 타일 채움(BossWeakSpot 레이어)과 같은 수명이다.</summary>
        private static void MergeWeakSpotMarks(
            System.Collections.Generic.List<CombatOverlayIconAnnotation> annotations,
            System.Collections.Generic.IReadOnlyList<BossWeakSpotState> weakSpots)
        {
            foreach (var spot in weakSpots)
            {
                var existing = annotations.FindIndex(annotation => annotation.Coord == spot.Coord);
                if (existing >= 0)
                {
                    var current = annotations[existing];
                    annotations[existing] = new CombatOverlayIconAnnotation(
                        current.Coord,
                        current.StatusEffects,
                        current.KnockbackDistance,
                        current.AnnihilationTurnsRemaining,
                        current.AnnihilationDamage,
                        current.SafeZoneCandidate,
                        spot.DamagePercent,
                        spot.KnownTurnsRemaining,
                        current.SafeZoneConfirmed);
                }
                else
                {
                    annotations.Add(new CombatOverlayIconAnnotation(
                        spot.Coord,
                        System.Array.Empty<ActiveEffect>(),
                        knockbackDistance: 0,
                        annihilationTurnsRemaining: 0,
                        annihilationDamage: 0,
                        safeZoneCandidate: false,
                        weakSpotDamagePercent: spot.DamagePercent,
                        weakSpotTurnsRemaining: spot.KnownTurnsRemaining));
                }
            }
        }

        /// <summary>
        /// 판명된 진짜 안전지대 칸마다 <b>호버 전용</b> 주석을 붙인다(§28 W5 후속 T1). `?` 후보와
        /// 달리 새 아이콘은 그리지 않는다 — 초록 확정 채움이 이미 시각 신호라 글리프가 과하고,
        /// 필요한 것은 "왜 초록인가"에 답하는 툴팁뿐이다. 병합 규칙은 다른 표식과 같다
        /// (아이콘 렌더러는 주석 하나당 타일 그룹 하나 — 같은 칸에 둘을 흘리면 그룹이 겹친다).
        /// </summary>
        private static void MergeSafeZoneConfirmedMarks(
            List<CombatOverlayIconAnnotation> annotations,
            IReadOnlyList<HexCoord> confirmedSafeZones)
        {
            foreach (var coord in confirmedSafeZones)
            {
                var existing = annotations.FindIndex(annotation => annotation.Coord.Equals(coord));
                if (existing >= 0)
                {
                    var current = annotations[existing];
                    annotations[existing] = new CombatOverlayIconAnnotation(
                        current.Coord,
                        current.StatusEffects,
                        current.KnockbackDistance,
                        current.AnnihilationTurnsRemaining,
                        current.AnnihilationDamage,
                        current.SafeZoneCandidate,
                        current.WeakSpotDamagePercent,
                        current.WeakSpotTurnsRemaining,
                        safeZoneConfirmed: true);
                    continue;
                }

                annotations.Add(new CombatOverlayIconAnnotation(
                    coord,
                    System.Array.Empty<ActiveEffect>(),
                    knockbackDistance: 0,
                    safeZoneConfirmed: true));
            }
        }

        private static void MergeSafeZoneCandidateMarks(
            List<CombatOverlayIconAnnotation> annotations,
            IReadOnlyList<HexCoord> unknownCandidates)
        {
            foreach (var coord in unknownCandidates)
            {
                var existing = annotations.FindIndex(annotation => annotation.Coord.Equals(coord));
                if (existing >= 0)
                {
                    annotations[existing] = new CombatOverlayIconAnnotation(
                        coord,
                        annotations[existing].StatusEffects,
                        annotations[existing].KnockbackDistance,
                        annotations[existing].AnnihilationTurnsRemaining,
                        annotations[existing].AnnihilationDamage,
                        safeZoneCandidate: true);
                    continue;
                }

                annotations.Add(new CombatOverlayIconAnnotation(
                    coord,
                    System.Array.Empty<ActiveEffect>(),
                    knockbackDistance: 0,
                    annihilationTurnsRemaining: 0,
                    annihilationDamage: 0,
                    safeZoneCandidate: true));
            }
        }

        private static void AddMonsterIntentOverlays(List<CombatOverlayLayerState> states, List<CombatOverlayIconAnnotation> annotations, CombatOverlayPresentationRequest request, bool visibleInCurrentPhase)
        {
            if (!visibleInCurrentPhase)
            {
                return;
            }

            if (request.State.Phase == CombatPhase.PlayerMovement)
            {
                var move = request.OverlayQuery.GetMonsterIntentMoveHighlightCells(
                    request.State,
                    request.LoadedMap,
                    request.RevealAllMapCellsInDebugMode,
                    request.HoveredMonsterId);
                Add(states, HexOverlayLayer.Path, request.ShowMonsterMoveOverlay, move);
                Add(states, HexOverlayLayer.MonsterMoveIntent, request.ShowMonsterMoveOverlay, move);
            }

            Add(states, HexOverlayLayer.MonsterAttackIntent, request.ShowMonsterAttackOverlay,
                request.OverlayQuery.GetMonsterIntentAttackHighlightCells(request.State, request.LoadedMap, request.RevealAllMapCellsInDebugMode, request.HoveredMonsterId));
            // 자기부여 예고(#10)는 공격 예고와 같은 토글을 탄다 — 둘 다 "이 몬스터가 이번 턴 무엇을
            // 하는가"이고, 하나만 꺼지면 자기부여 턴에만 정보가 새어 나온다.
            Add(states, HexOverlayLayer.MonsterSelfBuffIntent, request.ShowMonsterAttackOverlay,
                request.OverlayQuery.GetMonsterSelfBuffHighlightCells(request.State, request.LoadedMap, request.RevealAllMapCellsInDebugMode, request.HoveredMonsterId));
            // 소환 예고(요괴 §4-4)도 같은 토글 — "이 몬스터가 이번 턴 무엇을 하는가"의 일부다.
            Add(states, HexOverlayLayer.MonsterSummonIntent, request.ShowMonsterAttackOverlay,
                request.OverlayQuery.GetMonsterSummonHighlightCells(request.State, request.LoadedMap, request.RevealAllMapCellsInDebugMode, request.HoveredMonsterId));
            Add(states, HexOverlayLayer.MonsterChaseRange, request.ShowMonsterChaseOverlay,
                request.OverlayQuery.GetMonsterChaseHighlightCells(
                    request.State,
                    request.LoadedMap,
                    request.RevealAllMapCellsInDebugMode,
                    request.HoveredMonsterId));

            // Status-effect icons share the MonsterAttackIntent gate (Locked Decision #2: reuse
            // ShowMonsterAttackOverlay rather than a dedicated toggle).
            if (request.ShowMonsterAttackOverlay)
            {
                annotations.AddRange(request.OverlayQuery.GetMonsterAttackStatusIconCells(
                    request.State, request.LoadedMap, request.RevealAllMapCellsInDebugMode, request.HoveredMonsterId));
            }
        }

        /// <summary>손을 얹은 몬스터가 보스 기물이고 폭발 반경이 저작돼 있으면 그 원판, 아니면 빈 목록.</summary>
        internal static IReadOnlyList<HexCoord> ResolveHoveredBossPropBlastCells(CombatOverlayPresentationRequest request)
        {
            if (string.IsNullOrEmpty(request.HoveredMonsterId)
                || !request.State.TryGetBossPropBlastAuthoring(request.HoveredMonsterId, out var blastRadius, out _)
                || blastRadius < 0)
            {
                return System.Array.Empty<HexCoord>();
            }

            foreach (var monster in request.State.Monsters)
            {
                if (string.Equals(monster.Id, request.HoveredMonsterId, System.StringComparison.Ordinal) && !monster.IsDead)
                {
                    return HexArea.CellsWithin(monster.Coord, blastRadius).ToList();
                }
            }

            return System.Array.Empty<HexCoord>();
        }

        /// <summary>
        /// 레이어 상태를 추가하되, <b>같은 레이어가 이미 있으면 좌표를 합친다</b>.
        ///
        /// ⚠️ 합치지 않으면 조용히 사라진다: <see cref="CombatMapOverlayPresenter.ApplyPresentation"/>은
        /// 목록을 순서대로 돌며 <c>ShowLayer</c>를 부르고, 렌더러는 레이어당 비주얼 <b>하나</b>를 다시
        /// 만든다(교체이지 병합이 아니다) — 즉 뒤엣것이 앞엣것을 덮어쓴다. 실제로
        /// <see cref="HexOverlayLayer.MonsterAttackIntent"/>는 "이 칸은 위험"이라는 같은 어휘를
        /// 세 소스(주기 함정 예고 · 전멸기 예고 · 몬스터 공격 예고)가 <b>의도적으로 공유</b>하므로
        /// 중복 추가가 정상이고, 그 셋 중 뒤엣것만 그려지던 것이 결함이었다.
        /// 스타일은 먼저 들어온 것을 유지한다(같은 레이어면 <c>ResolveDefaultStyle</c> 결과가 동일하다).
        /// </summary>
        private static void Add(List<CombatOverlayLayerState> states, HexOverlayLayer layer, bool visible, IEnumerable<HexCoord> coords)
        {
            if (!visible)
            {
                return;
            }

            for (var i = 0; i < states.Count; i++)
            {
                if (states[i].Layer != layer)
                {
                    continue;
                }

                var merged = states[i].Coords.Concat(coords ?? Enumerable.Empty<HexCoord>()).Distinct().ToList();
                states[i] = new CombatOverlayLayerState(layer, merged, states[i].Style);
                return;
            }

            states.Add(new CombatOverlayLayerState(layer, coords, CombatOverlayTheme.ResolveDefaultStyle(layer)));
        }
    }

    public readonly struct CombatOverlayPresentationRequest
    {
        public CombatOverlayPresentationRequest(
            CombatState state,
            HexMapData loadedMap,
            CombatOverlayQuery overlayQuery,
            bool isMoveSelectionActive,
            CombatCardKind? selectedTargetCardKind,
            IEnumerable<HexCoord> reachableCoords,
            IEnumerable<HexCoord> selectedTargetHighlightCoords,
            bool revealAllMapCellsInDebugMode,
            bool showPlayerMovementOverlay,
            bool showPlayerActionOverlay,
            bool showMonsterMoveOverlay,
            bool showMonsterAttackOverlay,
            bool showMonsterChaseOverlay,
            string hoveredMonsterId = null,
            IEnumerable<HexCoord> selectedEffectAreaHighlightCoords = null,
            IReadOnlyList<HexCoord> bossFootprintCoords = null,
            IReadOnlyList<HexCoord> hoveredTraitReachCoords = null)
        {
            State = state;
            LoadedMap = loadedMap;
            OverlayQuery = overlayQuery ?? new CombatOverlayQuery();
            IsMoveSelectionActive = isMoveSelectionActive;
            SelectedTargetCardKind = selectedTargetCardKind;
            ReachableCoords = reachableCoords ?? System.Array.Empty<HexCoord>();
            SelectedTargetHighlightCoords = selectedTargetHighlightCoords ?? System.Array.Empty<HexCoord>();
            SelectedEffectAreaHighlightCoords = selectedEffectAreaHighlightCoords ?? System.Array.Empty<HexCoord>();
            RevealAllMapCellsInDebugMode = revealAllMapCellsInDebugMode;
            ShowPlayerMovementOverlay = showPlayerMovementOverlay;
            ShowPlayerActionOverlay = showPlayerActionOverlay;
            ShowMonsterMoveOverlay = showMonsterMoveOverlay;
            ShowMonsterAttackOverlay = showMonsterAttackOverlay;
            ShowMonsterChaseOverlay = showMonsterChaseOverlay;
            HoveredMonsterId = string.IsNullOrEmpty(hoveredMonsterId) ? null : hoveredMonsterId;
            BossFootprintCoords = bossFootprintCoords;
            HoveredTraitReachCoords = hoveredTraitReachCoords ?? System.Array.Empty<HexCoord>();
        }

        public CombatState State { get; }
        public HexMapData LoadedMap { get; }
        public CombatOverlayQuery OverlayQuery { get; }
        public bool IsMoveSelectionActive { get; }
        public CombatCardKind? SelectedTargetCardKind { get; }
        public IEnumerable<HexCoord> ReachableCoords { get; }
        public IEnumerable<HexCoord> SelectedTargetHighlightCoords { get; }
        public IEnumerable<HexCoord> SelectedEffectAreaHighlightCoords { get; }
        public bool RevealAllMapCellsInDebugMode { get; }
        public bool ShowPlayerMovementOverlay { get; }
        public bool ShowPlayerActionOverlay { get; }
        public bool ShowMonsterMoveOverlay { get; }
        public bool ShowMonsterAttackOverlay { get; }
        public bool ShowMonsterChaseOverlay { get; }

        /// <summary>
        /// When non-null, the monster attack-intent overlay (and its telegraphed status icons) is
        /// narrowed to this monster id only. Null shows every visible monster's attack intent.
        /// </summary>
        public string HoveredMonsterId { get; }

        /// <summary>
        /// 가시성 게이트를 통과한 보스 점유 좌표(#22). 컨트롤러가 몬스터 본체와 같은 규칙
        /// (스테이지 인트로 은닉 → 디버그 전체 공개 → 시네마틱 강제 공개 → 셀 Revealed)으로 걸러
        /// 넘긴다. null이면 <see cref="CombatState.BossFootprintCoords"/> 전량(게이트 없음)을 쓴다.
        /// </summary>
        public IReadOnlyList<HexCoord> BossFootprintCoords { get; }

        /// <summary>
        /// 지금 뒤끝 배지에 손을 얹어 그릴 칸들(2026-09-01 #3). 유도값이라 커서가 떠나면 비어 온다.
        /// </summary>
        public IReadOnlyList<HexCoord> HoveredTraitReachCoords { get; }
    }
}

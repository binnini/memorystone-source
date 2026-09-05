using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>옛 <c>CombatState.BossAnnihilation.cs</c>의 본문(3-B · 본문 무변경 이동).</summary>
    internal sealed partial class BossEncounterState
    {
        /// <summary>
        /// 전멸기를 개시한다: <b>중앙 점프 → 착지 처리 → 후보 3곳 → 예고</b>. 반환값은 예고된 칸 수이며,
        /// 0이면 아무것도 일어나지 않았다(<b>점프도 하지 않는다</b>).
        ///
        /// <para>🔴 순서가 규칙이다. 후보 산출은 <b>점프 커밋보다 먼저</b> 시도해야 한다 — 점프한 뒤에
        /// 포기하면 보스만 중앙으로 옮겨진 채 아무 일도 안 일어난다. 단 후보 판정은 <b>점프 후 좌표
        /// 기준</b>이어야 하므로(보스 몸이 후보 칸을 덮을 수 있다) 구현은 가상 점프 좌표로 후보를 미리
        /// 계산하고, 성공했을 때만 점프를 커밋하는 2단계다.</para>
        ///
        /// <para>🔴 봉인 중 보스 이동 0(C-5)의 <b>예외</b>다. 반드시 기믹 <c>Resolve</c> 안에서만
        /// 일어나야 하고 플래너 경로(<c>ApplyControlStatusConstraintToPlan</c>)를 타면 안 된다 —
        /// §17에서 도약이 죽어 있던 원인이 정확히 그 경로다. 기믹 결의는 이동 단계 <b>뒤</b>에 돌므로
        /// 그 턴에는 안전하고, 다음 턴은 이동 0이라 제자리에 남는다.</para>
        /// </summary>
        internal int BeginBossAnnihilation(BossPhaseTrack track, MonsterRuntime boss, CombatState.BossAnnihilationRequest request)
        {
            if (track == null || boss == null || host.Player.IsDead)
            {
                return 0;
            }

            if (!TryResolveAnnihilationLanding(boss, out var landing))
            {
                return 0;
            }

            // ① 가상 점프 좌표 기준으로 후보를 먼저 세운다. 세우지 못하면 발동 포기 — 점프도 없다.
            if (!TryResolveAnnihilationCandidates(boss, landing, request, out var candidates))
            {
                return 0;
            }

            // ② 커밋. 여기부터는 되돌리지 않는다.
            CommitBossAnnihilationJump(boss, landing, request);

            // ③ 진짜/가짜 배정. 가짜가 정확히 (후보 − 진짜)개이며, 계약상 그 값은 1이다(저작 가드).
            //    무작위 결과는 트랙에 저장하고 서스펜드 왕복시킨다 — 저장/재개로 답을 다시 굴리는
            //    세이브 스컴을 막는다(RNG 시드 재현이 불가능하므로 재현이 아니라 저장으로 해결한다).
            var fakeCount = Math.Max(0, candidates.Count - request.RealSafeCells);
            var shuffled = candidates.OrderBy(_ => host.PushRng.Next()).ToList();
            var fakes = new HashSet<HexCoord>(shuffled.Take(fakeCount));

            track.AnnihilationCandidateCells.Clear();
            track.AnnihilationCandidateCells.AddRange(candidates);
            track.AnnihilationRealSafeCells.Clear();
            track.AnnihilationRealSafeCells.AddRange(candidates.Where(coord => !fakes.Contains(coord)));
            track.AnnihilationRevealedCandidates.Clear();

            // ④ 예고 = 영역 − 진짜 후보 − 보스 점유 칸. <b>가짜 후보는 예고에 포함된다</b> — 그래서
            //    판별 시 별도 "가짜" 색이 필요 없다: `?`를 걷어내면 아래 깔린 붉은 예고가 그대로 드러난다.
            var area = EnumerateAnnihilationArea(boss, request.FallbackRadius)
                .Where(IsAnnihilationAreaCell)
                .Distinct()
                .ToList();
            var realSafe = new HashSet<HexCoord>(track.AnnihilationRealSafeCells);

            track.AnnihilationTelegraphCells.Clear();
            foreach (var coord in area)
            {
                // 보스 자신의 점유 칸은 예고에서 뺀다 — 어차피 설 수 없는 칸에 위험 표시를 겹치면
                // 점유 오버레이(C-4)와 어휘가 충돌한다.
                if (realSafe.Contains(coord) || host.IsMonsterOccupying(boss, coord))
                {
                    continue;
                }

                track.AnnihilationTelegraphCells.Add(coord);
            }

            return track.AnnihilationTelegraphCells.Count;
        }

        /// <summary>
        /// 발동 가능한가를 <b>상태를 바꾸지 않고</b> 확인한다. 점프 턴 공격 억제 질의(§20-B-3-1)가
        /// 결의 <b>전에</b> 이 답을 쓰므로, 여기서 참이었다가 실제 발동이 실패하면 보스가 아무것도
        /// 하지 않는 턴이 된다 — 그래서 같은 판정 경로를 그대로 재사용한다(중복 판정 = 드리프트).
        /// </summary>
        internal bool CanBeginBossAnnihilation(MonsterRuntime boss, CombatState.BossAnnihilationRequest request)
        {
            return boss != null
                && !host.Player.IsDead
                && TryResolveAnnihilationLanding(boss, out var landing)
                && TryResolveAnnihilationCandidates(boss, landing, request, out _);
        }

        /// <summary>
        /// 착지 지점(아레나 중심, 없으면 제자리)과 착지 원판. 원판이 아레나 밖으로 삐져나오면
        /// 발동을 포기한다 — 결계 밖에 몸을 걸친 보스는 히트테스트·이동 계약을 통째로 흔든다.
        /// </summary>
        private bool TryResolveAnnihilationLanding(MonsterRuntime boss, out HexCoord landing)
        {
            landing = boss.Coord;
            if (!IsBossArenaBarrierActive || !TryGetSealedArena(out var area) || area.Coords.Count == 0)
            {
                // 아레나 없는 보스전(고정형 등)에서는 점프할 "중앙"이 없다 — 제자리에서 발동한다.
                return true;
            }

            var center = area.GetCenter();
            var radius = GetMonsterFootprintRadius(boss);
            foreach (var coord in HexArea.CellsWithin(center, radius))
            {
                if (!area.Contains(coord) || !host.Map.TryGetCell(coord, out var cell) || !cell.BaseWalkable)
                {
                    return false;
                }
            }

            landing = center;
            return true;
        }

        /// <summary>
        /// 착지 커밋. 순서가 규칙이다(§20-B-3):
        /// <list type="number">
        /// <item>보스를 옮긴다(경로를 걷지 않는다 — 재배치다).</item>
        /// <item>원판 안의 기물을 <b>조용히 파괴</b>한다. ⚠️ 흡수로 처리하면 스택이 들어가
        ///       전멸기가 페이즈 가속기로 변질된다 — 보상 없는 제거여야 한다.</item>
        /// <item><c>UpdateOccupancy()</c>.</item>
        /// <item>플레이어가 원판 안이면 밀어낸다(기존 <c>PushPlayerOutOfGrownBossFootprint</c> 재사용).</item>
        /// <item>착지 <b>인접 링</b>에 피해. 4→5 순서 때문에 몸통 안에 있던 플레이어는 밀려난 뒤
        ///       인접 링에서 반드시 착지 피해를 맞는다 — 보스에게 붙어 있던 대가다.</item>
        /// </list>
        /// </summary>
        private void CommitBossAnnihilationJump(MonsterRuntime boss, HexCoord landing, CombatState.BossAnnihilationRequest request)
        {
            var from = boss.Coord;
            boss.Coord = landing;

            // §21.7 양방향 배타(판에 철조각이 있으면 점프하지 않는다) 이후 이 파괴는 정상 경로에서
            // 도달 불가다 — 그래도 지우지 않는다: 폴백 반경 경로 등 예외 상황에서 착지 원판 안에
            // 기물이 남아 있으면 점유 계약이 깨지므로 방어 가드로 존치한다(§21.7-얻는 것-1).
            var radius = GetMonsterFootprintRadius(boss);
            foreach (var prop in host.Monsters
                         .Where(monster => !monster.Combatant.IsDead
                                           && MonsterSpawnRoles.IsBossProp(monster.SpawnRole)
                                           && landing.DistanceTo(monster.Coord) <= radius)
                         .ToList())
            {
                host.RemoveMonster(prop);
            }

            host.UpdateOccupancy();
            PushPlayerOutOfGrownBossFootprint();

            host.RaiseEffect(
                EffectKind.Knockback,
                landing,
                radius,
                from.DistanceTo(landing),
                boss.Id,
                BossAnnihilationSourceRefs.Jump,
                sourceUnitId: boss.Id,
                sourceActorKind: "monster",
                targetActorKind: "monster");

            ApplyBossAnnihilationLandingBlast(boss, landing, radius, request.LandingBlastDamage);
        }

        /// <summary>
        /// 착지 인접 링(중심 거리 == 반경 + 1)의 피해. 필드 피해 계약을 따른다 —
        /// Block이 먼저 깎이고, 전부 막히면 "방어!"가 뜬다(흡수 폭발·전멸기 폭발과 같은 규약).
        /// </summary>
        private void ApplyBossAnnihilationLandingBlast(MonsterRuntime boss, HexCoord landing, int radius, int damage)
        {
            if (damage <= 0 || host.Player.IsDead || landing.DistanceTo(host.PlayerCoord) != radius + 1)
            {
                return;
            }

            var blockBefore = host.Player.Block;
            var applied = host.Player.ApplyDamage(damage);
            if (applied <= 0 && host.Player.Block >= blockBefore)
            {
                return;
            }

            host.RaiseEffect(
                applied > 0 ? EffectKind.Damage : EffectKind.DamageBlocked,
                host.PlayerCoord,
                0,
                applied,
                CombatState.PlayerUnitId,
                BossAnnihilationSourceRefs.LandingBlast,
                sourceUnitId: boss?.Id ?? string.Empty,
                sourceActorKind: "monster",
                targetActorKind: "player");
        }

        /// <summary>
        /// 후보 <paramref name="request"/>.CandidateCells곳을 <b>계단식 완화</b>로 찾는다(§20-B-4).
        ///
        /// <para>하드 조건(절대 완화하지 않는다): 이동 가능 지형 · 이동 차단 오브젝트 없음 ·
        /// living 몬스터 비점유(점프 후 보스 몸 포함) · <b>플레이어 현재 칸 제외</b>(제자리는 답이 아니다
        /// = 이동 강제 퍼즐) · <b>필드 오브젝트 없음</b>(장판·폭탄류).</para>
        ///
        /// <para>소프트 조건은 이격 3→2→1을 <b>먼저</b> 소진하고, 그래도 안 되면 함정 겹침을 허용한다.
        /// 🔑 완화 순서의 근거: 이격 완화는 후보들이 서로 가까워지는 것이고 결정 3이 "정찰 하나가 여러
        /// 후보를 덮으면 둘 다 판별"을 허용했으므로 플레이어에게 <b>순이득이거나 최소한 손해가 아니다</b>.
        /// 반면 함정 겹침은 <b>실제 피해</b>다. 공짜 완화를 전부 소진한 뒤에 유료 완화로 넘어간다.</para>
        ///
        /// <para>✅ 회피 100% 보장은 이격이 아니라 "가짜가 정확히 1개"(§20-B-2)에서 나오므로,
        /// 어느 단계로 내려가도 계약은 유지된다.</para>
        /// </summary>
        private bool TryResolveAnnihilationCandidates(
            MonsterRuntime boss,
            HexCoord landing,
            CombatState.BossAnnihilationRequest request,
            out List<HexCoord> candidates)
        {
            candidates = null;
            var radius = GetMonsterFootprintRadius(boss);
            // §21.6: 필드 오브젝트 칸을 배제하지 <b>않는다</b>. 예전엔 완화 불가 하드 조건으로 배제했고,
            // 후보 풀이 플레이어 반경 2(최대 18칸)라 반경 2 장판 한 장(19칸)이면 전멸기가 통째로 발동을
            // 포기하는 익스플로잇이 있었다. 폭발이 예고 칸의 장판을 파괴하므로(ResolveBossAnnihilationBlast)
            // 후보를 막을 이유가 없다 — 파괴와 이 완화는 한 쌍이며 한쪽만 되돌리면 안 된다.
            var pool = HexArea.CellsWithin(host.PlayerCoord, request.SafeReach)
                .Where(coord => coord != host.PlayerCoord)
                .Where(IsAnnihilationAreaCell)
                .Where(coord => landing.DistanceTo(coord) > radius)
                .Where(coord => !IsCoordOccupiedByLivingMonsterOtherThan(coord, boss))
                // 순서는 결정적이어야 한다(같은 상황이 매번 같은 후보를 낸다). 플레이어에게 가까운 순.
                .OrderBy(coord => coord.DistanceTo(host.PlayerCoord))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .ToList();
            if (pool.Count < request.CandidateCells)
            {
                return false;
            }

            foreach (var allowTraps in new[] { false, true })
            {
                var usable = allowTraps ? pool : pool.Where(coord => !HasArmedTrapAt(coord)).ToList();
                for (var spacing = request.CandidateSpacing; spacing >= 1; spacing--)
                {
                    if (TryPickSpacedCells(usable, request.CandidateCells, spacing, out candidates))
                    {
                        return true;
                    }
                }
            }

            candidates = null;
            return false;
        }

        /// <summary>
        /// 서로 <paramref name="spacing"/>칸 이상 떨어진 칸을 <paramref name="count"/>개 그리디로 고른다.
        /// 후보 순서가 이미 결정적이므로 결과도 결정적이다.
        /// </summary>
        private static bool TryPickSpacedCells(
            IReadOnlyList<HexCoord> pool, int count, int spacing, out List<HexCoord> picked)
        {
            picked = new List<HexCoord>(count);
            foreach (var coord in pool)
            {
                if (picked.All(chosen => chosen.DistanceTo(coord) >= spacing))
                {
                    picked.Add(coord);
                    if (picked.Count == count)
                    {
                        return true;
                    }
                }
            }

            picked = null;
            return false;
        }

        /// <summary>
        /// 이 칸에 아직 살아 있는 함정이 있는가(소진된 것은 없는 것과 같다). <b>발견 여부는 보지 않는다</b> —
        /// 후보 배치는 규칙이고, 플레이어가 그 함정을 아느냐는 별개의 질문이다.
        /// </summary>
        private bool HasArmedTrapAt(HexCoord coord)
        {
            foreach (var trap in host.AllTrapRefs)
            {
                if (trap.Contains(coord) && !(trap.OneShot && host.IsTrapConsumed(trap.TrapId)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>이 칸이 전멸기 영역·후보로 성립하는 바닥 조건(이동 가능 지형 + 차단 오브젝트 없음).</summary>
        private bool IsAnnihilationAreaCell(HexCoord coord)
        {
            return host.Map != null
                && host.Map.TryGetCell(coord, out var cell)
                && cell.BaseWalkable
                && host.TerrainTraits.IsWalkable(cell.TerrainTypeId)
                && !host.Map.HasMovementBlockingObject(coord)
                && IsInsideActiveBossArena(coord);
        }

        private bool IsCoordOccupiedByLivingMonsterOtherThan(HexCoord coord, MonsterRuntime excluded)
        {
            foreach (var monster in host.Monsters)
            {
                if (monster.Combatant.IsDead || ReferenceEquals(monster, excluded))
                {
                    continue;
                }

                if (host.IsMonsterOccupying(monster, coord))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 정찰이 후보를 덮으면 진위를 판별한다(§20-B-4 결정 3: 넓은 정찰이 후보 여러 곳을 덮으면
        /// <b>둘 다</b> 판별한다 — 상위 정찰 카드의 정상적 보상으로 인정한다).
        ///
        /// 함정과 겹친 후보에 강제 발견 특례를 두지 않는다(결정 9): <c>TryPlayerScout</c>이 바로 뒤에서
        /// <c>RevealTrapsInArea</c>를 부르므로 <b>같은 카드 한 장</b>이 "여기가 진짜인가"와 "여기 함정이
        /// 있는가"를 동시에 답한다. 특례를 넣으면 함정 발견의 의미가 두 갈래로 갈라진다.
        /// </summary>
        internal void RevealBossAnnihilationCandidatesInScoutArea(HexCoord target, int revealRadius)
        {
            var radius = Math.Max(0, revealRadius);
            foreach (var track in bossPhaseTracks)
            {
                foreach (var candidate in track.AnnihilationCandidateCells)
                {
                    if (target.DistanceTo(candidate) <= radius
                        && !track.AnnihilationRevealedCandidates.Contains(candidate))
                    {
                        track.AnnihilationRevealedCandidates.Add(candidate);
                    }
                }
            }
        }

        /// <summary>
        /// 폭발 해소: 예고 시점에 저장된 칸 집합(예고=명중)에 플레이어가 서 있으면 피해를 준다.
        /// 피해는 필드 피해 계약(<see cref="ApplyFieldDamageToPlayer"/>)을 따라 Block이 먼저 깎이고,
        /// 방어 성공 시 "방어!"가 뜬다. 해소 후 예고와 후보는 항상 지워진다(맞았든 피했든).
        /// </summary>
        internal void ResolveBossAnnihilationBlast(BossPhaseTrack track, MonsterRuntime boss, int damage)
        {
            if (track == null || track.AnnihilationTelegraphCells.Count == 0)
            {
                return;
            }

            var hitPlayer = !host.Player.IsDead && track.AnnihilationTelegraphCells.Contains(host.PlayerCoord);
            if (hitPlayer && damage > 0)
            {
                var blockBefore = host.Player.Block;
                var applied = host.Player.ApplyDamage(damage);
                if (applied > 0)
                {
                    host.RaiseEffect(
                        EffectKind.Damage,
                        host.PlayerCoord,
                        0,
                        applied,
                        CombatState.PlayerUnitId,
                        "boss.annihilation",
                        sourceUnitId: boss?.Id ?? string.Empty,
                        sourceActorKind: "monster",
                        targetActorKind: "player");
                }
                else if (host.Player.Block < blockBefore)
                {
                    host.RaiseEffect(
                        EffectKind.DamageBlocked,
                        host.PlayerCoord,
                        0,
                        0,
                        CombatState.PlayerUnitId,
                        "boss.annihilation",
                        sourceUnitId: boss?.Id ?? string.Empty,
                        sourceActorKind: "monster",
                        targetActorKind: "player");
                }
            }

            // §21.6: 폭발이 예고 칸에 <b>걸친</b> 필드 오브젝트를 함께 파괴한다. 진짜 안전지대 위에만
            // 놓인 장판은 자동으로 살아남는다(안전지대는 예고에서 빠져 있다) — 규칙이 예고와 완전히
            // 일치한다. 소유자 분기는 없다(필드 오브젝트는 전부 플레이어 것 · 생성 지점 한 곳).
            // MassImmobilize도 함께 지워진다 — 속박으로 회피를 봉쇄하는 대응 자체는 의도이며(§28.1 D2),
            // 그 효력은 폭발 전 턴까지로 충분하다. 이 파괴는 후보 하드 조건 완화
            // (TryResolveAnnihilationCandidates)와 한 쌍이다.
            var telegraphed = new HashSet<HexCoord>(track.AnnihilationTelegraphCells);
            host.FieldObjects.RemoveAll(existing => telegraphed.Any(existing.Contains));
            host.PendingFieldObjects.RemoveAll(existing => telegraphed.Any(existing.Contains));

            track.AnnihilationTelegraphCells.Clear();
            track.AnnihilationTelegraphTurnsRemaining = 0;
            track.AnnihilationCandidateCells.Clear();
            track.AnnihilationRealSafeCells.Clear();
            track.AnnihilationRevealedCandidates.Clear();
        }

        /// <summary>
        /// 살아 있는 보스들의 전멸기 예고 칸(표현용). 오버레이 빌더가 주기 함정 예고와 같은 레이어
        /// (MonsterAttackIntent = "이번에 위험한 칸" 어휘)로 그린다. 죽은 보스의 잔여 예고는 걸러 낸다 —
        /// 기믹 루프가 죽은 보스를 건너뛰므로 폭발도 없다.
        /// </summary>
        public IReadOnlyList<HexCoord> GetBossAnnihilationTelegraphCells()
        {
            List<HexCoord> result = null;
            foreach (var telegraph in GetBossAnnihilationTelegraphs())
            {
                result ??= new List<HexCoord>();
                result.AddRange(telegraph.Cells);
            }

            return (IReadOnlyList<HexCoord>)result ?? Array.Empty<HexCoord>();
        }

        /// <summary>
        /// 진행 중인 전멸기 예고를 보스별로 투영한다 — 칸 집합에 더해 <b>남은 턴</b>과 <b>피해</b>까지.
        /// 예고 오버레이는 평범한 공격 예고와 같은 위험 레이어를 쓰므로(같은 붉은 해치), 그 둘을 가르는
        /// 경고 아이콘·호버 툴팁이 이 값들을 읽는다. 죽은 보스의 잔여 예고는 여기서 걸러진다 —
        /// 기믹 루프가 죽은 보스를 건너뛰므로 폭발도 일어나지 않는다.
        /// </summary>
        public IReadOnlyList<BossAnnihilationTelegraphState> GetBossAnnihilationTelegraphs()
        {
            List<BossAnnihilationTelegraphState> result = null;
            foreach (var track in bossPhaseTracks)
            {
                if (track.AnnihilationTelegraphCells.Count == 0)
                {
                    continue;
                }

                var boss = host.Monsters.FirstOrDefault(monster =>
                    string.Equals(monster.Id, track.BossUnitId, StringComparison.Ordinal));
                if (boss == null || boss.Combatant.IsDead)
                {
                    continue;
                }

                // 피해·이름은 저장하지 않고 프로필에서 매번 유도한다(수치는 CSV 저작이 정본).
                var damage = 0;
                var displayName = string.Empty;
                if (bossCatalog != null && bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
                {
                    damage = profile.GetMechanicInt(AnnihilationMechanicParams.Damage);
                    displayName = profile.DisplayName;
                }

                result ??= new List<BossAnnihilationTelegraphState>();
                result.Add(new BossAnnihilationTelegraphState(
                    track.BossUnitId,
                    displayName,
                    track.AnnihilationTelegraphCells.ToList(),
                    track.AnnihilationTelegraphTurnsRemaining,
                    damage));
            }

            return (IReadOnlyList<BossAnnihilationTelegraphState>)result
                   ?? Array.Empty<BossAnnihilationTelegraphState>();
        }

        /// <summary>
        /// 안전지대 후보의 표현용 투영(§20-B-6). 미판별 후보만 <c>?</c>를 달고, 판별된 후보는
        /// 여기서 상태가 갈린다 — 진짜는 "예고가 없다"가 이미 안전을 뜻하고(별도 색 불필요),
        /// 가짜는 <c>?</c>가 걷히면서 아래 깔린 붉은 예고가 드러난다.
        /// </summary>
        public IReadOnlyList<BossSafeZoneCandidateState> GetBossSafeZoneCandidates()
        {
            List<BossSafeZoneCandidateState> result = null;
            foreach (var track in bossPhaseTracks)
            {
                if (track.AnnihilationCandidateCells.Count == 0)
                {
                    continue;
                }

                var boss = host.Monsters.FirstOrDefault(monster =>
                    string.Equals(monster.Id, track.BossUnitId, StringComparison.Ordinal));
                if (boss == null || boss.Combatant.IsDead)
                {
                    continue;
                }

                foreach (var coord in track.AnnihilationCandidateCells)
                {
                    var revealed = track.AnnihilationRevealedCandidates.Contains(coord);
                    var state = !revealed
                        ? BossSafeZoneCandidateKind.Unknown
                        : track.AnnihilationRealSafeCells.Contains(coord)
                            ? BossSafeZoneCandidateKind.Real
                            : BossSafeZoneCandidateKind.Fake;

                    result ??= new List<BossSafeZoneCandidateState>();
                    result.Add(new BossSafeZoneCandidateState(track.BossUnitId, coord, state));
                }
            }

            return (IReadOnlyList<BossSafeZoneCandidateState>)result
                   ?? Array.Empty<BossSafeZoneCandidateState>();
        }

        /// <summary>폭발 영역의 원본: 봉인된 아레나가 있으면 그 전체, 없으면 보스 중심 원판(§13.5).</summary>
        private IEnumerable<HexCoord> EnumerateAnnihilationArea(MonsterRuntime boss, int fallbackRadius)
        {
            if (IsBossArenaBarrierActive && TryGetSealedArena(out var area) && area.Coords.Count > 0)
            {
                return area.Coords;
            }

            return HexArea.CellsWithin(boss.Coord, Math.Max(1, fallbackRadius));
        }
    }
}

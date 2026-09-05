using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    // 몬스터 AI 해소 레이어: 커밋된 턴 계획을 실제로 굴린다(공격 해소·피해·넉백·상태이상·연출 이벤트)
    // + 표시용 조회와 제어 상태 부여 경로.
    //
    // 레이어 경계:
    //   · 의사결정 트리      = IMonsterAi(MonsterBehaviorTreeAi)
    //   · 계획(무엇을 할지)  = MonsterAiPlanner (IMonsterPlanningContext 표면만 사용)
    //   · 해소(실제로 굴림)  = 이 파셜
    // 계획부 진입점은 아래 planner 위임 메서드들뿐이며, 그 뒤로는 CombatState 내부를 만지지 않는다.
    public sealed partial class CombatState : IMonsterPlanningContext
    {
        private readonly MonsterAiPlanner planner;

        // IMonsterPlanningContext: 이미 public인 Map/PlayerCoord/Config/TerrainTraits/RuntimeStates는
        // 암시적으로 계약을 만족하고, 나머지는 명시적 구현으로 노출해 public API를 넓히지 않는다.
        bool IMonsterPlanningContext.IsPlayerDead => Player.IsDead;

        IReadOnlyList<MonsterRuntime> IMonsterPlanningContext.PlanningMonsters => monsters;

        IEnumerable<MonsterRuntime> IMonsterPlanningContext.MonsterActionOrder() => GetMonsterActionOrder();

        MonsterActivityState IMonsterPlanningContext.ClassifyMonsterActivity(MonsterRuntime monster) => ClassifyMonsterActivity(monster);

        bool IMonsterPlanningContext.IsMonsterMovementBlocked(MonsterRuntime monster) => IsMonsterMovementBlocked(monster);

        bool IMonsterPlanningContext.IsMonsterMovementRooted(MonsterRuntime monster) => IsMonsterMovementRooted(monster);

        bool IMonsterPlanningContext.IsMonsterAttackBlocked(MonsterRuntime monster) => IsMonsterAttackBlocked(monster);

        int IMonsterPlanningContext.GetMonsterFootprintRadius(MonsterRuntime monster) => GetMonsterFootprintRadius(monster);
        IReadOnlyList<HexCoord> IMonsterPlanningContext.GetMonsterFootprintOffsets(MonsterRuntime monster) => GetMonsterFootprintOffsets(monster);

        int IMonsterPlanningContext.GetMonsterPatternPhaseGate(MonsterRuntime monster) => GetMonsterPatternPhaseGateInternal(monster);

        bool IMonsterPlanningContext.IsAttackPatternSuppressed(MonsterRuntime monster, MonsterAttackPattern pattern) =>
            IsSummonPatternCapped(monster, pattern);

        // Presentation-only: the ordered tile route a monster intends to walk this turn, so the move-path
        // presenter can draw an arrow/ghost. Mirrors the planning logic but never mutates state.
        public IReadOnlyList<HexCoord> GetMonsterMovePath(string monsterId)
        {
            var monster = monsters.FirstOrDefault(candidate => !candidate.Combatant.IsDead && candidate.Id == monsterId);
            if (monster == null)
            {
                return Array.Empty<HexCoord>();
            }

            var plan = monster.TurnPlan;
            var origin = monster.Coord;
            // 은폐된 예고(미지·은신)는 이동 경로도 없다(2026-09-05 실플레이: 어둑시니가 숨어 있는데
            // 이동 예정 잔상이 그려졌다). GetMonsterIntentPreviews는 이미 예측 좌표를 제자리로 되돌려
            // 이동 예고를 지우지만, 이 경로 술어는 예측 좌표를 직접 읽어 다른 답을 냈다 — 같은 은폐
            // 술어를 여기서도 보아 예고와 잔상이 갈라지지 않게 한다. 규칙은 그대로다(실제 이동은 한다).
            if (IsMonsterIntentHidden(monster))
            {
                return new[] { origin };
            }

            var destination = plan.IsActive ? plan.PlannedMoveCoord : monster.IntentPredictedMoveCoord;
            if (destination == origin)
            {
                return new[] { origin };
            }

            var movePoints = Math.Max(GetMonsterEffectiveMoveBudget(monster), origin.DistanceTo(destination));
            var path = HexPathfinder.FindPath(
                Map,
                new MovementQuery(
                    origin,
                    movePoints,
                    includeStart: true,
                    unitId: monster.Id,
                    footprintRadius: GetMonsterFootprintRadius(monster),
                    footprintOffsets: GetMonsterFootprintOffsets(monster)),
                destination,
                runtimeStates,
                terrainTraits);
            if (path.Count < 2)
            {
                return new[] { origin, destination };
            }

            return path;
        }

        // Movement is blocked by Immobilize (속박), Stun (기절), or a MassImmobilize field (섬광 장판).
        // Attack is blocked only by Stun ??Immobilize roots the monster in place but it can still attack.
        //
        // §21.3 개정(2026-08-05): <b>멀티셀 축을 제거했다.</b> 그 축(IsMultiCellSealedArenaBoss)이 존재한
        // 유일한 근거는 "원판 클리어런스 경로탐색이 없다"였는데(§14.3), 이제 있다 —
        // MovementQuery.FootprintRadius를 받은 HexPathfinder.CanEnter가 원판 전체의 통과 가능성을 묻는다.
        // 근거가 사라졌으므로 축도 사라진다. 결과적으로 이동 차단 = rooted 하나이고, 2·3페이즈 보스는
        // 일반 몬스터와 같은 경로탐색으로 걷는다.
        // 🔑 걷기를 막던 사고들은 이제 게이트가 아니라 <b>경로탐색 자체</b>가 막는다: 못 지나가는 틈은
        // 원판 검사가, 결계 링·철조각은 각 칸의 진입 판정이(둘 다 이미 CanEnter를 막는다) 걸러낸다.
        private bool IsMonsterMovementBlocked(MonsterRuntime monster)
        {
            return IsMonsterMovementRooted(monster);
        }

        /// <summary>
        /// <b>몸이 묶여 있는가</b>(속박·기절·섬광 장판). §21.3 이후로 이것이 이동 차단의 <b>유일한</b>
        /// 축이며 <see cref="IsMonsterMovementBlocked"/>와 같은 답을 준다. 두 이름을 남겨 두는 이유는
        /// <b>도약</b>(§17)이 "걷기 차단"이 아니라 "몸이 묶임"에만 걸린다는 계약을 호출부에서 읽히게
        /// 하기 위해서다 — 걷기만 막는 축이 다시 생기면 그때 둘이 갈라진다.
        /// </summary>
        private bool IsMonsterMovementRooted(MonsterRuntime monster)
        {
            return FieldObjects.Objects.Any(fieldObject =>
                fieldObject.Kind == FieldObjectKind.MassImmobilize
                && !fieldObject.IsExpired
                && fieldObject.Contains(monster.Coord))
                || HasActiveEffect(monster.Id, StatusEffectKind.Immobilize)
                || HasActiveEffect(monster.Id, StatusEffectKind.Stun);
        }

        private bool IsMonsterAttackBlocked(MonsterRuntime monster)
        {
            // 공격 차단의 단일 술어(기절 게이트 선례). 보스 기믹이 그 턴의 행동을 <b>대체</b>하는 경우
            // (철조각 살포)도 여기로 들어와야 예고와 결의가 갈라지지 않는다 — 두 경로가 모두 이 술어를 읽는다.
            return HasActiveEffect(monster.Id, StatusEffectKind.Stun)
                   || IsMonsterAttackReplacedByBossMechanic(monster);
        }

        /// <summary>
        /// 이 몬스터의 예고를 화면에서 감추는가(미지, D-4). <b>표시에만</b> 영향을 준다 —
        /// 이동도 공격도 평소대로 결의되고 집행된다.
        ///
        /// <para>🔴 <see cref="IsMonsterAttackBlocked"/>와 절대 합치지 말 것. 둘 다 예고를 비우지만
        /// 이유가 반대다: 기절은 <b>실제로 공격이 취소돼서</b> 예고할 것이 없고, 미지는 <b>공격이
        /// 그대로 나가는데</b> 보여주지 않는 것이다. 합치면 미지 몬스터가 공격을 못 하게 되고,
        /// 그 증상은 "왜 가끔 안 때리지?"로 나타나 원인을 찾기 어렵다.</para>
        /// </summary>
        internal bool IsMonsterIntentHidden(MonsterRuntime monster)
        {
            return monster != null && IsMonsterIntentHidden(monster.Id);
        }

        /// <summary>
        /// 뷰가 쓰는 표면. 툴팁·마커는 <c>MonsterRuntimeState</c>만 들고 있어 런타임 인스턴스에
        /// 접근할 수 없으므로 id로 묻는다. 은폐 판정 축을 하나로 유지하기 위한 오버로드다 —
        /// 뷰가 "Unknown 효과가 있나"를 직접 뒤지기 시작하면 판정이 두 벌이 된다.
        /// </summary>
        public bool IsMonsterIntentHidden(string monsterId)
        {
            // 은신(어둑시니)도 같은 은폐를 쓴다(2026-09-01 사용자 확정): 숨은 몬스터는 이동 예고도
            // 공격 예고도 내지 않는다. 종전에는 "예고는 감추지 않는다"였으나, 마커가 없는 채로 예고만
            // 뜨는 화면이 「누가 겨누는지 모를 위험 칸」으로 읽혔다. 대신 맞는 순간 "기습!"이 뜬다.
            // 🔑 판정을 여기로 모으면 오버레이 서명(ComputeMonsterIntentOverlaySignature)이 이미 이 값을
            // 해싱하므로 노출·재은신에서 오버레이 캐시가 저절로 갱신된다.
            return !string.IsNullOrEmpty(monsterId)
                   && (IsMonsterIntentVeiledByStatus(monsterId)
                       || IsMonsterHiddenByStealth(monsterId));
        }

        /// <summary>
        /// 예고 은폐 중 <b>「미지」 상태이상으로 인한 것만</b>. 은신은 여기 없다.
        ///
        /// <para>🔴 <b>이 술어가 따로 서 있는 이유</b>(2026-09-02 #6): 은폐 <b>판정</b>은
        /// <see cref="IsMonsterIntentHidden"/> 하나로 모아 두는 것이 옳지만(예고 산출부와 오버레이 서명이
        /// 그 값을 본다), 네임플레이트의 <b>'?' 표식</b>까지 같은 값을 읽으면 <b>숨은 몬스터의 위치가
        /// 표식으로 샌다</b> — 마커를 감춰 놓고 그 자리에 물음표를 세우는 꼴이다. 표식은 "저 놈이
        /// 무엇을 하려는지 모른다"는 <b>보이는 몬스터</b>의 이야기이고, 은신은 "저기 누가 있는지조차
        /// 모른다"라서 애초에 표시할 대상이 아니다.</para>
        ///
        /// <para>🔑 판정을 두 벌로 가르는 것이 아니라 <b>쓰임새를 둘로 가른다</b>: 예고 산출은 합친
        /// 술어를, 표식은 이 술어를 쓴다.</para>
        /// </summary>
        public bool IsMonsterIntentVeiledByStatus(string monsterId)
        {
            return !string.IsNullOrEmpty(monsterId)
                   && HasActiveEffect(monsterId, StatusEffectKind.Unknown);
        }

            // Monster move distance for this turn after Slow and Agility modifiers. Floored at 0:
        // a monster slowed below its base move stays put (handled like a soft root, attack still allowed).
        private int GetMonsterEffectiveMoveBudget(MonsterRuntime monster)
        {
            // 0 하한: 이동력 0으로 저작된 고정 포탑(D-5)은 민첩이 걸리지 않는 한 제자리다.
            var baseBudget = System.Math.Max(0, monster.MovePerTurn);
            var hastened = SumActiveEffectAmountByValueMode(monster.Id, StatusEffectValueMode.MoveRangeBonus);
            var slowed = SumActiveEffectAmountByValueMode(monster.Id, StatusEffectValueMode.MoveRangePenalty);
            return System.Math.Max(0, baseBudget + hastened - slowed);
        }

        private void RefreshMonsterIntentStep()
        {
            planner.RefreshAllIntents();
        }

        private void ResolveMonsterAttackStep(IReadOnlyDictionary<string, MonsterActionResolutionBuilder> actionRecords = null)
        {
            // 공격 해석 직전(이동 후, 어떤 넉백도 일어나기 전 = 플레이어의 '기존 위치') 각 몬스터의 가시성을 기록한다.
            // 이후 넉백으로 플레이어 시야가 바뀌어도, 이 시점에 보이던 몬스터의 공격은 끝까지 가시 연출로 보여준다(빗맞음 포함).
            if (actionRecords != null)
            {
                foreach (var monster in monsters.Where(candidate => !candidate.Combatant.IsDead))
                {
                    if (actionRecords.TryGetValue(monster.Id, out var attackTimeRecord))
                    {
                        attackTimeRecord.VisibleAtAttackTime =
                            visibilityRuntime.GetVisibility(monster.Coord) == HexCellVisibility.Revealed;
                        // 공격 해소가 은신을 푸는 자리이므로, 「그때 숨어 있었나」는 반드시 여기서
                        // 찍어야 한다. 행동이 끝난 뒤 재면 방금의 기습이 「보이던 공격」이 된다.
                        attackTimeRecord.HiddenByStealthDuringAction |= IsMonsterHiddenByStealth(monster);
                    }
                }
            }

            // 지난 사이클의 방어막을 여기서 지운다 — 몬스터가 <b>다시 행동하기 직전</b>이 플레이어의
            // ClearBlock과 대칭인 자리다. 몬스터 방어막은 자기 행동에서 얻어 플레이어의 다음 턴을
            // 버티고, 자기 다음 행동 직전에 풀린다("그 사이에 깎아라").
            //
            // 🔴 전체 턴 경계(BeginNextOverallTurn·플레이어 ClearBlock 자리)에 두면 안 된다 — 그 경계는
            // 몬스터 행동 <b>직후</b>라, 방금 두른 방어막이 같은 호출 안에서 즉시 증발한다(실측).
            ClearMonsterBlockExceptSturdy();

            var anyKilledByReflection = false;
            var attackOrder = 0;
            foreach (var monster in GetMonsterActionOrder())
            {
                if (monster.ActivityState == MonsterActivityState.Dormant)
                {
                    monster.PendingAttackIntent = false;
                    continue;
                }

                // A monster that had committed to an attack can whiff if the player was knocked out of its
                // resolved area before this monster's attack beat. Keep it in the same presentation queue so
                // visible attacks read as: wind-up -> impact -> "빗맞음" -> next monster gap.
                if (!CanPlannedMonsterActionAttack(monster) && ShouldPresentMissedMonsterAttack(monster))
                {
                    var stationaryPattern = monster.CurrentAttackPattern;
                    var stationaryGroupId = CreatePresentationGroupId(monster.Id, stationaryPattern.Id);
                    if (actionRecords != null && actionRecords.TryGetValue(monster.Id, out var stationaryRecord))
                    {
                        stationaryRecord.AttackedPlayer = true;
                        stationaryRecord.MissedAttack = true;
                        stationaryRecord.AttackOrder = attackOrder++;
                        stationaryRecord.AttackPatternId = stationaryPattern.Id;
                        stationaryRecord.AttackAnimationTrigger = stationaryPattern.AnimationTrigger;
                        stationaryRecord.PresentationGroupId = stationaryGroupId;
                        stationaryRecord.AimCoord = GetCommittedAimCoord(monster);
                    }

                    RaiseEffect(
                        EffectKind.AttackMissed,
                        monster.Coord,
                        stationaryPattern.AreaRadius,
                        0,
                        monster.Id,
                        ToMonsterPatternSourceRef(stationaryPattern),
                        sourceUnitId: monster.Id,
                        sourceActorKind: "monster",
                        targetActorKind: "player",
                        sourcePatternId: stationaryPattern.Id,
                        hitIndex: 0,
                        hitCount: 1,
                        presentationGroupId: stationaryGroupId,
                        sourceCoord: monster.Coord,
                        areaCoords: GetCommittedMonsterAttackFootprint(monster, stationaryPattern));
                    monster.PendingAttackIntent = false;
                    continue;
                }

                if (CanPlannedMonsterActionAttack(monster))
                {
                    var pattern = monster.CurrentAttackPattern;
                    // 고슴도치 인형(T2 페이즈 C)의 인접 판정은 넉백으로 좌표가 흐트러지기 전, 공격이
                    // 명중하는 이 시점에 잰다(monster.Coord 거리 1 — 인계문 확정).
                    var wasAdjacentAtImpact = PlayerCoord.DistanceTo(monster.Coord) == 1;
                    // Put this pattern on cooldown now that it actually lands, so status-effect attacks
                    // (stun/slow/poison/…) cannot be selected again for the next CooldownTurns monster turns.
                    monster.StartAttackPatternCooldown(monster.AttackPatternIndex);
                    // 방어막 저작(shieldGain)은 공격이 <b>실제로 성립한</b> 이 자리에서 준다 — 예고만
                    // 띄우고 불발한 턴에 굳으면 교환이 공짜가 된다(불가살 trap-volley와 같은 규약).
                    GrantMonsterPatternShield(monster, pattern);
                    var presentationGroupId = CreatePresentationGroupId(monster.Id, pattern.Id);
                    // Captured before damage/knockback mutate anything, and carried on the impact
                    // events: the tile flash must show this committed footprint, not a re-derivation
                    // at render time (see GetCommittedMonsterAttackFootprint).
                    var attackFlashFootprint = GetCommittedMonsterAttackFootprint(monster, pattern);
                    // 상태이상 지대(요괴 §4-3)는 공격이 <b>실제로 성립한</b> 이 자리에서 깔린다
                    // (방어막 저작과 같은 규약 — 예고만 띄우고 불발한 턴에는 아무것도 남지 않는다).
                    PlaceMonsterAttackStatusZones(monster, pattern);
                    // 소환(요괴 §4-4)도 같은 자리에서 — 예고만 띄우고 불발한 턴에는 아무도 나오지 않는다.
                    ResolveMonsterSummonPattern(monster, pattern);
                    // 예고(GetMonsterIntentPreviews)와 <b>같은 함수</b>를 쓴다 — R-8이 요구하는
                    // "예고 수치 = 실제 피해"는 테스트가 아니라 이 공유가 성립시킨다.
                    // §21.8 제안 6(다단 히트): pattern.Damage는 <b>히트당</b> 피해이고 HitCount회 반복된다
                    // (카드 쪽 F05 HitsPerTick과 같은 규약). 히트마다 별개의 Damage 이벤트가 hitIndex/hitCount를
                    // 달고 나가므로 표현(연타 플로팅·박자)은 기존 다단 배관이 그대로 소화한다.
                    var perHitDamage = pending.IncomingDamageNullifiedThisMonsterAction
                        ? 0
                        : ResolveMonsterAttackDamageToPlayer(monster, pattern);
                    var hitCount = Math.Max(1, pattern.HitCount);

                    var hpBefore = Player.Hp;
                    var blockBeforeAttack = Player.Block;
                    MonsterActionResolutionBuilder record = null;
                    if (actionRecords != null && actionRecords.TryGetValue(monster.Id, out record))
                    {
                        record.AttackedPlayer = true;
                        record.AttackOrder = attackOrder++;
                        record.AttackPatternId = pattern.Id;
                        record.AttackAnimationTrigger = pattern.AnimationTrigger;
                        record.PresentationGroupId = presentationGroupId;
                        // 자기부여 패턴은 플레이어를 건드리지 않는다 — 어셈블러가 암시야 "기습!" 판정에서
                        // 제외할 수 있도록 기록에 남긴다(AttackedPlayer는 가시 공격 애니 비트용으로 유지).
                        record.SelfTargetedAttack = pattern.IsSelfTargeted;
                        record.AimCoord = GetCommittedAimCoord(monster);
                    }

                    // 상태 부여·저주 삽입은 다단이어도 <b>공격당 1회</b>다(사용자 확정) — 히트당으로 돌리면
                    // 스택형 상태·카드 오염이 히트 수만큼 조용히 배가되어 수치 저작이 폭주한다.
                    ApplyMonsterAttackStatusEffects(monster, pattern, actionRecords, presentationGroupId);
                    ApplyMonsterAttackCurseInjection(monster, pattern);

                    var reflectedTotal = 0;
                    for (var hit = 0; hit < hitCount; hit++)
                    {
                        // Reflect (반사, 양날의 방패): 저작된 퍼센트만큼 경감하고 경감분을 공격자에게
                        // 되돌린다 — 히트마다 같은 산술이라 내림도 히트마다 일어난다(I-05: ÷2 하드코딩이
                        // 연마 75%를 무효로 만들던 것을 퍼센트 소비로 교정).
                        var reflected = ComputeReflectedDamage(perHitDamage);
                        var incomingToPlayer = perHitDamage - reflected;

                        var blockBeforeHit = Player.Block;
                        var applied = Player.ApplyDamage(incomingToPlayer);
                        reflectedTotal += reflected;
                        if (record != null)
                        {
                            record.AffectedPlayer |= applied > 0 || Player.Hp != hpBefore || Player.Block != blockBeforeAttack;
                            record.DamageToPlayer += applied;
                        }

                        if (applied > 0)
                        {
                            RaiseEffect(
                                EffectKind.Damage,
                                PlayerCoord,
                                pattern.AreaRadius,
                                applied,
                                PlayerUnitId,
                                ToMonsterPatternSourceRef(pattern),
                                sourceUnitId: monster.Id,
                                sourceActorKind: "monster",
                                targetActorKind: "player",
                                sourcePatternId: pattern.Id,
                                hitIndex: hit,
                                hitCount: hitCount,
                                presentationGroupId: presentationGroupId,
                                lethal: Player.IsDead,
                                delaySeconds: MultiHitBeatDelaySeconds(hit),
                                sourceCoord: monster.Coord,
                                areaCoords: attackFlashFootprint);
                        }
                        else if (perHitDamage > 0 && Player.Block < blockBeforeHit)
                        {
                            // Incoming hit fully absorbed by Block (HP unchanged, block consumed): surface "방어!".
                            // 다단에서는 히트마다 갈린다 — 앞 히트가 방어를 다 깎으면 뒤 히트부터 피해가 뚫는다.
                            RaiseEffect(
                                EffectKind.DamageBlocked,
                                PlayerCoord,
                                pattern.AreaRadius,
                                0,
                                PlayerUnitId,
                                ToMonsterPatternSourceRef(pattern),
                                sourceUnitId: monster.Id,
                                sourceActorKind: "monster",
                                targetActorKind: "player",
                                sourcePatternId: pattern.Id,
                                hitIndex: hit,
                                hitCount: hitCount,
                                presentationGroupId: presentationGroupId,
                                delaySeconds: MultiHitBeatDelaySeconds(hit),
                                sourceCoord: monster.Coord,
                                areaCoords: attackFlashFootprint);
                        }
                        // "무효화되지 않았다면 피해가 있었을 공격인가"를 같은 해소 함수로 되묻는다
                        // (강화·쇠약·허점까지 포함). 무효화는 공격 전체에 걸리므로 표식은 첫 히트 한 번이면 된다.
                        else if (pending.IncomingDamageNullifiedThisMonsterAction && hit == 0
                                 && ResolveMonsterAttackDamageToPlayer(monster, pattern) > 0)
                        {
                            // Incoming hit nullified by Protection Zone in Rain: the attack still presents, but deals 0 damage.
                            RaiseEffect(
                                EffectKind.DamageBlocked,
                                PlayerCoord,
                                pattern.AreaRadius,
                                0,
                                PlayerUnitId,
                                ToMonsterPatternSourceRef(pattern),
                                sourceUnitId: monster.Id,
                                sourceActorKind: "monster",
                                targetActorKind: "player",
                                sourcePatternId: pattern.Id,
                                hitIndex: 0,
                                hitCount: 1,
                                presentationGroupId: presentationGroupId,
                                sourceCoord: monster.Coord,
                                areaCoords: attackFlashFootprint);
                        }

                        if (Player.IsDead)
                        {
                            break;
                        }
                    }

                    // 소매치기(2026-09-04 §2-D · 야광귀 A051): 명중 시 엽전 절도. 보유가 모자라면 있는
                    // 만큼만(음수 금지) — 절도액은 개체에 쌓이고 처치해야만 돌아온다(aftermath.restore
                    // kind=Money). 히트 수와 무관하게 공격당 1회다(상태 부여·저주 삽입과 같은 규약).
                    if (pattern.StealMoneyAmount > 0 && !monster.Combatant.IsDead)
                    {
                        var stolen = Math.Min(pattern.StealMoneyAmount, PlayerInventory.Wallet.Balance);
                        if (stolen > 0 && PlayerInventory.Wallet.TrySpend(stolen, out _))
                        {
                            monster.StolenMoney += stolen;
                            RaiseMonsterTraitAnnouncement(monster, MonsterTraitAnnouncement.PickpocketRef, stolen);
                        }
                    }

                    // 0만 아니면 된다 — 음수는 끌어당김이고 부호 처리는 변위 함수가 맡는다(§16.1).
                    if (pattern.KnockbackDistance != 0 && !Player.IsDead)
                    {
                        var playerBeforeKnockback = PlayerCoord;
                        ApplyDirectionalKnockback(
                            monster.Coord,
                            PlayerUnitId,
                            pattern.KnockbackDistance,
                            pattern.KnockbackImpactDamage,
                            presentationGroupId,
                            monster.Id,
                            "monster",
                            "player");
                        if (record != null && PlayerCoord != playerBeforeKnockback)
                        {
                            record.AffectedPlayer = true;
                            record.KnockedBackPlayer = true;
                            record.PlayerKnockbackFrom = playerBeforeKnockback;
                            record.PlayerKnockbackTo = PlayerCoord;
                        }
                    }

                    // 반사는 히트마다 산출해 합산하고 한 번에 되돌린다 — 팝업 하나로 읽히고, 몬스터가
                    // 중간 히트의 반사로 죽어도 남은 히트가 플레이어에게 그대로 들어가는 순서와 일치한다.
                    if (reflectedTotal > 0)
                    {
                        DamageMonster(monster, reflectedTotal);
                        RaiseEffect(
                            EffectKind.ReflectDamage,
                            monster.Coord,
                            0,
                            reflectedTotal,
                            monster.Id,
                            CardEffectRefs.DefendHalfReflect,
                            sourceUnitId: PlayerUnitId,
                            sourceActorKind: "player",
                            targetActorKind: "monster",
                            sourcePatternId: pattern.Id,
                            hitIndex: 0,
                            hitCount: 1,
                            presentationGroupId: presentationGroupId);
                        if (monster.Combatant.IsDead)
                        {
                            ResetDeadMonsterToPatrolIntent(monster);
                            anyKilledByReflection = true;
                        }
                    }

                    // 고슴도치 인형(T2 페이즈 C): 인접 칸에서 명중한 공격에 반사 피해를 돌려준다.
                    // 명중이 트리거다 — 방어막·무적으로 피해가 0이어도 "피격"이고(StS Thorns 계약),
                    // 빗맞음(miss) 분기는 여기 오지 않는다.
                    var thorns = wasAdjacentAtImpact && !monster.Combatant.IsDead
                        ? GetAdjacentThornsReflectDamage()
                        : 0;
                    if (thorns > 0)
                    {
                        DamageMonster(monster, thorns);
                        RaiseEffect(
                            EffectKind.ReflectDamage,
                            monster.Coord,
                            0,
                            thorns,
                            monster.Id,
                            "relic.relic-hedgehog-doll",
                            sourceUnitId: PlayerUnitId,
                            sourceActorKind: "player",
                            targetActorKind: "monster",
                            sourcePatternId: pattern.Id,
                            hitIndex: 0,
                            hitCount: 1,
                            presentationGroupId: presentationGroupId);
                        if (monster.Combatant.IsDead)
                        {
                            ResetDeadMonsterToPatrolIntent(monster);
                            anyKilledByReflection = true;
                        }
                    }

                    // 「밀어붙이기」(요괴 §4-5): 때린 뒤 한 칸 다가선다. 반사·가시로 죽었으면 움직이지 않는다.
                    ApplyAdvanceAfterAttack(monster, record);

                    // 은신(요괴 §4-1): 노출은 <b>해소 시점</b>부터다(Q1 확정 — 예고 시점이 아니다).
                    //
                    // 🔴 <b>「들킴!」을 이 공격의 연출 그룹에 실어 보낸다</b>(2026-09-05 사용자 요구:
                    // "공격한 직후, 받은 직후에 이루어져야함"). 그룹이 비어 있으면 어셈블러가
                    // <c>AppendUnscheduledTurnStartEffects</c>로 흘려보내, 몬스터 페이즈가 <b>전부 끝난
                    // 뒤</b> 턴 경계 이펙트들과 함께 떴다 — 규칙은 이미 맞는 순간에 서 있었고 늦은 것은
                    // 화면뿐이었다. 그룹을 달면 공격 임팩트 비트에 붙는다(플레이어가 때려서 드러나는
                    // 갈래는 <c>BuildPlayerAttack</c>이 버퍼를 통째로 재생하므로 원래부터 제자리다).
                    RevealStealthMonsterAfterAttack(monster, presentationGroupId);

                    // 「달아나기」(야광귀): 패턴 저작(selfTeleportRadius) 자리 이동. 노출 처리 뒤에 둔다 —
                    // 「드러나고 → 옮긴다」 순서가 계약이다(구 A039 은신 흩어지기도 이 자리를 지났다).
                    //
                    // 🔴 <b>걸어서 달아난다</b>(2026-09-05 사용자 요구: "순간 이동이 아니라 도망가는 지점까지
                    // 이동 애니메이션, SFX가 재생되도록"). 규칙은 그대로 결정적 자리 선정이고, 바뀐 것은
                    // <b>연출 채널</b>이다 — 밀어붙이기 전진이 쓰는 「공격 뒤 자리 이동」 칸에 실어 보내면
                    // 어셈블러가 공격 비트 <b>직후</b>에 EnemyMoveStep을 내고, 걸음 연출과 이동 소리가
                    // 기존 배관 그대로 붙는다. 한 몬스터가 전진과 달아나기를 <b>둘 다</b> 저작하는 일은
                    // 없으므로(밀어붙이기=두억시니 · 달아나기=야광귀) 같은 칸을 나눠 쓰는 것이 안전하다.
                    if (ResolvePatternSelfTeleport(monster, pattern, out var escapeFrom, out var escapeTo)
                        && record != null)
                    {
                        record.AdvancedFrom = escapeFrom;
                        record.AdvancedTo = escapeTo;
                    }

                    deferredMonsterActionState?.RecordAttackSnapshot(
                        presentationGroupId,
                        CaptureDeferredMonsterActionSnapshot());
                }

                monster.PendingAttackIntent = false;
            }

            if (anyKilledByReflection)
            {
                UpdateOccupancy();
            }
        }

        /// <summary>
        /// 자기부여 패턴의 상태를 몬스터 자신에게 건다.
        /// <para>플레이어 경로와 다른 점 둘: ①플레이어 사망 여부와 무관하다(대상이 자신이므로),
        /// ②<c>IsPlayerImmuneToControlStatus</c>를 보지 않는다 — 그 면역은 플레이어가 하드CC에
        /// 잠기는 것을 막는 장치이고 몬스터 자기버프와는 무관하다.</para>
        /// <para>⚠️ 지속시간은 패턴의 <c>statusEffectDurationTurns</c>를 그대로 쓴다. 0으로 저작하면
        /// 즉시 만료돼 아무 일도 일어나지 않는다.</para>
        /// </summary>
        private void ApplyMonsterSelfStatusEffects(MonsterRuntime monster, MonsterAttackPattern pattern)
        {
            foreach (var kind in pattern.StatusEffects.Distinct())
            {
                var statusAmount = StatusEffectInfo.DefaultAmount(kind);
                AddDurationStatusEffect(
                    kind,
                    monster.Id,
                    pattern.StatusEffectDurationTurns,
                    statusAmount,
                    monster.Id);
                RaiseStatusEffect(
                    kind,
                    monster.Coord,
                    0,
                    statusAmount,
                    monster.Id,
                    ToMonsterPatternSourceRef(pattern),
                    sourceUnitId: monster.Id);
            }
        }

        private void ApplyMonsterAttackStatusEffects(
            MonsterRuntime monster,
            MonsterAttackPattern pattern,
            IReadOnlyDictionary<string, MonsterActionResolutionBuilder> actionRecords,
            string presentationGroupId)
        {
            if (monster == null || pattern.StatusEffects == null || pattern.StatusEffects.Count == 0)
            {
                return;
            }

            // 자기부여 패턴(targeting=self, D-4): 상태를 플레이어가 아니라 <b>자기 자신</b>에게 건다.
            // 미지처럼 몬스터가 스스로 두르는 버프를 저작으로 만들 수 있는 유일한 길이다 —
            // 이 갈래가 생기기 전에는 statusEffects 열이 언제나 플레이어를 향했다.
            if (pattern.IsSelfTargeted)
            {
                ApplyMonsterSelfStatusEffects(monster, pattern);
                return;
            }

            if (Player.IsDead)
            {
                return;
            }

            MonsterActionResolutionBuilder record = null;
            actionRecords?.TryGetValue(monster.Id, out record);

            foreach (var kind in pattern.StatusEffects.Distinct())
            {
                if (!IsPersistentMonsterAttackStatusEffect(kind))
                {
                    continue;
                }

                // Player is briefly immune to re-application of this hard control status (anti stun-lock):
                // skip both the apply and its status VFX so the HUD doesn't falsely show it landing.
                if (IsPlayerImmuneToControlStatus(kind))
                {
                    continue;
                }

                var statusAmount = StatusEffectInfo.DefaultAmount(kind);
                if (!AddDurationStatusEffect(
                    kind,
                    PlayerUnitId,
                    pattern.StatusEffectDurationTurns,
                    statusAmount,
                    monster.Id))
                {
                    // 수호(T2 페이즈 C)가 무효화했다 — 부여 VFX까지 올리면 "걸리지도 않은 상태"가 HUD에 뜬다.
                    continue;
                }

                if (record != null)
                {
                    record.AffectedPlayer = true;
                }
                RaiseStatusEffect(
                    kind,
                    PlayerCoord,
                    pattern.AreaRadius,
                    statusAmount,
                    PlayerUnitId,
                    ToMonsterPatternSourceRef(pattern),
                    sourceUnitId: monster.Id,
                    sourceActorKind: "monster",
                    targetActorKind: "player",
                    sourcePatternId: pattern.Id,
                    hitIndex: 0,
                    hitCount: 1,
                    presentationGroupId: presentationGroupId);
            }
        }

        private static string ToMonsterPatternSourceRef(MonsterAttackPattern pattern)
        {
            return string.IsNullOrWhiteSpace(pattern.Id)
                ? "monster.pattern"
                : $"monster.pattern.{pattern.Id}";
        }

        /// <summary>
        /// 해코지(A027, T2): 공격이 플레이어에게 닿는 턴에 저주 카드를 덱에 섞는다(StS Painful Stabs 문법).
        /// 자기부여 패턴은 대상이 플레이어가 아니므로 제외. 저작 표면은 monster_attack_patterns.csv의
        /// injectStatusCardId(항상 그 카드) 또는 injectStatusCardPool(이 중 하나) 컬럼이며, 임포트가
        /// 둘 중 하나만 허용한다.
        ///
        /// <para>풀 추첨은 <see cref="MonsterCurseCardPool"/>의 순수 함수다 — 몬스터 id·패턴 id·전체 턴
        /// 번호만 섞으므로 전투 중 저장·복원 뒤에도 같은 카드가 나온다(무시드 <c>pushRng</c>를 쓰면
        /// 그 재현이 깨진다).</para>
        /// </summary>
        private void ApplyMonsterAttackCurseInjection(MonsterRuntime monster, MonsterAttackPattern pattern)
        {
            if (pattern.IsSelfTargeted)
            {
                return;
            }

            if (pattern.HasInjectStatusCardPool)
            {
                var seed = MonsterCurseCardPool.MixSeed(
                    $"{monster?.Id}|{pattern.Id}", OverallTurnNumber);
                TryInjectStatusCard(MonsterCurseCardPool.Pick(pattern.InjectStatusCardPool, seed));
                return;
            }

            if (string.IsNullOrWhiteSpace(pattern.InjectStatusCardId))
            {
                return;
            }

            TryInjectStatusCard(pattern.InjectStatusCardId);
        }

        private static bool IsPersistentMonsterAttackStatusEffect(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Immobilize:
                case StatusEffectKind.Agility:
                case StatusEffectKind.Poison:
                case StatusEffectKind.Stun:
                case StatusEffectKind.Slow:
                case StatusEffectKind.Rupture:
                case StatusEffectKind.Reflect:
                // T1(2026-08-06): 꾸중(A025)=쇠약 · 홀리기(A026)=봉인.
                case StatusEffectKind.Weaken:
                case StatusEffectKind.Seal:
                    return true;
                default:
                    return false;
            }
        }

        private bool CanPlannedMonsterActionAttack(MonsterRuntime monster)
        {
            if (monster == null || monster.Combatant.IsDead || Player.IsDead)
            {
                return false;
            }

            if (monster.ActivityState == MonsterActivityState.Dormant ||
                IsMonsterAttackBlocked(monster) ||
                !monster.TurnPlan.IsActive ||
                !monster.TurnPlan.CanAttack)
            {
                return false;
            }

            return IsActiveMonsterAttackAreaCoveringPlayer(monster, monster.Coord);
        }

        // True when an immobilized (이동만 막힌) monster still intends to attack from where it stands — used to
        // telegraph a stationary attack motion even when its area does not currently cover the player.
        private bool ShouldCastStationaryAttack(MonsterRuntime monster)
        {
            if (monster == null
                || monster.Combatant.IsDead
                || monster.ActivityState == MonsterActivityState.Dormant
                || !IsMonsterMovementBlocked(monster)
                || IsMonsterAttackBlocked(monster)
                || !monster.TurnPlan.IsActive)
            {
                return false;
            }

            return monster.TurnPlan.CanAttack;
        }

        private bool ShouldPresentMissedMonsterAttack(MonsterRuntime monster)
        {
            if (monster == null
                || monster.Combatant.IsDead
                || monster.ActivityState == MonsterActivityState.Dormant
                || IsMonsterAttackBlocked(monster)
                || !monster.TurnPlan.IsActive
                || !monster.TurnPlan.CanAttack)
            {
                return false;
            }

            // PendingAttackIntent means the monster had the player in its committed attack area at movement
            // end; if a preceding monster knocks the player away before this beat, the attack should visibly
            // whiff. AnyAttackPatternCoversPlayerFrom catches the cooldown case: the player IS within reach of
            // some pattern, but the only covering one is on cooldown, so the committed off-cooldown pattern
            // whiffs instead of resting. The stationary fallback preserves the immobilized telegraph behavior.
            return monster.PendingAttackIntent
                || AnyAttackPatternCoversPlayerFrom(monster, monster.Coord)
                || ShouldCastStationaryAttack(monster);
        }

        private bool IsActiveMonsterAttackAreaCoveringPlayer(MonsterRuntime monster, HexCoord origin)
        {
            return planner.IsActiveMonsterAttackAreaCoveringPlayer(monster, origin);
        }

        private bool AnyAttackPatternCoversPlayerFrom(MonsterRuntime monster, HexCoord origin)
        {
            return planner.AnyAttackPatternCoversPlayerFrom(monster, origin);
        }

        /// <summary>
        /// The COMMITTED attack footprint for a monster attack (or whiff) presentation event,
        /// captured at raise time. Shaped patterns face toward the facing intent locked at movement
        /// end — exactly like <see cref="IsActiveMonsterAttackAreaCoveringPlayer"/> — never toward
        /// the player's live position: the player may have been knocked/stepped elsewhere between
        /// commit and this beat, and by the time presentation renders, the next turn's intent may
        /// already be computed. Plain patterns cover the pattern-range disk from the attacker.
        /// </summary>
        /// <summary>
        /// 예고(커밋) 시점에 겨눈 칸(2026-08-20 #7). 형상 footprint가 쓰는
        /// <see cref="MonsterTurnPlan.AttackFacingIntent"/>와 <b>같은 출처</b>다 — 연출의 모델 회전이
        /// 이걸 봐야 화면과 판정이 같은 방향을 가리킨다. 플레이어가 예고 후 비켜서도 몸은 예고한
        /// 쪽을 향한 채 공격 애니를 재생한다.
        /// </summary>
        private static HexCoord GetCommittedAimCoord(MonsterRuntime monster)
        {
            var facingIntent = monster.TurnPlan.IsActive ? monster.TurnPlan.AttackFacingIntent : monster.LockedFacingIntent;
            return facingIntent.PlayerCoord;
        }

        private IReadOnlyList<HexCoord> GetCommittedMonsterAttackFootprint(MonsterRuntime monster, MonsterAttackPattern pattern)
        {
            // 자기부여 패턴은 타일을 때리지 않는다 — 플래시 footprint가 비어야 화면도 침묵한다
            // (연출은 상태 부여 이벤트(RaiseStatusEffect)가 담당한다).
            if (pattern.IsSelfTargeted)
            {
                return Array.Empty<HexCoord>();
            }

            var origin = monster.Coord;
            var body = GetMonsterBody(monster);
            if (!string.IsNullOrEmpty(pattern.ShapeId))
            {
                var facingIntent = monster.TurnPlan.IsActive ? monster.TurnPlan.AttackFacingIntent : monster.LockedFacingIntent;
                var attackDir = origin.ApproximateDirection(facingIntent.PlayerCoord);
                return AttackShapeLibrary.GetAffectedCells(pattern.ShapeId, origin, attackDir, body)
                    .Where(coord => Map.TryGetCell(coord, out _))
                    .ToList();
            }

            // 비-shape 사거리는 몸통 가장자리 기준(§13.4 C-3): 원판은 range + 반경, 형상은 몸통 칸별 최솟값.
            return GetAttackRangeCoords(origin, pattern.Range, body);
        }

        /// <summary>
        /// 상태이상 지대(요괴 §4-3 · 두억시니)를 형상 안 저작 칸에 깐다. 회전·몸 반경 보정은
        /// <see cref="GetCommittedMonsterAttackFootprint"/>와 <b>같은 값</b>을 쓴다 — 예고가 그린 칸과
        /// 장판이 생기는 칸이 갈라지면 "예고=명중"이 무너진다.
        ///
        /// <para>지대는 <b>플레이어만</b> 때린다(핸들러가 몬스터를 아예 보지 않는다). 그래서 이 함수는
        /// 몬스터의 길찾기·배치·도달성 어디에도 개입하지 않는다 — 통행 불가 갈래를 폐기하고 지대로 온
        /// 이유가 그것이다.</para>
        /// </summary>
        private void PlaceMonsterAttackStatusZones(MonsterRuntime monster, MonsterAttackPattern pattern)
        {
            if (monster == null || !pattern.HasStatusZone || string.IsNullOrEmpty(pattern.ShapeId))
            {
                return;
            }

            var origin = monster.Coord;
            var bodyRadius = GetMonsterFootprintRadius(monster);
            var facingIntent = monster.TurnPlan.IsActive ? monster.TurnPlan.AttackFacingIntent : monster.LockedFacingIntent;
            var attackDir = origin.ApproximateDirection(facingIntent.PlayerCoord);
            for (var i = 0; i < bodyRadius; i++)
            {
                origin = origin.Neighbor(attackDir);
            }

            // RotateSteps는 시계 방향, 방향 enum은 반시계라 (6-d)%6 — AttackShapeLibrary와 같은 식이다.
            var rotationSteps = (6 - (int)attackDir) % 6;
            List<HexCoord> placedCoords = null;
            foreach (var offset in pattern.ZoneOffsets)
            {
                var coord = origin + offset.RotateSteps(rotationSteps);
                if (!IsStatusZoneGround(coord))
                {
                    continue;
                }

                // 같은 칸에 이미 같은 지대가 있으면 갱신이 아니라 교체한다 — 겹쳐 쌓으면 한 칸이
                // 여러 번 부여를 굴려 저작 수치가 조용히 배가된다.
                FieldObjects.RemoveAll(existing =>
                    existing.Kind == FieldObjectKind.StatusZone
                    && existing.Position == coord
                    && existing.StatusKind == pattern.ZoneStatusKind);

                var zone = new FieldObject(
                    coord,
                    0,
                    pattern.ZoneDurationTurns,
                    FieldObjectKind.StatusZone,
                    0,
                    sourceUnitId: monster.Id,
                    visualRef: MonsterStatusZone.SourceRef,
                    hitsPerTick: 1,
                    statusKind: pattern.ZoneStatusKind);
                FieldObjects.Add(zone);
                placedCoords ??= new List<HexCoord>();
                placedCoords.Add(coord);
            }

            // 🔴 연출은 칸마다가 아니라 <b>한 번</b> 올린다(2026-09-05 사용자 요구: "지대가 하나씩
            // 순차적으로 재생되는 게 아니라 모든 지대에 일괄적으로"). 종전에는 이 루프 안에서
            // 칸 수만큼 배치 VFX를 올려 7칸 지대가 일곱 박자로 터졌다. 칸 목록은 areaCoords로
            // 실어 보내므로 표현은 한 큐로 전 칸을 동시에 그린다.
            if (placedCoords != null)
            {
                // 그림(배치 VFX)과 말(「{상태} 지대 생성!」)이 <b>같은 연출 그룹</b>을 쓴다 — 그룹 id는
                // 부를 때마다 증가하므로(NextGroupId) 한 번 만들어 나눠 준다. 따로 만들면 두 신호가
                // 서로 다른 비트로 흩어져 "지대가 생기는 순간"이 두 번으로 갈린다.
                var zoneGroupId = CreatePresentationGroupId(monster.Id, pattern.Id + ".zone");
                RaiseStatusZonePlacementVfx(monster, pattern, placedCoords, zoneGroupId);
                RaiseStatusZoneCreatedAnnouncement(monster, pattern, zoneGroupId);
                // 플레이어가 서 있는 칸에 깔렸으면 <b>지금</b> 걸린다 — 좌표는 안 바뀌었으니
                // 세터 관문이 돌지 않는다(실시간 판정의 두 번째 진입점).
                RefreshStandingStatusZoneEffects();
            }
        }

        /// <summary>
        /// 「{상태} 지대 생성!」 알림 한 줄(2026-09-05 사용자 요구: "두억시니가 지대 기믹을 실행할 때
        /// 플로팅 텍스트가 표시되어야함").
        ///
        /// <para>🔴 <b>배치 VFX 이벤트에 문안을 실을 수 없다.</b> 그 이벤트는 <c>targetUnitId="field"</c>·
        /// 수치 0이라 표현층의 「칸에 그림만 얹는 배치 큐」 게이트(<c>ShouldShowFloatingText</c>)에 걸려
        /// 통째로 침묵한다 — 폭탄·섬광 장판이 숫자를 안 띄우는 것과 같은 자리다. 그래서 <b>몬스터를
        /// 겨눈</b> 특성 알림으로 따로 낸다: 그림은 배치 큐가, 말은 알림 채널이 맡는다.</para>
        ///
        /// <para>🔑 연출 그룹은 배치 VFX와 <b>같은 것</b>을 쓴다 — 그래야 어셈블러가 이 공격의 비트에
        /// 붙여 내고, 턴 경계로 밀려나 「다 끝난 뒤에 뜨는 글자」가 되지 않는다.</para>
        /// </summary>
        private void RaiseStatusZoneCreatedAnnouncement(
            MonsterRuntime monster, MonsterAttackPattern pattern, string presentationGroupId)
        {
            if (monster == null || !IsMonsterCoordVisible(monster))
            {
                return;
            }

            RaiseEffect(
                EffectKind.MonsterTraitTriggered,
                monster.Coord,
                0,
                pattern.ZoneDurationTurns,
                monster.Id,
                MonsterTraitAnnouncement.StatusZoneCreatedRef,
                sourceUnitId: monster.Id,
                sourceActorKind: "monster",
                targetActorKind: "monster",
                sourcePatternId: pattern.Id,
                presentationGroupId: presentationGroupId,
                statusKind: pattern.ZoneStatusKind);
        }

        /// <summary>
        /// 이 칸에 지대를 깔 수 있는가 — <b>밟을 수 있는 땅</b>만 부순다(2026-09-05 사용자 요구:
        /// "물 타일에는 발동되지 않도록"). 지대는 「그 칸에 서 있으면 받는 효과」라, 애초에 설 수 없는
        /// 칸에 깔리면 아무 일도 일어나지 않는 그림만 남는다.
        ///
        /// <para>🔑 통행 판정과 <b>같은 술어</b>를 쓴다(<c>BaseWalkable</c> + 지형 통행) — 다른 술어를
        /// 쓰면 "설 수 있는데 지대가 없는 칸"이나 그 반대가 조용히 생긴다.</para>
        /// </summary>
        private bool IsStatusZoneGround(HexCoord coord)
        {
            return Map.TryGetCell(coord, out var cell)
                && cell.BaseWalkable
                && terrainTraits.IsWalkable(cell.TerrainTypeId);
        }

        /// <summary>
        /// 지대 배치 연출 한 큐. 전 칸을 <c>areaCoords</c>로 실어 <b>동시에</b> 그리게 한다 —
        /// 칸마다 이벤트를 올리면 표현이 순차 재생으로 읽힌다.
        /// </summary>
        private void RaiseStatusZonePlacementVfx(
            MonsterRuntime monster,
            MonsterAttackPattern pattern,
            IReadOnlyList<HexCoord> coords,
            string presentationGroupId)
        {
            // 🔴 sourceRef는 <b>패턴 ref</b>다(MonsterStatusZone.SourceRef가 아니라). VFX 카탈로그는
            // 저작 큐를 monster_pattern_vfx_bindings.csv의 patternId로만 실어 오므로(임포터가
            // sourceRef를 monster.pattern.{id}로 덮어쓴다), 다른 ref로 쏘면 어떤 저작 큐도 안 걸리고
            // 범용 폴백만 뜬다 — 그게 "발동한 건지 아닌지 구분이 안 간다"의 절반이었다.
            // 지대라는 사실은 targetActorKind="field"와 statusKind가 말한다.
            RaiseEffect(
                EffectKind.StatusEffectApplied,
                coords[0],
                0,
                0,
                "field",
                ToMonsterPatternSourceRef(pattern),
                sourceUnitId: monster.Id,
                sourceActorKind: "monster",
                targetActorKind: "field",
                sourcePatternId: pattern.Id,
                presentationGroupId: presentationGroupId,
                statusKind: pattern.ZoneStatusKind,
                sourceCoord: monster.Coord,
                areaCoords: coords);
        }

        /// <summary>
        /// 「밀어붙이기」(요괴 §4-5 · 두억시니): 공격을 마치면 플레이어 쪽으로 1칸 전진한다.
        ///
        /// <para>🔴 가드 셋 — ①플레이어가 이미 인접이면 아무것도 하지 않는다(플레이어 칸으로 밀고 들어가
        /// 서로 겹치는 상태를 만들지 않는다) ②전진 칸이 막혔으면 하지 않는다(이동과 같은 술어
        /// <c>HexPathfinder.CanEnter</c>를 쓴다 — 다른 술어를 쓰면 이동이 못 가는 칸에 전진으로 들어간다)
        /// ③몸이 묶였거나(속박·기절·섬광 장판) 죽었으면 하지 않는다.</para>
        /// </summary>
        /// <summary>
        /// 이 몬스터가 <paramref name="from"/>에서 공격한 뒤 전진한다면 어느 칸인가 — <b>예고와 집행의
        /// 단일 술어</b>(2026-09-01 #19).
        ///
        /// <para>🔴 예고가 이 함수를 안 지나면 「예고=명중」의 전진판이 깨진다: 화면은 한 칸을 그리고
        /// 규칙은 다른 칸으로 간다. 예고는 <b>계획된 이동 도착지</b>를, 집행은 <b>실제 현재 칸</b>을
        /// 넣어 같은 답을 받는다.</para>
        ///
        /// <para>가드 셋 — ①플레이어가 이미 인접이면 하지 않는다(플레이어 칸으로 밀고 들어가 겹치지
        /// 않게) ②전진 칸이 막혔으면 하지 않는다(이동과 같은 술어 <c>HexPathfinder.CanEnter</c> —
        /// 다른 술어를 쓰면 이동이 못 가는 칸에 전진으로 들어간다) ③몸이 묶였거나 죽었으면 하지 않는다.</para>
        /// </summary>
        private bool TryResolveAdvanceAfterAttack(MonsterRuntime monster, HexCoord from, out HexCoord next)
        {
            next = from;
            if (monster == null
                || monster.Combatant.IsDead
                || Player.IsDead
                || !TryGetEnemyGrammarEntry(monster, out var entry)
                || !entry.HasAdvanceAfterAttack
                || IsMonsterMovementRooted(monster))
            {
                return false;
            }

            // 🔴 「이미 인접이면 전진하지 않는다」는 <b>몸</b>에서 재야 한다(2026-09-05). 앵커 좌표로 재면
            // 3칸 몸이 이미 플레이어에 닿아 있는데 앵커는 2칸이라 전진이 돌고(몸이 플레이어를 파고든다),
            // 반대로 앵커는 1칸인데 몸은 닿지 않은 자세에서 전진이 막힌다. 밀칠 자리 판정과 통행 판정은
            // 이미 footprint를 보고 있었으므로, 이 가드만 앵커 기준으로 남아 축이 갈라져 있었다.
            var body = GetMonsterBody(monster);
            if (body.DistanceFrom(from, PlayerCoord) <= 1)
            {
                return false;
            }

            // 🔴 3칸 몸은 <b>세 칸이 동시에</b> 들어갈 자리를 찾아야 한다 — 정면 한 칸만 보면 몸통 한 칸이
            // 물·건물에 걸릴 때마다 전진이 통째로 무산된다("밀어붙이기가 발동을 안 한다"의 정체).
            // 그래서 정면이 막히면 좌우 한 걸음(±1)까지 본다. 한 칸 몸은 후보가 정면 하나뿐이라
            // 종전과 완전히 같은 답이 나온다(이 완화는 다칸 몸에서만 열린다).
            var forward = from.ApproximateDirection(PlayerCoord);
            var directions = body.IsMultiCell
                ? new[] { forward, RotateDirection(forward, 1), RotateDirection(forward, -1) }
                : new[] { forward };
            foreach (var direction in directions)
            {
                var candidate = from.Neighbor(direction);
                if (candidate == PlayerCoord
                    // 전진은 <b>멀어지면 안 된다</b>. 옆걸음은 거리가 같을 수 있는데(막힌 정면을 우회하는 자세),
                    // 그건 허용한다 — 금지해야 할 것은 뒤로 물러나는 「전진」뿐이다.
                    || body.DistanceFrom(candidate, PlayerCoord) > body.DistanceFrom(from, PlayerCoord)
                    || !HexPathfinder.CanEnter(
                        Map,
                        from,
                        candidate,
                        new MovementQuery(from, 1, unitId: monster.Id, footprintRadius: GetMonsterFootprintRadius(monster), footprintOffsets: GetMonsterFootprintOffsets(monster)),
                        runtimeStates,
                        terrainTraits,
                        out _))
                {
                    continue;
                }

                next = candidate;
                return true;
            }

            return false;
        }

        /// <summary>육각 방향을 반시계 <paramref name="steps"/>칸 돌린다(음수 = 시계).</summary>
        private static HexDirection RotateDirection(HexDirection direction, int steps)
        {
            return (HexDirection)(((int)direction + steps % 6 + 6) % 6);
        }

        /// <summary>예고용 — 계획된 이동 도착지에서 전진할 칸(없으면 null). 집행과 같은 술어를 쓴다.
        /// 밀침 갈래(2026-09-04)도 그린다: 플레이어를 밀쳐낼 수 있는 자리면 전진 칸이 곧 플레이어 칸이다.</summary>
        private HexCoord? PredictAdvanceAfterAttack(MonsterRuntime monster, HexCoord plannedMove)
        {
            if (TryResolveAdvanceAfterAttack(monster, plannedMove, out var next))
            {
                return next;
            }

            if (TryResolveAdvancePushCandidate(monster, plannedMove, out var candidate)
                && CanDisplacePlayerOneStep(plannedMove))
            {
                return candidate;
            }

            return null;
        }

        /// <summary>밀침 충돌 피해(잠정 — 랩 튜닝 대상). A036 씨름 걸기의 「충돌 2」와 같은 무게.</summary>
        private const int AdvancePushImpactDamage = 2;

        private void ApplyAdvanceAfterAttack(MonsterRuntime monster, MonsterActionResolutionBuilder record)
        {
            var from = monster.Coord;
            if (!TryResolveAdvanceAfterAttack(monster, from, out var next))
            {
                // 밀침 갈래(2026-09-04 §2-A): 전진 자리를 막은 것이 <b>플레이어</b>면 전진 방향으로
                // 1칸 밀쳐내고 그 칸을 차지한다(충돌 2). 벽·물·기물 충돌 규칙은 기존 넉백 규약
                // 그대로다 — 밀리지 못하면(벽에 낑김) 충돌 피해만 남고 전진은 무산된다.
                // 밀린 자리가 A041 파열 지대와 겹치는 연계가 설계 의도다.
                if (!TryResolveAdvancePushCandidate(monster, from, out _))
                {
                    return;
                }

                var playerBefore = PlayerCoord;
                ApplyDirectionalKnockback(
                    from,
                    PlayerUnitId,
                    1,
                    AdvancePushImpactDamage,
                    sourceUnitId: monster.Id,
                    sourceActorKind: "monster",
                    targetActorKind: "player");
                if (record != null && PlayerCoord != playerBefore)
                {
                    record.AffectedPlayer = true;
                    record.KnockedBackPlayer = true;
                    record.PlayerKnockbackFrom = playerBefore;
                    record.PlayerKnockbackTo = PlayerCoord;
                }

                // 자리가 비었으면 이제 일반 전진과 같은 술어로 들어간다 — 두 벌 금지.
                if (!TryResolveAdvanceAfterAttack(monster, from, out next))
                {
                    return;
                }
            }

            monster.Coord = next;
            UpdateOccupancy();
            if (record != null)
            {
                record.AdvancedFrom = from;
                record.AdvancedTo = next;
            }
        }

        /// <summary>
        /// 전진을 막은 것이 플레이어인가 — 전진 후보 자리에 몸(footprint)을 놓으면 플레이어 칸과
        /// 겹치는 경우다(tri 몸은 앵커가 아닌 몸통 칸이 겹칠 수도 있다). 예고와 집행이 같은 술어를 쓴다.
        /// </summary>
        private bool TryResolveAdvancePushCandidate(MonsterRuntime monster, HexCoord from, out HexCoord candidate)
        {
            candidate = from;
            if (monster == null
                || monster.Combatant.IsDead
                || Player.IsDead
                || !TryGetEnemyGrammarEntry(monster, out var entry)
                || !entry.HasAdvanceAfterAttack
                || IsMonsterMovementRooted(monster))
            {
                return false;
            }

            candidate = from.Neighbor(from.ApproximateDirection(PlayerCoord));
            foreach (var offset in GetMonsterFootprintOffsets(monster))
            {
                if (candidate + offset == PlayerCoord)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 플레이어가 전진 방향으로 한 칸 밀릴 수 있는가 — 넉백 한 걸음과 <b>같은 술어</b>
        /// (<see cref="IsKnockbackStepOpen"/>)를 본다. 예고 전용이다(집행은 변위 함수가 직접 걷는다).
        /// </summary>
        private bool CanDisplacePlayerOneStep(HexCoord from)
        {
            var direction = from.ApproximateDirection(PlayerCoord);
            return IsKnockbackStepOpen(PlayerCoord + HexCoord.Directions[(int)direction]);
        }

        private IReadOnlyList<HexCoord> GetAttackRangeCoords(HexCoord origin, int range, MonsterBodyShape body)
        {
            var clampedRange = Math.Max(0, range);
            return Map.AllCells
                .Where(cell => body.DistanceFrom(origin, cell.Coord) <= clampedRange)
                .Select(cell => cell.Coord)
                .OrderBy(coord => coord.DistanceTo(origin))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .ToList();
        }

        private IEnumerable<MonsterRuntime> GetMonsterActionOrder()
        {
            return monsters
                .Where(monster => !monster.Combatant.IsDead)
                // 공격속도가 높을수록 먼저 행동(선공)한다. attackSpeed 값↑ = 빠름.
                .OrderByDescending(monster => monster.AttackSpeed)
                .ThenByDescending(monster => monster.TurnPlan.IsActive && monster.TurnPlan.CanAttack)
                .ThenBy(monster => monster.Coord.DistanceTo(PlayerCoord))
                .ThenBy(monster => monster.Coord.Q)
                .ThenBy(monster => monster.Coord.R)
                .ThenBy(monster => monster.Id);
        }

        private MonsterRuntime GetRepresentativeLivingMonster()
        {
            return monsters.FirstOrDefault(monster => !monster.Combatant.IsDead);
        }

        public bool TriggerMonsterAlert(HexCoord monsterCoord, int rangeBonus, int durationTurns)
        {
            var monster = FindLivingMonsterAt(monsterCoord);
            if (monster == null)
            {
                return false;
            }

            planner.ApplyAlert(monster, rangeBonus, durationTurns);
            return true;
        }

        private void RefreshMonsterTurnPlan(MonsterRuntime monster, ISet<HexCoord> reservedDestinations = null)
        {
            planner.RefreshTurnPlan(monster, reservedDestinations);
        }

        /// <summary>
        /// 이 몬스터의 이 공격 패턴이 플레이어에게 실제로 넣을 피해. <b>예고와 집행이 공유하는 단일 지점</b>이다 —
        /// R-8("예고 수치와 실제 피해 일치")을 테스트로 뒤쫓는 대신 구조로 성립시킨다. 강화와 쇠약은 같은
        /// 배율 축에서 부호만 반대로 합산되고, 허점은 배율이 아니라 <b>타격당 고정치</b>라(D-10) 곱한 뒤에 더한다.
        ///
        /// 반사(반사는 맞은 뒤 되돌리는 것)와 <c>PendingEffects.IncomingDamageNullifiedThisMonsterAction</c>(그 행동 창 한정 무효화)은
        /// 여기 들어오지 않는다 — 둘 다 "이 공격이 얼마짜리인가"가 아니라 그 뒤에 일어나는 일이다.
        /// </summary>
        internal int ResolveMonsterAttackDamageToPlayer(MonsterRuntime monster, MonsterAttackPattern pattern)
        {
            // 턴별 피해 변주(DEC-2026-08-19-02): 의도 잠금 시 계획부가 굴려 둔 저장값을 더한다 —
            // 여기서 다시 굴리면 예고와 집행이 갈라진다(굴림은 CommitAttackPattern에만 있다).
            // HasDamageJitter 가드는 구세이브(오프셋 0)·변주 없는 패턴에의 낡은 오프셋 누출 백스톱.
            var rolledDamage = System.Math.Max(0, pattern.Damage
                + (pattern.HasDamageJitter ? monster.AttackDamageRollOffset : 0));
            var baseDamage = System.Math.Max(0, rolledDamage - GetPlayerTerrainDefenseBonus());
            // 강화(Strength): 1점당 나가는 피해 +1%. D02 비 보호구역이 ApplyStrengthToAllMonsters로 +100%를 준다.
            // 보스 페이즈 강화는 만료되지 않는 고유 스탯이라 상태이상이 아니라 페이즈 트랙에서 직접 읽는다(보스가 아니면 0).
            // Math.Max(0, …)은 쇠약 100% 초과 저작에 대한 백스톱(현행 30%에서는 발동하지 않는다).
            var outgoingPercent = System.Math.Max(0, 100
                + SumActiveEffectAmountByValueMode(monster.Id, StatusEffectValueMode.DamageDealtBonusPercent)
                - SumActiveEffectAmountByValueMode(monster.Id, StatusEffectValueMode.DamageDealtPenaltyPercent)
                + GetBossPhaseStrengthBonusPercent(monster.Id)
                // 엘리트 강화(#12): 저작 배율을 % 축에 그대로 얹는다(강화·쇠약과 같은 축이라
                // 산식이 하나로 유지된다). role만 보므로 랜덤화 엘리트와 손저작 엘리트가 같다.
                + GetEliteDamageBonusPercent(monster));
            return System.Math.Max(0, baseDamage * outgoingPercent / 100
                + GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.IncomingDamageDelta)
                // 원귀(X12)의 flat +1은 제거됐다(2026-08-20 #18) — 「손에 있는 동안 허점 1턴 부여」로
                // 바뀌어 이제 허점(IncomingDamageBonusFlat) 축 하나로만 들어온다. 두 축을 함께 두면
                // 같은 저주가 두 번 세진다.
                + SumActivePlayerEffectAmountByValueMode(StatusEffectValueMode.IncomingDamageBonusFlat)
                // 힘(2026-09-04): 약오름·담력 시험이 부여하는 <b>상태이상</b>이 이 축으로 들어온다.
                // 종전에는 전용 카운터를 여기서 직접 더했고, 그래서 상태이상 목록에 없는 값이 하나
                // 떠 있었다 — 이제 배지·툴팁·피해가 같은 ActiveEffect 하나를 본다.
                // 이 함수는 예고·집행 공유 지점(R-8)이므로 여기 넣는 것만으로 둘이 함께 맞는다.
                + SumActiveEffectAmountByValueMode(monster.Id, StatusEffectValueMode.DamageDealtBonusFlat));
        }

        /// <summary>엘리트의 가하는 피해 보너스(%). 저작 100 = 보너스 0.</summary>
        private int GetEliteDamageBonusPercent(MonsterRuntime monster)
        {
            return monster != null && MonsterSpawnRoles.IsElite(monster.SpawnRole)
                ? Config.EliteDamagePercent - 100
                : 0;
        }

        private static void SetMonsterPlannedMove(MonsterRuntime monster, HexCoord resolvedCoord, bool canAttack)
        {
            if (monster == null)
            {
                return;
            }

            monster.IntentPredictedMoveCoord = resolvedCoord;
            monster.TurnPlan = monster.TurnPlan.WithResolvedMove(resolvedCoord, canAttack);
        }

        // 계획 레이어(MonsterAiPlanner)로 옮겨간 판정을 해소부에서 그대로 쓰기 위한 위임.
        private static bool IsMovementIntent(EnemyIntentType type)
        {
            return MonsterAiPlanner.IsMovementIntent(type);
        }
    }
}

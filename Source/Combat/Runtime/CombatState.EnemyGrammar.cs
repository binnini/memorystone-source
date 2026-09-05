using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 신규 적 문법 3종(T7-2): 약오름·맷집·뒤끝.
    ///
    /// 🔴 대원칙(설계 문서 §4 T7): 이 게임은 전투의 시작·끝이 없다 — StS의 "전투 1판" 단위 규칙은
    /// 이식 불가하고, 경계는 전부 <b>감지 상태</b>로 정의한다(약오름 해소·맷집 재장전이 그 산물).
    /// 감지의 단일 표면은 <see cref="MonsterFsmMemory.State"/>다: 은신(전체)·실명(단일) 마스킹이
    /// 이미 Step 안에서 State를 Search로 떨어뜨리므로 여기서 별도 마스킹 분기를 들 필요가 없다.
    /// (ActivityState는 시뮬레이션 LOD 등급이라 감지 술어로 쓰면 은신 중에도 약오름이 쌓인다 — 금지.)
    /// </summary>
    public sealed partial class CombatState
    {
        /// <summary>
        /// 약오름 적립 신호: 이번 턴 플레이어가 방어/정찰 카드를 썼는가.
        /// <see cref="RegisterActionCardUse"/>(단일 수렴점)만이 세우고, 턴 경계에서 리셋된다.
        /// 서스펜드 왕복 대상(bagAttackBonusThisTurn 선례) — 잃으면 그 턴의 적립이 조용히 증발한다.
        /// </summary>
        private bool defensiveCardUsedThisTurn;

        /// <summary>뒤끝 중복 집행 방지 래치(호루라기 relicKillRewardedMonsterIds 선례).</summary>
        private readonly HashSet<string> deathAftermathResolvedMonsterIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 이 몬스터가 지금 플레이어를 <b>감지 중</b>인가(추격/공격/경계). Search는 "놓치고 마지막
        /// 목격지를 뒤지는 중"이라 비감지다 — 도주·은신이 약오름 해소·맷집 재장전의 리셋 수단이 되는
        /// 것이 이 정의에서 나온다.
        /// </summary>
        internal static bool IsMonsterDetectingPlayer(MonsterRuntime monster)
        {
            if (monster == null || monster.Combatant.IsDead)
            {
                return false;
            }

            var state = monster.FsmMemory.State;
            return state == MonsterFsmState.Chase
                   || state == MonsterFsmState.Attack
                   || state == MonsterFsmState.Alert;
        }

        private bool TryGetEnemyGrammarEntry(MonsterRuntime monster, out MonsterCatalogEntry entry)
        {
            entry = default;
            return monster != null
                   && !IsPropMonster(monster)
                   && monsterCatalog != null
                   && monsterCatalog.TryGetEntry(monster.DefinitionId, out entry);
        }

        /// <summary>행동 카드 사용 시 적 문법 신호 갱신 — 약오름은 방어·정찰 카드에만 반응한다.</summary>
        private void RegisterEnemyGrammarCardUse(CombatCardKind kind)
        {
            if (kind == CombatCardKind.Defend || kind == CombatCardKind.Scout)
            {
                defensiveCardUsedThisTurn = true;
            }
        }

        /// <summary>
        /// 몬스터 턴 경계에서 한 번 도는 적 문법 진행(쿨다운과 같은 경계 — TickControlPacingTimers).
        /// 이 시점은 <see cref="BeginNextOverallTurn"/>의 ApplyActiveEffectTurnStart 안이라
        /// <see cref="defensiveCardUsedThisTurn"/>이 아직 방금 끝난 턴의 값을 들고 있다(리셋은 그 뒤).
        /// </summary>
        private void TickEnemyGrammarPerTurn()
        {
            // 홀림(오라 봉인)은 죽은 개체의 해제까지 맡으므로 산 놈만 도는 아래 루프 밖에서 돈다.
            TickAuraSealPerTurn();

            foreach (var monster in monsters)
            {
                if (monster.Combatant.IsDead || !TryGetEnemyGrammarEntry(monster, out var entry))
                {
                    continue;
                }

                var detecting = IsMonsterDetectingPlayer(monster);

                if (entry.HasAgitation)
                {
                    if (MonsterAgitationCondition.IsStrengthDistance(entry.AgitationConditionRef))
                    {
                        TickDistanceStrength(monster, entry);
                    }
                    else if (detecting && defensiveCardUsedThisTurn)
                    {
                        var before = monster.AgitationStacks;
                        SetMonsterAgitationStacks(
                            monster, Math.Min(entry.AgitationMaxStacks, monster.AgitationStacks + 1));
                        AnnounceAgitationChange(monster, before);
                    }
                    else if (!detecting && monster.AgitationStacks > 0)
                    {
                        var before = monster.AgitationStacks;
                        SetMonsterAgitationStacks(monster, monster.AgitationStacks - 1);
                        AnnounceAgitationChange(monster, before);
                    }

                    // 🔴 스택이 <b>안 변한 턴</b>에도 힘을 다시 세운다(2026-09-05 실플레이 피드백:
                    // "힘이 몇 턴 있다가 사라져버림"). 위 세 갈래는 스택이 움직일 때만 돌고, 투영
                    // (SyncMonsterMightEffect)은 그 안에서만 불렸다 — 감지 중인데 방어·정찰을 안 쓴 턴은
                    // 어느 갈래도 안 타므로 힘의 갱신 지속(2턴)이 그대로 흘러 만료됐다.
                    // 투영은 멱등이라 매 턴 불러도 값이 흔들리지 않는다. 카운터가 정본이고,
                    // 카운터가 0이 될 때까지 힘이 산다 — 그게 원래 계약이었다.
                    SyncMonsterMightEffect(monster);
                }

                if (entry.HasToughness && monster.ToughnessSpent)
                {
                    // 2026-08-20 #11(사용자 확정): 재충전은 <b>순수 쿨다운</b>이다 — 「나를 놓친 채 N턴」
                    // 연속 조건은 폐기됐다. 플레이어가 계속 붙어 있으면 영영 안 차던 종전 규칙은
                    // "발동 후 3턴 있다가 채워짐"이라는 요구와 정반대였다(들킬수록 안 찬다).
                    //
                    // 🔑 경계 정의: 이 훅은 턴 시작(BeginNextOverallTurn)에 돈다. 발동 턴 T에는
                    // ToughnessSpent가 아직 false였으므로 첫 증가는 T+1이다. 사용자 확정은 <b>발동한
                    // 턴을 빼고 3턴이 지난 뒤</b>, 즉 T+4에 부활이다 — T+1·T+2·T+3은 여전히 무르다.
                    // 그래서 비교가 >= 가 아니라 > 다(T+4에 progress 4 > 3).
                    monster.ToughnessReloadProgress += 1;
                    if (monster.ToughnessReloadProgress > entry.ToughnessReloadTurns)
                    {
                        monster.ToughnessSpent = false;
                        monster.ToughnessReloadProgress = 0;
                        // 배지는 준비/소진을 색으로만 가른다 — 「언제 다시 굳었나」는 이 한 줄이 유일한 신호다.
                        RaiseMonsterTraitAnnouncement(monster, MonsterTraitAnnouncement.ToughnessReloadedRef);
                    }
                }

                TickGuardRecharge(monster, entry);
            }
        }

        /// <summary>
        /// 수호 재충전 특성(2026-09-05 결정 1 · Q13): <c>guardRechargeTurns</c>마다 수호 충전을 1 얻는다. 상한
        /// <see cref="MonsterCatalogEntry.GuardRechargeMaxCharges"/>(1)에 닿아 있으면 진행만 되감고 부여하지 않는다 —
        /// 상태이상을 안 쓰는 동안 무한히 쌓여 뒤에 통째로 막히는 것이 상한의 존재 이유다. 옛 guard 기믹
        /// (페이즈 진입 1회)과 달리 보스 전용이 아니라 카탈로그 컬럼을 가진 어느 몬스터든 쓴다.
        /// 부여는 플레이어 수호와 같은 상태·같은 소비 관문(TryConsumeGuardCharge)을 지난다.
        /// </summary>
        private void TickGuardRecharge(MonsterRuntime monster, MonsterCatalogEntry entry)
        {
            if (!entry.HasGuardRecharge || monster.Combatant.IsDead)
            {
                return;
            }

            monster.GuardRechargeProgress += 1;
            if (monster.GuardRechargeProgress < entry.GuardRechargeTurns)
            {
                return;
            }

            monster.GuardRechargeProgress = 0;
            var index = activeEffects.IndexOfCharged(monster.Id, StatusEffectKind.Guard);
            var current = index >= 0 ? activeEffects[index].Amount : 0;
            if (current >= MonsterCatalogEntry.GuardRechargeMaxCharges)
            {
                return;
            }

            // 수명은 턴이 아니라 소비다(OnConsume) — turns 값은 IsExpired(>0)만 만족하면 된다(보스 guard 기믹과 동일).
            AddDurationStatusEffect(StatusEffectKind.Guard, monster.Id, 1, 1, MonsterTraitAnnouncement.GuardRechargedRef);
            RaiseStatusEffect(StatusEffectKind.Guard, monster.Coord, 0, 1, monster.Id, MonsterTraitAnnouncement.GuardRechargedRef);
            RaiseMonsterTraitAnnouncement(monster, MonsterTraitAnnouncement.GuardRechargedRef, current + 1);
        }

        /// <summary>
        /// 몬스터 방어막(Block)을 <b>몬스터가 다시 행동하기 직전</b>에 지운다 — <b>견고 특성을 가진
        /// 몬스터만 예외</b>(2026-08-10 사용자 확정). 플레이어의 <c>Player.ClearBlock()</c>과 대칭이라
        /// "방어막은 한 사이클짜리"가 양쪽 공통 어휘가 되고, 그 어휘를 깨는 놈은 배지로 드러난다.
        ///
        /// <para>🔴 호출 자리는 <see cref="ResolveMonsterAttackStep"/> 머리다. 전체 턴 경계
        /// (<c>BeginNextOverallTurn</c>)는 몬스터 행동 <b>직후</b>라, 거기서 지우면 방금 두른 방어막이
        /// 같은 호출 안에서 증발한다(실측 — shieldGain이 항상 0으로 보였다).</para>
        ///
        /// <para>🔑 견고가 없으면 불가살의 trap-volley 방어막(§21.8 제안 4)이 죽는다 — 그 기믹은
        /// "부술 때까지 남는" 것을 전제로 설계된 「깎아라」 퍼즐이다.</para>
        ///
        /// <para>기물(프롭)도 그대로 지운다 — 프롭은 애초에 Block을 얻는 경로가 없어 무해하고,
        /// 예외를 하나 더 두면 규칙이 둘로 갈린다.</para>
        /// </summary>
        private void ClearMonsterBlockExceptSturdy()
        {
            foreach (var monster in monsters)
            {
                if (monster.Combatant.Block <= 0)
                {
                    continue;
                }

                if (TryGetEnemyGrammarEntry(monster, out var entry) && entry.HasSturdyBlock)
                {
                    // 견고가 <b>일한</b> 순간은 정확히 여기다: 다른 놈이면 지금 지워졌을 방어막이 남는다.
                    // 방어막이 없으면 애초에 이 루프에 들어오지 않으므로(위의 Block <= 0 continue)
                    // "굳지도 않았는데 견고!"가 뜰 수 없다.
                    RaiseMonsterTraitAnnouncement(
                        monster, MonsterTraitAnnouncement.SturdyKeptRef, monster.Combatant.Block);
                    continue;
                }

                monster.Combatant.ClearBlock();
            }
        }

        /// <summary>
        /// 공격 패턴의 방어막 저작(<c>monster_attack_patterns.csv</c> <c>shieldGain</c>)을 집행한다 —
        /// 몬스터가 <b>자기 자신</b>에게 방어막을 두른다. 공격 패턴에도, 자기부여 패턴(targeting=self)에도
        /// 붙일 수 있어 "치면서 굳는다"와 "이번 턴은 몸만 굳힌다"가 컬럼 하나로 갈린다.
        ///
        /// <para>부여·흡수·연출이 전부 기존 것이다: 필드는 플레이어와 공유하는
        /// <see cref="CombatantState.Block"/>, 흡수는 <c>ApplyDamage</c>, 연출은 플레이어 방어 획득과
        /// 같은 <see cref="EffectKind.Block"/>("+N" 플로팅). 보스 수호 방어막
        /// (<c>GrantBossGuardBlock</c>)과 같은 계약이고, 다른 것은 부여 계기뿐이다.</para>
        ///
        /// <para>수명은 <see cref="ClearMonsterBlockExceptSturdy"/>가 정한다 — 견고가 없으면 다음 전체
        /// 턴 시작에 사라진다.</para>
        /// </summary>
        private void GrantMonsterPatternShield(MonsterRuntime monster, MonsterAttackPattern pattern)
        {
            if (monster == null || pattern.ShieldGain <= 0 || monster.Combatant.IsDead)
            {
                return;
            }

            monster.Combatant.AddBlock(pattern.ShieldGain);
            RaiseEffect(
                EffectKind.Block,
                monster.Coord,
                0,
                pattern.ShieldGain,
                monster.Id,
                ToMonsterPatternSourceRef(pattern),
                sourceUnitId: monster.Id,
                sourceActorKind: "monster",
                targetActorKind: "monster",
                sourcePatternId: pattern.Id);
        }

        /// <summary>
        /// 담력 시험(거구귀 · 2026-09-04 §2-B) — 「멀어질수록 강해지고, 붙으면 힘이 꺼진다」.
        /// <b>재설정형</b>: 매 턴 힘 = clamp((몸 거리 − 1) × 2, 0, 상한). 누적·리셋이 없으므로
        /// 격앙 분기와 배관을 공유하지 않는다(어둠 먹기가 약오름 배관을 빌려 쓰다 표기가 샌 선례).
        /// 스택 저장·피해 보너스(+1/스택)·배지·세이브는 힘 카운터(AgitationStacks) 공용이다.
        ///
        /// <para>🔑 거리는 <see cref="MonsterBodyShape.DistanceFrom"/>(몸통 칸 최솟값) — tri 몸 거리
        /// 술어와 같은 자를 쓴다. 인접(거리 1) = 0. 구 겁먹음(agitation.dread·누적형)은 이 특성으로
        /// 대체되며 「끌어당김→자기 해제」 규약도 함께 소멸했다(A049 뱉어내기 반전).</para>
        /// </summary>
        private void TickDistanceStrength(MonsterRuntime monster, MonsterCatalogEntry entry)
        {
            var distance = GetMonsterBody(monster).DistanceFrom(monster.Coord, PlayerCoord);
            SetMonsterAgitationStacks(monster, Math.Clamp(
                (distance - 1) * MonsterAgitationCondition.StrengthPerDistance,
                0,
                entry.AgitationMaxStacks));
        }

        /// <summary>홀림 오라 봉인의 개체별 출처 ref — 제거·제자리 갱신의 열쇠다(장판 속박 선례).</summary>
        internal static string AuraSealSourceRef(string monsterId) => $"monster.aura.seal.{monsterId}";

        /// <summary>오라 유지 지속(턴) — 틱 사이에 자연 만료되지 않게 2로 두고, 실제 해제는 이탈 틱이 한다.</summary>
        private const int AuraSealRefreshTurns = 2;

        /// <summary>
        /// 홀림(그슨새 · 2026-09-04 §2-C) — 그슨새가 플레이어와 <b>거리 radius 이내에 있는 동안</b>
        /// 무작위 카드 cards장을 봉인(Seal)으로 잠그고, 벗어나거나 죽으면 푼다. 오라형.
        ///
        /// <para>🔑 어느 카드가 잠기는가는 기존 Seal 규약(Amount 합산 + <c>StableSealHash</c> 결정적
        /// 순위)이 정한다 — 무시드 RNG가 없고(A035 저주 풀 「같은 상황이면 같은 카드」 규약과 동일),
        /// 여러 마리·사자탈 A026과 겹쳐도 장수 합산이라 같은 카드를 이중 봉인하지 않는다.</para>
        ///
        /// <para>🔑 부여는 장판 속박(<c>ApplyOrRefreshFieldImmobilize</c>) 선례를 따라 부여 관문을
        /// 거치지 않고 레지스트리를 직접 유지한다 — 매 턴 다시 거는 오라가 관문(수호)을 두드리면
        /// 접근만으로 충전이 매턴 타는 다른 게임이 된다. 해제는 자연 만료와 같은 신호를 올려
        /// HUD·루프 VFX가 기존 구독자로 걷힌다.</para>
        /// </summary>
        private void TickAuraSealPerTurn()
        {
            foreach (var monster in monsters)
            {
                if (monster == null
                    || IsPropMonster(monster)
                    || monsterCatalog == null
                    || !monsterCatalog.TryGetEntry(monster.DefinitionId, out var entry)
                    || !MonsterAuraSeal.TryParse(entry.HiddenTraitRef, entry.HiddenTraitParam, out var spec, out _))
                {
                    continue;
                }

                var sourceRef = AuraSealSourceRef(monster.Id);
                var qualifies = !monster.Combatant.IsDead
                                && !Player.IsDead
                                && monster.Coord.DistanceTo(PlayerCoord) <= spec.Radius;
                if (qualifies)
                {
                    var refreshed = new ActiveEffect(
                        EffectType.Duration, StatusEffectKind.Seal, PlayerUnitId,
                        AuraSealRefreshTurns, spec.Cards, sourceRef);
                    if (!activeEffects.TryReplaceInPlaceFromSameSource(refreshed))
                    {
                        activeEffects.Add(refreshed);
                        // 진입 전이만 알린다(「전이만 알린다」 특성 알림 규약). 봉인 배지 자체는 상태
                        // 채널이 들고 있으므로 여기서는 원인(홀림)을 가리키는 글자 하나면 된다.
                        RaiseMonsterTraitAnnouncement(monster, MonsterTraitAnnouncement.AuraSealAppliedRef, spec.Cards);
                        RaiseStatusEffect(
                            StatusEffectKind.Seal, PlayerCoord, 0, spec.Cards, PlayerUnitId,
                            sourceRef, sourceUnitId: monster.Id);
                    }

                    continue;
                }

                RemoveAuraSealEffects(sourceRef);
            }
        }

        /// <summary>오라 이탈·사망 해제 — 자연 만료와 같은 만료 신호를 올린다(반환 뒤끝과 같은 처방).</summary>
        private void RemoveAuraSealEffects(string sourceRef)
        {
            for (var i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (effect.Kind != StatusEffectKind.Seal
                    || !string.Equals(effect.TargetUnitId, PlayerUnitId, StringComparison.Ordinal)
                    || !string.Equals(effect.SourceRef, sourceRef, StringComparison.Ordinal))
                {
                    continue;
                }

                activeEffects.RemoveAt(i);
                RaiseStatusEffectExpired(effect, sourceRef);
            }
        }

        /// <summary>
        /// 약오름 스택 변화 알림(2026-09-01). <b>전이만</b> 말한다: 오르면 오른 값을, 0으로 내려가면
        /// 「해소」를. 내려가는 도중(3→2→1)은 침묵한다 — 매 턴 뜨는 글자는 정보가 아니라 소음이고,
        /// 스택 수 자체는 배지가 상시로 들고 있다.
        /// </summary>
        private void AnnounceAgitationChange(MonsterRuntime monster, int before)
        {
            var after = monster.AgitationStacks;
            if (after > before)
            {
                RaiseMonsterTraitAnnouncement(monster, MonsterTraitAnnouncement.AgitationGainedRef, after);
            }
            else if (after == 0 && before > 0)
            {
                RaiseMonsterTraitAnnouncement(monster, MonsterTraitAnnouncement.AgitationClearedRef);
            }
        }

        /// <summary>약오름 스택은 공격 피해 flat +1/스택 — 예고=집행 공유 산식이 소비한다.</summary>
        /// <summary>
        /// 힘 스택의 <b>단일 기록자</b>(2026-09-04). 스택을 쓰는 자리가 넷이었고 각자 상태이상을
        /// 함께 갱신해야 하는데, 그건 「순서가 아니라 구조가 지킨다」를 어기는 배치다 — 하나만
        /// 빠뜨려도 배지와 피해가 조용히 갈라진다. 쓰는 문이 하나면 갈라질 수 없다.
        ///
        /// <para>🔑 카운터가 정본이고 상태이상은 그 <b>투영</b>이다. 반대로 두면(상태이상이 정본)
        /// 세이브 하위호환이 깨진다 — 구세이브는 카운터만 들고 있다.</para>
        /// </summary>
        internal void SetMonsterAgitationStacks(MonsterRuntime monster, int stacks)
        {
            if (monster == null)
            {
                return;
            }

            monster.AgitationStacks = Math.Max(0, stacks);
            SyncMonsterMightEffect(monster);
        }

        /// <summary>힘 상태이상을 카운터에 맞춘다. 멱등이라 몇 번을 불러도 같은 결과다.</summary>
        private void SyncMonsterMightEffect(MonsterRuntime monster)
        {
            var sourceRef = MonsterMightSourceRef(monster.Id);
            var stacks = monster.Combatant.IsDead ? 0 : Math.Max(0, monster.AgitationStacks);
            if (stacks <= 0)
            {
                RemoveMonsterMightEffect(sourceRef);
                return;
            }

            // 지속은 「카운터가 0이 될 때까지」다 — 턴으로 만료시키면 기록자가 둘이 된다(틱과 카운터).
            // 오라 봉인과 같은 처방: 제자리 갱신 + 이탈 시 명시적 제거.
            var refreshed = new ActiveEffect(
                EffectType.Duration, StatusEffectKind.Might, monster.Id,
                MonsterMightRefreshTurns, stacks, sourceRef);
            if (!activeEffects.TryReplaceInPlaceFromSameSource(refreshed))
            {
                activeEffects.Add(refreshed);
            }
        }

        private void RemoveMonsterMightEffect(string sourceRef)
        {
            for (var i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (effect.Kind == StatusEffectKind.Might
                    && string.Equals(effect.SourceRef, sourceRef, StringComparison.Ordinal))
                {
                    activeEffects.RemoveAt(i);
                    RaiseStatusEffectExpired(effect, sourceRef);
                }
            }
        }

        /// <summary>개체별 출처 ref — 제거·제자리 갱신의 열쇠(오라 봉인과 같은 규약).</summary>
        internal static string MonsterMightSourceRef(string monsterId) => $"monster.trait.might.{monsterId}";

        /// <summary>틱 사이에 자연 만료되지 않게 2로 둔다. 실제 해제는 카운터가 0이 될 때 일어난다.</summary>
        private const int MonsterMightRefreshTurns = 2;

        /// <summary>
        /// 저장된 카운터에서 힘을 다시 세운다(세이브 복원·전투 개시). 구세이브는 상태이상 없이
        /// 카운터만 들고 오므로, 이 한 줄이 없으면 불러온 판에서 배지와 피해가 사라진다.
        /// </summary>
        internal void RebuildMonsterMightEffects()
        {
            foreach (var monster in monsters)
            {
                if (monster != null)
                {
                    SyncMonsterMightEffect(monster);
                }
            }
        }

        /// <summary>
        /// 맷집이 지금 유효한가(저작 + 미소진). 판정만 한다 — 소진은 실행 경로(<c>execute</c>)에서만
        /// 일어나므로 미리보기가 래치를 태우지 않는다.
        /// </summary>
        private bool IsMonsterToughnessReady(MonsterRuntime monster)
        {
            return monster != null
                   && !monster.ToughnessSpent
                   && TryGetEnemyGrammarEntry(monster, out var entry)
                   && entry.HasToughness;
        }

        /// <summary>
        /// 뒤끝(T7-2): 죽은 자리에 저작된 효과를 남긴다. 처치 수렴 지점
        /// (<see cref="ResetDeadMonsterToPatrolIntent"/>)에서 호출되므로 공격·스플래시·반사·함정·필드
        /// 어느 경로로 죽어도 발동한다. 기물 제외·몬스터별 래치는 호루라기와 같은 계약.
        /// </summary>
        /// <summary>
        /// 뒤끝 한 벌을 <b>플레이어에게 설명할 수 있는 모양</b>으로 편 것(2026-09-01 #3).
        /// 배지 호버가 「무엇을·어디에」를 그리는 데 쓴다.
        /// </summary>
        public readonly struct MonsterAftermathPreview
        {
            public MonsterAftermathPreview(
                IReadOnlyList<HexCoord> cells,
                StatusEffectKind? statusKind,
                int curseCount,
                bool leavesField,
                bool explodes,
                bool restoresStatus,
                bool restoresMoney = false,
                int stolenMoney = 0)
            {
                Cells = cells ?? System.Array.Empty<HexCoord>();
                StatusKind = statusKind;
                CurseCount = curseCount;
                LeavesField = leavesField;
                Explodes = explodes;
                RestoresStatus = restoresStatus;
                RestoresMoney = restoresMoney;
                StolenMoney = stolenMoney;
            }

            /// <summary>
            /// 이 뒤끝이 미치는 칸들. <b>비어 있을 수 있다</b> — 막타 디버프·반환처럼 거리와 무관한
            /// 갈래는 그릴 자리가 없다(없는데 그리면 「저 칸만 위험하다」로 거짓말한다).
            /// </summary>
            public IReadOnlyList<HexCoord> Cells { get; }

            /// <summary>상태이상을 남기거나 걷어 가는 갈래면 그 종류.</summary>
            public StatusEffectKind? StatusKind { get; }

            /// <summary>덱에 남기는 저주 장수(0이면 저주 갈래가 아니다).</summary>
            public int CurseCount { get; }

            public bool LeavesField { get; }
            public bool Explodes { get; }
            public bool RestoresStatus { get; }

            /// <summary>소매치기(2026-09-04) — 처치하면 훔친 엽전을 돌려주는 갈래인가.</summary>
            public bool RestoresMoney { get; }

            /// <summary>지금까지 훔쳐 간 액수 — 「쫓아가 잡을 이유」가 화면에 보여야 성립한다(§2-D).</summary>
            public int StolenMoney { get; }
        }

        /// <summary>
        /// 이 몬스터의 뒤끝을 편다(2026-09-01 #3 — 배지 호버 오버레이·툴팁이 쓴다).
        ///
        /// <para>🔑 <b>집행과 같은 파서</b>(<see cref="MonsterDeathAftermath.TryParse"/>)를 지난다 —
        /// 화면이 말하는 것과 실제로 일어나는 일이 갈라질 수 없다. 「뒤끝!」 한 낱말로는 무엇을
        /// 당했는지 모른다는 것이 사용자 지적이었고, 답은 새 표가 아니라 <b>이미 있는 저작을 읽는 것</b>이다.</para>
        /// </summary>
        public bool TryGetMonsterAftermathPreview(string monsterId, out MonsterAftermathPreview preview)
        {
            preview = default;
            var monster = monsters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, monsterId, StringComparison.Ordinal));
            if (monster == null
                || monster.Combatant.IsDead
                || !TryGetEnemyGrammarEntry(monster, out var entry)
                || !entry.HasDeathAftermath
                || !MonsterDeathAftermath.TryParse(entry.OnDeathEffectRef, entry.OnDeathEffectParam, out var spec, out _))
            {
                return false;
            }

            var restoresMoney = spec.Kind == MonsterDeathAftermathKind.Restore && spec.RestoresMoney;
            preview = new MonsterAftermathPreview(
                CollectAftermathCells(monster, spec),
                spec.Kind == MonsterDeathAftermathKind.Debuff
                || (spec.Kind == MonsterDeathAftermathKind.Restore && !spec.RestoresMoney)
                    ? spec.StatusKind
                    : (StatusEffectKind?)null,
                spec.Kind == MonsterDeathAftermathKind.Curse ? spec.CurseCount : 0,
                spec.Kind == MonsterDeathAftermathKind.Field,
                spec.Kind == MonsterDeathAftermathKind.Blast,
                spec.Kind == MonsterDeathAftermathKind.Restore && !spec.RestoresMoney,
                restoresMoney,
                restoresMoney ? monster.StolenMoney : 0);
            return true;
        }

        /// <summary>
        /// 특성 배지에 손을 얹었을 때 판에 그릴 <b>그 특성의 범위</b>(2026-09-04 사용자 요구).
        /// 무엇을 세는지는 특성마다 다르지만 <see cref="MonsterTraitReachKind"/> 한 축으로 갈리므로,
        /// 표현층은 「칸 목록」 하나만 받는다.
        ///
        /// <para>🔑 칸을 세는 술어는 <b>집행이 쓰는 것과 같다</b> — 뒤끝은 발동 사거리
        /// (<see cref="WillMonsterDeathAftermathResolve"/>와 같은 반경), 홀림은 오라 반경, 담력 시험은
        /// 힘이 붙는 거리다. 그리는 그림과 실제로 일어나는 일이 갈라질 표면이 없다.</para>
        ///
        /// <para>🔑 <b>담력 시험만 「위험한 칸」의 뜻이 뒤집혀 있다.</b> 뒤끝·홀림은 칠해진 칸이 위험
        /// 지대지만, 담력 시험은 칠해진 칸이 「여기 서면 저 놈이 세진다」라서 <b>안 칠해진 인접 링이
        /// 답</b>이다. 그림 하나로 「붙어라」가 읽히는 것이 이 특성의 유일한 대응이기도 하다.</para>
        /// </summary>
        public bool TryGetMonsterTraitReach(string monsterId, string traitId, out IReadOnlyList<HexCoord> cells)
        {
            cells = System.Array.Empty<HexCoord>();
            var traitCatalog = MonsterTraitCatalogProvider.Active;
            if (traitCatalog == null || !traitCatalog.TryGet(traitId, out var trait))
            {
                return false;
            }

            var monster = monsters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, monsterId, StringComparison.Ordinal));
            if (monster == null || monster.Combatant.IsDead || !TryGetEnemyGrammarEntry(monster, out var entry))
            {
                return false;
            }

            switch (trait.Reach)
            {
                case MonsterTraitReachKind.Aftermath:
                    if (MonsterDeathAftermath.TryParse(
                            entry.OnDeathEffectRef, entry.OnDeathEffectParam, out var aftermathSpec, out _))
                    {
                        cells = CollectAftermathCells(monster, aftermathSpec);
                    }

                    break;

                case MonsterTraitReachKind.AuraRadius:
                    if (MonsterAuraSeal.TryParse(entry.HiddenTraitRef, entry.HiddenTraitParam, out var seal, out _))
                    {
                        cells = CollectCellsWithinRadius(monster, seal.Radius);
                    }

                    break;

                case MonsterTraitReachKind.DistanceStrength:
                    cells = CollectDistanceStrengthCells(monster);
                    break;
            }

            return cells.Count > 0;
        }

        /// <summary>몬스터 몸에서 반경 이내의 실재하는 칸. 반경이 음수면 빈 목록(제한 없음 = 그릴 자리 없음).</summary>
        private IReadOnlyList<HexCoord> CollectCellsWithinRadius(MonsterRuntime monster, int radius)
        {
            if (radius < 0)
            {
                return System.Array.Empty<HexCoord>();
            }

            var body = GetMonsterBody(monster);
            var cells = new List<HexCoord>();
            foreach (var cell in Map.AllCells)
            {
                if (body.DistanceFrom(monster.Coord, cell.Coord) <= radius)
                {
                    cells.Add(cell.Coord);
                }
            }

            return cells;
        }

        /// <summary>
        /// 담력 시험 오버레이의 바깥 반경(몸 거리). 힘은 거리에 따라 계속 붙지만 그림은 여기까지만
        /// 그린다 — 2026-09-05 실플레이에서 「거리 2 이상 전부」가 맵 전체를 칠해 범위가 아니라
        /// 배경으로 읽혔다(사용자 확정: 3칸으로 한정).
        /// </summary>
        public const int DistanceStrengthReachRadius = 3;

        /// <summary>
        /// 담력 시험이 <b>일하는</b> 칸 — 힘이 1이라도 붙는 자리(몸 거리 2 이상)를
        /// <see cref="DistanceStrengthReachRadius"/>까지만. 칠해지지 않은 인접 링이 「붙으면 0」을
        /// 그림 하나로 말하고, 바깥 경계는 「멀수록 세진다」의 방향을 읽히게 한다.
        /// </summary>
        private IReadOnlyList<HexCoord> CollectDistanceStrengthCells(MonsterRuntime monster)
        {
            var body = GetMonsterBody(monster);
            var cells = new List<HexCoord>();
            foreach (var cell in Map.AllCells)
            {
                var distance = body.DistanceFrom(monster.Coord, cell.Coord);
                if (distance >= 2 && distance <= DistanceStrengthReachRadius)
                {
                    cells.Add(cell.Coord);
                }
            }

            return cells;
        }

        /// <summary>
        /// 뒤끝이 미치는 칸들. 반경이 없는(거리와 무관한) 갈래는 <b>빈 목록</b>이다 — 그릴 자리가
        /// 없는데 그리면 「저 칸만」으로 읽혀 거짓말이 된다.
        /// </summary>
        private IReadOnlyList<HexCoord> CollectAftermathCells(MonsterRuntime monster, MonsterDeathAftermathSpec spec)
        {
            var radius = spec.Kind switch
            {
                MonsterDeathAftermathKind.Field => spec.Radius,
                MonsterDeathAftermathKind.Blast => spec.Radius,
                // 저주는 반경이 <b>부여 사거리</b>다(#3). 음수 = 제한 없음이라 그릴 자리가 없다.
                MonsterDeathAftermathKind.Curse => spec.Radius,
                MonsterDeathAftermathKind.Debuff => spec.Radius,
                _ => -1,
            };
            if (radius < 0)
            {
                return System.Array.Empty<HexCoord>();
            }

            var cells = new List<HexCoord>();
            foreach (var cell in Map.AllCells)
            {
                if (monster.Coord.DistanceTo(cell.Coord) <= radius)
                {
                    cells.Add(cell.Coord);
                }
            }

            return cells;
        }

        /// <summary>
        /// 이 뒤끝이 실제로 성립하는가 — 사거리·생존 판정의 <b>단일 지점</b>(2026-09-01 #3).
        ///
        /// <para>알림과 집행이 같은 술어를 지나므로 "뒤끝!"만 뜨고 아무 일도 안 일어나는 화면이
        /// 구조적으로 불가능하다. 저주의 반경은 <b>음수 = 제한 없음</b>이고(종전 저작 보존),
        /// 폭발의 반경은 언제나 유효 거리다.</para>
        /// </summary>
        private bool WillMonsterDeathAftermathResolve(MonsterRuntime monster, MonsterDeathAftermathSpec spec)
        {
            switch (spec.Kind)
            {
                case MonsterDeathAftermathKind.Blast:
                    return !Player.IsDead && monster.Coord.DistanceTo(PlayerCoord) <= spec.Radius;

                case MonsterDeathAftermathKind.Curse:
                // 막타 디버프도 같은 문법을 쓴다(2026-09-04) — 반경을 안 적으면 종전대로 무조건이고,
                // 적으면 거리가 선택지가 된다. 술어가 하나라 화면(오버레이)과 집행이 갈라질 수 없다.
                case MonsterDeathAftermathKind.Debuff:
                    // 저주는 <b>영구</b> 오염이라 「죽으면 무조건」은 사기였다(사용자 확정). 붙어서 잡으면
                    // 대가를 치르고 떨어져서 잡으면 면한다 — 거리가 곧 선택지가 된다.
                    return spec.Radius < 0 || monster.Coord.DistanceTo(PlayerCoord) <= spec.Radius;

                case MonsterDeathAftermathKind.Restore:
                    // 엽전 반환(소매치기)은 훔친 것이 있어야 성립한다 — 0원 반환에 「되돌려줌!」이 뜨면
                    // 아무 일도 안 일어난 화면이다. 상태 반환 갈래는 종전 동작 그대로 항상 발동한다.
                    return !spec.RestoresMoney || monster.StolenMoney > 0;

                default:
                    return true;
            }
        }

        private void ResolveMonsterDeathAftermath(MonsterRuntime monster)
        {
            if (monster == null || !monster.Combatant.IsDead || IsPropMonster(monster))
            {
                return;
            }

            if (!TryGetEnemyGrammarEntry(monster, out var entry) || !entry.HasDeathAftermath)
            {
                return;
            }

            if (!deathAftermathResolvedMonsterIds.Add(monster.Id))
            {
                return;
            }

            // 임포트가 같은 파서로 걸렀으므로 여기 실패는 사실상 불가능하지만, 만약을 위해 조용히 생략한다
            // (죽음 처리 중 예외는 처치 자체를 깨뜨린다).
            if (!MonsterDeathAftermath.TryParse(entry.OnDeathEffectRef, entry.OnDeathEffectParam, out var spec, out _))
            {
                return;
            }

            // 🔴 성립하지 않는 뒤끝은 <b>알리지도 않는다</b>(2026-09-01 #3). 사거리 게이트가 붙으면서
            //    "뒤끝!"만 뜨고 아무 일도 안 일어나는 화면이 가능해졌다 — 알리는 것은 상태가 아니라
            //    실제로 일어난 일이다. 판정은 아래 갈래들과 <b>같은 술어</b>를 쓴다(두 벌 금지).
            if (!WillMonsterDeathAftermathResolve(monster, spec))
            {
                return;
            }

            // 🔑 시체 자리에 <b>무엇을 남겼는지</b>를 띄운다(#3 사용자 요구: 「뒤끝!」만으로는 뭘 당했는지
            //    모른다). 문안은 MonsterTraitAnnouncement 한 표가 정하고 여기서는 갈래만 고른다.
            //    디버프 갈래만 "뒤끝!"으로 남는다 — 그쪽은 결과가 플레이어 쪽에 "뒤끝! {상태}"로 이미
            //    뜨므로, 여기서까지 상태 이름을 말하면 같은 말을 두 번 한다.
            RaiseMonsterTraitAnnouncement(monster, MonsterTraitAnnouncement.RefForAftermath(spec.Kind));

            switch (spec.Kind)
            {
                case MonsterDeathAftermathKind.Field:
                {
                    // 독기 장판: sourceUnitId가 몬스터이므로 기존 FieldDamage 핸들러가 그대로 플레이어를
                    // 때린다(소유자 검사). 같은 핸들러가 살아 있는 몬스터도 때리므로 장판 위의 다른 적도
                    // 상한다 — "잡는 위치를 골라라"의 양면(위험이자 이용 수단)으로 의도한 동작이다.
                    var fieldObject = new FieldObject(
                        monster.Coord,
                        spec.Radius,
                        spec.DurationTurns,
                        FieldObjectKind.FieldDamage,
                        spec.Damage,
                        sourceUnitId: monster.Id,
                        visualRef: MonsterDeathAftermath.FieldRef);
                    FieldObjects.Add(fieldObject);
                    RaiseFieldPlacementVfx(FieldObjectKind.FieldDamage, fieldObject, MonsterDeathAftermath.FieldRef);
                    break;
                }

                case MonsterDeathAftermathKind.Blast:
                {
                    // 흡수 폭발 계약(§19): 대상은 플레이어뿐(다른 기물·몬스터까지 맞으면 연쇄),
                    // Block이 먼저 깎이고 전부 막히면 "방어!".
                    // 사거리·생존 판정은 위 WillMonsterDeathAftermathResolve가 이미 했다 — 여기서 다시
                    // 재면 술어가 두 벌이 되어 언젠가 갈린다.
                    var blockBefore = Player.Block;
                    var applied = Player.ApplyDamage(spec.Damage);
                    if (applied <= 0 && Player.Block >= blockBefore)
                    {
                        break;
                    }

                    RaiseEffect(
                        applied > 0 ? EffectKind.Damage : EffectKind.DamageBlocked,
                        PlayerCoord,
                        0,
                        applied,
                        PlayerUnitId,
                        MonsterDeathAftermath.BlastRef,
                        sourceUnitId: monster.Id,
                        sourceActorKind: "monster",
                        targetActorKind: "player");
                    break;
                }

                case MonsterDeathAftermathKind.Curse:
                {
                    // 심술(요괴 §4-5 · 도깨비): 쓰러지며 저주 카드를 덱에 남긴다. 추첨은 순수 함수라
                    // 전투 중 저장·복원 뒤에도 같은 카드가 나온다(MonsterCurseCardPool 주석 참조).
                    // 서로 다른 장을 뽑는다 — 같은 저주를 두 장 넣는 것은 저작 의도가 아니다.
                    var seed = MonsterCurseCardPool.MixSeed(
                        $"{monster.Id}|{MonsterDeathAftermath.CurseRef}", OverallTurnNumber);
                    foreach (var cardId in MonsterCurseCardPool.PickDistinct(spec.CursePool, spec.CurseCount, seed))
                    {
                        TryInjectStatusCard(cardId);
                    }

                    break;
                }

                case MonsterDeathAftermathKind.Restore:
                {
                    // 소매치기(2026-09-04 §2-D): 훔친 엽전 <b>전액</b> 반환. 유물 보너스를 태우지 않는다 —
                    // 보상이 아니라 원금이 돌아오는 것이다. 처치 못 하고 전투가 끝나면(패배·이탈) 손실은
                    // 별도 코드가 필요 없다: 액수가 개체에 실려 있어 개체와 함께 사라진다.
                    if (spec.RestoresMoney)
                    {
                        // 지갑에는 <b>지금</b> 넣는다(전리품 목록을 건너뛰어도 원금은 잃지 않는다).
                        // 액수는 표시 전용 필드에 남겨 전리품 목록이 「(반환)」 줄을 세울 수 있게 한다
                        // (2026-09-05 사용자 요구) — StolenMoney는 여기서 0이 되므로 그 값을 쓸 수 없다.
                        PlayerInventory.Wallet.Add(monster.StolenMoney);
                        monster.RestoredMoney += monster.StolenMoney;
                        monster.StolenMoney = 0;
                        break;
                    }

                    // 훔친 것을 돌려준다 — 자연 만료와 <b>같은</b> 만료 신호를 올리므로 HUD 아이콘·루프 VFX가
                    // 기존 구독자로 알아서 걷힌다(정화와 같은 처방).
                    RemovePlayerStatusEffectsOfKind(spec.StatusKind, MonsterDeathAftermath.RestoreRef);
                    break;
                }

                case MonsterDeathAftermathKind.Debuff:
                {
                    // "막타 친 자" = 항상 플레이어(이 게임의 모든 피해 출처는 플레이어 — 호루라기 계약).
                    // 부여 관문이 false면(수호 무효·CC 면역) VFX도 함께 삼킨다(T2-C 계약).
                    var amount = StatusEffectInfo.DefaultAmount(spec.StatusKind);
                    if (AddDurationStatusEffect(spec.StatusKind, PlayerUnitId, spec.StatusTurns, amount, monster.Id))
                    {
                        RaiseStatusEffect(spec.StatusKind, PlayerCoord, 0, amount, PlayerUnitId,
                            MonsterDeathAftermath.DebuffRef, sourceUnitId: monster.Id);
                    }

                    break;
                }
            }
        }
    }
}

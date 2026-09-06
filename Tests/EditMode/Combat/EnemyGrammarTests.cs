using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 신규 적 문법 3종(T7-2) — 약오름·맷집·뒤끝. 전투 경계가 없는 게임이라 규칙 스코프는 전부
    /// <b>감지 상태</b>(FsmMemory.State ∈ Chase/Attack/Alert)로 정의된다는 것이 대원칙이고,
    /// 이 스위트가 그 계약(적립/해소·소진/재장전·죽은 자리 효과·저작 검증·서스펜드 왕복)을 잠근다.
    /// </summary>
    public sealed class EnemyGrammarTests
    {
        private const string MonsterId = "grammar-m1";
        private const string DefinitionId = "M001";
        private const int AttackCardDamage = 7;
        private const string CurseA = "XT1";
        private const string CurseB = "XT2";
        private const string CurseC = "XT3";

        // ------------------------------------------------------------------ 약오름

        [Test]
        public void AgitationStacksWhenDetectingAndDefendCardUsed()
        {
            var state = CreateState(agitationMaxStacks: 3);
            var monster = FirstMonster(state);

            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.TryPlayerDefend("D00"), Is.True, state.LastFailureReason);
            monster.FsmMemory.State = MonsterFsmState.Chase;
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();

            Assert.That(monster.AgitationStacks, Is.EqualTo(1),
                "감지 중(추격) + 방어 카드를 쓴 턴 → 약오름 +1.");
        }

        [Test]
        public void AgitationDoesNotStackWithoutDefensiveCard()
        {
            var state = CreateState(agitationMaxStacks: 3);
            var monster = FirstMonster(state);

            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            monster.FsmMemory.State = MonsterFsmState.Chase;
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();

            Assert.That(monster.AgitationStacks, Is.EqualTo(0),
                "방어·정찰 카드를 안 쓴 턴에는 감지 중이어도 쌓이지 않는다.");
        }

        /// <summary>
        /// 🔴 <b>방어 카드 신호는 그 턴에서 끝난다</b> — 다음 턴에 아무 카드도 안 써도 또 쌓이면 안 된다.
        ///
        /// 적립 신호(<c>defensiveCardUsedThisTurn</c>)는 턴 경계에서 <b>판정 뒤에</b> 리셋된다
        /// (<c>ResetPerTurnSignalsStep</c>). 리셋이 빠지면 방어 카드 한 장이 <b>영구 적립기</b>가 되어
        /// 그 뒤로 매 턴 약오름이 1씩 오른다 — 위 <c>AgitationStacksWhenDetectingAndDefendCardUsed</c>는
        /// 첫 턴만 보므로 이 사고를 못 잡는다.
        ///
        /// 7-1이 「턴 한정 리셋 5개는 서로 순서 무관」으로 판정한 그 묶음의 <b>수명</b> 계약이다
        /// (T7-5, 계획 §7).
        /// </summary>
        [Test]
        public void DefensiveCardSignalDoesNotLeakIntoTheFollowingTurn()
        {
            var state = CreateState(agitationMaxStacks: 3);
            var monster = FirstMonster(state);

            // 1턴: 감지 중에 방어 카드를 쓴다 → +1.
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.TryPlayerDefend("D00"), Is.True, state.LastFailureReason);
            monster.FsmMemory.State = MonsterFsmState.Chase;
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();
            Assert.That(monster.AgitationStacks, Is.EqualTo(1), "전제: 첫 턴에 적립된다.");

            // 2턴: 감지는 유지하되 아무 카드도 쓰지 않는다 → 그대로 1이어야 한다.
            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Chase);

            Assert.That(
                monster.AgitationStacks, Is.EqualTo(1),
                "카드를 안 썼는데 또 쌓였다 — 적립 신호가 턴 경계에서 리셋되지 않아 방어 카드 한 장이 영구 적립기가 됐다.");
        }

        [Test]
        public void AgitationDoesNotStackWhileUndetected()
        {
            var state = CreateState(agitationMaxStacks: 3);
            var monster = FirstMonster(state);

            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.TryPlayerDefend("D00"), Is.True, state.LastFailureReason);
            monster.FsmMemory.State = MonsterFsmState.Patrol;
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();

            Assert.That(monster.AgitationStacks, Is.EqualTo(0),
                "비감지(순찰) 상태에서는 방어 카드를 써도 쌓이지 않는다 — 감지 스코프가 곧 규칙이다.");
        }

        [Test]
        public void AgitationRespectsAuthoredCapAndDecaysWhileUndetected()
        {
            var state = CreateState(agitationMaxStacks: 3);
            var monster = FirstMonster(state);
            monster.AgitationStacks = 3;

            // 상한: 감지 + 방어 카드에도 3에서 멈춘다.
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.TryPlayerDefend("D00"), Is.True, state.LastFailureReason);
            monster.FsmMemory.State = MonsterFsmState.Chase;
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();
            Assert.That(monster.AgitationStacks, Is.EqualTo(3), "저작 상한(3)을 넘지 않는다.");

            // 해소: 비감지가 되면 매 턴 1씩 가라앉는다.
            monster.FsmMemory.State = MonsterFsmState.Search;
            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Search);
            Assert.That(monster.AgitationStacks, Is.EqualTo(2), "비감지(수색) 턴마다 1 해소 — Search는 '놓친 상태'다.");
            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Patrol);
            Assert.That(monster.AgitationStacks, Is.EqualTo(1));
        }

        [Test]
        public void AgitationAddsFlatDamageToMonsterAttackFormula()
        {
            var state = CreateState(agitationMaxStacks: 3);
            var monster = FirstMonster(state);
            var pattern = monster.CurrentAttackPattern;
            var baseline = state.ResolveMonsterAttackDamageToPlayer(monster, pattern);

            // 🔑 카운터를 직접 쓰지 않는다(2026-09-04). 피해는 이제 카운터가 아니라 <b>힘 상태이상</b>에서
            // 오므로, 기록자를 우회하면 배지도 피해도 안 붙는다 — 그 우회가 불가능한 것이 이 설계의 요점이다.
            state.SetMonsterAgitationStacks(monster, 2);

            Assert.That(state.ResolveMonsterAttackDamageToPlayer(monster, pattern), Is.EqualTo(baseline + 2),
                "힘 1스택당 공격 피해 +1(flat) — 예고·집행 공유 산식(R-8)에 실린다.");
            Assert.That(
                state.ActiveEffects.Any(effect =>
                    effect.Kind == StatusEffectKind.Might
                    && string.Equals(effect.TargetUnitId, monster.Id, System.StringComparison.Ordinal)
                    && effect.Amount == 2),
                Is.True,
                "약오름은 힘을 부여한다 — 배지와 피해가 같은 ActiveEffect 하나를 본다.");
        }

        [Test]
        public void AgitationNeverStacksOnMonsterWithoutTheGrammar()
        {
            var state = CreateState(agitationMaxStacks: 0);
            var monster = FirstMonster(state);

            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.TryPlayerDefend("D00"), Is.True, state.LastFailureReason);
            monster.FsmMemory.State = MonsterFsmState.Chase;
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();

            Assert.That(monster.AgitationStacks, Is.EqualTo(0), "문법 미저작 몬스터는 어떤 조건에서도 쌓이지 않는다.");
        }

        // ------------------------------------------------------------------ 맷집

        [Test]
        public void ToughnessHalvesFirstCardHitThenConsumes()
        {
            var state = CreateState(toughnessReloadTurns: 3, enemyDistance: 2);
            var monster = FirstMonster(state);
            var hpBefore = monster.Combatant.Hp;

            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerAttack(monster.Coord, "A-T"), Is.True, state.LastFailureReason);

            Assert.That(monster.Combatant.Hp, Is.EqualTo(hpBefore - (AttackCardDamage + 1) / 2),
                "첫 카드 피격은 반감(올림)된다.");
            Assert.That(monster.ToughnessSpent, Is.True, "반감과 함께 래치가 소진된다.");

            // 두 번째 공격은 온전히 들어간다 — 액션 덱이 2장뿐이라 다음 턴에 손을 다시 받아 때린다.
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();
            state.StartPlayerTurn();
            AdvanceToPlayerAction(state);
            var hpAfterFirst = monster.Combatant.Hp;
            Assert.That(state.TryPlayerAttack(monster.Coord, "A-T"), Is.True, state.LastFailureReason);
            Assert.That(monster.Combatant.Hp, Is.EqualTo(hpAfterFirst - AttackCardDamage),
                "소진 후에는 반감이 없다(재장전 3턴 전).");
        }

        /// <summary>
        /// 🔴🔴 <b>화면에 뜨는 피해 숫자도 반감돼야 한다</b>(2026-09-02 #5).
        ///
        /// <para>연출 루프가 피해 루프 <b>뒤에</b> 돌면서 숫자를 다시 풀었는데, 그때는 맷집 래치가 이미
        /// 타 있어 반감이 사라진 값이 떴다 — HP는 반만 깎이는데 플로팅 텍스트는 온전한 값이라
        /// 「맷집!만 뜨고 피해는 그대로」로 읽혔다. 소진성 축이 하나라도 있으면 「나중에 다시 계산」은
        /// 성립하지 않는다.</para>
        ///
        /// <para>실제 HP 감소와 <b>같은 수</b>인지로 잰다 — 상수를 따로 적으면 반감 규칙이 바뀔 때
        /// 이 테스트가 규칙이 아니라 상수를 지키게 된다.</para>
        /// </summary>
        [Test]
        public void ToughnessHalvedDamageIsWhatTheFloatingNumberShows()
        {
            var state = CreateState(toughnessReloadTurns: 3, enemyDistance: 2);
            var monster = FirstMonster(state);
            var damageEvents = new List<EffectResultEvent>();
            state.EffectResolved += resultEvent =>
            {
                if (resultEvent.Kind == EffectKind.Damage &&
                    string.Equals(resultEvent.TargetUnitId, monster.Id, StringComparison.Ordinal))
                {
                    damageEvents.Add(resultEvent);
                }
            };

            AdvanceToPlayerAction(state);
            var hpBefore = monster.Combatant.Hp;
            Assert.That(state.TryPlayerAttack(monster.Coord, "A-T"), Is.True, state.LastFailureReason);
            var hpLost = hpBefore - monster.Combatant.Hp;

            Assert.That(hpLost, Is.EqualTo((AttackCardDamage + 1) / 2), "전제: 첫 타격은 반감된다.");
            Assert.That(damageEvents, Is.Not.Empty, "피해 연출 이벤트가 하나는 나야 시험이 성립한다.");
            Assert.That(damageEvents.Sum(resultEvent => resultEvent.Amount), Is.EqualTo(hpLost),
                "플로팅 숫자가 실제 HP 감소와 달랐다 — 맷집이 화면에서 사라졌다.");
        }

        [Test]
        public void ToughnessPreviewShowsHalvedDamageWithoutConsuming()
        {
            var state = CreateState(toughnessReloadTurns: 3, enemyDistance: 2);
            var monster = FirstMonster(state);
            AdvanceToPlayerAction(state);
            var card = state.ActionDeck.Hand.First(candidate => candidate.Id == "A-T");

            var display = state.GetDisplayValue(card, monster.Coord);

            Assert.That(display, Is.EqualTo((AttackCardDamage + 1) / 2),
                "미리보기도 반감 수치를 보여줘야 화면과 규칙이 일치한다.");
            Assert.That(monster.ToughnessSpent, Is.False,
                "카드 호버(미리보기)만으로 맷집이 벗겨지면 안 된다 — 소진은 실행 경로 전용.");
        }

        [Test]
        public void VulnerablePreviewShowsTheBonusDamageWithoutConsumingIt()
        {
            // 🔑 2026-09-01 #16: 「허점 같은 상태이상이 카드를 겨눌 때 실시간으로 반영돼야 한다 —
            //    하드코딩이 아니라 시스템으로」. 시스템은 이미 그 모양이다: GetDisplayValue가 집행과
            //    <b>같은 함수</b>를 부르므로 화면과 규칙이 갈라질 수 없다. 다만 <b>대상 의존</b> 보정
            //    (허점·표식)은 previewTarget이 있어야만 보인다(D-7) — 맷집 미리보기의 쌍둥이 핀이다.
            var state = CreateState(enemyDistance: 2);
            var monster = FirstMonster(state);
            AdvanceToPlayerAction(state);
            var card = state.ActionDeck.Hand.First(candidate => candidate.Id == "A-T");

            var plain = state.GetDisplayValue(card, monster.Coord);

            // ⚠️ AddDurationStatusEffect는 5인자 오버로드가 둘이라(인자 순서만 다르다) 이름+개수로는
            //    못 고른다 — 타입으로 못 박는다.
            var apply = typeof(CombatState).GetMethod(
                "AddDurationStatusEffect",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(StatusEffectKind), typeof(string), typeof(int), typeof(int), typeof(string) },
                null);
            Assert.That(apply, Is.Not.Null);
            apply.Invoke(state, new object[]
            {
                StatusEffectKind.Vulnerable, monster.Id, 2,
                StatusEffectInfo.DefaultAmount(StatusEffectKind.Vulnerable), "test"
            });

            var targeted = state.GetDisplayValue(card, monster.Coord);
            Assert.That(targeted, Is.GreaterThan(plain),
                "허점이 걸린 대상을 겨누면 미리보기 수치가 그 자리에서 올라야 한다.");

            // 🔴 대상이 없으면 이 보정은 <b>보일 수 없다</b>(D-7) — 그래서 화면 쪽 숙제는
            //    「호버 좌표가 CardPreviewTargetCoord로 들어가는가」다. 규칙은 여기서 이미 맞다.
            Assert.That(state.GetDisplayValue(card, null), Is.EqualTo(plain),
                "대상 없는 미리보기는 대상 무관분만 보여준다 — 허점은 대상이 정해져야 알 수 있다.");

            Assert.That(state.ActiveEffects.Count(effect =>
                    effect.Kind == StatusEffectKind.Vulnerable && effect.TargetUnitId == monster.Id),
                Is.EqualTo(1),
                "호버(미리보기)만으로 허점이 소모되면 안 된다 — 맷집과 같은 계약.");
        }

        [Test]
        public void ToughnessReloadsThreeFullTurnsAfterItWasSpent()
        {
            // 2026-08-20 #11(사용자 확정): 재충전은 순수 쿨다운이고, 「발동한 턴을 빼고 3턴이 지난 뒤」
            // = 4턴째 부활이다. 발동 턴 T에는 진행이 오르지 않으므로 T+1·T+2·T+3은 여전히 무르고
            // T+4에 단단해진다.
            var state = CreateState(toughnessReloadTurns: 3);
            var monster = FirstMonster(state);
            monster.ToughnessSpent = true;

            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Patrol); // T+1
            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Patrol); // T+2
            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Patrol); // T+3
            Assert.That(monster.ToughnessSpent, Is.True, "3턴이 '지나기' 전에는 아직 무르다.");

            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Patrol); // T+4
            Assert.That(monster.ToughnessSpent, Is.False, "발동 후 3턴이 지나면 4턴째에 다시 단단해진다.");
            Assert.That(monster.ToughnessReloadProgress, Is.EqualTo(0));
        }

        [Test]
        public void ToughnessReloadIgnoresWhetherThePlayerWasDetected()
        {
            // 종전 규칙(「나를 놓친 채 N턴」 연속)은 폐기됐다(#11) — 플레이어가 계속 붙어 있으면
            // 영영 재충전되지 않아 "발동 후 3턴 있다가 채워짐"과 정반대로 동작했다.
            var state = CreateState(toughnessReloadTurns: 3);
            var monster = FirstMonster(state);
            monster.ToughnessSpent = true;

            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Chase);
            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Chase);
            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Chase);
            Assert.That(monster.ToughnessSpent, Is.True);

            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Chase);
            Assert.That(monster.ToughnessSpent, Is.False, "감지 중이어도 쿨다운은 그대로 흐른다.");
        }

        // ------------------------------------------------------------------ 뒤끝

        [Test]
        public void AftermathFieldLeavesDamageFieldAtDeathCoordOnce()
        {
            var state = CreateState(onDeathEffectRef: MonsterDeathAftermath.FieldRef,
                onDeathEffectParam: "damage=2;radius=1;turns=3");
            var monster = FirstMonster(state);
            var deathCoord = monster.Coord;

            monster.Combatant.ApplyDamage(monster.Combatant.Hp);
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);

            var field = state.FieldObjects.Objects.SingleOrDefault(candidate => candidate.Position.Equals(deathCoord));
            Assert.That(field.Kind, Is.EqualTo(FieldObjectKind.FieldDamage), "죽은 자리에 독기 장판이 남는다.");
            Assert.That(field.Value, Is.EqualTo(2));
            Assert.That(field.Radius, Is.EqualTo(1));
            Assert.That(field.RemainingTurns, Is.EqualTo(3));
            Assert.That(field.SourceUnitId, Is.EqualTo(monster.Id),
                "소유자가 몬스터여야 기존 FieldDamage 핸들러가 플레이어를 때린다.");

            // 처치 수렴 지점 중복 호출에도 장판은 1개다(호루라기 래치 계약).
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);
            Assert.That(state.FieldObjects.Objects.Count(candidate => candidate.Position.Equals(deathCoord)), Is.EqualTo(1));
        }

        [Test]
        public void AftermathBlastDamagesPlayerInRadiusOnly()
        {
            var near = CreateState(onDeathEffectRef: MonsterDeathAftermath.BlastRef,
                onDeathEffectParam: "damage=3;radius=1", enemyDistance: 1);
            var nearMonster = FirstMonster(near);
            var hpBefore = near.Player.Hp;
            nearMonster.Combatant.ApplyDamage(nearMonster.Combatant.Hp);
            Invoke(near, "ResetDeadMonsterToPatrolIntent", nearMonster);
            Assert.That(near.Player.Hp, Is.EqualTo(hpBefore - 3), "반경 1 안에서 죽으면 플레이어가 3 피해를 받는다.");

            var far = CreateState(onDeathEffectRef: MonsterDeathAftermath.BlastRef,
                onDeathEffectParam: "damage=3;radius=1", enemyDistance: 2);
            var farMonster = FirstMonster(far);
            var farHpBefore = far.Player.Hp;
            farMonster.Combatant.ApplyDamage(farMonster.Combatant.Hp);
            Invoke(far, "ResetDeadMonsterToPatrolIntent", farMonster);
            Assert.That(far.Player.Hp, Is.EqualTo(farHpBefore), "반경 밖에서는 아무 일도 없다.");
        }

        [Test]
        public void AftermathDebuffAppliesStatusToPlayer()
        {
            var state = CreateState(onDeathEffectRef: MonsterDeathAftermath.DebuffRef,
                onDeathEffectParam: "kind=Poison;turns=2");
            var monster = FirstMonster(state);

            monster.Combatant.ApplyDamage(monster.Combatant.Hp);
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);

            Assert.That(PlayerHas(state, StatusEffectKind.Poison), Is.True,
                "막타 친 자(항상 플레이어)에게 저작된 디버프가 걸린다.");
        }

        // ------------------------------------------------------------------ 심술(뒤끝 · aftermath.curse)

        [Test]
        public void AftermathCurseLeavesDistinctCurseCardsInTheDeck()
        {
            var state = CreateState(onDeathEffectRef: MonsterDeathAftermath.CurseRef,
                onDeathEffectParam: $"pool={CurseA}|{CurseB}|{CurseC};count=2");
            var monster = FirstMonster(state);
            var before = InjectedCurseIds(state);

            monster.Combatant.ApplyDamage(monster.Combatant.Hp);
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);

            var injected = InjectedCurseIds(state);
            Assert.That(injected.Count - before.Count, Is.EqualTo(2), "심술은 저작된 장수만큼 남긴다.");
            Assert.That(injected.Distinct().Count(), Is.EqualTo(injected.Count),
                "서로 다른 종류로 남는다 — 같은 저주를 겹쳐 채우지 않는다.");

            // 처치 수렴 지점이 두 번 불려도 래치가 막는다(장판·폭발과 같은 계약).
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);
            Assert.That(InjectedCurseIds(state).Count, Is.EqualTo(injected.Count));
        }

        [Test]
        public void AftermathCurseOnlyLandsWithinItsAuthoredRadius()
        {
            // 🔴 저주는 <b>영구</b> 오염이라 「죽으면 무조건 2장」은 사기였다(2026-09-01 #3 사용자 확정).
            //    붙어서 잡으면 대가를 치르고, 떨어져서 잡으면 면한다 — 거리가 곧 선택지가 된다.
            var param = $"pool={CurseA}|{CurseB}|{CurseC};count=1;radius=1";

            var near = CreateState(onDeathEffectRef: MonsterDeathAftermath.CurseRef,
                onDeathEffectParam: param, enemyDistance: 1);
            var nearBefore = InjectedCurseIds(near).Count;
            KillFirstMonster(near);
            Assert.That(InjectedCurseIds(near).Count - nearBefore, Is.EqualTo(1),
                "인접해서 잡으면 저주 한 장을 받는다.");

            var far = CreateState(onDeathEffectRef: MonsterDeathAftermath.CurseRef,
                onDeathEffectParam: param, enemyDistance: 3);
            var farBefore = InjectedCurseIds(far).Count;
            KillFirstMonster(far);
            Assert.That(InjectedCurseIds(far).Count, Is.EqualTo(farBefore),
                "사거리 밖에서 잡으면 저주가 남지 않는다 — 이것이 조정의 전부다.");
        }

        [Test]
        public void AftermathCurseWithoutARadiusStaysUnlimited()
        {
            // 반경은 <b>음수 = 제한 없음</b>이 기본이다 — 기존 저작(radius 없음)이 조용히 약해지면 안 된다.
            var state = CreateState(onDeathEffectRef: MonsterDeathAftermath.CurseRef,
                onDeathEffectParam: $"pool={CurseA}|{CurseB}|{CurseC};count=1", enemyDistance: 5);
            var before = InjectedCurseIds(state).Count;

            KillFirstMonster(state);

            Assert.That(InjectedCurseIds(state).Count - before, Is.EqualTo(1),
                "radius를 저작하지 않으면 거리 제한이 없다(종전 동작).");
        }

        [Test]
        public void AftermathAnnouncementNamesWhatWasLeftBehind()
        {
            // 🔑 「뒤끝!」 한 낱말로는 무엇을 당했는지 모른다(#3 사용자 요구). 갈래마다 다른 ref가 나가고,
            //    문안은 MonsterTraitAnnouncement 한 표가 정한다 — 표현층이 자기 문자열을 들면 갈린다.
            var state = CreateState(onDeathEffectRef: MonsterDeathAftermath.CurseRef,
                onDeathEffectParam: $"pool={CurseA}|{CurseB}|{CurseC};count=1;radius=1", enemyDistance: 1);
            var announced = new List<EffectResultEvent>();
            state.EffectResolved += resultEvent =>
            {
                if (resultEvent.Kind == EffectKind.MonsterTraitTriggered)
                {
                    announced.Add(resultEvent);
                }
            };

            KillFirstMonster(state);

            var aftermath = announced.SingleOrDefault(a => a.SourceRef.StartsWith("trait.aftermath.", StringComparison.Ordinal));
            Assert.That(aftermath.SourceRef, Is.EqualTo(MonsterTraitAnnouncement.AftermathCurseRef));
            Assert.That(MonsterTraitAnnouncement.TryGetText(aftermath.SourceRef, aftermath.AppliedAmount, out var text), Is.True,
                "표에 없는 ref를 내보내면 화면에는 아무 글자도 안 뜬다.");
            Assert.That(text, Is.EqualTo("저주 부적 부여!"));
        }

        [Test]
        public void AnAftermathThatDoesNotResolveAnnouncesNothing()
        {
            // 🔴 사거리 게이트가 생기면서 「뒤끝!」만 뜨고 아무 일도 안 일어나는 화면이 가능해졌다.
            //    알리는 것은 <b>실제로 일어난 일</b>이다 — 알림과 집행이 같은 술어를 지나야 한다.
            var state = CreateState(onDeathEffectRef: MonsterDeathAftermath.CurseRef,
                onDeathEffectParam: $"pool={CurseA}|{CurseB}|{CurseC};count=1;radius=1", enemyDistance: 3);
            var announced = new List<EffectResultEvent>();
            state.EffectResolved += resultEvent =>
            {
                if (resultEvent.Kind == EffectKind.MonsterTraitTriggered)
                {
                    announced.Add(resultEvent);
                }
            };

            KillFirstMonster(state);

            Assert.That(announced.Where(a => a.SourceRef.StartsWith("trait.aftermath.", StringComparison.Ordinal)),
                Is.Empty, "성립하지 않은 뒤끝은 알리지 않는다.");
        }

        [Test]
        public void ShippingDuduriCarriesNoAftermath()
        {
            // 2026-09-04 리워크: 「심술」 삭제 확정 — 두두리는 특성 없음(삼목구·호랑 선생 선례).
            // aftermath.curse 배관 자체는 저작면이라 남는다(위의 픽스처 스위트가 계약을 계속 문다).
            var catalog = ShippingMonsterCatalogForAftermath();
            var duduri = catalog.Entries.Single(entry => entry.Id == "M008");
            Assert.That(duduri.OnDeathEffectRef, Is.Empty,
                "두두리의 심술은 2026-09-04 삭제됐다 — 뒤끝을 다시 붙이려면 사용자 확정이 먼저다.");
            Assert.That(duduri.OnDeathEffectParam, Is.Empty);
        }

        [Test]
        public void EveryCurseInjectionAnnouncesWhichCardEnteredTheDeck()
        {
            // 🔴 2026-09-01 #9: 저주가 덱에 드는 순간 <b>아무 신호도 없었다</b>. 플레이어는 몇 턴 뒤
            //    그 카드를 뽑고 나서야 알았고, 원인(때린 놈)과 결과가 화면에서 이어지지 않았다.
            //    표현층이 「무엇이 들었는지」를 보여주려면 카드 id가 신호에 실려야 한다.
            var state = CreateState(onDeathEffectRef: MonsterDeathAftermath.CurseRef,
                onDeathEffectParam: $"pool={CurseA}|{CurseB}|{CurseC};count=1;radius=1", enemyDistance: 1);
            var injected = new List<EffectResultEvent>();
            state.EffectResolved += resultEvent =>
            {
                if (resultEvent.Kind == EffectKind.StatusCardInjected)
                {
                    injected.Add(resultEvent);
                }
            };
            var before = InjectedCurseIds(state);

            KillFirstMonster(state);

            var added = InjectedCurseIds(state).Except(before).ToList();
            Assert.That(added, Has.Count.EqualTo(1), "전제: 저주 한 장이 실제로 들어갔다.");
            Assert.That(injected, Has.Count.EqualTo(1), "덱에 든 장수만큼 신호가 나간다.");
            // 🔑 신호는 <b>카드 id</b>를 싣는다 — 표현층이 그 id로 카탈로그에서 카드를 찾아 스냅샷을 띄우기 때문이다.
            Assert.That(injected[0].SourceCardId, Is.EqualTo(added[0]),
                "신호는 어떤 카드가 들었는지 말해야 한다 — 그것이 연출의 그림이다.");
            Assert.That(new[] { CurseA, CurseB, CurseC }, Does.Contain(injected[0].SourceCardId));
            Assert.That(injected[0].SourceRef, Is.EqualTo(CombatState.StatusCardInjectionRef));
        }

        [Test]
        public void AnInjectionThatFindsNoCardStaysSilent()
        {
            // 역방향 가드: 카탈로그에 없는 id는 덱을 안 바꾸므로 알릴 것도 없다.
            // (신호를 주입 <b>앞</b>에 두면 「없는 카드가 들었다」고 거짓말한다.)
            var state = CreateState();
            var injected = 0;
            state.EffectResolved += resultEvent =>
            {
                if (resultEvent.Kind == EffectKind.StatusCardInjected)
                {
                    injected++;
                }
            };

            var method = typeof(CombatState).GetMethod(
                "TryInjectStatusCard", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That((bool)method.Invoke(state, new object[] { "NOT-A-CARD" }), Is.False);
            Assert.That(injected, Is.Zero);
        }

        [Test]
        public void TheAftermathPreviewDrawsExactlyWhereTheRuleReaches()
        {
            // 🔑 2026-09-01 #3: 뒤끝 배지에 손을 얹으면 「여기서 잡으면 대가가 따른다」를 판에 그린다.
            //    그리는 칸은 <b>집행과 같은 파서</b>가 편 것이라야 한다 — 화면이 한 칸을 그리고 규칙이
            //    다른 칸에서 발동하면 그 오버레이는 없느니만 못하다.
            var state = CreateState(onDeathEffectRef: MonsterDeathAftermath.CurseRef,
                onDeathEffectParam: $"pool={CurseA}|{CurseB}|{CurseC};count=1;radius=1", enemyDistance: 3);
            var monster = FirstMonster(state);

            Assert.That(state.TryGetMonsterAftermathPreview(MonsterId, out var preview), Is.True);
            Assert.That(preview.CurseCount, Is.EqualTo(1), "무엇을 남기는지 말한다.");
            Assert.That(preview.Cells, Is.Not.Empty, "사거리가 있는 갈래는 그릴 칸이 있다.");
            Assert.That(preview.Cells, Does.Contain(monster.Coord), "제 칸을 포함한다.");
            Assert.That(
                preview.Cells.All(coord => monster.Coord.DistanceTo(coord) <= 1), Is.True,
                "저작한 반경(1)을 넘지 않는다 — 넘으면 「여기 서면 면한다」가 거짓말이 된다.");
        }

        /// <summary>
        /// 막타 디버프도 <b>그릴 칸을 낸다</b>(2026-09-04). 종전에는 반경 갈래가 장판·폭발·저주
        /// 셋뿐이라 출하 몬스터의 뒤끝이 전부 빈 목록으로 떨어졌고, 오버레이 배관이 완성돼 있는데도
        /// 화면에 아무것도 안 떴다(G1 D1). <b>편 칸</b>을 재야 그 회귀가 잡힌다 — 파서만 재면
        /// 반경을 읽고도 안 쓰는 코드가 그대로 통과한다(돌연변이 실증).
        /// </summary>
        [Test]
        public void DebuffAftermathDrawsExactlyItsAuthoredReach()
        {
            var state = CreateState(
                onDeathEffectRef: MonsterDeathAftermath.DebuffRef,
                onDeathEffectParam: "kind=Seal;turns=2;radius=2",
                enemyDistance: 3);
            var monster = FirstMonster(state);

            Assert.That(state.TryGetMonsterAftermathPreview(MonsterId, out var preview), Is.True);
            Assert.That(preview.Cells, Is.Not.Empty, "반경을 저작한 디버프 뒤끝은 그릴 칸이 있다.");
            Assert.That(preview.Cells, Does.Contain(monster.Coord));
            Assert.That(
                preview.Cells.All(coord => monster.Coord.DistanceTo(coord) <= 2), Is.True,
                "저작한 반경(2)을 넘지 않는다.");
        }

        /// <summary>반경을 안 적은 디버프는 종전대로 <b>무조건</b> 발동하고, 그래서 그릴 칸이 없다.</summary>
        [Test]
        public void DebuffAftermathWithoutARadiusStaysUnconditional()
        {
            var state = CreateState(
                onDeathEffectRef: MonsterDeathAftermath.DebuffRef,
                onDeathEffectParam: "kind=Seal;turns=2",
                enemyDistance: 6);

            Assert.That(state.TryGetMonsterAftermathPreview(MonsterId, out var preview), Is.True);
            Assert.That(
                preview.Cells, Is.Empty,
                "제한 없음을 「저 칸만」으로 그리면 거짓말이 된다 — 종전 저작의 뜻이 보존된다.");
        }

        /// <summary>
        /// 출하 사자탈이 반경을 저작하고 있는가. 이 한 줄이 빠지면 실플레이에서 뒤끝 오버레이가
        /// 다시 안 뜬다 — 배관이 아니라 <b>저작</b>이 원인이었던 것이 G1 D1의 요점이다.
        /// </summary>
        [Test]
        [Category("ShippingData")]
        public void ShippingLionMaskAuthorsAnAftermathReach()
        {
            var monsters = MonsterCatalogCsvConverter.Convert(new MonsterCatalogCsvSource(
                System.IO.File.ReadAllText(System.IO.Path.Combine(CombatCsvPaths.MonsterDirectory, "monster_catalog.csv")),
                System.IO.File.ReadAllText(System.IO.Path.Combine(CombatCsvPaths.MonsterDirectory, "monster_attack_patterns.csv")),
                System.IO.File.ReadAllText(System.IO.Path.Combine(CombatCsvPaths.MonsterDirectory, "monster_pattern_bindings.csv")),
                System.IO.File.ReadAllText(System.IO.Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_vfx_cues.csv")),
                System.IO.File.ReadAllText(System.IO.Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_sound_cues.csv")))).MonsterCatalog;

            var lionMask = monsters.Entries.FirstOrDefault(entry => entry.Id == "M004");
            Assert.That(lionMask.Id, Is.EqualTo("M004"), "요술 사자탈이 출하 카탈로그에 있어야 한다.");
            Assert.That(
                MonsterDeathAftermath.TryParse(
                    lionMask.OnDeathEffectRef, lionMask.OnDeathEffectParam, out var spec, out _),
                Is.True);
            Assert.That(
                spec.Radius, Is.GreaterThanOrEqualTo(0),
                "반경이 없으면 뒤끝 호버 오버레이가 그릴 칸이 없다 — 배관이 완성돼 있어도 화면에 안 뜬다.");
        }

        [Test]
        public void AnAftermathWithoutAReachDrawsNothing()
        {
            // 🔴 막타 디버프는 거리와 무관하다 — 그릴 자리가 없는데 그리면 「저 칸만 위험하다」로
            //    읽혀 오히려 오해를 만든다. 「없다」를 말하는 것도 정보다.
            var state = CreateState(onDeathEffectRef: MonsterDeathAftermath.DebuffRef,
                onDeathEffectParam: "kind=Blind;turns=2");

            Assert.That(state.TryGetMonsterAftermathPreview(MonsterId, out var preview), Is.True);
            Assert.That(preview.Cells, Is.Empty, "거리 조건이 없는 갈래는 칸을 그리지 않는다.");
            Assert.That(preview.StatusKind, Is.EqualTo(StatusEffectKind.Blind), "대신 무엇을 남기는지 말한다.");
        }

        [Test]
        public void AMonsterWithoutAnAftermathHasNoPreview()
        {
            var state = CreateState();
            Assert.That(state.TryGetMonsterAftermathPreview(MonsterId, out _), Is.False);
        }

        [Test]
        public void AftermathCurseDrawIsDeterministicForTheSameSituation()
        {
            // 🔴 세이브는 full-snapshot이고 전투 RNG는 무시드다 — 그래서 추첨은 순수 함수여야 한다.
            // 같은 몬스터·같은 턴이면 같은 카드가 나와야 저장·복원 뒤에도 판정이 흔들리지 않는다.
            List<string> Roll()
            {
                var state = CreateState(onDeathEffectRef: MonsterDeathAftermath.CurseRef,
                    onDeathEffectParam: $"pool={CurseA}|{CurseB}|{CurseC};count=2");
                var monster = FirstMonster(state);
                monster.Combatant.ApplyDamage(monster.Combatant.Hp);
                Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);
                // 덱 주입은 뽑을 더미를 섞으므로(DeckState.InjectIntoDrawPile) 자리 순서는 매번 다르다 —
                // 계약은 "어떤 카드가 들어왔는가"이지 "몇 번째에 꽂혔는가"가 아니다.
                return InjectedCurseIds(state).OrderBy(id => id, StringComparer.Ordinal).ToList();
            }

            Assert.That(Roll(), Is.EqualTo(Roll()), "같은 상황이면 같은 저주가 나온다.");
        }

        [Test]
        public void AftermathCurseParserGuardsThePoolAuthoring()
        {
            Assert.That(MonsterDeathAftermath.TryParse(MonsterDeathAftermath.CurseRef, "count=2", out _, out var missing),
                Is.False, "pool이 필수다.");
            Assert.That(missing, Does.Contain("pool="));

            Assert.That(MonsterDeathAftermath.TryParse(
                MonsterDeathAftermath.CurseRef, "pool=X02|X02;count=1", out _, out _), Is.False, "중복은 저작 실수다.");
            Assert.That(MonsterDeathAftermath.TryParse(
                MonsterDeathAftermath.CurseRef, "pool=X02|X09;count=3", out _, out var tooMany), Is.False,
                "풀보다 많이 뽑을 수 없다 — 같은 저주를 겹쳐 채우지 않기 때문이다.");
            Assert.That(tooMany, Does.Contain("exceeds pool size"));

            Assert.That(MonsterDeathAftermath.TryParse(
                MonsterDeathAftermath.CurseRef, "pool=X02|X09|X12;count=2", out var spec, out _), Is.True);
            Assert.That(spec.Kind, Is.EqualTo(MonsterDeathAftermathKind.Curse));
            Assert.That(spec.CursePool, Is.EqualTo(new[] { "X02", "X09", "X12" }));
            Assert.That(spec.CurseCount, Is.EqualTo(2));
        }

        // ------------------------------------------------------------------ 저작 검증(파서)

        [Test]
        public void AftermathParserRejectsUnknownRefAndBrokenParams()
        {
            Assert.That(MonsterDeathAftermath.TryParse("aftermath.unknown", "damage=1", out _, out var error), Is.False);
            Assert.That(error, Does.Contain("not registered"));

            Assert.That(MonsterDeathAftermath.TryParse(MonsterDeathAftermath.FieldRef, "radius=1;turns=3", out _, out _),
                Is.False, "field는 damage가 필수다.");
            Assert.That(MonsterDeathAftermath.TryParse(MonsterDeathAftermath.FieldRef, "damage=2;turns=0", out _, out _),
                Is.False, "turns는 양수여야 한다.");
            Assert.That(MonsterDeathAftermath.TryParse(MonsterDeathAftermath.DebuffRef, "kind=NotAKind", out _, out _),
                Is.False, "미지의 StatusEffectKind는 거부된다.");
            Assert.That(MonsterDeathAftermath.TryParse(MonsterDeathAftermath.DebuffRef, "turns=2", out _, out _),
                Is.False, "debuff는 kind가 필수다.");

            Assert.That(MonsterDeathAftermath.TryParse(
                MonsterDeathAftermath.FieldRef, "damage=2;radius=1;turns=3", out var fieldSpec, out _), Is.True);
            Assert.That(fieldSpec.Kind, Is.EqualTo(MonsterDeathAftermathKind.Field));
            Assert.That(MonsterDeathAftermath.TryParse(
                MonsterDeathAftermath.DebuffRef, "kind=Poison;turns=2", out var debuffSpec, out _), Is.True);
            Assert.That(debuffSpec.StatusKind, Is.EqualTo(StatusEffectKind.Poison));
        }

        [Category("ShippingData")]
        [Test]
        public void CatalogConverterParsesGrammarColumnsAndRejectsUnknownAftermathRef()
        {
            var monster = MonsterCatalogCsvConverter.Convert(CreateCsvSource(
                    "M001,Tester,test-melee,B001,30,6,2,1,prototype,,3,3,aftermath.field,damage=2;radius=1;turns=3\n"))
                .MonsterCatalog.Entries.Single();
            Assert.That(monster.AgitationMaxStacks, Is.EqualTo(3), "agitationMaxStacks 컬럼이 엔트리에 실린다.");
            Assert.That(monster.ToughnessReloadTurns, Is.EqualTo(3), "toughnessReloadTurns 컬럼이 엔트리에 실린다.");
            Assert.That(monster.OnDeathEffectRef, Is.EqualTo(MonsterDeathAftermath.FieldRef));

            var ex = Assert.Throws<System.ArgumentException>(() => MonsterCatalogCsvConverter.Convert(CreateCsvSource(
                "M001,Tester,test-melee,B001,30,6,2,1,prototype,,0,0,aftermath.unknown,damage=1\n")));
            Assert.That(ex.Message, Does.Contain("not registered"), "미등록 뒤끝 ref는 임포트 시점에 거부된다.");

            var legacy = MonsterCatalogCsvConverter.Convert(CreateCsvSource(
                    "M001,Tester,test-melee,B001,30,6,2,1,prototype,\n", includeGrammarColumns: false))
                .MonsterCatalog.Entries.Single();
            Assert.That(legacy.HasAgitation, Is.False, "문법 컬럼이 없는 구 CSV는 문법 없음으로 내려앉는다.");
            Assert.That(legacy.HasToughness, Is.False);
            Assert.That(legacy.HasDeathAftermath, Is.False);
        }

        // ------------------------------------------------------------------ 서스펜드 왕복

        [Test]
        public void GrammarCountersSurviveSuspendRoundTrip()
        {
            var state = CreateState(agitationMaxStacks: 3, toughnessReloadTurns: 3);
            var monster = FirstMonster(state);
            monster.AgitationStacks = 2;
            monster.ToughnessSpent = true;
            monster.ToughnessReloadProgress = 1;

            var snapshot = state.CreateSuspendSnapshot();
            var restored = CreateState(agitationMaxStacks: 3, toughnessReloadTurns: 3);
            restored.RestoreFromSuspend(snapshot);
            var restoredMonster = FirstMonster(restored);

            Assert.That(restoredMonster.AgitationStacks, Is.EqualTo(2), "약오름 스택을 잃으면 세이브 스컴이 된다.");
            Assert.That(restoredMonster.ToughnessSpent, Is.True, "재개가 맷집을 공짜로 재충전하면 안 된다.");
            Assert.That(restoredMonster.ToughnessReloadProgress, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ 처치 보상 래치(2026-09-05 #7)

        /// <summary>
        /// 처치 보상 지급 완료는 뷰의 HashSet이 아니라 상태가 기억해야 한다 — 로비로 나갔다 이어하면
        /// 그 HashSet은 비고, 시체 전부가 「방금 죽은 적」이 돼 보상이 처치 수만큼 되풀이됐다.
        /// </summary>
        [Test]
        public void RewardClaimLatchSurvivesSuspendRoundTrip()
        {
            var state = CreateState();
            var monster = FirstMonster(state);
            Assert.That(state.MarkMonsterRewardClaimed(monster.Id), Is.True);
            Assert.That(state.MarkMonsterRewardClaimed("no-such-monster"), Is.False);

            var snapshot = state.CreateSuspendSnapshot();
            var restored = CreateState();
            restored.RestoreFromSuspend(snapshot);

            Assert.That(FirstMonster(restored).RewardClaimed, Is.True, "래치가 저장을 못 건너면 이어하기마다 보상이 다시 뿌려진다.");
            Assert.That(restored.Monsters.Single(m => m.Id == monster.Id).RewardClaimed, Is.True,
                "보상 흐름이 읽는 투영(MonsterRuntimeState)에도 같은 값이 실려야 한다.");
        }

        /// <summary>
        /// 래치 필드가 없던 옛 저장은 시체 전부를 지급 완료로 본다(열려 있던 전리품 창 하나를 잃는 쪽이
        /// 이어하기마다 보상이 되풀이되는 쪽보다 낫다). 살아 있는 몬스터는 건드리지 않는다.
        /// </summary>
        [Test]
        public void LegacySnapshotWithoutRewardTrackingMarksOnlyCorpsesAsClaimed()
        {
            var deadState = CreateState();
            FirstMonster(deadState).Combatant.ApplyDamage(999);
            var deadSnapshot = deadState.CreateSuspendSnapshot();
            deadSnapshot.TracksRewardClaims = false;
            deadSnapshot.Monsters.ForEach(m => m.RewardClaimed = false);
            var restoredDead = CreateState();
            restoredDead.RestoreFromSuspend(deadSnapshot);
            Assert.That(FirstMonster(restoredDead).RewardClaimed, Is.True, "옛 저장의 시체는 지급 완료로 복원한다.");

            var aliveSnapshot = CreateState().CreateSuspendSnapshot();
            aliveSnapshot.TracksRewardClaims = false;
            var restoredAlive = CreateState();
            restoredAlive.RestoreFromSuspend(aliveSnapshot);
            Assert.That(FirstMonster(restoredAlive).RewardClaimed, Is.False, "살아 있는 몬스터까지 잠그면 정상 처치 보상이 사라진다.");
        }

        // ------------------------------------------------------------------ 특성 발동 알림(2026-09-01)

        [Test]
        public void AgitationGainAnnouncesTheNewStackCount()
        {
            var state = CreateState(agitationMaxStacks: 3);
            var monster = FirstMonster(state);
            var announced = CaptureTraitAnnouncements(state);

            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.TryPlayerDefend("D00"), Is.True, state.LastFailureReason);
            monster.FsmMemory.State = MonsterFsmState.Chase;
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();

            var gain = announced.SingleOrDefault(
                announcement => announcement.SourceRef == MonsterTraitAnnouncement.AgitationGainedRef);
            Assert.That(gain.SourceRef, Is.EqualTo(MonsterTraitAnnouncement.AgitationGainedRef),
                "약오름이 올랐는데 화면에는 아무 말도 없었다 — 배지 숫자만으로는 '방금 올랐다'가 안 읽힌다.");
            Assert.That(gain.AppliedAmount, Is.EqualTo(1), "알림은 오른 뒤의 총 스택을 실어야 한다.");
            Assert.That(gain.TargetUnitId, Is.EqualTo(MonsterId), "글자는 그 몬스터 위에 떠야 한다.");
        }

        [Test]
        public void AgitationAnnouncesOnlyTheDropToZeroWhileDecaying()
        {
            // 🔴 매 턴 줄어드는 동안 계속 떠들면 그건 정보가 아니라 소음이다. 스택 수는 배지가 상시로
            //    들고 있고, 알림이 말할 것은 <b>전이</b>뿐이다 — 오를 때와 0이 될 때.
            var state = CreateState(agitationMaxStacks: 3);
            var monster = FirstMonster(state);
            monster.AgitationStacks = 2;
            var announced = CaptureTraitAnnouncements(state);

            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Patrol); // 2 → 1
            Assert.That(announced.Any(a => a.SourceRef == MonsterTraitAnnouncement.AgitationClearedRef), Is.False,
                "2→1은 아직 가라앉은 것이 아니다.");

            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Patrol); // 1 → 0
            Assert.That(announced.Count(a => a.SourceRef == MonsterTraitAnnouncement.AgitationClearedRef), Is.EqualTo(1),
                "0이 되는 순간 한 번만 알린다.");

            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Patrol); // 0 유지
            Assert.That(announced.Count(a => a.SourceRef == MonsterTraitAnnouncement.AgitationClearedRef), Is.EqualTo(1),
                "이미 0인 턴에는 다시 알리지 않는다.");
        }

        [Test]
        public void SturdyAnnouncesItselfOnlyWhenItActuallyKeepsBlock()
        {
            var state = CreateState(sturdyBlock: true);
            var monster = FirstMonster(state);
            var announced = CaptureTraitAnnouncements(state);

            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Chase);
            Assert.That(announced.Any(a => a.SourceRef == MonsterTraitAnnouncement.SturdyKeptRef), Is.False,
                "방어막이 없으면 지킬 것도 없다 — '굳지도 않았는데 견고!'는 거짓말이다.");

            monster.Combatant.AddBlock(5);
            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Chase);

            var kept = announced.SingleOrDefault(a => a.SourceRef == MonsterTraitAnnouncement.SturdyKeptRef);
            Assert.That(kept.SourceRef, Is.EqualTo(MonsterTraitAnnouncement.SturdyKeptRef),
                "다른 놈이면 지금 지워졌을 방어막이 남았다 — 그 순간이 견고가 일한 순간이다.");
            Assert.That(kept.AppliedAmount, Is.EqualTo(5), "남은 방어막을 실어야 '얼마가 살아남았나'가 읽힌다.");
        }

        [Test]
        public void ToughnessAnnouncesBothAbsorbAndReload()
        {
            var state = CreateState(toughnessReloadTurns: 3, enemyDistance: 2);
            var monster = FirstMonster(state);
            var announced = CaptureTraitAnnouncements(state);

            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerAttack(monster.Coord, "A-T"), Is.True, state.LastFailureReason);
            Assert.That(announced.Any(a => a.SourceRef == MonsterTraitAnnouncement.ToughnessAbsorbedRef), Is.True,
                "반감은 숫자가 작아진 것으로만 보인다 — 무엇이 먹었는지는 이 글자가 유일한 설명이다.");

            // 🔑 재장전 <b>타이밍</b>은 다른 테스트가 이미 잠갔다. 여기서 재는 것은 「채워지는 순간
            //    말을 하는가, 그리고 한 번만 하는가」라 넉넉히 돌리고 횟수를 본다 — 턴 산수를 여기서
            //    다시 세면 타이밍 규칙이 바뀔 때마다 무관한 이 테스트가 함께 빨개진다.
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();
            state.StartPlayerTurn();
            for (var turn = 0; turn < 8; turn++)
            {
                RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Chase);
            }

            Assert.That(monster.ToughnessSpent, Is.False, "전제: 이만큼 돌면 재장전이 끝난다.");
            Assert.That(announced.Count(a => a.SourceRef == MonsterTraitAnnouncement.ToughnessReloadedRef), Is.EqualTo(1),
                "다시 단단해진 순간을 한 번만 알린다 — 배지는 준비/소진을 색으로만 가른다.");
        }

        [Test]
        public void ToughnessPreviewAnnouncesNothing()
        {
            // 🔴 소진 래치와 같은 함정: 알림을 execute 게이트 밖에 두면 카드를 손에 든 것만으로 "맷집!"이 뜬다.
            var state = CreateState(toughnessReloadTurns: 3, enemyDistance: 2);
            var monster = FirstMonster(state);
            AdvanceToPlayerAction(state);
            var announced = CaptureTraitAnnouncements(state);
            var card = state.ActionDeck.Hand.First(candidate => candidate.Id == "A-T");

            state.GetDisplayValue(card, monster.Coord);

            Assert.That(announced, Is.Empty, "미리보기는 아무것도 발동시키지 않는다.");
        }

        [Test]
        public void AftermathAnnouncesItselfAtTheCorpse()
        {
            var state = CreateState(
                onDeathEffectRef: MonsterDeathAftermath.FieldRef,
                onDeathEffectParam: "damage=3;radius=1;turns=2",
                enemyDistance: 2);
            var monster = FirstMonster(state);
            var announced = CaptureTraitAnnouncements(state);
            monster.Combatant.ApplyDamage(monster.Combatant.Hp);

            // 처치 수렴 지점 — 어느 경로로 죽든 여기를 지난다(기존 뒤끝 테스트와 같은 진입점).
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);

            // 2026-09-01 #3으로 갈래별 문안이 생겼다 — 장판은 「뒤끝!」이 아니라 「독기 장판!」이다.
            // 「누가 남겼는지 시체 자리에 뜬다 + 래치가 있어 두 번 뜨지 않는다」는 계약은 그대로다.
            Assert.That(announced.Count(a => a.SourceRef == MonsterTraitAnnouncement.AftermathFieldRef), Is.EqualTo(1),
                "장판·폭발은 무엇이 남았는지는 보여도 누가 남겼는지는 안 보인다. 래치가 있으니 두 번 뜨지도 않는다.");
            Assert.That(MonsterTraitAnnouncement.TryGetText(
                MonsterTraitAnnouncement.AftermathFieldRef, 0, out var fieldText), Is.True);
            Assert.That(fieldText, Is.EqualTo("독기 장판!"));
        }

        private static List<EffectResultEvent> CaptureTraitAnnouncements(CombatState state)
        {
            var announced = new List<EffectResultEvent>();
            state.EffectResolved += resultEvent =>
            {
                if (resultEvent.Kind == EffectKind.MonsterTraitTriggered)
                {
                    announced.Add(resultEvent);
                }
            };

            return announced;
        }

        // ------------------------------------------------------------------ 견고(방어막 수명)

        [Test]
        public void MonsterBlockClearsWhenItActsAgainWithoutSturdyTrait()
        {
            // 기본 규칙은 플레이어와 대칭이다 — 방어막은 한 사이클짜리다(2026-08-10 사용자 확정).
            var state = CreateState();
            var monster = FirstMonster(state);
            monster.Combatant.AddBlock(5);

            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Chase);

            Assert.That(monster.Combatant.Block, Is.EqualTo(0),
                "견고가 없으면 몬스터 방어막은 다시 행동하는 순간 풀린다.");
        }

        [Test]
        public void SturdyTraitKeepsMonsterBlockAcrossTurns()
        {
            // 예외는 저작으로만 생긴다 — 코드에 숨은 예외가 아니라 배지로 드러나는 특성이다.
            var state = CreateState(sturdyBlock: true);
            var monster = FirstMonster(state);
            monster.Combatant.AddBlock(5);

            RunFullTurnKeepingFsmState(state, monster, MonsterFsmState.Chase);

            Assert.That(monster.Combatant.Block, Is.EqualTo(5),
                "견고 특성은 방어막을 턴 소멸에서 지킨다 — 불가살의 '깎아라' 퍼즐이 여기 걸려 있다.");
        }

        [Test]
        public void PatternShieldGainGrantsBlockWhenTheAttackLands()
        {
            // 부여 경로(2026-08-10): 공격이 실제로 성립한 턴에만 몸을 굳힌다.
            var state = CreateState(enemyDistance: 1, patternShieldGain: 4);
            var monster = FirstMonster(state);
            var playerHpBefore = state.Player.Hp;
            Assert.That(monster.Combatant.Block, Is.EqualTo(0), "전제: 시작 시 방어막은 없다.");

            AdvanceToPlayerAction(state);
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.LessThan(playerHpBefore), "전제: 공격이 실제로 성립했다.");
            Assert.That(monster.Combatant.Block, Is.EqualTo(4),
                "shieldGain 저작은 공격이 성립한 턴에 그만큼의 방어막이 된다.");
        }

        [Test]
        public void PatternShieldSurvivesThePlayerTurnThenClearsOnTheNextMonsterAction()
        {
            // 수명의 요점: 몬스터 방어막은 <b>플레이어의 다음 턴을 버텨야</b> 의미가 있다("그 사이에
            // 깎아라"). 그리고 그 몬스터가 다시 행동하기 직전에 풀린다 — 플레이어 ClearBlock의 대칭.
            var state = CreateState(enemyDistance: 1, patternShieldGain: 4);
            var monster = FirstMonster(state);

            AdvanceToPlayerAction(state);
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();
            Assert.That(monster.Combatant.Block, Is.EqualTo(4), "전제: 이번 행동에서 굳었다.");

            state.StartPlayerTurn();
            AdvanceToPlayerAction(state);
            Assert.That(monster.Combatant.Block, Is.EqualTo(4),
                "플레이어 턴 동안에는 남아 있어야 한다 — 여기서 사라지면 깎을 대상이 없다.");

            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();
            Assert.That(monster.Combatant.Block, Is.EqualTo(4),
                "다시 행동하는 순간 지난 방어막은 풀리고, 같은 행동에서 새로 두른 4만 남는다.");
        }

        [Test]
        public void SturdyTraitIsParsedFromCatalogCsv()
        {
            var monsters = CreateCsvSource(
                $"{DefinitionId},문법꾼,test-melee,B001,30,8,0,1,,,0,0,,,TRUE",
                includeSturdyColumn: true);
            var bundle = MonsterCatalogCsvConverter.Convert(monsters);
            var entry = bundle.MonsterCatalog.Entries.Single(candidate => candidate.Id == DefinitionId);

            Assert.That(entry.HasSturdyBlock, Is.True, "sturdyBlock=TRUE가 특성으로 실려야 한다.");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// 온전한 턴 사이클을 돌리되, 계획부가 FSM 상태를 갈아치우지 못하게 몬스터 행동 직전에
        /// 원하는 감지 상태를 다시 세운다 — 감지 스코프 규칙을 결정적으로 묻기 위한 고정이다.
        /// (턴 경계 틱은 ApplyActiveEffectTurnStart 안에서 돌므로 ResolveMonsterAction 직전 값이 판정값이다.)
        /// </summary>
        private static void RunFullTurnKeepingFsmState(CombatState state, MonsterRuntime monster, MonsterFsmState fsmState)
        {
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            monster.FsmMemory.State = fsmState;
            state.ResolveMonsterAction();
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        /// <summary>처치 수렴 지점까지 태워 뒤끝을 실제로 돌린다(기존 뒤끝 테스트들과 같은 절차).</summary>
        private static void KillFirstMonster(CombatState state)
        {
            var monster = FirstMonster(state);
            monster.Combatant.ApplyDamage(monster.Combatant.Hp);
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);
        }

        /// <summary>출하 몬스터 카탈로그 — 저작 핀은 픽스처가 아니라 실제 CSV를 봐야 의미가 있다.</summary>
        private static MonsterCatalogDefinition ShippingMonsterCatalogForAftermath()
        {
            AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(
                System.IO.File.ReadAllText(CombatCsvPaths.AttackShapesCsv, System.Text.Encoding.UTF8)));
            return MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory).MonsterCatalog;
        }

        private static CombatState CreateState(
            int agitationMaxStacks = 0,
            int toughnessReloadTurns = 0,
            string onDeathEffectRef = "",
            string onDeathEffectParam = "",
            int enemyDistance = 3,
            bool sturdyBlock = false,
            int patternShieldGain = 0)
        {
            var playerCoord = new HexCoord(0, 0);
            return new CombatState(
                CombatState.CreateDemoMap(6),
                playerCoord,
                new[] { new MonsterConfig(MonsterId, new HexCoord(enemyDistance, 0), 30, definitionId: DefinitionId) },
                TestCombatConfigs.Standard(playerMaxHp: 200, enemyMaxHp: 30, actionHandSize: 4),
                cardCatalog: CreateCardCatalog(),
                monsterCatalog: new MonsterCatalogDefinition(
                    "enemy-grammar-test-catalog",
                    "Enemy Grammar Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            DefinitionId,
                            "문법꾼",
                            "test-melee",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: 30,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern(
                                    "AT91",
                                    "몸통 박치기",
                                    range: 1,
                                    areaRadius: 0,
                                    damage: 3,
                                    shieldGain: patternShieldGain),
                            },
                            agitationMaxStacks: agitationMaxStacks,
                            toughnessReloadTurns: toughnessReloadTurns,
                            onDeathEffectRef: onDeathEffectRef,
                            onDeathEffectParam: onDeathEffectParam,
                            sturdyBlock: sturdyBlock)
                    }));
        }

        private static CardCatalogDefinition CreateCardCatalog()
        {
            return new CardCatalogDefinition(
                "enemy-grammar-test",
                "Enemy grammar test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        "D00", "방어의 기초", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 3, "self", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        "A-T", "시험 타격", CardCategory.Action, CardEffectType.Attack,
                        1, 5, AttackCardDamage, "enemy_in_range", status: CardCatalogStatus.Approved),
                    // 심술이 뽑는 저주 셋. includeInGameplayDecks=false여야 주입 대상이 된다(TryInjectStatusCard 계약).
                    CurseEntry(CurseA), CurseEntry(CurseB), CurseEntry(CurseC),
                });
        }

        /// <summary>인라인 CSV 컨버터 픽스처(MonsterCatalogCsvConverterTests 선례) — VFX/사운드 큐는 출하본을 읽는다.</summary>
        private static MonsterCatalogCsvSource CreateCsvSource(
            string monsterRow,
            bool includeGrammarColumns = true,
            bool includeSturdyColumn = false)
        {
            var header = includeGrammarColumns
                ? "monsterId,displayName,archetype,behaviorProfileRef,hp,detectionRange,movePerTurn,attackSpeed,status,visualPrefabPath,agitationMaxStacks,toughnessReloadTurns,onDeathEffectRef,onDeathEffectParam"
                    + (includeSturdyColumn ? ",sturdyBlock" : string.Empty) + "\n"
                : "monsterId,displayName,archetype,behaviorProfileRef,hp,detectionRange,movePerTurn,attackSpeed,status,visualPrefabPath\n";
            return new MonsterCatalogCsvSource(
                header + monsterRow,
                "patternId,displayName,range,damage,effectRef,targeting,vfxCueId,shapeId,statusEffects,statusEffectDurationTurns,cooldownTurns\n" +
                "A001,Basic,1,3,attack.damage,player_in_range,V001,single,,2,0\n",
                "monsterId,patternId,order,enabled,overrideWeight,designerNote\n" +
                "M001,A001,1,true,,\n",
                System.IO.File.ReadAllText(System.IO.Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_vfx_cues.csv")),
                System.IO.File.ReadAllText(System.IO.Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_sound_cues.csv")));
        }

        private static CardCatalogEntry CurseEntry(string id)
        {
            return new CardCatalogEntry(
                id, id, CardCategory.Action, CardEffectType.Attack,
                1, 0, 0, "self",
                status: CardCatalogStatus.Approved,
                includeInGameplayDecks: false);
        }

        /// <summary>
        /// 덱(뽑을 더미·손패·버린 더미)에 들어온 저주 카드 목록. 주입 카드의 <c>Id</c>는 런타임 인스턴스
        /// id라 카탈로그 id와 다르다 — 그래서 카탈로그 id(<c>Card.Id</c>)로 센다.
        /// </summary>
        private static List<string> InjectedCurseIds(CombatState state)
        {
            var curses = new[] { CurseA, CurseB, CurseC };
            return state.ActionDeck.DrawPile
                .Concat(state.ActionDeck.Hand)
                .Concat(state.ActionDeck.DiscardPile)
                .Select(card => card.Id)
                .Where(id => curses.Contains(id))
                .ToList();
        }

        private static bool PlayerHas(CombatState state, StatusEffectKind kind)
        {
            return state.ActiveEffects.Any(effect =>
                effect.Kind == kind && effect.TargetUnitId == "player" && !effect.IsExpired);
        }

        private static object Invoke(CombatState state, string methodName, params object[] args)
        {
            var method = typeof(CombatState).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, $"{methodName} not found");
            return method.Invoke(state, args);
        }

        /// <summary>공개 Monsters는 스냅샷 뷰라 런타임 조작이 안 된다 — 사설 monsters 리플렉션(전 스위트 선례).</summary>
        private static MonsterRuntime FirstMonster(CombatState state)
        {
            var monsters = (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            return monsters[0];
        }
    }
}

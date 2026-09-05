using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 🔴 <b>턴 경계(<c>BeginNextOverallTurn</c>) 계약 중 <u>테스트가 없던 것</u></b>(T7-2).
    ///
    /// 이 파일은 24단계 판정표(계획 §7 T7-1)를 <b>돌연변이로 전수 확인한 뒤</b> 남은 잔여물이다.
    /// 판정한 계약을 하나씩 뒤집어 보니 대부분은 이미 잡히고 있었다:
    /// <list type="bullet">
    /// <item>예약 속박이 백스톱을 우회한다 → <c>CleanseCardsTests.BookedImmobilizeIgnoresTheControlStatusImmunityWindow</c></item>
    /// <item>약오름 적립 뒤에 신호를 리셋한다 → <c>EnemyGrammarTests.AgitationStacksWhenDetectingAndDefendCardUsed</c></item>
    /// <item>방어도 소거 뒤에 필드 틱 → <c>TurnSequenceTests.TurnStartFieldDamageResolvesAfterPreviousBlockClears</c></item>
    /// <item>정찰 공개 소거 뒤에 시야 갱신 → <c>TurnSequenceTests.ScoutRevealExpiresToHintedOnNextPlayerTurnOutsidePlayerSight</c></item>
    /// </list>
    ///
    /// ⚠️ 단언은 <b>관찰 가능한 결과</b>로 쓴다. 「단계가 이 순서로 불렸는가」를 재는 테스트는 배열을
    /// 배열과 대조하는 것에 불과해 규칙이 아니라 배치만 지킨다.
    /// </summary>
    public sealed class TurnBoundaryContractTests
    {
        /// <summary>
        /// 🔴 <b>예고 수치는 이 경계에서 걸린 강화를 <u>이미</u> 반영한다</b>(R-8 「예고한 대로만 실행」).
        ///
        /// D02 「보호구역 안에서 도발」은 다음 턴 적 강화를 <b>예약</b>하고 그 예약은 이 경계에서
        /// 소진된다. 플레이어는 그 직후 이동 페이즈에서 예고를 보고 계획을 세우므로, 예고가 강화 전
        /// 수치를 말하면 <b>맞고 나서야 더 아팠다는 걸 안다</b> — 이 게임이 파는 약속이 정확히 그
        /// 반대다. 예고 산출과 집행이 <c>ResolveMonsterAttackDamageToPlayer</c> 하나를 공유하는 것이
        /// 근거이고, 이 테스트는 그 공유가 <b>턴 경계를 넘어서도</b> 성립하는지를 잰다.
        ///
        /// 🔎 7-1에서는 이걸 「소진이 예고 갱신보다 앞이어야 한다」는 <b>순서</b> 계약으로 판정했지만,
        /// 돌연변이로 확인해 보니 틀렸다 — <c>GetMonsterIntentPreviews</c>는 요청 시점에 계산하므로
        /// <c>RefreshMonsterIntentStep</c>과의 전후는 결과를 바꾸지 않는다. 지켜야 하는 것은 순서가
        /// 아니라 <b>「경계를 넘긴 예약이 예고에 반영된다」</b>는 결과이고, 그것을 여기서 잠근다.
        /// </summary>
        [Test]
        public void MonsterIntentPreviewAlreadyReflectsTheStrengthBookedForThisBoundary()
        {
            var withoutBooking = PreviewDamageAfterOneTurn(_ => { });
            var withBooking = PreviewDamageAfterOneTurn(state => state.ScheduleProvokeStrength(100, 3));

            Assert.That(withoutBooking, Is.GreaterThan(0), "전제: 강화 없이도 예고가 피해 수치를 말한다.");
            Assert.That(
                withBooking, Is.GreaterThan(withoutBooking),
                "이 경계에서 강화가 걸렸는데 예고 수치가 그대로다 — 플레이어가 맞고 나서야 알게 된다.");
        }

        /// <summary>
        /// 🔴 <b>피해 무효(D02·D05)는 <u>몬스터 행동 한 번</u>에서 끝난다</b> — 다음 턴 공격은 정상적으로 아프다.
        ///
        /// 이 플래그는 예약(booking)과 <b>수명이 다르다</b>: 예약은 턴 경계에서 소진되고, 이 플래그는
        /// <b>행동 경계</b>에서 리셋된다(<c>PendingEffects.ResetPerMonsterAction</c>). T6에서 둘을 한
        /// 타입에 넣으면서 <b>섞지 않는 것</b>이 설계의 핵심이었고, 이 테스트가 그 분리를 잠근다 —
        /// 리셋이 빠지거나 예약 소진 쪽에 섞이면 방어 카드 한 장이 <b>전투 내내 무적</b>이 된다.
        ///
        /// 🔑 7-1이 「<c>ResetPerMonsterAction</c>의 경계 안 위치는 순서 무관」으로 판정한 근거가
        /// 「경계 안에서 아무도 이 값을 읽지 않는다」였다. 그 판정이 성립하려면 <b>경계당 정확히 한 번</b>
        /// 리셋되면 된다는 뜻이고, 관찰 가능한 형태가 바로 이것이다(T7-5, 계획 §7).
        /// </summary>
        [Test]
        public void DamageNullificationCoversExactlyOneMonsterActionAndNotTheNextTurn()
        {
            var state = CombatStateFixture.Arena(2).WithEnemyEastAt(1).Build();

            // 1턴: 무효 창을 켠 채 몬스터 행동 → 피해 0.
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True, state.LastFailureReason);
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            PendingEffectsProbe.NullifyIncomingDamageThisMonsterAction(state);
            var hpBeforeNullified = state.Player.Hp;
            state.ResolveMonsterAction();
            Assert.That(state.Player.Hp, Is.EqualTo(hpBeforeNullified), "전제: 무효 창이 이번 행동을 막는다.");

            // 2턴: 아무것도 안 켠 채 몬스터 행동 → 다시 아파야 한다.
            var hpBeforeNormal = state.Player.Hp;
            AdvanceOneOverallTurn(state);

            Assert.That(
                state.Player.Hp, Is.LessThan(hpBeforeNormal),
                "다음 턴 공격까지 무효가 됐다 — 행동 경계 리셋이 빠지면 방어 카드 한 장이 전투 내내 무적이 된다.");
        }

        private static int PreviewDamageAfterOneTurn(System.Action<CombatState> book)
        {
            var state = CombatStateFixture.Arena(2).WithEnemyEastAt(1).Build();
            book(state);
            AdvanceOneOverallTurn(state);

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true).FirstOrDefault();
            Assert.That(preview.MonsterId, Is.Not.Null.And.Not.Empty, "전제: 예고가 하나는 나온다.");
            return preview.AttackPatternDamage;
        }

        private static void AdvanceOneOverallTurn(CombatState state)
        {
            var turn = state.OverallTurnNumber;
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True, state.LastFailureReason);
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();
            Assert.That(state.OverallTurnNumber, Is.GreaterThan(turn), "전제: 턴이 실제로 넘어갔다.");
        }
    }
}

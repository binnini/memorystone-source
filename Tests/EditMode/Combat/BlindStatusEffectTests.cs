using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 실명(Blind, C-1) 계약. 시야 산출은 <c>GetEffectivePlayerVisionRange</c> 한 곳에서만 일어나므로
    /// 계산 자체는 싸고, 실제 신규 과제는 <b>갱신 배선</b>이다: 안개(visibilityRuntime의 임시 공개 집합)는
    /// <c>RefreshPlayerVision</c>이 돌아야만 바뀌므로, 부여·만료·정화 세 지점 중 하나라도 빠지면
    /// "시야 값은 줄었는데 화면은 그대로"인 상태가 된다. 아래 테스트는 전부 값이 아니라 <b>보이는 셀</b>로 잰다.
    /// </summary>
    public sealed class BlindStatusEffectTests
    {
        private const int BaseVisionRange = 2;

        [Test]
        public void BaselineVisionRevealsCellsWithinConfiguredRange()
        {
            var state = CreateState();

            Assert.That(state.GetVisibility(new HexCoord(BaseVisionRange, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(state.GetVisibility(new HexCoord(BaseVisionRange + 1, 0)), Is.Not.EqualTo(HexCellVisibility.Revealed));
        }

        [Test]
        public void ApplyingBlindShrinksVisionImmediatelyWithoutWaitingForATurnBoundary()
        {
            var state = CreateState();
            var edge = new HexCoord(BaseVisionRange, 0);
            Assert.That(state.GetVisibility(edge), Is.EqualTo(HexCellVisibility.Revealed));

            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Blind, turns: 2, amount: 1), Is.True);

            Assert.That(state.ActiveEffects.Single().Kind, Is.EqualTo(StatusEffectKind.Blind));
            Assert.That(
                state.GetVisibility(edge),
                Is.Not.EqualTo(HexCellVisibility.Revealed),
                "실명은 부여 즉시 안개를 다시 굳혀야 한다 — 다음 이동/턴 전환까지 기다리면 안 된다.");
            Assert.That(
                state.GetVisibility(new HexCoord(BaseVisionRange - 1, 0)),
                Is.EqualTo(HexCellVisibility.Revealed),
                "시야가 1만 줄어야 한다.");
        }

        [Test]
        public void BlindExpiryRestoresVision()
        {
            var state = CreateState();
            var edge = new HexCoord(BaseVisionRange, 0);
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Blind, turns: 2, amount: 1), Is.True);
            Assert.That(state.GetVisibility(edge), Is.Not.EqualTo(HexCellVisibility.Revealed));

            // 적용 턴의 첫 틱은 건너뛰므로 duration 2는 세 번째 경계에서 만료된다(중독과 같은 규약).
            AdvanceTurn(state);
            AdvanceTurn(state);
            AdvanceTurn(state);

            Assert.That(state.ActiveEffects, Is.Empty);
            Assert.That(
                state.GetVisibility(edge),
                Is.EqualTo(HexCellVisibility.Revealed),
                "만료 시에도 안개를 다시 굳혀야 한다.");
        }

        [Test]
        public void CleansingBlindRestoresVision()
        {
            var state = CreateState();
            var edge = new HexCoord(BaseVisionRange, 0);
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Blind, turns: 5, amount: 1), Is.True);
            Assert.That(state.GetVisibility(edge), Is.Not.EqualTo(HexCellVisibility.Revealed));

            Assert.That(Cleanse(state), Is.EqualTo(1), "실명은 정화 대상이다.");

            Assert.That(state.ActiveEffects, Is.Empty);
            Assert.That(state.GetVisibility(edge), Is.EqualTo(HexCellVisibility.Revealed));
        }

        [Test]
        public void ReapplyingBlindExtendsDurationButDoesNotStackTheVisionPenalty()
        {
            var state = CreateState();
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Blind, turns: 2, amount: 1), Is.True);
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Blind, turns: 4, amount: 1), Is.True);

            var effect = state.ActiveEffects.Single();
            Assert.That(effect.RemainingTurns, Is.EqualTo(4), "재중첩은 턴을 연장한다.");
            Assert.That(
                effect.Amount,
                Is.EqualTo(1),
                "−1 실명이 두 번 겹쳐 −2가 되면 안 된다 — 강화판은 저작 amount로만 만든다.");
            Assert.That(
                state.GetVisibility(new HexCoord(BaseVisionRange - 1, 0)),
                Is.EqualTo(HexCellVisibility.Revealed));
        }

        [Test]
        public void StrongerBlindStacksByTakingTheLargerAmountAndClampsVisionAtZero()
        {
            var state = CreateState();
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Blind, turns: 2, amount: 1), Is.True);
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Blind, turns: 2, amount: 9), Is.True);

            Assert.That(state.ActiveEffects.Single().Amount, Is.EqualTo(9));
            // 시야는 0으로 클램프되므로 서 있는 칸만 보인다(음수 반경으로 새지 않는다).
            Assert.That(state.GetVisibility(state.PlayerCoord), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(state.GetVisibility(new HexCoord(1, 0)), Is.Not.EqualTo(HexCellVisibility.Revealed));
        }

        private static int Cleanse(CombatState state)
        {
            var method = typeof(CombatState).GetMethod(
                "CleanseStatusEffects",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (int)method.Invoke(state, new object[] { state.Player.Id, string.Empty });
        }

        // 몬스터 없는 직선 회랑: 이 테스트가 재는 것은 오직 "플레이어 시야가 어디까지 밝히는가"이므로
        // 몬스터 정찰/추격이 안개에 끼어들 여지를 아예 없앤다.
        private static CombatState CreateState()
        {
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 0, 1, 0, 3, 1, 4, BaseVisionRange);
            var map = new HexMapData(
                Enumerable.Range(-1, 8).Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false)));
            return new CombatState(map, new HexCoord(0, 0), System.Array.Empty<MonsterConfig>(), config);
        }

        private static void AdvanceTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
        }
    }
}

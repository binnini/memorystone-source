using System;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// P1.5(D-6) 계약: <c>status_effects.csv</c>의 동작 컬럼이 죽은 저작이 아니라 실제로 소비된다.
    ///
    /// 이 스위트의 목적은 **"배선했지만 동작은 안 변했다"를 증명하는 것**이다. 배선 전에 현행 동작
    /// (스택 병합·틱 타이밍·값 소비)을 여기에 고정하고, 배선 후 같은 스위트가 그대로 green이면
    /// 리팩토링이 게임을 바꾸지 않았다는 뜻이다. 그래서 단언은 전부 **관찰 가능한 결과**
    /// (인스턴스 수·수치·이동 범위·체력)로 쓰고 내부 술어를 직접 부르지 않는다.
    /// </summary>
    [Category("ShippingData")]
    public sealed class StatusEffectDataModelContractTests
    {
        private StatusEffectCatalogDefinition savedCatalog;

        [SetUp]
        public void SetUp()
        {
            // 프로바이더는 프로세스 전역이라 앞서 돈 테스트의 잔재를 끊는다. 이 스위트는 폴백 switch가
            // 아니라 **실제 출하 CSV**로 돌아야 의미가 있다 — 저작이 소비된다는 것이 계약이므로.
            savedCatalog = StatusEffectCatalogProvider.Active;
            StatusEffectCatalogProvider.Active = StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);
        }

        [TearDown]
        public void TearDown()
        {
            StatusEffectCatalogProvider.Active = savedCatalog;
        }

        // ---------------------------------------------------------------- stackPolicy

        /// <summary>
        /// 현행 두 정책은 모두 인스턴스를 하나로 병합한다. 종류를 늘려도 activeEffects가 불어나지
        /// 않는다는 것이 세이브 크기·HUD 아이콘 개수의 전제다.
        /// </summary>
        [TestCase(StatusEffectKind.Immobilize)]
        [TestCase(StatusEffectKind.Poison)]
        [TestCase(StatusEffectKind.Stun)]
        [TestCase(StatusEffectKind.Slow)]
        [TestCase(StatusEffectKind.Rupture)]
        [TestCase(StatusEffectKind.Reflect)]
        [TestCase(StatusEffectKind.Agility)]
        [TestCase(StatusEffectKind.Strength)]
        [TestCase(StatusEffectKind.Blind)]
        public void ReapplyingAnyKindKeepsExactlyOneInstance(StatusEffectKind kind)
        {
            var state = CreateState();

            Assert.That(state.DebugApplyStatusToPlayer(kind, turns: 2, amount: 1), Is.True);
            Assert.That(state.DebugApplyStatusToPlayer(kind, turns: 3, amount: 1), Is.True);

            Assert.That(
                state.ActiveEffects.Count(effect => effect.Kind == kind),
                Is.EqualTo(1),
                $"{kind}: 재적용은 하나로 병합돼야 한다(maxStacks 1).");
        }

        [TestCase(StatusEffectKind.Poison, 3, 4, 7, TestName = "AdditiveStacking_Poison")]
        [TestCase(StatusEffectKind.Rupture, 2, 5, 7, TestName = "AdditiveStacking_Rupture")]
        public void AddPolicyKindsSumTheirAmounts(StatusEffectKind kind, int first, int second, int expected)
        {
            var state = CreateState();

            Assert.That(state.DebugApplyStatusToPlayer(kind, turns: 2, amount: first), Is.True);
            Assert.That(state.DebugApplyStatusToPlayer(kind, turns: 2, amount: second), Is.True);

            Assert.That(state.ActiveEffects.Single(effect => effect.Kind == kind).Amount, Is.EqualTo(expected));
        }

        [TestCase(StatusEffectKind.Slow, 1, 4, 4, TestName = "RefreshStacking_Slow")]
        [TestCase(StatusEffectKind.Blind, 2, 1, 2, TestName = "RefreshStacking_Blind")]
        [TestCase(StatusEffectKind.Agility, 3, 2, 3, TestName = "RefreshStacking_Agility")]
        public void RefreshDurationKindsKeepTheLargerAmount(StatusEffectKind kind, int first, int second, int expected)
        {
            var state = CreateState();

            Assert.That(state.DebugApplyStatusToPlayer(kind, turns: 2, amount: first), Is.True);
            Assert.That(state.DebugApplyStatusToPlayer(kind, turns: 2, amount: second), Is.True);

            Assert.That(state.ActiveEffects.Single(effect => effect.Kind == kind).Amount, Is.EqualTo(expected));
        }

        [Test]
        public void ReapplyingExtendsButNeverShortensTheRemainingDuration()
        {
            var state = CreateState();

            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Slow, turns: 5, amount: 1), Is.True);
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Slow, turns: 2, amount: 1), Is.True);

            Assert.That(
                state.ActiveEffects.Single(effect => effect.Kind == StatusEffectKind.Slow).RemainingTurns,
                Is.EqualTo(5),
                "짧은 재적용이 남은 턴을 깎아 내리면 안 된다.");
        }

        // ---------------------------------------------------------------- expirePolicy

        /// <summary>
        /// TurnStartAfterTick(중독)은 적용 턴 유예를 받지 않고, TurnEnd(그 외)는 받는다.
        /// 이 차이는 예전에 <c>effect.Kind != Poison</c> 하드코딩이었다.
        /// </summary>
        [Test]
        public void TickBasedKindTicksOnTheBoundaryWhilePresenceKindsGetTheApplicationTurnGrace()
        {
            var state = CreateState();
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Poison, turns: 2, amount: 3), Is.True);
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Slow, turns: 2, amount: 1), Is.True);
            var hpBefore = state.Player.Hp;

            AdvanceTurn(state);

            // 적용 턴의 첫 틱은 SkipNextTick으로 건너뛴다 — 두 종류 모두 아직 살아 있다.
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "적용 턴의 틱은 건너뛴다.");
            Assert.That(state.ActiveEffects.Select(effect => effect.Kind), Does.Contain(StatusEffectKind.Poison));
            Assert.That(state.ActiveEffects.Select(effect => effect.Kind), Does.Contain(StatusEffectKind.Slow));

            AdvanceTurn(state);

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - 3), "중독은 다음 경계에서 실제로 아프다.");
        }

        // ---------------------------------------------------------------- valueMode

        /// <summary>
        /// 이동 범위는 이제 종류가 아니라 축(MoveRangeBonus / MoveRangePenalty)으로 합산된다.
        /// 두 축이 같은 식에서 반대 부호로 만나는지 관찰 가능한 결과로 고정한다.
        /// </summary>
        [Test]
        public void MoveRangeAxesCancelEachOtherThroughTheSameFormula()
        {
            var baseline = CreateState();
            var baseRange = EffectiveMoveRange(baseline);

            var slowed = CreateState();
            Assert.That(slowed.DebugApplyStatusToPlayer(StatusEffectKind.Slow, turns: 3, amount: 1), Is.True);
            Assert.That(EffectiveMoveRange(slowed), Is.EqualTo(baseRange - 1));

            var both = CreateState();
            Assert.That(both.DebugApplyStatusToPlayer(StatusEffectKind.Slow, turns: 3, amount: 1), Is.True);
            Assert.That(both.DebugApplyStatusToPlayer(StatusEffectKind.Agility, turns: 3, amount: 1), Is.True);
            Assert.That(
                EffectiveMoveRange(both),
                Is.EqualTo(baseRange),
                "둔화(−)와 민첩(+)은 같은 축이라 상쇄돼야 한다.");
        }

        [Test]
        public void VisionPenaltyAxisShrinksPlayerVision()
        {
            var state = CreateState();
            var edge = new HexCoord(VisionRange, 0);
            Assert.That(state.GetVisibility(edge), Is.EqualTo(HexCellVisibility.Revealed));

            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Blind, turns: 2, amount: 1), Is.True);

            Assert.That(state.GetVisibility(edge), Is.Not.EqualTo(HexCellVisibility.Revealed));
        }

        /// <summary>
        /// 값 축을 쓰지 않는 종류(valueMode=None)는 같은 유닛에 걸려 있어도 다른 축의 합산에 끼어들면 안 된다.
        /// 축 기반 합산으로 바꾸면서 생길 수 있는 대표적 사고가 이것이다.
        /// </summary>
        [Test]
        public void UnrelatedStatusesDoNotLeakIntoAnotherAxisSum()
        {
            var state = CreateState();
            var baseRange = EffectiveMoveRange(state);

            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Rupture, turns: 3, amount: 5), Is.True);
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Blind, turns: 3, amount: 2), Is.True);

            Assert.That(
                EffectiveMoveRange(state),
                Is.EqualTo(baseRange),
                "파열(방어도 축)·실명(시야 축)이 이동 범위를 건드리면 축 분류가 샌 것이다.");
        }

        // ---------------------------------------------------------------- 저작 값 공간

        /// <summary>
        /// 파서가 모르는 값을 거부하는지 — 이 계층이 "저작으로 규칙을 깨는" 경로를 막는 유일한 장치다.
        /// </summary>
        [Test]
        public void UnknownColumnValuesAreRejectedInsteadOfSilentlyIgnored()
        {
            Assert.That(
                () => StatusEffectCatalogCsv.ConvertText(Row(valueMode: "MakeUpAnEffect")),
                Throws.TypeOf<ArgumentException>(),
                "소비 코드가 없는 valueMode는 파싱에서 죽어야 한다.");

            Assert.That(
                () => StatusEffectCatalogCsv.ConvertText(Row(expirePolicy: "AfterMove")),
                Throws.TypeOf<ArgumentException>(),
                "AfterMove는 실제로 존재하지 않는 만료 규칙이었다 — 값 공간에서 제거됐다.");
        }

        [Test]
        public void TimingThatContradictsItsValueModeIsRejected()
        {
            Assert.That(
                () => StatusEffectCatalogCsv.ConvertText(Row(valueMode: "VisionRangePenalty", timing: "OnBlockGain")),
                Throws.TypeOf<ArgumentException>(),
                "timing은 서술 전용이지만 실제 소비 지점과 어긋난 서술은 읽는 사람을 오도한다.");
        }

        [Test]
        public void MaxStacksAboveOneIsRejectedWhileEveryPolicyMergesIntoASingleInstance()
        {
            Assert.That(
                () => StatusEffectCatalogCsv.ConvertText(Row(stackPolicy: "Add", maxStacks: "99")),
                Throws.TypeOf<ArgumentException>(),
                "'스택 99'는 인스턴스가 아니라 수치 누적을 뜻했다 — 그건 stackPolicy 'Add'가 표현한다.");
        }

        private static string Row(
            string valueMode = "MoveRangePenalty",
            string timing = "BeforeMoveRangeCalc",
            string stackPolicy = "RefreshDuration",
            string maxStacks = "1",
            string expirePolicy = "TurnEnd")
        {
            return "effectKind,displayNameKo,descriptionKo,defaultAmount,defaultDurationTurns,"
                   + "valueMode,timing,stackPolicy,maxStacks,expirePolicy\n"
                   + $"Slow,둔화,설명,1,2,{valueMode},{timing},{stackPolicy},{maxStacks},{expirePolicy}\n";
        }

        /// <summary>
        /// 이동 범위를 내부 계산식이 아니라 **실제로 갈 수 있는 칸**으로 잰다 — 직선 회랑이라
        /// 최대 도달 거리가 곧 유효 사거리다. 내부 술어를 부르면 "배선했지만 동작은 안 변했다"를
        /// 증명하지 못한다(같은 코드를 두 번 부르는 것에 불과하므로).
        /// </summary>
        private static int EffectiveMoveRange(CombatState state)
        {
            var reachable = state.GetReachablePlayerMoves();
            return reachable.Count == 0 ? 0 : reachable.Values.Max();
        }

        private const int VisionRange = 3;

        private static CombatState CreateState()
        {
            var config = new CombatConfig(20, 10, 3, 1, 4, 4, 0, 1, 0, 3, 1, 4, VisionRange);
            var map = new HexMapData(
                Enumerable.Range(-2, 12).Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false)));
            return new CombatState(map, new HexCoord(0, 0), Array.Empty<MonsterConfig>(), config);
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

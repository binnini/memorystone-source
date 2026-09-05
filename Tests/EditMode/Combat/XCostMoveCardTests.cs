using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// T5-3 X코스트 이동(전력 질주 — SpendAll 이동 카드). 계약: 이동 가능 거리의 기본값은 저작
    /// range가 아니라 <b>남은 기 전부</b>이고, 사용하면 기가 전부 소모된다. 도달 가능 칸
    /// 미리보기와 집행이 같은 GetEffectiveMoveRange를 지나므로 화면과 규칙이 갈라질 수 없다.
    /// </summary>
    public sealed class XCostMoveCardTests
    {
        [Test]
        public void SpendAllMoveReachesAsFarAsRemainingKiAndDrainsIt()
        {
            var state = CreateState(actionBudget: 4);
            var destination = new HexCoord(-3, 0);

            Assert.That(state.TryPlayerMove(destination, "M-X"), Is.True, state.LastFailureReason);

            Assert.That(state.PlayerCoord, Is.EqualTo(destination));
            Assert.That(state.ActionCostRemaining, Is.EqualTo(0),
                "SpendAll 이동은 남은 기를 전부 소모해야 한다.");
        }

        [Test]
        public void SpendAllMoveRejectsDestinationBeyondRemainingKi()
        {
            var state = CreateState(actionBudget: 2);

            Assert.That(state.TryPlayerMove(new HexCoord(-3, 0), "M-X"), Is.False,
                "남은 기(2)보다 먼 칸(3)은 도달 불가여야 한다.");
            Assert.That(state.ActionCostRemaining, Is.EqualTo(2), "실패한 이동이 기를 소모하면 안 된다.");
        }

        [Test]
        [Category("ShippingData")]
        public void ShippingSprintCardAuthorsSpendAllMovement()
        {
            var rows = CardCatalogAsset.ParseCsvText(
                File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true)));
            var sprint = rows.FirstOrDefault(row => row.Id == "M08");

            Assert.That(sprint, Is.Not.Null, "T5-3 전력 질주(M08)가 출하 CSV에 있어야 한다.");
            Assert.That(sprint.Type, Is.EqualTo("이동"));
            Assert.That(sprint.CostMode, Is.EqualTo("SpendAll"),
                "전력 질주의 본체는 SpendAll 코스트 모드다 — 이게 빠지면 평범한 0칸 이동 카드가 된다.");
        }

        // ------------------------------------------------------------------ helpers

        private static CombatState CreateState(int actionBudget)
        {
            var config = TestCombatConfigs.Standard(
                actionBudget: actionBudget, movementHandSize: 1, actionHandSize: 2);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new HexCoord(3, 0),
                config,
                cardCatalog: CreateCatalog());
        }

        private static CardCatalogDefinition CreateCatalog()
        {
            return new CardCatalogDefinition(
                "x-cost-move-test",
                "X-cost move test catalog",
                new[]
                {
                    // 전력 질주와 같은 저작: range 0 + SpendAll — 거리는 전부 남은 기에서 나온다.
                    new CardCatalogEntry(
                        "M-X", "전력 질주 테스트", CardCategory.Movement, CardEffectType.Move,
                        1, 0, 0, CardEffectRefs.MoveBasic, "reachable_hex",
                        status: CardCatalogStatus.Approved, costMode: CardCostMode.SpendAll),
                    new CardCatalogEntry(
                        "D00", "방어의 기초", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 3, CardEffectRefs.DefendBlock, "self", status: CardCatalogStatus.Approved),
                });
        }
    }
}

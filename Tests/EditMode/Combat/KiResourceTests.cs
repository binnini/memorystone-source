using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class KiResourceTests
    {
        [Test]
        public void CurrentKiAndMaxKiAliasLegacyActionCostFields()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.CurrentKi, Is.EqualTo(state.ActionCostRemaining));
            Assert.That(state.MaxKi, Is.EqualTo(state.Config.ActionBudget));
            Assert.That(state.CurrentKi, Is.EqualTo(state.MaxKi));
        }

        [Test]
        public void CombatConfigMaxKiAliasesLegacyActionBudget()
        {
            var config = CombatConfig.Default;

            Assert.That(config.MaxKi, Is.EqualTo(config.ActionBudget));
            Assert.That(config.MaxKi, Is.EqualTo(4));
        }

        [Test]
        public void MoveAndActionCardsSpendSameKiPoolAndResetNextTurn()
        {
            var state = CombatState.CreateDefaultDemo();
            var startingKi = state.CurrentKi;

            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            Assert.That(state.CurrentKi, Is.EqualTo(startingKi - 1));
            // DEC-2026-07-03-02: EndAction() → MonsterMovement → 몬스터 이동 해석 후 PlayerAction.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(state.TryPlayerDefend(), Is.True);
            Assert.That(state.CurrentKi, Is.EqualTo(startingKi - 2));
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.CurrentKi, Is.EqualTo(0));

            state.ResolveMonsterAction();

            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.CurrentKi, Is.EqualTo(state.MaxKi));
        }

        [Test]
        public void MoveFailsWhenKiIsInsufficient()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, actionBudget: 1));

            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            // DEC-2026-07-03-02: 액션 카드는 EndAction + 몬스터 이동 해석 후에야 사용 가능.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.TryPlayerDefend(), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("Not enough Ki"));
        }

        [Test]
        public void MoveCardSnapshotIsUnavailableWhenKiIsInsufficient()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, actionBudget: 1),
                cardCatalog: CreateHighKiMoveCatalog());

            var move = state.GetCombatCards()[0];

            Assert.That(move.Kind, Is.EqualTo(CombatCardKind.Move));
            Assert.That(move.KiCost, Is.EqualTo(2));
            Assert.That(move.IsUsable, Is.False);
            Assert.That(move.Status, Is.EqualTo(CombatCardStatusText.NotEnoughKi));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("Not enough Ki"));
        }

        [Test]
        public void CombatCardSnapshotExposesKiCostAlias()
        {
            var state = CombatState.CreateDefaultDemo();
            var move = state.GetCombatCards()[0];
            Assert.That(move.KiCost, Is.EqualTo(move.Cost));
            Assert.That(move.KiCost, Is.EqualTo(1));
        }

        private static CardCatalogDefinition CreateHighKiMoveCatalog()
        {
            return new CardCatalogDefinition(
                "test-high-ki-move",
                "Test high Ki move",
                new[]
                {
                    new CardCatalogEntry("high-ki-step", "High Ki Step", CardCategory.Movement, CardEffectType.Move, 2, 2, 2, "reachable_known_hex"),
                    new CardCatalogEntry("test-guard", "Guard", CardCategory.Action, CardEffectType.Defend, 1, 0, 4, "self")
                });
        }
    }
}


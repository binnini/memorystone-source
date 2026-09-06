using System;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatCardCatalogBindingTests
    {
        [Test]
        public void DefaultCardsResolveFromCatalogBoundMigrationSource()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.CardCatalog.SourceId, Is.EqualTo(DemoCardCatalog.SourceId));
            Assert.That(state.CardCatalogEvidence.IsValid, Is.True);
            CollectionAssert.AreEqual(new[] { "M01", "M02", "M03", "M04", "M05", "M06" }, state.CardCatalogEvidence.MoveDeckCardIds);
            CollectionAssert.AreEqual(new[] { "A01", "D01", "S01", "I01", "A06", "A02", "A03", "A04", "A05", "A07", "A08", "A10", "A11", "D02", "D03", "S02", "F01", "F02", "F03", "U01" }, state.CardCatalogEvidence.ActionDeckCardIds);
            Assert.That(state.CardCatalogEvidenceText, Does.Contain("MoveDeck=[M01,M02,M03,M04,M05,M06]"));
            Assert.That(state.CardCatalogEvidenceText, Does.Contain("ActionDeck=[A01,D01,S01,I01,A06,A02,A03,A04,A05,A07,A08,A10,A11,D02,D03,S02,F01,F02,F03,U01]"));
            Assert.That(state.GetCombatCards().Select(card => card.CatalogSourceId).Distinct().Single(), Is.EqualTo(state.CardCatalog.SourceId));
        }

        [Test]
        public void MoveDeckAndActionDeckRemainSeparateCatalogBackedStructures()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.MovementDeck, Is.Not.SameAs(state.ActionDeck));
            Assert.That(state.MovementDeck.Hand.All(card => card.Category == CardCategory.Movement), Is.True);
            Assert.That(state.ActionDeck.Hand.All(card => card.Category == CardCategory.Action), Is.True);
            Assert.That(state.MovementDeck.Hand.Select(card => card.Id), Is.EquivalentTo(new[] { "M01" }));
            Assert.That(state.ActionDeck.Hand.Select(card => card.Id), Is.EquivalentTo(new[] { "A01", "D01", "S01", "I01", "A06" }));
            Assert.That(state.CardCatalog.Entries.Select(entry => entry.Id), Does.Contain(DemoCardCatalog.ObjectiveInvestigateId));
            Assert.That(state.CardCatalog.Entries.Any(entry => entry.Id.StartsWith("m2-")), Is.False);
            Assert.That(state.ActiveCardCatalogIds, Does.Contain(DemoCardCatalog.ObjectiveInvestigateId));
            Assert.That(state.ActiveCardCatalogIds, Is.SupersetOf(new[] { "M01", "A01", "D01", "S01", "I01" }));
        }

        [Test]
        public void CatalogBoundCardsPreserveExistingGimmickBehavior()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                new CombatConfig(20, 10, 1, 1, 4, 4, 0, 1, 3, actionBudget: 3, actionHandSize: 5));

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.

            Assert.That(state.ValidateAttackTarget(new HexCoord(2, 0), CardIds.Sweep).IsValid, Is.False);
            Assert.That(state.ValidateAttackTarget(new HexCoord(2, 0), CardIds.DoubleHit).IsValid, Is.True);
            Assert.That(state.TryPlayerAttack(new HexCoord(2, 0), CardIds.DoubleHit), Is.True);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(6));
            Assert.That(state.GetCombatCards().Any(card => card.Id == CardIds.Sweep && !card.IsDiscarded), Is.True);
            Assert.That(state.GetCombatCards().Any(card => card.Id == CardIds.DoubleHit && card.IsDiscarded), Is.True);
        }

        [Test]
        public void CampfireFieldObjectCardIsActionDeckButPlayableDuringMovementPhase()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new HexCoord(3, 0),
                new CombatConfig(20, 10, 1, 1, 4, 4, 0, 1, 3, actionBudget: 3, actionHandSize: 18));

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.

            var campfire = state.GetCombatCards().Single(card => card.Id == CardIds.SacredLamp && !card.IsDiscarded);
            Assert.That(campfire.Kind, Is.EqualTo(CombatCardKind.FieldObject));
            Assert.That(campfire.IsUsable, Is.True);
            Assert.That(campfire.Status, Is.EqualTo("사용 가능"));
            Assert.That(campfire.PhaseAvailability, Is.EqualTo(CardUsePhase.Action));
            Assert.That(campfire.PlayMode, Is.EqualTo(CardPlayMode.ManualTarget));
            Assert.That(campfire.FieldObjectKind, Is.EqualTo(CardFieldObjectKind.ConditionalHeal));
            Assert.That(campfire.DurationTurns, Is.EqualTo(4));
            Assert.That(campfire.AreaRadius, Is.EqualTo(2));

            // A ManualTarget field object cannot be placed on the player's own tile; place it on the
            // adjacent walkable tile (1,0) within range instead.
            Assert.That(state.TryPlayerFieldObject(new HexCoord(1, 0), CardIds.SacredLamp), Is.True);

            // cards.csv: 신성한 램프(F02) 코스트 3 → actionBudget 3을 전부 소모.
            Assert.That(state.ActionCostRemaining, Is.EqualTo(0));
            Assert.That(state.LastDiscardedCard, Is.EqualTo(CombatCardKind.FieldObject));
            Assert.That(state.FieldObjects.Objects.Single().Kind, Is.EqualTo(FieldObjectKind.ConditionalHeal));
            Assert.That(state.FieldObjects.Objects.Single().Radius, Is.EqualTo(2));
            Assert.That(state.FieldObjects.Objects.Single().RemainingTurns, Is.EqualTo(4));
            Assert.That(state.GetCombatCards().Single(card => card.Id == CardIds.SacredLamp).IsDiscarded, Is.True);
        }

        [Test]
        public void CampfireFieldObjectCardRejectsActionPhaseUseBecauseItIsTorchPhaseScoped()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new HexCoord(3, 0),
                new CombatConfig(20, 10, 1, 1, 4, 4, 0, 1, 3, actionBudget: 3, actionHandSize: 18));

            var validation = state.ValidateFieldObjectTarget(state.PlayerCoord, CardIds.SacredLamp);
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.FailureReason, Does.Contain("player action").Or.Contain("No matching action card"));
        }

        [Test]
        public void MissingOrInvalidCatalogDataFailsWithReadableEvidence()
        {
            var missingActionDeck = new CardCatalogDefinition(
                "bad-catalog",
                "Bad catalog",
                new[] { new CardCatalogEntry("move-only", "Move Only", CardCategory.Movement, CardEffectType.Move, 0, 1, 1, "reachable_known_hex") });

            var evidence = missingActionDeck.CreateBindingEvidence();
            Assert.That(evidence.IsValid, Is.False);
            Assert.That(evidence.FailureReason, Does.Contain("ActionDeck"));
            Assert.That(evidence.ToEvidenceText(), Does.Contain("invalid"));

            var ex = Assert.Throws<ArgumentException>(() => new CombatState(
                CombatState.CreateDemoMap(1),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default,
                cardCatalog: missingActionDeck));
            Assert.That(ex.Message, Does.Contain("ActionDeck"));
        }
    }
}


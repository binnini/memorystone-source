using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// A13 잔혼 공격 (docs/new-cards-plan.md §11) — the first card to scale off the 소멸 더미.
    ///
    /// The pile it counts is the one the player can open and read (`GetExilePileCards`), so the count in the
    /// overlay, the `{HitCount}` printed on the card, and the hits actually delivered must be one number.
    /// </summary>
    public sealed class ExiledCardScalingTests
    {
        [Test]
        public void RemnantAttackRepeatsOncePerExiledCard()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            ExileActionCards(state, 2);

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackRemnantId), Is.True, state.LastFailureReason);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(10 - (3 * 2)), "damage 3 × 2 exiled cards.");
        }

        [Test]
        public void RemnantAttackCountsBothDecksExilePiles()
        {
            // GetExilePileCards unions the movement and action removed piles; the card must count the same set.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            ExileActionCards(state, 1);
            var movementCard = state.MovementDeck.Hand.First();
            Assert.That(state.MovementDeck.PermanentRemoveFromHand(movementCard), Is.True);

            Assert.That(state.GetExilePileCards(), Has.Count.EqualTo(2));
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackRemnantId), Is.True, state.LastFailureReason);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(10 - (3 * 2)));
        }

        [Test]
        public void RemnantAttackWithAnEmptyExilePileStillLandsOneHit()
        {
            // Floored at 1, like the other scaling modes: an attack card that resolves into nothing at all
            // would read as broken rather than as weak.
            var state = CreateState();
            AdvanceToPlayerAction(state);

            Assert.That(state.GetExilePileCards(), Is.Empty);
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackRemnantId), Is.True, state.LastFailureReason);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(10 - 3));
        }

        [Test]
        public void RemnantAttackPrintsItsRepeatCountOnTheCard()
        {
            // {HitCount} is resolved by a separate display path; a scaling mode wired only into the rules
            // would leave the card advertising "1번" while hitting three times.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            ExileActionCards(state, 3);

            var snapshot = state.GetHandCards().Single(card => card.Id == ApprovedCardCatalogFactory.AttackRemnantId);

            Assert.That(snapshot.Description, Does.Contain("(3번)"));
        }

        // --- helpers ------------------------------------------------------------------------------

        private static CombatState CreateState()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 6);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("remnant-target", new HexCoord(1, 0), 10) },
                config,
                cardCatalog: CreateCatalog());
        }

        // Mirrors the cards.csv row: cost 2 / range 1 / damage 3 / scalingMode ExiledCards.
        private static CardCatalogDefinition CreateCatalog()
        {
            var fillers = Enumerable.Range(1, 5).Select(index => new CardCatalogEntry(
                "FILLER" + index, "Filler " + index, CardCategory.Action, CardEffectType.Defend,
                1, 0, 1, CardEffectRefs.DefendBlock, "self", status: CardCatalogStatus.Approved));
            return new CardCatalogDefinition(
                "exiled-scaling-test",
                "Exiled card scaling test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.Move1HexId, "Move 1", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, CardEffectRefs.MoveBasic, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.AttackRemnantId, "잔혼 공격", CardCategory.Action, CardEffectType.Attack,
                        2, 1, 3, CardEffectRefs.AttackDamage, "living_monster_in_range",
                        description: "선택한 적에게 피해 {Damage}를 소멸된 부적 수({HitCount}번)만큼 반복합니다.",
                        scalingMode: CardScalingMode.ExiledCards, status: CardCatalogStatus.Approved)
                }.Concat(fillers));
        }

        private static void ExileActionCards(CombatState state, int count)
        {
            var doomed = state.ActionDeck.Hand
                .Where(card => card.Id.StartsWith("FILLER"))
                .Take(count)
                .ToList();
            Assert.That(doomed, Has.Count.EqualTo(count), "The fixture must hold enough filler cards to exile.");
            foreach (var card in doomed)
            {
                Assert.That(state.ActionDeck.PermanentRemoveFromHand(card), Is.True);
            }
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }
    }
}

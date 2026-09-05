using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// F04 흡수진 (docs/new-cards-plan.md §10) — the first field object that feeds back into the caster.
    ///
    /// The contract worth pinning is "피해 준 만큼", i.e. the damage that actually landed: overkill against a
    /// dying monster must not become free healing, and an empty footprint must heal nothing.
    /// </summary>
    public sealed class LifestealFieldCardTests
    {
        [Test]
        public void LifestealFieldHealsTheCasterForTheDamageItDealt()
        {
            var state = CreateState();
            state.Player.ApplyDamage(12);
            var hpBefore = state.Player.Hp;
            AddLifestealField(state, value: 2);

            AdvanceOneOverallTurn(state);

            // Two monsters inside the footprint, 2 damage each → 4 drained → 4 healed.
            Assert.That(state.Monsters.Select(monster => monster.Hp), Is.EqualTo(new[] { 8, 8 }));
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore + 4));
        }

        [Test]
        public void LifestealDrainsWhatLandedNotTheAuthoredFieldValue()
        {
            // Overkill is the interesting case: a 100-damage field against one 10 HP monster heals 10, not 100.
            // Radius 0 keeps the second monster out so the drain stays under MaxHp and the two readings differ.
            var state = CreateState();
            state.Player.ApplyDamage(12);
            state.FieldObjects.Add(new FieldObject(new HexCoord(4, 0), 0, 1, FieldObjectKind.LifestealDamage, 100, "player", "F04"));

            AdvanceOneOverallTurn(state);

            Assert.That(state.Monsters.Single(monster => monster.Id == "drain-a").Hp, Is.EqualTo(0));
            Assert.That(state.Player.Hp, Is.EqualTo(18), "Healing follows applied damage (10), not the field value (100) — which would cap out at 20.");
        }

        [Test]
        public void LifestealHealStopsAtMaxHp()
        {
            var state = CreateState();
            state.Player.ApplyDamage(2);
            AddLifestealField(state, value: 100);
            var effects = new List<EffectResultEvent>();
            state.EffectResolved += effects.Add;

            AdvanceOneOverallTurn(state);

            Assert.That(state.Player.Hp, Is.EqualTo(state.Player.MaxHp));
            Assert.That(
                effects.Single(effect => effect.Kind == EffectKind.Heal && effect.TargetUnitId == "player").AppliedAmount,
                Is.EqualTo(2),
                "The heal effect must report what landed, not the drained total.");
        }

        [Test]
        public void LifestealFieldWithNoTargetsHealsNothing()
        {
            var state = CreateState();
            state.Player.ApplyDamage(12);
            var hpBefore = state.Player.Hp;
            // Empty corner of the map: nobody inside the footprint.
            state.FieldObjects.Add(new FieldObject(new HexCoord(-3, 0), 1, 1, FieldObjectKind.LifestealDamage, 2, "player", "F04"));

            AdvanceOneOverallTurn(state);

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore));
        }

        [Test]
        public void LifestealFieldDoesNotBiteTheCasterStandingInIt()
        {
            // Same rule as 폭탄 투하(F01): a player-placed damage field spares its own caster.
            var state = CreateState();
            state.Player.ApplyDamage(12);
            var hpBefore = state.Player.Hp;
            state.FieldObjects.Add(new FieldObject(state.PlayerCoord, 1, 1, FieldObjectKind.LifestealDamage, 3, "player", "F04"));

            AdvanceOneOverallTurn(state);

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore));
        }

        [Test]
        public void LifestealCardPlacesItsOwnFieldKind()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var catalog = new CardCatalogDefinition(
                "lifesteal-card-test",
                "Lifesteal card test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.MoveBasicId, "Move", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, CardEffectRefs.MoveBasic, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    // Mirrors the cards.csv row: cost 3 / range 2 / blast-1 / damage 2 / duration 2.
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.FieldLifestealId, "흡수진", CardCategory.Action, CardEffectType.FieldObject,
                        3, 2, 2, CardEffectRefs.FieldLifesteal, "walkable_map_cell", areaRadius: 1,
                        fieldObjectKind: CardFieldObjectKind.LifestealDamage, durationTurns: 2,
                        status: CardCatalogStatus.Approved)
                });
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("drain-a", new HexCoord(2, 0), 10) },
                config,
                cardCatalog: catalog);
            state.Player.ApplyDamage(12);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            // Field objects cannot be dropped on an occupied tile, so aim beside the monster and let the
            // radius-1 footprint cover it.
            Assert.That(state.TryPlayerFieldObject(new HexCoord(1, 0), ApprovedCardCatalogFactory.FieldLifestealId), Is.True, state.LastFailureReason);

            var placed = state.FieldObjects.Objects.Single();
            Assert.That(placed.Kind, Is.EqualTo(FieldObjectKind.LifestealDamage));
            Assert.That(placed.Value, Is.EqualTo(2));
            Assert.That(placed.VisualRef, Is.EqualTo(ApprovedCardCatalogFactory.FieldLifestealId));

            var effects = new List<EffectResultEvent>();
            state.EffectResolved += effects.Add;
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            // Asserted on the effect rather than on HP: this monster is close enough to hit back, and its
            // attack would otherwise blur the drain in the player's HP delta.
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(8), "The placed field must bite on its first tick.");
            Assert.That(
                effects.Any(effect => effect.Kind == EffectKind.Heal
                    && effect.TargetUnitId == "player"
                    && effect.SourceRef == CardEffectRefs.FieldLifesteal
                    && effect.AppliedAmount == 2),
                Is.True,
                "…and drain the 2 it dealt back into the caster.");
        }

        // --- helpers ------------------------------------------------------------------------------

        // Chase range 0 parks both monsters where they spawn, and distance 4 keeps them outside every
        // shipping pattern's reach (line-3 reaches 3 since the WS-H #19 rebalance), so nothing but the
        // field touches the player.
        private static CombatState CreateState()
        {
            return new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("drain-a", new HexCoord(4, 0), 10),
                    new MonsterConfig("drain-b", new HexCoord(3, 1), 10)
                },
                TestCombatConfigs.Standard());
        }

        private static void AddLifestealField(CombatState state, int value)
        {
            state.FieldObjects.Add(new FieldObject(new HexCoord(4, 0), 1, 1, FieldObjectKind.LifestealDamage, value, "player", "F04"));
        }

        private static void AdvanceOneOverallTurn(CombatState state)
        {
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }
    }
}

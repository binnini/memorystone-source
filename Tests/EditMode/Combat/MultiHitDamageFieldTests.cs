using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// F05 콩콩탄탄 — a damage field whose tick lands more than once (DEC-2026-07-24-01, superseding
    /// DEC-2026-07-23-06's fold-into-one-hit).
    ///
    /// The point worth pinning is that the split is authored, not baked: the hits-per-tick comes from the
    /// card's <c>hitCount</c> column via <see cref="FieldObject.HitsPerTick"/>, and every field that leaves
    /// the column empty keeps ticking exactly once.
    /// </summary>
    public sealed class MultiHitDamageFieldTests
    {
        [Test]
        public void AMultiHitFieldAppliesItsDamageOncePerHitEachTick()
        {
            var state = CreateState();
            state.FieldObjects.Add(DamageField(value: 2, hitsPerTick: 2));

            AdvanceOneOverallTurn(state);

            Assert.That(state.Monsters.Single(monster => monster.Id == "bomb-a").Hp, Is.EqualTo(10 - 4),
                "2 damage twice in a single tick.");
        }

        [Test]
        public void EachHitIsItsOwnEffectSoTheTimelineCanStaggerThem()
        {
            // A doubled number on one effect would be indistinguishable from a single 4-damage hit; the
            // whole purpose of the change is 타격감, so the hits have to arrive as separate beats.
            var state = CreateState();
            state.FieldObjects.Add(DamageField(value: 2, hitsPerTick: 2));
            var effects = new List<EffectResultEvent>();
            state.EffectResolved += effects.Add;

            AdvanceOneOverallTurn(state);

            var hits = effects
                .Where(effect => effect.Kind == EffectKind.Damage && effect.TargetUnitId == "bomb-a")
                .ToList();
            Assert.That(hits, Has.Count.EqualTo(2));
            Assert.That(hits.Select(hit => hit.HitIndex), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(hits.Select(hit => hit.HitCount), Is.All.EqualTo(2));
            Assert.That(hits[0].DelaySeconds, Is.EqualTo(0f), "The lead hit keeps the tick's own timing.");
            Assert.That(hits[1].DelaySeconds, Is.GreaterThan(0f), "Follow-up hits are staggered so two hits read as two.");
        }

        [Test]
        public void AFieldThatAuthorsNoHitCountStillTicksExactlyOnce()
        {
            // Every pre-F05 field leaves hitCount empty; the importer resolves that to 1 and the field must
            // behave exactly as it did before HitsPerTick existed.
            var state = CreateState();
            state.FieldObjects.Add(DamageField(value: 2, hitsPerTick: 1));

            AdvanceOneOverallTurn(state);

            Assert.That(state.Monsters.Single(monster => monster.Id == "bomb-a").Hp, Is.EqualTo(10 - 2));
        }

        [Test]
        public void AMonsterPlacedMultiHitFieldBitesThePlayerOncePerHit()
        {
            // The player branch sits inside the same per-hit loop; a monster-sourced field is the only way
            // for a damage field to reach the player at all (a player-placed one spares its caster).
            var state = CreateState();
            var hpBefore = state.Player.Hp;
            state.FieldObjects.Add(new FieldObject(
                state.PlayerCoord, radius: 0, remainingTurns: 1, FieldObjectKind.FieldDamage,
                value: 2, sourceUnitId: "bomb-a", visualRef: "F05", hitsPerTick: 2));

            AdvanceOneOverallTurn(state);

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - 4));
        }

        [Test]
        public void TheCardAuthorsItsHitsPerTickInCsvRatherThanInTheHandler()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var catalog = new CardCatalogDefinition(
                "bounce-bomb-card-test",
                "Bounce bomb card test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Move2Hex, "Move", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    // Mirrors the cards.csv row: cost 2 / range 2 / blast-2 / damage 2 / hitCount 2 / duration 2.
                    new CardCatalogEntry(
                        CardIds.BounceBomb, "콩콩탄탄", CardCategory.Action, CardEffectType.FieldObject,
                        2, 2, 2, "walkable_map_cell", areaRadius: 2,
                        fieldObjectKind: CardFieldObjectKind.FieldDamage, durationTurns: 2, hitCount: 2,
                        status: CardCatalogStatus.Approved)
                });
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("bomb-a", new HexCoord(3, 0), 10) },
                config,
                cardCatalog: catalog);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            Assert.That(state.TryPlayerFieldObject(new HexCoord(2, 0), CardIds.BounceBomb), Is.True, state.LastFailureReason);

            var placed = state.FieldObjects.Objects.Single();
            Assert.That(placed.Kind, Is.EqualTo(FieldObjectKind.FieldDamage));
            Assert.That(placed.Value, Is.EqualTo(2));
            Assert.That(placed.HitsPerTick, Is.EqualTo(2), "hitCount is what the field reads as hits-per-tick.");
        }

        // --- helpers ------------------------------------------------------------------------------

        // Chase range 0 parks the monsters where they spawn, and distance 4 keeps them outside every
        // shipping pattern's reach (line-3 reaches 3 since the WS-H #19 rebalance), so nothing but the
        // field moves any HP.
        private static CombatState CreateState()
        {
            return new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("bomb-a", new HexCoord(4, 0), 10),
                    new MonsterConfig("bomb-b", new HexCoord(3, 1), 10)
                },
                TestCombatConfigs.Standard());
        }

        private static FieldObject DamageField(int value, int hitsPerTick)
        {
            return new FieldObject(
                new HexCoord(4, 0), radius: 0, remainingTurns: 1, FieldObjectKind.FieldDamage,
                value: value, sourceUnitId: "player", visualRef: CardIds.BounceBomb,
                hitsPerTick: hitsPerTick);
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

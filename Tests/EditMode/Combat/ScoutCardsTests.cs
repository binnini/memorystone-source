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
    /// The two P3 cards of docs/new-cards-plan.md — S03 기절초광 and S04 빙고! — which extend scouting from
    /// "reveal, then scale an effect by what was revealed" to "reveal, then control" and "reveal, then pay out
    /// all-or-nothing above a threshold".
    ///
    /// S04's threshold used to live in the behaviorParams column (D11); since the card-class track
    /// (DEC-2026-09-06-01) it is the rule constant <see cref="S04_Bingo.Threshold"/>, so these tests vary the
    /// number of revealed enemies instead of the authored token.
    /// </summary>
    public sealed class ScoutCardsTests
    {
        [Test]
        public void StunFlashStunsEveryRevealedEnemyAndSparesTheOnesOutsideTheArea()
        {
            var state = CreateState(StunFlashCatalog(durationTurns: 1));
            AdvanceToPlayerAction(state);

            // Reveal centered at (2,0), radius 1: covers (1,0) and (3,0); (0,3) is 3 away.
            Assert.That(state.TryPlayerScout(new HexCoord(2, 0), ApprovedCardCatalogFactory.ScoutStunFlashId), Is.True, state.LastFailureReason);

            Assert.That(IsStunned(state, "scout-near"), Is.True);
            Assert.That(IsStunned(state, "scout-mid"), Is.True);
            Assert.That(IsStunned(state, "scout-far"), Is.False, "A monster outside the reveal radius was never revealed, so it is never stunned.");
        }

        [Test]
        public void StunFlashUsesTheAuthoredDurationRatherThanAFixedOne()
        {
            var state = CreateState(StunFlashCatalog(durationTurns: 3));
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerScout(new HexCoord(2, 0), ApprovedCardCatalogFactory.ScoutStunFlashId), Is.True, state.LastFailureReason);

            var stun = state.ActiveEffects.First(effect => effect.TargetUnitId == "scout-near" && effect.Kind == StatusEffectKind.Stun);
            Assert.That(stun.RemainingTurns, Is.EqualTo(3), "지속 턴 comes from the duration column, not from the handler.");
            Assert.That(stun.SourceRef, Is.EqualTo(CardEffectRefs.ScoutEnemyStun));
        }

        [Test]
        public void StunFlashOnAnEmptyAreaStillResolves()
        {
            var state = CreateState(StunFlashCatalog(durationTurns: 1));
            AdvanceToPlayerAction(state);

            // (-3,0) is 4+ away from all three monsters, so the radius-1 reveal turns up nobody. Scouting
            // empty ground must still spend the card rather than fail.
            Assert.That(state.TryPlayerScout(new HexCoord(-3, 0), ApprovedCardCatalogFactory.ScoutStunFlashId), Is.True, state.LastFailureReason);

            Assert.That(state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Stun), Is.False);
        }

        [Test]
        public void BingoHealsOnlyWhenTheRevealedEnemyCountReachesTheThreshold()
        {
            // Two enemies revealed against the threshold (3): nothing.
            Assume.That(S04_Bingo.Threshold, Is.EqualTo(3), "The fixture below reveals exactly two enemies.");
            var state = CreateState(BingoCatalog());
            state.Player.ApplyDamage(9);
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerScout(new HexCoord(2, 0), ApprovedCardCatalogFactory.ScoutBingoId), Is.True, state.LastFailureReason);

            Assert.That(state.Player.Hp, Is.EqualTo(11), "2 revealed < threshold — 빙고! is all-or-nothing, not per-enemy.");
        }

        [Test]
        public void BingoHealsTheAuthoredAmountOnceTheThresholdIsMet()
        {
            // A third enemy inside the blast lifts the revealed count to the threshold; the heal is then paid in full.
            var state = CreateState(BingoCatalog(), extraMonsterAt: new HexCoord(2, 1));
            state.Player.ApplyDamage(9);
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerScout(new HexCoord(2, 0), ApprovedCardCatalogFactory.ScoutBingoId), Is.True, state.LastFailureReason);

            Assert.That(state.Player.Hp, Is.EqualTo(16), "heal=5 is paid in full, not scaled by the count.");
        }

        [Test]
        public void BingoPaysOutOncePerPlayNotPerRevealedEnemy()
        {
            var state = CreateState(BingoCatalog(), extraMonsterAt: new HexCoord(2, 1));
            state.Player.ApplyDamage(9);
            AdvanceToPlayerAction(state);
            var effects = new List<EffectResultEvent>();
            state.EffectResolved += effects.Add;

            Assert.That(state.TryPlayerScout(new HexCoord(2, 0), ApprovedCardCatalogFactory.ScoutBingoId), Is.True, state.LastFailureReason);

            Assert.That(effects.Count(effect => effect.Kind == EffectKind.Heal), Is.EqualTo(1));
            Assert.That(state.Player.Hp, Is.EqualTo(16));
        }

        [Test]
        public void ScoutIsPlayableDuringTheMovementPhase()
        {
            // Premise guard for the stun-lockout test below: scout cards are BothIfApproved, so the
            // movement phase is a legal play window in the first place.
            var state = CreateState(StunFlashCatalog(durationTurns: 1));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));

            Assert.That(state.TryPlayerScout(new HexCoord(2, 0), ApprovedCardCatalogFactory.ScoutStunFlashId), Is.True, state.LastFailureReason);
        }

        [Test]
        public void StunnedPlayerCannotScoutDuringTheMovementPhase()
        {
            // Regression: IsBlockedByStun used to gate only the action phase, so a stunned player could
            // slip a scout card in through the movement phase (BothIfApproved availability).
            var state = CreateState(StunFlashCatalog(durationTurns: 1));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            InjectPlayerStun(state);

            Assert.That(state.TryPlayerScout(new HexCoord(2, 0), ApprovedCardCatalogFactory.ScoutStunFlashId), Is.False,
                "Stunned (기절) player must not be able to scout in the movement phase either.");
            Assert.That(state.LastFailureReason, Does.Contain("기절"));
        }

        // --- helpers ------------------------------------------------------------------------------

        private static CombatState CreateState(CardCatalogDefinition catalog, HexCoord? extraMonsterAt = null)
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var monsters = new List<MonsterConfig>
            {
                new MonsterConfig("scout-near", new HexCoord(1, 0), 10),
                new MonsterConfig("scout-mid", new HexCoord(3, 0), 10),
                new MonsterConfig("scout-far", new HexCoord(0, 3), 10)
            };
            if (extraMonsterAt.HasValue)
            {
                monsters.Add(new MonsterConfig("scout-extra", extraMonsterAt.Value, 10));
            }

            return new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                monsters.ToArray(),
                config,
                cardCatalog: catalog);
        }

        // Mirrors what cards.csv authors for S03: cost 2, range 4, blast-1, duration → stun turns.
        // The reveal radius doubles as the stun radius, so narrowing the blast narrows both together.
        private static CardCatalogDefinition StunFlashCatalog(int durationTurns)
        {
            return new CardCatalogDefinition(
                "scout-stun-test",
                "Stun flash test catalog",
                new[]
                {
                    MoveEntry(),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.ScoutStunFlashId, "기절초광", CardCategory.Action, CardEffectType.Scout,
                        2, 4, 0, CardEffectRefs.ScoutEnemyStun, "walkable_map_cell", areaRadius: 1,
                        durationTurns: durationTurns, status: CardCatalogStatus.Approved)
                });
        }

        // Mirrors what cards.csv authors for S04: cost 1, range 4, blast-2, heal 5. The threshold is the class rule.
        private static CardCatalogDefinition BingoCatalog()
        {
            return new CardCatalogDefinition(
                "scout-bingo-test",
                "Bingo test catalog",
                new[]
                {
                    MoveEntry(),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.ScoutBingoId, "빙고!", CardCategory.Action, CardEffectType.Scout,
                        1, 4, 5, CardEffectRefs.ScoutEnemyCountHealThreshold, "walkable_map_cell", areaRadius: 2,
                        status: CardCatalogStatus.Approved)
                });
        }

        private static CardCatalogEntry MoveEntry()
        {
            return new CardCatalogEntry(
                ApprovedCardCatalogFactory.MoveBasicId, "Move", CardCategory.Movement, CardEffectType.Move,
                1, 2, 2, CardEffectRefs.MoveBasic, "reachable_known_hex", status: CardCatalogStatus.Approved);
        }

        private static void InjectPlayerStun(CombatState state)
        {
            ActiveEffectProbe.Registry(state).Add(new ActiveEffect(EffectType.Duration, StatusEffectKind.Stun, state.Player.Id, remainingTurns: 1, amount: 0, "test"));
        }

        private static bool IsStunned(CombatState state, string monsterId)
        {
            return state.ActiveEffects.Any(effect =>
                effect.TargetUnitId == monsterId && effect.Kind == StatusEffectKind.Stun);
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }
    }
}

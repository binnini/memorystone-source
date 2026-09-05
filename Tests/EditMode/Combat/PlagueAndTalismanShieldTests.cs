using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// The P6 pair of docs/new-cards-plan.md — A12 전염병 and D05 부적 방패.
    ///
    /// A12 is the card the plan called the hardest: the bonus damage needs the *target*, which the attack
    /// handler interface did not expose, and the contagion writes status onto units the attack never
    /// targeted, which no attack handler can express at all.
    /// </summary>
    public sealed class PlagueAndTalismanShieldTests
    {
        [Test]
        public void PlagueHitsForBaseDamageAgainstAHealthyTarget()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackPlagueId), Is.True, state.LastFailureReason);

            Assert.That(MonsterHp(state, "plague-target"), Is.EqualTo(20 - 3), "No affliction, no bonus.");
        }

        [Test]
        public void PlagueDoublesItsDamageAgainstAnAfflictedTarget()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            Inject(state, StatusEffectKind.Poison, "plague-target");

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackPlagueId), Is.True, state.LastFailureReason);

            Assert.That(MonsterHp(state, "plague-target"), Is.EqualTo(20 - 6), "damage 3 + a second {Damage} of 3.");
        }

        [Test]
        public void ABuffOnTheTargetIsNotAnAffliction()
        {
            // 강화 is what D02 grants monsters. Counting any ActiveEffect would turn the player's own
            // defensive card into a free damage bonus for A12.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            Inject(state, StatusEffectKind.Strength, "plague-target");

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackPlagueId), Is.True, state.LastFailureReason);

            Assert.That(MonsterHp(state, "plague-target"), Is.EqualTo(20 - 3));
            Assert.That(HasEffect(state, "plague-neighbour", StatusEffectKind.Strength), Is.False, "Buffs do not spread either.");
        }

        [Test]
        public void PlagueSpreadsExactlyOneDebuffOfTheTargetToOneNearbyEnemy()
        {
            // 2026-08-20 #18(사용자 확정): 전염은 <b>가진 상태이상 전부</b>가 아니라 <b>무작위 1가지</b>다.
            // 전부를 옮기던 종전 규칙은 디버프를 쌓아 둔 대상 하나가 판 전체를 오염시켰다.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            Inject(state, StatusEffectKind.Poison, "plague-target");
            Inject(state, StatusEffectKind.Slow, "plague-target");

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackPlagueId), Is.True, state.LastFailureReason);

            var spread = new[] { StatusEffectKind.Poison, StatusEffectKind.Slow }
                .Where(kind => HasEffect(state, "plague-neighbour", kind))
                .ToList();
            Assert.That(spread, Has.Count.EqualTo(1), "옮겨지는 것은 무작위 1가지뿐이다.");
            Assert.That(HasEffect(state, "plague-distant", StatusEffectKind.Poison), Is.False,
                "반경 2: 거리 3의 몬스터는 여전히 닿지 않는다.");
            // 전염 copies; the card text's "옮깁니다" is the colloquial reading of spread, not a move
            // (사용자 재확정 2026-08-20 — 복사 유지).
            Assert.That(HasEffect(state, "plague-target", StatusEffectKind.Poison), Is.True, "The original keeps its debuff.");
            Assert.That(HasEffect(state, "plague-target", StatusEffectKind.Slow), Is.True);
        }

        [Test]
        public void PlagueInfectsExactlyOneOfSeveralAdjacentEnemies()
        {
            // The contagion picks one neighbour (nearest first, random among ties), so with three equidistant
            // candidates the assertion has to be "exactly one, drawn from the candidate set" — naming a
            // specific monster would be a coin flip.
            var candidates = new[] { "plague-neighbour", "plague-neighbour-2", "plague-neighbour-3" };
            var state = CreateStateWithThreeAdjacentNeighbours();
            AdvanceToPlayerAction(state);
            Inject(state, StatusEffectKind.Poison, "plague-target");

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackPlagueId), Is.True, state.LastFailureReason);

            var infected = candidates.Where(id => HasEffect(state, id, StatusEffectKind.Poison)).ToList();
            Assert.That(infected, Has.Count.EqualTo(1), "전염 hits one adjacent enemy, not the whole neighbourhood.");
            Assert.That(candidates, Does.Contain(infected[0]));
        }

        [Test]
        public void PlagueSpreadsNothingWhenTheTargetIsClean()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackPlagueId), Is.True, state.LastFailureReason);

            Assert.That(state.ActiveEffects.Any(effect => effect.TargetUnitId == "plague-neighbour"), Is.False);
        }

        [Test]
        public void ContagionSurvivesTheTargetDying()
        {
            // The spread runs as a post-action, i.e. after damage. A lethal hit must still infect the
            // neighbours — otherwise the card gets weaker the better the hit lands.
            var state = CreateState(targetHp: 3);
            AdvanceToPlayerAction(state);
            Inject(state, StatusEffectKind.Poison, "plague-target");

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackPlagueId), Is.True, state.LastFailureReason);

            Assert.That(state.Monsters.Single(monster => monster.Id == "plague-target").IsDead, Is.True);
            Assert.That(HasEffect(state, "plague-neighbour", StatusEffectKind.Poison), Is.True);
        }

        [Test]
        public void TalismanShieldExilesOneCardAndNullifiesIncomingDamage()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var handBefore = state.ActionDeck.HandCount;
            var hpBefore = state.Player.Hp;

            Assert.That(state.TryPlayerDefend(ApprovedCardCatalogFactory.DefendTalismanShieldId), Is.True, state.LastFailureReason);

            // -1 for the played card, -1 for the exiled one.
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(handBefore - 2));
            Assert.That(state.GetExilePileCards(), Has.Count.EqualTo(1));
            Assert.That(
                state.GetExilePileCards().Single().Id,
                Is.Not.EqualTo(ApprovedCardCatalogFactory.DefendTalismanShieldId),
                "The card exiles another card, not itself.");
            Assert.That(state.Player.Block, Is.EqualTo(0), "Immunity, not block.");

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "The adjacent monster's attack must land for 0.");
        }

        [Test]
        public void TalismanShieldStillWorksWithNothingLeftToExile()
        {
            // D8-consistent: the card never refuses, it just pays no cost when the hand is otherwise empty.
            // A one-card catalog guarantees that lone card is the shield itself.
            var state = CreateStateWithShieldOnlyHand();
            AdvanceToPlayerAction(state);
            var hpBefore = state.Player.Hp;

            Assert.That(state.ActionDeck.Hand.Count, Is.EqualTo(1));
            Assert.That(state.TryPlayerDefend(ApprovedCardCatalogFactory.DefendTalismanShieldId), Is.True, state.LastFailureReason);
            Assert.That(state.GetExilePileCards(), Is.Empty);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore));
        }

        // --- helpers ------------------------------------------------------------------------------

        private static CombatState CreateState(int targetHp = 20)
        {
            // actionHandSize 6 draws the whole action catalog, so both cards under test are always in hand.
            // Chase range 0 parks every monster on its authored tile — the spread assertions depend on
            // plague-neighbour staying adjacent to the struck tile, and the WS-H #19 reach rebalance
            // changed which destinations a chasing monster prefers.
            var config = new CombatConfig(20, 20, 2, 1, 4, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 6);
            return CombatStateFixture.Arena(4)
                .WithConfig(config)
                .WithMonsters(
                    new MonsterConfig("plague-target", new HexCoord(1, 0), targetHp),
                    new MonsterConfig("plague-neighbour", new HexCoord(2, 0), 20),
                    new MonsterConfig("plague-distant", new HexCoord(4, 0), 20))
                .WithCardCatalog(CreateCatalog())
                .Build();
        }

        // (1,0) is the struck tile; (2,0), (1,1) and (2,-1) are all adjacent to it — (0,0) is the player.
        // Chase range 0 for the same reason as CreateState: the neighbours must stay adjacent.
        private static CombatState CreateStateWithThreeAdjacentNeighbours()
        {
            var config = new CombatConfig(20, 20, 2, 1, 4, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 6);
            return CombatStateFixture.Arena(4)
                .WithConfig(config)
                .WithMonsters(
                    new MonsterConfig("plague-target", new HexCoord(1, 0), 20),
                    new MonsterConfig("plague-neighbour", new HexCoord(2, 0), 20),
                    new MonsterConfig("plague-neighbour-2", new HexCoord(1, 1), 20),
                    new MonsterConfig("plague-neighbour-3", new HexCoord(2, -1), 20))
                .WithCardCatalog(CreateCatalog())
                .Build();
        }

        private static CombatState CreateStateWithShieldOnlyHand()
        {
            var config = new CombatConfig(20, 20, 2, 1, 4, 4, 3, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var catalog = new CardCatalogDefinition(
                "talisman-shield-only-test",
                "Talisman shield only catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.Move1HexId, "Move 1", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, CardEffectRefs.MoveBasic, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.DefendTalismanShieldId, "부적 방패", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 0, CardEffectRefs.DefendExileRandomNegate, "self", status: CardCatalogStatus.Approved)
                });
            return CombatStateFixture.Arena(4)
                .WithConfig(config)
                .WithMonsters(
                    new MonsterConfig("plague-target", new HexCoord(1, 0), 20))
                .WithCardCatalog(catalog)
                .Build();
        }

        // Mirrors the cards.csv rows: A12 attack.plague + SpreadStatus:1, D05 defend.exile_random_negate.
        private static CardCatalogDefinition CreateCatalog()
        {
            var fillers = Enumerable.Range(1, 4).Select(index => new CardCatalogEntry(
                "FILLER" + index, "Filler " + index, CardCategory.Action, CardEffectType.Defend,
                1, 0, 1, CardEffectRefs.DefendBlock, "self", status: CardCatalogStatus.Approved));
            return new CardCatalogDefinition(
                "plague-shield-test",
                "Plague and talisman shield test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.Move1HexId, "Move 1", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, CardEffectRefs.MoveBasic, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.AttackPlagueId, "전염병", CardCategory.Action, CardEffectType.Attack,
                        2, 1, 3, CardEffectRefs.AttackPlague, "living_monster_in_range",
                        postActions: $"{CardBehaviorMetadata.PostActionSpreadStatus}:1",
                        status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.DefendTalismanShieldId, "부적 방패", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 0, CardEffectRefs.DefendExileRandomNegate, "self", status: CardCatalogStatus.Approved)
                }.Concat(fillers));
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        private static int MonsterHp(CombatState state, string monsterId)
        {
            return state.Monsters.Single(monster => monster.Id == monsterId).Hp;
        }

        private static bool HasEffect(CombatState state, string unitId, StatusEffectKind kind)
        {
            return state.ActiveEffects.Any(effect => effect.TargetUnitId == unitId && effect.Kind == kind);
        }

        private static void Inject(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns = 2)
        {
            ActiveEffectProbe.Registry(state).Add(new ActiveEffect(EffectType.Duration, kind, unitId, remainingTurns, amount: 1, "test"));
        }
    }
}

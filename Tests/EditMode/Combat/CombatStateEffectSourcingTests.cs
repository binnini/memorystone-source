using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatStateEffectSourcingTests
    {
        [Test]
        public void SweepIsAreaRadiusOneWhileDoubleHitIsSingleTarget()
        {
            var entries = CombatCatalogFactory.CreateCardCatalog(CombatConfig.Default).Entries;

            Assert.That(entries.Single(entry => entry.Id == ApprovedCardCatalogFactory.AttackSweepId).AreaRadius, Is.EqualTo(1));
            Assert.That(entries.Single(entry => entry.Id == ApprovedCardCatalogFactory.AttackDoubleHitId).AreaRadius, Is.EqualTo(0));
        }

        [Test]
        public void AreaRadiusPropagatesThroughCardDefinition()
        {
            var entry = CombatCatalogFactory.CreateCardCatalog(CombatConfig.Default).Entries
                .Single(candidate => candidate.Id == ApprovedCardCatalogFactory.AttackSweepId);

            Assert.That(entry.ToCardDefinition("src").AreaRadius, Is.EqualTo(1));
        }

        [Test]
        public void DefendRaisesSingleBlockEffectAtPlayer()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.

            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            Assert.That(state.TryPlayerDefend(), Is.True);

            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(EffectKind.Block));
            Assert.That(events[0].Center, Is.EqualTo(state.PlayerCoord));
            Assert.That(events[0].Radius, Is.EqualTo(0));
        }

        [Test]
        public void PlayerAttackEffectTargetsClickedMonsterForPresentation()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("monster-a", new HexCoord(1, 0), 10),
                    new MonsterConfig("monster-b", new HexCoord(0, 1), 10)
                },
                CombatConfig.Default);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.

            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            Assert.That(state.TryPlayerAttack(new HexCoord(0, 1)), Is.True);

            // Per-target presentation raises one Damage event per hit; this multi-hit attack strikes the
            // clicked monster twice, each event resolving against that monster (never the player actor).
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events.Select(e => e.Kind), Has.All.EqualTo(EffectKind.Damage));
            Assert.That(events.Select(e => e.TargetUnitId), Has.All.EqualTo("monster-b"),
                "Monster-hit VFX must resolve against the damaged monster, not the player actor.");
        }

        /// <summary>
        /// 🔴 <b>반복을 보이게 하는 것은 횟수가 아니라 간격</b>이다(2026-09-02 #3 · 사용자 실플레이:
        /// "반복 공격인데 VFX·사운드가 한 번만 난다"). 히트가 <b>같은 프레임에</b> 몰리면 세 발이 한 점에
        /// 겹쳐 그림도 소리도 한 발이 된다 — 종전 플레이어·몬스터 다단이 정확히 그랬다
        /// (장판 다단 틱만 박자를 갖고 있었고, 그쪽에는 이 계약을 무는 테스트가 이미 있었다).
        ///
        /// <para>🔑 뒤 히트의 지연이 <b>사운드 큐 쿨다운(0.05초)보다 커야</b> 소리가 두 번 난다 —
        /// 숫자의 근거가 그것이라 여기서 함께 못 박는다.</para>
        /// </summary>
        [Test]
        public void MultiHitPlayerAttackStaggersItsHitsSoTwoHitsReadAsTwo()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("monster-a", new HexCoord(1, 0), 10),
                    new MonsterConfig("monster-b", new HexCoord(0, 1), 10)
                },
                CombatConfig.Default);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            Assert.That(state.TryPlayerAttack(new HexCoord(0, 1)), Is.True);

            var hits = events.Where(effect => effect.Kind == EffectKind.Damage).ToList();
            Assert.That(hits, Has.Count.EqualTo(2));
            Assert.That(hits.Select(hit => hit.HitIndex), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(hits[0].DelaySeconds, Is.EqualTo(0f), "첫 히트는 타격 그 순간이다.");
            Assert.That(hits[1].DelaySeconds, Is.GreaterThan(0.05f),
                "뒤 히트는 사운드 큐 쿨다운(0.05초)보다 뒤에 서야 소리가 두 번 난다.");
        }

        [Test]
        public void AreaPlayerAttackEffectUsesMonsterTargetFilterForPresentation()
        {
            // Sweep is a self-centred area card (radius 1 around the player), so the monster must
            // start adjacent for the attack to be valid; chase range 0 keeps it parked there.
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                new CombatConfig(20, 10, 1, 2, 4, 4, 0, 1, 3, actionBudget: 3, actionHandSize: 5));
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.

            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackSweepId), Is.True);

            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Radius, Is.EqualTo(1));
            // Per-target presentation resolves the specific hit monster's id (single monster in the area).
            Assert.That(events[0].TargetUnitId, Is.EqualTo("normal-enemy"));
        }

        [Test]
        public void MonsterAttackEffectsUsePatternSourceRefForVfxCueMatching()
        {
            var pattern = new MonsterAttackPattern(
                "A999",
                "Designer Pattern",
                1,
                1,
                2,
                statusEffects: new[] { StatusEffectKind.Stun },
                statusEffectAmount: 1);
            var catalog = new MonsterCatalogDefinition(
                "test-monsters",
                "Test Monsters",
                new[]
                {
                    new MonsterCatalogEntry(
                        "test-monster",
                        "Test Monster",
                        "test",
                        "test.behavior",
                        5,
                        1,
                        10,
                        attackPatterns: new[] { pattern })
                });
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("monster-01", new HexCoord(1, 0), 10, "test-monsters", "test-monster") },
                CombatConfig.Default,
                monsterCatalog: catalog);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.

            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(events, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(events.Select(resultEvent => resultEvent.SourceRef), Has.All.EqualTo("monster.pattern.A999"));
            Assert.That(events.Select(resultEvent => resultEvent.Radius), Has.All.EqualTo(1));
            Assert.That(events.Select(resultEvent => resultEvent.TargetUnitId), Has.All.EqualTo("player"));
        }

        [Test]
        public void NoSubscriberIsSafe()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.

            // No EffectResolved subscriber attached; resolving an action must not throw.
            Assert.DoesNotThrow(() => state.TryPlayerDefend());
        }

        [Test]
        public void BufferedEffectsAreDeferredUntilFlush()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.

            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            state.BeginEffectBuffering();
            Assert.That(state.IsBufferingEffects, Is.True);

            Assert.That(state.TryPlayerDefend(), Is.True);
            Assert.That(events, Is.Empty, "Buffered effects must not dispatch until flushed.");

            state.FlushBufferedEffects();
            Assert.That(state.IsBufferingEffects, Is.False);
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(EffectKind.Block));
        }

        [Test]
        public void FlushBufferedEffectsIsIdempotent()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.

            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            state.BeginEffectBuffering();
            Assert.That(state.TryPlayerDefend(), Is.True);
            state.FlushBufferedEffects();
            state.FlushBufferedEffects();

            Assert.That(events, Has.Count.EqualTo(1), "A second flush must not re-dispatch already-flushed effects.");
        }

        [Test]
        public void BufferedFlushPreservesUnbufferedOrderAndCount()
        {
            var direct = CollectMonsterActionEffects(buffered: false);
            var buffered = CollectMonsterActionEffects(buffered: true);

            Assert.That(buffered.Count, Is.EqualTo(direct.Count));
            CollectionAssert.AreEqual(
                direct.Select(DescribeEvent),
                buffered.Select(DescribeEvent),
                "Flushed buffered effects must match the unbuffered dispatch sequence exactly.");
        }

        private static string DescribeEvent(EffectResultEvent resultEvent)
        {
            return $"{resultEvent.Kind}|{resultEvent.SourceRef}|{resultEvent.TargetUnitId}|{resultEvent.AppliedAmount}";
        }

        private static List<EffectResultEvent> CollectMonsterActionEffects(bool buffered)
        {
            var pattern = new MonsterAttackPattern(
                "A999",
                "Designer Pattern",
                1,
                1,
                2,
                statusEffects: new[] { StatusEffectKind.Stun },
                statusEffectAmount: 1);
            var catalog = new MonsterCatalogDefinition(
                "test-monsters",
                "Test Monsters",
                new[]
                {
                    new MonsterCatalogEntry(
                        "test-monster",
                        "Test Monster",
                        "test",
                        "test.behavior",
                        5,
                        1,
                        10,
                        attackPatterns: new[] { pattern })
                });
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("monster-01", new HexCoord(1, 0), 10, "test-monsters", "test-monster") },
                CombatConfig.Default,
                monsterCatalog: catalog);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.

            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;
            Assert.That(state.EndAction(), Is.True);

            if (buffered)
            {
                state.BeginEffectBuffering();
                state.ResolveMonsterAction();
                Assert.That(events, Is.Empty, "Monster-action effects must stay buffered until flush.");
                state.FlushBufferedEffects();
            }
            else
            {
                state.ResolveMonsterAction();
            }

            return events;
        }
    }
}



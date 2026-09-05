using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatTileEffectDeltaResolverTests
    {
        [Test]
        public void FirstSnapshotCapturesBaselineWithoutEvents()
        {
            var resolver = new CombatTileEffectDeltaResolver();

            var events = resolver.Resolve(new[] { Monster("m1", 1, 0, 10) });

            Assert.That(events, Is.Empty);
            Assert.That(resolver.HasBaseline, Is.True);
        }

        [Test]
        public void MonsterHpLossEmitsDamageCenteredOnMonsterTile()
        {
            var resolver = new CombatTileEffectDeltaResolver();
            resolver.Resolve(new[] { Monster("m1", 2, -1, 10) });

            var events = resolver.Resolve(new[] { Monster("m1", 2, -1, 6) });

            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(EffectKind.Damage));
            Assert.That(events[0].AppliedAmount, Is.EqualTo(4));
            Assert.That(events[0].TargetUnitId, Is.EqualTo("m1"));
            Assert.That(events[0].Center, Is.EqualTo(new HexCoord(2, -1)));
        }

        [Test]
        public void MonsterHpGainEmitsHealOnMonsterTile()
        {
            var resolver = new CombatTileEffectDeltaResolver();
            resolver.Resolve(new[] { Monster("m1", 0, 0, 4) });

            var events = resolver.Resolve(new[] { Monster("m1", 0, 0, 7) });

            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(EffectKind.Heal));
            Assert.That(events[0].Center, Is.EqualTo(new HexCoord(0, 0)));
        }

        [Test]
        public void NoChangeProducesNoEvents()
        {
            var resolver = new CombatTileEffectDeltaResolver();
            resolver.Resolve(new[] { Monster("m1", 1, 0, 10), Monster("m2", 3, 0, 8) });

            var events = resolver.Resolve(new[] { Monster("m1", 1, 0, 10), Monster("m2", 3, 0, 8) });

            Assert.That(events, Is.Empty);
        }

        [Test]
        public void MultipleMonstersEachGetTheirOwnTileEvent()
        {
            var resolver = new CombatTileEffectDeltaResolver();
            resolver.Resolve(new[] { Monster("m1", 1, 0, 10), Monster("m2", 3, 0, 8) });

            var events = resolver.Resolve(new[] { Monster("m1", 1, 0, 6), Monster("m2", 3, 0, 5) });

            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events.Single(e => e.TargetUnitId == "m1").Center, Is.EqualTo(new HexCoord(1, 0)));
            Assert.That(events.Single(e => e.TargetUnitId == "m2").Center, Is.EqualTo(new HexCoord(3, 0)));
        }

        [Test]
        public void MovedMonsterDamageUsesCurrentTile()
        {
            var resolver = new CombatTileEffectDeltaResolver();
            resolver.Resolve(new[] { Monster("m1", 1, 0, 10) });

            // Monster moved to a new coord and also took damage this step.
            var events = resolver.Resolve(new[] { Monster("m1", 2, 0, 7) });

            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Center, Is.EqualTo(new HexCoord(2, 0)));
        }

        [Test]
        public void NewMonsterDoesNotEmitOnFirstAppearance()
        {
            var resolver = new CombatTileEffectDeltaResolver();
            resolver.Resolve(new[] { Monster("m1", 1, 0, 10) });

            var events = resolver.Resolve(new[] { Monster("m1", 1, 0, 10), Monster("m2", 3, 0, 8) });

            Assert.That(events, Is.Empty);
        }

        private static MonsterEffectSnapshot Monster(string id, int q, int r, int hp)
        {
            return new MonsterEffectSnapshot(id, new HexCoord(q, r), hp, hp <= 0);
        }
    }
}


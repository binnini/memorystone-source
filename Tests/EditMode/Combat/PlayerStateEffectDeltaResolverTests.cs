using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerStateEffectDeltaResolverTests
    {
        [Test]
        public void FirstSnapshotCapturesBaselineWithoutEvents()
        {
            var resolver = new PlayerStateEffectDeltaResolver();

            var events = resolver.Resolve(MakeSnapshot(hp: 20, block: 0, revealed: 3));

            Assert.That(events, Is.Empty);
            Assert.That(resolver.HasBaseline, Is.True);
        }

        [Test]
        public void NoChangeProducesNoEvents()
        {
            var resolver = new PlayerStateEffectDeltaResolver();
            resolver.Resolve(MakeSnapshot(hp: 20, block: 0, revealed: 3));

            var events = resolver.Resolve(MakeSnapshot(hp: 20, block: 0, revealed: 3));

            Assert.That(events, Is.Empty);
        }

        [Test]
        public void HpDecreaseProducesSingleDamageEvent()
        {
            var resolver = new PlayerStateEffectDeltaResolver();
            resolver.Resolve(MakeSnapshot(hp: 20, block: 0, revealed: 3));

            var events = resolver.Resolve(MakeSnapshot(hp: 14, block: 0, revealed: 3));

            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(EffectKind.Damage));
            Assert.That(events[0].AppliedAmount, Is.EqualTo(6));
        }

        [Test]
        public void HpIncreaseProducesSingleHealEvent()
        {
            var resolver = new PlayerStateEffectDeltaResolver();
            resolver.Resolve(MakeSnapshot(hp: 14, block: 0, revealed: 3));

            var events = resolver.Resolve(MakeSnapshot(hp: 19, block: 0, revealed: 3));

            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(EffectKind.Heal));
            Assert.That(events[0].AppliedAmount, Is.EqualTo(5));
        }

        [Test]
        public void BlockIncreaseProducesSingleBlockEvent()
        {
            var resolver = new PlayerStateEffectDeltaResolver();
            resolver.Resolve(MakeSnapshot(hp: 20, block: 0, revealed: 3));

            var events = resolver.Resolve(MakeSnapshot(hp: 20, block: 5, revealed: 3));

            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(EffectKind.Block));
            Assert.That(events[0].AppliedAmount, Is.EqualTo(5));
        }

        [Test]
        public void RevealedCountIncreaseProducesFogRevealEvent()
        {
            var resolver = new PlayerStateEffectDeltaResolver();
            resolver.Resolve(MakeSnapshot(hp: 20, block: 0, revealed: 3));

            var events = resolver.Resolve(MakeSnapshot(hp: 20, block: 0, revealed: 6));

            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(EffectKind.FogReveal));
            Assert.That(events[0].AppliedAmount, Is.EqualTo(3));
        }

        [Test]
        public void SimultaneousDamageAndBlockProduceBothEventsOnce()
        {
            var resolver = new PlayerStateEffectDeltaResolver();
            resolver.Resolve(MakeSnapshot(hp: 20, block: 0, revealed: 3));

            var events = resolver.Resolve(MakeSnapshot(hp: 16, block: 4, revealed: 3));

            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events.Count(e => e.Kind == EffectKind.Damage), Is.EqualTo(1));
            Assert.That(events.Count(e => e.Kind == EffectKind.Block), Is.EqualTo(1));
        }

        [Test]
        public void ResetReBaselinesWithoutEmitting()
        {
            var resolver = new PlayerStateEffectDeltaResolver();
            resolver.Resolve(MakeSnapshot(hp: 20, block: 0, revealed: 3));
            resolver.Reset();

            Assert.That(resolver.HasBaseline, Is.False);
            var events = resolver.Resolve(MakeSnapshot(hp: 5, block: 9, revealed: 12));
            Assert.That(events, Is.Empty);
        }

        private static PlayerStateSnapshot MakeSnapshot(int hp, int block, int revealed)
        {
            return new PlayerStateSnapshot(
                hp: hp,
                maxHp: 20,
                block: block,
                isDead: hp <= 0,
                position: new HexCoord(0, 0),
                phase: CombatPhase.PlayerAction,
                currentKi: 3,
                maxKi: 4,
                moveDeck: new PlayerDeckRuntimeSummary(5, 1, 0),
                actionDeck: new PlayerDeckRuntimeSummary(5, 2, 0),
                objectiveCompleted: false,
                objectiveStatusText: "test",
                lastDiscardedCard: null,
                lastFailureReason: string.Empty,
                lastInvestigateResult: string.Empty,
                visibility: new PlayerVisibilitySummary(10, 2, revealed));
        }
    }
}


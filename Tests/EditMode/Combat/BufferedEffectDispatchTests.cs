using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class BufferedEffectDispatchTests
    {
        [Test]
        public void DispatchBufferedEffectAt_ReplaysSingleBufferedEffectWithoutConsumingBuffer()
        {
            var state = CombatState.CreateDefaultDemo();
            var dispatched = 0;
            state.EffectResolved += _ => dispatched++;

            state.BeginEffectBuffering();
            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);

            // The move's effects are queued, not dispatched, while buffering.
            Assert.That(state.BufferedEffects.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(dispatched, Is.EqualTo(0));

            // Replaying one index fires exactly one effect and leaves the buffer (indices stable).
            state.DispatchBufferedEffectAt(0);
            Assert.That(dispatched, Is.EqualTo(1));
            Assert.That(state.BufferedEffects.Count, Is.GreaterThanOrEqualTo(1));

            state.EndBufferedEffectDispatch();
            Assert.That(state.BufferedEffects.Count, Is.EqualTo(0));
        }

        [Test]
        public void DispatchBufferedEffectAt_OutOfRange_IsNoOp()
        {
            var state = CombatState.CreateDefaultDemo();
            var dispatched = 0;
            state.EffectResolved += _ => dispatched++;

            state.BeginEffectBuffering();
            state.DispatchBufferedEffectAt(0);   // empty buffer
            state.DispatchBufferedEffectAt(-1);
            state.DispatchBufferedEffectAt(99);

            Assert.That(dispatched, Is.EqualTo(0));
        }
    }
}

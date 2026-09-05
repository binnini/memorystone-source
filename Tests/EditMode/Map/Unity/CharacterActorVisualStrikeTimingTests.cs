using NUnit.Framework;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    /// <summary>
    /// Covers the strike-point calculation behind <c>WaitForAttackStrike</c>, which
    /// <c>CombatTimingProfile.alignImpactToAnimationClip</c> depends on. The coroutine itself needs a live
    /// Animator, so only the pure part is asserted here — that is precisely why the original defect (the
    /// wait looping until maxWaitSeconds and swallowing the strike target) went unnoticed while the toggle
    /// shipped off. See docs/presentation-duration-data-plan.md §2.2.
    /// </summary>
    public sealed class CharacterActorVisualStrikeTimingTests
    {
        [Test]
        public void StrikeTargetIsTheClipFractionWhenItFitsInsideTheWaitBudget()
        {
            var resolved = CharacterActorVisual.TryResolveStrikeWaitTarget(
                clipLength: 1f,
                strikeFraction: 0.3f,
                maxWaitSeconds: 1.5f,
                out var target);

            Assert.That(resolved, Is.True);
            // The shipping profile's authored values (attackStrikeFraction 0.3, maxWait 1.5): the strike must
            // land at 0.3s, not at the 1.5s ceiling.
            Assert.That(target, Is.EqualTo(0.3f).Within(0.0001f));
        }

        [Test]
        public void StrikeTargetIsClampedToTheMaxWaitForALongClip()
        {
            var resolved = CharacterActorVisual.TryResolveStrikeWaitTarget(
                clipLength: 10f,
                strikeFraction: 0.85f,
                maxWaitSeconds: 1.5f,
                out var target);

            Assert.That(resolved, Is.True);
            Assert.That(target, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void StrikeFractionIsClampedToTheUnitRange()
        {
            Assert.That(
                CharacterActorVisual.TryResolveStrikeWaitTarget(2f, 5f, 100f, out var above),
                Is.True);
            Assert.That(above, Is.EqualTo(2f).Within(0.0001f), "A fraction above 1 must not overshoot the clip.");

            Assert.That(
                CharacterActorVisual.TryResolveStrikeWaitTarget(2f, -1f, 100f, out var below),
                Is.True);
            Assert.That(below, Is.EqualTo(0f).Within(0.0001f), "A negative fraction must not produce a negative wait.");
        }

        [Test]
        public void UnusableClipLengthsFallBackInsteadOfGuessingAStrikePoint()
        {
            // 0 is what a looping or not-yet-evaluated Animator state reports; the infinities are what a
            // malformed state reports. All three must hand control back to the caller's fixed delay.
            foreach (var length in new[] { 0f, -1f, float.PositiveInfinity, float.NaN })
            {
                Assert.That(
                    CharacterActorVisual.TryResolveStrikeWaitTarget(length, 0.3f, 1.5f, out var target),
                    Is.False,
                    $"clipLength {length} must not resolve a strike point.");
                Assert.That(target, Is.EqualTo(0f), $"clipLength {length} must not leak a target value.");
            }
        }

        [Test]
        public void NegativeMaxWaitCannotProduceANegativeTarget()
        {
            Assert.That(
                CharacterActorVisual.TryResolveStrikeWaitTarget(1f, 0.3f, -5f, out var target),
                Is.True);
            Assert.That(target, Is.EqualTo(0f).Within(0.0001f));
        }
    }
}

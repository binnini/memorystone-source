using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class FieldObjectVisualPresenterTests
    {
        [TestCase(CardIds.Firebomb, "bomb")]
        [TestCase(CardIds.SacredLamp, "Lantern_5000")]
        [TestCase(CardIds.Flashbang, "flashbang")]
        public void TryResolveDefaultObjectRef_KnownFieldCards_ReturnsExpectedPrefabObjectRef(string visualRef, string expectedObjectRef)
        {
            Assert.That(FieldObjectVisualPresenter.TryResolveDefaultObjectRef(visualRef, out var objectRef), Is.True);
            Assert.That(objectRef, Is.EqualTo(expectedObjectRef));
        }

        [Test]
        public void TryResolveDefaultObjectRef_UnknownVisualRef_ReturnsFalse()
        {
            Assert.That(FieldObjectVisualPresenter.TryResolveDefaultObjectRef("unknown", out var objectRef), Is.False);
            Assert.That(objectRef, Is.Null);
        }
    }
}

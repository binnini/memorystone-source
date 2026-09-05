using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class FieldObjectVisualPresenterTests
    {
        [TestCase(ApprovedCardCatalogFactory.FieldFirebombId, "bomb")]
        [TestCase(ApprovedCardCatalogFactory.FieldSacredCampfireId, "Lantern_5000")]
        [TestCase(ApprovedCardCatalogFactory.FieldFlashbangId, "flashbang")]
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

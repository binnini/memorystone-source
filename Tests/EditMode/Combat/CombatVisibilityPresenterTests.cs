using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatVisibilityPresenterTests
    {
        [Test]
        public void GetSafeCellInfoReturnsMissingWithoutStateOrDebugMap()
        {
            var coord = new HexCoord(4, 0);
            var presenter = new CombatVisibilityPresenter();

            var info = presenter.GetSafeCellInfo(null, null, false, coord);

            Assert.That(info.Coord, Is.EqualTo(coord));
            Assert.That(info.Exists, Is.False);
            Assert.That(info.Visibility, Is.EqualTo(HexCellVisibility.Unknown));
        }

        [Test]
        public void GetSafeCellInfoDebugRevealUsesLoadedMapFullDetailsWithoutState()
        {
            var coord = new HexCoord(1, 0);
            var presenter = new CombatVisibilityPresenter();

            var info = presenter.GetSafeCellInfo(null, CreateSensitiveMap(), true, coord);

            Assert.That(info.Exists, Is.True);
            Assert.That(info.Visibility, Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(info.ExposesFullDetails, Is.True);
            Assert.That(info.TileDefinitionId, Is.EqualTo("near"));
            Assert.That(info.TerrainTypeId, Is.EqualTo("street"));
            Assert.That(info.EventId, Is.EqualTo("secret-event"));
            Assert.That(info.LandmarkId, Is.EqualTo("secret-landmark"));
        }

        [Test]
        public void TooltipHidesUnknownAndHintedSensitiveDetails()
        {
            var presenter = new CombatVisibilityPresenter();
            var unknown = CreateInfo(HexCellVisibility.Unknown, false, "secret-unknown-event", "secret-unknown-landmark");
            var hinted = CreateInfo(HexCellVisibility.Hinted, false, "secret-hinted-event", "secret-hinted-landmark");

            var unknownTooltip = presenter.GetTooltipText(unknown);
            var hintedTooltip = presenter.GetTooltipText(hinted);

            Assert.That(unknownTooltip, Does.Contain("Unknown"));
            Assert.That(unknownTooltip, Does.Not.Contain("secret-unknown-event"));
            Assert.That(unknownTooltip, Does.Not.Contain("secret-unknown-landmark"));
            Assert.That(hintedTooltip, Does.Contain("Hinted street"));
            Assert.That(hintedTooltip, Does.Not.Contain("secret-hinted-event"));
            Assert.That(hintedTooltip, Does.Not.Contain("secret-hinted-landmark"));
        }

        [Test]
        public void TooltipShowsRevealedSensitiveDetails()
        {
            var presenter = new CombatVisibilityPresenter();
            var revealed = CreateInfo(HexCellVisibility.Revealed, true, "revealed-event", "revealed-landmark");

            var tooltip = presenter.GetTooltipText(revealed);

            Assert.That(tooltip, Does.Contain("Revealed street"));
            Assert.That(tooltip, Does.Contain("event revealed-event"));
            Assert.That(tooltip, Does.Contain("landmark revealed-landmark"));
        }

        private static HexVisibilitySafeCellInfo CreateInfo(
            HexCellVisibility visibility,
            bool exposesFullDetails,
            string eventId,
            string landmarkId)
        {
            return new HexVisibilitySafeCellInfo(
                new HexCoord(1, 0),
                visibility,
                true,
                visibility != HexCellVisibility.Unknown,
                exposesFullDetails,
                exposesFullDetails ? "near" : string.Empty,
                "street",
                2,
                true,
                false,
                eventId,
                landmarkId);
        }

        private static HexMapData CreateSensitiveMap()
        {
            return new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "start", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "near", "street", 2, true, false, eventId: "secret-event", landmarkId: "secret-landmark")
            });
        }
    }
}


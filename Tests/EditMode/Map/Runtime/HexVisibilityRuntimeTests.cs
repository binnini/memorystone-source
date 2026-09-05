using NUnit.Framework;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    public sealed class HexVisibilityRuntimeTests
    {
        [Test]
        public void InitialVisibilityRevealsSightRangeAndLeavesDistantUnknown()
        {
            var runtime = new HexVisibilityRuntime(CreateMap(), new HexCoord(0, 0), hintedRadius: 1);

            Assert.That(runtime.GetVisibility(new HexCoord(0, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(runtime.GetVisibility(new HexCoord(1, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(runtime.GetVisibility(new HexCoord(2, 0)), Is.EqualTo(HexCellVisibility.Unknown));
        }

        [Test]
        public void UnknownSafeInfoDoesNotExposeHiddenCellDetails()
        {
            var runtime = new HexVisibilityRuntime(CreateMap(), new HexCoord(0, 0), hintedRadius: 0);

            var info = runtime.GetSafeCellInfo(new HexCoord(1, 0));

            Assert.That(info.Exists, Is.True);
            Assert.That(info.Visibility, Is.EqualTo(HexCellVisibility.Unknown));
            Assert.That(info.ExposesTerrain, Is.False);
            Assert.That(info.ExposesFullDetails, Is.False);
            Assert.That(info.TerrainTypeId, Is.Empty);
            Assert.That(info.EventId, Is.Empty);
            Assert.That(info.LandmarkId, Is.Empty);
        }

        [Test]
        public void ExpiredTemporaryRevealSafeInfoExposesLimitedTerrainButHidesSpecifics()
        {
            var runtime = new HexVisibilityRuntime(CreateMap(), new HexCoord(0, 0), hintedRadius: 1);
            runtime.RefreshTemporaryRevealArea(new HexCoord(2, 0), 0);

            var info = runtime.GetSafeCellInfo(new HexCoord(1, 0));

            Assert.That(info.Visibility, Is.EqualTo(HexCellVisibility.Hinted));
            Assert.That(info.ExposesTerrain, Is.True);
            Assert.That(info.ExposesFullDetails, Is.False);
            Assert.That(info.TerrainTypeId, Is.EqualTo("landmark-terrain"));
            Assert.That(info.BaseWalkable, Is.True);
            Assert.That(info.TileDefinitionId, Is.Empty);
            Assert.That(info.EventId, Is.Empty);
            Assert.That(info.LandmarkId, Is.Empty);
        }

        [Test]
        public void RevealedSafeInfoPreservesFullCellDetails()
        {
            var runtime = new HexVisibilityRuntime(CreateMap(), new HexCoord(0, 0), hintedRadius: 1);
            runtime.Reveal(new HexCoord(1, 0));

            var info = runtime.GetSafeCellInfo(new HexCoord(1, 0));

            Assert.That(info.Visibility, Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(info.ExposesTerrain, Is.True);
            Assert.That(info.ExposesFullDetails, Is.True);
            Assert.That(info.TileDefinitionId, Is.EqualTo("landmark-tile"));
            Assert.That(info.TerrainTypeId, Is.EqualTo("landmark-terrain"));
            Assert.That(info.EventId, Is.EqualTo("event-hidden"));
            Assert.That(info.LandmarkId, Is.EqualTo("landmark-hidden"));
        }

        [Test]
        public void RefreshTemporaryRevealAreaDemotesPreviousSightToHinted()
        {
            var runtime = new HexVisibilityRuntime(CreateMap(), new HexCoord(0, 0), hintedRadius: 1);

            runtime.RefreshTemporaryRevealArea(new HexCoord(2, 0), 1);

            Assert.That(runtime.GetVisibility(new HexCoord(0, 0)), Is.EqualTo(HexCellVisibility.Hinted));
            Assert.That(runtime.GetVisibility(new HexCoord(1, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(runtime.GetVisibility(new HexCoord(2, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(runtime.GetVisibility(new HexCoord(3, 0)), Is.EqualTo(HexCellVisibility.Revealed));
        }

        [Test]
        public void ScoutRevealTemporarilyRevealsRadiusAndExpiresToHintedNextTurn()
        {
            var runtime = new HexVisibilityRuntime(CreateMap(), new HexCoord(0, 0), hintedRadius: 0);

            runtime.ScoutReveal(new HexCoord(2, 0), 2);

            Assert.That(runtime.GetVisibility(new HexCoord(2, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(runtime.GetVisibility(new HexCoord(1, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(runtime.GetVisibility(new HexCoord(3, 0)), Is.EqualTo(HexCellVisibility.Revealed));

            runtime.RefreshTemporaryRevealArea(new HexCoord(0, 0), 0);

            Assert.That(runtime.GetVisibility(new HexCoord(2, 0)), Is.EqualTo(HexCellVisibility.Hinted));
            Assert.That(runtime.GetVisibility(new HexCoord(1, 0)), Is.EqualTo(HexCellVisibility.Hinted));
            Assert.That(runtime.GetVisibility(new HexCoord(3, 0)), Is.EqualTo(HexCellVisibility.Hinted));
        }

        [Test]
        public void BlocksVisionObjectDoesNotHideCellsBehindItWhenInSightRange()
        {
            var blocker = new HexCoord(1, 0);
            var behindBlocker = new HexCoord(2, 0);
            var runtime = new HexVisibilityRuntime(
                new HexMapData(
                    new[]
                    {
                        new HexCellData(new HexCoord(0, 0), "start", "street", 1, true, false),
                        new HexCellData(blocker, "blocker", "building", 1, true, false),
                        new HexCellData(behindBlocker, "behind", "street", 1, true, false)
                    },
                    objectRefs: new[] { new HexMapObjectData("tower-a", "Building", "tower", blocker, blocksVision: true) }),
                new HexCoord(0, 0),
                hintedRadius: 2);

            Assert.That(runtime.GetVisibility(blocker), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(runtime.GetVisibility(behindBlocker), Is.EqualTo(HexCellVisibility.Revealed));
        }

        private static HexMapData CreateMap()
        {
            return new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "start-tile", "street", 1, true, false, "event-start", "landmark-start"),
                new HexCellData(new HexCoord(1, 0), "landmark-tile", "landmark-terrain", 2, true, false, "event-hidden", "landmark-hidden"),
                new HexCellData(new HexCoord(2, 0), "far-tile", "far-terrain", 3, true, false, "event-far", "landmark-far"),
                new HexCellData(new HexCoord(3, 0), "farther-tile", "farther-terrain", 3, true, false, "event-farther", "landmark-farther")
            });
        }
    }
}


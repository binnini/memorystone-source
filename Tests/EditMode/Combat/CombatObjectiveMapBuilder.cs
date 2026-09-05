using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    internal static class CombatObjectiveMapBuilder
    {
        private const string ObjectiveId = "test-objective";
        private const string ObjectiveLandmarkId = "test-objective-landmark";

        public static HexMapData CreateObjectiveMap(HexCoord objectiveCoord)
        {
            var cells = Enumerable.Range(0, 5).Select(q =>
            {
                var coord = new HexCoord(q, 0);
                return new HexCellData(
                    coord,
                    coord == objectiveCoord ? "goal" : $"cell-{q}",
                    "street",
                    1,
                    true,
                    false,
                    landmarkId: coord == objectiveCoord ? ObjectiveLandmarkId : string.Empty);
            });

            return new HexMapData(cells, new[] { new HexObjectiveBinding(ObjectiveId, ObjectiveLandmarkId, "test objective") });
        }

        public static HexMapData CreateObjectiveBehindSightBlockerMap()
        {
            return new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "start", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "sight-blocker", "street", 1, true, true),
                new HexCellData(new HexCoord(1, -1), "detour", "street", 1, true, false),
                new HexCellData(new HexCoord(2, -1), "goal", "street", 1, true, false, landmarkId: ObjectiveLandmarkId),
                new HexCellData(new HexCoord(4, 0), "enemy-start", "street", 1, true, false)
            }, new[] { new HexObjectiveBinding(ObjectiveId, ObjectiveLandmarkId, "test objective") });
        }
    }
}


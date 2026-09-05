using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    public sealed class HexPathfinderTests
    {
        [Test]
        public void MovePointsCountStepsAndTheBoundaryIsExact()
        {
            // 지형별 이동 비용 축 폐기(2026-09-05) 이후의 계약: MovePoints는 "걸음 수"다.
            // 카드 문안("최대 {Range}칸 이동합니다")과 같은 것을 센다.
            var map = Map(Cell(0, 0), Cell(1, 0), Cell(2, 0), Cell(3, 0), Cell(0, 1));

            var reachable = HexPathfinder.GetReachableCells(map, new MovementQuery(new HexCoord(0, 0), 2));

            Assert.That(reachable.ContainsKey(new HexCoord(0, 0)), Is.False, "시작 칸은 기본적으로 제외된다.");
            Assert.That(reachable[new HexCoord(1, 0)], Is.EqualTo(1));
            Assert.That(reachable[new HexCoord(0, 1)], Is.EqualTo(1));
            Assert.That(reachable[new HexCoord(2, 0)], Is.EqualTo(2), "경계값은 포함된다.");
            Assert.That(reachable.ContainsKey(new HexCoord(3, 0)), Is.False, "3걸음은 예산 밖이다.");
        }

        [Test]
        public void NonUniformEnterCostThrowsInsteadOfSilentlyWrongPaths()
        {
            // 비용 축은 폐기됐다. 되살아나면 BFS가 조용히 틀린 경로를 내므로 그 자리에서 던져야 한다.
            var map = Map(Cell(0, 0), Cell(1, 0, cost: 2));

            Assert.Throws<System.InvalidOperationException>(
                () => HexPathfinder.GetReachableCells(map, new MovementQuery(new HexCoord(0, 0), 3)));
        }

        [Test]
        public void IncludeStartIsExplicit()
        {
            var map = Map(Cell(0, 0), Cell(1, 0));
            var reachable = HexPathfinder.GetReachableCells(map, new MovementQuery(new HexCoord(0, 0), 1, includeStart: true));
            Assert.That(reachable[new HexCoord(0, 0)], Is.EqualTo(0));
        }

        [Test]
        public void MovePointsOneTwoThreeProduceDifferentReachableSets()
        {
            var map = RadiusLineMap(4);
            var start = new HexCoord(0, 0);

            Assert.That(HexPathfinder.GetReachableCells(map, new MovementQuery(start, 1)).Count, Is.EqualTo(1));
            Assert.That(HexPathfinder.GetReachableCells(map, new MovementQuery(start, 2)).Count, Is.EqualTo(2));
            Assert.That(HexPathfinder.GetReachableCells(map, new MovementQuery(start, 3)).Count, Is.EqualTo(3));
        }

        [Test]
        public void BlocksStaticRuntimeAndOccupiedCellsByDefault()
        {
            var blocked = new HexCoord(1, 0);
            var runtimeBlocked = new HexCoord(0, 1);
            var occupied = new HexCoord(-1, 1);
            var map = Map(Cell(0, 0), Cell(1, 0, walkable: false), Cell(0, 1), Cell(-1, 1));
            var states = new Dictionary<HexCoord, HexCellRuntimeState>
            {
                [runtimeBlocked] = new HexCellRuntimeState(temporaryBlocked: true),
                [occupied] = new HexCellRuntimeState("enemy")
            };

            var reachable = HexPathfinder.GetReachableCells(map, new MovementQuery(new HexCoord(0, 0), 2, unitId: "player"), states);

            Assert.That(reachable.ContainsKey(blocked), Is.False);
            Assert.That(reachable.ContainsKey(runtimeBlocked), Is.False);
            Assert.That(reachable.ContainsKey(occupied), Is.False);
            Assert.That(HexPathfinder.FindPath(map, new MovementQuery(new HexCoord(0, 0), 2, unitId: "player"), blocked, states), Is.Empty);
        }

        [Test]
        public void TemporaryBlockedCellRejectsEveryUnitRegardlessOfId()
        {
            // 필드 카드 오브젝트는 원점 타일을 TemporaryBlocked로 마킹한다(CombatState.UpdateOccupancy).
            // OccupyingUnitId는 같은 유닛에게는 진입을 허용하지만 TemporaryBlocked는 유닛 무관하게 모두 막아야
            // 플레이어와 몬스터가 똑같이 진입 불가가 된다. CanEnter 단일 소스를 직접 검증한다.
            var blocked = new HexCoord(1, 0);
            var map = Map(Cell(0, 0), Cell(1, 0), Cell(2, 0));
            var states = new Dictionary<HexCoord, HexCellRuntimeState>
            {
                [blocked] = new HexCellRuntimeState(temporaryBlocked: true)
            };

            Assert.That(
                HexPathfinder.CanEnter(map, blocked, new MovementQuery(new HexCoord(0, 0), 2, unitId: "player"), states, out _),
                Is.False,
                "Player is blocked by a field-object tile.");
            Assert.That(
                HexPathfinder.CanEnter(map, blocked, new MovementQuery(new HexCoord(0, 0), 2, unitId: "monster-1"), states, out _),
                Is.False,
                "Monster is blocked by the same field-object tile.");
        }

        [Test]
        public void BlocksMovementObjectRefsForReachableCellsAndPaths()
        {
            var blocked = new HexCoord(1, 0);
            var map = new HexMapData(
                new[] { Cell(0, 0), Cell(1, 0), Cell(2, 0), Cell(0, 1) },
                objectRefs: new[] { new HexMapObjectData("building-a", "Building", "building", blocked, blocksMovement: true) });

            var reachable = HexPathfinder.GetReachableCells(map, new MovementQuery(new HexCoord(0, 0), 3));

            Assert.That(reachable.ContainsKey(blocked), Is.False);
            Assert.That(HexPathfinder.FindPath(map, new MovementQuery(new HexCoord(0, 0), 3), blocked), Is.Empty);
        }

        [Test]
        public void EqualCostPathTieBreakIsDeterministic()
        {
            var start = new HexCoord(0, 0);
            var destination = new HexCoord(1, 1);
            var map = Map(Cell(0, 0), Cell(1, 0), Cell(0, 1), Cell(1, 1));

            var first = HexPathfinder.FindPath(map, new MovementQuery(start, 2), destination).ToArray();
            var second = HexPathfinder.FindPath(map, new MovementQuery(start, 2), destination).ToArray();

            CollectionAssert.AreEqual(first, second);
            CollectionAssert.AreEqual(new[] { start, new HexCoord(1, 0), destination }, first);
        }

        [Test]
        public void UnreachableMissingOrTooExpensiveDestinationReturnsEmptyPath()
        {
            var map = Map(Cell(0, 0), Cell(1, 0), Cell(2, 0), Cell(3, 0));
            Assert.That(HexPathfinder.FindPath(map, new MovementQuery(new HexCoord(0, 0), 2), new HexCoord(3, 0)), Is.Empty,
                "예산(2걸음)을 넘는 목적지.");
            Assert.That(HexPathfinder.FindPath(map, new MovementQuery(new HexCoord(0, 0), 2), new HexCoord(9, 9)), Is.Empty,
                "맵에 없는 목적지.");
        }

        [Test]
        public void WaterTerrainIsImpassableWhenTraitsSupplied()
        {
            var water = new HexCoord(1, 0);
            var map = Map(Cell(0, 0), WaterCell(1, 0), Cell(2, 0));

            var reachable = HexPathfinder.GetReachableCells(
                map, new MovementQuery(new HexCoord(0, 0), 3), null, HexTerrainTraits.Default);

            Assert.That(reachable.ContainsKey(water), Is.False, "Water terrain cannot be entered.");
            Assert.That(reachable.ContainsKey(new HexCoord(2, 0)), Is.False, "Cells reachable only through water stay blocked.");
            Assert.That(
                HexPathfinder.FindPath(map, new MovementQuery(new HexCoord(0, 0), 3), water, null, HexTerrainTraits.Default),
                Is.Empty);
        }

        [Test]
        public void WaterTerrainStaysWalkableWithoutTraits()
        {
            var map = Map(Cell(0, 0), WaterCell(1, 0));

            var reachable = HexPathfinder.GetReachableCells(map, new MovementQuery(new HexCoord(0, 0), 1));

            Assert.That(reachable.ContainsKey(new HexCoord(1, 0)), Is.True, "Without traits the terrain rule is not applied.");
        }

        [Test]
        public void HeightDifferenceOfTwoOrMoreBlocksMovement()
        {
            // Line of cells: heights 0 -> 1 (ok) -> 3 (diff 2 from previous, blocked).
            var map = Map(
                CellAtHeight(0, 0, 0),
                CellAtHeight(1, 0, 1),
                CellAtHeight(2, 0, 3));

            var reachable = HexPathfinder.GetReachableCells(
                map, new MovementQuery(new HexCoord(0, 0), 5), null, HexTerrainTraits.Default);

            Assert.That(reachable.ContainsKey(new HexCoord(1, 0)), Is.True, "A single-level climb is allowed.");
            Assert.That(reachable.ContainsKey(new HexCoord(2, 0)), Is.False, "A two-level climb is blocked.");
        }

        [Test]
        public void HeightDifferenceOfTwoBlocksDirectFirstStep()
        {
            var map = Map(CellAtHeight(0, 0, 0), CellAtHeight(1, 0, 2));

            Assert.That(
                HexPathfinder.FindPath(map, new MovementQuery(new HexCoord(0, 0), 3), new HexCoord(1, 0), null, HexTerrainTraits.Default),
                Is.Empty);
        }

        private static HexMapData RadiusLineMap(int length)
        {
            var cells = Enumerable.Range(0, length + 1).Select(q => Cell(q, 0)).ToArray();
            return Map(cells);
        }

        private static HexMapData Map(params HexCellData[] cells) => new HexMapData(cells);
        private static HexCellData Cell(int q, int r, int cost = 1, bool walkable = true) => new HexCellData(new HexCoord(q, r), "tile", "terrain", cost, walkable, false);
        private static HexCellData WaterCell(int q, int r) => new HexCellData(new HexCoord(q, r), "tile", "river", 1, true, false);
        private static HexCellData CellAtHeight(int q, int r, int heightLevel) => new HexCellData(new HexCoord(q, r), "tile", "terrain", 1, true, false, null, null, 0, null, heightLevel);
    }
}


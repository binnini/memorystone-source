using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    /// <summary>
    /// 원판 클리어런스(계획서 §21.3): <see cref="MovementQuery.FootprintRadius"/>가 0보다 크면
    /// 진입 판정이 "중심 칸 하나"가 아니라 <b>중심 기준 반경 R 원판 전체</b>를 묻는다.
    ///
    /// 이 파일이 고정하는 계약은 넷이다:
    /// ① 반경 0(기존 호출부 전부)은 동작이 <b>한 톨도</b> 바뀌지 않는다 — 순수 가산이다.
    /// ② 몸보다 좁은 틈으로는 걸어 들어가지 않는다(이것이 §14.3이 걷기를 통째로 끈 이유였다).
    /// ③ 자기 몸이 이미 깔고 있는 칸은 자기를 막지 않는다.
    /// ④ 몸이 맵 밖으로 삐져나오는 자리에는 서지 않는다.
    /// </summary>
    public sealed class HexPathfinderClearanceTests
    {
        // -----------------------------------------------------------------------------------------
        // ① 반경 0 = 기존 동작 (회귀 방어)
        // -----------------------------------------------------------------------------------------

        [Test]
        public void RadiusZeroBehavesExactlyLikeBefore()
        {
            var map = Disk(4);
            var start = new HexCoord(0, 0);

            var withoutField = HexPathfinder.GetReachableCells(map, new MovementQuery(start, 2, unitId: "u"));
            var withExplicitZero = HexPathfinder.GetReachableCells(
                map, new MovementQuery(start, 2, unitId: "u", footprintRadius: 0));

            Assert.That(withExplicitZero.Keys, Is.EquivalentTo(withoutField.Keys),
                "반경 0은 기본값이며, 명시해도 같은 집합이어야 한다 — 아니면 가산이 아니다.");
        }

        [Test]
        public void RadiusZeroStillWalksThroughAOneCellGap()
        {
            // 양성 대조: 아래 ②가 "무엇이든 항상 막힘"으로 통과하는 게 아님을 고정한다.
            var map = Disk(4);
            var states = WallWithOneCellGap();

            var path = HexPathfinder.FindPath(
                map, new MovementQuery(new HexCoord(-2, 0), 8, unitId: "u"), new HexCoord(2, 0), states);

            Assert.That(path, Is.Not.Empty, "한 칸 몸은 한 칸 틈을 지나갈 수 있어야 한다.");
        }

        // -----------------------------------------------------------------------------------------
        // ② 몸보다 좁은 틈은 통과하지 못한다
        // -----------------------------------------------------------------------------------------

        [Test]
        public void RadiusOneCannotSqueezeThroughAOneCellGap()
        {
            var map = Disk(4);
            var states = WallWithOneCellGap();

            var path = HexPathfinder.FindPath(
                map,
                new MovementQuery(new HexCoord(-2, 0), 8, unitId: "u", footprintRadius: 1),
                new HexCoord(2, 0),
                states);

            Assert.That(path, Is.Empty,
                "7칸 몸은 한 칸 틈으로 들어갈 수 없다 — 이 사고를 막으려고 §14.3이 걷기를 통째로 껐었다.");
        }

        [Test]
        public void ClearanceRejectsACellWhoseNeighbourIsBlocked()
        {
            // 단일 칸 판정으로는 통과하지만 원판 판정으로는 막히는 최소 사례.
            var map = Disk(3);
            var target = new HexCoord(1, 0);
            var states = new Dictionary<HexCoord, HexCellRuntimeState>
            {
                [new HexCoord(2, 0)] = new HexCellRuntimeState(temporaryBlocked: true)
            };

            Assert.That(
                HexPathfinder.CanEnter(map, null, target, new MovementQuery(new HexCoord(0, 0), 1, unitId: "u"), states, null, out _),
                Is.True,
                "중심 칸 자체는 비어 있다.");

            Assert.That(
                HexPathfinder.CanEnter(map, null, target, new MovementQuery(new HexCoord(0, 0), 1, unitId: "u", footprintRadius: 1), states, null, out _),
                Is.False,
                "원판의 한 칸이라도 막히면 그 자리에 설 수 없다.");
        }

        // -----------------------------------------------------------------------------------------
        // ③ 자기 몸은 자기를 막지 않는다
        // -----------------------------------------------------------------------------------------

        [Test]
        public void OwnOccupiedCellsDoNotBlockTheMoverItself()
        {
            // 멀티셀 유닛은 점유 캐시에 원판 전체가 자기 id로 등록된다(CombatState.UpdateOccupancy).
            // 진입 판정이 OccupyingUnitId != UnitId일 때만 막으므로 별도 예외가 필요 없다 —
            // 그 성질이 없으면 멀티셀 보스는 첫 걸음부터 자기 몸에 막혀 한 칸도 못 움직인다.
            var map = Disk(4);
            var states = new Dictionary<HexCoord, HexCellRuntimeState>();
            foreach (var coord in HexArea.CellsWithin(new HexCoord(0, 0), 1))
            {
                states[coord] = new HexCellRuntimeState("boss");
            }

            var reachable = HexPathfinder.GetReachableCells(
                map, new MovementQuery(new HexCoord(0, 0), 1, unitId: "boss", footprintRadius: 1), states);

            Assert.That(reachable, Is.Not.Empty, "자기 몸에 자기가 막히면 안 된다.");
        }

        [Test]
        public void AnotherUnitsOccupiedCellStillBlocksTheDisk()
        {
            // 양성 대조: 위가 "점유를 통째로 무시"해서 통과하는 게 아님을 고정한다.
            var map = Disk(4);
            var states = new Dictionary<HexCoord, HexCellRuntimeState>
            {
                [new HexCoord(2, 0)] = new HexCellRuntimeState("other")
            };

            Assert.That(
                HexPathfinder.CanEnter(map, null, new HexCoord(1, 0), new MovementQuery(new HexCoord(0, 0), 1, unitId: "boss", footprintRadius: 1), states, null, out _),
                Is.False,
                "남이 점유한 칸은 원판이 덮을 수 없다(철조각·결계 링이 이 경로로 걸린다).");
        }

        // -----------------------------------------------------------------------------------------
        // ④ 몸이 맵 밖으로 나가지 않는다
        // -----------------------------------------------------------------------------------------

        [Test]
        public void DiskMustFitInsideTheMap()
        {
            var map = Disk(2);
            var edge = new HexCoord(2, 0);

            Assert.That(
                HexPathfinder.CanEnter(map, null, edge, new MovementQuery(new HexCoord(0, 0), 3, unitId: "u"), null, null, out _),
                Is.True,
                "가장자리 칸 자체는 맵 안이다.");

            Assert.That(
                HexPathfinder.CanEnter(map, null, edge, new MovementQuery(new HexCoord(0, 0), 3, unitId: "u", footprintRadius: 1), null, null, out _),
                Is.False,
                "원판이 맵 밖으로 삐져나오는 자리에는 설 수 없다.");
        }

        [Test]
        public void EnterCostComesFromTheCentreCellOnly()
        {
            // 몸이 커졌다고 한 칸 전진이 비싸지지는 않는다 — 비용은 중심 칸의 것 하나다.
            var cells = HexArea.CellsWithin(new HexCoord(0, 0), 3)
                .Select(coord => coord == new HexCoord(1, 0) ? Cell(coord, cost: 3) : Cell(coord))
                .ToArray();
            var map = new HexMapData(cells);

            HexPathfinder.CanEnter(
                map, null, new HexCoord(1, 0),
                new MovementQuery(new HexCoord(0, 0), 5, unitId: "u", footprintRadius: 1), null, null, out var cost);

            Assert.That(cost, Is.EqualTo(3), "이웃 칸들의 비용이 더해지면 안 된다.");
        }

        // -----------------------------------------------------------------------------------------
        // 픽스처
        // -----------------------------------------------------------------------------------------

        /// <summary>q = 0 세로줄을 전부 막고 (0,0) 한 칸만 남긴 벽. 좌우를 잇는 유일한 통로가 한 칸이다.</summary>
        private static Dictionary<HexCoord, HexCellRuntimeState> WallWithOneCellGap()
        {
            var states = new Dictionary<HexCoord, HexCellRuntimeState>();
            for (var r = -4; r <= 4; r++)
            {
                var coord = new HexCoord(0, r);
                if (coord == new HexCoord(0, 0))
                {
                    continue;
                }

                states[coord] = new HexCellRuntimeState(temporaryBlocked: true);
            }

            return states;
        }

        private static HexMapData Disk(int radius) =>
            new HexMapData(HexArea.CellsWithin(new HexCoord(0, 0), radius).Select(coord => Cell(coord)).ToArray());

        private static HexCellData Cell(HexCoord coord, int cost = 1) =>
            new HexCellData(coord, "tile", "terrain", cost, true, false);
    }
}

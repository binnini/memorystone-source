using System.Linq;
using NUnit.Framework;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    /// <summary>
    /// 접근성 공유 술어(2026-08-20 #4)의 계약. 뿌리는 「물 타일이 BaseWalkable=1로 저작돼 있다」 —
    /// BaseWalkable만 보는 필터(배치·오버레이·보행 BFS)를 물이 전부 통과해 상호작용 오브젝트가
    /// 물 위에 배치됐다. 여기의 핀 셋: ①물 = 접근 불가, ②사방 높이 2단계 차이 = 접근 불가,
    /// ③보행 BFS(<see cref="HexWalkDistances"/>)가 같은 술어를 타서 지대에 물·고립칸이 섞이지 않는다.
    /// </summary>
    public sealed class HexAccessibilityTests
    {
        private static HexCellData Cell(int q, int r, string terrain = "street", int height = 0, bool walkable = true)
        {
            return new HexCellData(
                new HexCoord(q, r), $"cell-{q}-{r}", terrain,
                baseMoveCost: 1, baseWalkable: walkable, baseBlocksVision: false, heightLevel: height);
        }

        [Test]
        public void WaterTileIsNotAccessibleEvenWhenAuthoredBaseWalkable()
        {
            // 실저작 그대로: hanriver-water 셀은 baseWalkable=1이다 — 그런데도 접근 불가여야 한다.
            // (0,0)에는 뭍 이웃 (0,1)을 준다 — 이웃이 전부 물인 칸은 그 자체로 접근 불가라서(들어올
            // 길이 없다), 뭍 판정의 대조군이 되려면 걸어 들어올 길이 있어야 한다.
            var map = new HexMapData(new[] { Cell(0, 0), Cell(0, 1), Cell(1, 0, terrain: "hanriver-water") });

            Assert.That(HexAccessibility.IsAccessible(map, new HexCoord(1, 0), HexTerrainTraits.Default), Is.False,
                "물 타일이 BaseWalkable 저작만으로 접근 가능 판정을 통과하면 배치·오버레이가 다시 새기 시작한다.");
            Assert.That(HexAccessibility.IsAccessible(map, new HexCoord(0, 0), HexTerrainTraits.Default), Is.True);
        }

        [Test]
        public void TileCutOffFromEveryNeighborByTwoHeightLevelsIsNotAccessible()
        {
            // 가운데 칸(높이 2)의 사방이 전부 높이 0 — 어느 이웃에서도 걸어 들어올 수 없다.
            var cells = HexCoord.Directions.Select(direction =>
            {
                var coord = new HexCoord(0, 0) + direction;
                return Cell(coord.Q, coord.R, height: 0);
            }).Append(Cell(0, 0, height: 2)).ToList();
            var map = new HexMapData(cells);

            Assert.That(HexAccessibility.IsAccessible(map, new HexCoord(0, 0), HexTerrainTraits.Default), Is.False,
                "사방과 높이 2단계 이상 차이 나는 칸은 접근 불가다.");
        }

        [Test]
        public void TileWithAtLeastOneTraversableNeighborIsAccessible()
        {
            // 이웃 하나만 높이 1(차이 1 ≤ 한계)이면 들어올 길이 있다.
            var cells = HexCoord.Directions.Select((direction, index) =>
            {
                var coord = new HexCoord(0, 0) + direction;
                return Cell(coord.Q, coord.R, height: index == 0 ? 1 : 0);
            }).Append(Cell(0, 0, height: 2)).ToList();
            var map = new HexMapData(cells);

            Assert.That(HexAccessibility.IsAccessible(map, new HexCoord(0, 0), HexTerrainTraits.Default), Is.True);
        }

        [Test]
        public void WalkDistancesNeverEnterWaterOrClimbTwoLevels()
        {
            // 일렬 지형: 시작(0,0) - 물(1,0) - 평지(2,0) / 그리고 (0,1)은 높이 2 절벽.
            // 물이 통로면 (2,0)에 닿고, 절벽을 오르면 (0,1)에 닿는다 — 둘 다 지대에 섞이면 안 된다.
            var map = new HexMapData(new[]
            {
                Cell(0, 0),
                Cell(1, 0, terrain: "hanriver-water"),
                Cell(2, 0),
                Cell(0, 1, height: 2),
            });

            var distances = HexWalkDistances.FromCoord(map, new HexCoord(0, 0));

            Assert.That(distances.ContainsKey(new HexCoord(1, 0)), Is.False, "물 타일이 보행 지대에 들어왔다.");
            Assert.That(distances.ContainsKey(new HexCoord(2, 0)), Is.False, "물 건너편이 물을 통로 삼아 닿았다.");
            Assert.That(distances.ContainsKey(new HexCoord(0, 1)), Is.False, "높이 2단계 차이를 걸어 올랐다.");
        }
    }
}

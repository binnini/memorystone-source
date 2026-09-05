using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 2026-09-05 결정 8 — 몸 크기 전용 형상 어휘. <c>body-shell</c>(둘레의 둘레)과 <c>body</c>+오프셋 합집합의
    /// 기하를 footprint별로 고정한다. 출하 CSV를 읽지 않고 로컬 카탈로그로 잰다(값은 규칙이지 저작이 아니다).
    /// </summary>
    public sealed class AttackShapeBodyShellTests
    {
        private const string Header = "shapeId,adjacency,offsets,designerNote";
        private static readonly HexCoord Origin = new HexCoord(10, 10);

        [Test]
        public void BodyShellIsTheGraphDistanceTwoShellOfTheFootprint()
        {
            var catalog = AttackShapeCatalogCsv.ConvertText(Header + "\nshell,body-shell,,\n");
            AttackShapeLibrary.Initialize(catalog);
            try
            {
                var single = AttackShapeLibrary.GetAffectedCells("shell", Origin, HexDirection.East, new MonsterBodyShape(0, null)).ToList();
                Assert.That(single, Has.Count.EqualTo(12), "1칸 몸: 거리 2 링 12칸.");
                Assert.That(single.All(cell => Origin.DistanceTo(cell) == 2), Is.True);

                var disk = AttackShapeLibrary.GetAffectedCells("shell", Origin, HexDirection.East, new MonsterBodyShape(1, null)).ToList();
                Assert.That(disk, Has.Count.EqualTo(18), "원판 r1: 중심 거리 3 링 18칸 — 붙은 둘레 12칸은 안전.");
                Assert.That(disk.All(cell => Origin.DistanceTo(cell) == 3), Is.True);

                var tri = AttackShapeLibrary.GetAffectedCells("shell", Origin, HexDirection.East, new MonsterBodyShape(0, MonsterFootprints.TriangleOffsets)).ToList();
                var body = MonsterFootprints.TriangleOffsets.Select(offset => Origin + offset).ToList();
                Assert.That(tri, Has.Count.EqualTo(15), "tri: 둘레 9칸의 바깥 껍질 15칸.");
                Assert.That(tri.All(cell => body.Min(b => b.DistanceTo(cell)) == 2), Is.True, "껍질 칸은 몸에서 정확히 거리 2다.");

                // 방향은 껍질을 바꾸지 않는다 — 안전지대는 방향이 아니라 「붙음/멀리」다.
                var west = AttackShapeLibrary.GetAffectedCells("shell", Origin, HexDirection.West, new MonsterBodyShape(0, MonsterFootprints.TriangleOffsets)).ToList();
                Assert.That(west, Is.EquivalentTo(tri));
            }
            finally
            {
                AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(System.IO.File.ReadAllText(CombatCsvPaths.AttackShapesCsv, System.Text.Encoding.UTF8)));
            }
        }

        [Test]
        public void BodyWithOffsetsUnionsThePerimeterAndTheFrontOriginOffsets()
        {
            var catalog = AttackShapeCatalogCsv.ConvertText(Header + "\nclaw,body,2:0 3:0,\nring,body,,\n");
            AttackShapeLibrary.Initialize(catalog);
            try
            {
                var tri = new MonsterBodyShape(0, MonsterFootprints.TriangleOffsets);
                var ring = AttackShapeLibrary.GetAffectedCells("ring", Origin, HexDirection.East, tri).ToList();
                var claw = AttackShapeLibrary.GetAffectedCells("claw", Origin, HexDirection.East, tri).ToList();
                var front = tri.ResolveShapeOrigin(Origin, HexDirection.East);
                Assert.That(claw, Is.SupersetOf(ring), "둘레는 그대로 남는다.");
                Assert.That(claw, Has.Member(front + new HexCoord(2, 0)).And.Member(front + new HexCoord(3, 0)),
                    "오프셋은 앞 몸통 칸(원점) 기준으로 얹힌다.");
                Assert.That(claw.Count, Is.EqualTo(ring.Count + 2), "합집합 — 둘레와 겹치지 않는 두 칸만 늘어난다.");
                Assert.That(claw.Distinct().Count(), Is.EqualTo(claw.Count));
            }
            finally
            {
                AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(System.IO.File.ReadAllText(CombatCsvPaths.AttackShapesCsv, System.Text.Encoding.UTF8)));
            }
        }
    }
}

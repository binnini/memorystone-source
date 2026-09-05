using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 형상 카탈로그(<c>attack_shapes.csv</c> · 계획 §27)의 파서·가드·출하 데이터 계약.
    ///
    /// <para>이관의 안전망은 둘이다: ①출하 CSV가 파싱·검증을 통과하고 형상 기하 불변식
    /// (도표 도구의 검증 17건과 같은 대조)을 만족하는가 — 하드코딩 시절과 같은 규칙이 데이터에서도
    /// 성립함을 고정한다. ②저작 가드가 실제로 막는가 — DEC-07-28-04 선례("저작으로 규칙을 깰 수
    /// 있는가")대로, 인접 링 opt-out에 거리 1 칸이 섞이는 저작은 파싱 단계에서 죽어야 한다.
    /// "붙으면 안전" 회피 계약이 CSV 편집 실수 하나로 무너지면 안 되기 때문이다.</para>
    /// </summary>
    public sealed class AttackShapeCatalogCsvTests
    {
        private const string Header = "shapeId,adjacency,offsets,designerNote";

        private static readonly HexCoord Origin = new HexCoord(0, 0);

        private static AttackShapeCatalogDefinition ParseShipping()
        {
            return AttackShapeCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8));
        }

        [Test]
        [Category("ShippingData")]
        public void ShippingCsvKeepsTheAuthoredShapesAndTheirAdjacencyVocabulary()
        {
            // 🔑 형상 수는 저작에서 파생한다(2026-08-31 T4) — 형상을 하나 추가할 때마다 이 줄이
            // 거짓 경보로 깨지면 안 된다. 지키는 계약은 "저작 행 하나 = 형상 하나"다.
            var catalog = ParseShipping();
            Assert.That(catalog.Shapes, Is.Not.Empty, "출하 형상을 하나도 못 읽었다.");
            Assert.That(
                catalog.Shapes, Has.Count.EqualTo(AuthoredCsvRows.Count(CombatCsvPaths.AttackShapesCsv)),
                "attack_shapes.csv 행 하나가 형상 하나다 — 수가 다르면 파서가 행을 흘렸다.");

            // 인접 어휘별 목록 — 늘거나 줄면 회피 문법이 바뀐 것이므로 의도 확인 대상.
            // none = "붙으면 안전"을 파서가 보증하는 형상(밴드형 둘 + 2026-08-18 스와이프 둘 + 요괴 넷).
            var none = ByAdjacency(catalog, AttackShapeAdjacency.None);
            Assert.That(none, Is.EquivalentTo(new[]
            {
                AttackShapeLibrary.Donut2, AttackShapeLibrary.Artillery3,
                "front-right-swipe", "wide-swipe",
                "lash-fan", "cone-far", "prongs-3", "summon-2"
            }));

            // open = 인접 링을 자동으로 깔지 않고 거리 1 칸을 저작으로 채우는 형상. "붙으면 안전"을
            // 약속하지 않으므로 거리 1 저작이 허용된다(§2-2).
            var open = ByAdjacency(catalog, AttackShapeAdjacency.Open);
            Assert.That(open, Is.EquivalentTo(new[]
            {
                "grapple-arc", "maw-deep", "prank-burst", "hook-up",
                "tail-coil", "swipe-arc", "slam-heavy", "fissure",
                // 2026-08-30 6종 개편: 어둑시니가 maw-deep을 거구귀에 넘기며 받은 그림자 형상.
                "shadow-creep",
                // 2026-09-04 기존 요괴 형상 문법 일괄 전환(full→open) — 무안전지대는 중간보스·보스
                // 전유가 됐다. 터렛 3종(single)·호랑 A010(ring-full-2)·불가살만 full에 남는다.
                "bite-front", "lunge-3", "charge-4", "stomp-fan", "pincer-open", "roar-ring",
                "slap-swing", "punch-2", "pole-thrust", "cone-long-open", "cross-mid",
                // 그슨새 A059 전용(2026-09-04 사용자 확정) — 신규 6종의 마지막 single 잔존 해소.
                "claw-rake",
                // 2026-09-04 두억시니·거구귀 tri 재저작(§3-A).
                "swipe-wide", "fissure-twin", "maw-wide",
                // 2026-09-05 불가살 개편(결정 7): 축소 브레스(전방 인접 3칸만 저작). storm-4는 인접 전부라 full.
                "breath-fan"
            }));

            // body = 몸 둘레(2026-09-04 §3-B) — 셀을 저작하지 않고 footprint에서 유도하는 형상.
            var body = ByAdjacency(catalog, AttackShapeAdjacency.Body);
            // body-claw(2026-09-05 결정 8) = 둘레 ∪ 앞 몸통 칸 기준 오프셋 — body에 오프셋을 더한 첫 저작.
            Assert.That(body, Is.EquivalentTo(new[] { "body-ring", "body-claw" }));

            // body-shell = 둘레의 둘레(2026-09-05 결정 8) — footprint에서 거리 2 껍질을 유도한다.
            var bodyShell = ByAdjacency(catalog, AttackShapeAdjacency.BodyShell);
            Assert.That(bodyShell, Is.EquivalentTo(new[] { "body-shell" }));

            // full은 나머지 전부이며, 자동 링을 쓰는 형상만 IncludeAdjacentRing이 참이다.
            // 🔑 오늘의 숫자가 아니라 **"네 어휘가 형상 전체를 빠짐없이 나눈다"**를 잠근다
            // (2026-08-31 T4). 새 형상이 어느 어휘에도 안 잡히면 여기서 걸린다.
            Assert.That(
                ByAdjacency(catalog, AttackShapeAdjacency.Full),
                Has.Count.EqualTo(catalog.Shapes.Count - none.Count - open.Count - body.Count - bodyShell.Count),
                "none·open·body·body-shell·full 다섯이 형상 전체를 나눈다 — 남는 형상이 있으면 어휘가 하나 빠진 것이다.");
            Assert.That(
                catalog.Shapes.Where(shape => shape.IncludeAdjacentRing).Select(shape => shape.Id),
                Is.EquivalentTo(ByAdjacency(catalog, AttackShapeAdjacency.Full)),
                "IncludeAdjacentRing은 full 하나만 참이다 — open이 참이 되면 인접 6칸이 두 번 깔린다.");
        }

        [Test]
        [Category("ShippingData")]
        public void AdjacencyMigrationKeepsTheSeventeenLegacyShapesCellForCell()
        {
            // 🔴 §2-2 완료 기준: includeAdjacentRing(bool) → adjacency(3어휘) 치환은 어휘만 바꾸는
            // 무변경 개조여야 한다. 스냅샷은 치환 직전 옛 파서로 뽑은 17형상 × 6방향의 해소 결과다.
            // 하나라도 어긋나면 기존 몬스터·보스·플레이어 카드의 명중 칸이 소리 없이 달라진 것이다.
            AttackShapeLibrary.Initialize(ParseShipping());

            var snapshot = AttackShapeAdjacencyMigrationSnapshot.Parse();
            Assert.That(snapshot, Has.Count.EqualTo(17), "스냅샷은 치환 직전 17형상이며 자라지 않는다.");

            foreach (var pair in snapshot)
            {
                Assert.That(AttackShapeLibrary.TryGet(pair.Key, out _), Is.True,
                    $"치환 전 형상 '{pair.Key}'가 사라졌다.");

                for (var direction = 0; direction < 6; direction++)
                {
                    var cells = string.Join(" ", Cells(pair.Key, (HexDirection)direction, 0)
                        .OrderBy(cell => cell.Q).ThenBy(cell => cell.R)
                        .Select(cell => $"{cell.Q}:{cell.R}"));
                    Assert.That(cells, Is.EqualTo(pair.Value[direction]),
                        $"{pair.Key} 방향 {direction}의 셀 집합이 치환 전과 다르다.");
                }
            }
        }

        [Test]
        [Category("ShippingData")]
        public void NewYogoeShapesKeepTheAuthoringBalanceRules()
        {
            // §2-3 형상 밸런스 기준: 인접 6칸이 공짜로 깔리지 않는 형상은 그 칸을 직접 벌어야 한다.
            // 5~8칸(summon-2는 피해 형상이 아니라 예외) · 폭 2줄 이상(한 줄짜리는 옆으로 한 칸만
            // 비켜도 통째로 빠진다).
            AttackShapeLibrary.Initialize(ParseShipping());

            var newShapes = new[]
            {
                "grapple-arc", "maw-deep", "prank-burst", "lash-fan", "hook-up", "tail-coil",
                "swipe-arc", "slam-heavy", "fissure", "cone-far", "prongs-3"
            };

            foreach (var shapeId in newShapes)
            {
                Assert.That(AttackShapeLibrary.TryGet(shapeId, out var shape), Is.True, shapeId);
                Assert.That(shape.Adjacency, Is.Not.EqualTo(AttackShapeAdjacency.Full),
                    $"{shapeId}는 인접 6칸을 자동으로 깔지 않는 신규 어휘 형상이다.");

                var cells = Cells(shapeId, HexDirection.East, 0);
                Assert.That(cells, Has.Count.EqualTo(shape.Offsets.Length),
                    $"{shapeId}: 자동 링이 없으므로 칸 수 = 저작 오프셋 수.");
                Assert.That(cells.Count, Is.InRange(4, 8), $"{shapeId}: 형상 크기 기준(5~8칸, 꺾임 형상 4칸 허용).");

                // 폭 2줄 = 정동 기준 서로 다른 r 값이 둘 이상.
                Assert.That(cells.Select(cell => cell.R).Distinct().Count(), Is.GreaterThan(1),
                    $"{shapeId}: 한 줄짜리 형상은 만들지 않는다(§2-3).");
            }

            // summon-2는 소환 자리 2칸이라 피해 형상 기준을 적용하지 않는다 — 그래도 링은 없어야 한다.
            Assert.That(AttackShapeLibrary.TryGet("summon-2", out var summon), Is.True);
            Assert.That(summon.Adjacency, Is.EqualTo(AttackShapeAdjacency.None));
            // 개수 고정 유지: summon-2는 이름 그대로 **소환 자리 2칸**이라 2가 곧 형상의 정의다.
            // 저작이 늘어도 이 숫자는 늘지 않는다(늘었다면 다른 형상이다).
            Assert.That(Cells("summon-2", HexDirection.East, 0), Has.Count.EqualTo(2));
        }

        [Test]
        public void OpenAdjacencyAllowsAuthoredDistanceOneCellsWithoutTheAutomaticRing()
        {
            // §2-1 실측: 링을 안 쓰는 경로는 저작 오프셋만 쓴다 — open은 파서 가드 한 줄만 푼 것이고
            // 런타임(GetAffectedCells)은 무변경이다.
            try
            {
                var csv = Header + "\npartial-ring,open,1:0 1:-1 2:0,\n";
                AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(csv, "custom.csv"));

                Assert.That(AttackShapeLibrary.TryGet("partial-ring", out var shape), Is.True);
                Assert.That(shape.Adjacency, Is.EqualTo(AttackShapeAdjacency.Open));
                Assert.That(shape.IncludeAdjacentRing, Is.False);
                Assert.That(Cells("partial-ring", HexDirection.East, 0), Is.EquivalentTo(new[]
                {
                    new HexCoord(1, 0), new HexCoord(1, -1), new HexCoord(2, 0)
                }), "open은 저작한 인접 칸만 맞는다 — 나머지 인접 4칸은 안전하다.");
            }
            finally
            {
                AttackShapeLibrary.Initialize(ParseShipping());
            }
        }

        [Test]
        public void UnknownAdjacencyTokenAndLegacySchemaAreRejected()
        {
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(Header + "\nbad-word,partial,2:0,\n"),
                Throws.ArgumentException.With.Message.Contain("full/none/open"));

            // 옛 스키마가 남은 CSV는 조용히 통과하면 안 된다 — true가 어휘로 오해되어 open이 full로
            // 읽히는 드리프트가 된다.
            Assert.That(
                () => AttackShapeCatalogCsv.ConvertText(
                    "shapeId,includeAdjacentRing,offsets,designerNote\nlegacy,true,2:0,\n"),
                Throws.ArgumentException.With.Message.Contain("adjacency"));
        }

        [Test]
        [Category("ShippingData")]
        public void ShippingShapesKeepTheGeometryInvariants()
        {
            // 도표 도구(tools/attack-shape-chart)의 검증 17건과 같은 대조 — 이관 전 하드코딩 표와
            // 동일한 기하가 데이터에서 나옴을 고정한다. 회전·몸 반경 경로는 GetAffectedCells 그대로.
            AttackShapeLibrary.Initialize(ParseShipping());

            var coneMid = Cells(AttackShapeLibrary.ConeMid, HexDirection.East, 0);
            Assert.That(coneMid, Has.Count.EqualTo(9), "cone-mid 동쪽·반경0 = 링 6 + 부채꼴 3.");
            Assert.That(coneMid, Does.Contain(new HexCoord(2, 0)));
            Assert.That(coneMid, Does.Contain(new HexCoord(2, -1)));
            Assert.That(coneMid, Does.Contain(new HexCoord(1, 1)));

            var donut = Cells(AttackShapeLibrary.Donut2, HexDirection.East, 0);
            Assert.That(donut, Has.Count.EqualTo(12), "donut-2 = 거리 2 링 12칸뿐.");
            Assert.That(donut.All(cell => Origin.DistanceTo(cell) == 2), Is.True, "인접 링 칸이 없어야 '붙으면 안전'.");

            // 2026-08-18 재저작: 반경3 링 18칸 + 안쪽 반경2 4칸(1:-2 1:1 -1:-1 -1:2) = 22칸.
            // 인접(1)은 여전히 안전 — "근접이 답"이라는 회피 계약은 유지된다.
            var artillery = Cells(AttackShapeLibrary.Artillery3, HexDirection.East, 0);
            Assert.That(artillery, Has.Count.EqualTo(22), "artillery-3 = 거리 3 링 18칸 + 거리 2 4칸.");
            Assert.That(artillery.All(cell => Origin.DistanceTo(cell) >= 2 && Origin.DistanceTo(cell) <= 3), Is.True);

            var ringFull = Cells(AttackShapeLibrary.RingFull2, HexDirection.East, 0);
            Assert.That(ringFull, Has.Count.EqualTo(18), "ring-full-2 = 거리 1~2 완전 원판.");
            Assert.That(ringFull.All(cell => Origin.DistanceTo(cell) >= 1 && Origin.DistanceTo(cell) <= 2), Is.True);

            // line-2: 6방 회전 시 추가 칸이 따라간다.
            var expectedLine2 = new[]
            {
                new HexCoord(2, 0), new HexCoord(2, -2), new HexCoord(0, -2),
                new HexCoord(-2, 0), new HexCoord(-2, 2), new HexCoord(0, 2)
            };
            for (var direction = 0; direction < 6; direction++)
            {
                var cells = Cells(AttackShapeLibrary.Line2, (HexDirection)direction, 0);
                var extra = cells.Where(cell => Origin.DistanceTo(cell) == 2).ToList();
                Assert.That(extra, Is.EqualTo(new[] { expectedLine2[direction] }),
                    $"line-2 방향 {direction}의 거리 2 칸.");
            }

            // 회전 대칭 형상은 어느 방향으로 펴도 같은 집합이다.
            // (artillery-3는 2026-08-18 안쪽 4칸 추가로 180° 대칭만 남아 목록에서 뺐다 — 조준 방향이 그림에 영향을 준다.)
            foreach (var shapeId in new[]
                     {
                         AttackShapeLibrary.Tremor, AttackShapeLibrary.RingFull2,
                         AttackShapeLibrary.Donut2
                     })
            {
                var east = Cells(shapeId, HexDirection.East, 0);
                for (var direction = 1; direction < 6; direction++)
                {
                    Assert.That(Cells(shapeId, (HexDirection)direction, 0), Is.EquivalentTo(east),
                        $"{shapeId}는 회전 대칭이어야 한다.");
                }
            }

            // 몸 반경 1·동쪽이면 전체가 (1,0)만큼 평행이동한다.
            var baseCells = Cells(AttackShapeLibrary.ConeMid, HexDirection.East, 0);
            var shifted = Cells(AttackShapeLibrary.ConeMid, HexDirection.East, 1);
            Assert.That(shifted, Is.EquivalentTo(baseCells.Select(cell => new HexCoord(cell.Q + 1, cell.R))));
        }

        [Test]
        [Category("ShippingData")]
        public void EveryAuthoredPatternAndCardShapeResolves()
        {
            // 참조 무결성: 패턴 CSV의 shapeId가 전부 카탈로그에 있어야 한다. (몬스터 CSV 컨버터가
            // 변환 시점에 같은 대조를 하지만, 여기서는 형상 CSV 하나만 고친 커밋도 잡히도록 따로 둔다.)
            AttackShapeLibrary.Initialize(ParseShipping());

            var patternsCsv = CsvTable.Parse(
                File.ReadAllText(CombatCsvPaths.MonsterDirectory + "/monster_attack_patterns.csv", Encoding.UTF8),
                "monster_attack_patterns.csv");
            foreach (var row in patternsCsv.Rows)
            {
                Assert.That(row.TryGet("shapeId", out var shapeId), Is.True);
                if (!string.IsNullOrWhiteSpace(shapeId))
                {
                    Assert.That(AttackShapeLibrary.TryGet(shapeId.Trim(), out _), Is.True,
                        $"monster_attack_patterns.csv:{row.LineNumber} shapeId '{shapeId}' 미해소.");
                }
            }
        }

        [Test]
        public void BodyAdjacencyDerivesCellsFromTheFootprint()
        {
            // §3-B: body는 저작 셀이 없고 footprint 둘레를 실행 시점에 편다.
            // 🔴 2026-09-05 계약 변경(사용자 확정): <b>등 뒤는 비운다</b>. 몸이 회전하지 않으므로
            // (MonsterFootprints §13.4) 앞뒤는 조준 방향이 정하고, 조준 방향 기준 가장 뒤 칸 하나를
            // 둘레 계산에서 뺀다 — 그 칸에만 닿는 이웃이 안전지대가 되어 "등 뒤로 파고들면 안 맞는다"가
            // 성립한다. 종전에는 방향과 무관한 9칸이라 어느 쪽으로 돌아도 맞았다.
            AttackShapeLibrary.Initialize(ParseShipping());

            // single footprint 폴백 = 인접 6칸(폴백 명세 — 실수로 써도 무해한 인접 공격이 된다).
            var single = Cells("body-ring", HexDirection.East, 0);
            Assert.That(single, Is.EquivalentTo(AttackShapeLibrary.AdjacentOffsets));

            var tri = AttackShapeLibrary.GetAffectedCells(
                "body-ring", Origin, HexDirection.East,
                new MonsterBodyShape(0, MonsterFootprints.TriangleOffsets)).ToList();
            Assert.That(tri.Intersect(MonsterFootprints.TriangleOffsets), Is.Empty,
                "점유 칸은 형상에 들지 않는다 — 둘레는 이웃 − 점유다.");

            // 동쪽을 보면 앵커 (0,0)이 가장 뒤다. 그 칸에만 닿는 이웃 둘이 안전해진다.
            Assert.That(tri, Has.No.Member(new HexCoord(0, -1)), "등 뒤 전용 이웃은 위협에서 빠진다.");
            Assert.That(tri, Has.No.Member(new HexCoord(-1, 0)), "등 뒤 전용 이웃은 위협에서 빠진다.");
            Assert.That(tri, Has.Member(new HexCoord(-1, 1)),
                "앞 칸에도 닿는 이웃은 등 쪽이어도 위협으로 남는다 — 빠지는 것은 '뒤 칸에만' 닿는 이웃뿐이다.");

            // 어느 방향을 보든 안전지대는 정확히 두 칸 — 형상이 세 칸이고 각 칸의 전용 이웃이 둘이라
            // 「뒤 한 칸」 규칙이 늘 같은 크기의 뒷문을 연다. 숫자가 아니라 이 <b>구조</b>가 계약이다.
            var body = new MonsterBodyShape(0, MonsterFootprints.TriangleOffsets);
            var full = new HashSet<HexCoord>();
            for (var direction = 0; direction < 6; direction++)
            {
                var facing = AttackShapeLibrary.GetAffectedCells(
                    "body-ring", Origin, (HexDirection)direction, body).ToList();
                foreach (var cell in facing)
                {
                    full.Add(cell);
                }

                var rear = body.ResolveRearOffset((HexDirection)direction);
                Assert.That(rear, Is.Not.Null, "형상 몸에는 언제나 뒤 칸이 하나 있다.");
                Assert.That(
                    facing.Any(cell => MonsterFootprints.TriangleOffsets
                        .Where(offset => offset != rear.Value)
                        .All(offset => (Origin + offset).DistanceTo(cell) > 1)),
                    Is.False,
                    $"방향 {direction}: 앞 칸 어디에도 닿지 않는 칸이 위협에 남아 있다.");
            }

            Assert.That(full, Has.Count.EqualTo(9),
                "여섯 방향을 합치면 옛 둘레 전체가 나온다 — 칸이 사라진 것이 아니라 방향마다 뒷문이 열린다.");

            // 원판 r1(보스) = 거리 2 링 12칸 — A028이 P3에서 얻는 집합. 원판에는 「뒤」가 없어 무변경이다.
            var disc = AttackShapeLibrary.GetAffectedCells("body-ring", Origin, HexDirection.East, footprintRadius: 1).ToList();
            Assert.That(disc, Has.Count.EqualTo(12), "원판 r1 둘레 = 12칸.");
            Assert.That(disc.All(cell => Origin.DistanceTo(cell) == 2), Is.True);
        }

        [Test]
        public void BodyShellRejectsAuthoredOffsetsWhileBodyUnionsThem()
        {
            // body-shell은 footprint가 정본이다 — 저작 칸이 섞이면 「붙으면 안전」을 파서가 못 지킨다.
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(Header + "\nbad-shell,body-shell,2:0,\n"),
                Throws.ArgumentException.With.Message.Contain("body-shell"));

            // body는 2026-09-05부터 오프셋을 더할 수 있다(둘레 ∪ 원점 기준 오프셋 · 결정 8).
            var union = AttackShapeCatalogCsv.ConvertText(Header + "\nclaw,body,2:0,\n");
            Assert.That(union.Shapes.Single().Adjacency, Is.EqualTo(AttackShapeAdjacency.Body));
            Assert.That(union.Shapes.Single().Offsets, Has.Length.EqualTo(1));

            // 빈 오프셋은 body에서만 허용된다.
            var ok = AttackShapeCatalogCsv.ConvertText(Header + "\ngood-body,body,,\n");
            Assert.That(ok.Shapes.Single().Adjacency, Is.EqualTo(AttackShapeAdjacency.Body));
            Assert.That(ok.Shapes.Single().IncludeAdjacentRing, Is.False,
                "body가 자동 링까지 얹으면 인접 6칸이 두 번 깔린다.");
        }

        [Test]
        public void NoneAdjacencyWithDistanceOneOffsetsIsRejected()
        {
            // 🔒 핵심 가드: adjacency=none 형상에 거리 1 칸 — "붙으면 안전"을 저작으로 깨는 바로 그 실수.
            // open이 생겼다고 이 가드가 느슨해지면 안 된다: 거리 1을 원하면 어휘를 바꿔 적어야 한다.
            var csv = Header + "\nbad-band,none,1:0 2:0,\n";
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(csv),
                Throws.ArgumentException.With.Message.Contain("거리 2 이상"));
        }

        [Test]
        public void RingOptOutWithNoOffsetsIsRejected()
        {
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(Header + "\nbad-empty,none,,\n"),
                Throws.ArgumentException);
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(Header + "\nbad-open-empty,open,,\n"),
                Throws.ArgumentException, "open도 오프셋이 없으면 아무 칸도 맞히지 않는다.");
        }

        [Test]
        public void OriginOffsetAndDuplicatesAndBadTokensAreRejected()
        {
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(Header + "\nbad-origin,full,0:0,\n"),
                Throws.ArgumentException, "원점(0:0)은 형상 칸이 될 수 없다.");
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(Header + "\nbad-dup,full,2:0 2:0,\n"),
                Throws.ArgumentException, "중복 오프셋은 저작 실수다.");
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(Header + "\nbad-token,full,2;0,\n"),
                Throws.ArgumentException, "오프셋은 q:r 형식이어야 한다.");
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(Header + "\nbad-far,full,7:0,\n"),
                Throws.ArgumentException, "거리 7은 상한(6) 밖 — 오타로 취급한다.");
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(Header + "\ndup,full,2:0,\ndup,full,3:0,\n"),
                Throws.ArgumentException, "shapeId 중복.");
            Assert.That(() => AttackShapeCatalogCsv.ConvertText(Header + "\nBadCase,full,2:0,\n"),
                Throws.ArgumentException, "shapeId는 kebab-case.");
        }

        [Test]
        public void ValidCustomCatalogRoundTripsThroughTheLibrary()
        {
            // Initialize가 정말 테이블을 교체하는지 — 그리고 시험이 끝나면 출하 카탈로그로 되돌려
            // 다른 시험(파일 폴백에 기대는 기존 스위트)을 오염시키지 않는지까지 이 시험의 계약이다.
            try
            {
                var csv = Header + "\ncustom-jab,full,2:0,\n";
                AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(csv, "custom.csv"));

                Assert.That(AttackShapeLibrary.TryGet("custom-jab", out var shape), Is.True);
                Assert.That(shape.Offsets, Is.EqualTo(new[] { new HexCoord(2, 0) }));
                Assert.That(AttackShapeLibrary.TryGet(AttackShapeLibrary.ConeMid, out _), Is.False,
                    "Initialize는 병합이 아니라 교체다.");
            }
            finally
            {
                AttackShapeLibrary.Initialize(ParseShipping());
            }
        }

        private static System.Collections.Generic.List<string> ByAdjacency(
            AttackShapeCatalogDefinition catalog, AttackShapeAdjacency adjacency) =>
            catalog.Shapes.Where(shape => shape.Adjacency == adjacency).Select(shape => shape.Id).ToList();

        private static System.Collections.Generic.List<HexCoord> Cells(string shapeId, HexDirection direction, int footprint) =>
            AttackShapeLibrary.GetAffectedCells(shapeId, Origin, direction, footprint).ToList();
    }
}

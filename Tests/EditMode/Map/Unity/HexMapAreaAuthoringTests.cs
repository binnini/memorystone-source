using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.MapDesign.Editor;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    /// <summary>
    /// 보스 아레나 저작 표면과 검증기 규칙(P3). 아레나 저작 실수는 런타임에 "영영 닫히지 않는 결계" 또는
    /// "싸울 수 없는 주머니"로 나타나 판에서야 드러나므로, 빌드/검증 단계에서 전부 거부하는 것이 계약이다.
    /// </summary>
    public sealed class HexMapAreaAuthoringTests
    {
        private const string BossSpawnId = "boss-spawn";
        private const string ArenaId = "boss-arena-1";
        private static readonly HexCoord ArenaCenter = new HexCoord(0, 0);

        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in created)
            {
                Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        [Test]
        public void BoundaryRingIsTheCellsJustOutsideTheArea()
        {
            var area = new HexMapAreaRef(ArenaId, HexArea.CellsWithin(ArenaCenter, 2), HexMapAreaRef.BossArenaPurpose, BossSpawnId);

            var ring = area.EnumerateBoundaryRing().ToList();

            Assert.That(ring, Is.Not.Empty);
            Assert.That(ring, Is.Unique);
            Assert.That(ring.All(coord => ArenaCenter.DistanceTo(coord) == 3), Is.True, "링은 바깥 한 겹뿐이다.");
            Assert.That(ring.Any(area.Contains), Is.False, "링은 영역 안쪽을 포함하지 않는다.");
            Assert.That(ring, Has.Count.EqualTo(18), "반경 3 고리는 18칸이다.");
        }

        [Test]
        public void SingleCellAreaRingIsItsSixNeighbours()
        {
            var area = new HexMapAreaRef("a", new[] { ArenaCenter }, HexMapAreaRef.BossArenaPurpose, BossSpawnId);

            Assert.That(area.EnumerateBoundaryRing().Count(), Is.EqualTo(6));
        }

        /// <summary>
        /// 중심은 기물 배치의 기준점이다(보스가 아니라 아레나가 기준이어야 배치가 보스를 따라다니지 않는다).
        /// 반경 대칭 영역에서는 저작 중심과 같아야 하고, 어떤 모양이든 <b>영역 안</b>이어야 한다 —
        /// 밖으로 나가면 그 기준점을 쓰는 쪽이 결계 밖에 기물을 놓게 된다.
        /// </summary>
        [Test]
        public void CentreOfARadialAreaIsItsAuthoredCentre()
        {
            var area = new HexMapAreaRef(ArenaId, HexArea.CellsWithin(ArenaCenter, 3), HexMapAreaRef.BossArenaPurpose, BossSpawnId);

            Assert.That(area.GetCenter(), Is.EqualTo(ArenaCenter));
        }

        [Test]
        public void CentreOfARingShapedAreaSnapsBackInsideTheArea()
        {
            // 도넛 모양: 무게중심은 구멍 한가운데라 영역 밖이다. 그래도 중심은 영역 안이어야 한다.
            var donut = HexArea.CellsWithin(ArenaCenter, 3)
                .Where(coord => ArenaCenter.DistanceTo(coord) >= 2)
                .ToList();
            var area = new HexMapAreaRef(ArenaId, donut, HexMapAreaRef.BossArenaPurpose, BossSpawnId);

            var centre = area.GetCenter();

            Assert.That(area.Contains(centre), Is.True, "중심은 언제나 영역 안이어야 한다.");
            Assert.That(ArenaCenter.DistanceTo(centre), Is.EqualTo(2), "구멍에서 가장 가까운 고리로 스냅한다.");
        }

        [Test]
        public void PurposeComparisonIgnoresCase()
        {
            Assert.That(new HexMapAreaRef("a", new[] { ArenaCenter }, "Boss-Arena").IsBossArena, Is.True);
            Assert.That(new HexMapAreaRef("a", new[] { ArenaCenter }, "patrol").IsBossArena, Is.False);
            Assert.That(new HexMapAreaRef("a", new[] { ArenaCenter }).IsBossArena, Is.False, "용도 없는 영역은 결계가 아니다.");
        }

        [Test]
        public void WellFormedArenaBuildsIntoMapData()
        {
            var source = CreateSource(arenaRadius: 2);

            Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
            Assert.That(map.Areas.Count, Is.EqualTo(1));
            var area = map.Areas[0];
            Assert.That(area.Id, Is.EqualTo(ArenaId));
            Assert.That(area.IsBossArena, Is.True);
            Assert.That(area.BossSpawnRefId, Is.EqualTo(BossSpawnId));
            Assert.That(area.Contains(ArenaCenter), Is.True);
        }

        [Test]
        public void MapsWithoutAreasStillBuild()
        {
            var source = CreateSource(arenaRadius: null);

            Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
            Assert.That(map.Areas, Is.Empty);
        }

        [Test]
        public void ArenaWithoutABoundSpawnIsRejected()
        {
            var source = CreateSource(arenaRadius: 2, bossSpawnRefId: string.Empty);

            Assert.That(source.TryToHexMapData(out _, out var error), Is.False);
            Assert.That(error, Does.Contain("bossSpawnRefId"));
        }

        [Test]
        public void ArenaBoundToAMissingSpawnIsRejected()
        {
            var source = CreateSource(arenaRadius: 2, bossSpawnRefId: "no-such-spawn");

            Assert.That(source.TryToHexMapData(out _, out var error), Is.False);
            Assert.That(error, Does.Contain("missing monster spawn ref"));
        }

        [Test]
        public void ArenaThatDoesNotContainItsBossIsRejected()
        {
            // 아레나를 보스에서 멀리 떨어진 곳에 만든다 — 결계가 닫혀도 보스가 밖에 있게 된다.
            var source = CreateSource(arenaRadius: 1, arenaCenterOverride: new HexCoord(4, 0));

            Assert.That(source.TryToHexMapData(out _, out var error), Is.False);
            Assert.That(error, Does.Contain("does not contain its bound boss spawn"));
        }

        [Test]
        public void ValidatorReportsAnArenaBoundToANonBossSpawn()
        {
            var source = CreateSource(arenaRadius: 2, spawnRole: MonsterSpawnRoles.NormalEnemy);

            var report = HexMapValidationUtility.Validate(source, (IEnumerable<string>)null);

            Assert.That(Errors(report).Any(message => message.Contains("expected `boss`")), Is.True,
                string.Join(" | ", Errors(report)));
        }

        [Test]
        public void ValidatorAcceptsAWellFormedArena()
        {
            var source = CreateSource(arenaRadius: 2);

            var report = HexMapValidationUtility.Validate(source, (IEnumerable<string>)null);

            Assert.That(report.HasErrors, Is.False, string.Join(" | ", Errors(report)));
            Assert.That(Infos(report).Any(message => message.Contains("Boss arena")), Is.True);
        }

        [Test]
        public void ValidatorWarnsWhenThePlayerStartsInsideTheArena()
        {
            // 아레나가 판 전체를 덮으면 PlayerSpawn도 그 안이다 — 두 경고가 함께 나와야 한다.
            var source = CreateSource(arenaRadius: 5);

            var report = HexMapValidationUtility.Validate(source, (IEnumerable<string>)null);

            Assert.That(report.HasErrors, Is.False, string.Join(" | ", Errors(report)));
            Assert.That(Warnings(report).Any(message => message.Contains("contains a PlayerSpawn")), Is.True,
                string.Join(" | ", Warnings(report)));
            Assert.That(Warnings(report).Any(message => message.Contains("barrier would seal nothing")), Is.True,
                string.Join(" | ", Warnings(report)));
        }

        private static string[] Errors(HexMapValidationReport report) =>
            report.Errors.Select(item => item.Message).ToArray();

        private static string[] Warnings(HexMapValidationReport report) =>
            report.Warnings.Select(item => item.Message).ToArray();

        private static string[] Infos(HexMapValidationReport report) =>
            report.Items.Where(item => item.Severity == HexMapValidationSeverity.Info).Select(item => item.Message).ToArray();

        [Test]
        public void ErasingACellDropsItFromEveryArea()
        {
            var source = CreateSource(arenaRadius: 2);
            var victim = new HexCoord(1, 0);
            Assert.That(source.TryGetAreaRef(ArenaId, out var before), Is.True);
            Assert.That(before.Cells.Any(cell => cell.Coord == victim), Is.True);

            Assert.That(source.RemoveCell(victim), Is.True);

            Assert.That(source.TryGetAreaRef(ArenaId, out var after), Is.True);
            Assert.That(after.Cells.Any(cell => cell.Coord == victim), Is.False,
                "지운 셀이 영역에 남으면 맵 빌드가 '없는 좌표'로 거부한다.");
        }

        [Test]
        public void RemovingTheBoundSpawnDropsTheArenaInsteadOfBreakingTheMap()
        {
            var source = CreateSource(arenaRadius: 2);

            Assert.That(source.RemoveObjectRef(BossSpawnId), Is.True);

            Assert.That(source.TryGetAreaRef(ArenaId, out _), Is.False,
                "바인딩이 끊긴 아레나가 남으면 맵 전체가 로드되지 않는다.");
            Assert.That(source.TryToHexMapData(out _, out var error), Is.True, error);
        }

        private HexSparseMapAuthoringSource CreateSource(
            int? arenaRadius,
            string bossSpawnRefId = BossSpawnId,
            string spawnRole = MonsterSpawnRoles.Boss,
            HexCoord? arenaCenterOverride = null,
            int boardRadius = 5)
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            source.name = "arena-test-source";
            created.Add(source);

            var cells = HexArea.CellsWithin(ArenaCenter, boardRadius)
                .Select(coord => new HexSparseMapAuthoringCell(coord, "tile", "street", "mvp-0"))
                .ToArray();
            var objectRefs = new List<HexMapObjectRef>
            {
                new HexMapObjectRef(
                    BossSpawnId,
                    HexMapObjectType.MonsterSpawn,
                    "M002",
                    ArenaCenter.Q,
                    ArenaCenter.R,
                    spawnRole,
                    HexMapPurpose.Unspecified),
                new HexMapObjectRef(
                    "player-start",
                    HexMapObjectType.PlayerSpawn,
                    "player",
                    -boardRadius,
                    0,
                    string.Empty,
                    HexMapPurpose.Unspecified)
            };

            var areaRefs = new List<HexMapAreaAuthoringRef>();
            if (arenaRadius.HasValue)
            {
                areaRefs.Add(new HexMapAreaAuthoringRef(
                    ArenaId,
                    HexArea.CellsWithin(arenaCenterOverride ?? ArenaCenter, arenaRadius.Value)
                        .Where(coord => ArenaCenter.DistanceTo(coord) <= boardRadius),
                    HexMapAreaRef.BossArenaPurpose,
                    bossSpawnRefId));
            }

            source.ConfigureForTests(cells, HexMapPurpose.PlayableMap, objectRefs, null, null, areaRefs);
            return source;
        }
    }
}

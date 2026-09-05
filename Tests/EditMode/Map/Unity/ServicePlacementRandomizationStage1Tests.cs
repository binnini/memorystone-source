#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    /// <summary>
    /// 서비스 오브젝트 추첨 배치 감사(2026-09-01). 시드 스윕으로 이 설계의 <b>존재 이유</b>를 문다:
    /// 어느 루트를 걷든 잡화점·캠핑카를 각 2번 이상 만나는가.
    ///
    /// <para>
    /// 함께 무는 것 — ①동종 최소 거리 ②결정성 ③몬스터·함정 스트림 불변(서비스 추가가 기존 시드의
    /// 배치를 흔들지 않았는가) ④<b>상자가 옮겨진 서비스를 본다</b>(순서 계약의 감시자).
    /// </para>
    /// </summary>
    public sealed class ServicePlacementRandomizationStage1Tests
    {
        private const string Stage1SourcePath = "Assets/Data/Map/Authoring/Stage_1_Source.asset";
        private const string Stage1Id = "stage_001_prototype";
        private const string ServiceGroupPrefix = "svc-";
        private const int SweepSeedCount = 120;

        private static HexSparseMapAuthoringSource LoadSource()
        {
            var source = AssetDatabase.LoadAssetAtPath<HexSparseMapAuthoringSource>(Stage1SourcePath);
            Assert.That(source, Is.Not.Null, $"Stage_1 authoring source not found at {Stage1SourcePath}.");
            return source;
        }

        private static StageRandomizationProfile LoadProfile()
        {
            Assert.That(
                StageRandomizationProfileSource.TryLoadProfile(Stage1Id, out var profile, out var error),
                Is.True, $"Stage_1 randomization profile load failed: {error}");
            Assert.That(profile.Enabled, Is.True, "Stage_1 프로파일이 꺼져 있다.");
            return profile;
        }

        private static HexMapData BuildBaseMap(HexSparseMapAuthoringSource source)
        {
            Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
            return map;
        }

        private static IReadOnlyList<string> RouteIdsOf(HexMapObjectRef slot)
        {
            var group = slot.RandomizationGroup.Trim();
            if (!group.StartsWith(ServiceGroupPrefix))
            {
                return new string[0];
            }

            return group.Substring(ServiceGroupPrefix.Length)
                .Split('|')
                .Select(id => id.Trim())
                .Where(id => id.Length > 0)
                .ToArray();
        }

        /// <summary>좌표 → 그 칸을 지나는 루트 id들. 후보 슬롯의 저작 태그가 정본이다.</summary>
        private static Dictionary<HexCoord, IReadOnlyList<string>> RoutesByCoord(HexSparseMapAuthoringSource source)
        {
            return source.ObjectRefs
                .Where(objectRef => objectRef != null && objectRef.IsServiceObject)
                .Where(objectRef => RouteIdsOf(objectRef).Count > 0)
                .ToDictionary(objectRef => objectRef.Coord, RouteIdsOf);
        }

        private static List<HexMapObjectData> ServicesOf(HexMapData map)
        {
            return map.ObjectRefs.Where(objectRef => objectRef.IsShop || objectRef.IsCamperVan).ToList();
        }

        private static HexMapData Randomize(
            HexSparseMapAuthoringSource source, HexMapData baseMap, StageRandomizationProfile profile, int seed)
        {
            Assert.That(
                HexMapPlacementRandomization.TryApplyProfile(
                    source, baseMap, seed, profile, out var randomized, out var evidence),
                Is.True, $"seed={seed} 랜덤화 실패: {evidence}");
            return randomized;
        }

        [Test]
        public void EveryRouteMeetsEachServiceKindTwice()
        {
            // 이 트랙의 존재 이유. 실패하면 「우회로를 택한 판에서 캠핑카를 한 번도 못 만난다」가 된다.
            var source = LoadSource();
            var profile = LoadProfile();
            var baseMap = BuildBaseMap(source);
            var routesByCoord = RoutesByCoord(source);
            var allRoutes = routesByCoord.Values.SelectMany(ids => ids).Distinct().OrderBy(id => id).ToList();
            Assert.That(allRoutes.Count, Is.GreaterThanOrEqualTo(2), "루트 태그가 2종 미만이다.");

            for (var seed = 1; seed <= SweepSeedCount; seed++)
            {
                var map = Randomize(source, baseMap, profile, seed);
                foreach (var isShop in new[] { true, false })
                {
                    var placed = ServicesOf(map)
                        .Where(objectRef => isShop ? objectRef.IsShop : objectRef.IsCamperVan)
                        .ToList();
                    foreach (var route in allRoutes)
                    {
                        var met = placed.Count(objectRef =>
                            routesByCoord.TryGetValue(objectRef.Coord, out var ids) && ids.Contains(route));
                        Assert.That(met, Is.GreaterThanOrEqualTo(profile.ServiceRouteMin),
                            $"seed={seed}: {(isShop ? "잡화점" : "캠핑카")}이 루트 '{route}'에서 {met}번만 만난다.");
                    }
                }
            }
        }

        [Test]
        public void SameKindServicesKeepTheMinimumDistance()
        {
            var source = LoadSource();
            var profile = LoadProfile();
            var baseMap = BuildBaseMap(source);
            for (var seed = 1; seed <= SweepSeedCount; seed++)
            {
                var map = Randomize(source, baseMap, profile, seed);
                foreach (var isShop in new[] { true, false })
                {
                    var coords = ServicesOf(map)
                        .Where(objectRef => isShop ? objectRef.IsShop : objectRef.IsCamperVan)
                        .Select(objectRef => objectRef.Coord)
                        .ToList();
                    for (var i = 0; i < coords.Count; i++)
                    {
                        for (var j = i + 1; j < coords.Count; j++)
                        {
                            Assert.That(coords[i].DistanceTo(coords[j]),
                                Is.GreaterThanOrEqualTo(profile.ServiceMinDistance),
                                $"seed={seed}: {(isShop ? "잡화점" : "캠핑카")} 두 개가 " +
                                $"{coords[i]}·{coords[j]}로 최소 거리 미만이다.");
                        }
                    }
                }
            }
        }

        [Test]
        public void ServicesLandOnAuthoredSlotsAndNeverOverlap()
        {
            // 추첨 결과는 반드시 저작된 후보 칸 위에 있어야 한다 — 런타임이 자기 마음대로 칸을
            // 만들어내면 디자이너가 통제할 수 없는 배치가 된다.
            var source = LoadSource();
            var profile = LoadProfile();
            var baseMap = BuildBaseMap(source);
            var slotCoords = new HashSet<HexCoord>(RoutesByCoord(source).Keys);
            for (var seed = 1; seed <= SweepSeedCount; seed++)
            {
                var map = Randomize(source, baseMap, profile, seed);
                var services = ServicesOf(map);
                Assert.That(services.Count, Is.EqualTo(profile.ServiceShopCount + profile.ServiceCamperCount),
                    $"seed={seed}: 서비스 총수가 프로파일 값과 다르다.");
                foreach (var service in services)
                {
                    Assert.That(slotCoords.Contains(service.Coord), Is.True,
                        $"seed={seed}: {service.ObjectId}가 저작되지 않은 칸 {service.Coord}에 섰다.");
                }

                var coords = services.Select(service => service.Coord).ToList();
                Assert.That(coords.Distinct().Count(), Is.EqualTo(coords.Count),
                    $"seed={seed}: 서비스 두 개가 같은 칸에 겹쳤다.");

                // objectId는 보존돼야 한다 — 소비 기록(ClaimedEventObjectIds)의 키다.
                var authoredIds = ServicesOf(baseMap).Select(service => service.ObjectId).OrderBy(id => id);
                var placedIds = services.Select(service => service.ObjectId).OrderBy(id => id);
                Assert.That(placedIds, Is.EqualTo(authoredIds), $"seed={seed}: 서비스 objectId 집합이 바뀌었다.");
            }
        }

        [Test]
        public void ChestsRespectTheMovedServicePositions()
        {
            // 🔴 순서 계약의 감시자. 상자 후보 수집이 <b>옮겨진</b> 서비스 좌표를 봐야 한다 —
            // baseMap의 옛 좌표를 보면 상자가 새 서비스 옆에 조용히 붙는다(예외도 로그도 없다).
            var source = LoadSource();
            var profile = LoadProfile();
            var baseMap = BuildBaseMap(source);
            const int serviceChestBanRadius = 2;
            for (var seed = 1; seed <= SweepSeedCount; seed++)
            {
                var map = Randomize(source, baseMap, profile, seed);
                var serviceCoords = ServicesOf(map).Select(service => service.Coord).ToList();
                var chestCoords = map.ObjectRefs
                    .Where(objectRef => objectRef.IsTreasureChest)
                    .Select(objectRef => objectRef.Coord)
                    .ToList();
                foreach (var chest in chestCoords)
                {
                    foreach (var service in serviceCoords)
                    {
                        Assert.That(chest.DistanceTo(service), Is.GreaterThan(serviceChestBanRadius),
                            $"seed={seed}: 상자 {chest}가 서비스 {service}의 금지 반경 안에 있다 — " +
                            "서비스 배치가 상자 추첨보다 먼저 돌지 않았거나, 옮겨진 좌표가 전달되지 않았다.");
                    }
                }
            }
        }

        [Test]
        public void SameSeedProducesTheSameServicePlacement()
        {
            var source = LoadSource();
            var profile = LoadProfile();
            var baseMap = BuildBaseMap(source);
            for (var seed = 1; seed <= 30; seed++)
            {
                var first = ServicesOf(Randomize(source, baseMap, profile, seed))
                    .OrderBy(service => service.ObjectId)
                    .Select(service => $"{service.ObjectId}@{service.Coord}")
                    .ToList();
                var second = ServicesOf(Randomize(source, baseMap, profile, seed))
                    .OrderBy(service => service.ObjectId)
                    .Select(service => $"{service.ObjectId}@{service.Coord}")
                    .ToList();
                Assert.That(second, Is.EqualTo(first), $"seed={seed}: 같은 시드가 다른 서비스 배치를 냈다.");
            }
        }

        [Test]
        public void ServicePlacementVariesAcrossSeeds()
        {
            // 결정성만 물으면 「항상 같은 자리」도 통과한다 — 추첨이 실제로 도는지 함께 본다.
            var source = LoadSource();
            var profile = LoadProfile();
            var baseMap = BuildBaseMap(source);
            var layouts = new HashSet<string>();
            for (var seed = 1; seed <= SweepSeedCount; seed++)
            {
                var layout = string.Join("|", ServicesOf(Randomize(source, baseMap, profile, seed))
                    .OrderBy(service => service.ObjectId)
                    .Select(service => $"{service.ObjectId}@{service.Coord}"));
                layouts.Add(layout);
            }

            Assert.That(layouts.Count, Is.GreaterThan(5),
                $"{SweepSeedCount}개 시드에서 서로 다른 배치가 {layouts.Count}가지뿐이다 — 추첨이 사실상 안 돈다.");
        }

        [Test]
        public void MonsterAndTrapStreamsAreUnaffectedByServicePlacement()
        {
            // 서비스는 시드 스트림 3을 쓴다. 몬스터(원시 시드)·함정(1)을 건드리지 않았음을 증명한다 —
            // 서비스 추첨을 껐을 때와 켰을 때의 몬스터·함정 배치가 같아야 한다.
            var source = LoadSource();
            var profile = LoadProfile();
            var baseMap = BuildBaseMap(source);
            // 🔴 손으로 재조립하면 새 컬럼이 조용히 기본값으로 떨어져 <b>가짜 실패</b>가 난다
            //    (2026-09-01: eliteMinDistance가 빠져 「서비스가 몬스터를 흔들었다」로 잘못 나왔다).
            //    사본은 언제나 프로파일 자신이 만든다.
            var withoutServices = profile.WithServiceCounts(0, 0);

            for (var seed = 1; seed <= 30; seed++)
            {
                var on = Randomize(source, baseMap, profile, seed);
                var off = Randomize(source, baseMap, withoutServices, seed);

                var monstersOn = on.MonsterSpawnRefs.OrderBy(spawn => spawn.Coord)
                    .Select(spawn => $"{spawn.MonsterId}@{spawn.Coord}").ToList();
                var monstersOff = off.MonsterSpawnRefs.OrderBy(spawn => spawn.Coord)
                    .Select(spawn => $"{spawn.MonsterId}@{spawn.Coord}").ToList();
                Assert.That(monstersOn, Is.EqualTo(monstersOff), $"seed={seed}: 서비스 추첨이 몬스터 배치를 흔들었다.");

                var trapsOn = on.TrapRefs.OrderBy(trap => trap.Coord).Select(trap => $"{trap.Coord}").ToList();
                var trapsOff = off.TrapRefs.OrderBy(trap => trap.Coord).Select(trap => $"{trap.Coord}").ToList();
                Assert.That(trapsOn, Is.EqualTo(trapsOff), $"seed={seed}: 서비스 추첨이 함정 배치를 흔들었다.");
            }
        }
    }
}
#endif

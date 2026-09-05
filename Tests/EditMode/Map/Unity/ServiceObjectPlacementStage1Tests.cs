#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    /// <summary>
    /// 출하 맵(Stage_1) 서비스 오브젝트 실배치 감사 — 저작 유실 게이트.
    ///
    /// <para>
    /// 2026-09-01 개편으로 계약이 셋 바뀌었다. ①잡화점·캠핑카가 각 3개이고 공작소는 맵에서
    /// <b>사라졌다</b>(사용자 확정). ②footprint가 7칸에서 <b>1칸</b>으로 줄었다 — 회귀가 아니라
    /// 설계 변경이라, 옛 「7칸」 단언을 그대로 두면 거짓 경보가 된다. ③후보지 18곳이
    /// <c>svc-</c> 태그로 저작되고 그중 12곳은 <b>예비 슬롯</b>(objectRef 없음)이다.
    /// </para>
    ///
    /// 좌표 자체는 디자이너 재량이라 고정하지 않는다 — 고정하는 것은 개수·불변식·루트 조건이다.
    /// </summary>
    public sealed class ServiceObjectPlacementStage1Tests
    {
        private const string Stage1SourcePath = "Assets/Data/Map/Authoring/Stage_1_Source.asset";
        private const string ServiceGroupPrefix = "svc-";

        private static readonly (HexMapObjectType Type, string ObjectRef, int Count)[] Expected =
        {
            (HexMapObjectType.Shop, "popup_shop_tmp", 3),
            (HexMapObjectType.CamperVan, "camper_van_tmp", 3),
        };

        private static HexSparseMapAuthoringSource LoadSource()
        {
            var source = AssetDatabase.LoadAssetAtPath<HexSparseMapAuthoringSource>(Stage1SourcePath);
            Assert.That(source, Is.Not.Null, $"Stage_1 authoring source not found at {Stage1SourcePath}.");
            return source;
        }

        private static List<HexMapObjectRef> ServiceSlots(HexSparseMapAuthoringSource source) =>
            source.ObjectRefs
                .Where(candidate => candidate != null && candidate.IsServiceObject)
                .ToList();

        private static List<HexMapObjectRef> PlacedServices(HexSparseMapAuthoringSource source) =>
            ServiceSlots(source).Where(candidate => !candidate.IsRandomizationSpareSlot).ToList();

        [Test]
        public void Stage1HasTheConfirmedServiceObjectPlacements()
        {
            var source = LoadSource();
            var placed = PlacedServices(source);
            foreach (var (type, objectRef, count) in Expected)
            {
                var ofType = placed.Where(candidate => candidate.ObjectType == type).ToList();
                Assert.That(ofType.Count, Is.EqualTo(count),
                    $"Stage_1 {type} 실물 배치 수가 확정값과 다르다 — 저작이 유실됐거나 무단 증감됐다.");
                Assert.That(ofType.All(candidate => candidate.ObjectRef == objectRef), Is.True,
                    $"Stage_1 {type} objectRef가 '{objectRef}'가 아니다.");
            }

            Assert.That(placed.Count, Is.EqualTo(Expected.Sum(entry => entry.Count)),
                "실물 서비스 총수가 확정값과 다르다.");
        }

        [Test]
        public void WorkshopIsGoneFromTheAuthoredMap()
        {
            // 2026-09-01 사용자 확정: 공작소는 스위치로 끄는 것을 넘어 오브젝트 자체를 지운다.
            // ServiceObjectAvailability.WorkshopEnabled를 되살려도 맵에 엔트리가 없으면 서지 않는다 —
            // 그 사실을 여기서 못 박아, 「스위치만 켜면 돌아온다」는 오해를 막는다.
            var source = LoadSource();
            var workshops = source.ObjectRefs
                .Where(candidate => candidate != null && candidate.ObjectType == HexMapObjectType.Workshop)
                .ToList();
            Assert.That(workshops, Is.Empty, "공작소 엔트리가 맵에 남아 있다 — 삭제 확정과 어긋난다.");
        }

        [Test]
        public void EveryServiceSlotCarriesARouteTag()
        {
            // 「어느 루트를 걷든 각 2회」는 코드가 아니라 이 태그가 지킨다. 태그 없는 슬롯은
            // 추첨 후보에서 조용히 빠지므로(브리지가 접두사로 거른다) 여기서 잡지 않으면
            // 후보지 하나가 소리 없이 사라진다.
            var source = LoadSource();
            var slots = ServiceSlots(source);
            Assert.That(slots.Count, Is.EqualTo(18), "서비스 후보 슬롯 수가 확정값(18)과 다르다.");

            foreach (var slot in slots)
            {
                var group = slot.RandomizationGroup.Trim();
                Assert.That(group.StartsWith(ServiceGroupPrefix), Is.True,
                    $"{slot.ObjectId}: randomizationGroup '{group}'이 '{ServiceGroupPrefix}'로 시작하지 않는다.");
                var routes = group.Substring(ServiceGroupPrefix.Length)
                    .Split('|')
                    .Select(id => id.Trim())
                    .Where(id => id.Length > 0)
                    .ToList();
                Assert.That(routes, Is.Not.Empty, $"{slot.ObjectId}: 루트 id가 하나도 없다.");
            }

            var spares = slots.Where(slot => slot.IsRandomizationSpareSlot).ToList();
            Assert.That(spares.Count, Is.EqualTo(12), "서비스 예비 슬롯 수가 확정값(12)과 다르다.");
        }

        [Test]
        public void SpareServiceSlotsNeverReachTheBuiltMap()
        {
            // 예비 슬롯은 좌표 후보일 뿐이다. 맵 빌드가 건너뛰지 않으면 objectRef 없는 물건이
            // 서거나(빈 프리팹) 빌드가 통째로 깨진다.
            var source = LoadSource();
            Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
            var built = map.ObjectRefs.Where(candidate => candidate.IsShop || candidate.IsCamperVan).ToList();
            Assert.That(built.Count, Is.EqualTo(Expected.Sum(entry => entry.Count)),
                "빌드된 맵의 서비스 수가 실물 수와 다르다 — 예비 슬롯이 새어 들어왔다.");
            Assert.That(built.All(candidate => !string.IsNullOrWhiteSpace(candidate.ObjectRef)), Is.True,
                "빌드된 서비스 중 objectRef가 빈 것이 있다.");
        }

        [Test]
        public void ServiceObjectsSatisfyTheCommonTriggerGate()
        {
            // interactable=1이 아니면 3종 공통 게이트(candidate.Interactable)가 침묵하고,
            // blocksMovement=1이면 밟기 트리거 자신이 죽는다 — 저작 표면의 양대 함정.
            // 예비 슬롯은 세울 물건이 아니므로 이 게이트의 대상이 아니다.
            var source = LoadSource();
            var services = PlacedServices(source);
            Assert.That(services, Is.Not.Empty);

            foreach (var service in services)
            {
                Assert.That(service.Interactable, Is.True, $"{service.ObjectId}: interactable=0 — 밟아도 아무 일도 안 일어난다.");
                Assert.That(service.BlocksMovement, Is.False, $"{service.ObjectId}: blocksMovement=1 — 밟기 트리거가 자기 기능을 죽인다.");
                Assert.That(service.BlocksVision, Is.False, $"{service.ObjectId}: blocksVision=1 — 저작 계약 위반.");
            }
        }

        [Test]
        public void ServiceSlotsOccupySingleCellFootprints()
        {
            // 2026-09-01 확정: footprint 1칸. 트리거(GetObjectsAt)가 OccupiedCoords 인덱스를 읽으므로
            // 이제 중심 한 칸만 발화한다 — 좁아진 발동면은 개수 증가·측면 배치·암시야 노출이 메운다.
            // 실물과 예비를 모두 검사한다: 예비 슬롯도 추첨되면 실물이 서는 자리다.
            var source = LoadSource();
            Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);

            var trapCoords = new HashSet<HexCoord>(source.TrapRefs
                .Where(trapRef => trapRef != null)
                .Select(trapRef => trapRef.Coord));
            var monsterCoords = new HashSet<HexCoord>(source.ObjectRefs
                .Where(candidate => candidate != null && candidate.IsMonsterSpawn)
                .Select(candidate => candidate.Coord));

            foreach (var slot in ServiceSlots(source))
            {
                var occupied = slot.OccupiedCoords.ToList();
                Assert.That(occupied.Count, Is.EqualTo(1),
                    $"{slot.ObjectId}: footprint가 1칸이 아니다 — 2026-09-01 확정 위반.");
                Assert.That(occupied[0], Is.EqualTo(slot.Coord), $"{slot.ObjectId}: 점유칸이 중심이 아니다.");

                var coord = occupied[0];
                Assert.That(map.TryGetCell(coord, out var cell), Is.True, $"{slot.ObjectId}: {coord} 셀 없음.");
                Assert.That(cell.BaseWalkable, Is.True, $"{slot.ObjectId}: {coord} 통행 불가.");
                Assert.That(string.IsNullOrEmpty(cell.EventId), Is.True, $"{slot.ObjectId}: {coord} 이벤트 셀과 겹침.");
                Assert.That(string.IsNullOrEmpty(cell.LandmarkId), Is.True, $"{slot.ObjectId}: {coord} 랜드마크와 겹침.");
                Assert.That(trapCoords.Contains(coord), Is.False, $"{slot.ObjectId}: {coord}가 함정 슬롯과 겹친다.");
                Assert.That(monsterCoords.Contains(coord), Is.False, $"{slot.ObjectId}: {coord}가 몬스터 슬롯과 겹친다.");
            }

            var coords = ServiceSlots(source).Select(slot => slot.Coord).ToList();
            Assert.That(coords.Distinct().Count(), Is.EqualTo(coords.Count),
                "서비스 후보 슬롯 중 좌표가 겹치는 것이 있다 — 추첨이 한 칸을 둘로 세게 된다.");
        }

        [Test]
        public void ServiceSlotsAreWalkReachable()
        {
            // 통행 ≠ 도달(AllRandomizationSlotsAreWalkReachable 패턴): BaseWalkable이어도 고립 섬이면
            // 플레이어가 영영 못 밟는다. 예비 슬롯도 후보이므로 함께 본다.
            var source = LoadSource();
            Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
            var playerSpawn = source.ObjectRefs.Single(candidate => candidate != null && candidate.IsPlayerSpawn).Coord;
            var walkDistances = HexWalkDistances.FromCoord(map, playerSpawn);

            var unreachable = ServiceSlots(source)
                .Where(candidate => !walkDistances.ContainsKey(candidate.Coord))
                .Select(candidate => $"{candidate.ObjectId}@{candidate.Coord}")
                .ToList();
            Assert.That(unreachable, Is.Empty, "보행 도달 불가한 서비스 슬롯이 있다.");
        }

        [Test]
        public void AuthoredBaselineMeetsEveryRouteTwicePerKind()
        {
            // 폴백(랜덤화 off·실패)에서도 판이 성립해야 한다. 저작된 6개만으로 어느 루트를 걷든
            // 각 종류를 2번 만나는지 — 이걸 안 물으면 폴백이 조용히 「한쪽 루트에선 잡화점 1개」가 된다.
            var source = LoadSource();
            var placed = PlacedServices(source);
            var routes = ServiceSlots(source)
                .SelectMany(RouteIdsOf)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            Assert.That(routes.Count, Is.GreaterThanOrEqualTo(2), "루트 태그가 2종 미만이다.");

            foreach (var (type, _, _) in Expected)
            {
                var ofType = placed.Where(candidate => candidate.ObjectType == type).ToList();
                foreach (var route in routes)
                {
                    var met = ofType.Count(candidate => RouteIdsOf(candidate).Contains(route));
                    Assert.That(met, Is.GreaterThanOrEqualTo(2),
                        $"저작 기준선에서 {type}이 루트 '{route}'에서 {met}번만 만난다 — 최소 2번이어야 한다.");
                }
            }
        }

        [Test]
        public void SameKindServicesKeepTheAuthoredMinimumDistance()
        {
            // stage_randomization.csv의 serviceMinDistance(10)와 같은 규칙을 저작 기준선에도 적용한다 —
            // 추첨은 지키는데 폴백은 안 지키면 폴백만 몰려 보인다.
            var source = LoadSource();
            var placed = PlacedServices(source);
            foreach (var (type, _, _) in Expected)
            {
                var coords = placed.Where(candidate => candidate.ObjectType == type)
                    .Select(candidate => candidate.Coord)
                    .ToList();
                for (var i = 0; i < coords.Count; i++)
                {
                    for (var j = i + 1; j < coords.Count; j++)
                    {
                        Assert.That(coords[i].DistanceTo(coords[j]), Is.GreaterThanOrEqualTo(10),
                            $"{type} 두 개가 {coords[i]}·{coords[j]}로 10칸 미만이다.");
                    }
                }
            }
        }

        [Test]
        public void ServiceVisualsFitInsideTheirOwnCell()
        {
            // footprint를 1칸으로 줄여도 <b>모델 크기는 따라 줄지 않는다</b> — visualScaleMultiplier가
            // 별개 축이기 때문이다. 잡화점은 7칸 시절 값(0.83) 그대로면 평면 폭이 셀 1.92칸분이라
            // 이웃 칸을 덮는데, 트리거는 중심 한 칸뿐이라 <b>덮인 칸을 밟아도 아무 일이 없다</b> —
            // 그림이 규칙을 속이는 최악의 조합이다. 이웃 <b>중심</b>까지 닿지 않는 것이 기준선.
            const float adjacentCenterDistance = 1.7320508f; // 헥스 반지름 1.0(tileRadius×tileSpacing) 기준
            var source = LoadSource();
            foreach (var service in PlacedServices(source))
            {
                var path = AssetDatabase.FindAssets($"{service.ObjectRef} t:Prefab")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(candidate =>
                        System.IO.Path.GetFileNameWithoutExtension(candidate) == service.ObjectRef);
                Assert.That(path, Is.Not.Null, $"{service.ObjectId}: 프리팹 '{service.ObjectRef}'을 찾지 못했다.");

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var filters = contents.GetComponentsInChildren<MeshFilter>(true)
                        .Where(filter => filter.sharedMesh != null)
                        .ToList();
                    Assert.That(filters, Is.Not.Empty, $"{service.ObjectRef}: 메시가 없다.");

                    var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    foreach (var filter in filters)
                    {
                        var bounds = filter.sharedMesh.bounds;
                        var matrix = filter.transform.localToWorldMatrix;
                        for (var corner = 0; corner < 8; corner++)
                        {
                            var local = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                                (corner & 1) == 0 ? -1f : 1f,
                                (corner & 2) == 0 ? -1f : 1f,
                                (corner & 4) == 0 ? -1f : 1f));
                            var world = matrix.MultiplyPoint3x4(local);
                            min = Vector3.Min(min, world);
                            max = Vector3.Max(max, world);
                        }
                    }

                    var size = max - min;
                    var planWidth = Mathf.Max(size.x, size.z) * service.VisualScaleMultiplier.x;
                    Assert.That(planWidth, Is.LessThan(adjacentCenterDistance),
                        $"{service.ObjectId}({service.ObjectRef}): 평면 폭 {planWidth:F2}가 이웃 중심 거리 " +
                        $"{adjacentCenterDistance:F2} 이상이다 — 밟아도 안 되는 칸을 그림이 덮는다. " +
                        "visualScaleMultiplier를 줄일 것.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
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
    }
}
#endif

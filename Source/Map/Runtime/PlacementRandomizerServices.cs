using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    /// <summary>
    /// 서비스 오브젝트(잡화점·캠핑카) 추첨의 후보 슬롯 한 칸. 저작 소스의 태그된 objectRef에서
    /// 뽑아 온다 — 실물이 서 있는 칸(점유)과 비어 있는 예비 슬롯을 구분하지 않는다: 둘 다
    /// 「그 자리에 설 수 있다」는 뜻일 뿐이다.
    /// </summary>
    public readonly struct ServiceSlotCandidate
    {
        public ServiceSlotCandidate(string slotId, HexCoord coord, IReadOnlyList<string> routeIds)
        {
            SlotId = slotId ?? string.Empty;
            Coord = coord;
            RouteIds = routeIds ?? Array.Empty<string>();
        }

        public string SlotId { get; }
        public HexCoord Coord { get; }

        /// <summary>이 칸을 지나는 루트 id들. 공유 구간이면 둘 이상이 들어간다.</summary>
        public IReadOnlyList<string> RouteIds { get; }
    }

    /// <summary>추첨할 서비스 한 종류: objectId 목록(개수 = 세울 수)과 동종 최소 거리.</summary>
    public readonly struct ServicePlacementRequest
    {
        public ServicePlacementRequest(string kind, IReadOnlyList<string> objectIds, int minPairDistance)
        {
            Kind = kind ?? string.Empty;
            ObjectIds = objectIds ?? Array.Empty<string>();
            MinPairDistance = Math.Max(0, minPairDistance);
        }

        /// <summary>진단 문자열용 종류 이름(Shop·CamperVan). 규칙에는 쓰이지 않는다.</summary>
        public string Kind { get; }
        public IReadOnlyList<string> ObjectIds { get; }
        public int MinPairDistance { get; }
    }

    /// <summary>서비스 배치 결과 한 건: 어느 오브젝트(objectId 유지)가 어느 칸으로 가는가.</summary>
    public readonly struct ServiceAssignment
    {
        public ServiceAssignment(string objectId, HexCoord coord)
        {
            ObjectId = objectId ?? string.Empty;
            Coord = coord;
        }

        public string ObjectId { get; }
        public HexCoord Coord { get; }
    }

    public sealed class ServicePlacementResult
    {
        public ServicePlacementResult(
            bool success,
            IReadOnlyList<ServiceAssignment> assignments,
            int rerollsUsed,
            string failureReason)
        {
            Success = success;
            Assignments = assignments ?? Array.Empty<ServiceAssignment>();
            RerollsUsed = rerollsUsed;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool Success { get; }
        public IReadOnlyList<ServiceAssignment> Assignments { get; }
        public int RerollsUsed { get; }
        public string FailureReason { get; }
    }

    public static partial class PlacementRandomizer
    {
        /// <summary>
        /// P3 — 서비스 오브젝트 추첨 배치. 개수·objectId는 고정이고 <b>좌표만 바뀐다</b>(상자와 같은
        /// 문법) — 서비스는 1회 소비라 소비 기록이 objectId로 남고, id가 유지되어야 세이브가 맞는다.
        ///
        /// <para>
        /// 상자와 다른 점이 둘 있다. ①후보 칸을 런타임에 계산하지 않고 <b>저작된 슬롯</b>에서만 뽑는다.
        /// ②「어느 루트를 걷든 각 종류를 <paramref name="routeMinPerKind"/>번 이상 만난다」를
        /// <b>검증하고 재롤</b>한다 — 이 검증이 이 함수의 존재 이유다. 루트 지식은 코드가 아니라
        /// 슬롯의 <see cref="ServiceSlotCandidate.RouteIds"/>(저작 태그)에서 온다.
        /// </para>
        ///
        /// <para>
        /// 여러 종류를 <b>한 번에</b> 뽑는다 — 종류별로 따로 돌리면 같은 칸에 둘이 겹칠 수 있고,
        /// 그걸 사후에 걸러내면 결정성 해석이 갈라진다. 최소 거리는 <b>같은 종류끼리만</b> 본다
        /// (잡화점 옆 캠핑카는 허용).
        /// </para>
        ///
        /// 순수 함수 — 같은 입력·시드 = 같은 배치.
        /// </summary>
        public static ServicePlacementResult PlaceServices(
            IReadOnlyList<ServiceSlotCandidate> candidates,
            IReadOnlyList<ServicePlacementRequest> requests,
            IReadOnlyCollection<string> routeIds,
            int routeMinPerKind,
            int rerollLimit,
            int seed)
        {
            var kinds = (requests ?? Array.Empty<ServicePlacementRequest>())
                .Where(request => request.ObjectIds.Count > 0)
                .OrderBy(request => request.Kind, StringComparer.Ordinal)
                .ToList();
            if (kinds.Count == 0)
            {
                return new ServicePlacementResult(true, Array.Empty<ServiceAssignment>(), 0, string.Empty);
            }

            // 슬롯 순서를 id로 고정한다 — 저작 파일의 줄 순서가 배치를 흔들면 안 된다.
            var slots = (candidates ?? Array.Empty<ServiceSlotCandidate>())
                .GroupBy(slot => slot.Coord)
                .Select(group => group.First())
                .OrderBy(slot => slot.SlotId, StringComparer.Ordinal)
                .ToList();
            var needed = kinds.Sum(request => request.ObjectIds.Count);
            if (slots.Count < needed)
            {
                return new ServicePlacementResult(
                    false, Array.Empty<ServiceAssignment>(), 0,
                    $"Service placement has {slots.Count} candidate slots for {needed} objects.");
            }

            var routes = (routeIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            routeMinPerKind = Math.Max(0, routeMinPerKind);
            rerollLimit = Math.Max(1, rerollLimit);
            var rng = new Random(seed);
            var lastFailure = string.Empty;

            for (var attempt = 0; attempt < rerollLimit; attempt++)
            {
                var shuffled = slots.ToList();
                for (var i = shuffled.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }

                if (!TryFillKinds(kinds, shuffled, out var picksByKind, out var fillFailure))
                {
                    lastFailure = fillFailure;
                    continue;
                }

                if (!TryValidateRouteCoverage(kinds, picksByKind, routes, routeMinPerKind, out var coverageFailure))
                {
                    lastFailure = coverageFailure;
                    continue;
                }

                var assignments = new List<ServiceAssignment>(needed);
                foreach (var request in kinds)
                {
                    var picks = picksByKind[request.Kind];
                    // objectId도 정렬해 짝짓는다 — 요청 목록의 순서가 배치를 흔들지 않게.
                    var ids = request.ObjectIds
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToList();
                    for (var i = 0; i < ids.Count; i++)
                    {
                        assignments.Add(new ServiceAssignment(ids[i], picks[i].Coord));
                    }
                }

                return new ServicePlacementResult(true, assignments, attempt, string.Empty);
            }

            return new ServicePlacementResult(
                false, Array.Empty<ServiceAssignment>(), rerollLimit,
                $"Service placement exceeded the reroll limit ({rerollLimit}) over {slots.Count} candidate slots. " +
                $"Last failure: {lastFailure}");
        }

        /// <summary>
        /// 셔플된 후보를 훑으며 종류별로 탐욕 배치한다. 이미 쓴 칸은 종류를 가리지 않고 제외하고
        /// (겹침 금지), 최소 거리는 같은 종류끼리만 본다.
        /// </summary>
        private static bool TryFillKinds(
            IReadOnlyList<ServicePlacementRequest> kinds,
            IReadOnlyList<ServiceSlotCandidate> shuffled,
            out Dictionary<string, List<ServiceSlotCandidate>> picksByKind,
            out string failureReason)
        {
            picksByKind = new Dictionary<string, List<ServiceSlotCandidate>>(StringComparer.Ordinal);
            var used = new HashSet<HexCoord>();
            foreach (var request in kinds)
            {
                var want = request.ObjectIds.Count(id => !string.IsNullOrWhiteSpace(id));
                var picks = new List<ServiceSlotCandidate>(want);
                foreach (var slot in shuffled)
                {
                    if (used.Contains(slot.Coord))
                    {
                        continue;
                    }

                    if (picks.Any(existing => existing.Coord.DistanceTo(slot.Coord) < request.MinPairDistance))
                    {
                        continue;
                    }

                    picks.Add(slot);
                    used.Add(slot.Coord);
                    if (picks.Count == want)
                    {
                        break;
                    }
                }

                if (picks.Count < want)
                {
                    failureReason = $"kind '{request.Kind}' placed {picks.Count}/{want} " +
                                    $"at min pair distance {request.MinPairDistance}";
                    picksByKind = null;
                    return false;
                }

                picksByKind[request.Kind] = picks;
            }

            failureReason = string.Empty;
            return true;
        }

        /// <summary>
        /// 이 판이 「어느 루트를 걷든 각 종류를 최소 몇 번 만나는가」를 만족하는지. 슬롯이 여러 루트에
        /// 태그돼 있으면(공유 구간) 그 루트 전부에 대해 한 번씩 세어진다.
        /// </summary>
        private static bool TryValidateRouteCoverage(
            IReadOnlyList<ServicePlacementRequest> kinds,
            IReadOnlyDictionary<string, List<ServiceSlotCandidate>> picksByKind,
            IReadOnlyList<string> routes,
            int routeMinPerKind,
            out string failureReason)
        {
            if (routeMinPerKind <= 0 || routes.Count == 0)
            {
                failureReason = string.Empty;
                return true;
            }

            foreach (var request in kinds)
            {
                var picks = picksByKind[request.Kind];
                foreach (var route in routes)
                {
                    var met = picks.Count(slot => slot.RouteIds.Contains(route, StringComparer.Ordinal));
                    if (met < routeMinPerKind)
                    {
                        failureReason = $"kind '{request.Kind}' meets route '{route}' only {met} time(s), " +
                                        $"needs {routeMinPerKind}";
                        return false;
                    }
                }
            }

            failureReason = string.Empty;
            return true;
        }
    }
}

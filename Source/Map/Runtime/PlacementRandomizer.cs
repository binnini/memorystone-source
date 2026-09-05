using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    /// <summary>
    /// 배치 랜덤화 후보 슬롯(placement-randomization-plan §2~§3). 저작 표면의 몬스터 스폰 ref에서
    /// 추출된 순수 DTO다. OccupantMonsterId가 비어 있으면 「예비 슬롯」— 위치 후보로만 쓰이고
    /// 그룹 명단(roster)에는 기여하지 않는다. patrolAreaId는 슬롯에 붙는다(몬스터가 슬롯을 옮겨도
    /// 순찰은 슬롯 기준).
    /// </summary>
    public readonly struct PlacementSlotCandidate
    {
        public PlacementSlotCandidate(
            string slotId,
            HexCoord coord,
            string group,
            string occupantMonsterId,
            string occupantRole = "",
            HexMapPurpose enabledForPurpose = HexMapPurpose.Unspecified,
            string patrolAreaId = "")
        {
            SlotId = slotId ?? string.Empty;
            Coord = coord;
            Group = group ?? string.Empty;
            OccupantMonsterId = occupantMonsterId ?? string.Empty;
            OccupantRole = occupantRole ?? string.Empty;
            EnabledForPurpose = enabledForPurpose;
            PatrolAreaId = patrolAreaId ?? string.Empty;
        }

        public string SlotId { get; }
        public HexCoord Coord { get; }
        public string Group { get; }
        public string OccupantMonsterId { get; }
        public string OccupantRole { get; }
        public HexMapPurpose EnabledForPurpose { get; }
        public string PatrolAreaId { get; }
        public bool HasOccupant => !string.IsNullOrWhiteSpace(OccupantMonsterId);
    }

    /// <summary>랜덤화 결과 한 건: 어느 슬롯에 어느 몬스터가 서는가. role은 몬스터를 따라간다.</summary>
    public readonly struct PlacementSpawnAssignment
    {
        public PlacementSpawnAssignment(PlacementSlotCandidate slot, string monsterId, string role)
        {
            SlotId = slot.SlotId;
            Coord = slot.Coord;
            PatrolAreaId = slot.PatrolAreaId;
            EnabledForPurpose = slot.EnabledForPurpose;
            MonsterId = monsterId ?? string.Empty;
            Role = role ?? string.Empty;
        }

        public string SlotId { get; }
        public HexCoord Coord { get; }
        public string PatrolAreaId { get; }
        public HexMapPurpose EnabledForPurpose { get; }
        public string MonsterId { get; }
        public string Role { get; }
    }

    public sealed class PlacementRandomizationConfig
    {
        /// <summary>
        /// PlayerSpawn 안전 반경(§3-4, Q4). 확정값 6 — monster_catalog의 detectionRange 표준값(6)과
        /// 대조한 결과다: 6 미만이면 첫 턴부터 감지가 성립해 저작 원본(현행 최근접 6칸)보다 나빠진다.
        /// </summary>
        public int SafeRadiusFromPlayerSpawn { get; }

        /// <summary>재롤 상한(§3-4). 초과 시 호출자가 저작 원본으로 폴백한다.</summary>
        public int RerollLimit { get; }

        public PlacementRandomizationConfig(int safeRadiusFromPlayerSpawn, int rerollLimit)
        {
            SafeRadiusFromPlayerSpawn = Math.Max(0, safeRadiusFromPlayerSpawn);
            RerollLimit = Math.Max(1, rerollLimit);
        }

        public static PlacementRandomizationConfig Default { get; } = new PlacementRandomizationConfig(
            safeRadiusFromPlayerSpawn: 6,
            rerollLimit: 20);
    }

    public sealed class PlacementRandomizationResult
    {
        public PlacementRandomizationResult(
            bool success,
            IReadOnlyList<PlacementSpawnAssignment> assignments,
            int rerollsUsed,
            string failureReason)
        {
            Success = success;
            Assignments = assignments ?? Array.Empty<PlacementSpawnAssignment>();
            RerollsUsed = rerollsUsed;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool Success { get; }
        public IReadOnlyList<PlacementSpawnAssignment> Assignments { get; }
        public int RerollsUsed { get; }
        public string FailureReason { get; }
    }

    /// <summary>
    /// 슬롯 승격형 배치 랜덤라이저(placement-randomization-plan §3, P0). 순수 함수: 같은 입력과
    /// 같은 시드는 항상 같은 배치를 낸다(결정성 계약 §2-2 — 세이브 재개와 시드 스윕 감사의 전제).
    /// P0 의미론: 그룹별로 명단(점유 슬롯의 몬스터 멀티셋)은 불변, 「어느 슬롯에 서는가」만 바뀐다.
    /// 검증 실패 시 그룹 단위 재롤, 상한 초과 시 실패를 반환하고 호출자가 저작 원본으로 폴백한다.
    /// </summary>
    public static partial class PlacementRandomizer
    {
        /// <summary>
        /// 같은 종을 거듭 뽑을 때 가중치가 내려앉는 바닥(2026-09-02 #5). 0이 아니라 1인 이유는
        /// <see cref="DecayedWeight"/> 주석에 있다 — 감쇠는 상한이 아니다.
        /// </summary>
        private const int RepeatDecayFloorWeight = 1;

        /// <summary>
        /// 이 몬스터의 몸이 이 슬롯에 들어가는가. 화이트리스트가 없거나 그 몬스터가 목록에 없으면
        /// 제약이 없다 — 한 칸 몸은 어디에나 선다.
        /// </summary>
        private static bool BodyFitsOnSlot(
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> allowedSlotIdsByMonsterId,
            string monsterId,
            string slotId)
        {
            if (allowedSlotIdsByMonsterId == null
                || string.IsNullOrEmpty(monsterId)
                || !allowedSlotIdsByMonsterId.TryGetValue(monsterId, out var allowed)
                || allowed == null)
            {
                return true;
            }

            return allowed.Contains(slotId);
        }

        public static PlacementRandomizationResult Randomize(
            IReadOnlyList<PlacementSlotCandidate> slots,
            HexCoord playerSpawnCoord,
            PlacementRandomizationConfig config,
            int seed)
        {
            config = config ?? PlacementRandomizationConfig.Default;
            var tagged = (slots ?? Array.Empty<PlacementSlotCandidate>())
                .Where(slot => !string.IsNullOrWhiteSpace(slot.Group) && !string.IsNullOrWhiteSpace(slot.SlotId))
                .ToList();
            if (tagged.Count == 0)
            {
                return new PlacementRandomizationResult(true, Array.Empty<PlacementSpawnAssignment>(), 0, string.Empty);
            }

            var duplicateSlotId = tagged
                .GroupBy(slot => slot.SlotId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateSlotId != null)
            {
                return new PlacementRandomizationResult(
                    false, Array.Empty<PlacementSpawnAssignment>(), 0,
                    $"Duplicate slot id '{duplicateSlotId.Key}' among randomization candidates.");
            }

            // 순회 순서가 곧 RNG 소비 순서다 — 그룹·슬롯을 서수 정렬로 고정해야 입력 리스트의
            // 순서와 무관하게 같은 시드가 같은 배치를 낸다.
            var groups = tagged
                .GroupBy(slot => slot.Group, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
            var rng = new Random(seed);
            var assignments = new List<PlacementSpawnAssignment>();
            var totalRerolls = 0;

            foreach (var group in groups)
            {
                var candidates = group.OrderBy(slot => slot.SlotId, StringComparer.Ordinal).ToList();
                var roster = candidates
                    .Where(slot => slot.HasOccupant)
                    .Select(slot => (slot.OccupantMonsterId, slot.OccupantRole))
                    .ToList();
                if (roster.Count == 0)
                {
                    continue;
                }

                if (candidates.Count < roster.Count)
                {
                    return new PlacementRandomizationResult(
                        false, Array.Empty<PlacementSpawnAssignment>(), totalRerolls,
                        $"Randomization group '{group.Key}' has fewer slots ({candidates.Count}) than roster monsters ({roster.Count}).");
                }

                var success = false;
                for (var attempt = 0; attempt < config.RerollLimit; attempt++)
                {
                    var selectedSlots = SampleWithoutReplacement(candidates, roster.Count, rng);
                    var shuffledRoster = Shuffled(roster, rng);
                    if (!Validate(selectedSlots, playerSpawnCoord, config, out _))
                    {
                        totalRerolls++;
                        continue;
                    }

                    for (var i = 0; i < selectedSlots.Count; i++)
                    {
                        assignments.Add(new PlacementSpawnAssignment(
                            selectedSlots[i], shuffledRoster[i].Item1, shuffledRoster[i].Item2));
                    }

                    success = true;
                    break;
                }

                if (!success)
                {
                    return new PlacementRandomizationResult(
                        false, Array.Empty<PlacementSpawnAssignment>(), totalRerolls,
                        $"Randomization group '{group.Key}' exceeded the reroll limit ({config.RerollLimit}) " +
                        $"without satisfying the safe radius {config.SafeRadiusFromPlayerSpawn} from {playerSpawnCoord}.");
                }
            }

            return new PlacementRandomizationResult(true, assignments, totalRerolls, string.Empty);
        }

        /// <summary>
        /// P1 — 몬스터 풀 + 위협 예산(placement-randomization-plan §4). 그룹별 슬롯 선택은 P0과
        /// 같고(그룹당 스폰 수 = 저작 점유 수 고정 = 밀도 앵커), 슬롯에 설 몬스터를 그룹 풀에서
        /// 가중 추첨한다. 검증 = 안전 반경 + 스테이지 위협 예산 ±tolerance + 밀도 상한 +
        /// 지대(zone) 제한 + 타입 상한(capKey 합산). 실패 시 스테이지 단위 재롤, 상한 초과 시
        /// 실패 반환(호출자가 저작 원본 폴백). 순수 함수 — 같은 입력·시드 = 같은 배치.
        /// </summary>
        /// <param name="allowedSlotIdsByMonsterId">
        /// 몸이 여러 칸인 몬스터(삼각형 정예 등)가 <b>설 수 있는 슬롯 id</b>의 화이트리스트
        /// (2026-09-05 실플레이 피드백: "1칸 초과 footprint 몬스터가 물 타일에 걸쳐서 걸어다닌다").
        ///
        /// <para>🔴 이동 판정(<c>HexPathfinder.CanEnter</c>)은 원래부터 몸통 전 칸을 봤다 — 새는 곳은
        /// <b>배치</b>였다. 슬롯 유효성은 「그 한 칸이 통행 가능한가」로만 검사되므로, 3칸 몸이 물가 슬롯을
        /// 뽑으면 몸통 한두 칸이 물 위에 얹힌 채로 판이 시작된다(200시드 실측 17%).</para>
        ///
        /// <para>🔑 여기에 <b>맵을 들이지 않은</b> 이유: 몸 형상은 전투 카탈로그가 정본이라 맵 레이어가
        /// 알 수 없고, 반대로 통행 판정은 맵의 몫이다. 아는 쪽(전투 컨트롤러)이 「이 몬스터는 이 슬롯들에만
        /// 설 수 있다」를 데이터로 계산해 넣어 준다 — 은신 태그(<c>stealthMonsterIds</c>)와 같은 규약이다.
        /// 목록에 없는 몬스터는 제약이 없다(한 칸 몸은 여기 들어오지 않는다).</para>
        /// </param>
        public static PlacementRandomizationResult RandomizeWithProfile(
            IReadOnlyList<PlacementSlotCandidate> slots,
            HexCoord playerSpawnCoord,
            StageRandomizationProfile profile,
            IReadOnlyDictionary<HexCoord, int> walkDistanceFromPlayer,
            int seed,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> allowedSlotIdsByMonsterId = null)
        {
            if (profile == null)
            {
                return new PlacementRandomizationResult(false, Array.Empty<PlacementSpawnAssignment>(), 0, "No randomization profile.");
            }

            var tagged = (slots ?? Array.Empty<PlacementSlotCandidate>())
                .Where(slot => !string.IsNullOrWhiteSpace(slot.Group) && !string.IsNullOrWhiteSpace(slot.SlotId))
                .ToList();
            if (tagged.Count == 0)
            {
                return new PlacementRandomizationResult(true, Array.Empty<PlacementSpawnAssignment>(), 0, string.Empty);
            }

            var duplicateSlotId = tagged
                .GroupBy(slot => slot.SlotId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateSlotId != null)
            {
                return new PlacementRandomizationResult(
                    false, Array.Empty<PlacementSpawnAssignment>(), 0,
                    $"Duplicate slot id '{duplicateSlotId.Key}' among randomization candidates.");
            }

            var groups = tagged
                .GroupBy(slot => slot.Group, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new
                {
                    Key = group.Key,
                    Candidates = group.OrderBy(slot => slot.SlotId, StringComparer.Ordinal).ToList(),
                    SpawnCount = group.Count(slot => slot.HasOccupant),
                    // 풀 행 순서 = CSV 저작 순서 — 가중 추첨의 결정성이 여기 걸린다.
                    Pool = profile.Pools.Where(entry => entry.IsMonster &&
                        string.Equals(entry.Group, group.Key, StringComparison.Ordinal)).ToList(),
                })
                .Where(group => group.SpawnCount > 0)
                .ToList();

            foreach (var group in groups)
            {
                if (group.Pool.Count == 0 || group.Pool.All(entry => entry.Weight <= 0))
                {
                    return new PlacementRandomizationResult(
                        false, Array.Empty<PlacementSpawnAssignment>(), 0,
                        $"Randomization group '{group.Key}' has no drawable pool entries for stage '{profile.StageId}'.");
                }
            }

            var rng = new Random(seed);
            var lastFailure = string.Empty;
            for (var attempt = 0; attempt < profile.RerollLimit; attempt++)
            {
                var capUsed = new Dictionary<string, int>(StringComparer.Ordinal);
                var picks = new List<(PlacementSlotCandidate Slot, StageRandomizationPoolEntry Entry)>();
                var attemptOk = true;

                foreach (var group in groups)
                {
                    var selectedSlots = SampleWithoutReplacement(group.Candidates, group.SpawnCount, rng);
                    foreach (var slot in selectedSlots)
                    {
                        var zone = walkDistanceFromPlayer != null && walkDistanceFromPlayer.TryGetValue(slot.Coord, out var walkDistance)
                            ? profile.ZoneForBfsDistance(walkDistance)
                            : PlacementZone.Late;
                        // 지대는 <b>하한</b>이다(2026-09-01 #14): 저작한 지대<b>부터 그 뒤로 전부</b> 설 수 있다.
                        // 종전의 정확히 일치로는 「중반부터 나온다」를 한 행으로 못 적고 mid·late 두 행으로
                        // 쪼개야 했다 — 가중치가 두 벌이 되어 저작이 조용히 갈린다.
                        // 현행 저작(any·late뿐)에는 영향이 없다: late는 마지막 지대라 둘이 같은 뜻이다.
                        var eligible = group.Pool
                            .Where(entry => entry.Weight > 0 &&
                                (entry.Zone == PlacementZone.Any || zone >= entry.Zone) &&
                                (!capUsed.TryGetValue(entry.CapKey, out var used) || used < entry.MaxCount) &&
                                // 몸이 안 들어가는 슬롯은 애초에 후보가 아니다(2026-09-05).
                                BodyFitsOnSlot(allowedSlotIdsByMonsterId, entry.EntryRef, slot.SlotId))
                            .ToList();
                        if (eligible.Count == 0)
                        {
                            lastFailure = $"Group '{group.Key}' slot '{slot.SlotId}' ({zone}) has no eligible pool entry (zone/cap/body exhausted).";
                            attemptOk = false;
                            break;
                        }

                        var entryPicked = WeightedPickWithRepeatDecay(eligible, rng, capUsed);
                        capUsed[entryPicked.CapKey] = (capUsed.TryGetValue(entryPicked.CapKey, out var count) ? count : 0) + 1;
                        picks.Add((slot, entryPicked));
                    }

                    if (!attemptOk)
                    {
                        break;
                    }
                }

                if (attemptOk && !ValidateProfileAttempt(picks, playerSpawnCoord, profile, out lastFailure))
                {
                    attemptOk = false;
                }

                if (attemptOk)
                {
                    var assignments = picks
                        .Select(pick => new PlacementSpawnAssignment(pick.Slot, pick.Entry.EntryRef, pick.Entry.Role))
                        .ToList();
                    return new PlacementRandomizationResult(true, assignments, attempt, string.Empty);
                }
            }

            return new PlacementRandomizationResult(
                false, Array.Empty<PlacementSpawnAssignment>(), profile.RerollLimit,
                $"Profile randomization exceeded the reroll limit ({profile.RerollLimit}). Last failure: {lastFailure}");
        }

        private static bool ValidateProfileAttempt(
            IReadOnlyList<(PlacementSlotCandidate Slot, StageRandomizationPoolEntry Entry)> picks,
            HexCoord playerSpawnCoord,
            StageRandomizationProfile profile,
            out string violation)
        {
            foreach (var pick in picks)
            {
                if (pick.Slot.Coord.DistanceTo(playerSpawnCoord) < profile.SafeRadius)
                {
                    violation = $"Slot '{pick.Slot.SlotId}' at {pick.Slot.Coord} is inside the safe radius {profile.SafeRadius}.";
                    return false;
                }
            }

            var totalThreat = picks.Sum(pick => pick.Entry.ThreatCost);
            if (totalThreat < profile.BudgetMin || totalThreat > profile.BudgetMax)
            {
                violation = $"Threat total {totalThreat} is outside budget [{profile.BudgetMin}, {profile.BudgetMax}].";
                return false;
            }

            // elite 최소 보장(#18): capGroup은 상한만 걸므로 elite 0인 판이 실제로 나왔다 —
            // 하한은 여기서 재롤로 보장한다. role 문자열은 풀 행이 정본이다.
            if (profile.EliteMin > 0)
            {
                var eliteCount = picks.Count(pick =>
                    string.Equals(pick.Entry.Role, "elite", StringComparison.OrdinalIgnoreCase));
                if (eliteCount < profile.EliteMin)
                {
                    violation = $"Elite count {eliteCount} is below the stage minimum {profile.EliteMin}.";
                    return false;
                }
            }

            // 정예 간 최소 거리(2026-09-01 #17). 🔴 여태 이 축이 <b>통째로 없었다</b> — bans 표의
            // SpawnMonsters↔elite는 <b>함정</b>과 정예 사이의 것이고, 밀도 상한도 정예 둘(3+3=6)은
            // 반경 2 상한 8 아래라 통과시킨다. 문법은 trapMinDistance의 복제이고, 0이면 꺼진다.
            if (profile.EliteMinDistance > 0)
            {
                var elites = picks
                    .Where(pick => string.Equals(pick.Entry.Role, "elite", StringComparison.OrdinalIgnoreCase))
                    .Select(pick => pick.Slot)
                    .ToList();
                for (var i = 0; i < elites.Count; i++)
                {
                    for (var j = i + 1; j < elites.Count; j++)
                    {
                        var distance = elites[i].Coord.DistanceTo(elites[j].Coord);
                        if (distance < profile.EliteMinDistance)
                        {
                            violation =
                                $"Elites '{elites[i].SlotId}' and '{elites[j].SlotId}' are {distance} apart, " +
                                $"below the stage minimum {profile.EliteMinDistance}.";
                            return false;
                        }
                    }
                }
            }

            // 종류 다양성 하한(#12): 가중치 추첨만으로는 무거운 가중치 하나가 판을 독식한다 —
            // capGroup은 종류별 <b>상한</b>만 걸어서 "한 종류만 잔뜩"을 못 막는다. elite 하한과 같은
            // 재롤 게이트로 하한을 보장한다. 세는 단위는 entryRef(= 몬스터 정의 id)다.
            if (profile.MonsterMinKinds > 0)
            {
                var kindCount = picks
                    .Select(pick => pick.Entry.EntryRef)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                if (kindCount < profile.MonsterMinKinds)
                {
                    violation = $"Monster kind count {kindCount} is below the stage minimum {profile.MonsterMinKinds}.";
                    return false;
                }
            }

            // 밀도 상한(「죽음의 방」 방지): 어느 배치 칸 기준으로도 반경 내 위협 합이 상한을 넘지 않는다.
            foreach (var center in picks)
            {
                var nearbyThreat = picks
                    .Where(pick => pick.Slot.Coord.DistanceTo(center.Slot.Coord) <= profile.DensityRadius)
                    .Sum(pick => pick.Entry.ThreatCost);
                if (nearbyThreat > profile.DensityCap)
                {
                    violation = $"Threat density {nearbyThreat} around {center.Slot.Coord} exceeds cap {profile.DensityCap} (radius {profile.DensityRadius}).";
                    return false;
                }
            }

            violation = string.Empty;
            return true;
        }

        /// <summary>
        /// 이미 뽑힌 종의 가중치를 <b>뽑을 때마다 절반으로</b> 깎아 뽑는다 — 몬스터 전용
        /// (2026-09-02 #5 · 사용자 실플레이: "초반엔 삼목구, 중반엔 사자탈과 황소만 나온다").
        ///
        /// <para>🔴 <b>종전 추첨은 복원추출이었다</b>: 뽑혀도 가중치가 그대로라 한 종이 연달아 나올 확률이
        /// 매번 같았다. 제동은 <c>maxCount</c> 하나뿐인데 그것은 <b>맵 전체 상한</b>이고 삼목구는 8이라
        /// 한 밴드의 슬롯 수보다 커서 <b>사실상 물지 않았다</b>. <c>monsterMinKinds</c>도 <b>맵 전체 종 수</b>라
        /// 밴드 안 다양성은 아무도 안 보고 있었다.</para>
        ///
        /// <para>🔑 <b>저작 표를 한 줄도 안 고치는 것</b>이 이 손잡이를 고른 이유다. 밴드별 상한이나
        /// 종 하한을 새로 저작하면 행마다 손이 가고 재롤 실패도 는다. 여기서는 「진입부는 삼목구 위주」
        /// 같은 <b>저작 의도(첫 뽑기 확률)는 그대로 두고</b>, 같은 종이 <b>거듭</b> 나오는 것만 누른다.</para>
        ///
        /// <para>🔑 셈은 <c>capUsed</c>를 그대로 쓴다 — 상한과 감쇠가 <b>같은 카운터</b>를 보므로 둘이
        /// 갈라질 수 없고, 새 상태도 없다. 정수 시프트라 시드 결정성도 그대로다(부동소수 금지).
        /// ⚠️ 대신 <b>같은 시드가 종전과 다른 배치를 낸다</b> — 진행 중인 세이브의 맵은 재개 시 달라진다.</para>
        ///
        /// <para>⚠️ 카운터는 그룹이 아니라 <b>시도 전체(맵 전체)</b>에 누적된다. 그래서 감쇠는 밴드를 넘어 이어지고,
        /// 앞 밴드에서 많이 뽑힌 종은 뒤 밴드에서 표의 가중치와 다른 확률로 나온다(2026-09-05 1,000판 실측: 삼목구
        /// 밴드별 45→16→12→8→8%, 저작 첫 뽑기는 50·26·22·15·18%). 사용자 판단(99-open #32): <b>현행 유지</b> —
        /// 「초반 약하게·후반 강하게」 곡선과 같은 방향이고, 밸런스 변경은 실플레이 판정 뒤에만 한다.</para>
        /// </summary>
        private static StageRandomizationPoolEntry WeightedPickWithRepeatDecay(
            IReadOnlyList<StageRandomizationPoolEntry> entries,
            Random rng,
            IReadOnlyDictionary<string, int> alreadyPicked)
        {
            var weights = new int[entries.Count];
            var total = 0;
            for (var index = 0; index < entries.Count; index++)
            {
                weights[index] = DecayedWeight(entries[index], alreadyPicked);
                total += weights[index];
            }

            var roll = rng.Next(total);
            for (var index = 0; index < entries.Count; index++)
            {
                roll -= weights[index];
                if (roll < 0)
                {
                    return entries[index];
                }
            }

            return entries[entries.Count - 1];
        }

        /// <summary>
        /// 뽑힌 횟수만큼 절반씩 — 다만 <b>1 아래로는 안 내려간다</b>. 0으로 떨어뜨리면 감쇠가
        /// <b>상한</b>이 되어 「몇 마리까지」를 두 곳에서 정하게 된다(그건 <c>maxCount</c>의 몫이다).
        /// </summary>
        private static int DecayedWeight(
            StageRandomizationPoolEntry entry, IReadOnlyDictionary<string, int> alreadyPicked)
        {
            if (!alreadyPicked.TryGetValue(entry.CapKey, out var picked) || picked <= 0)
            {
                return entry.Weight;
            }

            // 시프트 폭이 int 폭을 넘으면 결과가 정의되지 않는다 — 그 전에 바닥으로 눕힌다.
            return picked >= 31 ? RepeatDecayFloorWeight : Math.Max(RepeatDecayFloorWeight, entry.Weight >> picked);
        }

        private static StageRandomizationPoolEntry WeightedPick(IReadOnlyList<StageRandomizationPoolEntry> entries, Random rng)
        {
            var total = entries.Sum(entry => entry.Weight);
            var roll = rng.Next(total);
            foreach (var entry in entries)
            {
                roll -= entry.Weight;
                if (roll < 0)
                {
                    return entry;
                }
            }

            return entries[entries.Count - 1];
        }

        private static bool Validate(
            IReadOnlyList<PlacementSlotCandidate> selectedSlots,
            HexCoord playerSpawnCoord,
            PlacementRandomizationConfig config,
            out string violation)
        {
            foreach (var slot in selectedSlots)
            {
                if (slot.Coord.DistanceTo(playerSpawnCoord) < config.SafeRadiusFromPlayerSpawn)
                {
                    violation = $"Slot '{slot.SlotId}' at {slot.Coord} is inside the safe radius " +
                                $"{config.SafeRadiusFromPlayerSpawn} from player spawn {playerSpawnCoord}.";
                    return false;
                }
            }

            violation = string.Empty;
            return true;
        }

        private static List<PlacementSlotCandidate> SampleWithoutReplacement(
            IReadOnlyList<PlacementSlotCandidate> candidates,
            int count,
            Random rng)
        {
            var pool = candidates.ToList();
            var picked = new List<PlacementSlotCandidate>(count);
            for (var i = 0; i < count; i++)
            {
                var index = rng.Next(pool.Count);
                picked.Add(pool[index]);
                pool.RemoveAt(index);
            }

            return picked;
        }

        private static List<(string, string)> Shuffled(IReadOnlyList<(string, string)> roster, Random rng)
        {
            var shuffled = roster.ToList();
            for (var i = shuffled.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            return shuffled;
        }
    }
}

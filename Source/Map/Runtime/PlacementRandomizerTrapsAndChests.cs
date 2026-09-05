using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    /// <summary>
    /// P2 함정 후보 슬롯(placement-randomization-plan §5). 저작 trapRefs에서 추출된 순수 DTO.
    /// HasOccupant가 false면 「예비 슬롯」(Q10: 태그 있음 + presetId·effects 빈 함정) — 위치
    /// 후보로만 쓰이고 그룹의 추첨 수에 기여하지 않는다.
    /// </summary>
    public readonly struct TrapSlotCandidate
    {
        public TrapSlotCandidate(string slotId, HexCoord coord, string group, bool hasOccupant)
        {
            SlotId = slotId ?? string.Empty;
            Coord = coord;
            Group = group ?? string.Empty;
            HasOccupant = hasOccupant;
        }

        public string SlotId { get; }
        public HexCoord Coord { get; }
        public string Group { get; }
        public bool HasOccupant { get; }
    }

    /// <summary>함정 추첨 결과 한 건: 어느 슬롯(trapId 유지)에 어느 프리셋이 서는가(presetId 치환, §2-4).</summary>
    public readonly struct TrapAssignment
    {
        public TrapAssignment(TrapSlotCandidate slot, string presetId)
        {
            SlotId = slot.SlotId;
            Coord = slot.Coord;
            PresetId = presetId ?? string.Empty;
        }

        public string SlotId { get; }
        public HexCoord Coord { get; }
        public string PresetId { get; }
    }

    public sealed class TrapRandomizationResult
    {
        public TrapRandomizationResult(
            bool success,
            IReadOnlyList<TrapAssignment> assignments,
            int rerollsUsed,
            string failureReason)
        {
            Success = success;
            Assignments = assignments ?? Array.Empty<TrapAssignment>();
            RerollsUsed = rerollsUsed;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool Success { get; }
        public IReadOnlyList<TrapAssignment> Assignments { get; }
        public int RerollsUsed { get; }
        public string FailureReason { get; }
    }

    /// <summary>
    /// bans 판정에 필요한 프리셋 성질의 순수 사본(TrapPresetCatalog는 Unity 계층이라 직접 못 쓴다).
    /// EffectKinds는 HexTrapEffectKind 이름 문자열이다(예: "SpawnMonsters").
    /// </summary>
    public readonly struct TrapPresetTraits
    {
        public TrapPresetTraits(string presetId, IEnumerable<string> effectKinds, int radius)
        {
            PresetId = presetId ?? string.Empty;
            EffectKinds = effectKinds == null ? Array.Empty<string>() : effectKinds.ToArray();
            Radius = Math.Max(0, radius);
        }

        public string PresetId { get; }
        public IReadOnlyList<string> EffectKinds { get; }
        public int Radius { get; }
    }

    /// <summary>bans의 몬스터 쪽 표적: 배치 좌표 + 태그 집합(스폰 role + 브리지가 붙인 특성 태그).</summary>
    public readonly struct PlacementTaggedPoint
    {
        public PlacementTaggedPoint(HexCoord coord, IEnumerable<string> tags)
        {
            Coord = coord;
            Tags = tags == null ? Array.Empty<string>() : tags.ToArray();
        }

        public HexCoord Coord { get; }
        public IReadOnlyList<string> Tags { get; }
    }

    /// <summary>상자 셔플 결과 한 건: 어느 상자(objectId 유지)가 어느 칸으로 가는가(§6-5).</summary>
    public readonly struct ChestAssignment
    {
        public ChestAssignment(string objectId, HexCoord coord)
        {
            ObjectId = objectId ?? string.Empty;
            Coord = coord;
        }

        public string ObjectId { get; }
        public HexCoord Coord { get; }
    }

    public sealed class ChestShuffleResult
    {
        public ChestShuffleResult(
            bool success,
            IReadOnlyList<ChestAssignment> assignments,
            int rerollsUsed,
            string failureReason)
        {
            Success = success;
            Assignments = assignments ?? Array.Empty<ChestAssignment>();
            RerollsUsed = rerollsUsed;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool Success { get; }
        public IReadOnlyList<ChestAssignment> Assignments { get; }
        public int RerollsUsed { get; }
        public string FailureReason { get; }
    }

    public static partial class PlacementRandomizer
    {
        /// <summary>
        /// P2에서 몬스터·함정·상자가 각자 독립 RNG 스트림을 쓰기 위한 시드 유도(결정성 계약 §2-2).
        /// 몬스터는 원시 시드를 그대로 써서 P0~P1과 같은 시드 = 같은 몬스터 배치가 유지되고,
        /// 함정·상자는 유도 시드를 써서 어느 한쪽의 소비량 변화가 다른 쪽을 흔들지 않는다.
        /// </summary>
        public static int DeriveSeed(int seed, int streamIndex)
        {
            unchecked
            {
                var mixed = (uint)seed * 2654435761u + (uint)streamIndex * 40503u + 0x9E3779B9u;
                mixed ^= mixed >> 16;
                mixed *= 2246822519u;
                mixed ^= mixed >> 13;
                return (int)mixed;
            }
        }

        /// <summary>
        /// P2 — 함정 풀 추첨(placement-randomization-plan §5). 슬롯 선택은 몬스터와 같은 문법
        /// (그룹당 추첨 수 = 저작 점유 수), 내용은 presetId 치환(§2-4 프리셋 비대칭). 검증 =
        /// 함정 위협 예산(몬스터 예산과 별도 창) + 금지 조합 표(bans, 몬스터 배치 대조).
        /// 실패 시 스테이지 단위 재롤, 상한 초과 시 실패 반환(호출자가 저작 원본 폴백).
        /// 순수 함수 — 같은 입력·시드 = 같은 배치.
        /// </summary>
        public static TrapRandomizationResult RandomizeTraps(
            IReadOnlyList<TrapSlotCandidate> slots,
            StageRandomizationProfile profile,
            IReadOnlyDictionary<HexCoord, int> walkDistanceFromPlayer,
            IReadOnlyDictionary<string, TrapPresetTraits> presetTraits,
            IReadOnlyList<PlacementTaggedPoint> monsterPoints,
            int seed)
        {
            if (profile == null)
            {
                return new TrapRandomizationResult(false, Array.Empty<TrapAssignment>(), 0, "No randomization profile.");
            }

            presetTraits = presetTraits ?? new Dictionary<string, TrapPresetTraits>(StringComparer.Ordinal);
            monsterPoints = monsterPoints ?? Array.Empty<PlacementTaggedPoint>();
            var tagged = (slots ?? Array.Empty<TrapSlotCandidate>())
                .Where(slot => !string.IsNullOrWhiteSpace(slot.Group) && !string.IsNullOrWhiteSpace(slot.SlotId))
                .ToList();
            if (tagged.Count == 0)
            {
                return new TrapRandomizationResult(true, Array.Empty<TrapAssignment>(), 0, string.Empty);
            }

            var duplicateSlotId = tagged
                .GroupBy(slot => slot.SlotId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateSlotId != null)
            {
                return new TrapRandomizationResult(
                    false, Array.Empty<TrapAssignment>(), 0,
                    $"Duplicate trap slot id '{duplicateSlotId.Key}' among randomization candidates.");
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
                    Pool = profile.Pools.Where(entry => entry.IsTrapPreset &&
                        string.Equals(entry.Group, group.Key, StringComparison.Ordinal)).ToList(),
                })
                .Where(group => group.SpawnCount > 0)
                .ToList();

            foreach (var group in groups)
            {
                if (group.Pool.Count == 0 || group.Pool.All(entry => entry.Weight <= 0))
                {
                    return new TrapRandomizationResult(
                        false, Array.Empty<TrapAssignment>(), 0,
                        $"Trap randomization group '{group.Key}' has no drawable pool entries for stage '{profile.StageId}'.");
                }
            }

            var rng = new Random(seed);
            var lastFailure = string.Empty;
            for (var attempt = 0; attempt < profile.RerollLimit; attempt++)
            {
                var capUsed = new Dictionary<string, int>(StringComparer.Ordinal);
                var picks = new List<(TrapSlotCandidate Slot, StageRandomizationPoolEntry Entry)>();
                var attemptOk = true;

                // 함정 간 최소 거리(Q-A2)는 그룹 경계를 넘는 제약이다 — 선택된 좌표를 시도 단위로
                // 누적해 다음 그룹의 슬롯 선택이 이미 확정된 좌표와의 거리를 본다.
                var takenCoords = profile.TrapMinDistance > 0 ? new List<HexCoord>() : null;
                foreach (var group in groups)
                {
                    var selectedSlots = takenCoords != null
                        ? SampleTrapSlotsWithMinDistance(
                            group.Candidates, group.SpawnCount, profile.TrapMinDistance, takenCoords, rng)
                        : SampleTrapSlotsWithoutReplacement(group.Candidates, group.SpawnCount, rng);
                    if (selectedSlots == null)
                    {
                        lastFailure = $"Trap group '{group.Key}' cannot fill {group.SpawnCount} slots " +
                                      $"with min distance {profile.TrapMinDistance} (candidates exhausted).";
                        attemptOk = false;
                        break;
                    }

                    foreach (var slot in selectedSlots)
                    {
                        var zone = walkDistanceFromPlayer != null && walkDistanceFromPlayer.TryGetValue(slot.Coord, out var walkDistance)
                            ? profile.ZoneForBfsDistance(walkDistance)
                            : PlacementZone.Late;
                        // 지대는 몬스터와 같은 <b>하한</b>이다(2026-09-01 #14를 함정에도 — 2026-09-05 #30).
                        // 종전엔 이 줄만 정확히 일치라 두 파일의 의미론이 갈라져 있었다. 현행 Stage_1 저작에서는
                        // 갈리는 슬롯이 0(1,000판 실측)이라 드리프트 정리이고, 슬롯 재저작 뒤에도 같은 뜻이 되게 한다.
                        var eligible = group.Pool
                            .Where(entry => entry.Weight > 0 &&
                                (entry.Zone == PlacementZone.Any || zone >= entry.Zone) &&
                                (!capUsed.TryGetValue(entry.CapKey, out var used) || used < entry.MaxCount))
                            .ToList();
                        if (eligible.Count == 0)
                        {
                            lastFailure = $"Trap group '{group.Key}' slot '{slot.SlotId}' ({zone}) has no eligible pool entry (zone/cap exhausted).";
                            attemptOk = false;
                            break;
                        }

                        var entryPicked = WeightedPick(eligible, rng);
                        capUsed[entryPicked.CapKey] = (capUsed.TryGetValue(entryPicked.CapKey, out var count) ? count : 0) + 1;
                        picks.Add((slot, entryPicked));
                    }

                    if (!attemptOk)
                    {
                        break;
                    }
                }

                if (attemptOk && !ValidateTrapAttempt(picks, profile, presetTraits, monsterPoints, out lastFailure))
                {
                    attemptOk = false;
                }

                if (attemptOk)
                {
                    var assignments = picks
                        .Select(pick => new TrapAssignment(pick.Slot, pick.Entry.EntryRef))
                        .ToList();
                    return new TrapRandomizationResult(true, assignments, attempt, string.Empty);
                }
            }

            return new TrapRandomizationResult(
                false, Array.Empty<TrapAssignment>(), profile.RerollLimit,
                $"Trap randomization exceeded the reroll limit ({profile.RerollLimit}). Last failure: {lastFailure}");
        }

        /// <summary>
        /// 금지 조합 위반 검사(Q11). 배치 확정본에 대해서도 그대로 부를 수 있게 공개한다 —
        /// 시드 스윕 감사(§8)가 랜덤라이저와 같은 판정을 쓰는 근거다.
        /// </summary>
        public static bool TryFindBanViolation(
            IReadOnlyList<TrapAssignment> trapAssignments,
            IReadOnlyDictionary<string, TrapPresetTraits> presetTraits,
            IReadOnlyList<PlacementTaggedPoint> monsterPoints,
            IReadOnlyList<PlacementBanRule> bans,
            out string violation)
        {
            violation = string.Empty;
            if (trapAssignments == null || bans == null || bans.Count == 0 || monsterPoints == null)
            {
                return false;
            }

            foreach (var ban in bans)
            {
                foreach (var trap in trapAssignments)
                {
                    if (!presetTraits.TryGetValue(trap.PresetId, out var traits) ||
                        !traits.EffectKinds.Any(kind => string.Equals(kind, ban.TrapEffectKind, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var banDistance = ban.Relation == PlacementBanRelation.Adjacent
                        ? Math.Max(1, ban.Radius)
                        : traits.Radius;
                    foreach (var monster in monsterPoints)
                    {
                        if (!monster.Tags.Any(tag => string.Equals(tag, ban.MonsterTag, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        if (trap.Coord.DistanceTo(monster.Coord) <= banDistance)
                        {
                            violation = $"Ban violated: trap '{trap.SlotId}' ({trap.PresetId}, {ban.TrapEffectKind}) at {trap.Coord} " +
                                        $"is within {banDistance} of monster tag '{ban.MonsterTag}' at {monster.Coord} ({ban.Relation}).";
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// P2 — 상자 위치 셔플(placement-randomization-plan §5). 개수 고정·objectId 유지·위치만
        /// 바뀐다(보상 총량 보존 §1-4). 제약 = 상자 간 최소 거리(Q8 확정 4). 후보 칸 제약(통행·
        /// 도달·특수 셀 제외 등 §6-6)은 호출자가 candidateCoords로 걸러 넘긴다. 탐욕 배치 +
        /// 스테이지 단위 재롤 — 순수 함수, 같은 입력·시드 = 같은 배치.
        /// </summary>
        public static ChestShuffleResult ShuffleChests(
            IReadOnlyList<string> chestObjectIds,
            IReadOnlyList<HexCoord> candidateCoords,
            int minPairDistance,
            int rerollLimit,
            int seed)
        {
            var chestIds = (chestObjectIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            if (chestIds.Count == 0)
            {
                return new ChestShuffleResult(true, Array.Empty<ChestAssignment>(), 0, string.Empty);
            }

            var candidates = (candidateCoords ?? Array.Empty<HexCoord>()).Distinct().OrderBy(coord => coord).ToList();
            if (candidates.Count < chestIds.Count)
            {
                return new ChestShuffleResult(
                    false, Array.Empty<ChestAssignment>(), 0,
                    $"Chest shuffle has {candidates.Count} candidate cells for {chestIds.Count} chests.");
            }

            minPairDistance = Math.Max(0, minPairDistance);
            rerollLimit = Math.Max(1, rerollLimit);
            var rng = new Random(seed);
            for (var attempt = 0; attempt < rerollLimit; attempt++)
            {
                // 후보 순서를 셔플한 뒤 탐욕으로 최소 거리를 지키며 채운다 — 거절 표본추출보다
                // 수렴이 훨씬 좋고(§3 제약+재롤 원칙), 셔플이 곧 무작위성이라 결정성도 유지된다.
                var shuffled = candidates.ToList();
                for (var i = shuffled.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }

                var taken = new List<HexCoord>(chestIds.Count);
                foreach (var coord in shuffled)
                {
                    if (taken.All(existing => existing.DistanceTo(coord) >= minPairDistance))
                    {
                        taken.Add(coord);
                        if (taken.Count == chestIds.Count)
                        {
                            break;
                        }
                    }
                }

                if (taken.Count == chestIds.Count)
                {
                    var assignments = chestIds
                        .Select((id, index) => new ChestAssignment(id, taken[index]))
                        .ToList();
                    return new ChestShuffleResult(true, assignments, attempt, string.Empty);
                }
            }

            return new ChestShuffleResult(
                false, Array.Empty<ChestAssignment>(), rerollLimit,
                $"Chest shuffle exceeded the reroll limit ({rerollLimit}) placing {chestIds.Count} chests " +
                $"with min pair distance {minPairDistance} over {candidates.Count} candidates.");
        }

        private static bool ValidateTrapAttempt(
            IReadOnlyList<(TrapSlotCandidate Slot, StageRandomizationPoolEntry Entry)> picks,
            StageRandomizationProfile profile,
            IReadOnlyDictionary<string, TrapPresetTraits> presetTraits,
            IReadOnlyList<PlacementTaggedPoint> monsterPoints,
            out string violation)
        {
            var totalThreat = picks.Sum(pick => pick.Entry.ThreatCost);
            if (totalThreat < profile.TrapBudgetMin || totalThreat > profile.TrapBudgetMax)
            {
                violation = $"Trap threat total {totalThreat} is outside budget [{profile.TrapBudgetMin}, {profile.TrapBudgetMax}].";
                return false;
            }

            // 최소 거리 최종 게이트(Q-A2): 선택기가 이미 지키지만, 선택 로직이 어긋나도 위반 배치가
            // 새어 나가지 않게 확정본을 한 번 더 쌍별로 검사한다(스윕 감사와 같은 판정).
            if (profile.TrapMinDistance > 0)
            {
                for (var i = 0; i < picks.Count; i++)
                {
                    for (var j = i + 1; j < picks.Count; j++)
                    {
                        if (picks[i].Slot.Coord.DistanceTo(picks[j].Slot.Coord) < profile.TrapMinDistance)
                        {
                            violation = $"Traps '{picks[i].Slot.SlotId}' and '{picks[j].Slot.SlotId}' are closer than " +
                                        $"the min distance {profile.TrapMinDistance}.";
                            return false;
                        }
                    }
                }
            }

            var assignments = picks.Select(pick => new TrapAssignment(pick.Slot, pick.Entry.EntryRef)).ToList();
            if (TryFindBanViolation(assignments, presetTraits, monsterPoints, profile.Bans, out violation))
            {
                return false;
            }

            violation = string.Empty;
            return true;
        }

        /// <summary>
        /// 최소 거리 인지 슬롯 선택(Q-A2). 무작위로 뽑되 이미 확정된 좌표(전 그룹 누적)와
        /// <paramref name="minDistance"/> 미만인 후보는 버리고 계속한다 — 시도 단위 기각보다 수렴이
        /// 훨씬 좋다(상자 셔플의 탐욕 배치와 같은 원칙). 후보가 바닥나면 null(시도 실패 → 재롤).
        /// 순수·결정적: 같은 시드 = 같은 선택.
        /// </summary>
        private static List<TrapSlotCandidate> SampleTrapSlotsWithMinDistance(
            IReadOnlyList<TrapSlotCandidate> candidates,
            int count,
            int minDistance,
            List<HexCoord> takenCoords,
            Random rng)
        {
            var pool = candidates.ToList();
            var picked = new List<TrapSlotCandidate>(count);
            while (picked.Count < count && pool.Count > 0)
            {
                var index = rng.Next(pool.Count);
                var candidate = pool[index];
                pool.RemoveAt(index);
                if (takenCoords.All(coord => coord.DistanceTo(candidate.Coord) >= minDistance))
                {
                    picked.Add(candidate);
                    takenCoords.Add(candidate.Coord);
                }
            }

            return picked.Count == count ? picked : null;
        }

        private static List<TrapSlotCandidate> SampleTrapSlotsWithoutReplacement(
            IReadOnlyList<TrapSlotCandidate> candidates,
            int count,
            Random rng)
        {
            var pool = candidates.ToList();
            var picked = new List<TrapSlotCandidate>(count);
            for (var i = 0; i < count; i++)
            {
                var index = rng.Next(pool.Count);
                picked.Add(pool[index]);
                pool.RemoveAt(index);
            }

            return picked;
        }
    }
}

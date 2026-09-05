using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// 배치 랜덤화 브리지(placement-randomization-plan §2-1). 저작 소스에서 그룹 태그가 붙은
    /// 몬스터 스폰 슬롯을 순수 DTO로 추출해 <see cref="PlacementRandomizer"/>에 넘기고, 결과로
    /// 기본 맵의 몬스터 스폰 목록만 치환한 새 <see cref="HexMapData"/>를 만든다. 전투 진입
    /// 경로에서만 호출한다 — 룩뎁·타일 프리뷰·에디터 검증은 저작 원본을 그대로 쓴다.
    /// P0(<see cref="TryApply"/>) = 구성 불변 슬롯 셔플, P1(<see cref="TryApplyProfile"/>) =
    /// CSV 풀 + 위협 예산 추첨. 실패는 false로 알리고 호출자가 저작 원본으로 폴백한다.
    /// </summary>
    public static class HexMapPlacementRandomization
    {
        /// <summary>랜덤화 대상에서 제외되는 스폰 role(§4). 태그가 붙어 있어도 건드리지 않는다.</summary>
        private static readonly HashSet<string> ExcludedRoles = new HashSet<string>(StringComparer.Ordinal)
        {
            "boss", "boss-prop", "player-prop",
        };

        public static bool HasRandomizationSlots(HexSparseMapAuthoringSource source)
        {
            return source != null && source.ObjectRefs.Any(objectRef =>
                objectRef != null &&
                objectRef.IsMonsterSpawn &&
                !string.IsNullOrWhiteSpace(objectRef.RandomizationGroup) &&
                !ExcludedRoles.Contains(objectRef.Role ?? string.Empty));
        }

        /// <summary>
        /// P0 — 구성 불변 슬롯 셔플. 실패 시 false를 반환하며 <paramref name="randomizedMap"/>은
        /// null — 호출자는 <paramref name="baseMap"/>(저작 원본)으로 폴백하고 evidence를 경고
        /// 로그로 남긴다.
        /// </summary>
        public static bool TryApply(
            HexSparseMapAuthoringSource source,
            HexMapData baseMap,
            int seed,
            PlacementRandomizationConfig config,
            out HexMapData randomizedMap,
            out string evidence)
        {
            randomizedMap = null;
            config = config ?? PlacementRandomizationConfig.Default;
            if (!TryCollectSlots(source, baseMap, out var slots, out var playerSpawnCoord, out var droppedSpares, out evidence))
            {
                return false;
            }

            var result = PlacementRandomizer.Randomize(slots, playerSpawnCoord, config, seed);
            if (!result.Success)
            {
                evidence = $"Placement randomization failed (seed={seed}): {result.FailureReason}";
                return false;
            }

            randomizedMap = BuildRandomizedMap(source, baseMap, result.Assignments);
            evidence = $"Placement randomization applied: seed={seed} groups={slots.Select(slot => slot.Group).Distinct().Count()} " +
                       $"slots={slots.Count} assigned={result.Assignments.Count} rerolls={result.RerollsUsed}" +
                       DescribeDropped(droppedSpares);
            return true;
        }

        /// <summary>
        /// 몸이 여러 칸인 몬스터별로 <b>몸이 통째로 들어가는 슬롯</b>만 추린다(2026-09-05).
        ///
        /// <para>🔴 슬롯 수집(<see cref="TryCollectSlots"/>)은 「그 한 칸이 통행 가능한가」만 본다 —
        /// 한 칸 몬스터에는 맞는 검사지만 3칸 몸에는 절반의 검사다. 이동 판정은 원래부터 몸통 전 칸을
        /// 보므로(<c>HexPathfinder.CanEnter</c>), 이 화이트리스트가 붙으면 배치와 이동이 <b>같은 기준</b>을
        /// 쓰게 된다.</para>
        ///
        /// <para>몸 형상을 모르면(null·빈 목록) 아무 제약도 만들지 않는다 — 랜덤화가 게임을 깨지 않는다는
        /// 계약(§4)을 지킨다.</para>
        /// </summary>
        private static Dictionary<string, IReadOnlyCollection<string>> BuildBodyFitWhitelist(
            HexMapData baseMap,
            IReadOnlyList<PlacementSlotCandidate> slots,
            IReadOnlyDictionary<string, IReadOnlyList<HexCoord>> monsterBodyOffsets)
        {
            if (monsterBodyOffsets == null || monsterBodyOffsets.Count == 0 || slots == null)
            {
                return null;
            }

            var whitelist = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            foreach (var pair in monsterBodyOffsets)
            {
                var offsets = pair.Value;
                if (offsets == null || offsets.Count <= 1)
                {
                    continue;
                }

                var allowed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var slot in slots)
                {
                    if (string.IsNullOrWhiteSpace(slot.SlotId))
                    {
                        continue;
                    }

                    var fits = true;
                    for (var i = 0; i < offsets.Count && fits; i++)
                    {
                        var coord = slot.Coord + offsets[i];
                        fits = HexAccessibility.IsAccessible(baseMap, coord, HexTerrainTraits.Default)
                               && !baseMap.HasMovementBlockingObject(coord);
                    }

                    if (fits)
                    {
                        allowed.Add(slot.SlotId);
                    }
                }

                whitelist[pair.Key] = allowed;
            }

            return whitelist.Count == 0 ? null : whitelist;
        }

        /// <summary>
        /// P1 — CSV 프로파일(풀 + 위협 예산 + 지대 + 밀도) 추첨. 지대는 PlayerSpawn 기준 보행
        /// BFS 거리로 계산한다. 실패 시 false — 호출자는 저작 원본으로 폴백한다.
        /// </summary>
        /// <param name="stealthMonsterIds">
        /// 「은신」 특성을 가진 몬스터 id 집합(요괴 §4-1). bans의 <c>monster:stealth</c> 행이 이 태그를
        /// 표적으로 삼는다 — 특성은 전투 카탈로그가 정본이라 맵 레이어가 직접 알 수 없고, 아는 쪽
        /// (전투 컨트롤러)이 넣어 준다. null·빈 집합이면 예전처럼 role 태그만 붙는다.
        /// </param>
        public static bool TryApplyProfile(
            HexSparseMapAuthoringSource source,
            HexMapData baseMap,
            int seed,
            StageRandomizationProfile profile,
            out HexMapData randomizedMap,
            out string evidence,
            IReadOnlyCollection<string> stealthMonsterIds = null,
            IReadOnlyDictionary<string, IReadOnlyList<HexCoord>> monsterBodyOffsets = null)
        {
            randomizedMap = null;
            if (profile == null || !profile.Enabled)
            {
                evidence = "Placement profile randomization skipped: no enabled profile.";
                return false;
            }

            if (!TryCollectSlots(source, baseMap, out var slots, out var playerSpawnCoord, out var droppedSpares, out evidence))
            {
                return false;
            }

            var walkDistances = HexWalkDistances.FromCoord(baseMap, playerSpawnCoord);
            var allowedSlotIds = BuildBodyFitWhitelist(baseMap, slots, monsterBodyOffsets);
            var result = PlacementRandomizer.RandomizeWithProfile(
                slots, playerSpawnCoord, profile, walkDistances, seed, allowedSlotIds);
            if (!result.Success)
            {
                evidence = $"Placement profile randomization failed (stage={profile.StageId} seed={seed}): {result.FailureReason}";
                return false;
            }

            // P2 — 함정 풀 추첨(§5). 몬스터 배치가 확정된 뒤에 돌린다: bans(Q11)가 몬스터 좌표·태그를
            // 대조하기 때문이다. 시드는 몬스터와 독립 스트림(DeriveSeed) — 어느 한쪽의 소비량 변화가
            // 다른 쪽 배치를 흔들지 않는다. 실패는 전체 저작 원본 폴백(§4 계약과 동일 — 부분 적용으로
            // 결정성 해석이 갈라지면 안 된다).
            var trapAssignments = (IReadOnlyList<TrapAssignment>)Array.Empty<TrapAssignment>();
            var trapEvidence = string.Empty;
            if (profile.TrapThreatBudget > 0)
            {
                if (!TryCollectTrapSlots(source, baseMap, out var trapSlots, out var droppedTrapSpares, out evidence))
                {
                    return false;
                }

                if (trapSlots.Count > 0)
                {
                    var presetTraits = BuildPresetTraits();
                    var monsterPoints = BuildMonsterTaggedPoints(baseMap, slots, result.Assignments, stealthMonsterIds);
                    var trapResult = PlacementRandomizer.RandomizeTraps(
                        trapSlots, profile, walkDistances, presetTraits, monsterPoints,
                        PlacementRandomizer.DeriveSeed(seed, RunSeedStreams.Traps));
                    if (!trapResult.Success)
                    {
                        evidence = $"Trap randomization failed (stage={profile.StageId} seed={seed}): {trapResult.FailureReason}";
                        return false;
                    }

                    trapAssignments = trapResult.Assignments;
                    var trapComposition = string.Join(" ", trapAssignments
                        .GroupBy(assignment => assignment.PresetId, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => $"{group.Key}x{group.Count()}"));
                    var trapThreat = SumTrapThreat(profile, trapAssignments);
                    trapEvidence = $" traps={trapAssignments.Count} trapThreat={trapThreat}/[{profile.TrapBudgetMin},{profile.TrapBudgetMax}] " +
                                   $"trapComposition=[{trapComposition}] trapRerolls={trapResult.RerollsUsed}" +
                                   DescribeDropped(droppedTrapSpares);
                }
            }

            // P3 — 서비스 배치(2026-09-01). 🔴 반드시 상자보다 <b>먼저</b> 돌린다: 상자 후보 수집이
            // 서비스 점유칸 반경을 비우는데, 서비스가 옮겨진 뒤의 좌표를 봐야 하기 때문이다. 순서를
            // 뒤집으면 상자가 옛 서비스 자리를 피하고 새 서비스 옆에 붙는다(조용히 틀린다 —
            // 예외도 로그도 없다).
            var serviceAssignments = (IReadOnlyList<ServiceAssignment>)Array.Empty<ServiceAssignment>();
            var serviceEvidence = string.Empty;
            if (profile.ServiceShopCount > 0 || profile.ServiceCamperCount > 0)
            {
                if (!TryPlaceServices(source, baseMap, profile, seed, out serviceAssignments, out serviceEvidence))
                {
                    evidence = serviceEvidence;
                    return false;
                }
            }

            // P2 — 상자 위치 셔플(§5). 개수 고정·objectId 유지·좌표만 바뀐다. 후보 칸 제약은 §6-6.
            var chestAssignments = (IReadOnlyList<ChestAssignment>)Array.Empty<ChestAssignment>();
            var chestEvidence = string.Empty;
            if (profile.ChestMinDistance >= 0)
            {
                var chestIds = baseMap.ObjectRefs
                    .Where(objectRef => IsShuffledChest(objectRef))
                    .Select(objectRef => objectRef.ObjectId)
                    .ToList();
                if (chestIds.Count > 0)
                {
                    var candidates = CollectChestCandidateCoords(
                        source, baseMap, walkDistances, result.Assignments, serviceAssignments);
                    var chestResult = PlacementRandomizer.ShuffleChests(
                        chestIds, candidates, profile.ChestMinDistance, profile.RerollLimit,
                        PlacementRandomizer.DeriveSeed(seed, RunSeedStreams.Chests));
                    if (!chestResult.Success)
                    {
                        evidence = $"Chest shuffle failed (stage={profile.StageId} seed={seed}): {chestResult.FailureReason}";
                        return false;
                    }

                    chestAssignments = chestResult.Assignments;
                    chestEvidence = $" chests={chestAssignments.Count} chestMinDistance={profile.ChestMinDistance} " +
                                    $"chestRerolls={chestResult.RerollsUsed}";
                }
            }

            randomizedMap = BuildRandomizedMap(
                source, baseMap, result.Assignments, trapAssignments, chestAssignments, serviceAssignments);
            var costByMonster = profile.Pools.Where(entry => entry.IsMonster)
                .GroupBy(entry => entry.EntryRef, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().ThreatCost, StringComparer.Ordinal);
            var totalThreat = result.Assignments.Sum(assignment =>
                costByMonster.TryGetValue(assignment.MonsterId, out var cost) ? cost : 0);
            var composition = string.Join(" ", result.Assignments
                .GroupBy(assignment => assignment.MonsterId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}x{group.Count()}"));
            evidence = $"Placement profile randomization applied: stage={profile.StageId} seed={seed} " +
                       $"assigned={result.Assignments.Count} threat={totalThreat}/[{profile.BudgetMin},{profile.BudgetMax}] " +
                       $"composition=[{composition}] rerolls={result.RerollsUsed}" + DescribeDropped(droppedSpares) +
                       trapEvidence + chestEvidence + serviceEvidence;
            return true;
        }

        private static bool TryCollectSlots(
            HexSparseMapAuthoringSource source,
            HexMapData baseMap,
            out List<PlacementSlotCandidate> slots,
            out HexCoord playerSpawnCoord,
            out List<string> droppedSpares,
            out string evidence)
        {
            slots = new List<PlacementSlotCandidate>();
            droppedSpares = new List<string>();
            playerSpawnCoord = default;
            if (source == null || baseMap == null)
            {
                evidence = "Placement randomization skipped: no source or base map.";
                return false;
            }

            var taggedRefs = source.ObjectRefs
                .Where(objectRef => objectRef != null &&
                    objectRef.IsMonsterSpawn &&
                    !string.IsNullOrWhiteSpace(objectRef.RandomizationGroup) &&
                    !ExcludedRoles.Contains(objectRef.Role ?? string.Empty))
                .ToList();
            if (taggedRefs.Count == 0)
            {
                evidence = "Placement randomization skipped: no tagged slots.";
                return false;
            }

            var playerSpawn = source.ObjectRefs.FirstOrDefault(objectRef => objectRef != null && objectRef.IsPlayerSpawn);
            if (playerSpawn == null)
            {
                evidence = "Placement randomization failed: map has no player spawn to anchor the safe radius.";
                return false;
            }

            playerSpawnCoord = playerSpawn.Coord;
            var patrolAreaIds = new HashSet<string>(baseMap.PatrolAreas.Select(area => area.Id), StringComparer.Ordinal);
            foreach (var objectRef in taggedRefs)
            {
                // 점유 슬롯은 TryToHexMapData가 이미 검증했다. 예비 슬롯은 빌드가 건너뛰므로
                // 여기서 같은 기준(셀 존재·통행 가능·이동 차단 오브젝트 없음·순찰 영역 해소)으로
                // 거른다 — 무효한 예비 슬롯은 후보에서 빠질 뿐 전체를 깨지 않는다.
                // 접근성(물·사방 높이차)은 배치·오버레이가 같은 술어를 공유한다(2026-08-20 #4).
                var valid = HexAccessibility.IsAccessible(baseMap, objectRef.Coord, HexTerrainTraits.Default) &&
                            !baseMap.HasMovementBlockingObject(objectRef.Coord) &&
                            (string.IsNullOrWhiteSpace(objectRef.PatrolAreaId) || patrolAreaIds.Contains(objectRef.PatrolAreaId));
                if (!valid)
                {
                    if (objectRef.IsRandomizationSpareSlot)
                    {
                        droppedSpares.Add(objectRef.ObjectId);
                        continue;
                    }

                    evidence = $"Placement randomization failed: occupied slot '{objectRef.ObjectId}' at {objectRef.Coord} is invalid in the built map.";
                    return false;
                }

                slots.Add(new PlacementSlotCandidate(
                    objectRef.ObjectId,
                    objectRef.Coord,
                    objectRef.RandomizationGroup.Trim(),
                    objectRef.ObjectRef,
                    objectRef.Role,
                    objectRef.EnabledForPurpose,
                    objectRef.PatrolAreaId));
            }

            evidence = string.Empty;
            return true;
        }

        private static HexMapData BuildRandomizedMap(
            HexSparseMapAuthoringSource source,
            HexMapData baseMap,
            IReadOnlyList<PlacementSpawnAssignment> assignments,
            IReadOnlyList<TrapAssignment> trapAssignments = null,
            IReadOnlyList<ChestAssignment> chestAssignments = null,
            IReadOnlyList<ServiceAssignment> serviceAssignments = null)
        {
            var taggedIds = new HashSet<string>(source.ObjectRefs
                .Where(objectRef => objectRef != null &&
                    objectRef.IsMonsterSpawn &&
                    !string.IsNullOrWhiteSpace(objectRef.RandomizationGroup) &&
                    !ExcludedRoles.Contains(objectRef.Role ?? string.Empty))
                .Select(objectRef => objectRef.ObjectId), StringComparer.Ordinal);
            var spawnRefs = baseMap.MonsterSpawnRefs
                .Where(spawnRef => !taggedIds.Contains(spawnRef.Id))
                .Concat(assignments.Select(assignment => new HexMonsterSpawnRef(
                    assignment.SlotId,
                    assignment.MonsterId,
                    assignment.Coord,
                    assignment.Role,
                    assignment.EnabledForPurpose,
                    assignment.PatrolAreaId)))
                .ToList();

            // 함정 치환(§2-4 프리셋 비대칭): trapId(슬롯)와 좌표는 슬롯 저작값이고 프리셋 내용만
            // 바뀐다 — ConsumedTrapIds(trapId 키)·RevealedTrapCoords(좌표 키) 세이브 정합은 시드
            // 결정성(같은 시드 = 같은 슬롯 선택)이 지킨다(§5). 몬스터 스폰과 같은 문법: 태깅된
            // 슬롯은 전부 걷어내고 추첨 결과만 합류시킨다 — 예비 슬롯(Q-A2)이 뽑히면 그만큼의
            // 점유 슬롯이 안 뽑히므로, 안 뽑힌 점유 슬롯을 남겨 두면 함정 수가 불어난다.
            var trapRefs = baseMap.TrapRefs;
            if (trapAssignments != null && trapAssignments.Count > 0)
            {
                var presetCatalog = TrapPresetCatalog.LoadDefault();
                var taggedTrapIds = new HashSet<string>(source.TrapRefs
                    .Where(trapRef => trapRef != null && !string.IsNullOrWhiteSpace(trapRef.RandomizationGroup))
                    .Select(trapRef => trapRef.TrapId), StringComparer.Ordinal);
                trapRefs = baseMap.TrapRefs
                    .Where(trap => !taggedTrapIds.Contains(trap.TrapId))
                    .Concat(trapAssignments
                        .OrderBy(assignment => assignment.SlotId, StringComparer.Ordinal)
                        .Select(assignment => BuildTrapFromPreset(presetCatalog, assignment)))
                    .ToList();
            }

            // 상자 재배치(§6-5): ObjectRefs 레벨에서 objectId 유지·좌표만 바꾼 사본으로 치환한다.
            // objectRefsByCoord 파생은 HexMapData 생성자가 다시 계산하므로 좌표 이동은 안전하다.
            // 상자·서비스 재배치(§6-5, 그리고 2026-09-01 서비스): ObjectRefs 레벨에서 objectId 유지·
            // 좌표만 바꾼 사본으로 치환한다. 두 종류를 한 표에 합쳐 한 번에 훑는다 — 두 번 훑으면
            // 나중 것이 앞선 치환을 덮을 여지가 생긴다. 뽑히지 않은 서비스 예비 슬롯은 애초에
            // baseMap에 없으므로(맵 빌드가 건너뛴다) 걷어낼 것이 없다 — 함정의 「안 뽑힌 점유
            // 슬롯이 남아 개수가 불어나는」 함정이 여기엔 구조적으로 없다.
            var objectRefs = baseMap.ObjectRefs;
            var coordByObjectId = new Dictionary<string, HexCoord>(StringComparer.Ordinal);
            if (chestAssignments != null)
            {
                foreach (var assignment in chestAssignments)
                {
                    coordByObjectId[assignment.ObjectId] = assignment.Coord;
                }
            }

            if (serviceAssignments != null)
            {
                foreach (var assignment in serviceAssignments)
                {
                    coordByObjectId[assignment.ObjectId] = assignment.Coord;
                }
            }

            if (coordByObjectId.Count > 0)
            {
                objectRefs = baseMap.ObjectRefs
                    .Select(objectRef => coordByObjectId.TryGetValue(objectRef.ObjectId, out var coord)
                        ? objectRef.WithCoord(coord)
                        : objectRef)
                    .ToList();
            }

            return new HexMapData(
                baseMap.AllCells,
                baseMap.ObjectiveBindings,
                spawnRefs,
                baseMap.PatrolAreas,
                objectRefs,
                trapRefs,
                baseMap.Areas);
        }

        private static HexTrapData BuildTrapFromPreset(TrapPresetCatalog presetCatalog, TrapAssignment assignment)
        {
            if (presetCatalog == null || !presetCatalog.TryGet(assignment.PresetId, out var preset))
            {
                // 풀 감사(pool CSV 무결성 테스트)가 막는 경로 — 그래도 뚫리면 밟아도 무해한 빈 함정이
                // 아니라 즉시 드러나는 저작 오류여야 하므로 예외로 죽인다.
                throw new InvalidOperationException(
                    $"Trap randomization drew preset '{assignment.PresetId}' that is missing from TrapPresetCatalog.");
            }

            return new HexTrapData(
                assignment.SlotId,
                assignment.Coord,
                preset.Radius,
                new[] { new HexTrapEffectData(preset.EffectKind, preset.EffectAmount, preset.DurationTurns, preset.MonsterDefinitionId, preset.StatusCardId) },
                preset.AffectsPlayer,
                preset.AffectsMonsters,
                preset.OneShot,
                preset.TriggerOnEnter,
                preset.PeriodTurns,
                preset.PresetId);
        }

        private static bool TryCollectTrapSlots(
            HexSparseMapAuthoringSource source,
            HexMapData baseMap,
            out List<TrapSlotCandidate> slots,
            out List<string> droppedSpares,
            out string evidence)
        {
            slots = new List<TrapSlotCandidate>();
            droppedSpares = new List<string>();
            foreach (var trapRef in source.TrapRefs)
            {
                if (trapRef == null || string.IsNullOrWhiteSpace(trapRef.RandomizationGroup))
                {
                    continue;
                }

                // 점유 슬롯은 TryBuildTrapRefs가 이미 검증했다. 예비 슬롯은 빌드가 건너뛰므로 여기서
                // 같은 기준(셀 존재·통행 가능·이동 차단 오브젝트 없음)으로 거른다 — §6-3 「통행 ≠ 도달」
                // 은 시드 스윕 감사(AllRandomizationSlotsAreWalkReachable 패턴)가 상시 게이트한다.
                // 접근성(물·사방 높이차)은 배치·오버레이가 같은 술어를 공유한다(2026-08-20 #4).
                var valid = HexAccessibility.IsAccessible(baseMap, trapRef.Coord, HexTerrainTraits.Default) &&
                            !baseMap.HasMovementBlockingObject(trapRef.Coord);
                if (!valid)
                {
                    if (trapRef.IsRandomizationSpareSlot)
                    {
                        droppedSpares.Add(trapRef.TrapId);
                        continue;
                    }

                    evidence = $"Trap randomization failed: occupied trap slot '{trapRef.TrapId}' at {trapRef.Coord} is invalid in the built map.";
                    return false;
                }

                slots.Add(new TrapSlotCandidate(
                    trapRef.TrapId,
                    trapRef.Coord,
                    trapRef.RandomizationGroup.Trim(),
                    hasOccupant: !trapRef.IsRandomizationSpareSlot));
            }

            evidence = string.Empty;
            return true;
        }

        /// <summary>TrapPresetCatalog를 순수 DTO로 사영한다 — bans 판정(순수 계층)이 쓴다.</summary>
        private static Dictionary<string, TrapPresetTraits> BuildPresetTraits()
        {
            var traits = new Dictionary<string, TrapPresetTraits>(StringComparer.Ordinal);
            var catalog = TrapPresetCatalog.LoadDefault();
            if (catalog == null)
            {
                return traits;
            }

            foreach (var preset in catalog.Presets)
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.PresetId))
                {
                    continue;
                }

                traits[preset.PresetId] = new TrapPresetTraits(
                    preset.PresetId,
                    new[] { preset.EffectKind.ToString() },
                    preset.Radius);
            }

            return traits;
        }

        /// <summary>
        /// bans의 몬스터 쪽 표적(Q11): 랜덤화로 확정된 스폰 + 고정 스폰(보스 등) 전부. 태그 = 스폰 role
        /// + 특성 태그. 2026-08-20(요괴 S4)에 「은신」이 생기면서 <c>monster:stealth</c> 휴면 행이 깨어났다 —
        /// 특성 정본은 전투 카탈로그라 호출부가 id 집합을 넣어 준다.
        /// </summary>
        private static List<PlacementTaggedPoint> BuildMonsterTaggedPoints(
            HexMapData baseMap,
            IReadOnlyList<PlacementSlotCandidate> taggedSlots,
            IReadOnlyList<PlacementSpawnAssignment> assignments,
            IReadOnlyCollection<string> stealthMonsterIds)
        {
            var stealth = stealthMonsterIds == null || stealthMonsterIds.Count == 0
                ? null
                : new HashSet<string>(stealthMonsterIds, StringComparer.OrdinalIgnoreCase);
            var taggedSlotIds = new HashSet<string>(taggedSlots.Select(slot => slot.SlotId), StringComparer.Ordinal);
            var points = new List<PlacementTaggedPoint>();
            foreach (var spawnRef in baseMap.MonsterSpawnRefs.Where(spawnRef => !taggedSlotIds.Contains(spawnRef.Id)))
            {
                points.Add(new PlacementTaggedPoint(spawnRef.Coord, TagsForMonster(spawnRef.SpawnRole, spawnRef.MonsterId, stealth)));
            }

            foreach (var assignment in assignments)
            {
                points.Add(new PlacementTaggedPoint(assignment.Coord, TagsForMonster(assignment.Role, assignment.MonsterId, stealth)));
            }

            return points;
        }

        /// <summary>배치 태그: 스폰 role + 특성. bans는 이 목록을 대소문자 무시로 대조한다.</summary>
        private static IReadOnlyList<string> TagsForMonster(string role, string monsterId, HashSet<string> stealthMonsterIds)
        {
            var tags = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(role))
            {
                tags.Add(role.Trim());
            }

            if (stealthMonsterIds != null
                && !string.IsNullOrWhiteSpace(monsterId)
                && stealthMonsterIds.Contains(monsterId.Trim()))
            {
                tags.Add(StealthTag);
            }

            return tags;
        }

        /// <summary>bans CSV가 <c>monster:stealth</c>로 가리키는 태그 이름.</summary>
        internal const string StealthTag = "stealth";

        /// <summary>
        /// 상자 셔플 후보 칸(§6-6): 통행+도달 가능 · 특수 셀(EventId/LandmarkId) 제외 · 오브젝트 점유
        /// 칸 제외(셔플 대상 상자 자신의 현재 칸은 후보) · 함정 칸 제외(예비 슬롯 포함) · 보스 아레나와
        /// 그 결계 링 제외 · 몬스터 배치 칸 제외 · 서비스 오브젝트(잡화점/캠핑카/공작소) 반경 2 제외
        /// (배치 인계문 Q4 확정: 서비스 옆 상자 밀집 방지). PlayerSpawn은 오브젝트 점유로 함께
        /// 빠진다 — 안전 반경은 상자에 불요(Q8 확정: 보상이라 가까워도 무해).
        /// </summary>
        private static List<HexCoord> CollectChestCandidateCoords(
            HexSparseMapAuthoringSource source,
            HexMapData baseMap,
            IReadOnlyDictionary<HexCoord, int> walkDistances,
            IReadOnlyList<PlacementSpawnAssignment> monsterAssignments,
            IReadOnlyList<ServiceAssignment> serviceAssignments)
        {
            var blocked = new HashSet<HexCoord>();
            foreach (var objectRef in baseMap.ObjectRefs.Where(objectRef => !IsShuffledChest(objectRef)))
            {
                foreach (var coord in objectRef.OccupiedCoords)
                {
                    blocked.Add(coord);
                }
            }

            // 🔴 서비스는 이 판에서 <b>옮겨졌을 수 있다</b>(2026-09-01 추첨 배치). baseMap의 좌표는
            //    저작 원본이라 이미 낡았을 수 있으므로, 배치가 넘어왔으면 그쪽을 정본으로 쓴다 —
            //    몬스터 배치를 인자로 받는 것과 같은 이유다.
            var movedServiceCoords = (serviceAssignments ?? Array.Empty<ServiceAssignment>())
                .ToDictionary(assignment => assignment.ObjectId, assignment => assignment.Coord, StringComparer.Ordinal);
            var serviceCoords = baseMap.ObjectRefs
                .Where(IsServiceObject)
                .SelectMany(objectRef => movedServiceCoords.TryGetValue(objectRef.ObjectId, out var moved)
                    ? objectRef.WithCoord(moved).OccupiedCoords
                    : objectRef.OccupiedCoords)
                .ToList();

            foreach (var trapRef in source.TrapRefs.Where(trapRef => trapRef != null))
            {
                blocked.Add(trapRef.Coord);
            }

            foreach (var area in baseMap.Areas.Where(area => area.IsBossArena))
            {
                foreach (var coord in area.Coords)
                {
                    blocked.Add(coord);
                }

                foreach (var coord in area.EnumerateBoundaryRing())
                {
                    blocked.Add(coord);
                }
            }

            foreach (var spawnRef in baseMap.MonsterSpawnRefs)
            {
                blocked.Add(spawnRef.Coord);
            }

            foreach (var assignment in monsterAssignments)
            {
                blocked.Add(assignment.Coord);
            }

            return baseMap.AllCells
                .Where(cell => cell.BaseWalkable &&
                    walkDistances.ContainsKey(cell.Coord) &&
                    string.IsNullOrEmpty(cell.EventId) &&
                    string.IsNullOrEmpty(cell.LandmarkId) &&
                    !blocked.Contains(cell.Coord) &&
                    serviceCoords.All(serviceCoord => serviceCoord.DistanceTo(cell.Coord) > ServiceChestBanRadius))
                .Select(cell => cell.Coord)
                .ToList();
        }

        /// <summary>서비스 오브젝트 주변 상자 금지 반경(Q4 확정). 후보 수집에서만 쓰는 저작 규칙 값.</summary>
        private const int ServiceChestBanRadius = 2;

        private static bool IsShuffledChest(HexMapObjectData objectRef)
        {
            return string.Equals(objectRef.ObjectType, "TreasureChest", StringComparison.Ordinal);
        }

        /// <summary>저작 슬롯이 서비스 추첨 후보임을 알리는 randomizationGroup 접두사.</summary>
        private const string ServiceGroupPrefix = "svc-";

        /// <summary>
        /// 서비스 추첨(2026-09-01). 후보 슬롯은 저작 소스에서 <c>svc-</c> 태그가 붙은 objectRef 전부다 —
        /// 실물이 서 있는 칸과 예비 슬롯을 구분하지 않는다(둘 다 「설 수 있는 자리」일 뿐).
        ///
        /// <para>
        /// 세울 오브젝트는 <b>baseMap에 실제로 있는</b> 서비스들이다. 개수를 프로파일이 아니라 맵에서
        /// 읽는 이유: objectId가 소비 기록의 키라서 저작에 있는 것만 세울 수 있고, 프로파일 값은
        /// 그것과 어긋났는지 확인하는 <b>대조용</b>으로 쓴다 — 저작이 조용히 드리프트하면 여기서 잡힌다.
        /// </para>
        /// </summary>
        private static bool TryPlaceServices(
            HexSparseMapAuthoringSource source,
            HexMapData baseMap,
            StageRandomizationProfile profile,
            int seed,
            out IReadOnlyList<ServiceAssignment> assignments,
            out string evidence)
        {
            assignments = Array.Empty<ServiceAssignment>();
            var slots = new List<ServiceSlotCandidate>();
            var routeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var objectRef in source.ObjectRefs)
            {
                if (objectRef == null || !objectRef.IsServiceObject)
                {
                    continue;
                }

                var group = objectRef.RandomizationGroup.Trim();
                if (!group.StartsWith(ServiceGroupPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                // svc-main|loop → ["main", "loop"]. 공유 구간이 두 루트를 한 태그에 담는 문법이다.
                var routes = group.Substring(ServiceGroupPrefix.Length)
                    .Split('|')
                    .Select(id => id.Trim())
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();
                if (routes.Count == 0)
                {
                    evidence = $"Service placement failed: slot '{objectRef.ObjectId}' has group '{group}' with no route id.";
                    return false;
                }

                foreach (var route in routes)
                {
                    routeIds.Add(route);
                }

                slots.Add(new ServiceSlotCandidate(objectRef.ObjectId, objectRef.Coord, routes));
            }

            var requests = new List<ServicePlacementRequest>();
            if (!TryBuildServiceRequest(baseMap, "Shop", data => data.IsShop, profile.ServiceShopCount,
                    profile.ServiceMinDistance, requests, out evidence) ||
                !TryBuildServiceRequest(baseMap, "CamperVan", data => data.IsCamperVan, profile.ServiceCamperCount,
                    profile.ServiceMinDistance, requests, out evidence))
            {
                return false;
            }

            if (requests.Count == 0)
            {
                evidence = string.Empty;
                return true;
            }

            // 스트림 3 — 몬스터(원시 시드)·함정(1)·상자(2)를 건드리지 않는다. 기존 시드의 몬스터·함정
            // 배치가 그대로 재현되는 것이 이 선택의 목적이다.
            var result = PlacementRandomizer.PlaceServices(
                slots, requests, routeIds, profile.ServiceRouteMin, profile.RerollLimit,
                PlacementRandomizer.DeriveSeed(seed, RunSeedStreams.Services));
            if (!result.Success)
            {
                evidence = $"Service placement failed (stage={profile.StageId} seed={seed}): {result.FailureReason}";
                return false;
            }

            assignments = result.Assignments;
            var composition = string.Join(" ", requests.Select(request => $"{request.Kind}x{request.ObjectIds.Count}"));
            evidence = $" services={assignments.Count} serviceComposition=[{composition}] " +
                       $"serviceSlots={slots.Count} serviceRoutes=[{string.Join("|", routeIds.OrderBy(id => id, StringComparer.Ordinal))}] " +
                       $"serviceMinDistance={profile.ServiceMinDistance} serviceRouteMin={profile.ServiceRouteMin} " +
                       $"serviceRerolls={result.RerollsUsed}";
            return true;
        }

        private static bool TryBuildServiceRequest(
            HexMapData baseMap,
            string kind,
            Func<HexMapObjectData, bool> match,
            int expectedCount,
            int minPairDistance,
            List<ServicePlacementRequest> requests,
            out string evidence)
        {
            var objectIds = baseMap.ObjectRefs
                .Where(objectRef => match(objectRef))
                .Select(objectRef => objectRef.ObjectId)
                .ToList();
            if (expectedCount > 0 && objectIds.Count != expectedCount)
            {
                evidence = $"Service placement failed: profile expects {expectedCount} {kind} object(s) " +
                           $"but the authored map has {objectIds.Count}.";
                return false;
            }

            if (objectIds.Count > 0)
            {
                requests.Add(new ServicePlacementRequest(kind, objectIds, minPairDistance));
            }

            evidence = string.Empty;
            return true;
        }

        /// <summary>1회 소비 서비스 오브젝트 3종(잡화점/캠핑카/공작소) — 상자 셔플의 반경 금지 대상.</summary>
        private static bool IsServiceObject(HexMapObjectData objectRef)
        {
            return string.Equals(objectRef.ObjectType, "Shop", StringComparison.Ordinal) ||
                   string.Equals(objectRef.ObjectType, "CamperVan", StringComparison.Ordinal) ||
                   string.Equals(objectRef.ObjectType, "Workshop", StringComparison.Ordinal);
        }

        private static int SumTrapThreat(StageRandomizationProfile profile, IReadOnlyList<TrapAssignment> trapAssignments)
        {
            var costByPreset = profile.Pools.Where(entry => entry.IsTrapPreset)
                .GroupBy(entry => entry.EntryRef, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().ThreatCost, StringComparer.Ordinal);
            return trapAssignments.Sum(assignment =>
                costByPreset.TryGetValue(assignment.PresetId, out var cost) ? cost : 0);
        }

        private static string DescribeDropped(List<string> droppedSpares)
        {
            return droppedSpares.Count > 0 ? $" droppedInvalidSpares=[{string.Join(",", droppedSpares)}]" : string.Empty;
        }
    }
}

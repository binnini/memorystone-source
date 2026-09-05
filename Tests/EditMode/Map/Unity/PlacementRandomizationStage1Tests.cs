#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using SeoulPlayup.MapDesign.Editor;
using UnityEditor;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    /// <summary>
    /// 출하 맵(Stage_1) 배치 랜덤화 감사(placement-randomization-plan §8) — 이 트랙의 핵심 자산.
    /// 시드 스윕으로 ①안전 반경 위반 0 ②구성(멀티셋) 보존 ③슬롯 유효성(통행·차단·순찰 해소)
    /// ④폴백 발생률 &lt; 1%를 게이트로 못 박고, 결정성(같은 시드 = 같은 배치)과 보스 불변을 본다.
    /// 랜덤화 자체는 순수 C#이라 씬 로드 없이 돈다.
    /// </summary>
    public sealed class PlacementRandomizationStage1Tests
    {
        private const string Stage1SourcePath = "Assets/Data/Map/Authoring/Stage_1_Source.asset";
        private const int SweepSeedCount = 200;
        private const int SafeRadius = 6;

        private static HexSparseMapAuthoringSource LoadSource()
        {
            var source = AssetDatabase.LoadAssetAtPath<HexSparseMapAuthoringSource>(Stage1SourcePath);
            Assert.That(source, Is.Not.Null, $"Stage_1 authoring source not found at {Stage1SourcePath}.");
            return source;
        }

        private static HexMapData BuildBaseMap(HexSparseMapAuthoringSource source)
        {
            Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
            return map;
        }

        private static List<HexMapObjectRef> TaggedSlots(HexSparseMapAuthoringSource source)
        {
            return source.ObjectRefs
                .Where(objectRef => objectRef != null &&
                    objectRef.IsMonsterSpawn &&
                    !string.IsNullOrWhiteSpace(objectRef.RandomizationGroup))
                .ToList();
        }

        [Test]
        public void AuthoredSourceValidatesWithSpareSlots()
        {
            var report = HexMapValidationUtility.Validate(LoadSource(), (IEnumerable<string>)null);

            Assert.That(report.HasErrors, Is.False,
                string.Join("\n", report.Errors.Select(item => item.Message)));
        }

        [Test]
        public void Stage1IsTaggedForRandomization()
        {
            var source = LoadSource();
            var tagged = TaggedSlots(source);
            var occupied = tagged.Count(slot => !string.IsNullOrWhiteSpace(slot.ObjectRef));
            var spares = tagged.Count(slot => slot.IsRandomizationSpareSlot);

            Assert.That(occupied, Is.GreaterThanOrEqualTo(15), "태깅된 점유 슬롯이 예상보다 적다 — 저작이 유실됐다.");
            Assert.That(spares, Is.GreaterThanOrEqualTo(10), "예비 슬롯이 예상보다 적다 — 저작이 유실됐다.");
            // 보스·기물은 랜덤화 대상이 아니다(role 필터가 아니라 「태그를 안 붙인다」가 1차 방어).
            Assert.That(
                tagged.Where(slot => slot.Role == "boss" || slot.Role == "boss-prop" || slot.Role == "player-prop"),
                Is.Empty, "boss/prop 스폰에 랜덤화 태그가 붙었다.");
        }

        [Test]
        public void BaseMapSkipsSpareSlotsAndKeepsAuthoredSpawns()
        {
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var authoredOccupied = source.ObjectRefs
                .Where(objectRef => objectRef != null && objectRef.IsMonsterSpawn && !objectRef.IsRandomizationSpareSlot)
                .ToList();

            Assert.That(baseMap.MonsterSpawnRefs.Count, Is.EqualTo(authoredOccupied.Count));
            Assert.That(baseMap.MonsterSpawnRefs.All(spawnRef => !string.IsNullOrWhiteSpace(spawnRef.MonsterId)), Is.True,
                "예비 슬롯이 기본 맵의 스폰으로 새어 들어왔다.");
        }

        [Test]
        public void SameSeedProducesTheIdenticalMap()
        {
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);

            Assert.That(HexMapPlacementRandomization.TryApply(
                source, baseMap, 424242, PlacementRandomizationConfig.Default, out var first, out var evidence1), Is.True, evidence1);
            Assert.That(HexMapPlacementRandomization.TryApply(
                source, baseMap, 424242, PlacementRandomizationConfig.Default, out var second, out var evidence2), Is.True, evidence2);

            Assert.That(
                second.MonsterSpawnRefs.Select(Describe).OrderBy(text => text),
                Is.EqualTo(first.MonsterSpawnRefs.Select(Describe).OrderBy(text => text)));
        }

        [Test]
        public void BossSpawnIsNeverMoved()
        {
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var authoredBoss = baseMap.MonsterSpawnRefs.Single(spawnRef => spawnRef.SpawnRole == "boss");

            for (var seed = 0; seed < 20; seed++)
            {
                Assert.That(HexMapPlacementRandomization.TryApply(
                    source, baseMap, seed, PlacementRandomizationConfig.Default, out var randomized, out var evidence), Is.True, evidence);
                var boss = randomized.MonsterSpawnRefs.Single(spawnRef => spawnRef.SpawnRole == "boss");
                Assert.That(boss.Coord, Is.EqualTo(authoredBoss.Coord), $"seed {seed}");
                Assert.That(boss.MonsterId, Is.EqualTo(authoredBoss.MonsterId), $"seed {seed}");
            }
        }

        [Test]
        public void SeedSweepHasNoViolationsAndFallbackStaysUnderOnePercent()
        {
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var playerSpawn = source.ObjectRefs.Single(objectRef => objectRef != null && objectRef.IsPlayerSpawn).Coord;
            var taggedIds = new HashSet<string>(TaggedSlots(source).Select(slot => slot.ObjectId));
            var patrolAreaIds = new HashSet<string>(baseMap.PatrolAreas.Select(area => area.Id));
            var authoredComposition = baseMap.MonsterSpawnRefs
                .Select(spawnRef => spawnRef.MonsterId).OrderBy(id => id).ToList();

            var fallbacks = 0;
            var distinctLayouts = new HashSet<string>();
            for (var seed = 0; seed < SweepSeedCount; seed++)
            {
                if (!HexMapPlacementRandomization.TryApply(
                        source, baseMap, seed, PlacementRandomizationConfig.Default, out var randomized, out _))
                {
                    fallbacks++;
                    continue;
                }

                var spawns = randomized.MonsterSpawnRefs;
                // ② 구성 보존: 몬스터 멀티셋은 저작 원본과 완전히 같다.
                Assert.That(
                    spawns.Select(spawnRef => spawnRef.MonsterId).OrderBy(id => id),
                    Is.EqualTo(authoredComposition), $"seed {seed}");
                Assert.That(spawns.Select(spawnRef => spawnRef.Id).Distinct().Count(), Is.EqualTo(spawns.Count), $"seed {seed}: duplicate spawn id");
                Assert.That(spawns.Select(spawnRef => spawnRef.Coord).Distinct().Count(), Is.EqualTo(spawns.Count), $"seed {seed}: two spawns share a coord");

                foreach (var spawnRef in spawns)
                {
                    // ③ 슬롯 유효성: 통행 가능·이동 차단 없음·순찰 영역 해소.
                    Assert.That(randomized.TryGetCell(spawnRef.Coord, out var cell), Is.True, $"seed {seed}: {spawnRef.Id}");
                    Assert.That(cell.BaseWalkable, Is.True, $"seed {seed}: {spawnRef.Id} on unwalkable cell");
                    Assert.That(randomized.HasMovementBlockingObject(spawnRef.Coord), Is.False, $"seed {seed}: {spawnRef.Id} blocked");
                    if (!string.IsNullOrWhiteSpace(spawnRef.PatrolAreaId))
                    {
                        Assert.That(patrolAreaIds.Contains(spawnRef.PatrolAreaId), Is.True, $"seed {seed}: {spawnRef.Id} patrol missing");
                    }

                    // ① 안전 반경: 랜덤화가 움직인 스폰은 PlayerSpawn 반경 밖이어야 한다.
                    if (taggedIds.Contains(spawnRef.Id))
                    {
                        Assert.That(spawnRef.Coord.DistanceTo(playerSpawn), Is.GreaterThanOrEqualTo(SafeRadius),
                            $"seed {seed}: {spawnRef.Id} inside safe radius");
                    }
                }

                distinctLayouts.Add(string.Join("|", spawns.Select(Describe).OrderBy(text => text)));
            }

            // ④ 폴백 발생률 < 1% (200회 중 1회까지 허용).
            Assert.That(fallbacks, Is.LessThan(2), $"fallback rate {fallbacks}/{SweepSeedCount}");
            Assert.That(distinctLayouts.Count, Is.GreaterThan(SweepSeedCount / 4),
                "배치 다양성이 비정상적으로 낮다 — 셔플이 죽었거나 슬롯이 모자란다.");
        }

        private static string Describe(HexMonsterSpawnRef spawnRef)
        {
            return $"{spawnRef.Id}:{spawnRef.MonsterId}:{spawnRef.SpawnRole}:{spawnRef.Coord}:{spawnRef.PatrolAreaId}";
        }

        // ── P1: CSV 풀 + 위협 예산 (§4) ───────────────────────────────────────────────

        private const string Stage1Id = "stage_001_prototype";

        private static StageRandomizationProfile LoadStage1Profile()
        {
            Assert.That(
                StageRandomizationProfileSource.TryLoadProfile(Stage1Id, out var profile, out var error),
                Is.True, error);
            Assert.That(profile.Enabled, Is.True, "Stage_1 프로파일이 비활성이다.");
            return profile;
        }

        [Test]
        public void AllRandomizationSlotsAreWalkReachable()
        {
            // 도달성 게이트(§8-④): 통행 가능해도 고립 섬이면 몬스터가 영영 닿지 않는다 —
            // 실제로 예비 슬롯 2개가 이 구멍으로 출하됐다(2026-08-18 실측, 교체됨).
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var playerSpawn = source.ObjectRefs.Single(objectRef => objectRef != null && objectRef.IsPlayerSpawn).Coord;
            var walkDistances = HexWalkDistances.FromCoord(baseMap, playerSpawn);

            var unreachable = TaggedSlots(source)
                .Where(slot => !walkDistances.ContainsKey(slot.Coord))
                .Select(slot => slot.ObjectId)
                .ToList();
            Assert.That(unreachable, Is.Empty, "보행 도달 불가 슬롯이 있다.");
        }

        [Test]
        public void ProfileSameSeedProducesTheIdenticalMap()
        {
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var profile = LoadStage1Profile();

            Assert.That(HexMapPlacementRandomization.TryApplyProfile(
                source, baseMap, 424242, profile, out var first, out var evidence1), Is.True, evidence1);
            Assert.That(HexMapPlacementRandomization.TryApplyProfile(
                source, baseMap, 424242, profile, out var second, out var evidence2), Is.True, evidence2);

            Assert.That(
                second.MonsterSpawnRefs.Select(Describe).OrderBy(text => text),
                Is.EqualTo(first.MonsterSpawnRefs.Select(Describe).OrderBy(text => text)));
        }

        [Test]
        public void ProfileSeedSweepHasNoViolationsAndCompositionVaries()
        {
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var profile = LoadStage1Profile();
            var playerSpawn = source.ObjectRefs.Single(objectRef => objectRef != null && objectRef.IsPlayerSpawn).Coord;
            var walkDistances = HexWalkDistances.FromCoord(baseMap, playerSpawn);
            var taggedIds = new HashSet<string>(TaggedSlots(source).Select(slot => slot.ObjectId));
            var poolByRef = profile.Pools.Where(entry => entry.IsMonster)
                .GroupBy(entry => entry.EntryRef)
                .ToDictionary(group => group.Key, group => group.First());
            var capLimits = profile.Pools.Where(entry => entry.IsMonster)
                .GroupBy(entry => entry.CapKey)
                .ToDictionary(group => group.Key, group => group.First().MaxCount);

            var fallbacks = 0;
            var compositions = new HashSet<string>();
            for (var seed = 0; seed < SweepSeedCount; seed++)
            {
                if (!HexMapPlacementRandomization.TryApplyProfile(source, baseMap, seed, profile, out var randomized, out _))
                {
                    fallbacks++;
                    continue;
                }

                var randomizedSpawns = randomized.MonsterSpawnRefs.Where(spawnRef => taggedIds.Contains(spawnRef.Id)).ToList();
                Assert.That(randomizedSpawns.Count, Is.EqualTo(19), $"seed {seed}");

                // #18 elite 최소 보장: capGroup 상한만 있던 풀에서 elite 0인 판이 실측됐다 — 하한 게이트.
                Assert.That(profile.EliteMin, Is.GreaterThanOrEqualTo(2), "Stage_1 elite 하한(#18)이 저작과 다르다.");
                Assert.That(randomizedSpawns.Count(spawnRef => spawnRef.SpawnRole == "elite"),
                    Is.GreaterThanOrEqualTo(profile.EliteMin), $"seed {seed}: elite below minimum");

                // ① 예산: 위협 합이 [min, max] 이내.
                var totalThreat = randomizedSpawns.Sum(spawnRef => poolByRef[spawnRef.MonsterId].ThreatCost);
                Assert.That(totalThreat, Is.InRange(profile.BudgetMin, profile.BudgetMax), $"seed {seed}");

                // ② 타입 상한(합산 키 포함).
                foreach (var capGroup in randomizedSpawns.GroupBy(spawnRef => poolByRef[spawnRef.MonsterId].CapKey))
                {
                    Assert.That(capGroup.Count(), Is.LessThanOrEqualTo(capLimits[capGroup.Key]), $"seed {seed}: cap '{capGroup.Key}'");
                }

                foreach (var spawnRef in randomizedSpawns)
                {
                    // ③ 안전 반경 + ④ 도달성.
                    Assert.That(spawnRef.Coord.DistanceTo(playerSpawn), Is.GreaterThanOrEqualTo(profile.SafeRadius), $"seed {seed}: {spawnRef.Id}");
                    Assert.That(walkDistances.ContainsKey(spawnRef.Coord), Is.True, $"seed {seed}: {spawnRef.Id} unreachable");

                    // ⑤ 지대 제한: 저작한 지대는 <b>하한</b>이다(2026-09-01 #14) — 그 지대부터 뒤로 전부
                    //    설 수 있고, 그 앞에는 절대 못 선다. late 엔트리는 late가 마지막이라 종전과 같다.
                    var entry = poolByRef[spawnRef.MonsterId];
                    if (entry.Zone != PlacementZone.Any)
                    {
                        Assert.That(
                            profile.ZoneForBfsDistance(walkDistances[spawnRef.Coord]),
                            Is.GreaterThanOrEqualTo(entry.Zone),
                            $"seed {seed}: {spawnRef.Id} — 저작 지대({entry.Zone})보다 이른 곳에 섰다.");
                    }

                    Assert.That(spawnRef.SpawnRole, Is.EqualTo(entry.Role), $"seed {seed}: {spawnRef.Id}");
                }

                // ⑥-2 정예 간 최소 거리(2026-09-01 #17). 이 축은 여태 <b>통째로 없었다</b> —
                //     bans의 SpawnMonsters↔elite는 함정 대 정예이고, 밀도 상한 8도 정예 둘(3+3=6)은
                //     반경 2 안에서 통과시켜 나란히 선 정예가 규칙상 정상이었다.
                Assert.That(profile.EliteMinDistance, Is.GreaterThanOrEqualTo(2),
                    "Stage_1 정예 최소 거리(#17)가 저작과 다르다.");
                var eliteSpawns = randomizedSpawns.Where(spawnRef => spawnRef.SpawnRole == "elite").ToList();
                for (var a = 0; a < eliteSpawns.Count; a++)
                {
                    for (var b = a + 1; b < eliteSpawns.Count; b++)
                    {
                        Assert.That(
                            eliteSpawns[a].Coord.DistanceTo(eliteSpawns[b].Coord),
                            Is.GreaterThanOrEqualTo(profile.EliteMinDistance),
                            $"seed {seed}: elites {eliteSpawns[a].Id}·{eliteSpawns[b].Id} too close");
                    }
                }

                // ⑥ 밀도 상한.
                foreach (var center in randomizedSpawns)
                {
                    var nearbyThreat = randomizedSpawns
                        .Where(spawnRef => spawnRef.Coord.DistanceTo(center.Coord) <= profile.DensityRadius)
                        .Sum(spawnRef => poolByRef[spawnRef.MonsterId].ThreatCost);
                    Assert.That(nearbyThreat, Is.LessThanOrEqualTo(profile.DensityCap), $"seed {seed}: around {center.Coord}");
                }

                compositions.Add(string.Join("|", randomizedSpawns.Select(spawnRef => spawnRef.MonsterId).OrderBy(id => id)));
            }

            // ⑦ 폴백 발생률 < 1% + ⑧ 구성 변주(P1의 존재 이유).
            Assert.That(fallbacks, Is.LessThan(2), $"fallback rate {fallbacks}/{SweepSeedCount}");
            Assert.That(compositions.Count, Is.GreaterThan(20), "구성 변주가 비정상적으로 낮다 — 풀 추첨이 죽었다.");
        }

        // ── P2: 함정 풀 + 상자 셔플 + bans (§5) ─────────────────────────────────────

        [Test]
        public void Stage1TrapsAreTaggedForRandomization()
        {
            var source = LoadSource();
            var tagged = source.TrapRefs
                .Where(trapRef => trapRef != null && !string.IsNullOrWhiteSpace(trapRef.RandomizationGroup))
                .ToList();
            var occupied = tagged.Count(trapRef => !trapRef.IsRandomizationSpareSlot);
            var spares = tagged.Count(trapRef => trapRef.IsRandomizationSpareSlot);

            Assert.That(occupied, Is.GreaterThanOrEqualTo(54), "점유 함정 슬롯이 예상보다 적다 — 저작이 유실됐다.");
            // Q-A2(2026-08-19): trapMinDistance는 예비 슬롯 없이는 만족할 수 없다(저작 점유 54칸에 인접 쌍 6 실측).
            Assert.That(spares, Is.GreaterThanOrEqualTo(15), "함정 예비 슬롯이 예상보다 적다 — 최소 거리 규칙이 굶는다.");
            Assert.That(
                tagged.Select(trapRef => trapRef.RandomizationGroup.Trim()).Distinct().OrderBy(group => group),
                Is.EqualTo(new[] { "trap-early", "trap-late", "trap-mid" }),
                "함정 그룹은 BFS 지대 3분할(trap-early/mid/late)이다.");
            // #4 저작 감사: 슬롯 좌표(점유+예비)는 전부 서로 다른 칸이어야 한다 — 한 타일 중복의 1차 방어.
            Assert.That(
                tagged.Select(trapRef => trapRef.Coord).Distinct().Count(), Is.EqualTo(tagged.Count),
                "함정 슬롯 두 개가 같은 칸에 저작됐다.");
        }

        [Test]
        public void AllTrapSlotsAreWalkReachable()
        {
            // 몬스터 슬롯과 같은 도달성 게이트(§6-3) — 통행 가능해도 고립 섬이면 밟을 수 없는
            // 죽은 함정이다. 예비 슬롯이 뽑히기 시작한 지금(Q-A2)부터는 예비 쪽도 같은 계약이다.
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var playerSpawn = source.ObjectRefs.Single(objectRef => objectRef != null && objectRef.IsPlayerSpawn).Coord;
            var walkDistances = HexWalkDistances.FromCoord(baseMap, playerSpawn);

            var unreachable = source.TrapRefs
                .Where(trapRef => trapRef != null && !string.IsNullOrWhiteSpace(trapRef.RandomizationGroup))
                .Where(trapRef => !walkDistances.ContainsKey(trapRef.Coord))
                .Select(trapRef => trapRef.TrapId)
                .ToList();
            Assert.That(unreachable, Is.Empty, "보행 도달 불가 함정 슬롯이 있다.");
        }

        [Test]
        public void TrapPoolEntriesResolveInPresetCatalogAndAvoidBurn()
        {
            // §6-2: 풀 CSV의 프리셋이 카탈로그에 없으면 추첨이 뽑는 순간 지뢰가 되고, Burn은
            // 밟으면 크래시하는 유령 kind다(ShippingMapTrapAuditTests와 같은 계약을 풀에도 건다).
            var profile = LoadStage1Profile();
            var catalog = TrapPresetCatalog.LoadDefault();
            Assert.That(catalog, Is.Not.Null);

            var trapEntries = profile.Pools.Where(entry => entry.IsTrapPreset).ToList();
            Assert.That(trapEntries, Is.Not.Empty, "Stage_1 함정 풀이 비어 있다.");
            foreach (var entry in trapEntries)
            {
                Assert.That(catalog.TryGet(entry.EntryRef, out var preset), Is.True,
                    $"풀 엔트리 '{entry.EntryRef}'가 TrapPresetCatalog에 없다.");
                Assert.That(preset.EffectKind, Is.Not.EqualTo(HexTrapEffectKind.Burn),
                    $"풀 엔트리 '{entry.EntryRef}'가 저작 금지 kind(Burn)다.");
            }
        }

        [Test]
        public void ShippingBansCanActuallyFire()
        {
            // 2026-09-05 #31: VisionDown↔stealth 행이 overlap/0이었는데 blind_smoke가 Q-A1로 radius 0이 되면서
            // 「같은 칸」만 금지했고, 함정 슬롯과 몬스터 슬롯은 좌표를 공유하지 않아 한 번도 발동할 수 없었다.
            // 규칙이 조용히 죽는 것을 막는다: overlap 행은 그 kind의 프리셋 중 반경>0인 것이 풀에 있어야 하고,
            // 어느 관계든 판정 거리는 1 이상이어야 한다(슬롯끼리 최소 거리 1).
            var source = LoadSource();
            var profile = LoadStage1Profile();
            var catalog = TrapPresetCatalog.LoadDefault();
            Assert.That(profile.Bans, Is.Not.Empty, "Stage_1 bans가 비어 있다.");
            var poolPresetIds = new HashSet<string>(profile.Pools.Where(entry => entry.IsTrapPreset).Select(entry => entry.EntryRef));

            var trapCoords = new HashSet<HexCoord>(source.TrapRefs.Where(trap => trap != null && !string.IsNullOrWhiteSpace(trap.RandomizationGroup)).Select(trap => trap.Coord));
            var monsterCoords = TaggedSlots(source).Select(slot => slot.Coord).ToList();
            Assert.That(monsterCoords.Any(trapCoords.Contains), Is.False, "함정 슬롯과 몬스터 슬롯이 좌표를 공유한다 — 이 게이트의 전제가 바뀌었다.");

            foreach (var ban in profile.Bans)
            {
                var reach = ban.Relation == PlacementBanRelation.Adjacent
                    ? Math.Max(1, ban.Radius)
                    : catalog.Presets
                        .Where(preset => preset != null && poolPresetIds.Contains(preset.PresetId)
                            && string.Equals(preset.EffectKind.ToString(), ban.TrapEffectKind, StringComparison.OrdinalIgnoreCase))
                        .Select(preset => preset.Radius)
                        .DefaultIfEmpty(0)
                        .Max();
                Assert.That(reach, Is.GreaterThanOrEqualTo(1),
                    $"ban '{ban.TrapEffectKind}↔{ban.MonsterTag}'({ban.Relation})의 판정 거리가 {reach} — 슬롯은 겹치지 않으므로 이 규칙은 발동할 수 없다.");
            }
        }

        [Test]
        public void AuthoredTrapPresetsStayInsideTheStagePool()
        {
            // 2026-09-05 #34: 저작에 남은 teleport_pad·siren_alarm은 풀에서 빠져 폴백·랜덤화 off 판에서만 나왔다.
            // 저작 원본과 랜덤화 판의 함정 어휘를 같게 유지한다(프리셋 저작 함정만 — 인라인 함정은 별개 문법).
            var source = LoadSource();
            var profile = LoadStage1Profile();
            var poolByGroup = profile.Pools.Where(entry => entry.IsTrapPreset)
                .GroupBy(entry => entry.Group)
                .ToDictionary(group => group.Key, group => new HashSet<string>(group.Select(entry => entry.EntryRef)));
            foreach (var trap in source.TrapRefs.Where(trap => trap != null
                         && !string.IsNullOrWhiteSpace(trap.RandomizationGroup) && !string.IsNullOrWhiteSpace(trap.PresetId)))
            {
                Assert.That(poolByGroup.TryGetValue(trap.RandomizationGroup.Trim(), out var pool) && pool.Contains(trap.PresetId), Is.True,
                    $"저작 함정 '{trap.TrapId}'의 프리셋 '{trap.PresetId}'가 그룹 '{trap.RandomizationGroup}' 풀에 없다 — 폴백 판에서만 나오는 함정이다.");
            }
        }

        [Test]
        public void ProfileSameSeedProducesIdenticalTrapsAndChests()
        {
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var profile = LoadStage1Profile();

            Assert.That(HexMapPlacementRandomization.TryApplyProfile(
                source, baseMap, 424242, profile, out var first, out var evidence1), Is.True, evidence1);
            Assert.That(HexMapPlacementRandomization.TryApplyProfile(
                source, baseMap, 424242, profile, out var second, out var evidence2), Is.True, evidence2);

            Assert.That(
                second.TrapRefs.Select(DescribeTrap).OrderBy(text => text),
                Is.EqualTo(first.TrapRefs.Select(DescribeTrap).OrderBy(text => text)));
            Assert.That(
                second.ObjectRefs.Where(IsChest).Select(chest => $"{chest.ObjectId}:{chest.Coord}").OrderBy(text => text),
                Is.EqualTo(first.ObjectRefs.Where(IsChest).Select(chest => $"{chest.ObjectId}:{chest.Coord}").OrderBy(text => text)));
        }

        [Test]
        public void MultiCellMonstersNeverStandWithTheirBodyOffWalkableGround()
        {
            // 🔴 2026-09-05 실플레이 피드백: "1칸 초과 footprint 몬스터가 물 타일에 1~2칸 걸쳐서 걸어다닌다".
            // 이동 판정(HexPathfinder.CanEnter)은 원래부터 몸통 전 칸을 봤다 — 새는 곳은 <b>배치</b>였다.
            // 슬롯 유효성이 「그 한 칸이 통행 가능한가」로만 검사돼, 3칸 몸이 물가 슬롯을 뽑으면
            // 몸통 한두 칸이 물 위에 얹힌 채로 판이 시작됐다(수정 전 200시드 실측 17%).
            //
            // 이 테스트는 <b>저작 원본과 추첨 결과 양쪽</b>을 잰다: 랜덤화가 꺼진 판(구세이브 재개·폴백)은
            // 저작 원본을 그대로 쓰므로 한쪽만 막으면 다른 쪽으로 샌다.
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var profile = LoadStage1Profile();
            var bodyOffsets = BuildMultiCellBodyOffsets();
            Assert.That(bodyOffsets, Is.Not.Empty, "출하 카탈로그에 다칸 몬스터가 없다 — 이 계약이 무의미해졌는지 확인할 것.");

            AssertEveryBodyFits(baseMap, baseMap.MonsterSpawnRefs, bodyOffsets, "저작 원본");

            for (var seed = 0; seed < SweepSeedCount; seed++)
            {
                if (!HexMapPlacementRandomization.TryApplyProfile(
                        source, baseMap, seed, profile, out var randomized, out _, null, bodyOffsets))
                {
                    continue;
                }

                AssertEveryBodyFits(baseMap, randomized.MonsterSpawnRefs, bodyOffsets, $"seed {seed}");
            }
        }

        private static void AssertEveryBodyFits(
            HexMapData map,
            IEnumerable<HexMonsterSpawnRef> spawns,
            IReadOnlyDictionary<string, IReadOnlyList<HexCoord>> bodyOffsets,
            string context)
        {
            foreach (var spawn in spawns)
            {
                if (!bodyOffsets.TryGetValue(spawn.MonsterId, out var offsets))
                {
                    continue;
                }

                foreach (var offset in offsets)
                {
                    var coord = spawn.Coord + offset;
                    Assert.That(
                        HexAccessibility.IsAccessible(map, coord, HexTerrainTraits.Default)
                        && !map.HasMovementBlockingObject(coord),
                        Is.True,
                        $"{context}: {spawn.MonsterId} @{spawn.Coord}의 몸통 칸 {coord}가 통행 불가다 — 몸이 걸쳐 선다.");
                }
            }
        }

        /// <summary>출하 몬스터 카탈로그에서 몸이 두 칸 이상인 것만 추린다.</summary>
        private static Dictionary<string, IReadOnlyList<HexCoord>> BuildMultiCellBodyOffsets()
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory);
            var byId = new Dictionary<string, IReadOnlyList<HexCoord>>(StringComparer.Ordinal);
            foreach (var entry in bundle.MonsterCatalog.Entries)
            {
                var offsets = MonsterFootprints.OffsetsOf(entry.FootprintShape);
                if (offsets.Count > 1 && !byId.ContainsKey(entry.Id))
                {
                    byId.Add(entry.Id, offsets);
                }
            }

            return byId;
        }

        [Test]
        public void ProfileTrapSweepHasNoViolations()
        {
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var profile = LoadStage1Profile();
            Assert.That(profile.TrapMinDistance, Is.GreaterThanOrEqualTo(2),
                "Stage_1 함정 최소 거리(Q-A2)가 저작과 다르다 — 인접 함정 금지가 꺼졌다.");
            // Q-A2부터 함정은 슬롯 승격형이다: trapId는 태깅된 슬롯(점유+예비) 중에서 나오고 좌표는
            // 그 슬롯의 저작 좌표를 유지한다. 개수(그룹별 점유 수)는 불변 — ConsumedTrapIds·
            // RevealedTrapCoords는 시드 결정성(같은 시드 = 같은 슬롯 선택)이 지킨다(§5).
            var taggedSlots = source.TrapRefs
                .Where(trapRef => trapRef != null && !string.IsNullOrWhiteSpace(trapRef.RandomizationGroup))
                .ToDictionary(trapRef => trapRef.TrapId, trapRef => trapRef.Coord);
            var occupiedCount = source.TrapRefs.Count(trapRef =>
                trapRef != null && !string.IsNullOrWhiteSpace(trapRef.RandomizationGroup) && !trapRef.IsRandomizationSpareSlot);
            var poolPresets = profile.Pools.Where(entry => entry.IsTrapPreset)
                .GroupBy(entry => entry.EntryRef)
                .ToDictionary(group => group.Key, group => group.First());

            var fallbacks = 0;
            var compositions = new HashSet<string>();
            var distinctLayouts = new HashSet<string>();
            for (var seed = 0; seed < SweepSeedCount; seed++)
            {
                if (!HexMapPlacementRandomization.TryApplyProfile(source, baseMap, seed, profile, out var randomized, out _))
                {
                    fallbacks++;
                    continue;
                }

                var traps = randomized.TrapRefs;
                // ⑦ 개수 불변 + 슬롯 착지: trapId·좌표는 태깅된 슬롯의 저작값 그대로다.
                Assert.That(traps.Count, Is.EqualTo(occupiedCount), $"seed {seed}");
                foreach (var trap in traps)
                {
                    Assert.That(taggedSlots.TryGetValue(trap.TrapId, out var authoredCoord), Is.True,
                        $"seed {seed}: unknown trapId '{trap.TrapId}'");
                    Assert.That(trap.Coord, Is.EqualTo(authoredCoord), $"seed {seed}: trap '{trap.TrapId}' moved off its slot");
                    // 추첨된 함정은 풀의 프리셋만 나온다.
                    Assert.That(poolPresets.ContainsKey(trap.PresetId), Is.True,
                        $"seed {seed}: trap '{trap.TrapId}' preset '{trap.PresetId}' not in pool");
                }

                // #4: 한 타일 중복 없음(좌표 Distinct) — 슬롯 좌표가 전부 달라 원리상 불가지만 못 박는다.
                Assert.That(traps.Select(trap => trap.Coord).Distinct().Count(), Is.EqualTo(traps.Count),
                    $"seed {seed}: two traps share a coord");

                // ⑧ 함정 간 최소 거리(Q-A2): 전 그룹에 걸쳐 쌍별 거리 >= trapMinDistance.
                var trapList = traps.ToList();
                for (var i = 0; i < trapList.Count; i++)
                {
                    for (var j = i + 1; j < trapList.Count; j++)
                    {
                        Assert.That(trapList[i].Coord.DistanceTo(trapList[j].Coord),
                            Is.GreaterThanOrEqualTo(profile.TrapMinDistance),
                            $"seed {seed}: traps '{trapList[i].TrapId}'/'{trapList[j].TrapId}' too close");
                    }
                }

                // ① 함정 예산: 몬스터 예산과 별도 창.
                var trapThreat = traps.Sum(trap => poolPresets[trap.PresetId].ThreatCost);
                Assert.That(trapThreat, Is.InRange(profile.TrapBudgetMin, profile.TrapBudgetMax), $"seed {seed}");

                // ③ bans: SpawnMonsters 함정 ↔ elite 인접 금지(Q11 활성 규칙).
                var eliteCoords = randomized.MonsterSpawnRefs
                    .Where(spawnRef => spawnRef.SpawnRole == "elite")
                    .Select(spawnRef => spawnRef.Coord)
                    .ToList();
                foreach (var trap in traps.Where(trap =>
                    trap.Effects.Any(effect => effect.Kind == HexTrapEffectKind.SpawnMonsters)))
                {
                    foreach (var eliteCoord in eliteCoords)
                    {
                        Assert.That(trap.Coord.DistanceTo(eliteCoord), Is.GreaterThan(1),
                            $"seed {seed}: SpawnMonsters trap '{trap.TrapId}' adjacent to elite at {eliteCoord}");
                    }
                }

                compositions.Add(string.Join("|", traps.Select(trap => trap.PresetId).OrderBy(id => id)));
                distinctLayouts.Add(string.Join("|", traps.Select(trap => trap.TrapId).OrderBy(id => id)));
            }

            // ⑤ 폴백 < 1% + 구성 변주 + 위치 변주(Q-A2 예비 슬롯의 존재 이유).
            Assert.That(fallbacks, Is.LessThan(2), $"fallback rate {fallbacks}/{SweepSeedCount}");
            Assert.That(compositions.Count, Is.GreaterThan(20), "함정 구성 변주가 비정상적으로 낮다 — 풀 추첨이 죽었다.");
            Assert.That(distinctLayouts.Count, Is.GreaterThan(20), "함정 위치 변주가 비정상적으로 낮다 — 예비 슬롯이 죽었다.");
        }

        [Test]
        public void ProfileChestSweepHasNoViolations()
        {
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var profile = LoadStage1Profile();
            Assert.That(profile.ChestMinDistance, Is.GreaterThanOrEqualTo(0), "Stage_1 상자 셔플이 비활성이다.");
            var playerSpawn = source.ObjectRefs.Single(objectRef => objectRef != null && objectRef.IsPlayerSpawn).Coord;
            var walkDistances = HexWalkDistances.FromCoord(baseMap, playerSpawn);
            var authoredChests = baseMap.ObjectRefs.Where(IsChest).ToDictionary(chest => chest.ObjectId);
            Assert.That(authoredChests.Count, Is.GreaterThanOrEqualTo(24), "저작 상자가 예상보다 적다.");

            var fallbacks = 0;
            var distinctLayouts = new HashSet<string>();
            for (var seed = 0; seed < SweepSeedCount; seed++)
            {
                if (!HexMapPlacementRandomization.TryApplyProfile(source, baseMap, seed, profile, out var randomized, out _))
                {
                    fallbacks++;
                    continue;
                }

                var chests = randomized.ObjectRefs.Where(IsChest).ToList();
                var trapCoords = new HashSet<HexCoord>(randomized.TrapRefs.Select(trap => trap.Coord));
                var monsterCoords = new HashSet<HexCoord>(randomized.MonsterSpawnRefs.Select(spawnRef => spawnRef.Coord));
                var serviceCoords = randomized.ObjectRefs.Where(IsServiceObject).Select(objectRef => objectRef.Coord).ToList();

                // ⑦ 개수·정체 불변: objectId·objectRef가 유지된다(보상 총량 보존 §1-4 + ClaimedEventObjectIds 정합).
                Assert.That(chests.Count, Is.EqualTo(authoredChests.Count), $"seed {seed}");
                foreach (var chest in chests)
                {
                    Assert.That(authoredChests.TryGetValue(chest.ObjectId, out var authored), Is.True, $"seed {seed}: {chest.ObjectId}");
                    Assert.That(chest.ObjectRef, Is.EqualTo(authored.ObjectRef), $"seed {seed}: {chest.ObjectId} objectRef changed");

                    // ④ 후보 칸 제약(§6-6): 통행+도달 · 특수 셀 제외 · 함정/몬스터 칸 제외.
                    Assert.That(randomized.TryGetCell(chest.Coord, out var cell), Is.True, $"seed {seed}: {chest.ObjectId}");
                    Assert.That(cell.BaseWalkable, Is.True, $"seed {seed}: {chest.ObjectId} on unwalkable cell");
                    Assert.That(walkDistances.ContainsKey(chest.Coord), Is.True, $"seed {seed}: {chest.ObjectId} unreachable");
                    Assert.That(string.IsNullOrEmpty(cell.EventId), Is.True, $"seed {seed}: {chest.ObjectId} on event cell");
                    Assert.That(string.IsNullOrEmpty(cell.LandmarkId), Is.True, $"seed {seed}: {chest.ObjectId} on landmark cell");
                    Assert.That(trapCoords.Contains(chest.Coord), Is.False, $"seed {seed}: {chest.ObjectId} on a trap");
                    Assert.That(monsterCoords.Contains(chest.Coord), Is.False, $"seed {seed}: {chest.ObjectId} on a monster spawn");

                    // ⑧ 서비스 오브젝트(잡화점/캠핑카/공작소) 반경 2 금지(배치 인계문 Q4).
                    foreach (var serviceCoord in serviceCoords)
                    {
                        Assert.That(chest.Coord.DistanceTo(serviceCoord), Is.GreaterThan(2),
                            $"seed {seed}: {chest.ObjectId} within radius 2 of service object at {serviceCoord}");
                    }
                }

                // ② 상자 간 최소 거리(Q8 확정 4).
                for (var i = 0; i < chests.Count; i++)
                {
                    for (var j = i + 1; j < chests.Count; j++)
                    {
                        Assert.That(chests[i].Coord.DistanceTo(chests[j].Coord),
                            Is.GreaterThanOrEqualTo(profile.ChestMinDistance),
                            $"seed {seed}: chests '{chests[i].ObjectId}'/'{chests[j].ObjectId}' too close");
                    }
                }

                distinctLayouts.Add(string.Join("|", chests.Select(chest => $"{chest.ObjectId}:{chest.Coord}").OrderBy(text => text)));
            }

            // ⑤ 폴백 < 1% + 배치 다양성.
            Assert.That(fallbacks, Is.LessThan(2), $"fallback rate {fallbacks}/{SweepSeedCount}");
            Assert.That(distinctLayouts.Count, Is.GreaterThan(SweepSeedCount / 4),
                "상자 배치 다양성이 비정상적으로 낮다 — 셔플이 죽었다.");
        }

        [Test]
        public void EveryRewardGachaUsesTheSameModel()
        {
            // 🔴 「위장」(보상뽑기 하나에 지하철 기둥 모델을 물린 것)은 사용자 판정으로 폐지됐다
            //    (2026-09-01 #4). 좌표는 셔플되므로 「그 자리」가 아니라 <b>그 objectId</b>가 위장이었고,
            //    한 개만 다르면 화면에서는 그냥 결함으로 읽힌다. 저작이 다시 갈라지지 못하게 잠근다.
            var source = LoadSource();
            var baseMap = BuildBaseMap(source);
            var chests = baseMap.ObjectRefs.Where(IsChest).ToList();
            Assert.That(chests, Is.Not.Empty, "전제: 출하 맵에 보상뽑기가 있어야 한다.");

            var models = chests
                .Select(chest => chest.ObjectRef)
                .Distinct(System.StringComparer.Ordinal)
                .ToList();
            Assert.That(models, Has.Count.EqualTo(1),
                $"보상뽑기 모델이 갈렸다: [{string.Join(", ", models)}] — 위장은 폐지됐다.");

            var scales = chests
                .Select(chest => $"{chest.VisualScaleX}|{chest.VisualScaleY}|{chest.VisualScaleZ}")
                .Distinct(System.StringComparer.Ordinal)
                .ToList();
            Assert.That(scales, Has.Count.EqualTo(1),
                $"보상뽑기 크기가 갈렸다: [{string.Join(", ", scales)}] — 모델이 같아도 크기가 다르면 여전히 하나만 튄다.");
        }

        /// <summary>
        /// 「초반에는 약한 몬스터」 계약(2026-09-01 #14). 두 가지를 잰다:
        ///  ① 진입 밴드의 기대 위협이 <b>단조 증가</b>한다(mon-a ≤ mon-b ≤ mon-c).
        ///  ② 지대 하한이 걸린 엔트리는 그보다 이른 지대의 풀에서 <b>뽑힐 수조차 없다</b>.
        ///
        /// <para>🔴 이 게이트가 없어서 결함이 안 보였다: mon-b 풀에 가장 싼 M001이 <b>아예 없어</b>
        /// 두 번째 밴드가 세 번째보다 무거웠는데(1.91 &gt; 1.68), 아무도 재지 않으니 아무도 몰랐다.
        /// 재는 것은 배치 결과가 아니라 <b>풀 자체</b>다 — 시드마다 흔들리지 않는 값이라야 게이트가 된다.</para>
        /// </summary>
        [Test]
        public void EntryBandsGetMonotonicallyHarder()
        {
            var profile = LoadStage1Profile();

            double ExpectedThreat(string group, PlacementZone zone)
            {
                var eligible = profile.Pools
                    .Where(entry => entry.IsMonster
                        && entry.Group == group
                        && entry.Weight > 0
                        && (entry.Zone == PlacementZone.Any || zone >= entry.Zone))
                    .ToList();
                Assert.That(eligible, Is.Not.Empty, $"{group}/{zone}: 뽑을 수 있는 엔트리가 없다.");
                var weight = eligible.Sum(entry => entry.Weight);
                return eligible.Sum(entry => (double)entry.Weight * entry.ThreatCost) / weight;
            }

            // 진입 3밴드의 실제 지대: mon-a(BFS 6~13)·mon-b(15~21)는 Early, mon-c(21~39)는 Mid.
            var a = ExpectedThreat("mon-a", PlacementZone.Early);
            var b = ExpectedThreat("mon-b", PlacementZone.Early);
            var c = ExpectedThreat("mon-c", PlacementZone.Mid);

            Assert.That(a, Is.LessThanOrEqualTo(b),
                $"진입부(mon-a {a:F2})가 그 다음 밴드(mon-b {b:F2})보다 무겁다.");
            Assert.That(b, Is.LessThanOrEqualTo(c),
                $"두 번째 밴드(mon-b {b:F2})가 세 번째(mon-c {c:F2})보다 무겁다 — 계단이 거꾸로다.");
        }

        [Test]
        public void CurseAndStealthAxesNeverAppearInTheEntryZone()
        {
            // 사용자 확정(#14): 「초반에는 약한 몬스터」. 저주(M008)는 <b>영구</b> 오염이고 은신(M009)은
            // 정찰이라는 대응 수단을 아는 뒤라야 성립한다 — 둘 다 진입부에서 만나면 만회가 안 된다.
            var profile = LoadStage1Profile();
            foreach (var axis in new[] { "M008", "M009" })
            {
                var rows = profile.Pools.Where(entry => entry.IsMonster && entry.EntryRef == axis).ToList();
                Assert.That(rows, Is.Not.Empty, $"{axis} 풀 행이 사라졌다.");
                foreach (var row in rows)
                {
                    Assert.That(row.Zone, Is.GreaterThanOrEqualTo(PlacementZone.Mid),
                        $"{axis}({row.Group})가 진입 지대에 설 수 있다 — 지대 하한이 풀린다.");
                }
            }
        }

        private static bool IsChest(HexMapObjectData objectRef)
        {
            return objectRef.ObjectType == "TreasureChest";
        }

        private static bool IsServiceObject(HexMapObjectData objectRef)
        {
            return objectRef.ObjectType == "Shop" ||
                   objectRef.ObjectType == "CamperVan" ||
                   objectRef.ObjectType == "Workshop";
        }

        private static string DescribeTrap(HexTrapData trap)
        {
            return $"{trap.TrapId}:{trap.PresetId}:{trap.Coord}:{trap.Radius}:{trap.Effects.Count}";
        }
    }
}
#endif

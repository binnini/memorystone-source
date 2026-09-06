# VERIFIED FACT SHEET — 배치 랜덤화
Repo: /Users/yebin/game/memorystone-source (read-only)

## G1 — HexSparseMapAuthoringSource: occupied vs reserve slot tags

Occupied/reserve status is carried by DTOs referenced by the authoring source, not by
HexSparseMapAuthoringSource itself.

Monster/service slot (Source/Map/Unity/HexMapAuthoringRefs.cs):
- field: `Source/Map/Unity/HexMapAuthoringRefs.cs:142`
  `[SerializeField] private string randomizationGroup = "";`
- accessor: `Source/Map/Unity/HexMapAuthoringRefs.cs:316`
  `public string RandomizationGroup => randomizationGroup ?? string.Empty;`
- reserve/spare-slot predicate: `Source/Map/Unity/HexMapAuthoringRefs.cs:328-331`
```
public bool IsRandomizationSpareSlot =>
    (IsMonsterSpawn || IsServiceObject) &&
    !string.IsNullOrWhiteSpace(RandomizationGroup) &&
    string.IsNullOrWhiteSpace(objectRef);
```
  (comment above, lines 317-326: reserve = group tag present but no objectRef/prefab id;
  authored/lookdev/preview/off/fallback paths never spawn it; only the randomization bridge
  reads it as a position candidate. Covers both monster spawn and service objects — service
  spares are authored uniformly as HexMapObjectType.Shop since the draw decides the kind.)

Trap slot (Source/Map/Unity/HexTrapAuthoringRefs.cs):
- field: `Source/Map/Unity/HexTrapAuthoringRefs.cs:63`
  `[SerializeField] private string randomizationGroup = "";`
- accessor: `Source/Map/Unity/HexTrapAuthoringRefs.cs:137`
  `public string RandomizationGroup => randomizationGroup ?? string.Empty;`
- reserve/spare-slot predicate: `Source/Map/Unity/HexTrapAuthoringRefs.cs:123-126`
```
public bool IsRandomizationSpareSlot =>
    !string.IsNullOrWhiteSpace(RandomizationGroup) &&
    string.IsNullOrWhiteSpace(PresetId) &&
    Effects.Count == 0;
```

TryToHexMapData signature: `Source/Map/Unity/HexSparseMapAuthoringSource.cs:522`
```
public bool TryToHexMapData(out HexMapData map, out string error)
```

## G2 — Source/Map/Unity/HexMapPlacementRandomization.cs

TryApplyProfile signature: `Source/Map/Unity/HexMapPlacementRandomization.cs:134-142`
```
public static bool TryApplyProfile(
    HexSparseMapAuthoringSource source,
    HexMapData baseMap,
    int seed,
    StageRandomizationProfile profile,
    out HexMapData randomizedMap,
    out string evidence,
    IReadOnlyCollection<string> stealthMonsterIds = null,
    IReadOnlyDictionary<string, IReadOnlyList<HexCoord>> monsterBodyOffsets = null)
```

CSV files loaded, all defined in Source/Map/Unity/StageRandomizationProfileSource.cs:
- Resources id `stage_randomization` (line 16); asset path
  `Assets/Data/Map/Randomization/Resources/stage_randomization.csv` (line 19) — stage header row.
- Resources id `stage_randomization_pools` (line 17); asset path
  `Assets/Data/Map/Randomization/Resources/stage_randomization_pools.csv` (line 20) — weighted pool rows.
- Resources id `stage_randomization_bans` (line 18); asset path
  `Assets/Data/Map/Randomization/Resources/stage_randomization_bans.csv` (line 21) — optional P2
  ban-combo table; TryBuildProfile allows null (loader comment lines 40-42).
Loader logic: `Resources.Load<TextAsset>` first, `AssetDatabase.LoadAssetAtPath` editor fallback
(`Source/Map/Unity/StageRandomizationProfileSource.cs:45-59`).

Four stages, executed in this order inside TryApplyProfile
(Source/Map/Unity/HexMapPlacementRandomization.cs):
1. Monsters — `PlacementRandomizer.RandomizeWithProfile(...)` call at line 158-159
   (comment "P1" at line 126).
2. Traps — `PlacementRandomizer.RandomizeTraps(...)` call at line 183-185, gated by
   `if (profile.TrapThreatBudget > 0)` at line 172 (comment "P2" at line 166 — runs after
   monster placement is finalized because bans/Q11 diff monster coords/tags).
3. Services — `TryPlaceServices(...)` call at line 212, gated by
   `if (profile.ServiceShopCount > 0 || profile.ServiceCamperCount > 0)` at line 210
   (comment "P3" at line 204 — must run BEFORE chests, explicit warning lines 204-207).
4. Chests — `PlacementRandomizer.ShuffleChests(...)` call at line 232-234, gated by
   `if (profile.ChestMinDistance >= 0)` at line 222 (comment "P2" again at line 219).
NOTE: there is no method quartet literally named
`PlacementRandomizer.Monsters/TrapsAndChests/Services` — the four phases are implemented via
`PlacementRandomizer.RandomizeWithProfile`, `PlacementRandomizer.RandomizeTraps`,
`HexMapPlacementRandomization.TryPlaceServices` (which calls `PlacementRandomizer.PlaceServices`),
and `PlacementRandomizer.ShuffleChests`.

ValidateProfileAttempt checks (Source/Map/Runtime/PlacementRandomizer.cs:367-458):
- Safe radius: lines 375-379 — `pick.Slot.Coord.DistanceTo(playerSpawnCoord) < profile.SafeRadius`
- Threat budget sum: lines 382-387 — totalThreat vs [profile.BudgetMin, profile.BudgetMax]
- Elite minimum: lines 391-400 — profile.EliteMin
- Elite min distance: lines 405-425 — profile.EliteMinDistance
- Monster min kinds (species diversity floor): lines 430-441 — profile.MonsterMinKinds
- Density cap: lines 444-454 — profile.DensityRadius / profile.DensityCap

Retry cap: NO hardcoded "40" exists anywhere in this randomization code path.
- P0 default: `PlacementRandomizationConfig.Default` → `rerollLimit: 20`
  (Source/Map/Runtime/PlacementRandomizer.cs:81-83).
- P1/P2/P3 use `profile.RerollLimit`, read per-stage from the `rerollLimit` column of
  stage_randomization.csv (Source/Map/Runtime/StageRandomizationProfile.cs:356), floored by
  `Math.Max(1, rerollLimit)` in the constructor (line 64). Not a fixed literal anywhere.

Fallback-to-authored-map quotes (Source/Combat/Unity/MapCombatController.MapView.cs):
- Line 91:
  `Debug.Log($"Placement profile for stage '{placementStageId}' is disabled; using the authored layout.", this);`
- Line 107:
  `Debug.LogWarning($"Placement randomization fell back to the authored layout: {profileEvidence}", this);`
- Line 113:
  `Debug.Log($"Placement profile unavailable for stage '{placementStageId}' ({profileError}); using P0 slot shuffle.", this);`
- Line 120:
  `Debug.LogWarning($"Placement randomization fell back to the authored layout: {evidence}", this);`

## G3 — Source/Map/Runtime/PlacementRandomizer*.cs

Files (all `public static partial class PlacementRandomizer`):
- Source/Map/Runtime/PlacementRandomizer.cs (589 lines; class declared line 112) — P0 `Randomize`,
  P1 `RandomizeWithProfile`, `ValidateProfileAttempt`, `WeightedPickWithRepeatDecay`,
  `DecayedWeight`, `WeightedPick`, `Validate`, `SampleWithoutReplacement`, `Shuffled`.
- Source/Map/Runtime/PlacementRandomizerTrapsAndChests.cs (497 lines; class declared line 127) —
  `DeriveSeed`, `RandomizeTraps`, `ShuffleChests`.
- Source/Map/Runtime/PlacementRandomizerServices.cs (271 lines; class declared line 77) —
  `PlaceServices`.

WeightedPickWithRepeatDecay full body, verbatim
(Source/Map/Runtime/PlacementRandomizer.cs:482-506):
```
482	        private static StageRandomizationPoolEntry WeightedPickWithRepeatDecay(
483	            IReadOnlyList<StageRandomizationPoolEntry> entries,
484	            Random rng,
485	            IReadOnlyDictionary<string, int> alreadyPicked)
486	        {
487	            var weights = new int[entries.Count];
488	            var total = 0;
489	            for (var index = 0; index < entries.Count; index++)
490	            {
491	                weights[index] = DecayedWeight(entries[index], alreadyPicked);
492	                total += weights[index];
493	            }
494	
495	            var roll = rng.Next(total);
496	            for (var index = 0; index < entries.Count; index++)
497	            {
498	                roll -= weights[index];
499	                if (roll < 0)
500	                {
501	                    return entries[index];
502	                }
503	            }
504	
505	            return entries[entries.Count - 1];
506	        }
```
Companion helper immediately after (Source/Map/Runtime/PlacementRandomizer.cs:512-522):
```
512	        private static int DecayedWeight(
513	            StageRandomizationPoolEntry entry, IReadOnlyDictionary<string, int> alreadyPicked)
514	        {
515	            if (!alreadyPicked.TryGetValue(entry.CapKey, out var picked) || picked <= 0)
516	            {
517	                return entry.Weight;
518	            }
519	
520	            // 시프트 폭이 int 폭을 넘으면 결과가 정의되지 않는다 — 그 전에 바닥으로 눕힌다.
521	            return picked >= 31 ? RepeatDecayFloorWeight : Math.Max(RepeatDecayFloorWeight, entry.Weight >> picked);
522	        }
```
(halves weight per prior pick of the same CapKey; never drops below RepeatDecayFloorWeight;
overflow-guarded at picked >= 31.)

Group-wise without-replacement draw method: `SampleWithoutReplacement`
(Source/Map/Runtime/PlacementRandomizer.cs:560-575):
```
560	        private static List<PlacementSlotCandidate> SampleWithoutReplacement(
561	            IReadOnlyList<PlacementSlotCandidate> candidates,
562	            int count,
563	            Random rng)
564	        {
565	            var pool = candidates.ToList();
566	            var picked = new List<PlacementSlotCandidate>(count);
567	            for (var i = 0; i < count; i++)
568	            {
569	                var index = rng.Next(pool.Count);
570	                picked.Add(pool[index]);
571	                pool.RemoveAt(index);
572	            }
573	
574	            return picked;
575	        }
```
Call sites: P0 group draw at line 197; P1 per-group draw at line 313
(`var selectedSlots = SampleWithoutReplacement(group.Candidates, group.SpawnCount, rng);`).

## G4 — Source/Map/Runtime/StageRandomizationProfile.cs fields

Public properties (Source/Map/Runtime/StageRandomizationProfile.cs:85-165), all set via the
constructor at lines 33-83:
- StageId (string) — line 85
- Enabled (bool) — line 86
- ThreatBudget (int) — line 87
- BudgetTolerancePct (int) — line 88
- SafeRadius (int) — line 89
- RerollLimit (int) — line 90
- DensityRadius (int) — line 91
- DensityCap (int) — line 92
- ZoneEarlyMaxBfs (int) — line 93
- ZoneMidMaxBfs (int) — line 94
- Pools (IReadOnlyList<StageRandomizationPoolEntry>) — line 95
- MonsterMinKinds (int) — line 102
- EliteHpPercent (int) — line 109
- EliteDamagePercent (int) — line 112
- TrapThreatBudget (int) — line 115
- ChestMinDistance (int) — line 118
- ServiceShopCount (int) — line 124
- ServiceCamperCount (int) — line 127
- ServiceMinDistance (int) — line 133
- ServiceRouteMin (int) — line 139
- Bans (IReadOnlyList<PlacementBanRule>) — line 142
- TrapMinDistance (int) — line 149
- EliteMin (int) — line 155
- EliteMinDistance (int) — line 165
- Derived/computed (not ctor params): BudgetMin/BudgetMax — lines 167-168;
  TrapBudgetMin/TrapBudgetMax — lines 171-172
- WithServiceCounts(int shopCount, int camperCount) clone method — lines 183-192
- ZoneForBfsDistance(int bfsDistance) — lines 194-199

## G5 — Seed streams (RunSeedStreams) feeding each stage

Definitions (Source/Map/Runtime/RunSeedStreams.cs:21-42):
```
21	        public const int MonsterPlacement = 0;
24	        public const int Traps = 1;
26	        public const int Chests = 2;
28	        public const int Services = 3;
30	        public const int SpawnHpVariance = 3;
```
(SpawnHpVariance shares number 3 with Services but is re-mixed via spawnRefId in
CombatState.MixSpawnHpSeed so the sequences don't collide — combat-side, not part of this
diagram's map-layer streams.)

Usage inside Source/Map/Unity/HexMapPlacementRandomization.cs:
- Monsters (stream 0): NOT derived — `PlacementRandomizer.RandomizeWithProfile(..., seed, ...)`
  at line 158-159 passes the raw run seed directly. No DeriveSeed call site for monsters
  (RunSeedStreams.cs:14-17 comment: monster placement uses the raw seed for P0~P1 compatibility).
- Traps (stream 1): line 185 —
  `PlacementRandomizer.RandomizeTraps(trapSlots, profile, walkDistances, presetTraits, monsterPoints, PlacementRandomizer.DeriveSeed(seed, RunSeedStreams.Traps));`
- Chests (stream 2): line 234 —
  `PlacementRandomizer.ShuffleChests(chestIds, candidates, profile.ChestMinDistance, profile.RerollLimit, PlacementRandomizer.DeriveSeed(seed, RunSeedStreams.Chests));`
- Services (stream 3): line 730 —
  `PlacementRandomizer.PlaceServices(slots, requests, routeIds, profile.ServiceRouteMin, profile.RerollLimit, PlacementRandomizer.DeriveSeed(seed, RunSeedStreams.Services));`
  (comment lines 726-727: "스트림 3 — 몬스터(원시 시드)·함정(1)·상자(2)를 건드리지 않는다.")

DeriveSeed implementation (Source/Map/Runtime/PlacementRandomizerTrapsAndChests.cs:134-144):
```
134	        public static int DeriveSeed(int seed, int streamIndex)
135	        {
136	            unchecked
137	            {
138	                var mixed = (uint)seed * 2654435761u + (uint)streamIndex * 40503u + 0x9E3779B9u;
139	                mixed ^= mixed >> 16;
140	                mixed *= 2246822519u;
141	                mixed ^= mixed >> 13;
142	                return (int)mixed;
143	            }
144	        }
```

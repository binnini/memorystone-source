# Verified Fact Sheet — 세이브와 시드 (Save & Seed)
Repo: /Users/yebin/game/memorystone-source

## F1. Source/Map/Runtime/RunSeedStreams.cs — constant table + Derive

File: /Users/yebin/game/memorystone-source/Source/Map/Runtime/RunSeedStreams.cs, class body lines 19-50.

```
21  /// <summary>몬스터 슬롯 셔플·풀 추첨. 파생 없이 원시 시드(번호는 문서용).</summary>
22  public const int MonsterPlacement = 0;
23  /// <summary>함정 슬롯 승격·프리셋 치환.</summary>
24  public const int Traps = 1;
25  /// <summary>상자 좌표 셔플.</summary>
26  public const int Chests = 2;
27  /// <summary>서비스 오브젝트(잡화점·캠핑카) 배치.</summary>
28  public const int Services = 3;
29  /// <summary>스폰 시 체력 변주(hpVariancePct). 3을 공유하되 spawnRefId로 재혼합.</summary>
30  public const int SpawnHpVariance = 3;
31  /// <summary>몬스터 공격 패턴 선택 + 피해 변주(<c>MonsterAiPlanner</c>).</summary>
32  public const int MonsterAttackPattern = 4;
33  /// <summary>전투 판정(<c>CombatState.pushRng</c>): 취약 부위·빠른 거북·순간이동지·전염 대상·되돌릴 부적·제거될 카드.</summary>
34  public const int CombatJudgement = 5;
35  /// <summary>보스 기물 볼리의 링 회전각.</summary>
36  public const int BossProps = 6;
37  /// <summary>보상·상점·뽑기·전리품(<c>IRewardRandom</c>).</summary>
38  public const int Rewards = 7;
39  /// <summary>이동덱 셔플. 행동덱과 갈라 둔다 — 한쪽 소비량이 다른 쪽을 밀면 안 된다.</summary>
40  public const int MovementDeckShuffle = 8;
41  /// <summary>행동덱 셔플.</summary>
42  public const int ActionDeckShuffle = 9;

44  /// <summary>런 시드에서 <paramref name="stream"/>번 스트림의 시드를 뽑는다.</summary>
45  public static int Derive(int runSeed, int stream)
46  {
47      return PlacementRandomizer.DeriveSeed(runSeed, stream);
48  }
```

Stream 0-9 table (name -> number, line): MonsterPlacement=0 (L22), Traps=1 (L24), Chests=2 (L26), Services=3 (L28), SpawnHpVariance=3 (L30, shares 3 with Services, re-mixed via spawnRefId per doc comment L15-17,29), MonsterAttackPattern=4 (L32), CombatJudgement=5 (L34), BossProps=6 (L36), Rewards=7 (L38), MovementDeckShuffle=8 (L40), ActionDeckShuffle=9 (L42).

`Derive(int runSeed, int stream)` signature at L45, delegates to `PlacementRandomizer.DeriveSeed(seed, streamIndex)` defined at /Users/yebin/game/memorystone-source/Source/Map/Runtime/PlacementRandomizerTrapsAndChests.cs:134.

Class-level doc (L1-18) states the append-only rule ("🔴 append-only. 기존 번호를 바꾸면 그 시드로 이미 공유된 판이 달라진다"), and that MonsterPlacement uses the raw seed with no derivation (L14-17).

## F2. Source/CardCore/Runtime/CountingRandom.cs — public API

File: /Users/yebin/game/memorystone-source/Source/CardCore/Runtime/CountingRandom.cs

- `public sealed class CountingRandom : Random` — L16, wraps a private `inner` Random (L18).
- `public CountingRandom(int seed) : base(seed)` — L20-25; sets `Seed` and constructs `inner = new Random(seed)`.
- `public int Seed { get; }` — L28 ("진단용. 어느 시드로 만들어졌는지").
- `public int Consumed { get; private set; }` — L31 ("지금까지 소비한 내부 표본 수 = 저장할 커서. FastForward로 버린 칸도 포함한다").
- `public override int Next()` — L33-37, `Consumed++`.
- `public override int Next(int maxValue)` — L39-43, `Consumed++`.
- `public override int Next(int minValue, int maxValue)` — L45-50; `Consumed += (long)maxValue - minValue > int.MaxValue ? 2 : 1` (mirrors System.Random's GetSampleForLargeRange double-sample behavior).
- `public override double NextDouble()` — L52-56, `Consumed++`.
- `public override void NextBytes(byte[] buffer)` — L58-62, `Consumed += buffer?.Length ?? 0`.
- `protected override double Sample()` — L64-68, `Consumed++`.
- `public void FastForward(int count)` — L74-82: loops `inner.Next()` count times, then `Consumed += Math.Max(0, count)`.

Class doc (L6-14) states counting unit is "내부 표본" (internal sample), so FastForward using only Next() lands exactly at the same cursor regardless of which API was originally consumed.

## F3. Run-seed issuance & CombatState RNG instances

Run seed issuance — /Users/yebin/game/memorystone-source/Source/Flow/Unity/MainGameplayController.cs, lines 115-122 (new-entry-only branch inside `if (CurrentStage != null)`, `else` branch when neither `resumedSuspendEnvelope` nor `pendingContinue` applies):

```
115  // 🔑 런 시드 결정 지점(seed-determinism-handoff P1). 배치 랜덤화가 꺼진
116  // 스테이지에서도 발급한다 — 전투 판정·덱 셔플·보상이 이 값에서 갈라지므로
117  // 시드가 없으면 그 축들이 전부 무시드로 돌아간다. 사용자가 지정한 시드가
118  // 있으면(디버그 패널 「이 시드로 재시작」) 그것을, 없으면 무작위.
119  hasPlacementSeed = true;
120  placementSeed = RunSeedRequest.TryConsume(out var requestedSeed)
121      ? requestedSeed
122      : System.Guid.NewGuid().GetHashCode();
```

Followed by `combatController.SetPlacementRandomization(...)` (L125) and, if seeded, `Debug.Log($"MainGameplay run seed={placementSeed} ...")` (L128) + `RunSeedHudLabel.Show(placementSeed)` (L129).

CombatState creates the CountingRandom instances — constructor at /Users/yebin/game/memorystone-source/Source/Combat/Runtime/CombatState.cs:123 (the IEnumerable<MonsterConfig> overload; the single-monster overload at L118 delegates to it). Verified 7 instances:

| Field | Stream # | Creation site |
|---|---|---|
| pushRng | 5 (CombatJudgement) | CombatState.cs L128-130: `pushRng = runSeed.HasValue ? CreateStreamRandom(RunSeedStreams.CombatJudgement) : new System.Random();` |
| planner.monsterAttackPatternRng | 4 (MonsterAttackPattern) | CombatState.cs L135-137 passes `attackPatternSeed: RunSeedStreams.Derive(runSeed.Value, RunSeedStreams.MonsterAttackPattern)` into `new MonsterAiPlanner(...)`; MonsterAiPlanner.cs L36-41 ctor: `monsterAttackPatternRng = new CountingRandom(attackPatternSeed)` |
| planner.attackDamageJitterRng | 4' (derived again from the same attackPatternSeed) | MonsterAiPlanner.cs L41: `attackDamageJitterRng = new CountingRandom(DamageJitterSeed(attackPatternSeed))`, where DamageJitterSeed (L44) = `unchecked(attackPatternSeed * 31 + 17)` |
| bossPropRng | 6 (BossProps) | CombatState.cs L146-150 (inside `if (runSeed.HasValue)`): `bossPropRng = CreateStreamRandom(RunSeedStreams.BossProps); boss.ConfigureBossPropRandom(bossPropRng);` |
| movementShuffleRng | 8 (MovementDeckShuffle) | CombatState.cs L7332 (lazy, DeckShuffleFor): `movementShuffleRng ??= CreateStreamRandom(RunSeedStreams.MovementDeckShuffle)` |
| actionShuffleRng | 9 (ActionDeckShuffle) | CombatState.cs L7333: `actionShuffleRng ??= CreateStreamRandom(RunSeedStreams.ActionDeckShuffle)` |
| Reward RNG (SeededRewardRandom) | 7 (Rewards) | NOT created in CombatState — owned by the controller: /Users/yebin/game/memorystone-source/Source/Combat/Unity/MapCombatController.TestSeams.cs:280: `new SeededRewardRandom(RunSeedStreams.Derive(placementSeed, RunSeedStreams.Rewards), rewardCursor)` |

CreateStreamRandom helper is defined in CombatState.RngCursors.cs:17-20: `return new CountingRandom(RunSeedStreams.Derive(RunSeed.Value, stream));`

Confirmed: pushRng=5, planner=4/4', bossPropRng=6, movement/action shuffle=8/9, reward=7 (controller-owned, not a CombatState field) — 7 instances total, matching the doc comment at CombatSuspendData.cs:106 ("일곱 인스턴스 중 이 상태가 소유한 여섯을... 보상 커서는 컨트롤러 몫").

## F4. CombatState.Suspend.cs, CombatSuspendData, CombatState.RngCursors.cs

Header comment (/Users/yebin/game/memorystone-source/Source/Combat/Runtime/CombatState.Suspend.cs, lines 7-11, verbatim):

```
// ② full-snapshot suspend support. These live on the partial CombatState so they can reach the
// private runtime (monsters/markedMonster/activeEffects/visibilityRuntime and the misc turn fields)
// that a sibling assembly cannot. The design is full-snapshot + suspend: no RNG internal state is
// persisted; monster intent is not stored (it is re-derived deterministically from position/FSM/player
// by RefreshMonsterIntentStep, exactly as the constructor does on its final line).
```

CreateSuspendSnapshot: `public CombatSuspendData CreateSuspendSnapshot()` — CombatState.Suspend.cs:24 (return at L151).

RestoreFromSuspend: `public void RestoreFromSuspend(CombatSuspendData data)` — CombatState.Suspend.cs:154 (closing brace L252).

CombatSuspendData fields (/Users/yebin/game/memorystone-source/Source/Combat/Runtime/CombatSuspendData.cs):
- `public int AttackPatternIndex;` — this is a MonsterRuntimeSaveData field, at line 348: `public int AttackPatternIndex;` (inside the MonsterRuntimeSaveData class, L333-403), populated from `monster.AttackPatternIndex` in CreateMonsterSuspendData (CombatState.Suspend.cs:305).
- `public int AttackDamageRollOffset;` — MonsterRuntimeSaveData L354, with comment (L351-353) that it stores "이번 의도 굴림값" (the rolled result, not RNG state) per "DEC-2026-07-18-02 스냅샷 원칙".
- `public RngCursorsSaveData RngCursors = new RngCursorsSaveData();` — CombatSuspendData field at L101 (comment L98-100).
- RewardCursor is NOT in CombatSuspendData — it lives in CombatSuspendEnvelope.cs:28: `public int RewardCursor;` (controller-level envelope, since CombatState doesn't own the reward RNG — see F3).
- RngCursorsSaveData class (L108-117): Judgement, AttackPattern, DamageJitter, BossProps, MovementShuffle, ActionShuffle (6 int fields — one per CombatState-owned RNG instance; reward's cursor is the 7th, held separately in the envelope).

CombatState.RngCursors.cs (/Users/yebin/game/memorystone-source/Source/Combat/Runtime/CombatState.RngCursors.cs) summary:
- Fields owned here: bossPropRng, movementShuffleRng, actionShuffleRng (L13-15) — comment at L12 notes stream 6·8·9 live here; pushRng(5) is an existing CombatState.cs field; planner RNGs (4/4') are owned by MonsterAiPlanner.
- CreateStreamRandom(int stream) (L17-20): `new CountingRandom(RunSeedStreams.Derive(RunSeed.Value, stream));`
- CaptureRngCursors() (L26-37, public): builds RngCursorsSaveData by reading .Consumed off each live instance (`(pushRng as CountingRandom)?.Consumed ?? 0`, `planner.AttackPatternRngCursor`, `planner.DamageJitterRngCursor`, `bossPropRng?.Consumed ?? 0`, `movementShuffleRng?.Consumed ?? 0`, `actionShuffleRng?.Consumed ?? 0`).
- RestoreRngCursors(RngCursorsSaveData cursors) (L44-62, private): if `!RunSeed.HasValue` no-ops (L46-49). Otherwise rebuilds each of the 6 owned instances from scratch via CreateStreamRandom and immediately FastForwards each to its saved cursor (L52-61), plus calls `planner.RestoreRngCursors(cursors.AttackPattern, cursors.DamageJitter)` (L54) which itself reconstructs the planner's two CountingRandoms from the stored attackPatternSeed and fast-forwards them (MonsterAiPlanner.cs:55-60). Comment at L40-42 explains: rebuilding discards whatever the constructor already consumed (e.g. opening shuffle/first plan), so post-fast-forward the cursor equals exactly the saved value; deck-shuffle streams must be created before RestorePlayerFromSuspend captures them in a shuffle closure.

Intent re-derivation on restore ("preserve rolled values" mode) — RestoreFromSuspend calls, in order, `UpdateOccupancy();` then `planner.RefreshAllIntents(preserveCommittedAttackRolls: true);` (CombatState.Suspend.cs:244-245), with the surrounding comment (verbatim, L239-243):

```
// Re-derive occupancy and monster intent from the restored positions/FSM, mirroring the
// constructor's final two calls. Intent geometry is intentionally not persisted — but the
// committed pattern index and damage roll ARE (「굴린 값이 상태」), so this refresh must not
// re-roll them: re-rolling overwrote the saved intent and advanced the RNG cursor past the
// saved one, which is exactly what P5 (RngCursors) exists to prevent.
```

RefreshAllIntents/RefreshTurnPlan (MonsterAiPlanner.cs:97-101, 139, 147, 151-172) doc verbatim:

```
/// <param name="preserveCommittedAttackRolls">
/// 서스펜드 복원 전용(P5). 패턴 인덱스와 피해 변주는 「굴린 값이 상태」라 저장·복원되는데, 복원 뒤 갱신이
/// 다시 굴리면 저장된 의도가 덮이고 커서까지 밀린다. true면 이동 계획·조준·도약 같은 미저장 기하만
/// 다시 세우고 <b>굴림은 하나도 하지 않는다</b>.
/// </param>
```

Mechanically: when preserveCommittedAttackRolls is true, `ChooseEnemyIntent(monster, commitPatternSelection: false)` (L147) and SelectAttackPatternForOrigin is skipped entirely (L151-154, only called `if (!preserveCommittedAttackRolls)`); for leap attacks, restore mode only re-applies a landing coordinate if the freshly-computed leap pattern index equals the already-restored monster.AttackPatternIndex (L163-171: "복원 모드: 저장된 커밋이 곧 이 도약 패턴이면 착지만 되살린다(굴림 없음)... 여기서 새로 굴리지 않는다").

VERIFIED FACT SHEET — memorystone-source
Repo: /Users/yebin/game/memorystone-source
=================================================================
SYSTEM A — 전투 규칙 코어와 어셈블리 경계
=================================================================

A1. asmdef inventory
--------------------
NOTE on count: the repo has 19 *.asmdef files TOTAL, but only 14 live
under Source/**; the remaining 5 are test asmdefs under Tests/**.
Verified via: find Source -name "*.asmdef" -> 14 files;
find . -name "*.asmdef" -path "./Tests/*" -> 5 files. 14 + 5 = 19.

Source/** asmdefs (14), each with name / noEngineReferences / references:

1. Source/Audio/SeoulPlayup.Audio.asmdef
   name: SeoulPlayup.Audio
   noEngineReferences: false
   references: SeoulPlayup.CardCore, SeoulPlayup.Combat.Runtime, Unity.TextMeshPro

2. Source/CardCore/SeoulPlayup.CardCore.asmdef
   name: SeoulPlayup.CardCore
   noEngineReferences: true   *** (1 of 3)
   references: []

3. Source/Cards/Unity/SeoulPlayup.Cards.Unity.asmdef
   name: SeoulPlayup.Cards.Unity
   noEngineReferences: false
   references: SeoulPlayup.CardCore, SeoulPlayup.Combat.Runtime,
               SeoulPlayup.Combat.Contracts, SeoulPlayup.Map.Runtime,
               SeoulPlayup.Map.Unity, Unity.TextMeshPro, Unity.InputSystem

4. Source/Combat/Contracts/SeoulPlayup.Combat.Contracts.asmdef
   name: SeoulPlayup.Combat.Contracts
   noEngineReferences: false
   references: SeoulPlayup.CardCore, SeoulPlayup.Combat.Runtime

5. Source/Combat/Runtime/SeoulPlayup.Combat.Runtime.asmdef
   name: SeoulPlayup.Combat.Runtime
   noEngineReferences: true   *** (2 of 3)
   references: SeoulPlayup.CardCore, SeoulPlayup.Map.Runtime

6. Source/Combat/SeoulPlayup.Combat.asmdef
   name: SeoulPlayup.Combat
   noEngineReferences: false
   references: SeoulPlayup.Combat.Runtime, SeoulPlayup.Audio,
               SeoulPlayup.Tutorial, SeoulPlayup.Map.Runtime,
               Unity.InputSystem, SeoulPlayup.CardCore, SeoulPlayup.Map.Unity,
               Unity.TextMeshPro, Cinemachine, SeoulPlayup.Combat.Contracts,
               SeoulPlayup.Cards.Unity

7. Source/Combat/Unity/Dev/SeoulPlayup.Dev.asmdef
   name: SeoulPlayup.Dev
   noEngineReferences: false
   references: SeoulPlayup.CardCore, SeoulPlayup.Combat.Runtime,
               SeoulPlayup.Map.Runtime, SeoulPlayup.Map.Unity,
               SeoulPlayup.Combat, SeoulPlayup.Audio, Cinemachine,
               SeoulPlayup.Combat.Contracts, SeoulPlayup.Flow,
               SeoulPlayup.Cards.Unity, Unity.TextMeshPro, Unity.InputSystem,
               Unity.RenderPipelines.Core.Runtime,
               Unity.RenderPipelines.Universal.Runtime
   (defineConstraints: UNITY_EDITOR)

8. Source/Combat/Unity/Editor/AiTools/SeoulPlayup.Combat.Unity.Editor.AiTools.asmdef
   name: SeoulPlayup.Combat.Unity.Editor.AiTools
   noEngineReferences: false
   references: com.IvanMurzak.Unity.MCP.Editor, com.IvanMurzak.Unity.MCP.Runtime,
               SeoulPlayup.Combat, SeoulPlayup.Combat.Runtime,
               SeoulPlayup.Combat.Contracts, SeoulPlayup.Cards.Unity,
               SeoulPlayup.CardCore, SeoulPlayup.Audio, Unity.TextMeshPro,
               Unity.InputSystem
   (Editor-only, precompiledReferences: ReflectorNet.dll, McpPlugin.dll, McpPlugin.Common.dll)

9. Source/Combat/Unity/Tutorial/SeoulPlayup.Tutorial.asmdef
   name: SeoulPlayup.Tutorial
   noEngineReferences: false
   references: SeoulPlayup.Combat.Runtime, SeoulPlayup.Map.Runtime,
               Unity.TextMeshPro, Unity.InputSystem

10. Source/Editor/MapDesign/SeoulPlayup.MapDesign.Editor.asmdef
    name: SeoulPlayup.MapDesign.Editor
    noEngineReferences: false
    references: SeoulPlayup.Map.Runtime, SeoulPlayup.Map.Unity,
                SeoulPlayup.Combat, SeoulPlayup.Combat.Runtime

11. Source/Flow/SeoulPlayup.Flow.asmdef
    name: SeoulPlayup.Flow
    noEngineReferences: false
    references: SeoulPlayup.Combat, SeoulPlayup.Combat.Runtime,
                SeoulPlayup.CardCore, SeoulPlayup.Audio, SeoulPlayup.Tutorial,
                SeoulPlayup.Map.Unity, SeoulPlayup.Map.Runtime,
                Unity.TextMeshPro, Unity.InputSystem, SeoulPlayup.Cards.Unity

12. Source/Map/Runtime/SeoulPlayup.Map.Runtime.asmdef
    name: SeoulPlayup.Map.Runtime
    noEngineReferences: true   *** (3 of 3)
    references: []

13. Source/Map/Unity/Editor/AiTools/SeoulPlayup.Map.Unity.Editor.AiTools.asmdef
    name: SeoulPlayup.Map.Unity.Editor.AiTools
    noEngineReferences: false
    references: com.IvanMurzak.Unity.MCP.Editor, com.IvanMurzak.Unity.MCP.Runtime,
                SeoulPlayup.Map.Unity, SeoulPlayup.Map.Runtime,
                SeoulPlayup.MapDesign.Editor
    (Editor-only)

14. Source/Map/Unity/SeoulPlayup.Map.Unity.asmdef
    name: SeoulPlayup.Map.Unity
    noEngineReferences: false
    references: SeoulPlayup.Map.Runtime, Unity.InputSystem,
                Unity.RenderPipelines.Core.Runtime

*** The 3 with noEngineReferences:true → SeoulPlayup.CardCore,
    SeoulPlayup.Combat.Runtime, SeoulPlayup.Map.Runtime — i.e. exactly the
    pure-C# "rules" layer with zero Unity engine dependency.

Test-side asmdefs (5, not "Source/**", found separately):
Tests/EditMode/Combat/SeoulPlayup.Combat.EditModeTests.asmdef
Tests/EditMode/Map/Runtime/SeoulPlayup.Map.Runtime.EditModeTests.asmdef
Tests/EditMode/Map/Unity/SeoulPlayup.Map.Unity.EditModeTests.asmdef
Tests/PlayMode/Combat/SeoulPlayup.Combat.PlayModeTests.asmdef
Tests/PlayMode/Map/SeoulPlayup.Map.PlayModeTests.asmdef


A2. CombatState constructors
-----------------------------
File: Source/Combat/Runtime/CombatState.cs
Class decl (line 12): `public sealed partial class CombatState`

grep -n 'public CombatState(' Source/Combat/Runtime/CombatState.cs:

Line 118:
public CombatState(HexMapData map, HexCoord playerCoord, HexCoord enemyCoord,
    CombatConfig config, HexTerrainTable terrainTable = null,
    CardCatalogDefinition cardCatalog = null, MonsterCatalogDefinition monsterCatalog = null,
    HexTerrainTraits terrainTraits = null, PlayerInventoryState playerInventory = null,
    PlayerDeckData playerDeck = null, CardDeckState movementDeck = null,
    CardDeckState actionDeck = null, bool drawOpeningHands = true,
    bool shuffleDecks = false, BossCatalogDefinition bossCatalog = null,
    int? runSeed = null)

Line 123 (overload — takes many monsters instead of a single enemyCoord):
public CombatState(HexMapData map, HexCoord playerCoord,
    IEnumerable<MonsterConfig> monsterConfigs, CombatConfig config,
    HexTerrainTable terrainTable = null, CardCatalogDefinition cardCatalog = null,
    MonsterCatalogDefinition monsterCatalog = null, HexTerrainTraits terrainTraits = null,
    PlayerInventoryState playerInventory = null, PlayerDeckData playerDeck = null,
    CardDeckState movementDeck = null, CardDeckState actionDeck = null,
    bool drawOpeningHands = true, bool shuffleDecks = false,
    BossCatalogDefinition bossCatalog = null, int? runSeed = null)

Is it partial? YES — `sealed partial class CombatState`.
All CombatState.*.cs partial files (33, in Source/Combat/Runtime/), verified via find:
CombatState.cs (main), CombatState.BagItems.cs, CombatState.Boss.cs,
CombatState.BossAnnihilation.cs, CombatState.BossArena.cs,
CombatState.BossEncounterHost.cs, CombatState.BossFootprint.cs,
CombatState.BossProps.cs, CombatState.BossScrapChain.cs,
CombatState.BossTraps.cs, CombatState.BossWeakSpot.cs,
CombatState.CodexSightings.cs, CombatState.ControlPacing.cs,
CombatState.DebugSandbox.cs, CombatState.EnemyGrammar.cs,
CombatState.FieldObjectCardEffects.cs, CombatState.MonsterAi.cs,
CombatState.MonsterDamage.cs, CombatState.MonsterFootprint.cs,
CombatState.MonsterIntentCancel.cs, CombatState.MonsterSpawn.cs,
CombatState.MonsterStealth.cs, CombatState.MonsterSummon.cs,
CombatState.MonsterWeakSpot.cs, CombatState.MoveCardEffects.cs,
CombatState.Refine.cs, CombatState.RelicTriggers.cs,
CombatState.RngCursors.cs, CombatState.RuntimeTraps.cs,
CombatState.ServiceObjects.cs, CombatState.Shop.cs,
CombatState.StatusZones.cs, CombatState.Suspend.cs,
CombatState.TorchAndDisarmCardEffects.cs.


A3. Seam chain — verified types, files, assemblies, linking methods
---------------------------------------------------------------------
1. CombatState
   File: Source/Combat/Runtime/CombatState.cs (line 12)
   Assembly: SeoulPlayup.Combat.Runtime (noEngineReferences: true)
   Holds an internal `presentationBuffer` (EffectPresentationBuffer) and exposes:
     - `public bool IsFlushingBufferedEffects => presentationBuffer.IsFlushing;` (line 259)
     - `public void FlushBufferedEffects()` (line 275)
     - `public void FlushBufferedEffects(Func<EffectResultEvent, bool> predicate)` (line 285)
     - `public IReadOnlyList<EffectResultEvent> BufferedEffects => presentationBuffer.Queued;` (line 308)

2. EffectPresentationBuffer
   File: Source/Combat/Runtime/EffectPresentationBuffer.cs (line 20)
   Declaration: `internal sealed class EffectPresentationBuffer`
   Assembly: SeoulPlayup.Combat.Runtime
   Doc comment states it is deliberately rule-agnostic — takes handlers as
   arguments only, never reads/writes rule state; it just defers when
   EffectResolved actually fires (queue of EffectResultEvent, `Queued`
   read-only view, `Begin()`/`TryEnqueue()`).

3. CombatTimelineAssembler
   File: Source/Combat/Runtime/Timeline/CombatTimelineAssembler.cs (line 18)
   Declaration: `public static class CombatTimelineAssembler`
   Assembly: SeoulPlayup.Combat.Runtime (Timeline/ subfolder, no separate asmdef)
   Static factory methods (grep -n 'public static'):
     - Line 117: `public static CombatTimeline BuildPlayerMove(...)`
     - Line 135: `public static CombatTimeline BuildPlayerAttack(...)`
     - Line 217: `public static CombatTimeline BuildEffectBurst(...)`
     - Line 231: `public static CombatTimeline BuildEndAction(...)`
     - Line 469: `public static CombatTimeline BuildMonsterMovementPhase(...)`
   All return type `CombatTimeline` (there is no plain method literally named
   "Assemble" — the "assembling" is done by these Build* factory methods).
   CombatTimeline itself: `public sealed class CombatTimeline`
   File: Source/Combat/Runtime/Timeline/CombatTimeline.cs (line 15)

4. MapCombatController.Presentation.cs (partial of MapCombatController)
   File: Source/Combat/Unity/MapCombatController.Presentation.cs
   Assembly: SeoulPlayup.Combat (root Source/Combat/SeoulPlayup.Combat.asmdef —
   Source/Combat/Unity has no asmdef of its own, so it's compiled under
   SeoulPlayup.Combat)
   Linking: it reads the buffer via `State.BufferedEffects` and feeds it into
   the assembler, e.g.:
     Line 2394: `var timeline = CombatTimelineAssembler.BuildPlayerAttack(string.Empty, attackId, before.PlayerCoord, targetMonsterId, targetBefore, targetAfter, targetHit, targetKnockedBack, targetDied, State != null ? State.BufferedEffects : null, diedUnitIds);`
     Line 2408: `var timeline = CombatTimelineAssembler.BuildEffectBurst(State != null ? State.BufferedEffects : null, diedUnitIds);`
     Line 2432: `var timeline = CombatTimelineAssembler.BuildEndAction(...);`
     Line 2465: `var timeline = CombatTimelineAssembler.BuildMonsterMovementPhase(...);`
   Then hands the assembled timeline to the scheduler:
     Line 2564: `yield return presentationScheduler.Play(timeline, ResolveTimingProfile(), this);`
   (the "this" argument is MapCombatController itself acting as the
   ICombatPresentationSink implementation.)

5. PresentationScheduler.Play
   File: Source/Combat/Unity/Presentation/PresentationScheduler.cs
   Assembly: SeoulPlayup.Combat
   Signature (line 21):
     `public IEnumerator Play(CombatTimeline timeline, CombatTimingProfile timing, ICombatPresentationSink sink)`

6. ICombatPresentationSink
   File: Source/Combat/Unity/Presentation/ICombatPresentationSink.cs (line 17)
   Declaration: `public interface ICombatPresentationSink`
   Assembly: SeoulPlayup.Combat
   First ~10 members (grep -n):
     Line 20: IEnumerator Wait(float seconds);
     Line 22: IEnumerator MovePlayerStep(HexCoord from, HexCoord to, float seconds);
     Line 23: IEnumerator MoveEnemyStep(string monsterId, HexCoord from, HexCoord to, float seconds);
     Line 24: IEnumerator KnockbackPlayerStep(HexCoord from, HexCoord to, float seconds);
     Line 25: IEnumerator KnockbackEnemyStep(string monsterId, HexCoord from, HexCoord to, float seconds);
     Line 28: void StartPlayerAttack(HexCoord from, HexCoord to);
     Line 31: void StartEnemyAttack(string monsterId, string trigger, HexCoord from, HexCoord to);
     Line 34: IEnumerator AttackWindup(string timingKey);
     Line 41: IEnumerator AttackImpactWait(string timingKey, string actorId);
     Line 44: void CommitImpact(string groupId);
   (further members continue: ReactActorHit, ReactActorDeath,
   ReactActorKnockback, ReactPlayerHit, ReactPlayerDeath, DispatchEffect,
   HitStop, DeathHold, MonsterActionGap, FocusOnAction ...)

Full seam is verified real: CombatState (rules, engine-free) -> its internal
EffectPresentationBuffer buffers EffectResultEvents -> exposed as
CombatState.BufferedEffects -> MapCombatController.Presentation.cs (Unity
layer) passes that list into CombatTimelineAssembler.Build*() to get a
CombatTimeline -> hands that CombatTimeline to
PresentationScheduler.Play(timeline, timing, sink) -> the scheduler drives an
ICombatPresentationSink (MapCombatController itself) beat by beat.


A4. Requested verbatim excerpts
---------------------------------
--- Source/Combat/Runtime/SeoulPlayup.Combat.Runtime.asmdef (full text) ---
{
    "name": "SeoulPlayup.Combat.Runtime",
    "rootNamespace": "SeoulPlayup.Combat.Runtime",
    "references": [
        "SeoulPlayup.CardCore",
        "SeoulPlayup.Map.Runtime"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}

--- Source/Combat/Unity/Presentation/PresentationScheduler.cs, lines 1-18 ---
 1  using System.Collections;
 2  using SeoulPlayup.Combat.Runtime.Presentation;
 3  using SeoulPlayup.Combat.Runtime.Timeline;
 4
 5  namespace SeoulPlayup.Combat.Unity.Presentation
 6  {
 7      /// <summary>
 8      /// Replays an ordered <see cref="CombatTimeline"/> beat by beat against an
 9      /// <see cref="ICombatPresentationSink"/>. Movement and effect-stagger waits come from the global
10      /// <see cref="CombatTimingProfile"/>; attack-shaped waits (wind-up, impact, hit-stop) are delegated to
11      /// the sink via each beat's timing key so the per-attack timing table is honored without the scheduler
12      /// knowing about it.
13      ///
14      /// This is the single queue that fixes the old "everything on one frame" problems: movement plays one
15      /// tile at a time, and each impact effect is dispatched then spaced by the profile's effect-stagger so
16      /// SFX/VFX/damage numbers read individually instead of collapsing into one. The scheduler holds no Unity
17      /// references itself, which keeps its ordering logic deterministic and unit-testable.
18      /// </summary>
(line 21 is the Play signature, shown in A3.5 above)


=================================================================
SYSTEM B — 턴 흐름
=================================================================

B5. CombatPhase enum
--------------------
File: Source/Combat/Runtime/CombatPhase.cs (full file, 12 lines)
 1  namespace SeoulPlayup.Combat.Runtime
 2  {
 3      public enum CombatPhase
 4      {
 5          PlayerMovement,
 6          MonsterMovement,
 7          PlayerAction,
 8          MonsterAction,
 9          Victory,
10          Defeat
11      }
12  }


B6. CombatState.EndAction()
----------------------------
File: Source/Combat/Runtime/CombatState.cs, line 2735
Signature: `public bool EndAction()`

Branch structure (verified by reading lines 2735-2768):
  - `LastFailureReason = string.Empty;`
  - If `IsTerminal` -> `return Fail(...)` (no phase change).
  - If `Phase == CombatPhase.PlayerMovement`:
      clears lastMonsterActionRecords, nulls pendingMonsterActionRecords,
      calls `CommitMonsterAttackIntentsAfterPlayerMovementEnd()`,
      then `SetPhase(CombatPhase.MonsterMovement)`, returns true.
      (i.e. ending the movement phase early skips straight to monster
      movement — no player-action phase happens that turn.)
  - Else if `Phase != CombatPhase.PlayerAction` -> `return Fail(...)`.
  - Else (Phase == PlayerAction): resolves turn-end effects in order
    (`ResolveStatusCardTurnEndDamage()`, `ResolveCurseCardTurnEndEffects()`,
    `ResolveTurnEndRelicTriggers()`, `PurgeCopiesFromActionHand()`), then
    `SetPhase(CombatPhase.MonsterAction)`, resets `ActionCostRemaining = 0`,
    returns true.

SetPhase visibility: `private void SetPhase(CombatPhase next)` — line 4562,
i.e. it is a private method; all phase transitions are internal to
CombatState.cs (callers use EndAction/ResolveMonsterMovement/
ResolveMonsterAction/StartPlayerTurn/BeginNextOverallTurn as the public
surface, never SetPhase directly).

CommitMonsterAttackIntentsAfterPlayerMovementEnd:
  Declared (private) at line 3161.
  Called at:
    - Line 1335 (a separate call site elsewhere in CombatState.cs)
    - Line 2747 (inside EndAction(), the PlayerMovement branch above)


B7. ResolveMonsterMovement / ResolveMonsterAction
----------------------------------------------------
File: Source/Combat/Runtime/CombatState.cs

`public void ResolveMonsterMovement()` — line 2877
  Guard: returns early unless `Phase == CombatPhase.MonsterMovement` and not terminal.
  Body: `pendingMonsterActionRecords = BeginMonsterMovementResolution();`
        `ResolveMonsterMovementForCurrentAction(pendingMonsterActionRecords);`
        `CompleteMonsterActionResolution(pendingMonsterActionRecords);`
        if `CheckTerminalOutcomeStep()` return;
        ends with `SetPhase(CombatPhase.PlayerAction);`
  (Does NOT call BeginNextOverallTurn — it hands off to PlayerAction phase.)

`public void ResolveMonsterAction(bool drawPlayerTurnHands = true)` — line 2896
  Guard: returns early unless `Phase == CombatPhase.MonsterAction` and not terminal.
  Body resolves boss mechanics / weak spots / periodic traps / monster attacks,
  then at the end calls:
        `FinishMonsterActionTurnBoundary(statusEffectsBeforeMonsterAction);`
        (private helper, same file) which itself does:
           `EndCurrentOverallTurn();`
           `BeginNextOverallTurn(statusEffectsBeforeMonsterAction);`
        then `if (CheckTerminalOutcomeStep()) return;`
        then `if (drawPlayerTurnHands) { StartPlayerTurn(); }`
  Confirmed: ResolveMonsterAction is the phase that calls BeginNextOverallTurn
  (via FinishMonsterActionTurnBoundary), not ResolveMonsterMovement.


B8. BeginNextOverallTurn — full step sequence
------------------------------------------------
File: Source/Combat/Runtime/CombatState.cs, lines 3006-3024

 3006  private void BeginNextOverallTurn(int freshStatusStartIndex)
 3007  {
 3008      AdvanceOverallTurnCounterStep();
 3009      ClearPlayerBlockStep();
 3010      ActivatePendingFieldObjects();
 3011      ResolveFieldObjectTickStep();
 3012      ApplyActiveEffectTurnStart(freshStatusStartIndex);
 3013      RefillKiForNewTurnStep();
 3014      TickLimitedRelicTurnsStep();
 3015      ResolveTurnStartRelicTriggers();
 3016      ApplyCarriedMovementBonusStep();
 3017      ApplyPendingSelfImmobilize();
 3018      ApplyPendingProvokeStrength();
 3019      ResetPerTurnSignalsStep();
 3020      AdvanceAndExpirePlayerProps();
 3021      RefreshVisionForNewTurnStep();
 3022      RefreshMonsterIntentStep();
 3023      EnterPlayerMovementPhaseStep();
 3024  }

(16 step calls, matching the task's "~16" description.) The last step,
`EnterPlayerMovementPhaseStep()` (line 3107), itself does
`SetPhase(CombatPhase.PlayerMovement); pending.ResetPerMonsterAction();`

StartPlayerTurn:
  File: Source/Combat/Runtime/CombatState.cs, line 3113
  `public void StartPlayerTurn()`
  One-line purpose: gated by a `playerTurnDrawPending` flag — if terminal or
  no draw pending it's a no-op; otherwise calls `DrawNewTurnHands()` and
  clears the flag. (Lines 3113-3122.)

DrawNewTurnHands:
  File: Source/Combat/Runtime/CombatState.cs, line 6824
  `private void DrawNewTurnHands()`
  One-line purpose: draws the movement deck and action deck up to their
  effective hand sizes (accounting for retained cards), then sweeps codex
  sightings and raises `PlayerHandsDrawn`.


B9. PendingEffects.cs
------------------------
File: Source/Combat/Runtime/PendingEffects.cs (207 lines)
Declaration: `internal sealed class PendingEffects` (line 30)
Instantiated as a field on CombatState: line 85 of CombatState.cs —
`internal readonly PendingEffects pending = new PendingEffects();`

Internal "Booking" struct (line 35): `internal readonly struct Booking`
  Fields: `internal int Amount { get; }` and `internal int Turns { get; }`
  — i.e. exactly the (value, duration-in-turns) pair the task asked about.

Bookings held (private fields, lines 47-52):
  - `Booking agility` — pending movement-range-to-agility carryover
  - `Booking selfImmobilize` — delayed self-root
  - `Booking provokeStrength` — delayed enemy strength buff
  - `int nextTurnKiPenalty` — one-shot ki deduction
  - `int activeMovementRangeModifier` — turn-scoped movement bonus (not a Booking)
  - `bool incomingDamageNullifiedThisMonsterAction` — a MONSTER-ACTION-boundary
    flag, explicitly documented (lines 18-21) as having a different lifetime
    than the four turn-boundary bookings, and reset separately by
    `ResetPerMonsterAction()` (line 172) rather than by any Take*.

Activation timing / consumption at the turn boundary:
  All four "Take*" bookings are consumed inside `BeginNextOverallTurn` (see B8),
  at three different points as the class doc (lines 23-28) states:
    - `RefillKiForNewTurnStep()` (line 3047 of CombatState.cs) calls
      `pending.TakeNextTurnKiPenalty()`
    - `ApplyCarriedMovementBonusStep()` (line 3066) calls
      `pending.ClearMovementRangeModifier()` and `pending.TakeAgility()`
    - `ApplyPendingSelfImmobilize()` / `ApplyPendingProvokeStrength()` (called
      at lines 3017-3018) consume `TryTakeSelfImmobilize` / `TryTakeProvokeStrength`
  Each Take*/TryTake* clears the booking back to `default` as it reads it
  ("소진은 꺼내면서 비운다" / take-and-clear, lines 99-147), so double-consumption
  is structurally impossible.
  The monster-action-scoped flag is instead reset by
  `EnterPlayerMovementPhaseStep()` (line 3107-3111) calling
  `pending.ResetPerMonsterAction()`, deliberately kept separate from the four
  turn-boundary Takes (doc comment lines 18-21, 169-175).

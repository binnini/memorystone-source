# V0 — 도식 7장 상자·화살표 사양 초안 (cs:1420 · 2026-09-07)

> 인계문 `docs/prompts/github-readme-v2-handoff.md` §3 를 코드 실측으로 대조한 결과.
> 표기: `[상자]` = 둥근 상자(실제 타입명) · `{구역}` = 점선 큰 상자 · `▤` = 빗금 필드 상자 · `①…` = 번호 화살표.
> 색: 빨강 = 주 흐름 · 파랑 = 데이터 읽기 · 초록 = 2차/파생 · 검정 = 기본 연결.

## §3 대비 정정 (그리기 전에 알아 둘 것)

| 절 | 인계문 §3 | 코드 실측 (cs:1420) | 도식 반영 |
|---|---|---|---|
| A | asmdef 19개 | `Source/**` 14개 + `Tests/**` 5개 = 19. noEngine 3개는 CardCore·Map.Runtime·Combat.Runtime | 순수 구역에 3개만 그림 |
| A | `CombatTimelineAssembler` (비트 목록) | `Build*` 정적 팩토리 5개(`BuildPlayerMove/PlayerAttack/EffectBurst/EndAction/MonsterMovementPhase`) → `CombatTimeline` | 상자명 유지, 곁에 `Build*() → CombatTimeline` |
| B | `ResolveMonsterMovement`/`ResolveMonsterAction` → `BeginNextOverallTurn` | `ResolveMonsterMovement`는 `SetPhase(PlayerAction)`로 끝남. `BeginNextOverallTurn`은 `ResolveMonsterAction` → `FinishMonsterActionTurnBoundary` 안에서만 | 화살표를 MonsterAction 쪽에서만 뽑음 |
| B | `PendingEffects` 「값 + 지속 턴 + 발효 시점」 | `Booking{Amount, Turns}` 3개(agility·selfImmobilize·provokeStrength) + `nextTurnKiPenalty`. 소진은 `BeginNextOverallTurn`의 단계 3곳 | 빗금 상자에 4항목 |
| C | `IMonsterPlanningContext`(…·`PlayerHidden`·프로파일) | `PlayerHidden`은 `MonsterFsmContext`(플래너가 `CreateMonsterFsmContext`로 생성). 인터페이스엔 `IsPlayerHiddenFromMonsters`·`IsMonsterSenseBlindedAt`·`GetBehaviorProfileRef` | 컨텍스트 상자 2단(인터페이스 → FsmContext) |
| C | `GetMonsterIntentPreviews`·`ResolveMonsterMovementStep`이 `CombatState.MonsterAi` 파셜 | 둘 다 `CombatState.cs`; `ResolveMonsterAttackStep`만 `.MonsterAi.cs` | 소비자 구역 이름을 `CombatState`로 |
| D | 손패 정원 2/5 | 코드 기본 `CombatConfig.Default` 1/5, 씬 인스펙터 3/5 | 도식·본문에 숫자 안 씀: 「정원 + 유지분」 |
| D | 훅 5종 | 규칙 훅 9종(이동 목적지·이동 후·방어·유틸 유무·유틸·정찰 후속·공격 피해·처분·제한) + `Upgrade` | 대표 5 + 「…」 |
| F | `CombatSuspendData`에 `AttackPatternIndex`·`RewardCursor` | `AttackPatternIndex`·`AttackDamageRollOffset`는 `MonsterRuntimeSaveData`, `RngCursors`(6)는 `CombatSuspendData`, `RewardCursor`는 `CombatSuspendEnvelope`(컨트롤러) | 저장 상자 3단으로 |
| F | 보상 RNG를 `CombatState`가 생성 | `SeededRewardRandom`은 `MapCombatController` 소유(스트림 7) | 7번째 인스턴스를 Unity 구역에 |
| G | 재롤 상한 40 | 상수 없음. `profile.RerollLimit`(CSV `rerollLimit` 열), P0 기본 20 | 「상한 = 프로파일 rerollLimit」 |
| G | 단계 4 = 몬스터 / 함정·상자 / 서비스 | 순서 = ① 몬스터 `RandomizeWithProfile` ② 함정 `RandomizeTraps` ③ 서비스 `PlaceServices` ④ 상자 `ShuffleChests` (상자보다 서비스 먼저) | 순서대로 4상자 |
| G | 몬스터 스트림 0 | 몬스터는 원시 시드 직접 사용(번호 0은 문서용), 함정 1·상자 2·서비스 3만 `DeriveSeed` | 표기 「원시 시드 / 1 / 3 / 2」 |

---

## 1. [전투 규칙 코어와 어셈블리 경계] — `1.CombatCore`

**구역**
- `{순수 C# · noEngineReferences: true}` (왼쪽, 파랑 배경): `[Map.Runtime]` `[CardCore]` `[Combat.Runtime]` — 셋 다 `UnityEngine` 참조 없음. `Combat.Runtime` → `CardCore`·`Map.Runtime` 참조 화살표(검정 점선).
- `{Unity}` (오른쪽, 주황 배경): `[Combat]` `[Cards.Unity]` `[Map.Unity]` `[Flow]`.
- 가운데 큰 상자 `[CombatState]` (Combat.Runtime 안): 아래에 ▤ 「생성자 주입: HexMapData · CombatConfig · CardCatalogDefinition · MonsterCatalogDefinition · PlayerDeckData · CardDeckState ×2 · runSeed」.
- `CombatState` 안쪽 작은 상자 `[EffectPresentationBuffer]` → `BufferedEffects`(읽기 전용).
- Combat.Runtime 안 `[CombatTimelineAssembler]` (`Build*() → CombatTimeline`, 정적·순수).
- Unity 구역 `[MapCombatController]` (`.Presentation` 파셜) · `[PresentationScheduler]` (`Play(timeline, timing, sink)` 코루틴) · `[ICombatPresentationSink]` (▤ `MovePlayerStep · StartEnemyAttack · AttackWindup · CommitImpact · DispatchEffect …`).

**화살표**
1. (빨강) `Flow/MapCombatController` → `CombatState` 「규칙 호출(`EndAction` · 카드 사용 …)」
2. (검정) `CombatState` → `EffectPresentationBuffer` 「EffectResultEvent 적립」
3. (파랑) `MapCombatController` → `CombatState.BufferedEffects` 「연출이 규칙 산출물을 가지러 온다」
4. (빨강) `MapCombatController` → `CombatTimelineAssembler.Build*` 「버퍼 + 전후 스냅샷」
5. (빨강) `CombatTimelineAssembler` → `CombatTimeline` → `PresentationScheduler.Play`
6. (초록) `PresentationScheduler` → `ICombatPresentationSink` 「비트 하나씩」 → `MapCombatController`(구현체) 되돌아오는 곡선
7. 구역 경계에 큰 라벨: 「규칙은 연출을 모른다 → 어셈블리가 컴파일 단계에서 막는다」

**범례** 빨강 주 흐름 · 파랑 읽기 · 초록 연출 재생 · 점선 어셈블리 참조
**코드 발췌** `Source/Combat/Runtime/SeoulPlayup.Combat.Runtime.asmdef` 전문(17줄).

## 2. [턴 흐름] — `2.TurnFlow`

**상자**
- 원형 순환 4개(큰 둥근 상자): `[PlayerMovement]` → `[MonsterMovement]` → `[PlayerAction]` → `[MonsterAction]` → 다시 `PlayerMovement`. 곁에 `[Victory / Defeat]` (점선, 「종료 상태 — `CheckTerminalOutcomeStep`」).
- 공개 진입점 3개(테두리 굵게): `[EndAction()]` `[ResolveMonsterMovement()]` `[ResolveMonsterAction()]`. `SetPhase`는 private 라벨.
- `[CommitMonsterAttackIntentsAfterPlayerMovementEnd]` (PlayerMovement→MonsterMovement 화살표 위 작은 상자, 「예고 확정」).
- `[FinishMonsterActionTurnBoundary]` → `[BeginNextOverallTurn]` ▤ 16단계 중 대표: `AdvanceOverallTurnCounterStep · ClearPlayerBlockStep · ResolveFieldObjectTickStep · ApplyActiveEffectTurnStart · RefillKiForNewTurnStep · ResolveTurnStartRelicTriggers · ApplyCarriedMovementBonusStep · RefreshVisionForNewTurnStep · RefreshMonsterIntentStep · EnterPlayerMovementPhaseStep`.
- `[StartPlayerTurn]` → `[DrawNewTurnHands]` 「정원 + 유지분 − 현재 손패」.
- 곁상자 ▤ `[PendingEffects]`: `agility(Amount, Turns)` · `selfImmobilize` · `provokeStrength` · `nextTurnKiPenalty` — 「예약은 턴 경계에서 꺼내며 비운다」.

**화살표**
1. (빨강) `EndAction()` — Phase==PlayerMovement 분기 → `Commit…` → `MonsterMovement`
2. (빨강) `EndAction()` — Phase==PlayerAction 분기 → 턴 끝 효과(상태·저주·유물) → `MonsterAction`
3. (빨강) `ResolveMonsterMovement()` → 이동 해소 → `PlayerAction`
4. (빨강) `ResolveMonsterAction()` → 공격 해소 → `FinishMonsterActionTurnBoundary`
5. (빨강) → `BeginNextOverallTurn` 16단계 → `PlayerMovement`
6. (초록) `StartPlayerTurn` → `DrawNewTurnHands` (턴 경계 뒤 별도 호출)
7. (파랑) 카드/유물 → `PendingEffects` 「예약 기록」 · (파랑) `BeginNextOverallTurn` 단계 → `PendingEffects` 「Take*」
8. (검정 점선) 각 해소 뒤 → `Victory/Defeat`

**코드 발췌** `BeginNextOverallTurn` 본문 19줄(`CombatState.cs` 3006–3024).

## 3. [몬스터 AI — 계획 · 예고 · 해소] — `3.MonsterAi`

**구역**
- `{입력}` 왼쪽: `[IMonsterPlanningContext]` ▤ `Map · PlayerCoord · RuntimeStates · MonsterActionOrder() · ClassifyMonsterActivity · IsPlayerHiddenFromMonsters · IsMonsterSenseBlindedAt · GetBehaviorProfileRef` → `[MonsterFsmContext]` ▤ `DistanceToPlayer · PlayerHidden · PlayerIsDead`.
- `{MonsterAiPlanner}` 가운데 세로 파이프라인(위→아래): `[① RefreshAllIntents]` (루프 · `reservedDestinations`) → `[② SelectMovementIntent]` (if/else · `MonsterFsmMemory.State`: Patrol · Chase · Attack · Search · Alert · Return) → `[③ ChooseEnemyMovementStep]` (`HexPathfinder.FindPath`, 예약 칸 = `temporaryBlocked`) → `[④ SelectWeightedAttackPattern]` (`monster_attack_patterns.csv` · 스트림 4 `CountingRandom`) → `[⑤ TryPlanLeapAttack]` → `[⑥ monster.TurnPlan = new MonsterTurnPlan(...)]`.
- `{CombatState}` 오른쪽, 소비자 2: `[GetMonsterIntentPreviews]` 「투영 — AI 재실행 없음」 / `[ResolveMonsterMovementStep · ResolveMonsterAttackStep]` 「집행」.
- 곁상자(오른쪽 아래) `{형상}`: `attack_shapes.csv` → `[AttackShapeLibrary]` 「정동 오프셋 → `RotateSteps((6 − dir) % 6)`」 · ▤ `AttackShapeAdjacency: Full · None · Open · Body · BodyShell`.

**화살표**
1. (파랑) `CombatState` → `IMonsterPlanningContext` 「상태 읽기 전용」
2. (파랑) 컨텍스트 → `MonsterFsmContext` 생성
3–7. (빨강) ①→②→③→④→⑤→⑥ 순차
8. (초록) ⑥ `TurnPlan` → `GetMonsterIntentPreviews` 「같은 함수로 칸 펼침」
9. (빨강) ⑥ `TurnPlan` → `Resolve…Step` 「해소」
10. (검정) ③에서 `reservedDestinations`에 목적지 추가 → 다음 몬스터 ①로 되돌아가는 곡선
11. (파랑) `AttackShapeLibrary` → ④/해소 「형상 조회」

**코드 발췌** `RefreshAllIntents` 예약 루프(`MonsterAiPlanner.cs` 115–137, 23줄).

## 4. [카드와 덱] — `4.CardsAndDecks`

**구역**
- `{데이터}` 왼쪽 위(파랑 배경): `[cards.csv]` ▤ 「표시·메타·밸런스 열만: id · name · type · cost · range · shape · damage · …」 → `[CardCatalogCsvImporter]` (Editor) → `[CardCatalogAsset]` (베이크, 행 검증: 클래스 없는 id 거부) → `[CardCatalogDefinition]`.
- `{런}`: `[PlayerDeckData]` ▤ `MovementCards · ActionCards` (보유 덱).
- `{CombatState}` 가운데: `[MovementDeck : CardDeckState]` · `[ActionDeck : CardDeckState]` 각각 ▤ `DrawPile · Hand · DiscardPile · RemovedPile` (「셔플 스트림 8 / 9」). `[DrawNewTurnHands]` 「정원 + 유지분」.
- `{카드 클래스 · Combat.Runtime/Cards}` 오른쪽(초록 배경): `[CardBehaviorRegistry.Resolve(card)]` → `[CardBehavior]` (추상, 「종류당 1인스턴스·무상태」) ▤ 훅: `TryResolveMoveDestination · TryApplyDefend · TryApplyUtility · ApplyAfterScoutReveal · GetAttackDamage · …` / ▤ 선언: `Disposal · RetainOnTurnEnd · Keywords · Upgrade`. 아래 예시 상자 `[A01_Sweep : BasicAttackCard]` 「59개, 한 파일 한 클래스」.
- `[ConsumePlayedCard]` → `CardDisposal`: `Discard`→DiscardPile · `Exile`→RemovedPile · `HandledByRule`.

**화살표**
1. (파랑) csv → 임포터 → Asset → Definition
2. (검정) Definition → `PlayerDeckData` 「런 시작」
3. (검정) `PlayerDeckData` → `CardDeckState` ×2 「전투 시작, 셔플」
4. (빨강) `DrawNewTurnHands` → Hand
5. (빨강) 카드 사용 → `CardBehaviorRegistry.Resolve` → 훅 호출 (17곳 중 대표)
6. (빨강) 훅 → `CombatState` 규칙 메서드 「피해·이동·방어 …」
7. (초록) `ConsumePlayedCard` → `DisposeAfterPlay` → Discard/Removed 더미
8. (파랑) `CardCatalogAsset` → `CardBehaviorRegistry.Get(id)` 「베이크 시 클래스 존재 검사」
9. 라벨 「카드 클래스는 연출을 모른다」 (연출 구역은 그리지 않음)

**코드 발췌** `A01_Sweep.cs` 전문(14줄) + `CardBehavior` 훅 시그니처 4줄(별도 블록).

## 5. [암시야] — `5.FogOfWar`

**구역**
- `{CombatState}` 왼쪽: `[RefreshPlayerVision]` ▤ 세 소스 「시야 반경(`GetEffectivePlayerVisionRange`) · 필드 오브젝트(`FieldObjects`) · 정찰(`scoutRevealedThisTurn`)」 → 합집합 `revealed`.
- `{Map.Runtime}` 가운데: `[HexVisibilityRuntime]` ▤ 집합 4 `states(저장) · temporaryRevealed · permanentlyRevealed · trapRevealed` / `[SetVisibility]` 「승급만(단조)」 · `[ForceVisibility]` 「강등 허용 (복원 전용)」 / enum `Unknown → Hinted → Revealed`.
- `[GetSafeCellInfo]` → `[HexVisibilitySafeCellInfo]` ▤ 「Unknown: 없음 / Hinted: 지형·이동비용만, `EventId`·`LandmarkId` 비움 / Revealed: 전부」.
- `{Unity 소비 ①}` 오른쪽 위: `[CombatVisibilityPresenter]` 툴팁 · `[TacticalMinimapView]` · `[MapObjectVisualRegistry.ShouldShowForVisibility]`.
- `{Unity 소비 ②}` 오른쪽 아래: `[VisibilityLightingMaskService]` 「단계 → 바이트 마스크, 델타 스킵」 → `_SP_VisibilityMask` → `[MapVisibilityLit.shader]` 「URP Lit 변형 · 월드 좌표 마스크 샘플 · NaN 스크럽」.

**화살표**
1. (빨강) 턴 경계 `RefreshVisionForNewTurnStep` → `RefreshPlayerVision`
2. (빨강) `revealed` → `RefreshTemporaryRevealedCells`
3. (검정) `SetVisibility` → `states` (`Version++`)
4. (파랑) 소비 ① → `GetSafeCellInfo` 「필터된 구조체만」
5. (파랑) 소비 ② → `GetSafeCellInfo` → 마스크 텍스처
6. (초록) 마스크 → 셰이더 전역 프로퍼티
7. (검정 점선) `states` ↔ 세이브 「저장·복원(`ForceVisibility`)」

**코드 발췌** `GetSafeCellInfo` 앞 32줄(`HexVisibilityRuntime.cs` 375–406). A/B 렌더 PNG 2장 본문에 곁들임.

## 6. [세이브와 시드] — `6.SaveAndSeed`

**구역**
- `{Flow}` 왼쪽 위: `[MainGameplayController]` 「런 시드 발급: 요청값 또는 `Guid.NewGuid().GetHashCode()`」.
- `[RunSeedStreams]` ▤ 번호표 `0 MonsterPlacement · 1 Traps · 2 Chests · 3 Services · 4 MonsterAttackPattern · 5 CombatJudgement · 6 BossProps · 7 Rewards · 8 MovementDeckShuffle · 9 ActionDeckShuffle` · `Derive(runSeed, stream)`.
- `{CountingRandom ×7}` 가운데 가로 줄: `pushRng(5)` · `monsterAttackPatternRng(4)` · `attackDamageJitterRng(4′)` · `bossPropRng(6)` · `movementShuffleRng(8)` · `actionShuffleRng(9)` · `SeededRewardRandom(7, 컨트롤러 소유)`. 각 상자 안에 「`Consumed` 커서」.
- `{저장}` 오른쪽: `[CombatSuspendEnvelope]` ▤ `RewardCursor` ⊃ `[CombatSuspendData]` ▤ `RngCursors(6)` ⊃ `[MonsterRuntimeSaveData]` ▤ `AttackPatternIndex · AttackDamageRollOffset` 「굴린 값이 상태」.
- 아래: `[CreateSuspendSnapshot]` / `[RestoreFromSuspend]` → `[RestoreRngCursors]` 「같은 시드로 새로 만들어 `FastForward(Consumed)`」 → `[planner.RefreshAllIntents(preserveCommittedAttackRolls: true)]` 「기하만 재유도, 굴림 0」.

**화살표**
1. (빨강) 컨트롤러 → `RunSeedStreams.Derive` → 7인스턴스
2. (빨강) 전투 중 소비 → `Consumed` 증가
3. (파랑) `CreateSuspendSnapshot` → 커서 6 + 굴린 값
4. (파랑) 컨트롤러 → `RewardCursor` (봉투)
5. (초록) `RestoreFromSuspend` → 재생성 + `FastForward`
6. (초록) → `RefreshAllIntents(preserve)` 「예고 재유도, 저장된 패턴 유지」
7. 범례: 「(A) 스트림 재현 = 시드 + 커서」 vs 「(B) 굴린 결과 저장 = 패턴 인덱스·피해 변주」

**코드 발췌** `RunSeedStreams` 상수표(`RunSeedStreams.cs` 21–48, 28줄).

## 7. [배치 랜덤화] — `7.Placement`

**구역**
- `{저작}` 왼쪽: `[HexSparseMapAuthoringSource]` ▤ 「점유 슬롯(`objectRef` 있음) / 예비 슬롯(`RandomizationGroup` 태그 + `objectRef` 없음 = `IsRandomizationSpareSlot`)」 → `[TryToHexMapData]` → `HexMapData(base)`.
- `{CSV 3장}` 위(파랑): `stage_randomization.csv`(프로파일: ThreatBudget · SafeRadius · RerollLimit · DensityCap · EliteMin …) · `_pools.csv`(가중 풀) · `_bans.csv`(금지 조합) → `[StageRandomizationProfile]`.
- `{HexMapPlacementRandomization.TryApplyProfile}` 가운데 세로: `[① RandomizeWithProfile — 몬스터]` (그룹별 `SampleWithoutReplacement` + `WeightedPickWithRepeatDecay`, 원시 시드) → `[② RandomizeTraps — 함정]` (스트림 1) → `[③ PlaceServices — 서비스]` (스트림 3) → `[④ ShuffleChests — 상자]` (스트림 2).
- ① 옆 루프 상자 `[ValidateProfileAttempt]` ▤ 「안전 반경 · 위협 합 범위 · 정예 하한·거리 · 종 하한 · 밀도 상한」 → 실패 시 재롤(상한 `RerollLimit`).
- 오른쪽 출력: `[HexMapData(randomized)]` → 전투. 실패 시 `[저작 원본 폴백]` (`MapCombatController.MapView`).

**화살표**
1. (파랑) CSV 3 → 프로파일
2. (검정) 저작 → base map → `TryApplyProfile(source, baseMap, seed, profile)`
3–5. (빨강) ①→②→③→④
6. (초록) ① ↔ `ValidateProfileAttempt` 「통과/재롤」 (되돌아가는 곡선, `rerollLimit`)
7. (빨강) ④ → `HexMapData(randomized)` → 전투
8. (검정 점선) 실패 → 저작 원본 폴백
9. 시드 라벨: 「몬스터 = 원시 시드 · 함정 1 · 상자 2 · 서비스 3 (`DeriveSeed`)」

**코드 발췌** `WeightedPickWithRepeatDecay` 25줄(`PlacementRandomizer.cs` 482–506).

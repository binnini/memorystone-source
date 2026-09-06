# 기억결 (Memorystone)

<!-- TODO(D-5): 배너 이미지 — 트레일러 스틸 또는 키아트. ReadMeSource/0.Banner.png -->

> 이 리포는 **소스 열람용 발췌**이며 빌드 대상이 아닙니다. 씬·아트·오디오·서드파티 에셋·데이터 CSV는 포함하지 않습니다.
> 발췌 기준: Plastic SCM `/main/dev` **cs:1420** (2026-09-07). README의 파일 경로와 코드 발췌는 전부 이 리포 안의 파일입니다.

- **장르** : 턴제 헥스 전술 · 덱빌딩 로그라이크
- **도구** : Unity 6000.3 · C# · Plastic SCM · Claude Code / Codex + Unity MCP
- **팀** : 2인 (프로그래밍 1 · 아트 1)
- **플랫폼** : PC
- **기간** : 2026-05 → 2026-09
- **수상** : 제1회 서울 플레이업 AI 게임 챌린지 353팀 중 상위 8팀 · 2026 GES 전시

<br>

# ❗개요

서울을 무대로 한 턴제 헥스 전술 덱빌딩 로그라이크입니다. 플레이어는 이동 덱과 행동 덱 두 벌로 육각 타일 위를 움직이고, 몬스터는 다음 턴의 행동을 미리 예고합니다.

코드는 세 축으로 나뉩니다. **규칙층은 순수 C#**(UnityEngine 참조가 컴파일 단계에서 금지된 어셈블리 3개)이고, **연출은 규칙이 남긴 산출물에서 파생**되며, **밸런스는 코드가 아니라 CSV**에 있습니다. 구현은 AI 도구(Claude Code · Codex)로 했고, 설계 판단과 검증은 사람이 맡았습니다.

아래 7개 절은 시스템 하나마다 도식 한 장 → 동작 설명 → 코드 발췌 순서로 구성했습니다. 도식의 번호 화살표를 본문이 따라갑니다.

<br>

# 📜 목차

1. [전투 규칙 코어와 어셈블리 경계](#1-전투-규칙-코어와-어셈블리-경계)
2. [턴 흐름](#2-턴-흐름)
3. [몬스터 AI — 계획 · 예고 · 해소](#3-몬스터-ai--계획--예고--해소)
4. [카드와 덱](#4-카드와-덱)
5. [암시야](#5-암시야)
6. [세이브와 시드](#6-세이브와-시드)
7. [배치 랜덤화](#7-배치-랜덤화)
- [🕹️ 인게임 영상](#️-인게임-영상)

<br>

# 🖥️ 개발 내용

## 1. 전투 규칙 코어와 어셈블리 경계

<p align="center"><img src="ReadMeSource/1.CombatCore.svg" width="900" alt="전투 규칙 코어와 어셈블리 경계 도식"></p>

전투 규칙은 `Map.Runtime` · `CardCore` · `Combat.Runtime` 세 어셈블리에 있고, 셋 다 `noEngineReferences: true`입니다. 이 어셈블리 안에서는 `UnityEngine` 타입을 쓰는 코드가 컴파일되지 않으므로, 규칙층이 연출이나 씬을 참조하는 일은 구조적으로 막혀 있습니다. 규칙의 중심은 `CombatState`이며 맵·설정·카드 카탈로그·몬스터 카탈로그·덱 두 벌·런 시드를 전부 생성자로 받습니다. Unity 쪽(`Flow` · `Combat` · `Cards.Unity` · `Map.Unity`)은 이 객체의 공개 메서드를 호출하는 것으로 규칙을 움직입니다(①).

규칙 메서드가 실행되면 피해·이동·상태 변화 같은 결과가 `EffectResultEvent`로 `EffectPresentationBuffer`에 쌓입니다(②). 규칙은 여기까지만 하고 연출을 시작하지 않습니다. 대신 Unity 쪽 `MapCombatController`가 `BufferedEffects`를 읽어 가고(③), 규칙 실행 전후의 스냅샷과 함께 `CombatTimelineAssembler`의 `Build*` 메서드에 넘깁니다(④). 어셈블러는 정적 · 순수 코드이고 결과는 재생 순서가 정해진 `CombatTimeline`입니다.

타임라인은 `PresentationScheduler.Play` 코루틴이 비트 하나씩 재생하며(⑤), 각 비트마다 `ICombatPresentationSink`의 메서드(이동 한 칸 · 공격 준비 · 임팩트 · 효과 디스패치)를 호출합니다(⑥). 이 인터페이스의 구현체가 곧 `MapCombatController` 자신입니다(⑦). 스케줄러 자체도 Unity 참조가 없어서 재생 순서 로직은 결정적이고 단위 테스트가 가능합니다.

### 이 시스템에서 중점을 둔 것

「규칙은 연출을 모른다, 연출이 규칙 산출물을 가지러 온다」는 한 방향 의존을 어셈블리 정의 파일이 강제한다는 점입니다. 규칙 코드에 `MonoBehaviour`나 코루틴이 섞이는 실수는 코드 리뷰가 아니라 컴파일러가 잡습니다. 같은 이유로 `Tests/EditMode/Combat`의 테스트는 씬 없이 `CombatState`를 직접 만들어 규칙만 검증합니다.

### 코드

`Source/Combat/Runtime/SeoulPlayup.Combat.Runtime.asmdef` — 규칙 어셈블리 정의 전문. 참조는 순수 어셈블리 둘뿐이고 엔진 참조는 꺼져 있습니다.

```json
{{EX:ex1}}
```

<br>

## 2. 턴 흐름

<p align="center"><img src="ReadMeSource/2.TurnFlow.svg" width="900" alt="턴 흐름 도식"></p>

한 턴은 `CombatPhase`의 네 페이즈 `PlayerMovement → MonsterMovement → PlayerAction → MonsterAction`을 한 바퀴 돕니다. 페이즈를 바꾸는 `SetPhase`는 private이고, 밖에서 쓸 수 있는 진입점은 `EndAction()` · `ResolveMonsterMovement()` · `ResolveMonsterAction()` 셋입니다. 플레이어가 이동 페이즈를 끝내면 `EndAction()`이 몬스터 공격 예고를 확정(`CommitMonsterAttackIntentsAfterPlayerMovementEnd`)한 뒤 몬스터 이동으로 넘깁니다(①). 행동 페이즈를 끝내면 상태이상 · 저주 · 유물의 턴 끝 효과를 처리하고 몬스터 액션으로 넘깁니다(②).

`ResolveMonsterMovement()`는 계획된 이동을 해소한 뒤 플레이어 행동 페이즈로 넘기기만 합니다(③). 턴의 경계는 `ResolveMonsterAction()` 쪽에 있습니다. 몬스터 공격을 해소한 뒤 `FinishMonsterActionTurnBoundary`가 현재 턴을 닫고(④) `BeginNextOverallTurn`을 호출합니다(⑤). 이 함수는 턴 번호 증가 · 방어도 소거 · 필드 오브젝트 틱 · 상태이상 틱 · 기 회복 · 유물 훅 · 예약 소진 · 시야 갱신 · 몬스터 예고 갱신 · 페이즈 진입까지 16단계를 정해진 순서로 호출하고, 마지막 단계가 `PlayerMovement`로 되돌립니다(⑥). 새 손패 드로우는 경계 안이 아니라 그 뒤 `StartPlayerTurn` → `DrawNewTurnHands`에서 일어나며, 정원에 유지 카드 수를 더한 만큼 채웁니다(⑦).

「다음 턴에 발효되는 효과」는 `PendingEffects`가 맡습니다. 카드나 유물이 민첩 · 자기 속박 · 도발 강화 같은 예약을 값과 지속 턴으로 기록하면(⑧), `BeginNextOverallTurn`의 해당 단계가 `Take*`로 꺼내면서 비웁니다(⑨). 꺼낸 자리는 곧바로 기본값이 되므로 같은 예약이 두 번 발효될 수 없습니다. 승리 · 패배 판정은 해소가 끝날 때마다 `CheckTerminalOutcomeStep`이 확인합니다(⑩).

### 이 시스템에서 중점을 둔 것

턴 경계에서 일어나는 일의 **순서 자체가 게임 규칙**이라는 점입니다. 방어도가 먼저 사라지고 그 다음 상태이상이 틱하고 그 다음 기가 회복되는 순서는 밸런스에 직접 닿습니다. 그래서 16단계를 여러 곳에 흩어 두지 않고 한 함수의 호출 순서로 고정했고, 각 단계는 이름만 읽어도 무엇을 하는지 드러나는 메서드로 나눴습니다.

### 코드

`Source/Combat/Runtime/CombatState.cs` — `BeginNextOverallTurn` 본문. 이 19줄이 턴 경계의 전부입니다.

```csharp
{{EX:ex2}}
```

<br>

## 3. 몬스터 AI — 계획 · 예고 · 해소

<p align="center"><img src="ReadMeSource/3.MonsterAi.svg" width="900" alt="몬스터 AI 도식"></p>

몬스터 AI는 `MonsterAiPlanner` 한 클래스가 몬스터마다 위에서 아래로 한 번 흐르는 계획기입니다. 입력은 `IMonsterPlanningContext` 인터페이스로만 받습니다. `CombatState`가 이 인터페이스를 구현하며(①) 맵 · 플레이어 좌표 · 행동 순서 · 활성 상태 · 은신 · 실명 · 행동 프로파일을 읽기 전용으로 제공합니다. 플래너는 여기서 몬스터별 `MonsterFsmContext`(거리 · `PlayerHidden` · 플레이어 사망 여부)를 만들어(②) 의도 선택에 넘깁니다(③).

파이프라인은 여섯 단계입니다. `RefreshAllIntents`가 행동 순서대로 몬스터를 돌고(①), `SelectMovementIntent`가 `MonsterFsmMemory`의 상태(Patrol · Chase · Attack · Search · Alert · Return)를 if/else 한 함수로 갱신해 이동 의도를 정합니다(②). `ChooseEnemyMovementStep`은 `HexPathfinder.FindPath`로 목적지를 고르는데, 앞 몬스터가 예약한 목적지를 `temporaryBlocked` 칸으로 주입해 같은 칸으로 두 몬스터가 몰리지 않게 합니다(③). 공격 패턴은 `monster_attack_patterns.csv`의 가중치로 시드 스트림 4에서 추첨하고(④), 도약 공격이 가능하면 착지 칸을 정한 뒤(⑤) 결과를 `MonsterRuntime.TurnPlan`에 커밋합니다(⑥). 커밋된 목적지는 `reservedDestinations`에 들어가 다음 몬스터의 ③에 영향을 줍니다(⑨).

`TurnPlan`은 소비자가 둘입니다. `GetMonsterIntentPreviews`는 플레이어에게 보여 줄 예고를 만드는데, AI를 다시 돌리지 않고 커밋된 계획의 목적지와 잠긴 조준을 그대로 펼칩니다(⑩). `ResolveMonsterMovementStep` · `ResolveMonsterAttackStep`은 같은 계획을 실제로 집행합니다(⑪). 공격 범위 형상은 `attack_shapes.csv`에서 `AttackShapeLibrary`로 읽어 오며, 정동 방향 기준 오프셋을 `RotateSteps((6 − dir) % 6)`으로 회전해 어느 방향이든 같은 문법으로 폅니다(⑫). 형상마다 `AttackShapeAdjacency`(Full · None · Open · Body · BodyShell)가 몸체 칸과의 관계를 정합니다.

### 이 시스템에서 중점을 둔 것

**예고가 곧 계획**이라는 점입니다. 화면에 그려지는 예고와 다음 턴에 실제로 집행되는 행동이 같은 `TurnPlan` 객체에서 나오므로, 예고와 실행이 어긋날 여지가 없습니다. 플래너가 컨텍스트 밖의 상태를 건드리지 않고 유일한 가변 상태가 패턴 추첨 RNG뿐이라는 것도 같은 목적입니다. 해소(피해 · 넉백 · 상태이상)는 플래너에 없고 `CombatState`에 남습니다.

### 코드

`Source/Combat/Runtime/MonsterAiPlanner.cs` — `RefreshAllIntents`의 예약 루프. 죽었거나 휴면인 몬스터는 비활성 계획을 받고, 나머지는 앞 몬스터의 예약 목적지를 넘겨받으며 계획을 세웁니다.

```csharp
{{EX:ex3}}
```

<br>

## 4. 카드와 덱

<p align="center"><img src="ReadMeSource/4.CardsAndDecks.svg" width="900" alt="카드와 덱 도식"></p>

카드 데이터의 정본은 `cards.csv`입니다. 이 표에는 이름 · 설명 · 타입 · 비용 · 사거리 · 형상 · 피해 같은 표시와 밸런스 열만 있고 규칙 로직은 없습니다. 에디터의 `CardCatalogCsvImporter`가 이 표를 `CardCatalogAsset`으로 베이크하고, 런타임에는 `CardCatalogDefinition`이 됩니다(①). 베이크 단계에서 각 행의 id에 대응하는 카드 클래스가 `CardBehaviorRegistry`에 등록돼 있는지 검사해, 클래스 없는 카드는 카탈로그에 들어가지 못합니다(⑩).

런이 시작되면 카탈로그에서 `PlayerDeckData`(보유 카드 목록)가 만들어지고(②), 전투가 시작되면 이동 덱과 행동 덱이 각각 `CardDeckState`로 세워집니다(③). 덱 하나는 뽑을 더미 · 손패 · 버림 더미 · 소멸 더미 네 개로 이루어지고, 셔플은 이동 덱이 시드 스트림 8, 행동 덱이 9를 씁니다. 턴마다 `DrawNewTurnHands`가 정원에 유지 카드 수를 더한 만큼 손패를 채웁니다(④).

카드를 쓰면 `CombatState`가 `CardBehaviorRegistry.Resolve(card)`로 카드 클래스를 찾고(⑤) 훅을 호출합니다(⑥). `CardBehavior`는 추상 클래스로, 이동 목적지 결정 · 방어 · 유틸리티 · 정찰 후속 · 공격 피해 같은 규칙 훅과 `Disposal` · `RetainOnTurnEnd` · `Keywords` · `Upgrade` 같은 선언을 가집니다. 훅은 기본이 no-op이고 카드 클래스는 필요한 것만 override합니다. 훅은 `CombatState`의 규칙 메서드를 호출해 실제 피해 · 이동 · 상태를 적용하고(⑦), 사용이 끝나면 `ConsumePlayedCard`가 `DisposeAfterPlay`의 답에 따라 버림 더미 또는 소멸 더미로 보냅니다(⑧ · ⑨). 카드 클래스는 59개이고 한 파일에 한 클래스, 등록은 한 줄입니다. 클래스는 종류당 한 인스턴스이며 상태를 갖지 않습니다.

### 이 시스템에서 중점을 둔 것

카드 하나의 규칙이 **클래스 하나에 모여 있고**, 규칙이 카드를 부르는 지점이 `CombatState`의 `Resolve` 호출부로 한정된다는 점입니다. 데이터(CSV)와 규칙(클래스)이 id로만 이어지므로 밸런스 수정은 표에서, 규칙 수정은 클래스에서 끝납니다. 카드 클래스는 연출 타입을 전혀 모르고 `CombatState`의 규칙 메서드만 호출합니다.

### 코드

`Source/Combat/Runtime/Cards/Attack/A01_Sweep.cs` — 카드 클래스 하나의 전문. 기본 공격 카드라 훅 override 없이 id와 강화 규칙만 선언합니다.

```csharp
{{EX:ex4a}}
```

`Source/Combat/Runtime/CombatState.cs` — `ConsumePlayedCard`. 사용한 카드의 처분을 결정하는 유일한 지점입니다.

```csharp
{{EX:ex4b}}
```

<br>

## 5. 암시야

<p align="center"><img src="ReadMeSource/5.FogOfWar.svg" width="900" alt="암시야 도식"></p>

시야 갱신은 턴 경계와 이동 뒤에 `CombatState.RefreshPlayerVision`이 맡습니다(①). 시야 반경 안의 칸, 횃불 같은 필드 오브젝트가 비추는 칸, 이번 턴 정찰 카드로 밝힌 칸을 하나의 집합으로 합쳐 `HexVisibilityRuntime`에 넘깁니다(②). 런타임은 칸마다 `Unknown → Hinted → Revealed` 세 단계를 `states`에 들고, 이번 갱신에서 보인 칸 · 영구히 밝혀진 칸 · 함정이 드러난 칸을 별도 집합으로 관리합니다. `SetVisibility`는 단계를 올리기만 하는 단조 연산이고(③), 단계를 내리는 `ForceVisibility`는 세이브 복원 때만 쓰입니다(⑦).

Unity 쪽은 칸의 상태를 직접 읽지 않고 `GetSafeCellInfo`가 돌려주는 `HexVisibilitySafeCellInfo`만 받습니다(④). 이 구조체는 단계에 따라 내용을 거릅니다. `Unknown`이면 좌표만, `Hinted`면 지형과 이동 비용은 주되 이벤트 id와 랜드마크 id는 비우고, `Revealed`여야 전부 채웁니다. 툴팁(`CombatVisibilityPresenter`) · 미니맵(`TacticalMinimapView`) · 맵 오브젝트 표시 여부(`MapObjectVisualRegistry`)가 모두 이 구조체를 통해 정보를 얻습니다.

렌더는 같은 구조체에서 갈라집니다. `VisibilityLightingMaskService`가 칸 단계를 바이트 마스크 텍스처로 굽고(⑤), 바뀐 슬롯이 하나도 없으면 업로드를 건너뜁니다. 텍스처는 전역 프로퍼티 `_SP_VisibilityMask`로 올라가고, `MapVisibilityLit.shader`(URP Lit 변형)가 픽셀의 월드 좌표를 마스크 UV로 바꿔 샘플링해 밝기를 정합니다(⑥). 출력 직전에는 `max`/`min`으로 NaN을 씻어 후처리 블룸이 화면 전체로 번지는 것을 막습니다.

<p align="center">
<img src="ReadMeSource/5.FogRenderAB_1.png" width="440" alt="LightingMask 렌더. 시야 원판을 중심으로 밝기가 방사형으로 떨어진다.">
<img src="ReadMeSource/5.FogRenderAB_2.png" width="440" alt="OverlayTint 렌더. 칸 단위로 계단진다.">
</p>

### 이 시스템에서 중점을 둔 것

정보 은닉을 **자료구조 수준**에서 처리한다는 점입니다. 보이지 않는 칸의 이벤트나 랜드마크는 Unity 쪽 코드에 아예 전달되지 않으므로, UI 코드가 실수로 미지의 칸 정보를 그리는 일이 생기지 않습니다. 렌더 마스크도 같은 구조체에서 파생되어 논리 시야와 화면 시야가 한 소스를 공유합니다.

### 코드

`Source/Map/Runtime/HexVisibilityRuntime.cs` — `GetSafeCellInfo` 앞부분. 단계별로 무엇을 비우는지가 생성자 인자에 그대로 드러납니다.

```csharp
{{EX:ex5}}
```

<br>

## 6. 세이브와 시드

<p align="center"><img src="ReadMeSource/6.SaveAndSeed.svg" width="900" alt="세이브와 시드 도식"></p>

런 시드는 `MainGameplayController`가 발급합니다. 디버그 패널에서 지정한 값이 있으면 그것을, 없으면 무작위 값을 쓰며 배치 랜덤화가 꺼진 스테이지에서도 발급합니다(①). 시드 하나를 그대로 쓰지 않고 `RunSeedStreams`의 번호표(0 몬스터 배치 · 1 함정 · 2 상자 · 3 서비스 · 4 공격 패턴 · 5 전투 판정 · 6 보스 기물 · 7 보상 · 8 이동 덱 셔플 · 9 행동 덱 셔플)로 `Derive`해 용도별 스트림 시드를 만듭니다(②). 번호표는 추가만 하고 바꾸지 않습니다.

스트림마다 `CountingRandom` 인스턴스가 하나씩 섭니다. 전투 판정용 `pushRng`, 플래너의 패턴 추첨과 피해 변주, 보스 기물, 덱 셔플 둘, 그리고 컨트롤러가 소유하는 보상 RNG까지 일곱입니다. `CountingRandom`은 `System.Random`을 감싸 지금까지 소비한 표본 수 `Consumed`를 셉니다(③). 세이브는 RNG 내부 상태를 저장하지 않습니다. `CreateSuspendSnapshot`이 인스턴스 여섯의 커서를 `CombatSuspendData.RngCursors`에 담고(④), 보상 커서는 바깥 봉투 `CombatSuspendEnvelope`에 담깁니다(⑤). 몬스터가 이미 굴린 공격 패턴 인덱스와 피해 변주는 `MonsterRuntimeSaveData`에 값 자체로 저장됩니다.

복원은 `RestoreFromSuspend`가 저장된 커서와 값을 읽고(⑥), `RestoreRngCursors`가 같은 시드로 인스턴스를 새로 만들어 `FastForward(Consumed)`로 저장 시점의 자리까지 넘깁니다(⑦). 마지막으로 `planner.RefreshAllIntents(preserveCommittedAttackRolls: true)`가 몬스터 예고의 기하(경로 · 조준 · 도약 착지)만 다시 세우고 굴림은 하나도 하지 않아, 저장된 패턴이 그대로 남습니다(⑧).

### 이 시스템에서 중점을 둔 것

재현 방식을 두 가지로 나눈 점입니다. 아직 굴리지 않은 것은 **시드 + 커서**로 재현하고(A), 이미 굴려서 플레이어에게 보인 것은 **값 자체**로 저장합니다(B). 예고된 공격을 복원 뒤 다시 굴리면 플레이어가 본 것과 다른 공격이 나오므로, 그런 값은 상태로 취급합니다. 용도별 스트림을 나눈 것은 한쪽 소비량이 다른 쪽 결과를 밀지 않게 하기 위해서입니다.

### 코드

`Source/Map/Runtime/RunSeedStreams.cs` — 번호표 전문과 `Derive`. 각 번호의 주석이 그 스트림이 어디서 소비되는지를 적고 있습니다.

```csharp
{{EX:ex6}}
```

<br>

## 7. 배치 랜덤화

<p align="center"><img src="ReadMeSource/7.Placement.svg" width="900" alt="배치 랜덤화 도식"></p>

맵 에디터에서 저작하는 `HexSparseMapAuthoringSource`의 슬롯은 두 종류입니다. `objectRef`가 있는 점유 슬롯은 항상 그 자리에 그 오브젝트가 놓이고, `RandomizationGroup` 태그만 있고 `objectRef`가 비어 있는 예비 슬롯은 랜덤화가 채울 수 있는 후보 좌표입니다. `TryToHexMapData`가 저작 원본을 `HexMapData`로 바꾸고(②), 이 기본 맵과 시드가 `HexMapPlacementRandomization.TryApplyProfile`에 들어갑니다.

스테이지별 규칙은 CSV 세 장에서 옵니다. `stage_randomization.csv`가 위협 예산 · 안전 반경 · 재롤 상한 · 밀도 상한 · 정예 하한 같은 프로파일을, `_pools.csv`가 그룹별 가중 풀을, `_bans.csv`가 함께 나오면 안 되는 조합을 정하고 `StageRandomizationProfile`로 합쳐집니다(①). 배치는 네 단계를 정해진 순서로 지납니다. 몬스터(`RandomizeWithProfile`)는 그룹별로 슬롯을 비복원 추첨하고 풀에서 종을 고르는데, 이미 뽑힌 종은 가중치가 반감되어 같은 종이 몰리지 않습니다(③). 그 다음 함정(`RandomizeTraps`)(④), 서비스 오브젝트(`PlaceServices`)(⑤), 상자(`ShuffleChests`) 순이며 각 단계는 자기 시드 스트림을 씁니다.

몬스터 배치 결과는 `ValidateProfileAttempt`가 검사합니다(⑥). 플레이어 안전 반경 안에 몬스터가 없는지, 위협 합이 예산 범위인지, 정예 수와 거리가 하한을 넘는지, 종 수가 하한 이상인지, 반경 안 밀도가 상한 이하인지를 보고 하나라도 어긋나면 재롤합니다. 재롤 상한은 프로파일의 `RerollLimit`입니다. 통과하면 랜덤화된 `HexMapData`가 전투로 넘어가고(⑦), 상한을 넘기거나 프로파일이 없으면 저작 원본 그대로 진행합니다(⑧).

### 이 시스템에서 중점을 둔 것

저작과 랜덤화의 역할 분담입니다. 어디에 무엇이 올 **수 있는가**는 맵 에디터의 슬롯 문법(점유 / 예비 + 그룹 태그)이 정하고, 그중 무엇이 **실제로 오는가**는 CSV 프로파일과 시드가 정합니다. 검증 게이트는 랜덤 결과가 스테이지 의도를 벗어나지 않게 막는 마지막 층이며, 실패 시에도 저작 원본이라는 안전한 결과가 남습니다.

### 코드

`Source/Map/Runtime/PlacementRandomizer.cs` — `WeightedPickWithRepeatDecay`. 이미 뽑힌 횟수에 따라 감쇠된 가중치로 룰렛 추첨을 합니다.

```csharp
{{EX:ex7}}
```

<br>

# 🕹️ 인게임 영상

<!-- TODO(D-5): 트레일러 링크 · 스크린샷 -->

<br>

# 라이선스

- `Source/`·`Tests/`의 코드: [MIT](LICENSE)
- README 본문과 `ReadMeSource/`의 도식·이미지: [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/)

"""Generate the 7 README system diagrams as Excalidraw JSON (+ SVG previews).

    python3 gen_diagrams.py ../ /tmp/preview

Facts behind every box/arrow: _gen/facts/facts_*.md (verified at cs:1420).
"""
import os
import sys
from exlib import *  # noqa: F401,F403

OUT_EX = sys.argv[1] if len(sys.argv) > 1 else "."
OUT_SVG = sys.argv[2] if len(sys.argv) > 2 else OUT_EX
os.makedirs(OUT_EX, exist_ok=True)
os.makedirs(OUT_SVG, exist_ok=True)


def emit(d, fname):
    d.write(os.path.join(OUT_EX, fname + ".excalidraw"), os.path.join(OUT_SVG, fname + ".svg"))
    print("wrote", fname, len(d.els), "elements")


# =============================================================================
# 1. 전투 규칙 코어와 어셈블리 경계
# =============================================================================
def d1():
    d = Diagram("core", 2200, 1560, seed=11)
    d.title("전투 연산과 어셈블리 경계")

    d.zone(60, 120, 1180, 1350, "순수 C#  —  noEngineReferences: true  (UnityEngine 사용 불가)", bg=BG_BLUE)
    d.zone(1300, 120, 840, 1350, "Unity", bg=BG_ORANGE)

    d.box(100, 200, 220, 80, "Map.Runtime", sub="헥스 좌표 · 맵 · 시야", fs=20, sfs=13)
    d.box(360, 200, 220, 80, "CardCore", sub="덱 · 카드 정의 · 난수", fs=20, sfs=13)
    d.zone(100, 330, 1100, 1100, "Combat.Runtime  (전투 연산)", bg="#d0ebff", label_pos="topleft")
    d.arrow([(300, 330), (300, 290)], color=BLACK, style="dashed", sw=1.5)
    d.arrow([(470, 330), (470, 290)], color=BLACK, style="dashed", sw=1.5)
    d.note(620, 292, "참조", fs=14, color=GRAY, anchor="topleft", align="left")

    # CombatState
    d.rect(160, 420, 560, 660, stroke=BLACK, sw=3)
    d.text(440, 445, "CombatState", fs=30)
    d.note(440, 485, "전투 상태(위치·체력·손패·턴)을 입력 받고,\n요청이 오면 규칙대로 전투 연산을 실행", fs=15, color=GRAY)
    d.field(190, 545, 500, 250, "생성자 인자 (요청 시 입력 받음)",
            ["HexMapData  (맵)", "CombatConfig  (전투 설정)", "CardCatalogDefinition  (카드 카탈로그)",
             "MonsterCatalogDefinition  (몬스터 카탈로그)", "PlayerDeckData  (플레이어 보유 덱)",
             "CardDeckState ×2  (셔플된 이동덱 · 행동덱)", "runSeed  (런 시드)"],
            bg="#a5d8ff", fs=16, lfs=15, align="left")
    d.box(190, 840, 500, 120, "EffectPresentationBuffer",
          sub="EffectResultEvent(전투 연산 결과)를 일어난 순서대로\nBufferedEffects로 기록", sfs=14)

    d.box(780, 720, 400, 190, "CombatTimelineAssembler",
          sub="전투 화면 연출의 순서를 결정\nBuild (전투 기록)\n→ CombatTimeline (전투 연출 순서 목록)", sfs=14)

    # Unity side
    d.box(1340, 200, 230, 80, "Flow", sub="씬 전환 · 전투 시작 · 런 시드", fs=20, sfs=12)
    d.box(1605, 200, 230, 80, "Cards.Unity", sub="카드 UI · 입력", fs=20, sfs=12)
    d.box(1870, 200, 230, 80, "Map.Unity", sub="타일 좌표 · 캐릭터 액터", fs=20, sfs=12)
    d.note(1720, 172, "Combat 을 참조하는 다른 Unity 어셈블리 — 입력의 앞과 화면 출력의 뒤에 붙는다", fs=14, color=GRAY)
    d.zone(1340, 340, 760, 1090, "Combat  (어셈블리 · Source/Combat/Unity)", bg="#ffe8cc", label_pos="topleft")
    for x in (1455, 1720, 1985):
        d.arrow([(x, 280), (x, 340)], color=BLACK, style="dashed", sw=1.5)
    d.arrow([(1340, 400), (1200, 400)], color=BLACK, style="dashed", sw=1.5)
    d.note(1210, 408, "참조", fs=13, color=GRAY, anchor="topleft", align="left")

    d.box(1380, 450, 680, 190, "MapCombatController",
          sub="전투 컨트롤러(MonoBehaviour)\n사용자 입력을 받아 전투 규칙을 호출하고, 결과를 가져와 화면에 그린다\n아래 ICombatPresentationSink 의 구현체이기도 하다", sfs=14)
    d.box(1380, 820, 680, 130, "PresentationScheduler (연출 스케줄러)",
          sub="Play(timeline, timing, sink) (연출 재생 코루틴)\n타임라인을 한 항목씩, 항목 사이 대기 시간을 두고 실행", sfs=14)
    d.field(1380, 1030, 680, 340, "ICombatPresentationSink (스케줄러의 화면 지시 목록)",
            ["Wait  (대기)", "MovePlayerStep · MoveEnemyStep  (캐릭터 이동)",
             "KnockbackPlayerStep · KnockbackEnemyStep  (캐릭터 강제 이동)",
             "StartPlayerAttack · StartEnemyAttack  (공격 모션 시작)",
             "AttackWindup · AttackImpactWait  (타격 순간까지 대기)", "CommitImpact  (타격 확정)",
             "ReactActorHit · ReactActorDeath  (피격 · 사망 반응)", "DispatchEffect · HitStop (효과 표시 · 히트스톱)"],
            bg="#ffd8a8", fs=16, lfs=15, align="left")

    # arrows
    d.arrow([(1380, 520), (720, 520)], color=RED, sw=3)
    d.note(1050, 478, "① 「휘둘러치기 카드 사용」  「턴 종료」 요청", fs=16, color=RED)

    d.arrow([(440, 795), (440, 840)], color=BLACK, sw=2)
    d.note(470, 805, "② 전투 규칙대로 연산 후 결과 기록", fs=15, anchor="topleft", align="left")

    d.arrow([(1420, 640), (1420, 680), (740, 680), (740, 900), (690, 900)], color=BLUE, sw=3)
    d.note(1000, 652, "③ 연산이 끝나면 전투 기록 읽기", fs=15, color=BLUE)

    d.arrow([(1500, 640), (1500, 760), (1180, 760)], color=RED, sw=3)
    d.note(1340, 722, "④ 전투 기록 + 화면의 전후 스냅샷", fs=15, color=RED)

    d.arrow([(980, 910), (980, 960), (1250, 960), (1250, 885), (1380, 885)], color=RED, sw=3)
    d.note(1120, 968, "⑤ 완성된 연출 타임라인을 스케줄러로 전달", fs=15, color=RED)

    d.arrow([(1720, 950), (1720, 1030)], color=GREEN, sw=3)
    d.note(1740, 972, "⑥ 한 항목씩 화면 표현 지시", fs=16, color=GREEN, anchor="topleft", align="left")

    d.arrow([(2060, 1200), (2085, 1200), (2085, 545), (2060, 545)], color=GREEN, sw=2, style="dashed")
    d.note(1935, 970, "⑦ 컨트롤러에서\n연출 실행", fs=13, color=GREEN, anchor="topleft", align="left")

    d.note(440, 1130, "전투 연산은 ②에서 종료\n⑥,⑦이 진행되는 동안에도 이미 결과는 종료", fs=18, color=RED)

    d.legend(760, 1160, 420, [(RED, "solid", "연산 요청과 연출 요청"), (BLUE, "solid", "결과 읽기"),
                              (GREEN, "solid", "화면 재생"), (BLACK, "dashed", "어셈블리 참조 / 구현")])
    emit(d, "1.CombatCore")


# =============================================================================
# 2. 턴 페이즈와 턴 경계 처리
# =============================================================================
def d2():
    d = Diagram("turn", 2200, 1350, seed=22)
    d.title("턴 페이즈와 턴 경계 처리")

    d.box(600, 260, 320, 110, "PlayerMovement", sub="플레이어 이동 페이즈", fs=26, sfs=13, bg=BG_BLUE)
    d.box(1300, 260, 320, 110, "MonsterMovement", sub="몬스터 이동 페이즈", fs=26, sfs=13, bg=BG_RED)
    d.box(1300, 900, 320, 110, "PlayerAction", sub="플레이어 행동 페이즈", fs=26, sfs=13, bg=BG_BLUE)
    d.box(600, 900, 320, 110, "MonsterAction", sub="몬스터 행동 페이즈", fs=26, sfs=13, bg=BG_RED)
    d.note(1110, 440, "CombatPhase (페이즈 순환)\nSetPhase 는 private — 페이즈 전환은 CombatState 안에서만", fs=16, color=GRAY)

    d.box(860, 150, 500, 80, "CommitMonsterAttackIntentsAfterPlayerMovementEnd", sub="몬스터 공격 예고 확정", fs=18, sfs=14)

    d.arrow([(760, 260), (760, 190), (860, 190)], color=RED, sw=3)
    d.arrow([(1360, 190), (1460, 190), (1460, 260)], color=RED, sw=3)
    d.note(690, 215, "① EndAction() (이동 페이즈 종료)", fs=15, color=RED, anchor="topleft", align="left")

    d.arrow([(1460, 370), (1460, 900)], color=RED, sw=3)
    d.note(1200, 580, "③ ResolveMonsterMovement()\n(몬스터 이동 실행 → 플레이어 행동 페이즈)", fs=15, color=RED, anchor="topleft", align="left")

    d.arrow([(1300, 955), (920, 955)], color=RED, sw=3)
    d.note(1110, 965, "② EndAction() (행동 페이즈 종료)\n턴 끝 효과 처리: 상태이상 · 저주 · 유물", fs=15, color=RED, anchor="top")

    d.box(600, 670, 320, 100, "FinishMonsterActionTurnBoundary", sub="턴 경계 진입 (현재 턴 종료 → 다음 턴 시작)", fs=16, sfs=13)
    d.arrow([(760, 900), (760, 770)], color=RED, sw=3)
    d.note(780, 810, "④ ResolveMonsterAction()\n(몬스터 공격 실행 후)", fs=15, color=RED, anchor="topleft", align="left")

    d.field(60, 420, 470, 560, "BeginNextOverallTurn (턴 경계 16단계 · 순서 고정)",
            ["AdvanceOverallTurnCounterStep  (턴 번호 +1)", "ClearPlayerBlockStep  (방어도 소거)",
             "ResolveFieldObjectTickStep  (필드 오브젝트 틱)", "ApplyActiveEffectTurnStart  (상태이상 틱)",
             "RefillKiForNewTurnStep  (기 회복)", "ResolveTurnStartRelicTriggers  (유물 턴 시작 훅)",
             "ApplyCarriedMovementBonusStep  (예약 소진)", "…  (AdvanceAndExpirePlayerProps 등)",
             "RefreshVisionForNewTurnStep  (시야 갱신)", "RefreshMonsterIntentStep  (몬스터 예고 갱신)",
             "EnterPlayerMovementPhaseStep  (PlayerMovement 진입)"], bg="#ffd8a8", lfs=15, align="left")
    d.arrow([(600, 720), (530, 720)], color=RED, sw=3)
    d.note(565, 690, "⑤", fs=17, color=RED)

    d.arrow([(300, 420), (300, 315), (600, 315)], color=RED, sw=3)
    d.note(320, 330, "⑥ 16단계 종료 → PlayerMovement", fs=15, color=RED, anchor="topleft", align="left")

    d.box(60, 1040, 470, 80, "StartPlayerTurn", sub="턴 경계 뒤 새 손패 드로우 시작", fs=20, sfs=13)
    d.box(60, 1170, 470, 90, "DrawNewTurnHands", sub="이동덱 · 행동덱 손패 채우기 (정원 + 유지 카드 수)", fs=20, sfs=13)
    d.arrow([(300, 980), (300, 1040)], color=GREEN, sw=3)
    d.arrow([(300, 1120), (300, 1170)], color=GREEN, sw=3)
    d.note(320, 995, "⑦", fs=17, color=GREEN, anchor="topleft", align="left")

    d.field(1700, 420, 440, 330, "PendingEffects (다음 턴 발효 예약)",
            ["agility  (민첩 · Amount, Turns)", "selfImmobilize  (자기 속박 · Amount, Turns)",
             "provokeStrength  (도발 강화 · Amount, Turns)", "nextTurnKiPenalty  (다음 턴 기 감소)",
             "", "Take*  (턴 경계에서 꺼내며 비운다)"], bg="#b2f2bb", lfs=15, align="left")
    d.arrow([(1620, 935), (1660, 935), (1660, 600), (1700, 600)], color=BLUE, sw=2.5)
    d.note(1640, 765, "⑧ 카드 · 유물이\n예약 기록", fs=15, color=BLUE, anchor="topleft", align="left")
    d.arrow([(1800, 420), (1800, 110), (200, 110), (200, 420)], color=BLUE, sw=2.5)
    d.note(1000, 82, "⑨ 턴 경계 단계가 Take* 로 예약을 꺼내 적용", fs=15, color=BLUE, anchor="top")

    d.box(1700, 900, 440, 110, "Victory / Defeat", sub="CheckTerminalOutcomeStep (해소마다 승패 확인)", fs=24, sfs=14, style="dashed")
    d.arrow([(1620, 985), (1700, 985)], color=BLACK, sw=2, style="dashed")
    d.note(1640, 1020, "⑩", fs=17)

    d.legend(1700, 1080, 440, [(RED, "solid", "페이즈 전환 (공개 진입점 3개)"), (GREEN, "solid", "손패 드로우"),
                               (BLUE, "solid", "예약 기록 / 적용"), (BLACK, "dashed", "승패 판정")])
    emit(d, "2.TurnFlow")


# =============================================================================
# 3. 몬스터 AI 계획과 예고
# =============================================================================
def d3():
    d = Diagram("ai", 2200, 1600, seed=33)
    d.title("몬스터 AI 계획과 예고")

    d.zone(60, 140, 540, 940, "입력 (읽기 전용)", bg=BG_BLUE)
    d.field(90, 200, 480, 400, "IMonsterPlanningContext (전투 정보 · CombatState 가 구현)",
            ["Map · PlayerCoord  (맵 · 플레이어 좌표)", "RuntimeStates  (칸 상태)", "MonsterActionOrder()  (행동 순서)",
             "ClassifyMonsterActivity  (활성 / 휴면)", "IsMonsterMovementBlocked / Rooted  (이동 제한)",
             "IsPlayerHiddenFromMonsters  (은신)", "IsMonsterSenseBlindedAt  (실명)",
             "GetBehaviorProfileRef  (행동 프로파일 · 매복 등)"], bg="#a5d8ff", fs=16, lfs=14, align="left")
    d.field(90, 700, 480, 200, "MonsterFsmContext (몬스터별 판단 재료)",
            ["DistanceToPlayer  (플레이어까지 거리)", "PlayerHidden  (플레이어가 숨었는가)",
             "PlayerIsDead  (플레이어 사망)", "MonsterCoord  (몬스터 좌표)"], bg="#a5d8ff", fs=16, lfs=14, align="left")
    d.arrow([(330, 600), (330, 700)], color=BLUE, sw=2.5)
    d.note(350, 630, "② CreateMonsterFsmContext (몬스터별로 생성)", fs=14, color=BLUE, anchor="topleft", align="left")

    d.zone(660, 140, 700, 1400, "MonsterAiPlanner (계획기 · 몬스터마다 위 → 아래로 한 번)", bg=BG_GREEN)
    ys = [220, 400, 580, 780, 960, 1120]
    d.box(700, ys[0], 620, 120, "① RefreshAllIntents", sub="행동 순서대로 몬스터를 돌며 계획\nreservedDestinations (앞 몬스터의 목적지 모음)", fs=22, sfs=13)
    d.box(700, ys[1], 620, 120, "② SelectMovementIntent", sub="이동 의도 결정 (if/else 한 함수)\nMonsterFsmMemory.State: Patrol · Chase · Attack · Search · Alert · Return", fs=22, sfs=13)
    d.box(700, ys[2], 620, 130, "③ ChooseEnemyMovementStep", sub="목적지 선택 — HexPathfinder.FindPath\n예약된 목적지 = temporaryBlocked (막힌 칸으로 취급)", fs=22, sfs=13)
    d.box(700, ys[3], 620, 120, "④ SelectWeightedAttackPattern", sub="공격 패턴 추첨 — monster_attack_patterns.csv 가중치\n난수 = 시드 스트림 4", fs=22, sfs=13)
    d.box(700, ys[4], 620, 100, "⑤ TryPlanLeapAttack", sub="도약 공격 — 착지 칸 · 패턴 결정", fs=22, sfs=13)
    d.box(700, ys[5], 620, 130, "⑥ monster.TurnPlan = new MonsterTurnPlan(…)", sub="계획 커밋 — 이동 의도 · 목적지 · 조준 · 도약", fs=20, sfs=13)
    for y0, y1 in ((340, 400), (520, 580), (710, 780), (900, 960), (1060, 1120)):
        d.arrow([(1010, y0), (1010, y1)], color=RED, sw=3)

    d.arrow([(570, 850), (640, 850), (640, 460), (700, 460)], color=BLUE, sw=2.5)
    d.note(100, 908, "③ MonsterFsmContext → ② SelectMovementIntent (거리 · PlayerHidden)", fs=14, color=BLUE, anchor="topleft", align="left")

    d.arrow([(1600, 140), (1600, 100), (330, 100), (330, 200)], color=BLUE, sw=2.5)
    d.note(960, 78, "① CombatState 가 인터페이스를 구현 — 계획기는 이 밖의 상태를 건드리지 않는다", fs=15, color=BLUE, anchor="top")

    d.arrow([(1320, 1215), (1370, 1215), (1370, 280), (1320, 280)], color=BLACK, sw=2)
    d.note(1010, 1275, "⑨ 목적지를 reservedDestinations 에 추가 → 다음 몬스터는 ①부터", fs=14, anchor="top")

    d.zone(1520, 140, 620, 700, "CombatState (계획의 소비자 둘)", bg=BG_ORANGE)
    d.box(1560, 220, 540, 160, "GetMonsterIntentPreviews", sub="화면에 보여줄 예고\nTurnPlan 을 그대로 펼친다 — AI 재실행 없음", fs=22, sfs=13)
    d.box(1560, 560, 540, 160, "ResolveMonsterMovementStep\nResolveMonsterAttackStep", sub="계획 실행 — 이동 · 공격 해소", fs=20, sfs=13)
    d.arrow([(1320, 1160), (1410, 1160), (1410, 300), (1560, 300)], color=GREEN, sw=3)
    d.note(1415, 330, "⑩ 예고", fs=15, color=GREEN, anchor="topleft", align="left")
    d.arrow([(1320, 1200), (1450, 1200), (1450, 640), (1560, 640)], color=RED, sw=3)
    d.note(1455, 670, "⑪ 실행", fs=15, color=RED, anchor="topleft", align="left")

    d.zone(1520, 900, 620, 520, "공격 범위 형상", bg=BG_YELLOW)
    d.box(1560, 960, 280, 70, "attack_shapes.csv", sub="형상 저작 원본", fs=18, sfs=12)
    d.arrow([(1700, 1030), (1700, 1080)], color=BLACK, sw=2)
    d.box(1560, 1080, 540, 130, "AttackShapeLibrary", sub="형상 로더 — 정동(East) 기준 오프셋을\nRotateSteps((6 − dir) % 6) 로 방향에 맞춰 회전", fs=22, sfs=13)
    d.field(1560, 1250, 540, 130, "AttackShapeAdjacency (몸체 칸과의 관계)",
            ["Full · None · Open · Body · BodyShell"], bg="#ffec99", fs=16, lfs=15)
    d.arrow([(1560, 1145), (1490, 1145), (1490, 870), (1320, 870)], color=BLUE, sw=2.5, style="dashed")
    d.note(1100, 908, "⑫ 형상 조회", fs=14, color=BLUE, anchor="top")

    d.legend(60, 1130, 540, [(RED, "solid", "계획 파이프라인 / 실행"), (BLUE, "solid", "읽기 전용 입력"),
                             (GREEN, "solid", "예고 (계획 그대로)"), (BLACK, "solid", "다음 몬스터로")])
    emit(d, "3.MonsterAi")


# =============================================================================
# 4. 카드 데이터와 카드 클래스
# =============================================================================
def d4():
    d = Diagram("cards", 2200, 1250, seed=44)
    d.title("카드 데이터와 카드 클래스")

    d.zone(60, 140, 640, 560, "데이터 (Editor 베이크)", bg=BG_BLUE)
    d.field(100, 200, 560, 130, "cards.csv (표시 · 밸런스 값만)",
            ["id · name · type · cost · range · shape · damage · …", "규칙 로직 없음"], bg="#a5d8ff", fs=16, lfs=14)
    d.box(100, 360, 560, 70, "CardCatalogCsvImporter", sub="CSV → 에셋 베이크 (Editor)", fs=20, sfs=12)
    d.box(100, 460, 560, 80, "CardCatalogAsset", sub="ScriptableObject — 카드 클래스 없는 id 는 거부", fs=20, sfs=13)
    d.box(100, 570, 560, 70, "CardCatalogDefinition", sub="런타임 카드 카탈로그", fs=20, sfs=12)
    d.arrow([(380, 330), (380, 360)], color=BLUE, sw=2.5)
    d.arrow([(380, 430), (380, 460)], color=BLUE, sw=2.5)
    d.arrow([(380, 540), (380, 570)], color=BLUE, sw=2.5)
    d.note(400, 335, "①", fs=16, color=BLUE, anchor="topleft", align="left")

    d.box(100, 780, 560, 120, "PlayerDeckData", sub="런 동안 보유한 카드 목록\nMovementCards · ActionCards", fs=22, sfs=13)
    d.arrow([(380, 640), (380, 780)], color=BLACK, sw=2.5)
    d.note(400, 695, "② 런 시작", fs=15, anchor="topleft", align="left")

    d.zone(780, 140, 640, 1060, "CombatState", bg=BG_ORANGE)
    d.field(820, 220, 560, 200, "MovementDeck : CardDeckState (이동 덱)",
            ["DrawPile  (뽑을 더미)", "Hand  (손패)", "DiscardPile  (버림 더미)", "RemovedPile  (소멸 더미)", "셔플 난수 = 시드 스트림 8"],
            bg="#ffd8a8", fs=16, lfs=14, align="left")
    d.field(820, 460, 560, 200, "ActionDeck : CardDeckState (행동 덱)",
            ["DrawPile  (뽑을 더미)", "Hand  (손패)", "DiscardPile  (버림 더미)", "RemovedPile  (소멸 더미)", "셔플 난수 = 시드 스트림 9"],
            bg="#ffd8a8", fs=16, lfs=14, align="left")
    d.box(820, 720, 560, 90, "DrawNewTurnHands", sub="턴마다 손패 채우기 (정원 + 유지 카드 수 − 현재 손패)", fs=20, sfs=13)
    d.box(820, 870, 560, 100, "연산 메서드", sub="이동 · 공격 · 방어 · 정찰 · 유틸리티 — 피해 · 이동 · 상태 적용", fs=20, sfs=13)
    d.box(820, 1030, 560, 110, "ConsumePlayedCard", sub="다 쓴 카드의 처분 (단일 지점)\nDisposeAfterPlay → Discard / Exile / HandledByRule", fs=20, sfs=13)

    d.arrow([(660, 800), (740, 800), (740, 320), (820, 320)], color=BLACK, sw=2.5)
    d.arrow([(660, 840), (760, 840), (760, 560), (820, 560)], color=BLACK, sw=2.5)
    d.note(540, 748, "③ 전투 시작 · 셔플", fs=14, anchor="topleft", align="left")

    d.arrow([(1100, 720), (1100, 660)], color=RED, sw=3)
    d.note(1120, 680, "④ 손패 드로우", fs=15, color=RED, anchor="topleft", align="left")

    d.zone(1500, 140, 640, 1060, "카드 클래스 (Combat.Runtime/Cards)", bg=BG_GREEN)
    d.box(1540, 220, 560, 100, "CardBehaviorRegistry.Resolve(card)", sub="카드 id → 카드 클래스 인스턴스 (종류당 1개 · 상태 없음)", fs=20, sfs=13)
    d.field(1540, 370, 560, 420, "CardBehavior (카드 클래스의 추상 기반)",
            ["규칙 훅 (기본은 no-op)", "TryResolveMoveDestination  (이동 목적지)", "TryApplyDefend  (방어)",
             "TryApplyUtility  (유틸리티)", "ApplyAfterScoutReveal  (정찰 후속)", "GetAttackDamage  (공격 피해)",
             "DisposeAfterPlay  (처분) · …", "", "선언", "Disposal · RetainOnTurnEnd · Keywords · Upgrade"],
            bg="#b2f2bb", fs=16, lfs=14, align="left")
    d.box(1540, 840, 560, 120, "A01_Sweep : BasicAttackCard", sub="카드 클래스 예시 — 59개 · 한 파일 한 클래스 · 등록 한 줄", fs=20, sfs=13)
    d.arrow([(1820, 790), (1820, 840)], color=BLACK, sw=2, style="dashed")
    d.note(1820, 1010, "카드 클래스는 화면 연출을 모른다\n(CombatState 연산 메서드만 호출)", fs=16, color=RED)

    d.arrow([(1380, 560), (1460, 560), (1460, 270), (1540, 270)], color=RED, sw=3)
    d.note(1392, 380, "⑤\n카드\n사용", fs=15, color=RED, anchor="topleft", align="left")
    d.arrow([(1820, 320), (1820, 370)], color=RED, sw=3)
    d.note(1840, 330, "⑥ 훅 호출", fs=15, color=RED, anchor="topleft", align="left")
    d.arrow([(1540, 700), (1460, 700), (1460, 920), (1380, 920)], color=RED, sw=3)
    d.note(1395, 938, "⑦ 훅 →\n연산 메서드", fs=14, color=RED, anchor="topleft", align="left")
    d.arrow([(1100, 970), (1100, 1030)], color=GREEN, sw=3)
    d.note(1120, 985, "⑧", fs=16, color=GREEN, anchor="topleft", align="left")
    d.arrow([(1380, 1085), (1440, 1085), (1440, 640), (1380, 640)], color=GREEN, sw=3)
    d.note(1400, 1100, "⑨ Discard → DiscardPile · Exile → RemovedPile", fs=14, color=GREEN, anchor="topleft", align="left")

    d.arrow([(660, 500), (720, 500), (720, 105), (1820, 105), (1820, 220)], color=BLUE, sw=2, style="dashed")
    d.note(1270, 80, "⑩ 베이크 시 Registry.Get(id) 로 카드 클래스 존재 검사", fs=15, color=BLUE, anchor="top")

    d.legend(60, 1000, 640, [(BLUE, "solid", "데이터 → 카탈로그"), (BLACK, "solid", "덱 구성"),
                             (RED, "solid", "카드 사용 → 연산"), (GREEN, "solid", "처분"), (BLUE, "dashed", "베이크 검증")])
    emit(d, "4.CardsAndDecks")


# =============================================================================
# 5. 암시야 정보 처리와 렌더
# =============================================================================
def d5():
    d = Diagram("fog", 2200, 1360, seed=55)
    d.title("암시야 정보 처리와 렌더")

    d.zone(60, 140, 560, 560, "CombatState", bg=BG_ORANGE, label_pos="topleft")
    d.box(100, 200, 480, 90, "RefreshPlayerVision", sub="밝힐 칸 결정", fs=24, sfs=13)
    d.field(100, 330, 480, 250, "세 소스의 합집합 → revealed",
            ["GetEffectivePlayerVisionRange  (시야 반경)", "FieldObjects  (횃불 등 필드 오브젝트)",
             "scoutRevealedThisTurn  (이번 턴 정찰 · 턴 끝까지 유지)"], bg="#ffd8a8", fs=16, lfs=14, align="left")
    d.arrow([(340, 290), (340, 330)], color=BLACK, sw=2)
    d.arrow([(340, 100), (340, 200)], color=RED, sw=3)
    d.note(360, 112, "① RefreshVisionForNewTurnStep (턴 경계) · 이동 뒤", fs=15, color=RED, anchor="topleft", align="left")

    d.zone(700, 140, 760, 1060, "Map.Runtime (순수 C#)", bg=BG_BLUE)
    d.box(740, 200, 680, 80, "HexVisibilityRuntime", sub="칸별 시야 단계 관리", fs=26, sfs=13)
    d.field(740, 310, 680, 200, "집합 4",
            ["states  (칸별 단계 Unknown / Hinted / Revealed · 저장 대상)", "temporaryRevealed  (이번 갱신에 보인 칸)",
             "permanentlyRevealed  (영구히 밝혀진 칸)", "trapRevealed  (함정이 드러난 칸)"], bg="#a5d8ff", fs=16, lfs=14, align="left")
    d.box(740, 560, 320, 100, "ForceVisibility", sub="단계 내리기 허용 — 세이브 복원 전용 (private)", fs=22, sfs=12)
    d.box(1100, 560, 320, 100, "SetVisibility", sub="단계 올리기만 (단조) · Version++", fs=22, sfs=12)
    d.arrow([(900, 560), (900, 510)], color=BLACK, sw=2, style="dashed")
    d.arrow([(1260, 560), (1260, 510)], color=BLACK, sw=2)
    d.note(1280, 520, "③", fs=16, anchor="topleft", align="left")
    d.note(1080, 690, "Unknown  →  Hinted  →  Revealed", fs=18, color=GRAY)
    d.box(740, 760, 680, 90, "GetSafeCellInfo(coord)", sub="단계에 맞게 거른 칸 정보 반환", fs=24, sfs=13)
    d.field(740, 890, 680, 260, "HexVisibilitySafeCellInfo (단계별로 거른 정보)",
            ["Unknown  (좌표만)", "Hinted  (지형 · 이동 비용 · 걷기 가능 — EventId · LandmarkId 는 비움)",
             "Revealed  (전부 · TileDefinitionId 포함)", "TrapRevealed  (별도 축)"], bg="#a5d8ff", fs=16, lfs=14, align="left")
    d.arrow([(1080, 850), (1080, 890)], color=BLACK, sw=2)
    d.arrow([(1080, 510), (1080, 760)], color=BLACK, sw=1.5, style="dotted")

    d.arrow([(580, 450), (660, 450), (660, 410), (740, 410)], color=RED, sw=3)
    d.note(586, 462, "② Refresh\nTemporary\nRevealedCells", fs=13, color=RED, anchor="topleft", align="left")

    d.zone(1540, 140, 600, 460, "Unity 소비 ① — 정보 표시", bg=BG_GREEN)
    d.box(1580, 200, 520, 80, "CombatVisibilityPresenter", sub="칸 툴팁", fs=20, sfs=13)
    d.box(1580, 310, 520, 80, "TacticalMinimapView", sub="미니맵", fs=20, sfs=13)
    d.box(1580, 420, 520, 100, "MapObjectVisualRegistry", sub="ShouldShowForVisibility (오브젝트 표시 여부)", fs=20, sfs=13)
    d.arrow([(1580, 240), (1500, 240), (1500, 800), (1420, 800)], color=BLUE, sw=3)
    d.arrow([(1580, 350), (1500, 350)], color=BLUE, sw=3, head=False)
    d.arrow([(1580, 470), (1500, 470)], color=BLUE, sw=3, head=False)
    d.note(1510, 640, "④ 거른\n정보만", fs=15, color=BLUE, anchor="topleft", align="left")

    d.zone(1540, 680, 600, 640, "Unity 소비 ② — 화면 렌더", bg=BG_GREEN)
    d.box(1580, 740, 520, 150, "VisibilityLightingMaskService", sub="칸 단계 → 바이트 마스크 텍스처\n바뀐 슬롯 없으면 업로드 생략", fs=20, sfs=13)
    d.box(1580, 950, 520, 80, "_SP_VisibilityMask", sub="셰이더 전역 텍스처", fs=18, sfs=12)
    d.box(1580, 1090, 520, 200, "MapVisibilityLit.shader", sub="URP Lit 변형\n월드 좌표 → 마스크 UV 샘플 (셀 스냅)\n출력 전 NaN 제거 (max / min)", fs=20, sfs=13)
    d.arrow([(1580, 830), (1420, 830)], color=BLUE, sw=3)
    d.note(1445, 845, "⑤", fs=16, color=BLUE, anchor="topleft", align="left")
    d.arrow([(1840, 890), (1840, 950)], color=GREEN, sw=3)
    d.arrow([(1840, 1030), (1840, 1090)], color=GREEN, sw=3)
    d.note(1860, 1040, "⑥", fs=16, color=GREEN, anchor="topleft", align="left")

    d.box(100, 800, 480, 100, "세이브 (CombatSuspendData)", sub="states 만 저장", fs=20, sfs=13, style="dashed")
    d.arrow([(580, 850), (660, 850), (660, 610), (740, 610)], color=BLACK, sw=2, style="dashed", start_head=True)
    d.note(100, 906, "⑦ 저장 · 복원 (복원은 ForceVisibility 로 재설정)", fs=14, anchor="topleft", align="left")

    d.legend(60, 980, 560, [(RED, "solid", "시야 갱신"), (BLACK, "solid", "상태 쓰기"), (BLUE, "solid", "거른 정보 조회"),
                            (GREEN, "solid", "렌더"), (BLACK, "dashed", "저장 / 복원")])
    emit(d, "5.FogOfWar")


# =============================================================================
# 6. 세이브와 시드 재현
# =============================================================================
def d6():
    d = Diagram("seed", 2200, 1500, seed=66)
    d.title("세이브와 시드 재현")

    d.zone(60, 140, 600, 300, "Flow", bg=BG_ORANGE)
    d.box(100, 200, 520, 200, "MainGameplayController", sub="런 시드 발급\n디버그 지정값 또는 Guid.NewGuid().GetHashCode()\n배치 랜덤화가 꺼진 스테이지에서도 발급", fs=24, sfs=14)
    d.field(60, 520, 600, 560, "RunSeedStreams (스트림 번호표 · 추가만 허용)",
            ["0  MonsterPlacement  (몬스터 배치 · 원시 시드)", "1  Traps  (함정)", "2  Chests  (상자)", "3  Services  (서비스 오브젝트)",
             "4  MonsterAttackPattern  (몬스터 공격 패턴)", "5  CombatJudgement  (전투 판정)", "6  BossProps  (보스 기물)",
             "7  Rewards  (보상)", "8  MovementDeckShuffle  (이동 덱 셔플)", "9  ActionDeckShuffle  (행동 덱 셔플)", "",
             "Derive(runSeed, stream)  (스트림 시드 파생)"], bg="#a5d8ff", fs=16, lfs=15, align="left")
    d.arrow([(360, 400), (360, 520)], color=RED, sw=3)
    d.note(380, 445, "① runSeed", fs=16, color=RED, anchor="topleft", align="left")

    d.zone(760, 140, 1380, 440, "CountingRandom ×7 (뽑은 횟수 Consumed 를 세는 난수)", bg=BG_BLUE)
    d.box(800, 210, 300, 100, "pushRng", sub="스트림 5 (전투 판정)", fs=20, sfs=13)
    d.box(1130, 210, 300, 100, "monsterAttackPatternRng", sub="스트림 4 (공격 패턴 추첨)", fs=18, sfs=13)
    d.box(1460, 210, 300, 100, "attackDamageJitterRng", sub="스트림 4′ (피해 변주)", fs=18, sfs=13)
    d.box(1790, 210, 300, 100, "bossPropRng", sub="스트림 6 (보스 기물)", fs=20, sfs=13)
    d.box(800, 380, 300, 100, "movementShuffleRng", sub="스트림 8 (이동 덱 셔플)", fs=18, sfs=13)
    d.box(1130, 380, 300, 100, "actionShuffleRng", sub="스트림 9 (행동 덱 셔플)", fs=18, sfs=13)
    d.box(1460, 380, 630, 100, "SeededRewardRandom", sub="스트림 7 (보상) · MapCombatController 소유", fs=20, sfs=13)
    d.note(1450, 525, "③ 전투 중 난수를 뽑을 때마다 각 인스턴스의 Consumed 증가", fs=16, color=RED)
    d.arrow([(660, 700), (730, 700), (730, 350), (760, 350)], color=RED, sw=3)
    d.note(745, 598, "② Derive →\n인스턴스 7개 생성", fs=15, color=RED, anchor="topleft", align="left")

    d.zone(760, 660, 1380, 420, "세이브 데이터", bg=BG_GREEN)
    d.rect(800, 710, 1300, 340, stroke=BLACK, bg="#d3f9d8", fill="solid", sw=1.5)
    d.text(1450, 722, "CombatSuspendEnvelope  —  RewardCursor  (보상 난수 커서 · 스트림 7)", fs=18, anchor="top")
    d.rect(840, 770, 1220, 250, stroke=BLACK, bg="#b2f2bb", fill="solid", sw=1.5)
    d.text(1450, 782, "CombatSuspendData  —  RngCursors ×6  (Judgement · AttackPattern · DamageJitter · BossProps · MovementShuffle · ActionShuffle)", fs=15, anchor="top")
    d.field(880, 830, 1140, 160, "MonsterRuntimeSaveData (몬스터마다)",
            ["AttackPatternIndex  (이미 굴린 공격 패턴)", "AttackDamageRollOffset  (이미 굴린 피해 변주)",
             "난수 내부 상태는 저장하지 않는다 — 굴린 값이 곧 상태"], bg="#8ce99a", fs=16, lfs=14)
    d.arrow([(960, 580), (960, 710)], color=BLUE, sw=3)
    d.note(980, 610, "④ CreateSuspendSnapshot\n(CaptureRngCursors + 굴린 값 저장)", fs=15, color=BLUE, anchor="topleft", align="left")
    d.arrow([(620, 300), (700, 300), (700, 730), (800, 730)], color=BLUE, sw=2.5)
    d.note(560, 470, "⑤ RewardCursor", fs=14, color=BLUE, anchor="topleft", align="left")

    d.box(1240, 1140, 400, 100, "RestoreFromSuspend", sub="복원 진입점", fs=22, sfs=13)
    d.arrow([(1440, 1080), (1440, 1140)], color=GREEN, sw=3)
    d.note(1460, 1095, "⑥ 커서 + 굴린 값 읽기", fs=15, color=GREEN, anchor="topleft", align="left")
    d.box(1240, 1300, 400, 120, "RestoreRngCursors", sub="같은 시드로 인스턴스를 새로 만들어\nFastForward(Consumed) 로 같은 자리까지", fs=22, sfs=13)
    d.arrow([(1440, 1240), (1440, 1300)], color=GREEN, sw=3)
    d.note(1460, 1255, "⑦", fs=16, color=GREEN, anchor="topleft", align="left")
    d.box(1700, 1300, 440, 120, "planner.RefreshAllIntents(\n preserveCommittedAttackRolls: true)", sub="예고의 경로 · 조준만 재계산 · 굴림 없음", fs=17, sfs=13)
    d.arrow([(1640, 1360), (1700, 1360)], color=GREEN, sw=3)
    d.note(1655, 1325, "⑧", fs=16, color=GREEN, anchor="topleft", align="left")

    d.field(60, 1140, 1100, 280, "재현 방식 둘",
            ["(A) 스트림 재현  — 시드 + 커서만 저장, 복원 때 FastForward 로 같은 자리에 선다",
             "      셔플 · 전투 판정 · 보스 기물 · 보상",
             "(B) 굴린 값 저장  — 이미 확정되어 플레이어에게 보인 값은 값 자체를 저장, 다시 굴리지 않는다",
             "      몬스터 공격 패턴 · 피해 변주 (예고 유지)"], bg="#ffec99", fs=16, lfs=15, align="left")
    emit(d, "6.SaveAndSeed")


# =============================================================================
# 7. 맵 배치 랜덤화
# =============================================================================
def d7():
    d = Diagram("place", 2200, 1500, seed=77)
    d.title("맵 배치 랜덤화")

    d.zone(60, 140, 560, 660, "저작 (맵 에디터)", bg=BG_ORANGE)
    d.field(100, 200, 480, 280, "HexSparseMapAuthoringSource (저작 데이터)",
            ["점유 슬롯  (objectRef 있음 · 고정 배치)", "예비 슬롯  (RandomizationGroup 태그 + objectRef 없음)",
             "IsRandomizationSpareSlot  (예비 슬롯 판정)", "몬스터 · 함정 · 서비스 슬롯 공통"], bg="#ffd8a8", fs=16, lfs=14, align="left")
    d.box(100, 530, 480, 80, "TryToHexMapData", sub="저작 데이터 → 맵 데이터 변환", fs=22, sfs=13)
    d.box(100, 660, 480, 80, "HexMapData (저작 원본)", fs=20)
    d.arrow([(340, 480), (340, 530)], color=BLACK, sw=2)
    d.arrow([(340, 610), (340, 660)], color=BLACK, sw=2)

    d.zone(700, 140, 700, 400, "CSV 3장 → 스테이지 프로파일", bg=BG_BLUE)
    d.box(740, 190, 620, 60, "stage_randomization.csv  (스테이지 프로파일)", fs=17)
    d.box(740, 260, 620, 50, "stage_randomization_pools.csv  (가중 풀)", fs=17)
    d.box(740, 320, 620, 50, "stage_randomization_bans.csv  (금지 조합)", fs=17)
    d.box(740, 410, 620, 100, "StageRandomizationProfile", sub="ThreatBudget (위협 예산) · SafeRadius (안전 반경) · RerollLimit (재롤 상한)\nDensityCap (밀도 상한) · EliteMin (정예 하한) · Pools · Bans …", fs=22, sfs=12)
    d.arrow([(1050, 370), (1050, 410)], color=BLUE, sw=2.5)
    d.note(1070, 375, "①", fs=16, color=BLUE, anchor="topleft", align="left")

    d.zone(700, 620, 700, 820, "HexMapPlacementRandomization.TryApplyProfile(source, baseMap, seed, profile)", bg=BG_GREEN, fs=19)
    d.box(740, 700, 620, 130, "① RandomizeWithProfile  (몬스터)", sub="그룹별 SampleWithoutReplacement (슬롯 비복원 추첨)\nWeightedPickWithRepeatDecay (뽑힌 종은 가중치 반감) · 원시 시드", fs=21, sfs=13)
    d.box(740, 880, 620, 110, "② RandomizeTraps  (함정)", sub="함정 위협 예산 · 시드 스트림 1", fs=21, sfs=13)
    d.box(740, 1040, 620, 110, "③ PlaceServices  (서비스 오브젝트)", sub="잡화점 · 캠핑카 · 시드 스트림 3 (상자보다 먼저)", fs=21, sfs=13)
    d.box(740, 1200, 620, 110, "④ ShuffleChests  (상자)", sub="최소 거리 · 시드 스트림 2", fs=21, sfs=13)
    d.arrow([(1050, 830), (1050, 880)], color=RED, sw=3)
    d.arrow([(1050, 990), (1050, 1040)], color=RED, sw=3)
    d.arrow([(1050, 1150), (1050, 1200)], color=RED, sw=3)
    d.note(1070, 838, "③", fs=16, color=RED, anchor="topleft", align="left")
    d.note(1070, 998, "④", fs=16, color=RED, anchor="topleft", align="left")
    d.note(1070, 1158, "⑤", fs=16, color=RED, anchor="topleft", align="left")
    d.note(1050, 1345, "시드: 몬스터 = 원시 시드 · 함정 1 · 서비스 3 · 상자 2  (DeriveSeed)", fs=15, color=GRAY)

    d.arrow([(1050, 510), (1050, 700)], color=RED, sw=3)
    d.note(1070, 590, "프로파일", fs=15, color=RED, anchor="topleft", align="left")
    d.arrow([(580, 700), (660, 700), (660, 765), (740, 765)], color=BLACK, sw=2.5)
    d.note(100, 746, "② baseMap + seed → TryApplyProfile", fs=15, anchor="topleft", align="left")

    d.field(1500, 700, 640, 300, "ValidateProfileAttempt (검증 게이트)",
            ["SafeRadius  (플레이어 안전 반경)", "BudgetMin ~ BudgetMax  (위협 합 범위)",
             "EliteMin · EliteMinDistance  (정예 하한 · 거리)", "MonsterMinKinds  (종 하한)",
             "DensityRadius / DensityCap  (밀도 상한)"], bg="#b2f2bb", fs=16, lfs=15, align="left")
    d.arrow([(1360, 740), (1500, 740)], color=GREEN, sw=3)
    d.note(1400, 705, "⑥ 검사", fs=15, color=GREEN, anchor="topleft", align="left")
    d.arrow([(1500, 960), (1440, 960), (1440, 800), (1360, 800)], color=GREEN, sw=3, style="dashed")
    d.note(1365, 1010, "실패 → 재롤 (상한 = RerollLimit)", fs=14, color=GREEN, anchor="topleft", align="left")

    d.box(1500, 1100, 640, 100, "HexMapData (랜덤화됨) → 전투", fs=22, bg=BG_YELLOW)
    d.arrow([(1360, 1255), (1440, 1255), (1440, 1150), (1500, 1150)], color=RED, sw=3)
    d.note(1450, 1180, "⑦", fs=16, color=RED, anchor="topleft", align="left")
    d.box(1500, 1260, 640, 100, "저작 원본으로 폴백", sub="MapCombatController.MapView — 재롤 상한 초과 · 프로파일 없음", fs=20, sfs=13, style="dashed")
    d.arrow([(2140, 850), (2180, 850), (2180, 1310), (2140, 1310)], color=BLACK, sw=2, style="dashed")
    d.note(2170, 1050, "⑧", fs=16, anchor="topleft", align="left")

    d.legend(1500, 140, 640, [(BLUE, "solid", "CSV → 프로파일"), (BLACK, "solid", "저작 데이터"),
                              (RED, "solid", "배치 단계 (순서 고정)"), (GREEN, "solid", "검증 / 재롤"), (BLACK, "dashed", "폴백")])
    emit(d, "7.Placement")


if __name__ == "__main__":
    d1(); d2(); d3(); d4(); d5(); d6(); d7()

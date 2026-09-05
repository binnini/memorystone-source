using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 철조각 사슬(scrap-chain · §21.8 제안 2)의 규칙 계약: 살포 다음 턴에 스포크 예고 → 그 다음
    /// 몬스터 페이즈에 예고 칸 그대로 명중(피해+속박) · 예고 후 철조각을 부수면 그 가닥만 무효 ·
    /// 개시는 턴 시작 래치를 읽어 mechanicId 순서 독립 · 서스펜드 왕복.
    /// </summary>
    public sealed class BossScrapChainTests
    {
        private const string BossDefinitionId = "M002";
        private const string BossUnitId = "boss-01";
        private const int BossBaseHp = 40;
        private const int ChainDamage = 6;

        [Test]
        public void ChainTelegraphsTheTurnAfterTheVolleyAndHitsTheFrozenCells_RegardlessOfMechanicOrder()
        {
            // 🔴 개시를 "지금 기물이 있나"로 물으면 살포 턴(T1)의 답이 mechanicId 순서에 갈린다 —
            // iron-scrap 뒤에 돌면 그 턴에 깔린 기물이 보이고, 앞에 돌면 안 보인다. 턴 시작 래치가
            // 개시를 T2로 고정한다(§20.3 순서 독립 계약).
            foreach (var mechanicIds in new[] { "iron-scrap|scrap-chain", "scrap-chain|iron-scrap" })
            {
                var state = CreateChainBossState(mechanicIds);

                RunFullTurn(state); // T1: 살포 — 사슬은 아직이다.
                Assert.That(state.GetBossScrapChainTelegraphCells(), Is.Empty,
                    $"[{mechanicIds}] 살포 턴에는 예고하지 않는다 — 래치가 개시를 다음 턴으로 고정한다.");

                RunFullTurn(state); // T2: 스포크 예고.
                var telegraph = state.GetBossScrapChainTelegraphCells();
                Assert.That(telegraph, Is.Not.Empty, $"[{mechanicIds}] 철조각이 판에 있는 다음 턴에 예고가 걸린다.");

                var standable = FindStandableCell(state, telegraph);
                Assert.That(state.TryDebugMovePlayer(standable), Is.True, state.LastFailureReason);

                var hpBefore = state.Player.Hp;
                RunFullTurn(state); // T3: 명중.
                Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - ChainDamage),
                    $"[{mechanicIds}] 예고 칸에 서 있으면 사슬 피해를 받는다(예고=명중).");
                Assert.That(
                    state.ActiveEffects.Any(effect =>
                        effect.Kind == StatusEffectKind.Immobilize && effect.TargetUnitId == "player"),
                    Is.True,
                    $"[{mechanicIds}] 사슬에 맞으면 속박이 걸린다 — '사슬에 묶인다'는 어휘 그대로다.");
                Assert.That(state.GetBossScrapChainTelegraphCells(), Is.Empty,
                    $"[{mechanicIds}] 명중 후 예고는 지워진다.");
            }
        }

        [Test]
        public void HitResolutionProjectsTheLiveStrandsForPresentationAndClearsThemNextResolve()
        {
            // 2026-09-05 후속 #7: 라인 렌더러가 보스→철조각 셀을 따라 긋는 원천. 예고 셀과 같은 셀·같은 순서.
            var state = CreateChainBossState("iron-scrap|scrap-chain");
            RunFullTurn(state); // T1 살포
            RunFullTurn(state); // T2 예고
            var telegraph = state.GetBossScrapChainTelegraphCells().ToList();
            Assert.That(telegraph, Is.Not.Empty);
            Assert.That(state.LastBossScrapChainHits, Is.Empty, "예고만 걸린 결의엔 명중 투영이 없다.");

            var standable = FindStandableCell(state, telegraph);
            Assert.That(state.TryDebugMovePlayer(standable), Is.True, state.LastFailureReason);
            RunFullTurn(state); // T3 명중

            Assert.That(state.LastBossScrapChainHits, Has.Count.EqualTo(1));
            var hit = state.LastBossScrapChainHits[0];
            Assert.That(hit.BossUnitId, Is.EqualTo(BossUnitId));
            Assert.That(hit.HitPlayer, Is.True);
            Assert.That(hit.PlayerCoord, Is.EqualTo(standable));
            Assert.That(hit.Strands.Count, Is.EqualTo(2), "살포 2개 → 가닥 2");
            Assert.That(hit.Strands.SelectMany(strand => strand).Distinct(), Is.EquivalentTo(telegraph), "가닥 셀 = 예고 셀(예고=명중).");
            foreach (var strand in hit.Strands)
            {
                Assert.That(strand.Last(), Is.Not.EqualTo(hit.BossCoord), "가닥은 보스에서 철조각 쪽으로 끝난다.");
            }

            RunFullTurn(state); // T4: 다음 결의가 투영을 비운다.
            Assert.That(state.LastBossScrapChainHits, Is.Empty, "투영은 「직전 결의」만 든다.");
        }

        [Test]
        public void DestroyingAPropVoidsOnlyItsStrand()
        {
            var state = CreateChainBossState("iron-scrap|scrap-chain");
            RunFullTurn(state); // T1 살포
            RunFullTurn(state); // T2 예고
            var strands = GetTrack(state).ScrapChainStrands;
            Assert.That(strands, Has.Count.EqualTo(2), "전제: 철조각 2개 = 가닥 2개.");

            var doomed = strands[0];
            var survivorCells = strands[1].Cells;
            // 예고 뒤 철조각 하나를 부순다(플레이어의 파괴 보상 시나리오).
            KillMonster(state, doomed.PropUnitId);

            var telegraph = state.GetBossScrapChainTelegraphCells();
            foreach (var cell in doomed.Cells.Except(survivorCells))
            {
                Assert.That(telegraph, Has.No.Member(cell),
                    "부순 철조각의 가닥은 예고에서 즉시 꺼진다 — 화면과 명중이 같은 생존 필터를 쓴다.");
            }

            // 무효가 된 가닥 위에 서 있어도 명중하지 않는다.
            var voidedOnly = doomed.Cells.Except(survivorCells)
                .Where(cell => CanStand(state, cell))
                .ToList();
            if (voidedOnly.Count > 0)
            {
                Assert.That(state.TryDebugMovePlayer(voidedOnly.First()), Is.True, state.LastFailureReason);
                var hpBefore = state.Player.Hp;
                RunFullTurn(state); // T3
                Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "무효 가닥 위는 안전하다.");
            }
        }

        [Test]
        public void SuspendRoundTripPreservesTheTelegraphedStrands()
        {
            var state = CreateChainBossState("iron-scrap|scrap-chain");
            RunFullTurn(state);
            RunFullTurn(state); // T2 예고
            var telegraphBefore = Sorted(state.GetBossScrapChainTelegraphCells());
            Assert.That(telegraphBefore, Is.Not.Empty);

            var snapshot = state.CreateSuspendSnapshot();
            var resumed = CreateChainBossState("iron-scrap|scrap-chain");
            resumed.RestoreFromSuspend(snapshot);

            Assert.That(Sorted(resumed.GetBossScrapChainTelegraphCells()), Is.EqualTo(telegraphBefore),
                "가닥(예고=명중 저장본)을 왕복하지 않으면 재개 직후 예고 없이 명중하거나 예고가 증발한다.");

            var standable = FindStandableCell(resumed, resumed.GetBossScrapChainTelegraphCells());
            Assert.That(resumed.TryDebugMovePlayer(standable), Is.True, resumed.LastFailureReason);
            var hpBefore = resumed.Player.Hp;
            RunFullTurn(resumed);
            Assert.That(resumed.Player.Hp, Is.EqualTo(hpBefore - ChainDamage), "복원된 예고도 그대로 명중한다.");
        }

        // --- helpers ------------------------------------------------------------------------------

        [Test]
        public void TelegraphDrawsOnItsOwnOverlayLayerInsteadOfTheGenericDangerHatch()
        {
            // 2026-09-05 결정 5: 사슬 예고는 전용 레이어(전격 노랑)로 그린다 — 붉은 공격 해치에 합치면 스포크가 안 읽힌다.
            var state = CreateChainBossState("iron-scrap|scrap-chain");
            RunFullTurn(state);
            RunFullTurn(state); // T2 예고
            var telegraph = state.GetBossScrapChainTelegraphCells();
            Assert.That(telegraph, Is.Not.Empty, "전제: 예고가 걸려 있다.");

            var presentation = new CombatOverlayPresentationBuilder().Build(new CombatOverlayPresentationRequest(
                state,
                state.Map,
                new CombatOverlayQuery(),
                isMoveSelectionActive: false,
                selectedTargetCardKind: null,
                reachableCoords: Enumerable.Empty<HexCoord>(),
                selectedTargetHighlightCoords: Enumerable.Empty<HexCoord>(),
                revealAllMapCellsInDebugMode: true,
                showPlayerMovementOverlay: true,
                showPlayerActionOverlay: true,
                showMonsterMoveOverlay: true,
                showMonsterAttackOverlay: true,
                showMonsterChaseOverlay: true));

            Assert.That(presentation.Layers.Count(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.BossScrapChainTelegraph), Is.EqualTo(1), "사슬 전용 레이어가 없다.");
            var chainLayer = presentation.Layers.Single(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.BossScrapChainTelegraph);
            Assert.That(chainLayer.Coords, Is.EquivalentTo(telegraph), "레이어 칸 = 규칙의 예고 칸(예고=명중 필터 공유).");
            if (presentation.Layers.Any(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterAttackIntent))
            {
                var danger = presentation.Layers.Single(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterAttackIntent);
                // 보스 일반 공격 예고와 겹치는 칸은 있을 수 있다 — 하지만 「사슬만」인 칸이 붉은 해치에 섞여 있으면 안 된다.
                var bossAttackCells = new CombatOverlayQuery().GetMonsterIntentAttackHighlightCells(state, state.Map, true, null).ToList();
                Assert.That(danger.Coords.Except(bossAttackCells), Is.Empty, "사슬 예고 칸이 일반 위험 해치로 새고 있다.");
            }
        }

        private static List<HexCoord> Sorted(IReadOnlyList<HexCoord> coords)
        {
            return coords.OrderBy(coord => coord.Q).ThenBy(coord => coord.R).ToList();
        }

        private static HexCoord FindStandableCell(CombatState state, IReadOnlyList<HexCoord> cells)
        {
            var standable = cells.Where(cell => CanStand(state, cell)).ToList();
            Assert.That(standable, Is.Not.Empty, "전제: 설 수 있는 예고 칸이 하나는 있어야 한다.");
            return standable.First();
        }

        private static bool CanStand(CombatState state, HexCoord cell)
        {
            return state.Monsters.All(monster => monster.Hp <= 0 || monster.Coord != cell);
        }

        private static BossPhaseTrack GetTrack(CombatState state)
        {
            // 3-B: 트랙은 BossEncounterState 소유 — 리플렉션 문자열 대신 internal 이음새로 집는다.
            return state.BossPhaseTracksForTests.Single();
        }

        private static void KillMonster(CombatState state, string monsterId)
        {
            var runtimeMonsters = (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            runtimeMonsters.Single(candidate => candidate.Id == monsterId).Combatant.ApplyDamage(999);
        }

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        /// <summary>
        /// 철조각 살포 + 사슬 콤보 보스. 살포는 첫 결의에서 바로 2개(성숙 3 · 주기 10 = 사슬 창이
        /// 실기와 같은 3턴), 사슬은 1페이즈부터 주기 4 · 피해 6 · 속박 1턴.
        /// </summary>
        private static CombatState CreateChainBossState(string mechanicIds)
        {
            const string ironScrap =
                "propId=P001;stackPerProp=1;maturityTurns=3;volleyIntervalTurns=10;volleyByPhase=2;" +
                "ringRadius=2;minSpacing=2;maxAlive=8";
            const string chain =
                "scrapChainPropId=P001;scrapChainPhaseMin=1;scrapChainIntervalTurns=4;" +
                "scrapChainDamage=6;scrapChainRootTurns=1";
            var playerCoord = new HexCoord(1, 0);
            var bossCoord = new HexCoord(3, 0);
            var profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + $",테스트 보스,TurnCount,{mechanicIds},{ironScrap};{chain},,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,1,0,,\n";

            return new CombatState(
                CombatState.CreateDemoMap(7),
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: "boss") },
                new CombatConfig(200, BossBaseHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "boss-scrap-chain-test-catalog",
                    "Boss Scrap Chain Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            BossDefinitionId,
                            "테스트 보스",
                            "test-melee",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: BossBaseHp,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("A100", "기본", 1, 0, 0, cooldownTurns: 0, phaseMin: 0)
                            }),
                        new MonsterCatalogEntry(
                            "P001",
                            "철조각",
                            "test-prop",
                            "P001",
                            detectionRange: 0,
                            movePerTurn: 0,
                            hp: 4,
                            attackSpeed: 0,
                            attackPatterns: System.Array.Empty<MonsterAttackPattern>())
                    }),
                bossCatalog: BossCatalogCsvConverter.Convert(
                    new BossCatalogCsvSource(profiles, phases, "boss-scrap-chain-test", "Boss Scrap Chain Test")));
        }
    }
}

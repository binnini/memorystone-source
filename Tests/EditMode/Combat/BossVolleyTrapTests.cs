using System;
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
    /// 2026-09-05 불가살 개편(사용자 확정 8건) — 철조각 살포의 페이즈별 주기 · 살포 동반 함정(둔화·기절·저주·터렛,
    /// 아레나 전역·은닉) · 살포 턴 방어막 · 알림 플로팅, 그리고 카탈로그 특성 「수호 재충전」의 규칙을 고정한다.
    /// 전부 로컬 픽스처(출하 CSV 불읽음). 값은 저작 임의값이며 규칙(주기·상한·알림 여부)만 잰다.
    /// </summary>
    public sealed class BossVolleyTrapTests
    {
        private const string BossDefinitionId = "M002";
        private const string PropDefinitionId = "M901";
        private const string TurretDefinitionId = "M902";
        private const string BossUnitId = "boss-01";
        private const int BossBaseHp = 40;
        private const int PropHp = 10;
        private const int MaturityTurns = 2;
        private const int PhaseOneInterval = 3;
        private const int PhaseTwoInterval = 5;
        private const int GuardBlock = 8;

        [Test]
        public void VolleyIntervalFollowsThePhaseListInsteadOfTheSingleInterval()
        {
            // 단일 주기 10과 페이즈별 3|5|5를 함께 저작 — 페이즈별 값이 이겨야 한다(Q18: 1페이즈만 빠르게).
            var state = CreateBossState(intervalByPhase: $"{PhaseOneInterval}|{PhaseTwoInterval}|{PhaseTwoInterval}", trapPool: "");
            RunFullTurn(state);
            var firstCastTurn = state.OverallTurnNumber;
            var castTurns = new List<int> { firstCastTurn };

            for (var i = 0; i < PhaseOneInterval * 2; i++)
            {
                var before = LivingProps(state).Count;
                RunFullTurn(state);
                if (state.LastBossPropVolleyCasts.Count > 0 && LivingProps(state).Count >= before)
                {
                    castTurns.Add(state.OverallTurnNumber);
                }
            }

            Assert.That(castTurns.Count, Is.GreaterThanOrEqualTo(3), "3주기 안에 두 번 더 살포돼야 한다.");
            Assert.That(castTurns[1] - castTurns[0], Is.EqualTo(PhaseOneInterval),
                "두 번째 살포는 페이즈별 주기(3) 뒤에 온다 — 단일 주기(10)를 읽었다면 아직 안 왔다.");
            Assert.That(castTurns[2] - castTurns[1], Is.EqualTo(PhaseOneInterval));
        }

        [Test]
        public void VolleyIsTelegraphedOneMonsterPhaseAheadAndLandsOnTheTelegraphedCells()
        {
            // 2026-09-05 실플레이 요구: 살포 예고. 주기 3이면 살포(T0) → T0+1(쿨 2→1) → T0+2(쿨 1→0, 예고) → T0+3 살포.
            var state = CreateBossState(intervalByPhase: $"{PhaseOneInterval}|{PhaseTwoInterval}|{PhaseTwoInterval}", trapPool: "");
            RunFullTurn(state); // T0 살포(예고 없이 즉시)
            Assert.That(state.GetBossPropVolleyTelegraphCells(), Is.Empty, "살포 직후엔 예고가 없다.");
            RunFullTurn(state); // T0+1
            Assert.That(state.GetBossPropVolleyTelegraphCells(), Is.Empty, "아직 쿨다운이 남았다.");
            RunFullTurn(state); // T0+2 — 첫 볼리 흡수(성숙 2) + 다음 살포 예고
            var telegraph = state.GetBossPropVolleyTelegraphCells().ToList();
            Assert.That(telegraph, Has.Count.EqualTo(2), "다음 페이즈 살포 개수(2)만큼 예고한다.");

            var overlay = new CombatOverlayPresentationBuilder().Build(BuildRequest(state, hoveredMonsterId: null));
            // 2026-09-05 후속 #1: 소환 예고(청록 점유)가 아니라 전용 붉은 해치 레이어. 위험 해치에 합치지도 않는다.
            Assert.That(overlay.Layers.Count(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.BossPropVolleyTelegraph), Is.EqualTo(1), "예고가 전용 살포 예고 레이어로 그려진다.");
            Assert.That(overlay.Layers.Single(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.BossPropVolleyTelegraph).Coords, Is.SupersetOf(telegraph));
            var summonCells = overlay.Layers.Where(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterSummonIntent).SelectMany(layer => layer.Coords).ToList();
            Assert.That(summonCells.Intersect(telegraph), Is.Empty, "소환 예고 레이어는 더 이상 빌리지 않는다.");
            Assert.That(CombatOverlayPresentation.TacticalLayers, Has.Member(SeoulPlayup.Map.Unity.HexOverlayLayer.BossPropVolleyTelegraph),
                "매 refresh 재구축 레이어라 전술 목록에 있어야 살포 뒤 해치가 지워진다.");

            RunFullTurn(state); // T0+3 살포
            Assert.That(LivingProps(state).Select(prop => prop.Coord), Is.EquivalentTo(telegraph), "예고=배치 — 예고한 칸에 그대로 놓인다.");
            Assert.That(state.GetBossPropVolleyTelegraphCells(), Is.Empty, "살포가 예고를 소비한다.");
        }

        [Test]
        public void VolleyTurnPlantsTheAuthoredTrapPoolHiddenAcrossTheArenaAndAnnouncesIt()
        {
            var events = new List<EffectResultEvent>();
            var state = CreateBossState(trapPool: "Slow:2+Curse:1+Stun:1+Turret:1");
            state.EffectResolved += resultEvent => events.Add(resultEvent);

            RunFullTurn(state);

            var traps = state.RuntimeTrapRefs;
            Assert.That(traps, Has.Count.EqualTo(5), state.LastBossArenaTrapReport);
            var kinds = traps.Select(trap => trap.Effects.Single().Kind).ToList();
            Assert.That(kinds.Count(kind => kind == HexTrapEffectKind.Slow), Is.EqualTo(2));
            Assert.That(kinds.Count(kind => kind == HexTrapEffectKind.Stun), Is.EqualTo(1), "기절 함정은 옛 trap-volley 가드와 달리 허용된다(전멸기 은퇴 · 결정 6).");
            var curse = traps.Single(trap => trap.Effects.Single().Kind == HexTrapEffectKind.InjectStatusCard).Effects.Single();
            Assert.That(new[] { "X07", "X05" }, Has.Member(curse.StatusCardId), "저주 함정은 저작 풀 중 한 장을 문다.");
            var turret = traps.Single(trap => trap.Effects.Single().Kind == HexTrapEffectKind.SpawnMonsters).Effects.Single();
            Assert.That(turret.MonsterDefinitionId, Is.EqualTo(TurretDefinitionId));

            // 아레나 전역: 옛 링(r2) 배치와 달리 거리 2에 묶이지 않는다 — 살포 링(r3 ± 1) 밖 칸에도 놓일 수 있어야 한다.
            var bossCoord = BossCoord(state);
            Assert.That(traps.All(trap => trap.Coord != state.PlayerCoord), "플레이어 발밑에는 심지 않는다.");
            Assert.That(traps.Select(trap => trap.Coord).Distinct().Count(), Is.EqualTo(traps.Count));
            foreach (var pair in traps.SelectMany((a, i) => traps.Skip(i + 1).Select(b => (a, b))))
            {
                Assert.That(pair.a.Coord.DistanceTo(pair.b.Coord), Is.GreaterThanOrEqualTo(2), "최소 간격 2.");
            }

            // 은닉(Q14): 살포 직후에도 발견 상태가 아니다 — 정찰 전엔 위치도 종류도 안 보인다.
            var visibility = (HexVisibilityRuntime)typeof(CombatState)
                .GetField("visibilityRuntime", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            Assert.That(traps.All(trap => !visibility.IsTrapRevealed(trap.Coord)), Is.True, "살포 턴의 함정은 숨어 있어야 한다.");

            // 알림(Q20 문안): 「철조각 ×N」과 「함정 설치!」가 보스 위 특성 알림 채널로 나간다.
            var announcements = events.Where(e => e.Kind == EffectKind.MonsterTraitTriggered && e.TargetUnitId == BossUnitId).ToList();
            var volley = announcements.SingleOrDefault(e => e.SourceRef == BossPropAnnouncements.VolleyRef);
            Assert.That(volley.SourceRef, Is.EqualTo(BossPropAnnouncements.VolleyRef), "살포 알림이 없다.");
            Assert.That(volley.Amount, Is.EqualTo(LivingProps(state).Count), "살포 알림의 amount = 실제로 놓인 철조각 수.");
            Assert.That(announcements.Count(e => e.SourceRef == BossPropAnnouncements.TrapsPlacedRef), Is.EqualTo(1), "「함정 설치!」 알림은 한 번.");
            Assert.That(MonsterTraitAnnouncement.TryGetText(BossPropAnnouncements.TrapsPlacedRef, 0, out var text), Is.True);
            Assert.That(text, Is.EqualTo("함정 설치!"));
            Assert.That(MonsterTraitAnnouncement.TryGetText(BossPropAnnouncements.VolleyRef, 4, out text), Is.True);
            Assert.That(text, Is.EqualTo("철조각 ×4"));

            // 방어막(§21.8 제안 4 후계): 살포 턴에 몸을 굳힌다.
            Assert.That(state.Monsters.Single(monster => monster.Id == BossUnitId).Block, Is.EqualTo(GuardBlock));
            Assert.That(bossCoord, Is.EqualTo(BossCoord(state)), "살포 턴에 보스는 움직이지 않았다(전제 확인).");
        }

        [Test]
        public void TurretTrapsSkipWhenLivingTurretsAlreadyReachTheCap()
        {
            var state = CreateBossState(trapPool: "Turret:2", turretMaxAlive: 1, extraMonsters: new[]
            {
                new MonsterConfig("turret-01", new HexCoord(0, 3), 3, definitionId: TurretDefinitionId, spawnRole: "monster")
            });

            RunFullTurn(state);

            Assert.That(state.RuntimeTrapRefs.Count(trap => trap.Effects.Single().Kind == HexTrapEffectKind.SpawnMonsters), Is.EqualTo(0),
                "살아있는 터렛이 상한(1)에 닿아 있으면 터렛 함정은 그 볼리에서 건너뛴다.");
        }

        [Test]
        public void TurretTrapCountsItsOwnVolleyAgainstTheCap()
        {
            var state = CreateBossState(trapPool: "Turret:3", turretMaxAlive: 2);

            RunFullTurn(state);

            Assert.That(state.RuntimeTrapRefs.Count(trap => trap.Effects.Single().Kind == HexTrapEffectKind.SpawnMonsters), Is.EqualTo(2),
                "같은 볼리 안에서도 상한을 넘겨 심지 않는다 — 밟히면 전부 터렛이 되므로 배치 시점에 센다.");
        }

        [Test]
        public void ArmedTrapCapTrimsTheVolleyInsteadOfOverfillingTheArena()
        {
            var state = CreateBossState(trapPool: "Slow:4", trapMaxArmed: 3);

            RunFullTurn(state);

            Assert.That(state.RuntimeTrapRefs, Has.Count.EqualTo(3), "무장 상한이 볼리를 자른다(저작 사고 가드).");
        }

        [Test]
        public void AbsorptionAnnouncesTheGainedStacks()
        {
            var events = new List<EffectResultEvent>();
            var state = CreateBossState(trapPool: "");
            state.EffectResolved += resultEvent => events.Add(resultEvent);

            RunFullTurn(state);
            var placed = LivingProps(state).Count;
            Assert.That(placed, Is.GreaterThan(0));
            for (var i = 0; i < MaturityTurns; i++)
            {
                RunFullTurn(state);
            }

            var absorbed = events.Where(e => e.Kind == EffectKind.MonsterTraitTriggered && e.SourceRef == BossPropAnnouncements.AbsorbedRef).ToList();
            Assert.That(absorbed, Has.Count.EqualTo(1), "흡수가 일어난 결의에 「+N 흡수」 알림이 한 번 난다.");
            Assert.That(absorbed[0].Amount, Is.EqualTo(state.BossPhases.Single().AbsorbedStacks), "알림 amount = 그 결의에 얻은 스택(= 누적, 첫 흡수라).");
            Assert.That(MonsterTraitAnnouncement.TryGetText(BossPropAnnouncements.AbsorbedRef, 30, out var text), Is.True);
            Assert.That(text, Is.EqualTo("+30 흡수"));
        }

        [Test]
        public void TrapPoolAuthoringIsValidatedAtParseTime()
        {
            Assert.That(() => CreateBossState(trapPool: "Burn:1"), Throws.ArgumentException.With.Message.Contains("Burn"),
                "네 어휘 밖의 종류는 거부한다.");
            Assert.That(() => CreateBossState(trapPool: "Curse:1", cursePool: ""), Throws.ArgumentException.With.Message.Contains(IronScrapMechanicParams.VolleyTrapCursePool),
                "저주를 저작하면 카드 풀이 필수다.");
            Assert.That(() => CreateBossState(trapPool: "Slow:1|Slow:1"), Throws.ArgumentException.With.Message.Contains(IronScrapMechanicParams.VolleyTrapPoolByPhase),
                "페이즈 수(3)와 길이가 다르면 거부한다.");
            Assert.That(() => CreateBossState(intervalByPhase: "2|5|5", trapPool: ""), Throws.ArgumentException.With.Message.Contains(IronScrapMechanicParams.VolleyIntervalByPhase),
                "페이즈별 주기도 성숙(2)보다 커야 한다.");
        }

        [Test]
        public void HoveringAPropDrawsItsBlastRadiusOnTheDedicatedOverlayLayer()
        {
            // 2026-09-05 결정 4: 철조각에 손을 얹으면 폭발 원판이 판에 뜬다 — 툴팁의 「반경 N칸」과 같은 저작값.
            var state = CreateBossState(trapPool: "", blastRadius: 1);
            RunFullTurn(state);
            var prop = LivingProps(state).First();

            var presentation = new CombatOverlayPresentationBuilder().Build(BuildRequest(state, hoveredMonsterId: prop.Id));
            Assert.That(presentation.Layers.Count(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.BossPropBlastReach), Is.EqualTo(1), "손을 얹은 기물의 폭발 범위 레이어가 없다.");
            var blast = presentation.Layers.Single(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.BossPropBlastReach);
            Assert.That(blast.Coords, Is.EquivalentTo(HexArea.CellsWithin(prop.Coord, 1)), "폭발 반경 1 = 기물 칸 + 인접 6칸.");

            var idle = new CombatOverlayPresentationBuilder().Build(BuildRequest(state, hoveredMonsterId: null));
            Assert.That(idle.Layers.Any(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.BossPropBlastReach), Is.False,
                "손을 떼면 사라진다(호버 유도값).");
            var boss = new CombatOverlayPresentationBuilder().Build(BuildRequest(state, hoveredMonsterId: BossUnitId));
            Assert.That(boss.Layers.Any(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.BossPropBlastReach), Is.False,
                "기물이 아닌 몬스터에는 뜨지 않는다.");
        }

        private static CombatOverlayPresentationRequest BuildRequest(CombatState state, string hoveredMonsterId)
        {
            return new CombatOverlayPresentationRequest(
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
                showMonsterChaseOverlay: true,
                hoveredMonsterId: hoveredMonsterId);
        }

        // ── 전술 레이어 등록(2026-09-05 후속 #2) ─────────────────────────────────────

        [Test]
        public void EveryBuilderEmittedLayerIsTacticalSoAnEmptyRefreshClearsIt()
        {
            // 🔴 다섯 번째 재현: 철조각 폭발 범위 호버가 TacticalLayers 손 목록에서 빠져 손을 떼도 남았다.
            //   목록은 이제 열거형에서 유도된다 — 호버·튜토리얼이 직접 켜고 끄는 레이어만 빠진다.
            var hoverOwned = new[]
            {
                SeoulPlayup.Map.Unity.HexOverlayLayer.PlayerHoverMove,
                SeoulPlayup.Map.Unity.HexOverlayLayer.PlayerHoverAction,
                SeoulPlayup.Map.Unity.HexOverlayLayer.FieldObjectRange,
                SeoulPlayup.Map.Unity.HexOverlayLayer.TutorialTarget
            };
            var expected = Enum.GetValues(typeof(SeoulPlayup.Map.Unity.HexOverlayLayer))
                .Cast<SeoulPlayup.Map.Unity.HexOverlayLayer>()
                .Where(layer => !hoverOwned.Contains(layer))
                .ToList();
            Assert.That(CombatOverlayPresentation.TacticalLayers, Is.EquivalentTo(expected));
            Assert.That(CombatOverlayPresentation.TacticalLayers, Has.Member(SeoulPlayup.Map.Unity.HexOverlayLayer.BossPropBlastReach));
            Assert.That(CombatOverlayPresentation.TacticalLayers, Has.Member(SeoulPlayup.Map.Unity.HexOverlayLayer.BossScrapChainTelegraph));
            Assert.That(CombatOverlayPresentation.TacticalLayers, Has.Member(SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterSummonIntent));
            Assert.That(CombatOverlayPresentation.TacticalLayers, Has.Member(SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterSelfBuffIntent));
            Assert.That(CombatOverlayPresentation.TacticalLayers, Has.No.Member(SeoulPlayup.Map.Unity.HexOverlayLayer.PlayerHoverMove));
        }

        // ── 수호 재충전 특성(결정 1 · Q13) ──────────────────────────────────────────

        [Test]
        public void GuardRechargeTraitGrantsOneChargeEveryNTurnsUpToTheCap()
        {
            var events = new List<EffectResultEvent>();
            var state = CreateGuardTraitState(guardRechargeTurns: 2);
            state.EffectResolved += resultEvent => events.Add(resultEvent);

            Assert.That(GuardCharges(state, "guard-01"), Is.EqualTo(0), "조우만으로는 충전이 없다(페이즈 진입 부여는 폐지).");
            RunFullTurn(state);
            Assert.That(GuardCharges(state, "guard-01"), Is.EqualTo(0), "1턴 뒤에는 아직 없다.");
            RunFullTurn(state);
            Assert.That(GuardCharges(state, "guard-01"), Is.EqualTo(1), "2턴마다 1.");
            Assert.That(MonsterCatalogEntry.GuardRechargeMaxCharges, Is.EqualTo(1), "2026-09-05 후속 #5: 상한 1(플레이어와 동일).");
            RunFullTurn(state);
            RunFullTurn(state);
            Assert.That(GuardCharges(state, "guard-01"), Is.EqualTo(MonsterCatalogEntry.GuardRechargeMaxCharges),
                "상한(1)에서 멈춘다 — 안 쓰는 동안 무한히 쌓이면 뒤에 통째로 막힌다.");
            RunFullTurn(state);
            RunFullTurn(state);
            Assert.That(GuardCharges(state, "guard-01"), Is.EqualTo(MonsterCatalogEntry.GuardRechargeMaxCharges));

            var recharged = events.Where(e => e.Kind == EffectKind.MonsterTraitTriggered && e.SourceRef == MonsterTraitAnnouncement.GuardRechargedRef).ToList();
            Assert.That(recharged, Has.Count.EqualTo(1), "충전이 실제로 오른 한 번만 알린다(상한에 막힌 턴은 침묵).");
            Assert.That(MonsterTraitAnnouncement.TryGetText(MonsterTraitAnnouncement.GuardRechargedRef, 1, out var text), Is.True);
            Assert.That(text, Is.EqualTo("수호 충전"));
        }

        [Test]
        public void GuardRechargeProgressSurvivesSuspend()
        {
            var state = CreateGuardTraitState(guardRechargeTurns: 2);
            RunFullTurn(state);
            var snapshot = state.CreateSuspendSnapshot();

            var resumed = CreateGuardTraitState(guardRechargeTurns: 2);
            resumed.RestoreFromSuspend(snapshot);
            RunFullTurn(resumed);

            Assert.That(GuardCharges(resumed, "guard-01"), Is.EqualTo(1),
                "진행(1/2)이 왕복되지 않으면 재개 뒤 한 턴 더 기다려야 한다 — 세이브가 주기를 되감는다.");
        }

        [Test]
        public void MonsterWithoutTheColumnNeverRecharges()
        {
            var state = CreateGuardTraitState(guardRechargeTurns: 0);
            for (var i = 0; i < 4; i++)
            {
                RunFullTurn(state);
            }

            Assert.That(GuardCharges(state, "guard-01"), Is.EqualTo(0));
            Assert.That(MonsterTraitResolver.CollectTraitIds(CreateGuardEntry(2)), Has.Member(MonsterTraitIds.GuardCycle));
            Assert.That(MonsterTraitResolver.CollectTraitIds(CreateGuardEntry(0)), Has.No.Member(MonsterTraitIds.GuardCycle));
            Assert.That(MonsterTraitText.ResolveDescriptionValue(CreateGuardEntry(3), MonsterTraitIds.GuardCycle), Is.EqualTo(3));
        }

        // --- helpers ------------------------------------------------------------------------------

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();
        }

        private static List<MonsterRuntimeState> LivingProps(CombatState state) =>
            state.Monsters.Where(monster => monster.DefinitionId == PropDefinitionId && !monster.IsDead).ToList();

        private static HexCoord BossCoord(CombatState state) => state.Monsters.Single(monster => monster.Id == BossUnitId).Coord;

        private static int GuardCharges(CombatState state, string unitId)
        {
            return state.ActiveEffects
                .Where(effect => effect.Kind == StatusEffectKind.Guard && effect.TargetUnitId == unitId && !effect.IsExpired)
                .Sum(effect => effect.Amount);
        }

        private static HexMapData CreateDiskMap(HexCoord center, int radius)
        {
            return new HexMapData(HexArea.CellsWithin(center, radius)
                .Select(coord => new HexCellData(coord, "tile", "street", 1, true, false))
                .ToList());
        }

        private static CombatConfig CreateConfig() => new CombatConfig(200, BossBaseHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 8);

        /// <summary>반경 4 원판 맵(아레나 저작 없음 → 함정 폴백 원판 = 보스 기준 반경 4)에 보스 하나.</summary>
        private static CombatState CreateBossState(
            string trapPool,
            string intervalByPhase = null,
            string cursePool = "X07+X05",
            int turretMaxAlive = 2,
            int trapMaxArmed = 8,
            IEnumerable<MonsterConfig> extraMonsters = null,
            int blastRadius = 0)
        {
            var playerCoord = new HexCoord(0, 0);
            var bossCoord = new HexCoord(4, 0);
            var monsters = new List<MonsterConfig>
            {
                new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: MonsterSpawnRoles.Boss)
            };
            if (extraMonsters != null)
            {
                monsters.AddRange(extraMonsters);
            }

            var pools = string.IsNullOrEmpty(trapPool) ? "" : (trapPool.Contains("|") ? trapPool : $"{trapPool}|{trapPool}|{trapPool}");
            var mechanicParams = string.Join(";", new[]
            {
                $"{IronScrapMechanicParams.PropId}={PropDefinitionId}",
                $"{IronScrapMechanicParams.StackPerProp}=15",
                $"{IronScrapMechanicParams.MaturityTurns}={MaturityTurns}",
                $"{IronScrapMechanicParams.VolleyIntervalTurns}=10",
                $"{IronScrapMechanicParams.VolleyIntervalByPhase}={intervalByPhase ?? string.Empty}",
                $"{IronScrapMechanicParams.VolleyByPhase}=2|2|2",
                $"{IronScrapMechanicParams.RingRadius}=2",
                $"{IronScrapMechanicParams.MinSpacing}=2",
                $"{IronScrapMechanicParams.MaxAlive}=8",
                $"{IronScrapMechanicParams.BlastRadius}={blastRadius}",
                $"{IronScrapMechanicParams.BlastDamage}=3",
                $"{IronScrapMechanicParams.VolleyTrapPoolByPhase}={pools}",
                $"{IronScrapMechanicParams.VolleyTrapMinSpacing}=2",
                $"{IronScrapMechanicParams.VolleyTrapMaxArmed}={trapMaxArmed}",
                $"{IronScrapMechanicParams.VolleyTrapCursePool}={cursePool}",
                $"{IronScrapMechanicParams.VolleyTurretByPhase}={TurretDefinitionId}|{TurretDefinitionId}|{TurretDefinitionId}",
                $"{IronScrapMechanicParams.VolleyTurretMaxAlive}={turretMaxAlive}",
                $"{IronScrapMechanicParams.VolleyTrapSlowTurns}=2",
                $"{IronScrapMechanicParams.VolleyTrapStunTurns}=1",
                $"{IronScrapMechanicParams.VolleyGuardBlock}={GuardBlock}"
            });
            var profiles =
                BossCsv.ProfilesHeader + "\n" +
                $"{BossDefinitionId},테스트 보스,AbsorbedStacks,iron-scrap,{mechanicParams},,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                $"{BossDefinitionId},1,0,0,0,0,1,0,,\n" +
                $"{BossDefinitionId},2,1000,0,0,0,1,0,,\n" +
                $"{BossDefinitionId},3,2000,0,0,0,1,0,,\n";

            var state = new CombatState(
                CreateDiskMap(new HexCoord(2, 0), 4),
                playerCoord,
                monsters,
                CreateConfig(),
                monsterCatalog: new MonsterCatalogDefinition(
                    "boss-volley-trap-test-catalog",
                    "Boss Volley Trap Test Catalog",
                    new[]
                    {
                        new MonsterCatalogEntry(BossDefinitionId, "테스트 보스", "test-melee", "B001", detectionRange: 8, movePerTurn: 0, hp: BossBaseHp,
                            attackPatterns: new[] { new MonsterAttackPattern("A100", "근접", 1, 0, 2) }, sturdyBlock: true),
                        new MonsterCatalogEntry(PropDefinitionId, "철조각", "boss-prop", "B001", detectionRange: 0, movePerTurn: 1, hp: PropHp,
                            attackPatterns: new[] { new MonsterAttackPattern("A900", "없음", 1, 0, 0) }),
                        new MonsterCatalogEntry(TurretDefinitionId, "터렛", "test-ranged", "B001", detectionRange: 0, movePerTurn: 0, hp: 3,
                            attackPatterns: new[] { new MonsterAttackPattern("A901", "사격", 1, 0, 1) })
                    }),
                bossCatalog: BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(profiles, phases, "boss-volley-trap-test", "Boss Volley Trap Test")),
                drawOpeningHands: false);
            state.ConfigureBossPropRandomForTests(new System.Random(7));
            return state;
        }

        private static MonsterCatalogEntry CreateGuardEntry(int guardRechargeTurns)
        {
            return new MonsterCatalogEntry("M050", "수호 몬스터", "test-melee", "B001", detectionRange: 8, movePerTurn: 0, hp: 20,
                attackPatterns: new[] { new MonsterAttackPattern("A100", "근접", 1, 0, 2) },
                guardRechargeTurns: guardRechargeTurns);
        }

        /// <summary>보스가 아닌 일반 몬스터 하나 — 특성은 카탈로그 컬럼이므로 보스 프로필 없이도 돈다.</summary>
        private static CombatState CreateGuardTraitState(int guardRechargeTurns)
        {
            var state = new CombatState(
                CreateDiskMap(new HexCoord(2, 0), 4),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("guard-01", new HexCoord(4, 0), 20, definitionId: "M050", spawnRole: "monster") },
                CreateConfig(),
                monsterCatalog: new MonsterCatalogDefinition("guard-trait-test", "Guard Trait Test", new[] { CreateGuardEntry(guardRechargeTurns) }),
                drawOpeningHands: false);
            return state;
        }
    }
}

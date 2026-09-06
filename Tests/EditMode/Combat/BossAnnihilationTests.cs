using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 전멸기(§20-B)의 규칙 계약: 주기 → <b>중앙 점프 + 후보 3곳</b> → 2턴 예고 → 예고된 칸만 폭발.
    ///
    /// 핵심은 <b>회피 가능성 보장</b>이며, 개편 후 그 보장의 출처가 바뀌었다: 예전엔 "안전지대가
    /// 플레이어 근처에 반드시 있다"였고, 지금은 <b>후보 3 중 가짜가 정확히 1개</b>라는 산술이다 —
    /// 정찰로 한 곳만 판별하면 진짜면 거기로 가고 가짜면 남은 둘이 전부 진짜다. 그래서 이 파일의
    /// 가장 중요한 핀은 "진짜 2 · 가짜 1"과 그것을 지키는 저작 가드다.
    /// </summary>
    public sealed class BossAnnihilationTests
    {
        private const string BossDefinitionId = "M002";
        private const string BossUnitId = "boss-01";
        private const int BossBaseHp = 40;
        private const int BlastDamage = 5;
        private const int SafeReach = 2;
        private const string WideScoutCardId = "S001";

        [Test]
        public void TelegraphLeavesRealSafeCandidatesAndPunishesStandingStill()
        {
            var state = CreateAnnihilationBossState();

            RunFullTurn(state); // 턴 1: 쿨다운 0 → 중앙 점프 + 후보 + 예고 개시(2턴)
            var telegraph = state.GetBossAnnihilationTelegraphCells();
            Assert.That(telegraph, Is.Not.Empty, "예고가 개시되어야 한다.");
            Assert.That(telegraph, Does.Contain(state.PlayerCoord),
                "플레이어의 현재 칸은 후보가 아니다 — 제자리는 답이 아니다(이동 강제 퍼즐).");

            var candidates = state.GetBossSafeZoneCandidates().Select(candidate => candidate.Coord).ToList();
            Assert.That(candidates, Has.Count.EqualTo(3), "후보는 3곳이다.");
            var real = candidates.Where(coord => !telegraph.Contains(coord)).ToList();
            Assert.That(real, Has.Count.EqualTo(2),
                "🔴 진짜 2 · 가짜 1 — 이 산술이 '정찰 1장이면 회피 100%'의 유일한 출처다(§20-B-2).");
            Assert.That(real.All(coord => state.PlayerCoord.DistanceTo(coord) <= SafeReach), Is.True,
                "진짜 안전지대는 플레이어가 한 번의 이동으로 닿을 수 있어야 한다.");

            RunFullTurn(state); // 턴 2: 예고만 줄어든다(플레이어가 반응할 턴)
            Assert.That(state.GetBossAnnihilationTelegraphCells(), Is.Not.Empty,
                "예고 2턴 = 점프 턴 + 반응 턴. 점프한 턴에 바로 터지면 정찰도 이동도 낄 자리가 없다.");

            var hpBefore = state.Player.Hp;
            RunFullTurn(state); // 턴 3: 제자리 → 폭발 명중
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - BlastDamage), "예고된 칸에 서 있으면 폭발 피해를 받는다.");
            Assert.That(state.GetBossAnnihilationTelegraphCells(), Is.Empty, "폭발 후 예고는 지워진다.");
            Assert.That(state.GetBossSafeZoneCandidates(), Is.Empty, "후보도 함께 지워진다 — 남으면 낡은 `?`가 뜬다.");
        }

        [Test]
        public void MovingIntoARealCandidateAvoidsTheBlast()
        {
            var state = CreateAnnihilationBossState();
            RunFullTurn(state);
            var telegraph = state.GetBossAnnihilationTelegraphCells();
            var real = state.GetBossSafeZoneCandidates()
                .Select(candidate => candidate.Coord)
                .First(coord => !telegraph.Contains(coord));
            Assert.That(state.TryDebugMovePlayer(real), Is.True);

            var hpBefore = state.Player.Hp;
            RunFullTurn(state);
            RunFullTurn(state); // 폭발 — 진짜 후보는 예고에 없으므로 빗나간다
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "진짜 안전지대로 옮기면 전멸기는 맞지 않는다.");
        }

        [Test]
        public void StandingOnTheFakeCandidateStillGetsHit()
        {
            // 가짜 후보는 예고 칸에 <b>포함</b>된다(§20-B-5). 그래서 판별 시 별도 "가짜" 색이 필요 없다 —
            // `?`를 걷어내면 아래 깔린 붉은 예고가 그대로 드러난다.
            var state = CreateAnnihilationBossState();
            RunFullTurn(state);
            var telegraph = state.GetBossAnnihilationTelegraphCells();
            var fake = state.GetBossSafeZoneCandidates()
                .Select(candidate => candidate.Coord)
                .Single(coord => telegraph.Contains(coord));
            Assert.That(state.TryDebugMovePlayer(fake), Is.True);

            var hpBefore = state.Player.Hp;
            RunFullTurn(state);
            RunFullTurn(state);
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - BlastDamage));
        }

        [Test]
        public void CandidatesAreUnknownUntilScouted()
        {
            // 후보의 진위는 정찰로만 갈린다 — 서 보거나 시간이 지난다고 드러나지 않는다.
            var state = CreateAnnihilationBossState();
            RunFullTurn(state);

            Assert.That(
                state.GetBossSafeZoneCandidates().All(candidate => candidate.Kind == BossSafeZoneCandidateKind.Unknown),
                Is.True,
                "정찰 없이는 셋 다 미판별이어야 한다 — 겉모습으로 갈리면 정찰에 값을 지불할 이유가 없다.");
        }

        [Test]
        public void ScoutingACandidateRevealsItsVerdictAndAWideScoutRevealsEveryCandidateItCovers()
        {
            // 결정 3(§20-B-4): 넓은 정찰이 후보 여러 곳을 덮으면 <b>둘 다</b> 판별한다 —
            // 상위 정찰 카드의 정상적 보상으로 인정한다.
            var state = CreateAnnihilationBossState();
            RunFullTurn(state);
            var candidates = state.GetBossSafeZoneCandidates().Select(candidate => candidate.Coord).ToList();
            Assert.That(candidates, Has.Count.EqualTo(3));

            // 후보는 전부 플레이어 SafeReach(2) 이내이므로, 플레이어 칸을 중심으로 한 반경 2 정찰이
            // 셋을 통째로 덮는다.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.TryPlayerScout(state.PlayerCoord, WideScoutCardId), Is.True, state.LastFailureReason);

            var revealed = state.GetBossSafeZoneCandidates();
            Assert.That(revealed.All(candidate => candidate.Kind != BossSafeZoneCandidateKind.Unknown), Is.True,
                "반경 2 정찰이 후보 셋을 전부 덮었으므로 셋 다 판별된다.");
            Assert.That(revealed.Count(candidate => candidate.Kind == BossSafeZoneCandidateKind.Real), Is.EqualTo(2));
            Assert.That(revealed.Count(candidate => candidate.Kind == BossSafeZoneCandidateKind.Fake), Is.EqualTo(1),
                "🔴 가짜는 정확히 하나 — 이것이 '정찰 1장이면 회피 100%'의 산술적 근거다.");

            var telegraph = state.GetBossAnnihilationTelegraphCells();
            foreach (var candidate in revealed)
            {
                Assert.That(
                    telegraph.Contains(candidate.Coord),
                    Is.EqualTo(candidate.Kind == BossSafeZoneCandidateKind.Fake),
                    "판별 결과와 예고 포함 여부가 일치해야 한다 — 어긋나면 화면이 거짓말을 한다.");
            }
        }

        [Test]
        public void ConfirmedSafeZonesCarryHoverTooltipAnnotations()
        {
            // §28 W5 후속 T1: 초록 확정 채움은 레이어(BossSafeZoneConfirmed)라 호버 대상이 없다 —
            // 아이콘 채널에 호버 전용 주석이 함께 실려야 초록 칸이 "왜 초록인가"에 답할 수 있다.
            var state = CreateAnnihilationBossState();
            RunFullTurn(state);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.TryPlayerScout(state.PlayerCoord, WideScoutCardId), Is.True, state.LastFailureReason);

            var real = state.GetBossSafeZoneCandidates()
                .Where(candidate => candidate.Kind == BossSafeZoneCandidateKind.Real)
                .Select(candidate => candidate.Coord)
                .ToList();
            Assert.That(real, Is.Not.Empty);

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

            Assert.That(
                presentation.Layers.Select(layer => layer.Layer),
                Does.Contain(HexOverlayLayer.BossSafeZoneConfirmed),
                "판명된 진짜는 초록 확정 채움으로 그려진다(§28 W5).");
            Assert.That(
                presentation.Annotations.Where(annotation => annotation.SafeZoneConfirmed).Select(annotation => annotation.Coord),
                Is.EquivalentTo(real),
                "호버 툴팁 주석은 정확히 진짜 판명 칸에만 실린다 — 빠지면 초록 칸이 툴팁을 못 띄우고, 남으면 낡은 확정이 거짓말을 한다.");
        }

        [Test]
        public void JumpMovesTheBossToTheArenaCenterAndSkipsItsAttackThatTurn()
        {
            // 🔴 점프 턴 공격 억제는 <b>래치</b>로 판정해야 한다(§20-B-3-1): 쿨다운으로 판정하면 결의가
            // 쿨다운을 되감아 같은 턴 안에서 답이 뒤집히고, 예고 없이 공격이 나간다.
            var state = CreateAnnihilationBossState();

            // 턴 1의 몬스터 행동 <b>직전</b> = 공격 예고가 커밋되는 시점. 여기서 이미 "점프할 것이다"를
            // 알아야 예고와 결의가 갈라지지 않는다.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(InvokeIsMonsterAttackBlocked(state, BossUnitId), Is.True,
                "점프할 턴에는 예고 커밋 시점부터 공격이 막혀 있어야 한다.");

            Assert.That(state.EndAction(), Is.True);
            var turnAtJump = state.OverallTurnNumber;
            state.ResolveMonsterAction();

            Assert.That(state.GetBossAnnihilationTelegraphCells(), Is.Not.Empty, "점프 + 예고가 실제로 일어났다.");
            Assert.That(GetTrack(state).LastAnnihilationJumpOverallTurn, Is.EqualTo(turnAtJump),
                "🔴 래치가 결의 시점에 찍혀야 한다 — 쿨다운만 보면 결의가 그것을 되감아 같은 턴 안에서 답이 뒤집힌다.");

            // 턴 2(예고 턴)의 같은 시점: 보스는 평소대로 공격한다.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(InvokeIsMonsterAttackBlocked(state, BossUnitId), Is.False,
                "예고 턴에는 평소대로 공격한다 — 그것이 예고를 무시하고 접근하는 선택을 위험하게 만든다.");
        }

        [Test]
        public void SuspendRoundTripPreservesTelegraphCandidatesAndVerdicts()
        {
            var state = CreateAnnihilationBossState();
            RunFullTurn(state);
            var telegraphBefore = Sorted(state.GetBossAnnihilationTelegraphCells());
            var candidatesBefore = Sorted(state.GetBossSafeZoneCandidates().Select(c => c.Coord).ToList());
            var realBefore = Sorted(state.GetBossSafeZoneCandidates()
                .Select(c => c.Coord)
                .Where(coord => !telegraphBefore.Contains(coord))
                .ToList());
            Assert.That(telegraphBefore, Is.Not.Empty);

            var snapshot = state.CreateSuspendSnapshot();
            var savedTrack = snapshot.BossPhaseTracks.Single();
            Assert.That(savedTrack.AnnihilationTelegraphCells, Has.Count.EqualTo(telegraphBefore.Count));
            Assert.That(savedTrack.AnnihilationCandidateCells, Has.Count.EqualTo(3));
            Assert.That(savedTrack.AnnihilationRealSafeCells, Has.Count.EqualTo(2),
                "진위 배정을 왕복하지 않으면 저장/재개로 답을 다시 굴리는 세이브 스컴이 된다.");

            var resumed = CreateAnnihilationBossState();
            resumed.RestoreFromSuspend(snapshot);
            Assert.That(Sorted(resumed.GetBossAnnihilationTelegraphCells()), Is.EqualTo(telegraphBefore));
            Assert.That(Sorted(resumed.GetBossSafeZoneCandidates().Select(c => c.Coord).ToList()), Is.EqualTo(candidatesBefore));
            var realAfter = Sorted(resumed.GetBossSafeZoneCandidates()
                .Select(c => c.Coord)
                .Where(coord => !telegraphBefore.Contains(coord))
                .ToList());
            Assert.That(realAfter, Is.EqualTo(realBefore), "어느 후보가 진짜였는지가 왕복 후에도 같아야 한다.");

            var hpBefore = resumed.Player.Hp;
            RunFullTurn(resumed);
            RunFullTurn(resumed);
            Assert.That(resumed.Player.Hp, Is.EqualTo(hpBefore - BlastDamage));
        }

        // ------------------------------------------------------------------------------------------
        // §20-B-9 전멸기 ↔ 철조각 상호 배제
        // ------------------------------------------------------------------------------------------

        [Test]
        public void IronScrapYieldsWhileTheAnnihilationSequenceRuns_RegardlessOfMechanicOrder()
        {
            // 🔴 정합성이 CSV `mechanicId`의 `|` 순서에 걸리면 누가 순서를 되돌렸을 때 조용히 깨진다.
            // 두 순서 모두에서 같은 답이 나와야 한다 — 그것이 "단일 술어 + 래치" 설계의 목적이다.
            foreach (var mechanicIds in new[] { "annihilation|iron-scrap", "iron-scrap|annihilation" })
            {
                var state = CreateComboBossState(mechanicIds);

                RunFullTurn(state); // 점프 턴: 전멸기가 점유 → 살포 없음
                Assert.That(CountProps(state), Is.Zero, $"[{mechanicIds}] 점프 턴에는 살포하지 않는다.");

                RunFullTurn(state); // 예고 턴
                Assert.That(CountProps(state), Is.Zero, $"[{mechanicIds}] 예고 턴에도 살포하지 않는다.");

                RunFullTurn(state); // 폭발 턴 — 래치가 없으면 여기서 살포가 끼어든다
                Assert.That(CountProps(state), Is.Zero,
                    $"[{mechanicIds}] 폭발 턴에도 살포하지 않는다 — 폭발이 예고를 지우고 쿨다운을 되감으므로 래치 없이는 여기서 새어 나온다.");

                RunFullTurn(state); // 시퀀스가 끝난 다음 턴: 곧바로 살포
                Assert.That(CountProps(state), Is.GreaterThan(0),
                    $"[{mechanicIds}] 양보한 턴의 쿨다운은 소모되지 않으므로 시퀀스 종료 직후에 살포된다 — 소모하면 살포가 영영 밀린다.");
            }
        }

        // ------------------------------------------------------------------------------------------
        // §21.6 전멸기 ↔ 필드 오브젝트: 폭발이 예고 칸의 장판을 파괴하고, 후보는 장판을 배제하지 않는다
        // ------------------------------------------------------------------------------------------

        [Test]
        public void AFieldObjectBlanketDoesNotVetoTheAnnihilation()
        {
            // 🔴 §21.6 익스플로잇 수정의 핀: 후보 하드 조건이 필드 오브젝트 칸을 배제하던 시절에는
            // 플레이어 중심 반경 2 장판 한 장(19칸)이 후보 풀(safeReach 2 = 최대 18칸)을 통째로 덮어
            // 보스전 최대 압박 기믹이 카드 한 장에 무력화됐다. 파괴 규칙이 들어갔으므로 배제도 없다.
            var state = CreateAnnihilationBossState();
            state.FieldObjects.Add(new FieldObject(
                state.PlayerCoord, radius: 2, remainingTurns: 9, FieldObjectKind.FieldDamage, value: 0));

            RunFullTurn(state);
            Assert.That(state.GetBossAnnihilationTelegraphCells(), Is.Not.Empty,
                "장판이 후보 풀을 전부 덮어도 전멸기는 발동한다 — 배제 하드 조건과 파괴 규칙은 한 쌍으로 교체됐다.");
            Assert.That(state.GetBossSafeZoneCandidates().Select(candidate => candidate.Coord).ToList(),
                Has.Count.EqualTo(3), "장판 위 칸도 이제 정당한 후보다.");
        }

        [Test]
        public void TheBlastDestroysFieldObjectsOnTelegraphedCellsButSparesTheRealSafeZone()
        {
            var state = CreateAnnihilationBossState();
            RunFullTurn(state); // 점프 + 예고 개시
            var telegraph = state.GetBossAnnihilationTelegraphCells();
            var real = state.GetBossSafeZoneCandidates()
                .Select(candidate => candidate.Coord)
                .First(coord => !telegraph.Contains(coord));
            var doomed = telegraph.First(coord => coord != real);

            state.FieldObjects.Add(new FieldObject(doomed, 0, 9, FieldObjectKind.FieldDamage, value: 0));
            state.FieldObjects.Add(new FieldObject(real, 0, 9, FieldObjectKind.FieldDamage, value: 0));

            RunFullTurn(state); // 예고 턴 — 아직 아무것도 파괴되지 않는다
            Assert.That(state.FieldObjects.Objects.Count(fieldObject => fieldObject.Radius == 0), Is.EqualTo(2),
                "파괴 시점은 폭발(T+2)이다 — 예고 중에 지우면 '지금 까는 건 낭비'라는 정보가 성립하지 않는다.");

            RunFullTurn(state); // 폭발
            Assert.That(state.FieldObjects.Objects.Any(fieldObject => fieldObject.Position == doomed), Is.False,
                "예고 칸에 걸친 장판은 폭발과 함께 파괴된다(§21.6).");
            Assert.That(state.FieldObjects.Objects.Any(fieldObject => fieldObject.Position == real), Is.True,
                "진짜 안전지대 위의 장판은 살아남는다 — 안전지대는 예고에서 빠져 있고, 규칙은 예고와 일치한다.");
        }

        // ------------------------------------------------------------------------------------------
        // §21.7 양방향 배타 — 판에 철조각이 있으면 전멸기가 발동하지 않는다
        // ------------------------------------------------------------------------------------------

        [Test]
        public void AnnihilationWaitsWhileLivingPropsRemainAndFiresTheTurnAfterTheBoardEmpties_RegardlessOfMechanicOrder()
        {
            // 🔴 흡수 턴이 함정이다(§21.7): 철조각 흡수가 결의 중에 판을 비우므로, "지금 기물이 있나"를
            // 살아있는 값으로 물으면 mechanicId 순서에 따라 같은 턴의 답이 갈린다. 턴 시작 래치는
            // 두 순서 모두에서 "철조각이 다 사라진 <b>다음 턴</b>부터"로 답을 고정해야 한다.
            foreach (var mechanicIds in new[] { "annihilation|iron-scrap", "iron-scrap|annihilation" })
            {
                // 전멸기 interval 3(예고 2) · 철조각 volley 4 / 성숙 2:
                // T1 점프 → T2 예고 → T3 폭발(쿨다운 2) → T4 쿨다운 1 + 살포 → T5 쿨다운 0(기물 나이 1)
                // → T6 쿨다운 0 유지 · <b>흡수 턴</b>(나이 2 = 성숙) → T7 판이 빈 첫 턴 = 점프.
                var state = CreateExclusionBossState(mechanicIds);

                for (var turn = 1; turn <= 5; turn++)
                {
                    RunFullTurn(state);
                }

                Assert.That(CountProps(state), Is.GreaterThan(0), $"[{mechanicIds}] T5까지는 기물이 판에 있다.");

                RunFullTurn(state); // T6: 흡수가 판을 비우는 턴 — 여기서 발동하면 순서 의존이다.
                Assert.That(CountProps(state), Is.Zero, $"[{mechanicIds}] T6 흡수로 판이 빈다.");
                Assert.That(state.GetBossAnnihilationTelegraphCells(), Is.Empty,
                    $"[{mechanicIds}] 🔴 흡수 턴에는 발동하지 않는다 — 살아있는 값으로 물으면 " +
                    "iron-scrap|annihilation 순서에서 흡수 직후 질의가 '판이 비었다'로 읽혀 같은 턴에 발동한다.");
                Assert.That(GetTrack(state).AnnihilationCooldownTurns, Is.Zero,
                    $"[{mechanicIds}] 대기 중 쿨다운은 0에 머문다 — 소모되면 발동이 뒤로 밀린다.");

                RunFullTurn(state); // T7: 판이 빈 상태로 시작하는 첫 턴.
                Assert.That(state.GetBossAnnihilationTelegraphCells(), Is.Not.Empty,
                    $"[{mechanicIds}] 판이 빈 다음 턴에는 곧바로 발동한다 — 대기가 공짜라는 계약의 증거다.");
            }
        }

        [Test]
        public void AnnihilationDoesNotSuppressTheBossAttackWhileYieldingToProps()
        {
            // 양보한 턴은 점프 턴이 아니다 — 공격 억제 질의(예고 커밋 시점)도 같은 래치를 읽어야
            // "일어나지 않을 점프 때문에 공격을 건너뛰는 빈 턴"이 생기지 않는다.
            var state = CreateExclusionBossState("annihilation|iron-scrap");
            for (var turn = 1; turn <= 5; turn++)
            {
                RunFullTurn(state);
            }

            // T6(흡수 턴 = 양보 턴)의 예고 커밋 시점: 쿨다운 0이지만 기물이 살아 있으므로 점프 예정이 아니다.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(InvokeIsMonsterAttackBlocked(state, BossUnitId), Is.False,
                "양보 턴에는 공격이 막히지 않는다 — 점프하지 않을 턴에 공격까지 삼키면 보스가 빈 턴을 보낸다.");
        }

        /// <summary>
        /// §21.7 배타 시나리오 전용 콤보 보스: 전멸기 시퀀스가 끝난 뒤 살포가 오고, 그 기물이 성숙해
        /// <b>흡수로 판이 비는 턴</b>에 전멸기 쿨다운이 이미 0이 되도록 주기를 맞춘 저작.
        /// </summary>
        private static CombatState CreateExclusionBossState(string mechanicIds)
        {
            const string ironScrap =
                "propId=P001;stackPerProp=1;maturityTurns=2;volleyIntervalTurns=4;volleyByPhase=2;" +
                "ringRadius=2;minSpacing=1;maxAlive=8";
            var annihilation =
                "annihilationPhaseMin=1;annihilationIntervalTurns=3;annihilationTelegraphTurns=2;" +
                $"annihilationDamage={BlastDamage};annihilationCandidateCells=3;annihilationRealSafeCells=2;" +
                $"annihilationCandidateSpacing=3;annihilationSafeReach={SafeReach};annihilationFallbackRadius=3;" +
                "annihilationLandingBlastRadius=1;annihilationLandingBlastDamage=0";
            return CreateBossState(mechanicIds, annihilation + ";" + ironScrap, "P001");
        }

        // --- helpers ------------------------------------------------------------------------------

        private static List<HexCoord> Sorted(IReadOnlyList<HexCoord> coords)
        {
            return coords.OrderBy(coord => coord.Q).ThenBy(coord => coord.R).ToList();
        }

        private static int CountProps(CombatState state)
        {
            return state.Monsters.Count(monster =>
                monster.Hp > 0 && MonsterSpawnRoles.IsBossProp(monster.SpawnRole));
        }

        /// <summary>반경 2 정찰 한 장(<c>blast-2</c> · S01·S04 계열). 후보 셋을 한 번에 덮는 상위 카드다.</summary>
        private static CardCatalogDefinition AnnihilationTestCardCatalog()
        {
            return new CardCatalogDefinition(
                "boss-annihilation-cards",
                "Boss Annihilation Cards",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Move2Hex, "이동", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        WideScoutCardId, "광역 정찰", CardCategory.Action, CardEffectType.Scout,
                        1, 4, 0, "walkable_map_cell", areaRadius: 2,
                        status: CardCatalogStatus.Approved)
                });
        }

        private static BossPhaseTrack GetTrack(CombatState state)
        {
            // 3-B: 트랙은 BossEncounterState 소유 — 리플렉션 문자열 대신 internal 이음새로 집는다.
            return state.BossPhaseTracksForTests.Single();
        }

        private static bool InvokeIsMonsterAttackBlocked(CombatState state, string monsterId)
        {
            var runtimeMonsters = (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            var monster = runtimeMonsters.Single(candidate => candidate.Id == monsterId);
            var method = typeof(CombatState).GetMethod(
                "IsMonsterAttackBlocked", BindingFlags.Instance | BindingFlags.NonPublic);
            return (bool)method.Invoke(state, new object[] { monster });
        }

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        /// <summary>
        /// 전멸기 단독 보스(아레나 없음 → fallback 반경 3 원판, 점프 대상 '중앙'이 없어 제자리 착지).
        /// 1페이즈부터 활성, 주기 4·예고 2·피해 5. 착지 피해는 0으로 두어 폭발 피해만 관찰한다.
        /// 플레이어는 폭발 영역 안(보스에서 2칸)에서 시작한다.
        /// </summary>
        private static CombatState CreateAnnihilationBossState()
        {
            return CreateBossState("annihilation", AnnihilationParams(), string.Empty);
        }

        /// <summary>철조각과 전멸기를 함께 가진 보스. <paramref name="mechanicIds"/>로 저작 순서를 바꿔 넣는다.</summary>
        private static CombatState CreateComboBossState(string mechanicIds)
        {
            // 볼리 주기 1 = 매 턴 살포하려는 보스. 그런데도 전멸기 시퀀스 3턴 동안은 한 개도 놓이지 않아야 한다.
            const string ironScrap =
                "propId=P001;stackPerProp=1;maturityTurns=0;volleyIntervalTurns=1;volleyByPhase=2;" +
                "ringRadius=2;minSpacing=1;maxAlive=8";
            return CreateBossState(mechanicIds, AnnihilationParams() + ";" + ironScrap, "P001");
        }

        private static string AnnihilationParams()
        {
            return "annihilationPhaseMin=1;annihilationIntervalTurns=4;annihilationTelegraphTurns=2;" +
                   $"annihilationDamage={BlastDamage};annihilationCandidateCells=3;annihilationRealSafeCells=2;" +
                   $"annihilationCandidateSpacing=3;annihilationSafeReach={SafeReach};annihilationFallbackRadius=3;" +
                   "annihilationLandingBlastRadius=1;annihilationLandingBlastDamage=0";
        }

        private static CombatState CreateBossState(string mechanicIds, string mechanicParams, string propDefinitionId)
        {
            var playerCoord = new HexCoord(1, 0);
            var bossCoord = new HexCoord(3, 0);
            var profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + $",테스트 보스,TurnCount,{mechanicIds},{mechanicParams},,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,1,0,,\n";

            var entries = new List<MonsterCatalogEntry>
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
                    })
            };
            if (!string.IsNullOrEmpty(propDefinitionId))
            {
                entries.Add(new MonsterCatalogEntry(
                    propDefinitionId,
                    "철조각",
                    "test-prop",
                    "P001",
                    detectionRange: 0,
                    movePerTurn: 0,
                    hp: 4,
                    attackSpeed: 0,
                    attackPatterns: System.Array.Empty<MonsterAttackPattern>()));
            }

            return new CombatState(
                CombatState.CreateDemoMap(7),
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: "boss") },
                new CombatConfig(200, BossBaseHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "boss-annihilation-test-catalog",
                    "Boss Annihilation Test Catalog",
                    entries),
                cardCatalog: AnnihilationTestCardCatalog(),
                bossCatalog: BossCatalogCsvConverter.Convert(
                    new BossCatalogCsvSource(profiles, phases, "boss-annihilation-test", "Boss Annihilation Test")));
        }
    }
}

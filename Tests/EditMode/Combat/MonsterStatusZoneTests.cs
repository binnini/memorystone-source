using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 상태이상 지대(요괴 트랙 §4-3 · 두억시니)와 「밀어붙이기」(§4-5)의 계약.
    ///
    /// <para>🔴 이 스위트의 중심은 <b>지대가 몬스터를 때리지 않는다</b>는 것이다(S3 완료 기준).
    /// 몬스터도 상하게 만들면 위협이 스스로 풀릴 뿐 아니라, AI가 지대를 피하려 드는 순간
    /// 통행 불가 갈래를 폐기한 이유(길찾기·배치·도달성 오염)가 그대로 돌아온다.</para>
    /// </summary>
    public sealed class MonsterStatusZoneTests
    {
        // ------------------------------------------------------------------ 지대 집행

        [Test]
        public void StatusZoneNeverTouchesMonsters()
        {
            var state = CreateState();
            var monster = state.Monsters.Single(candidate => candidate.Id == "zone-a");
            var hpBefore = monster.Hp;

            // 몬스터가 서 있는 칸에 다른 몬스터가 깐 지대를 얹는다 — "적이 적을 상하게 하는가"의 정면 질문.
            state.FieldObjects.Add(Zone(monster.Coord, StatusEffectKind.Slow, sourceUnitId: "zone-b"));

            AdvanceOneOverallTurn(state);

            Assert.That(state.Monsters.Single(candidate => candidate.Id == "zone-a").Hp, Is.EqualTo(hpBefore),
                "지대는 몬스터의 체력을 건드리지 않는다.");
            Assert.That(HasStatus(state, "zone-a", StatusEffectKind.Slow), Is.False,
                "지대는 몬스터에게 상태이상도 걸지 않는다 — 핸들러가 몬스터 목록을 아예 보지 않는다.");
        }

        [Test]
        public void StatusZoneAppliesTheAuthoredStatusToThePlayerStandingOnIt()
        {
            var state = CreateState();
            state.FieldObjects.Add(Zone(state.PlayerCoord, StatusEffectKind.Rupture, sourceUnitId: "zone-a"));

            AdvanceOneOverallTurn(state);

            Assert.That(HasStatus(state, "player", StatusEffectKind.Rupture), Is.True,
                "밟고 있는 플레이어에게는 저작된 상태이상이 걸린다.");
        }

        [Test]
        public void StatusZoneLeavesThePlayerAloneWhenTheyStepOff()
        {
            // 지대는 자리이지 낙인이 아니다 — 벗어나 있으면 아무 일도 없어야 "피하면 된다"가 성립한다.
            var state = CreateState();
            state.FieldObjects.Add(Zone(new HexCoord(2, 0), StatusEffectKind.Rupture, sourceUnitId: "zone-a"));

            AdvanceOneOverallTurn(state);

            Assert.That(HasStatus(state, "player", StatusEffectKind.Rupture), Is.False);
        }

        [Test]
        public void StatusZoneSurvivesASuspendRoundTripWithItsAuthoredStatus()
        {
            var state = CreateState();
            state.FieldObjects.Add(Zone(state.PlayerCoord, StatusEffectKind.Slow, sourceUnitId: "zone-a"));

            var restored = CreateState();
            restored.RestoreFromSuspend(state.CreateSuspendSnapshot());

            var zone = restored.FieldObjects.Objects.Single(field => field.Kind == FieldObjectKind.StatusZone);
            Assert.That(zone.StatusKind, Is.EqualTo(StatusEffectKind.Slow),
                "지대의 상태이상 종류가 세이브를 넘지 못하면 재개 후 아무것도 걸지 않는 빈 장판이 된다.");
            Assert.That(zone.RemainingTurns, Is.EqualTo(3));
            Assert.That(zone.SourceUnitId, Is.EqualTo("zone-a"));
        }

        // ------------------------------------------------------------------ 실시간 판정(2026-09-05)

        [Test]
        public void SteppingIntoAZoneAppliesItsStatusWithoutWaitingForATurn()
        {
            // 🔴 계약이 바뀐 지점: 종전에는 필드 오브젝트 틱이 돌아야 걸렸다 — 이동 카드로 들어간
            // 턴에는 화면상 아무 일도 일어나지 않아 "발동이 안 된다"로 읽혔다.
            var state = CreateState();
            state.FieldObjects.Add(Zone(new HexCoord(2, 0), StatusEffectKind.Rupture, sourceUnitId: "zone-a"));
            Assume.That(HasStatus(state, "player", StatusEffectKind.Rupture), Is.False);

            Assert.That(state.TryDebugMovePlayer(new HexCoord(2, 0)), Is.True);

            Assert.That(HasStatus(state, "player", StatusEffectKind.Rupture), Is.True,
                "지대에 들어선 순간 걸려야 한다 — 턴을 넘기지 않았다.");
        }

        [Test]
        public void SteppingOutOfAZoneClearsItsStatusImmediately()
        {
            var state = CreateState();
            state.FieldObjects.Add(Zone(new HexCoord(2, 0), StatusEffectKind.Rupture, sourceUnitId: "zone-a"));
            Assume.That(state.TryDebugMovePlayer(new HexCoord(2, 0)), Is.True);
            Assume.That(HasStatus(state, "player", StatusEffectKind.Rupture), Is.True);

            Assert.That(state.TryDebugMovePlayer(new HexCoord(1, 0)), Is.True);

            Assert.That(HasStatus(state, "player", StatusEffectKind.Rupture), Is.False,
                "벗어나는 순간 풀려야 한다 — 지대는 낙인이 아니라 자리다.");
        }

        [Test]
        public void LeavingAZoneKeepsTheSameStatusWhenAnotherSourceGaveIt()
        {
            // 🔴 지대 유래(SourceRef)만 걷어 간다. 이 구분이 없으면 올무·카드가 건 둔화가
            // "지대를 벗어났다"는 이유로 함께 풀린다.
            var state = CreateState();
            state.FieldObjects.Add(Zone(new HexCoord(2, 0), StatusEffectKind.Slow, sourceUnitId: "zone-a"));
            Assume.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Slow, turns: 3, amount: 1), Is.True);
            Assume.That(state.TryDebugMovePlayer(new HexCoord(2, 0)), Is.True);

            Assert.That(state.TryDebugMovePlayer(new HexCoord(1, 0)), Is.True);

            Assert.That(HasStatus(state, "player", StatusEffectKind.Slow), Is.True,
                "다른 출처가 건 같은 종류의 상태이상은 지대를 벗어나도 남는다.");
        }

        [Test]
        public void AZoneTileStaysWalkable()
        {
            // 🔴 사용자 판정(2026-09-05): "장애물 판정이 아니어야 함. 밟을 수 있어야 함."
            // TryDebugMovePlayer는 통행 불가·기물 점유를 직접 거부하므로 이 한 줄이 그 계약이다.
            var state = CreateState();
            state.FieldObjects.Add(Zone(new HexCoord(2, 0), StatusEffectKind.Rupture, sourceUnitId: "zone-a"));

            Assert.That(state.TryDebugMovePlayer(new HexCoord(2, 0)), Is.True, state.LastFailureReason);
            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(2, 0)),
                "지대는 땅의 상태이지 서 있는 물건이 아니다 — 밟고 올라설 수 있어야 한다.");
        }

        [Test]
        public void ZonesAreOnlyLaidOnGroundThePlayerCouldStandOn()
        {
            // 🔴 사용자 요구(2026-09-05): "물 타일에는 발동되지 않도록". 설 수 없는 칸에 깔린 지대는
            // 아무 일도 일어나지 않는 그림만 남는다. 통행 판정과 같은 술어를 쓰는지 직접 잰다.
            var state = CreateStateWithWater();

            Assert.That(InvokeIsStatusZoneGround(state, new HexCoord(1, 0)), Is.True);
            Assert.That(InvokeIsStatusZoneGround(state, new HexCoord(2, 0)), Is.False,
                "물 칸에는 지대를 깔지 않는다.");
            Assert.That(InvokeIsStatusZoneGround(state, new HexCoord(9, 9)), Is.False,
                "맵 밖은 애초에 칸이 아니다.");
        }

        private static bool InvokeIsStatusZoneGround(CombatState state, HexCoord coord)
        {
            var method = typeof(CombatState).GetMethod(
                "IsStatusZoneGround",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, "IsStatusZoneGround가 사라졌다 — 지대 지면 판정을 다시 찾을 것.");
            return (bool)method.Invoke(state, new object[] { coord });
        }

        // ------------------------------------------------------------------ 저작 검증

        [Test]
        public void ZoneAuthoringRequiresBothColumnsAndAWellFormedEffect()
        {
            Assert.That(MonsterStatusZone.TryParse(string.Empty, string.Empty, out var none, out _), Is.True);
            Assert.That(none.HasZone, Is.False, "둘 다 비면 '지대 없음'이다 — 대다수 패턴이 그렇다.");

            Assert.That(MonsterStatusZone.TryParse("zone:Slow;3", string.Empty, out _, out var half), Is.False);
            Assert.That(half, Does.Contain("함께 저작"));
            Assert.That(MonsterStatusZone.TryParse(string.Empty, "2:0", out _, out _), Is.False);

            Assert.That(MonsterStatusZone.TryParse("Slow;3", "2:0", out _, out _), Is.False, "zone: 접두가 필요하다.");
            Assert.That(MonsterStatusZone.TryParse("zone:NotAKind;3", "2:0", out _, out _), Is.False);
            Assert.That(MonsterStatusZone.TryParse("zone:Slow;0", "2:0", out _, out _), Is.False, "턴은 양수.");
            Assert.That(MonsterStatusZone.TryParse("zone:Slow;9", "2:0", out _, out var tooLong), Is.False);
            Assert.That(tooLong, Does.Contain("cap"), "지대는 영구가 아니다(상한 3턴).");
            Assert.That(MonsterStatusZone.TryParse("zone:Slow;3", "2:0 2:0", out _, out _), Is.False, "중복 오프셋.");

            Assert.That(MonsterStatusZone.TryParse("zone:Rupture;3", "2:-1 2:0", out var spec, out _), Is.True);
            Assert.That(spec.StatusKind, Is.EqualTo(StatusEffectKind.Rupture));
            Assert.That(spec.DurationTurns, Is.EqualTo(3));
            Assert.That(spec.Offsets, Is.EqualTo(new[] { new HexCoord(2, -1), new HexCoord(2, 0) }));
        }

        [Test]
        [Category("ShippingData")]
        public void ZoneOffsetsOutsideTheShapeAreRejectedAtImport()
        {
            // 🔴 §7 ④ — 형상 밖 지대는 "예고=명중"을 깬다: 위험 칸 예고는 형상에서 나오는데 장판은
            // 다른 칸에 생기기 때문이다. 그래서 임포트에서 죽는다.
            var outside = Assert.Throws<System.ArgumentException>(() =>
                ConvertWithZone(shapeId: "cone-mid", zoneOffsets: "5:0", zoneEffect: "zone:Slow;3"));
            Assert.That(outside.Message, Does.Contain("outside shape"));

            var shapeless = Assert.Throws<System.ArgumentException>(() =>
                ConvertWithZone(shapeId: string.Empty, zoneOffsets: "2:0", zoneEffect: "zone:Slow;3"));
            Assert.That(shapeless.Message, Does.Contain("without a shapeId"));

            // 인접 링은 형상의 일부다(adjacency=full) — 링 칸에 지대를 까는 저작은 통과해야 한다.
            var onRing = ConvertWithZone(shapeId: "cone-mid", zoneOffsets: "1:0", zoneEffect: "zone:Slow;3");
            var ringPattern = onRing.MonsterCatalog.Entries.Single().AttackPatterns.Single();
            Assert.That(ringPattern.HasStatusZone, Is.True);
            Assert.That(ringPattern.ZoneOffsets, Is.EqualTo(new[] { new HexCoord(1, 0) }));
            Assert.That(ringPattern.ZoneStatusKind, Is.EqualTo(StatusEffectKind.Slow));
            Assert.That(ringPattern.ZoneDurationTurns, Is.EqualTo(3));
        }

        [Test]
        [Category("ShippingData")]
        public void ShippingZonesStayInsideTheirShapesAndOffThePermanentSide()
        {
            var catalog = ShippingMonsterCatalog();
            var zoned = catalog.Entries
                .SelectMany(entry => entry.AttackPatterns)
                .Where(pattern => pattern.HasStatusZone)
                .ToList();
            Assert.That(zoned, Is.Not.Empty, "S3 이후 지대 저작이 하나는 있어야 한다(두억시니).");

            foreach (var pattern in zoned)
            {
                Assert.That(pattern.ZoneDurationTurns, Is.InRange(1, MonsterStatusZone.MaxDurationTurns));
                Assert.That(AttackShapeLibrary.TryGet(pattern.ShapeId, out var shape), Is.True, pattern.Id);

                var cells = new HashSet<HexCoord>(shape.Offsets);
                if (shape.IncludeAdjacentRing)
                {
                    foreach (var adjacent in AttackShapeLibrary.AdjacentOffsets)
                    {
                        cells.Add(adjacent);
                    }
                }

                Assert.That(pattern.ZoneOffsets.All(cells.Contains), Is.True,
                    $"{pattern.Id}: 지대 칸이 형상 '{pattern.ShapeId}' 밖이다.");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void DuokseokiniCarriesItsZonesAndAdvance()
        {
            var catalog = ShippingMonsterCatalog();
            var entry = catalog.Entries.Single(candidate => candidate.Id == "M010");
            Assert.That(entry.HasAdvanceAfterAttack, Is.True, "「밀어붙이기」.");
            Assert.That(entry.MovePerTurn, Is.EqualTo(1), "느린 몸이 밀어붙이기의 전제다.");

            // 2026-09-04 리워크: A041 지대 5→7칸(전방 인접 추가 — 밀침으로 밀린 자리와 겹치는 연계),
            // A042 지대 2→4칸(fissure-twin 갈래 끝).
            var slam = entry.AttackPatterns.Single(pattern => pattern.Id == "A041");
            Assert.That(slam.ZoneStatusKind, Is.EqualTo(StatusEffectKind.Rupture));
            Assert.That(slam.ZoneOffsets.Count, Is.EqualTo(7));

            var fissure = entry.AttackPatterns.Single(pattern => pattern.Id == "A042");
            Assert.That(fissure.ZoneStatusKind, Is.EqualTo(StatusEffectKind.Slow));
            Assert.That(fissure.ZoneOffsets.Count, Is.EqualTo(4));
        }

        // ------------------------------------------------------------------ 밀어붙이기

        [Test]
        public void AdvanceAfterAttackStepsOneCellTowardThePlayer()
        {
            var state = CreateAdvanceState(monsterCoord: new HexCoord(2, 0));
            var hpBefore = state.Player.Hp;

            AdvanceOneOverallTurn(state);

            Assert.That(state.Player.Hp, Is.LessThan(hpBefore), "전제: 이 턴에 실제로 때렸다.");
            Assert.That(Pusher(state).Coord, Is.EqualTo(new HexCoord(1, 0)), "때린 뒤 한 칸 다가선다.");
        }

        [Test]
        public void AdvanceAfterAttackPushesTheAdjacentPlayerAndTakesTheCell()
        {
            // 2026-09-04 밀어붙이기 강화(§2-A): 전진 자리에 플레이어가 있으면 <b>1칸 밀쳐내고 그 칸을
            // 차지한다</b> — 종전 「인접이면 아무것도 안 함」 가드의 교체. 이동 1의 반 걸음을 넘어
            // 자리 자체를 빼앗는 수가 됐고, 밀린 자리가 A041 파열 지대와 겹치는 연계가 설계 의도다.
            var state = CreateAdvanceState(monsterCoord: new HexCoord(1, 0));

            AdvanceOneOverallTurn(state);

            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(-1, 0)), "전진 방향과 같은 방향으로 1칸 밀린다.");
            Assert.That(Pusher(state).Coord, Is.EqualTo(new HexCoord(0, 0)), "빈 자리를 차지한다.");
        }

        [Test]
        public void AdvancePushRespectsTheKnockbackObstacleRule()
        {
            // 밀릴 자리가 막혀 있으면(기존 넉백 규약 그대로) 충돌 피해만 남고 전진은 무산된다.
            var state = CreateAdvanceState(monsterCoord: new HexCoord(1, 0), blockerCoord: new HexCoord(-1, 0));
            var hpBefore = state.Player.Hp;

            AdvanceOneOverallTurn(state);

            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(0, 0)), "막힌 곳으로는 밀리지 않는다.");
            Assert.That(Pusher(state).Coord, Is.EqualTo(new HexCoord(1, 0)), "자리가 안 비었으면 전진도 없다.");
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - 3 - 2),
                "공격 피해 3에 더해 충돌 2가 들어간다 — 낑긴 값은 기존 넉백 충돌 규약 그대로다.");
        }

        [Test]
        public void AdvanceAfterAttackDoesNothingWhenTheStepIsBlocked()
        {
            // 다른 몬스터가 전진 칸을 막고 있으면 서로 겹치지 않는다 — 이동과 같은 술어를 쓰기 때문이다.
            var state = CreateAdvanceState(monsterCoord: new HexCoord(2, 0), blockerCoord: new HexCoord(1, 0));

            AdvanceOneOverallTurn(state);

            Assert.That(Pusher(state).Coord, Is.EqualTo(new HexCoord(2, 0)));
        }

        [Test]
        public void ThePreviewTelegraphsTheAdvanceAndItLandsThere()
        {
            // 🔴🔴 2026-09-01 #19: 규칙은 예전부터 전진했는데 <b>예고에는 없었다</b> — 「이동 1이라 뒤로
            //      도는 것이 답인데 밀어붙이기가 그 반 걸음을 되찾아 온다」는 저작 의도가 화면에서
            //      안 읽혔다. 예고 칸과 실제 도착 칸이 <b>같은 술어</b>에서 나오는지가 이 시험의 전부다.
            var state = CreateAdvanceState(monsterCoord: new HexCoord(2, 0));

            var telegraphed = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(preview => preview.MonsterId == "pusher")
                .AdvanceAfterAttackCoord;
            Assert.That(telegraphed, Is.Not.Null, "전진하는 턴에는 예고가 그 칸을 말해야 한다.");

            AdvanceOneOverallTurn(state);

            Assert.That(Pusher(state).Coord, Is.EqualTo(telegraphed.Value),
                "예고한 칸에 실제로 선다 — 갈라지면 「예고=명중」의 전진판이 깨진다.");
        }

        [Test]
        public void ThePreviewTellsTheTruthWhenThereIsNoAdvance()
        {
            // 인접이면 이제 밀침 갈래가 있다(2026-09-04) — 예고도 플레이어 칸(전진 목적지)을 말해야 한다.
            var adjacent = CreateAdvanceState(monsterCoord: new HexCoord(1, 0));
            Assert.That(
                adjacent.GetMonsterIntentPreviews(includeUnrevealed: true)
                    .Single(preview => preview.MonsterId == "pusher").AdvanceAfterAttackCoord,
                Is.EqualTo(new HexCoord(0, 0)), "붙어 있으면 밀쳐내고 그 칸으로 들어온다 — 예고=명중의 전진판.");

            // 밀릴 자리까지 막혀 있으면 밀침도 전진도 없다 — 예고가 「없다」고 말해야 한다.
            var pinned = CreateAdvanceState(monsterCoord: new HexCoord(1, 0), blockerCoord: new HexCoord(-1, 0));
            Assert.That(
                pinned.GetMonsterIntentPreviews(includeUnrevealed: true)
                    .Single(preview => preview.MonsterId == "pusher").AdvanceAfterAttackCoord,
                Is.Null, "플레이어가 밀릴 수 없으면 자리가 안 비니 전진 예고도 없다.");

            var blocked = CreateAdvanceState(monsterCoord: new HexCoord(2, 0), blockerCoord: new HexCoord(1, 0));
            Assert.That(
                blocked.GetMonsterIntentPreviews(includeUnrevealed: true)
                    .Single(preview => preview.MonsterId == "pusher").AdvanceAfterAttackCoord,
                Is.Null, "전진 칸이 다른 몬스터로 막혔으면 전진하지 않는다.");
        }

        [Test]
        public void RupturedGroundIsDrawnAsTilesNotAsObjectsOnTop()
        {
            // 🔴 사용자 정정(2026-09-01 #18): 두억시니의 지대는 「오브젝트 생성」이 아니라 <b>지형 파괴</b>다.
            //    종전에는 장판마다 실린더/프리팹이 칸 <b>위에</b> 서서 「누가 뭘 놓았다」로 읽혔고,
            //    무엇보다 「부수면 없어지는 물건」처럼 보였다. 이제 타일 자신이 그린다.
            var state = CreateState();
            var zone = Zone(new HexCoord(2, 0), StatusEffectKind.Rupture, "zone-a");
            state.FieldObjects.Add(zone);

            var cells = state.GetRupturedGroundCells();
            Assert.That(cells, Does.Contain(new HexCoord(2, 0)),
                "부서진 땅은 자기 칸을 오버레이 대상으로 내놓아야 한다.");

            // 🔑 오브젝트로 서지 않는다 — 프리젠터가 이 종류를 통째로 건너뛴다.
            //    (여기서 재는 것은 술어다: 씬 없이 잴 수 있는 유일한 이음매이고, 그 술어가
            //     한 곳뿐이라 프리젠터와 오버레이가 갈라질 수 없다.)
            Assert.That(FieldObjectVisualPresenterSkips(zone), Is.True,
                "지형 상태는 타일 위에 물건을 세우지 않는다.");
        }

        [Test]
        public void ExpiredRupturesLeaveNoTrace()
        {
            // 「몇 턴 뒤 원래대로 돌아온다」가 툴팁의 약속이다 — 만료된 지대가 칸을 계속 물들이면
            // 그 약속이 거짓말이 된다.
            var state = CreateState();
            state.FieldObjects.Add(new FieldObject(
                new HexCoord(2, 0), 0, 0, FieldObjectKind.StatusZone,
                sourceUnitId: "zone-a", statusKind: StatusEffectKind.Rupture));

            Assert.That(state.GetRupturedGroundCells(), Is.Empty);
        }

        /// <summary>프리젠터의 「지형 상태는 건너뛴다」 술어(private static) — 씬 없이 재는 유일한 이음매.</summary>
        private static bool FieldObjectVisualPresenterSkips(FieldObject fieldObject)
        {
            var method = typeof(FieldObjectVisualPresenter).GetMethod(
                "IsGroundState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "IsGroundState가 사라졌다 — 지형 상태 판정을 다시 찾을 것.");
            return (bool)method.Invoke(null, new object[] { fieldObject });
        }

        // ------------------------------------------------------------------ 3칸 몸의 밀어붙이기(2026-09-05)

        [Test]
        public void AdvanceMeasuresContactFromTheBodySoTheTouchingCaseGoesToThePush()
        {
            // 🔴 「이미 닿았는가」를 <b>앵커</b>로 재면 3칸 몸이 이미 플레이어에 닿았는데도 앵커 거리가 2라
            // 곧장 전진을 시도한다. 몸으로 재면 그 자세는 「닿음」이라 설계된 갈래 — <b>밀치고 그 칸을
            // 차지</b>(2026-09-04 §2-A) — 로 바로 들어간다. 두 술어가 갈라져 있던 것이 이번 수정이다.
            //
            // 몸 = 앵커 + 동(1,0) + 남동(0,1). 앵커 (-2,0)이면 몸통은 (-2,0)·(-1,0)·(-2,1)이고
            // 플레이어(0,0)까지 앵커는 2, 몸통 (-1,0)은 1이다.
            var state = CreateTriAdvanceState(new HexCoord(-2, 0));

            AdvanceOneOverallTurn(state);

            Assert.That(state.PlayerCoord, Is.Not.EqualTo(new HexCoord(0, 0)),
                "닿아 있으면 밀친다 — 밀침 갈래로 들어가지 않으면 플레이어는 제자리다.");
            var body = new MonsterBodyShape(0, MonsterFootprints.TriangleOffsets);
            Assert.That(
                MonsterFootprints.TriangleOffsets.Any(offset => Pusher(state).Coord + offset == state.PlayerCoord),
                Is.False,
                "전진이 끝난 뒤 몸통 칸이 플레이어와 겹치면 안 된다.");
        }

        [Test]
        public void AdvanceStepsSidewaysWhenTheBodyCannotFitStraightAhead()
        {
            // 🔴 3칸 몸은 <b>세 칸이 동시에</b> 들어갈 자리를 찾아야 한다. 정면 한 칸만 보면
            // 몸통 한 칸이 물에 걸릴 때마다 전진이 통째로 무산된다("밀어붙이기가 발동을 안 한다").
            // 정면 전진 자리의 몸통 칸 하나만 물로 막고, 옆걸음으로는 들어갈 수 있게 둔다.
            var state = CreateTriAdvanceState(new HexCoord(3, 0), waterCoord: new HexCoord(2, 1));
            var before = Pusher(state).Coord;

            AdvanceOneOverallTurn(state);

            Assert.That(Pusher(state).Coord, Is.Not.EqualTo(before),
                "정면이 몸에 안 맞으면 옆으로라도 파고든다 — 이 완화가 없으면 3칸 몸은 전진을 못 한다.");
            var body = new MonsterBodyShape(0, MonsterFootprints.TriangleOffsets);
            Assert.That(
                body.DistanceFrom(Pusher(state).Coord, state.PlayerCoord),
                Is.LessThanOrEqualTo(body.DistanceFrom(before, state.PlayerCoord)),
                "전진은 멀어지지 않는다.");
        }

        /// <summary>
        /// 3칸 몸(tri) 밀어붙이기 픽스처. 이동 0으로 세워 두어 걸어온 것과 전진을 구분한다.
        /// <paramref name="waterCoord"/>를 주면 그 칸만 통행 불가로 만든다(몸이 안 맞는 자세 재현).
        /// </summary>
        private static CombatState CreateTriAdvanceState(HexCoord monsterCoord, HexCoord? waterCoord = null)
        {
            var cells = HexArea.CellsWithin(new HexCoord(0, 0), 6)
                .Select(coord => new HexCellData(
                    coord,
                    $"cell-{coord.Q}-{coord.R}",
                    waterCoord.HasValue && coord == waterCoord.Value ? "water" : "street",
                    1,
                    baseWalkable: !(waterCoord.HasValue && coord == waterCoord.Value),
                    baseBlocksVision: false))
                .ToArray();

            return new CombatState(
                new HexMapData(cells),
                new HexCoord(0, 0),
                new List<MonsterConfig> { new MonsterConfig("pusher", monsterCoord, 30, definitionId: "M010TRI") },
                TestCombatConfigs.Standard(playerMaxHp: 200, enemyChaseRange: 8),
                monsterCatalog: new MonsterCatalogDefinition(
                    "tri-advance-catalog",
                    "Tri Advance Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            "M010TRI",
                            "3칸 몸 밀어붙이는 놈",
                            "test-melee",
                            "B001",
                            detectionRange: 10,
                            movePerTurn: 0,
                            hp: 30,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("AT95", "찍기", range: 3, areaRadius: 0, damage: 3)
                            },
                            advanceAfterAttack: true,
                            footprintShape: MonsterFootprintShape.Triangle)
                    }));
        }

        // ------------------------------------------------------------------ 픽스처

        private static FieldObject Zone(HexCoord coord, StatusEffectKind kind, string sourceUnitId)
        {
            return new FieldObject(
                coord,
                radius: 0,
                remainingTurns: 3,
                FieldObjectKind.StatusZone,
                value: 0,
                sourceUnitId: sourceUnitId,
                visualRef: MonsterStatusZone.SourceRef,
                hitsPerTick: 1,
                statusKind: kind);
        }

        private static MonsterRuntimeState Pusher(CombatState state) =>
            state.Monsters.Single(candidate => candidate.Id == "pusher");

        private static bool HasStatus(CombatState state, string unitId, StatusEffectKind kind)
        {
            return state.ActiveEffects.Any(effect =>
                effect.Kind == kind && effect.TargetUnitId == unitId && !effect.IsExpired);
        }

        /// <summary>(2,0)만 물인 한 줄 맵 — 지대가 「설 수 있는 땅」만 부수는지 재는 픽스처.</summary>
        private static CombatState CreateStateWithWater()
        {
            var cells = Enumerable.Range(0, 5)
                .Select(q => new HexCellData(
                    new HexCoord(q, 0),
                    $"cell-{q}",
                    q == 2 ? "water" : "street",
                    1,
                    baseWalkable: q != 2,
                    baseBlocksVision: false))
                .ToArray();

            return new CombatState(
                new HexMapData(cells),
                new HexCoord(0, 0),
                System.Array.Empty<MonsterConfig>(),
                TestCombatConfigs.Standard());
        }

        /// <summary>추격 사거리 밖(거리 4)에 세워 두어 지대 말고는 아무것도 체력을 움직이지 않게 한다.</summary>
        // ------------------------------------------------------------------ 「지대 생성!」 알림(2026-09-05)

        [Test]
        public void PlacingAZoneAnnouncesWhichZoneItIs()
        {
            // 🔴 사용자 피드백: "두억시니가 지대 기믹을 실행할 때 플로팅 텍스트가 표시되어야함".
            //    배치 VFX는 이미 있었지만 <b>말이 없었다</b> — 그림만으로는 무엇이 깔렸는지 모른다.
            var state = CreateZoneAttackState();
            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            AdvanceOneOverallTurn(state);

            Assume.That(state.FieldObjects.Objects.Any(field => field.Kind == FieldObjectKind.StatusZone), Is.True,
                "전제: 이 턴에 지대가 실제로 깔렸다 — 안 깔렸으면 알림을 물을 수 없다.");
            var announcements = events
                .Where(e => e.Kind == EffectKind.MonsterTraitTriggered
                            && e.SourceRef == MonsterTraitAnnouncement.StatusZoneCreatedRef)
                .ToList();
            Assert.That(announcements, Is.Not.Empty, "지대를 깔았는데 알림이 없다.");
            Assert.That(announcements[0].StatusKind, Is.EqualTo(StatusEffectKind.Rupture),
                "어느 지대인지는 StatusKind가 말한다 — 이게 없으면 문안이 종류를 못 붙인다.");
        }

        [Test]
        public void TheZoneAnnouncementSpeaksWhereThePlacementCueIsSilent()
        {
            // 🔴 결함의 정체: 배치 큐는 <c>targetUnitId="field"</c>·수치 0이라 표현층의 「칸에 그림만
            //    얹는 배치 큐」 게이트에 걸려 <b>어떤 문안도</b> 띄우지 않는다(폭탄·섬광 장판과 같은 자리).
            //    그래서 말은 몬스터를 겨눈 알림 채널로 따로 나간다 — 두 이벤트의 갈림이 이 계약이다.
            var placement = new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "field",
                targetActorKind: "field",
                sourceRef: "monster.pattern.A041",
                statusKind: StatusEffectKind.Rupture);
            var announcement = new EffectResultEvent(
                EffectKind.MonsterTraitTriggered,
                targetUnitId: "doeok",
                targetActorKind: "monster",
                appliedAmount: 3,
                sourceRef: MonsterTraitAnnouncement.StatusZoneCreatedRef,
                statusKind: StatusEffectKind.Rupture);

            Assert.That(EffectPresentationController.ShouldShowFloatingText(null, placement), Is.False,
                "배치 큐에 문안을 실으려 하면 안 된다 — 게이트가 삼킨다.");
            Assert.That(EffectPresentationController.ShouldShowFloatingText(null, announcement), Is.True,
                "알림은 떠야 한다.");
        }

        [Test]
        public void TheZoneAnnouncementNamesTheStatusItLays()
        {
            Assert.That(
                MonsterTraitAnnouncement.TryGetText(
                    MonsterTraitAnnouncement.StatusZoneCreatedRef, 3, StatusEffectKind.Rupture, out var named),
                Is.True);
            Assert.That(named, Does.Contain(StatusEffectInfo.DisplayName(StatusEffectKind.Rupture)),
                "어느 지대인지가 문안에 없으면 '무엇이 깔렸는지 모른다'가 그대로 남는다.");
            Assert.That(named, Does.Contain("지대 생성"));

            // 종류를 모르면 종류만 빼고 <b>생겼다는 사실</b>은 말한다 — 침묵으로 내려앉지 않는다.
            Assert.That(
                MonsterTraitAnnouncement.TryGetText(
                    MonsterTraitAnnouncement.StatusZoneCreatedRef, 0, null, out var unnamed),
                Is.True);
            Assert.That(unnamed, Does.Contain("지대 생성"));
        }

        /// <summary>
        /// 실제로 <b>공격으로</b> 지대를 까는 픽스처. 손으로 FieldObject를 얹는 다른 테스트들과 달리,
        /// 알림은 배치 경로(PlaceMonsterAttackStatusZones)를 지나야만 나므로 그 경로를 태운다.
        /// </summary>
        private static CombatState CreateZoneAttackState()
        {
            var catalog = ConvertWithZone(shapeId: "cone-mid", zoneOffsets: "1:0", zoneEffect: "zone:Rupture;3")
                .MonsterCatalog;
            return new CombatState(
                CombatState.CreateDemoMap(5),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("zoner", new HexCoord(1, 0), 30, definitionId: "M001") },
                TestCombatConfigs.Standard(playerMaxHp: 200, enemyChaseRange: 6),
                monsterCatalog: catalog);
        }

        private static CombatState CreateState()
        {
            return new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("zone-a", new HexCoord(4, 0), 10),
                    new MonsterConfig("zone-b", new HexCoord(3, 1), 10)
                },
                TestCombatConfigs.Standard());
        }

        /// <summary>
        /// 밀어붙이기 픽스처. 이동 0으로 세워 두어 <b>걸어온 것과 전진을 구분</b>한다 — 이동이 섞이면
        /// 좌표가 바뀐 이유가 둘이 되어 무엇을 시험하는지 모르게 된다.
        /// </summary>
        private static CombatState CreateAdvanceState(HexCoord monsterCoord, HexCoord? blockerCoord = null)
        {
            var configs = new List<MonsterConfig>
            {
                new MonsterConfig("pusher", monsterCoord, 30, definitionId: "M010T")
            };
            if (blockerCoord.HasValue)
            {
                // 🔴 blocker는 자리만 막는 기물이어야 한다 — 같은 정의(공격 3+전진)를 주면 피해가
                //    두 몬스터 몫으로 섞여 「밀침 충돌 2」를 잴 수 없다(밀침 핀 테스트의 산술 전제).
                configs.Add(new MonsterConfig("blocker", blockerCoord.Value, 30, definitionId: "M010B"));
            }

            return new CombatState(
                CombatState.CreateDemoMap(5),
                new HexCoord(0, 0),
                configs,
                // 🔴 TestCombatConfigs.Standard의 기본 추격 사거리는 0이다 — 열어 주지 않으면 몬스터가
                // 공격 자체를 하지 않아 "전진했는가"를 물을 수 없다.
                TestCombatConfigs.Standard(playerMaxHp: 200, enemyChaseRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "advance-test-catalog",
                    "Advance Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            "M010T",
                            "밀어붙이는 놈",
                            "test-melee",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: 30,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("AT90", "찍기", range: 2, areaRadius: 0, damage: 3)
                            },
                            advanceAfterAttack: true),
                        new MonsterCatalogEntry(
                            "M010B",
                            "자리만 막는 놈",
                            "test-melee",
                            "B001",
                            detectionRange: 0,
                            movePerTurn: 0,
                            hp: 30,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("AT91", "허수아비", range: 1, areaRadius: 0, damage: 0)
                            })
                    }));
        }

        private static MonsterCatalogDefinition ShippingMonsterCatalog()
        {
            AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8)));
            return MonsterCatalogCsvConverter.Convert(new MonsterCatalogCsvSource(
                ReadMonsterCsv("monster_catalog.csv"),
                ReadMonsterCsv("monster_attack_patterns.csv"),
                ReadMonsterCsv("monster_pattern_bindings.csv"),
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_vfx_cues.csv")),
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_sound_cues.csv")))).MonsterCatalog;
        }

        private static MonsterCatalogCsvBundle ConvertWithZone(string shapeId, string zoneOffsets, string zoneEffect)
        {
            AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8)));
            var patterns =
                "patternId,displayName,range,damage,effectRef,targeting,vfxCueId,shapeId,statusEffects,"
                + "statusEffectDurationTurns,cooldownTurns,zoneOffsets,zoneEffect\n"
                + $"A001,Basic,1,3,attack.damage,player_in_range,V001,{shapeId},,2,0,{zoneOffsets},{zoneEffect}\n";
            return MonsterCatalogCsvConverter.Convert(new MonsterCatalogCsvSource(
                "monsterId,displayName,archetype,behaviorProfileRef,hp,detectionRange,movePerTurn,attackSpeed,status,visualPrefabPath\n"
                + "M001,Tester,test-melee,B001,30,6,2,1,prototype,\n",
                patterns,
                "monsterId,patternId,order,enabled,overrideWeight,designerNote\nM001,A001,1,true,,\n",
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_vfx_cues.csv")),
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_sound_cues.csv"))));
        }

        private static string ReadMonsterCsv(string fileName) =>
            File.ReadAllText(Path.Combine(CombatCsvPaths.MonsterDirectory, fileName), Encoding.UTF8);

        private static void AdvanceOneOverallTurn(CombatState state)
        {
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }
    }
}

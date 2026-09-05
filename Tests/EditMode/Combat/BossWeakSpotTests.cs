using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 보스 취약 부위(§20-A)의 규칙 계약.
    ///
    /// 이 축은 <b>처벌이 아니라 보상</b>이다 — 찾지 못한 공격은 감쇠 없이 100%다. 그래서 가장 중요한
    /// 회귀 가드는 "판명하지 않아도 피해가 줄지 않는다"이고, 그다음이 "정찰만이 유일한 판명 경로"다
    /// (때려서 판명되면 다단 히트 카드 한 장이 정찰 카드를 통째로 대체한다).
    /// </summary>
    public sealed class BossWeakSpotTests
    {
        private const string BossDefinitionId = "M002";
        private const string BossUnitId = "boss-01";
        private const int BossHp = 400;
        private const int AttackAmount = 10;
        private const string AttackCardId = "A000";
        private const string ScoutCardId = "S000";

        [Test]
        public void MultiCellBossGetsAWeakSpotOnItsEdgeRing()
        {
            var state = CreateState();
            RunMonsterPhase(state);

            var boss = Boss(state);
            Assert.That(TryGetWeakSpot(state, boss, out var coord), Is.True, "몸이 여러 칸이면 취약 부위가 있어야 한다.");
            Assert.That(boss.Coord.DistanceTo(coord), Is.EqualTo(1),
                "후보는 가장자리 링뿐이다 — 중심은 모델이 덮고 있어 표시가 묻히고 '옆구리를 노린다'가 읽히지 않는다.");
        }

        [Test]
        public void SingleCellBossHasNoWeakSpotAndTakesNormalDamage()
        {
            // 반경 0 몬스터의 기존 동작 회귀 가드 — 몸이 한 칸이면 "어느 부위"가 성립하지 않는다.
            var state = CreateState(footprintRadius: 0);
            RunMonsterPhase(state);
            var boss = Boss(state);

            Assert.That(TryGetWeakSpot(state, boss, out _), Is.False);
            Assert.That(state.GetBossWeakSpots(), Is.Empty);

            var hpBefore = boss.Combatant.Hp;
            Attack(state, boss.Coord);
            Assert.That(hpBefore - boss.Combatant.Hp, Is.EqualTo(AttackAmount));
        }

        [Test]
        public void HittingTheWeakSpotWithoutScoutingDealsNormalDamageAndDoesNotRevealIt()
        {
            // 🔴 "때려서 알아내기"는 없다. 남겨두면 1타로 판명되고 2타부터 2배가 붙어, 정찰 카드를 넣을
            // 이유가 히트 수 많은 공격 카드 한 장으로 사라진다.
            var state = CreateState();
            RunMonsterPhase(state);
            var boss = Boss(state);
            Assert.That(TryGetWeakSpot(state, boss, out var weakSpot), Is.True);

            var hpBefore = boss.Combatant.Hp;
            Attack(state, weakSpot);

            Assert.That(hpBefore - boss.Combatant.Hp, Is.EqualTo(AttackAmount),
                "미판명 상태의 취약 부위는 평소 그대로 100%다 — 감쇠도 보너스도 없다.");
            Assert.That(state.GetBossWeakSpots(), Is.Empty, "때려도 판명되지 않는다.");
        }

        [Test]
        public void ScoutingRevealsTheWeakSpotAndDoublesDamageThere()
        {
            var state = CreateState();
            RunMonsterPhase(state);
            var boss = Boss(state);
            Assert.That(TryGetWeakSpot(state, boss, out var weakSpot), Is.True);

            Scout(state, boss.Coord);

            var revealed = state.GetBossWeakSpots().Single();
            Assert.That(revealed.Coord, Is.EqualTo(weakSpot));
            Assert.That(revealed.KnownTurnsRemaining, Is.EqualTo(2), "정찰 한 번은 정확히 2턴을 보장한다(결정 8).");

            var hpBefore = boss.Combatant.Hp;
            Attack(state, weakSpot);
            Assert.That(hpBefore - boss.Combatant.Hp, Is.EqualTo(AttackAmount * 2));
        }

        [Test]
        public void OtherBodyCellsStayAtFullDamageEvenWhileKnown()
        {
            var state = CreateState();
            RunMonsterPhase(state);
            var boss = Boss(state);
            Assert.That(TryGetWeakSpot(state, boss, out var weakSpot), Is.True);
            Scout(state, boss.Coord);

            // 가장자리 링에서 취약 부위가 아닌 칸.
            var otherEdge = HexArea.CellsWithin(boss.Coord, 1)
                .First(coord => coord != boss.Coord && coord != weakSpot);

            var hpBefore = boss.Combatant.Hp;
            Attack(state, otherEdge);
            Assert.That(hpBefore - boss.Combatant.Hp, Is.EqualTo(AttackAmount));
        }

        [Test]
        public void PreviewNeverLeaksTheWeakSpotWhileItIsUnknown()
        {
            // 🔴 미리보기가 실효값을 그대로 보여주면, 미판명 상태에서 몸통 6칸에 호버만 해봐도 숫자가
            // 뛰는 칸이 취약 부위임을 알 수 있다. 이 스캔은 매 턴 공짜로 반복 가능하므로 정찰 축이
            // 통째로 무력화된다. 미판명이면 집행도 100%이므로 이 핀은 거짓말이 아니라 <b>일치</b>다.
            var state = CreateState();
            RunMonsterPhase(state);
            var boss = Boss(state);
            Assert.That(TryGetWeakSpot(state, boss, out var weakSpot), Is.True);
            var card = state.ActionDeck.Hand.First(candidate => candidate.EffectType == CardEffectType.Attack);

            var previews = HexArea.CellsWithin(boss.Coord, 1)
                .Select(coord => state.GetDisplayValue(card, coord))
                .Distinct()
                .ToList();
            Assert.That(previews, Has.Count.EqualTo(1),
                "미판명이면 몸통 어느 칸을 호버해도 숫자가 같아야 한다 — 다르면 그 자체가 정답 유출이다.");

            Scout(state, boss.Coord);
            Assert.That(state.GetDisplayValue(card, weakSpot), Is.EqualTo(AttackAmount * 2),
                "판명 중에는 실효값(200%)을 그대로 보여준다 — 여기서 100%를 보여주면 그건 거짓말이다.");
        }

        [Test]
        public void KnownWeakSpotLastsExactlyTwoTurnsAndDoesNotMoveWhileKnown()
        {
            var state = CreateState();
            RunMonsterPhase(state);
            var boss = Boss(state);
            Scout(state, boss.Coord);
            var weakSpot = state.GetBossWeakSpots().Single().Coord;

            RunMonsterPhase(state); // 2 → 1
            Assert.That(state.GetBossWeakSpots().Single().Coord, Is.EqualTo(weakSpot),
                "🔴 판명 중에는 자리가 재선정되지 않는다.");
            Assert.That(state.GetBossWeakSpots().Single().KnownTurnsRemaining, Is.EqualTo(1));
            Assert.That(state.GetBossWeakSpots().Single().IsExpiringThisTurn, Is.True,
                "마지막 턴은 오버레이가 만료 임박을 알릴 수 있어야 한다 — 아무 예고 없이 사라지면 버그로 읽힌다.");

            var hpBefore = boss.Combatant.Hp;
            Attack(state, weakSpot);
            Assert.That(hpBefore - boss.Combatant.Hp, Is.EqualTo(AttackAmount * 2), "둘째 턴에도 2배가 유지된다.");

            RunMonsterPhase(state); // 1 → 0
            Assert.That(state.GetBossWeakSpots(), Is.Empty, "🔴 판명은 정확히 2턴이다.");
        }

        [Test]
        public void UnknownWeakSpotIsRerolledEveryMonsterPhase()
        {
            // 미판명이면 자리가 계속 바뀐다(결정 1) — 그래서 "한 번 찍어두고 계속 때리기"가 안 된다.
            var state = CreateState();
            var boss = Boss(state);
            var seen = new HashSet<HexCoord>();
            for (var i = 0; i < 30; i++)
            {
                RunMonsterPhase(state);
                Assert.That(TryGetWeakSpot(state, boss, out var coord), Is.True);
                seen.Add(coord);
            }

            Assert.That(seen.Count, Is.GreaterThan(1),
                "30번 재선정했는데 자리가 한 곳뿐이면 재선정이 돌지 않는 것이다(6칸 후보에서 확률적으로 불가능).");
        }

        [Test]
        public void RescoutingWhileKnownRefreshesTheCounterWithoutMovingTheSpot()
        {
            var state = CreateState();
            RunMonsterPhase(state);
            var boss = Boss(state);
            Scout(state, boss.Coord);
            var weakSpot = state.GetBossWeakSpots().Single().Coord;

            RunMonsterPhase(state); // 2 → 1
            Scout(state, boss.Coord);

            var refreshed = state.GetBossWeakSpots().Single();
            Assert.That(refreshed.Coord, Is.EqualTo(weakSpot), "자리는 그대로다.");
            Assert.That(refreshed.KnownTurnsRemaining, Is.EqualTo(2), "카운터만 2로 갱신된다(낭비지만 플레이어의 선택이다).");
        }

        [Test]
        public void SuspendRoundTripPreservesTheOffsetAndTheKnownCounter()
        {
            var state = CreateState();
            RunMonsterPhase(state);
            var boss = Boss(state);
            Scout(state, boss.Coord);
            var before = state.GetBossWeakSpots().Single();

            var snapshot = state.CreateSuspendSnapshot();
            var saved = snapshot.BossPhaseTracks.Single();
            Assert.That(saved.HasWeakSpot, Is.True);
            Assert.That(saved.WeakSpotKnownTurnsRemaining, Is.EqualTo(2));

            var resumed = CreateState();
            resumed.RestoreFromSuspend(snapshot);
            var after = resumed.GetBossWeakSpots().Single();
            Assert.That(after.Coord, Is.EqualTo(before.Coord),
                "왕복하지 않으면 저장/재개로 자리를 다시 굴리는 세이브 스컴이 된다.");
            Assert.That(after.KnownTurnsRemaining, Is.EqualTo(before.KnownTurnsRemaining));
        }

        [Test]
        public void WeakSpotIsStoredAsAnOffsetSoItFollowsTheBossWhenItMoves()
        {
            // 🔴 절대 좌표로 저장하면 보스가 걷거나 전멸기로 중앙 점프하는 순간 취약 부위가 몸 밖에 남는다.
            var state = CreateState();
            RunMonsterPhase(state);
            var boss = Boss(state);
            Scout(state, boss.Coord);
            var before = state.GetBossWeakSpots().Single().Coord;
            var offset = new HexCoord(before.Q - boss.Coord.Q, before.R - boss.Coord.R);

            boss.Coord = new HexCoord(boss.Coord.Q + 2, boss.Coord.R);

            var after = state.GetBossWeakSpots().Single().Coord;
            Assert.That(after, Is.EqualTo(new HexCoord(boss.Coord.Q + offset.Q, boss.Coord.R + offset.R)));
            Assert.That(boss.Coord.DistanceTo(after), Is.EqualTo(1), "옮긴 뒤에도 취약 부위는 몸통 안이다.");
        }

        // --- helpers ------------------------------------------------------------------------------

        /// <summary>
        /// public 투영(<see cref="MonsterRuntimeState"/>)에는 취약 부위도 좌표 대입 표면도 없다 —
        /// 이 시험의 관찰 대상이 바로 그것이라 내부 런타임 객체를 직접 잡는다(BossLeapAttackTests 선례).
        /// </summary>
        private static MonsterRuntime Boss(CombatState state)
        {
            var runtimeMonsters = (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            return runtimeMonsters.Single(monster => monster.Id == BossUnitId);
        }

        private static bool TryGetWeakSpot(CombatState state, MonsterRuntime boss, out HexCoord coord)
        {
            return state.TryGetBossWeakSpotCoord(boss, out coord);
        }

        private static void Attack(CombatState state, HexCoord target)
        {
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerAttack(target, AttackCardId), Is.True, state.LastFailureReason);
        }

        private static void Scout(CombatState state, HexCoord target)
        {
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerScout(target, ScoutCardId), Is.True, state.LastFailureReason);
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            if (state.Phase == CombatPhase.PlayerMovement)
            {
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterMovement();
            }
        }

        /// <summary>한 턴을 끝까지 돌려 몬스터 페이즈(기믹 결의)를 한 번 지나게 한다.</summary>
        private static void RunMonsterPhase(CombatState state)
        {
            if (state.Phase == CombatPhase.PlayerMovement)
            {
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterMovement();
            }

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        private static CombatState CreateState(int footprintRadius = 1)
        {
            var playerCoord = new HexCoord(-3, 0);
            var bossCoord = new HexCoord(0, 0);
            // 취약 부위 기믹은 저작 노브가 없다 — mechanicParams가 비어 있어도 성립해야 한다.
            var profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + ",테스트 보스,TurnCount,weak-spot,,,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + $",1,0,0,0,0,1,{footprintRadius},,\n";

            return new CombatState(
                CombatState.CreateDemoMap(8),
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossHp, definitionId: BossDefinitionId, spawnRole: "boss") },
                new CombatConfig(200, BossHp, 2, 1, 6, 6, 0, 1, 3, playerVisionRange: 8, actionBudget: 8, actionHandSize: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "boss-weak-spot-catalog",
                    "Boss Weak Spot Catalog",
                    new[]
                    {
                        new MonsterCatalogEntry(
                            BossDefinitionId,
                            "테스트 보스",
                            "test-melee",
                            "B001",
                            detectionRange: 0,
                            movePerTurn: 0,
                            hp: BossHp,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("A100", "기본", 1, 0, 0, cooldownTurns: 0, phaseMin: 0)
                            })
                    }),
                cardCatalog: WeakSpotTestCardCatalog(),
                bossCatalog: BossCatalogCsvConverter.Convert(
                    new BossCatalogCsvSource(profiles, phases, "boss-weak-spot-test", "Boss Weak Spot Test")));
        }

        /// <summary>
        /// 사거리 6짜리 단일 대상 공격 + <c>blast-1</c> 정찰. 정찰 반경 1은 반경 1 footprint(7칸)를
        /// 통째로 덮으므로 <b>정찰 1장이면 항상 판명된다</b> — 이 축의 비용은 추측이 아니라 카드 한 장이다.
        /// </summary>
        private static CardCatalogDefinition WeakSpotTestCardCatalog()
        {
            return new CardCatalogDefinition(
                "boss-weak-spot-cards",
                "Boss Weak Spot Cards",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.MoveBasicId, "이동", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, CardEffectRefs.MoveBasic, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        AttackCardId, "공격", CardCategory.Action, CardEffectType.Attack,
                        1, 6, AttackAmount, CardEffectRefs.AttackDamage, "enemy_in_range",
                        status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        ScoutCardId, "정찰", CardCategory.Action, CardEffectType.Scout,
                        1, 6, 0, CardEffectRefs.ScoutReveal, "walkable_map_cell", areaRadius: 1,
                        status: CardCatalogStatus.Approved)
                });
        }
    }
}

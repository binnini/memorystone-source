using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 2026-09-04 요괴 특성 리워크의 핀(정본: docs/prompts/yokai-six-trait-pattern-rework-handoff.md §2·§6).
    ///
    /// <para>담력 시험(재설정형 거리 힘) · 홀림(오라 봉인 진입/이탈/사망 토글) · 소매치기(절도·부분
    /// 절도·처치 반환·세이브 왕복)를 문다. 밀어붙이기 밀침은 기존 전진 스위트
    /// (<see cref="MonsterStatusZoneTests"/>)가 함께 문다 — 전진과 한 몸인 규칙이라 그쪽이 자연스럽다.</para>
    /// </summary>
    public sealed class YokaiTraitReworkTests
    {
        private MonsterTraitCatalogDefinition previousTraits;

        /// <summary>
        /// 범위 오버레이는 <c>reachKind</c>(monster_traits.csv)에서 갈리므로 특성 표가 있어야 한다.
        /// 인라인 픽스처라 Assets/를 읽지 않는다 — 출하 저작과의 대조는 MonsterTraitCatalogTests 소관.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            previousTraits = MonsterTraitCatalogProvider.Active;
            MonsterTraitCatalogProvider.Active = MonsterTraitCatalogCsv.ConvertText(
                "traitId,displayNameKo,keywordRef,glyph,badgeColorHex,badgeSortRank,badgeVisibility,reachKind,announceRef,designerNote\n"
                + "strengthDistance,담력 시험,담력 시험,담,C46A17,10,always,distanceStrength,,\n"
                + "auraSeal,홀림,홀림,홀,7A4DA8,12,always,auraRadius,,\n"
                + "aftermath,뒤끝,뒤끝,뒤,854D9E,11,always,aftermath,,\n");
        }

        [TearDown]
        public void TearDown()
        {
            MonsterTraitCatalogProvider.Active = previousTraits;
        }

        // ------------------------------------------------------------------ 담력 시험(거구귀)

        [Test]
        public void DistanceStrengthFollowsTheDistanceTableAndResets()
        {
            var state = CreateStrengthState(new HexCoord(4, 0));
            var monster = FirstMonster(state);

            // 재설정형 — 매 턴 힘 = clamp((거리 − 1) × 2, 0, 6). 누적이 아니므로 마지막의 인접 0이
            // 「그 자리에서 꺼진다」를 함께 문다(구 겁먹음의 누적+리셋 문법과 다른 축).
            foreach (var (coord, expected) in new[]
                     {
                         (new HexCoord(1, 0), 0),
                         (new HexCoord(2, 0), 2),
                         (new HexCoord(3, 0), 4),
                         (new HexCoord(4, 0), 6),
                         (new HexCoord(5, 0), 6),
                         (new HexCoord(1, 0), 0),
                     })
            {
                monster.Coord = coord;
                Tick(state);
                Assert.That(monster.AgitationStacks, Is.EqualTo(expected), $"몬스터 위치 {coord}");
            }
        }

        /// <summary>
        /// 담력 시험이 <b>힘 상태이상</b>으로 나타나는가(2026-09-04 사용자 확정). 배지에 뜨는 수치와
        /// 피해에 실리는 값이 같은 ActiveEffect 하나여야 한다 — 종전에는 카운터가 상태이상 목록 밖에
        /// 있어서 「스택은 있는데 상태 목록엔 없는」 값이 하나 떠 있었다.
        /// </summary>
        [Test]
        public void DistanceStrengthGrantsMightThatTracksTheDistance()
        {
            var state = CreateStrengthState(new HexCoord(4, 0));
            var monster = FirstMonster(state);

            int MightAmount() => state.ActiveEffects
                .Where(effect => effect.Kind == StatusEffectKind.Might
                                 && string.Equals(effect.TargetUnitId, monster.Id, System.StringComparison.Ordinal))
                .Select(effect => effect.Amount)
                .DefaultIfEmpty(0)
                .Sum();

            monster.Coord = new HexCoord(3, 0);
            Tick(state);
            Assert.That(MightAmount(), Is.EqualTo(4), "거리 3 → 힘 4가 상태이상으로 붙는다.");

            monster.Coord = new HexCoord(5, 0);
            Tick(state);
            Assert.That(MightAmount(), Is.EqualTo(6), "멀어지면 같은 효과가 제자리에서 커진다 — 두 벌이 되지 않는다.");
            Assert.That(
                state.ActiveEffects.Count(effect =>
                    effect.Kind == StatusEffectKind.Might
                    && string.Equals(effect.TargetUnitId, monster.Id, System.StringComparison.Ordinal)),
                Is.EqualTo(1),
                "매 턴 다시 부여해도 인스턴스는 하나다(오라 봉인과 같은 제자리 갱신 규약).");

            monster.Coord = new HexCoord(1, 0);
            Tick(state);
            Assert.That(MightAmount(), Is.EqualTo(0), "붙으면 힘이 0이 되고 효과 자체가 걷힌다.");
            Assert.That(
                state.ActiveEffects.Any(effect =>
                    effect.Kind == StatusEffectKind.Might
                    && string.Equals(effect.TargetUnitId, monster.Id, System.StringComparison.Ordinal)),
                Is.False,
                "0스택 힘이 남아 있으면 「힘 0」 빈 배지가 뜬다.");
        }

        /// <summary>
        /// 담력 시험의 범위 오버레이 — 힘이 붙는 칸(몸 거리 2 이상)을 칠하고 인접 링은 비운다.
        /// 「붙으면 0」이 그림 하나로 읽혀야 하므로 <b>안 칠해진 칸</b>이 이 오버레이의 답이다.
        /// </summary>
        [Test]
        public void AgitationMightSurvivesTurnsWhereTheStackDoesNotMove()
        {
            // 🔴 2026-09-05 실플레이 피드백: "힘이 몇 턴 있다가 사라져버림". 스택을 올리는·내리는 갈래
            // 안에서만 투영(SyncMonsterMightEffect)이 돌아, <b>감지 중인데 방어·정찰을 안 쓴 턴</b>에는
            // 아무 갈래도 안 타고 힘의 갱신 지속이 그대로 흘러 만료됐다. 카운터가 정본이므로
            // 카운터가 살아 있는 한 힘도 살아 있어야 한다.
            var state = CreateAgitationState();
            var monster = FirstMonster(state);
            state.SetMonsterAgitationStacks(monster, 2);
            Assume.That(MightAmountOf(state, monster.Id), Is.EqualTo(2));

            // 스택이 움직일 이유가 없는 턴을 <b>진짜로</b> 여러 번 넘긴다 — 힘의 갱신 지속(2턴)보다 길게.
            // 🔑 TickEnemyGrammarPerTurn만 부르면 상태이상 만료 틱이 안 돌아 이 테스트가 물지 않는다
            // (돌연변이 검사에서 실제로 안 빨개졌다). 전체 턴을 돌려야 만료가 재현된다.
            for (var turn = 0; turn < 5; turn++)
            {
                AdvanceOneOverallTurn(state);
            }

            Assert.That(MightAmountOf(state, monster.Id), Is.EqualTo(2),
                "스택이 그대로면 힘도 그대로다 — 턴으로 만료되면 안 된다.");
        }

        private static int MightAmountOf(CombatState state, string monsterId)
        {
            var sourceRef = CombatState.MonsterMightSourceRef(monsterId);
            foreach (var effect in state.ActiveEffects)
            {
                if (effect.Kind == StatusEffectKind.Might
                    && !effect.IsExpired
                    && string.Equals(effect.SourceRef, sourceRef, System.StringComparison.Ordinal))
                {
                    return effect.Amount;
                }
            }

            return 0;
        }

        /// <summary>누적형 약오름 픽스처 — 감지는 하되 스택이 저절로 움직이지 않는 자세.</summary>
        private static CombatState CreateAgitationState()
        {
            return new CombatState(
                CombatState.CreateDemoMap(6),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("bull", new HexCoord(3, 0), 25, definitionId: "M003T") },
                TestCombatConfigs.Standard(playerMaxHp: 200, enemyChaseRange: 8),
                monsterCatalog: new MonsterCatalogDefinition(
                    "agitation-test-catalog",
                    "Agitation Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            "M003T",
                            "약오르는 놈",
                            "test-melee",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: 25,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("AT94", "박치기", range: 1, areaRadius: 0, damage: 3)
                            },
                            agitationMaxStacks: 3)
                    }));
        }

        [Test]
        public void DistanceStrengthReachPaintsEveryCellWhereMightSticks()
        {
            var state = CreateStrengthState(new HexCoord(4, 0));
            var monster = FirstMonster(state);
            monster.Coord = new HexCoord(3, 0);

            Assert.That(
                state.TryGetMonsterTraitReach(monster.Id, MonsterTraitIds.StrengthDistance, out var cells),
                Is.True);
            Assert.That(cells.Count, Is.GreaterThan(0));
            Assert.That(
                cells.All(cell => monster.Coord.DistanceTo(cell) >= 2), Is.True,
                "인접 링이 칠해지면 「붙으면 0」이 거짓말이 된다.");
            Assert.That(
                cells.Contains(monster.Coord), Is.False,
                "몬스터가 선 칸 자체는 힘이 붙는 자리가 아니다.");
            // 2026-09-05 실플레이: 「거리 2 이상 전부」가 맵 전체를 칠해 범위가 아니라 배경으로 읽혔다.
            Assert.That(
                cells.All(cell => monster.Coord.DistanceTo(cell) <= CombatState.DistanceStrengthReachRadius), Is.True,
                "오버레이는 상한 반경까지만 — 맵 전체를 칠하면 범위가 아니다.");
            Assert.That(
                cells.Any(cell => monster.Coord.DistanceTo(cell) == CombatState.DistanceStrengthReachRadius), Is.True,
                "상한 반경의 링은 칠해져야 한다(경계가 하나 안쪽으로 밀리면 안 된다).");
            Assert.That(
                state.Map.AllCells.Any(cell => monster.Coord.DistanceTo(cell.Coord) > CombatState.DistanceStrengthReachRadius),
                Is.True,
                "전제: 픽스처 맵에 상한 밖 칸이 있어야 「전체 칠하기」와 구별된다.");
        }

        // ------------------------------------------------------------------ 홀림(그슨새 · 오라 봉인)

        [Test]
        public void AuraSealTogglesWithDistanceAndDeath()
        {
            var state = CreateAuraState(new HexCoord(2, 0));
            var monster = FirstMonster(state);

            Tick(state);
            Assert.That(AuraSealCount(state, monster.Id), Is.EqualTo(1), "거리 2 이내 — 봉인이 걸린다.");

            Tick(state);
            Assert.That(AuraSealCount(state, monster.Id), Is.EqualTo(1), "매 턴 갱신은 제자리 교체다 — 겹쳐 쌓이지 않는다.");

            monster.Coord = new HexCoord(3, 0);
            Tick(state);
            Assert.That(AuraSealCount(state, monster.Id), Is.Zero, "반경을 벗어나면 그 턴에 풀린다.");

            monster.Coord = new HexCoord(1, 0);
            Tick(state);
            Assert.That(AuraSealCount(state, monster.Id), Is.EqualTo(1), "다시 들어오면 다시 잠긴다.");

            monster.Combatant.ApplyDamage(monster.Combatant.Hp);
            Tick(state);
            Assert.That(AuraSealCount(state, monster.Id), Is.Zero, "죽으면 오라도 걷힌다.");
        }

        [Test]
        public void AuraSealLocksAndReleasesTheMomentThePlayerCrossesTheRadius()
        {
            // 🔴 2026-09-05 계약 변경(사용자 요구): 홀림은 턴이 끝날 때가 아니라 <b>실시간</b>이다.
            // 규칙 문서상 이미 「반경 안에 있는 동안만」이었는데 집행이 턴 틱에만 걸려 있어,
            // 이동 카드로 들어가도 턴이 끝나야 잠기고 벗어나도 턴이 끝나야 풀렸다.
            var state = CreateAuraState(new HexCoord(2, 0));
            var monster = FirstMonster(state);
            Assume.That(AuraSealCount(state, monster.Id), Is.Zero, "전제: 아직 반경 밖(거리 2 초과가 아니라 미판정)");

            // 반경 2 안으로 <b>이동</b>한다 — 턴을 넘기지 않는다.
            Assert.That(state.TryDebugMovePlayer(new HexCoord(1, 0)), Is.True, state.LastFailureReason);
            Assert.That(AuraSealCount(state, monster.Id), Is.EqualTo(1),
                "들어서는 순간 잠겨야 한다 — 턴이 끝나기를 기다리지 않는다.");

            Assert.That(state.TryDebugMovePlayer(new HexCoord(5, 0)), Is.True, state.LastFailureReason);
            Assert.That(AuraSealCount(state, monster.Id), Is.Zero,
                "벗어나는 순간 풀려야 한다.");
        }

        /// <summary>홀림의 범위 오버레이 = 오라 반경. 잠기는 판정과 <b>같은 거리</b>를 그린다.</summary>
        [Test]
        public void AuraSealReachPaintsExactlyTheRadiusThatLocksCards()
        {
            var state = CreateAuraState(new HexCoord(2, 0));
            var monster = FirstMonster(state);

            Assert.That(
                state.TryGetMonsterTraitReach(monster.Id, MonsterTraitIds.AuraSeal, out var cells), Is.True);
            Assert.That(
                cells.All(cell => monster.Coord.DistanceTo(cell) <= 2), Is.True,
                "반경 밖을 칠하면 「벗어나면 풀린다」가 거짓말이 된다.");
            Assert.That(
                cells.Contains(monster.Coord), Is.True, "오라의 중심도 반경 안이다.");
        }

        // ------------------------------------------------------------------ 소매치기(야광귀)

        [Test]
        public void PickpocketStealsOnHitAndReturnsAllOnKill()
        {
            var state = CreatePickpocketState();
            EmptyWallet(state);
            state.PlayerInventory.Wallet.Add(40);
            var monster = FirstMonster(state);

            AdvanceOneOverallTurn(state);

            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(25), "명중 시 엽전 15 절도.");
            Assert.That(monster.StolenMoney, Is.EqualTo(15), "절도액은 개체에 쌓인다.");

            monster.Combatant.ApplyDamage(monster.Combatant.Hp);
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);

            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(40),
                "처치 시 전액 반환 — 뒤끝의 유일한 이로운 갈래이자 쫓아가 잡을 이유다.");
            Assert.That(monster.StolenMoney, Is.Zero);
        }

        [Test]
        public void EscapingPickpocketIsPresentedAsAMoveNotABlink()
        {
            // 🔴 2026-09-05 사용자 요구: "순간 이동이 아니라 도망가는 지점까지 이동 애니메이션, SFX가
            // 재생되도록". 규칙(결정적 자리 선정)은 그대로 두고 <b>연출 채널</b>만 바꿨다 — 밀어붙이기
            // 전진이 쓰는 「공격 뒤 자리 이동」 칸에 실으면 어셈블러가 공격 비트 직후에 걸음을 낸다.
            var state = CreatePickpocketState();
            EmptyWallet(state);
            state.PlayerInventory.Wallet.Add(40);
            var before = FirstMonster(state).Coord;

            AdvanceOneOverallTurn(state);

            var monster = FirstMonster(state);
            Assume.That(monster.Coord, Is.Not.EqualTo(before), "전제: 이번 턴에 실제로 달아났다.");

            var record = state.LastMonsterActionRecords.Single(r => r.MonsterId == monster.Id);
            Assert.That(record.AdvancedFrom, Is.EqualTo(before),
                "달아남이 「공격 뒤 자리 이동」으로 실려야 걸음 연출이 붙는다 — 안 실으면 순간이동으로 보인다.");
            Assert.That(record.AdvancedTo, Is.EqualTo(monster.Coord));
        }

        [Test]
        public void ReturnedMoneyIsRememberedForTheLootList()
        {
            // 🔴 2026-09-05 사용자 요구: 돌려받은 엽전이 전리품 목록에 「(반환)」으로 따로 떠야 한다.
            // 지갑에는 처치 시점에 들어가고(목록을 건너뛰어도 원금은 안 잃는다), 액수는 표시 전용
            // 필드에 남는다 — StolenMoney는 반환과 함께 0이 되므로 그 값으로는 줄을 세울 수 없다.
            var state = CreatePickpocketState();
            EmptyWallet(state);
            state.PlayerInventory.Wallet.Add(40);
            var monster = FirstMonster(state);
            AdvanceOneOverallTurn(state);
            Assume.That(monster.StolenMoney, Is.EqualTo(15));

            monster.Combatant.ApplyDamage(monster.Combatant.Hp);
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);

            Assert.That(monster.RestoredMoney, Is.EqualTo(15),
                "반환액이 남아 있어야 전리품 목록이 「(반환)」 줄을 세운다.");
            Assert.That(monster.StolenMoney, Is.Zero);
            Assert.That(state.Monsters.Single(snapshot => snapshot.Id == monster.Id).RestoredMoney,
                Is.EqualTo(15), "스냅샷에 실려야 표현 계층이 읽는다.");
        }

        [Test]
        public void PickpocketTakesOnlyWhatThePlayerHas()
        {
            var state = CreatePickpocketState();
            EmptyWallet(state);
            state.PlayerInventory.Wallet.Add(7);
            var monster = FirstMonster(state);

            AdvanceOneOverallTurn(state);

            Assert.That(state.PlayerInventory.Wallet.Balance, Is.Zero, "있는 만큼만 훔친다 — 음수 금지.");
            Assert.That(monster.StolenMoney, Is.EqualTo(7), "부분 절도도 개체에 쌓인다.");
        }

        [Test]
        public void StolenMoneySurvivesASuspendRoundTrip()
        {
            // §7: 세이브 왕복에 절도액이 실려야 한다 — 잃으면 재개가 훔친 돈을 증발시킨다.
            var state = CreatePickpocketState();
            EmptyWallet(state);
            state.PlayerInventory.Wallet.Add(40);
            AdvanceOneOverallTurn(state);
            Assert.That(FirstMonster(state).StolenMoney, Is.EqualTo(15), "전제: 실제로 훔쳤다.");

            var restored = CreatePickpocketState();
            restored.RestoreFromSuspend(state.CreateSuspendSnapshot());

            Assert.That(FirstMonster(restored).StolenMoney, Is.EqualTo(15));
        }

        // ------------------------------------------------------------------ 픽스처

        private static void Tick(CombatState state) => Invoke(state, "TickEnemyGrammarPerTurn");

        private static void Invoke(CombatState state, string methodName, params object[] args)
        {
            var method = typeof(CombatState).GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"{methodName}이 사라졌다.");
            method.Invoke(state, args);
        }

        private static MonsterRuntime FirstMonster(CombatState state)
        {
            var monsters = (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            return monsters[0];
        }

        private static int AuraSealCount(CombatState state, string monsterId)
        {
            var sourceRef = CombatState.AuraSealSourceRef(monsterId);
            return state.ActiveEffects.Count(effect =>
                effect.Kind == StatusEffectKind.Seal
                && effect.TargetUnitId == "player"
                && effect.SourceRef == sourceRef
                && !effect.IsExpired);
        }

        private static void EmptyWallet(CombatState state)
        {
            var balance = state.PlayerInventory.Wallet.Balance;
            if (balance > 0)
            {
                Assert.That(state.PlayerInventory.Wallet.TrySpend(balance, out _), Is.True);
            }
        }

        private static void AdvanceOneOverallTurn(CombatState state)
        {
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        private static CombatState CreateStrengthState(HexCoord monsterCoord)
        {
            return new CombatState(
                CombatState.CreateDemoMap(6),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("gulper", monsterCoord, 40, definitionId: "M012T") },
                TestCombatConfigs.Standard(playerMaxHp: 200),
                monsterCatalog: new MonsterCatalogDefinition(
                    "strength-test-catalog",
                    "Strength Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            "M012T",
                            "담력 시험 놈",
                            "test-melee",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: 40,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("AT92", "물기", range: 1, areaRadius: 0, damage: 3)
                            },
                            agitationMaxStacks: 6,
                            agitationConditionRef: MonsterAgitationCondition.StrengthDistanceRef)
                    }));
        }

        private static CombatState CreateAuraState(HexCoord monsterCoord)
        {
            return new CombatState(
                CombatState.CreateDemoMap(6),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("charmer", monsterCoord, 22, definitionId: "M013T") },
                TestCombatConfigs.Standard(playerMaxHp: 200),
                monsterCatalog: new MonsterCatalogDefinition(
                    "aura-test-catalog",
                    "Aura Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            "M013T",
                            "홀리는 놈",
                            "test-ranged",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: 22,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("AT93", "후리기", range: 1, areaRadius: 0, damage: 3)
                            },
                            hiddenTraitRef: MonsterAuraSeal.AuraSealRef,
                            hiddenTraitParam: "radius=2;cards=1")
                    }));
        }

        private static CombatState CreatePickpocketState()
        {
            return new CombatState(
                CombatState.CreateDemoMap(5),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("thief", new HexCoord(1, 0), 14, definitionId: "M014T") },
                TestCombatConfigs.Standard(playerMaxHp: 200, enemyChaseRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "pickpocket-test-catalog",
                    "Pickpocket Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            "M014T",
                            "소매치기 놈",
                            "test-melee",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: 14,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                // 출하 A051처럼 <b>훔치고 달아난다</b>(selfTeleportRadius) — 달아남이
                                // 연출 채널에 실리는지까지 이 픽스처가 재려면 두 축이 함께 있어야 한다.
                                new MonsterAttackPattern(
                                    "AT94", "엽전 채기", range: 1, areaRadius: 0, damage: 2,
                                    selfTeleportRadius: 2, stealMoneyAmount: 15)
                            },
                            onDeathEffectRef: MonsterDeathAftermath.RestoreRef,
                            onDeathEffectParam: "kind=Money")
                    }));
        }
    }
}

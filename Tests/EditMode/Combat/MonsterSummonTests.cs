using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 패턴 소환(요괴 트랙 §4-4 · 구미호 A047)의 계약: 저작 파서 · 동시 상한 · <b>밀림 규칙</b> ·
    /// 예고와 집행의 자리 일치.
    ///
    /// <para>🔴 Q3 확정 — <b>소환 자리는 막을 수 없다</b>. 막혀 있으면 근처 빈 칸으로 밀려나 어쨌든
    /// 나온다. 자리를 막아 소환을 무력화할 수 있으면 정답이 "부르는 쪽을 먼저 처리한다"에서 "자리에
    /// 서 있으면 된다"로 바뀌어 패턴이 위협이 아니라 퍼즐 한 줄이 된다. 그리고 밀림은 <b>결정적</b>이라
    /// 세이브 재개마다 같은 상황이 같게 풀린다.</para>
    /// </summary>
    public sealed class MonsterSummonTests
    {
        private const string SummonerId = "gumiho";
        private const string SummonerDefinitionId = "M011T";
        private const string MinionDefinitionId = "M007T";

        // ------------------------------------------------------------------ 저작 파서

        [Test]
        public void SummonSpecParserGuardsTheAuthoring()
        {
            Assert.That(MonsterSummonSpec.TryParse(string.Empty, out var none, out _), Is.True);
            Assert.That(none.HasSummon, Is.False, "저작 없음이 대다수다.");

            Assert.That(MonsterSummonSpec.TryParse("M007;2", out _, out var prefix), Is.False);
            Assert.That(prefix, Does.Contain("summon:"));
            Assert.That(MonsterSummonSpec.TryParse("summon:M007", out _, out _), Is.False, "마리 수가 필수다.");
            Assert.That(MonsterSummonSpec.TryParse("summon:M007;0", out _, out _), Is.False, "0마리 소환은 저작 실수다.");
            Assert.That(MonsterSummonSpec.TryParse("summon:M007;3;2", out _, out var cap), Is.False);
            Assert.That(cap, Does.Contain("동시 상한"), "부르자마자 상한을 넘는 저작은 거부한다.");

            Assert.That(MonsterSummonSpec.TryParse("summon:M007;2", out var spec, out _), Is.True);
            Assert.That(spec.MonsterDefinitionId, Is.EqualTo("M007"));
            Assert.That(spec.Count, Is.EqualTo(2));
            Assert.That(spec.MaxAlive, Is.EqualTo(2), "상한을 생략하면 마리 수와 같다.");
        }

        [Test]
        [Category("ShippingData")]
        public void SummonWithoutSeatsIsRejectedAtImport()
        {
            // 자리는 형상 칸이 정한다 — 형상이 없거나 칸보다 많이 부르면 앉힐 데가 없는 저작이다.
            var shapeless = Assert.Throws<System.ArgumentException>(() =>
                ConvertWithSummon(shapeId: string.Empty, summonSpec: "summon:M007;2"));
            Assert.That(shapeless.Message, Does.Contain("without a resolvable shapeId"));

            var tooMany = Assert.Throws<System.ArgumentException>(() =>
                ConvertWithSummon(shapeId: "summon-2", summonSpec: "summon:M007;3;3"));
            Assert.That(tooMany.Message, Does.Contain("only seats"));

            var ok = ConvertWithSummon(shapeId: "summon-2", summonSpec: "summon:M007;2");
            var pattern = ok.MonsterCatalog.Entries.Single().AttackPatterns.Single();
            Assert.That(pattern.HasSummon, Is.True);
            Assert.That(pattern.SummonCount, Is.EqualTo(2));
            Assert.That(pattern.SummonMaxAlive, Is.EqualTo(2));
        }

        // ------------------------------------------------------------------ 집행

        [Test]
        public void SummonSpawnsTheAuthoredCountAsRealThreats()
        {
            var state = CreateState();
            Assert.That(AliveMinions(state), Is.Zero);

            ResolveSummon(state);

            Assert.That(AliveMinions(state), Is.EqualTo(2), "저작한 마리 수만큼 나온다.");
            foreach (var minion in Minions(state))
            {
                Assert.That(MonsterSpawnRoles.IsProp(minion.SpawnRole), Is.False,
                    "🔴 부른 적은 기물이 아니다 — 처리하지 않아도 이기는 소환은 소환이 아니다.");
                Assert.That(MonsterSpawnRoles.IsTrapSpawn(minion.SpawnRole), Is.True, "함정 소환과 같은 취급.");
            }
        }

        [Test]
        public void SummonSeatsMatchTheTelegraphedCells()
        {
            // 🔴 예고=명중의 소환판: 예고 칸과 실제 스폰 칸이 같은 함수에서 나와야 갈라질 수 없다.
            var state = CreateState();
            var monster = FirstMonster(state);
            var pattern = monster.AttackPatterns.Single(candidate => candidate.HasSummon);
            var seats = state.GetSummonSeatCoords(monster, pattern).ToList();

            ResolveSummon(state);

            // 🔑 자리 수는 형상 저작(summon-2)이 정하므로 여기서 박지 않는다(2026-08-31 T4).
            // 이 테스트의 계약은 개수가 아니라 **예고 칸 == 실제 스폰 칸**이고, 그건 아래 줄이 지킨다.
            Assert.That(seats, Is.Not.Empty, "소환 자리가 없으면 아래 대조가 빈 채로 통과한다.");
            Assert.That(Minions(state).Select(minion => minion.Coord), Is.EquivalentTo(seats));
        }

        [Test]
        public void SummonCannotBeBlocked()
        {
            // 🔴 Q3. 예고 칸을 다른 몬스터로 막아도 근처 빈 칸으로 밀려나 어쨌든 나온다.
            var state = CreateState();
            var monster = FirstMonster(state);
            var pattern = monster.AttackPatterns.Single(candidate => candidate.HasSummon);
            var seats = state.GetSummonSeatCoords(monster, pattern).ToList();
            var blockedSeat = seats[0];
            Assert.That(SpawnMinionAt(state, blockedSeat), Is.True, "전제: 예고 칸 하나를 막았다.");
            var blockerCount = AliveMinions(state);

            ResolveSummon(state);

            Assert.That(AliveMinions(state) - blockerCount, Is.EqualTo(2), "막혀도 두 마리 다 나온다.");
            Assert.That(Minions(state).Count(minion => minion.Coord == blockedSeat), Is.EqualTo(1),
                "막힌 칸에 겹쳐 앉지는 않는다 — 밀려난 것이다.");
        }

        [Test]
        public void PushOutIsDeterministic()
        {
            // 무작위로 밀면 세이브 재개마다 같은 상황이 다르게 풀려 플레이어가 학습할 수 없다.
            List<HexCoord> Roll()
            {
                var state = CreateState();
                var monster = FirstMonster(state);
                var pattern = monster.AttackPatterns.Single(candidate => candidate.HasSummon);
                foreach (var seat in state.GetSummonSeatCoords(monster, pattern))
                {
                    SpawnMinionAt(state, seat);
                }

                var before = Minions(state).Select(minion => minion.Coord).ToList();
                ResolveSummon(state);
                return Minions(state)
                    .Select(minion => minion.Coord)
                    .Where(coord => !before.Contains(coord))
                    .OrderBy(coord => coord.Q)
                    .ThenBy(coord => coord.R)
                    .ToList();
            }

            Assert.That(Roll(), Is.EqualTo(Roll()));
        }

        [Test]
        public void SummonStopsAtTheConcurrentCap()
        {
            var state = CreateState();
            ResolveSummon(state);
            Assert.That(AliveMinions(state), Is.EqualTo(2), "전제: 상한까지 찼다.");

            ResolveSummon(state);

            Assert.That(AliveMinions(state), Is.EqualTo(2),
                "상한이 없으면 무한 소환이 된다 — 집행도 상한을 다시 본다(같은 턴에 다른 소환자가 채웠을 수 있다).");
        }

        [Test]
        public void CappedSummonPatternIsSuppressedForPlanning()
        {
            // 상한을 집행 시점에만 보면 "예고는 떴는데 아무도 안 나오는 턴"이 생긴다 — 계획이 먼저 본다.
            var state = CreateState();
            var monster = FirstMonster(state);
            var pattern = monster.AttackPatterns.Single(candidate => candidate.HasSummon);
            var context = (IMonsterPlanningContext)state;

            Assert.That(context.IsAttackPatternSuppressed(monster, pattern), Is.False);
            ResolveSummon(state);
            Assert.That(context.IsAttackPatternSuppressed(monster, pattern), Is.True);
        }

        [Test]
        public void ADeadSummonFreesItsSlot()
        {
            var state = CreateState();
            ResolveSummon(state);
            // 스냅샷이 아니라 런타임 인스턴스를 잡아야 체력을 깎을 수 있다(state.Monsters는 스냅샷 목록).
            var minionId = Minions(state).First().Id;
            var minion = RuntimeMonsters(state).First(candidate => candidate.Id == minionId);
            minion.Combatant.ApplyDamage(minion.Combatant.Hp);

            var monster = FirstMonster(state);
            var pattern = monster.AttackPatterns.Single(candidate => candidate.HasSummon);
            Assert.That(((IMonsterPlanningContext)state).IsAttackPatternSuppressed(monster, pattern), Is.False,
                "처리하면 다시 부를 수 있다 — 그래야 '부르는 쪽을 먼저'가 판단으로 성립한다.");
        }

        // ------------------------------------------------------------------ 출하 데이터
        //
        // 🔴 두두리의 말뚝 소환 출하 단언은 2026-08-30에 지웠다 — 소환 <b>저작</b> 자체를 걷어냈기
        //    때문이다(사용자 확정: 자리를 막는 기물이 게임 흐름을 답답하게 만든다). 두두리의 그
        //    자리는 중거리 끌어당김 A054 낚아채기가 대신하고, 계약은 MonsterCursePoolTests가 잡는다.
        //
        // ⚠️ 위의 픽스처 기반 계약 9건은 <b>그대로 유효하다</b> — 소환 <b>런타임</b>은 지우지 않았고
        //    함정 소환·고깔(M905)·철조각(M901)이 여전히 같은 경로를 쓴다. 출하 저작이 다시 생기면
        //    여기에 그 몬스터의 단언을 새로 넣을 것.

        // ------------------------------------------------------------------ 픽스처

        /// <summary>테스트가 자리를 막을 때 쓰는 스폰. 규칙층의 스폰 함수를 그대로 부른다.</summary>
        private static bool SpawnMinionAt(CombatState state, HexCoord coord)
        {
            var method = typeof(CombatState).GetMethod(
                "TrySpawnMonsterAt", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var args = new object[] { MinionDefinitionId, coord, MonsterSpawnRoles.TrapSpawn, 0, null, null };
            return (bool)method.Invoke(state, args);
        }

        private static void ResolveSummon(CombatState state)
        {
            var monster = FirstMonster(state);
            var pattern = monster.AttackPatterns.Single(candidate => candidate.HasSummon);
            Invoke(state, "ResolveMonsterSummonPattern", monster, pattern);
        }

        private static IEnumerable<MonsterRuntimeState> Minions(CombatState state) =>
            state.Monsters.Where(monster => monster.DefinitionId == MinionDefinitionId && !monster.IsDead);

        private static int AliveMinions(CombatState state) => Minions(state).Count();

        private static CombatState CreateState()
        {
            return new CombatState(
                CombatState.CreateDemoMap(6),
                new HexCoord(0, 0),
                new[] { new MonsterConfig(SummonerId, new HexCoord(3, 0), 34, definitionId: SummonerDefinitionId) },
                TestCombatConfigs.Standard(playerMaxHp: 200, enemyChaseRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "summon-test-catalog",
                    "Summon Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            SummonerDefinitionId,
                            "부르는 놈",
                            "test-ranged",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: 34,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("AT88", "꼬리", range: 1, areaRadius: 0, damage: 3),
                                new MonsterAttackPattern(
                                    "AT87",
                                    "부르기",
                                    range: 3,
                                    areaRadius: 0,
                                    damage: 0,
                                    targeting: "self",
                                    shapeId: "summon-2",
                                    cooldownTurns: 5,
                                    summonMonsterDefinitionId: MinionDefinitionId,
                                    summonCount: 2,
                                    summonMaxAlive: 2)
                            }),
                        new MonsterCatalogEntry(
                            MinionDefinitionId,
                            "불씨",
                            "test-ranged",
                            "B001",
                            detectionRange: 6,
                            movePerTurn: 1,
                            hp: 12,
                            attackSpeed: 4)
                    }));
        }

        private static MonsterCatalogDefinition ShippingMonsterCatalog()
        {
            AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8)));
            return MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory).MonsterCatalog;
        }

        private static MonsterCatalogCsvBundle ConvertWithSummon(string shapeId, string summonSpec)
        {
            AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8)));
            var patterns =
                "patternId,displayName,range,damage,effectRef,targeting,vfxCueId,shapeId,statusEffects,"
                + "statusEffectDurationTurns,cooldownTurns,summonSpec\n"
                + $"A001,Basic,1,0,attack.damage,self,V001,{shapeId},,2,0,{summonSpec}\n";
            return MonsterCatalogCsvConverter.Convert(new MonsterCatalogCsvSource(
                "monsterId,displayName,archetype,behaviorProfileRef,hp,detectionRange,movePerTurn,attackSpeed,status,visualPrefabPath\n"
                + "M001,Tester,test-melee,B001,30,6,2,1,prototype,\n",
                patterns,
                "monsterId,patternId,order,enabled,overrideWeight,designerNote\nM001,A001,1,true,,\n",
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_vfx_cues.csv")),
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_sound_cues.csv"))));
        }

        private static List<MonsterRuntime> RuntimeMonsters(CombatState state) =>
            (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);

        private static MonsterRuntime FirstMonster(CombatState state) => RuntimeMonsters(state)[0];

        private static object Invoke(CombatState state, string methodName, params object[] args)
        {
            var method = typeof(CombatState)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(candidate => candidate.Name == methodName && candidate.GetParameters().Length == args.Length);
            return method.Invoke(state, args);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 턴별 패턴 피해 변주(damageJitter, DEC-2026-08-19-02) 감사. 계약: ①굴림은 의도 잠금 시
    /// 1회이고 「굴린 값이 상태」 — 읽기(예고·스냅샷)는 몇 번을 하든 같은 값 ②예고 피해 == 실제
    /// 적용 피해(R-8) ③변주 없는 패턴은 고정 수치(하위호환) ④범위 [damage−J, damage+J] 준수
    /// ⑤서스펜드 왕복이 굴림값을 보존 ⑥출하 저작 = 일반 몬스터 피해 패턴만 ±1(보스·터렛 제외).
    /// </summary>
    public sealed class MonsterDamageJitterTests
    {
        private static MonsterCatalogDefinition CatalogWith(int damage, int jitter, int movePerTurn = 2)
        {
            return new MonsterCatalogDefinition("test-catalog", "Test", new[]
            {
                new MonsterCatalogEntry(
                    "MJ01", "변주몹", "test-melee", "B001",
                    detectionRange: 8, movePerTurn: movePerTurn, hp: 30,
                    attackPatterns: new[]
                    {
                        new MonsterAttackPattern("PJ01", "지터 공격", range: 1, areaRadius: 0, damage: damage, damageJitter: jitter),
                    }),
            });
        }

        private static CombatState CreateState(MonsterCatalogDefinition catalog, HexCoord playerCoord, HexCoord monsterCoord)
        {
            var maxQ = System.Math.Max(playerCoord.Q, monsterCoord.Q) + 2;
            var cells = Enumerable.Range(0, maxQ + 1)
                .Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false))
                .ToArray();
            var map = new HexMapData(cells, monsterSpawnRefs: new[]
            {
                new HexMonsterSpawnRef("spawn-jitter", "MJ01", monsterCoord, "primary_pressure"),
            });
            var config = CombatConfig.Default;
            var monsterConfigs = CombatState.ResolveMonsterConfigsFromBoardSpawns(map, config, catalog);
            return new CombatState(map, playerCoord, monsterConfigs, config, monsterCatalog: catalog);
        }

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();
        }

        [Test]
        public void RollIsStoredStateNotPerRead()
        {
            var state = CreateState(CatalogWith(damage: 5, jitter: 2), new HexCoord(0, 0), new HexCoord(1, 0));

            var first = state.Monsters.Single().SelectedAttackPatternDamage;
            var second = state.Monsters.Single().SelectedAttackPatternDamage;

            Assert.That(first, Is.InRange(3, 7), "굴림이 저작 범위 [damage−J, damage+J]를 벗어났다.");
            Assert.That(second, Is.EqualTo(first), "읽을 때마다 값이 다르다 — 읽기 시점 재굴림은 R-8 위반이다.");
        }

        [Test]
        public void PreviewMatchesAppliedDamageEveryTurn()
        {
            // 인접 고정 몬스터(이동 0) — 매 턴 공격한다. 예고를 읽고 그 턴을 끝까지 돌려
            // 플레이어 체력 감소량과 대조한다: 이것이 R-8의 본 계약이다.
            var state = CreateState(CatalogWith(damage: 3, jitter: 2, movePerTurn: 0), new HexCoord(0, 0), new HexCoord(1, 0));

            var attacksObserved = 0;
            for (var turn = 0; turn < 6; turn++)
            {
                var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                    .SingleOrDefault(candidate => candidate.MonsterId == state.Monsters.Single().Id);
                var previewedDamage = preview.AttackPatternDamage;
                var hpBefore = state.Player.Hp;

                RunFullTurn(state);

                var applied = hpBefore - state.Player.Hp;
                if (applied > 0)
                {
                    attacksObserved++;
                    Assert.That(applied, Is.EqualTo(previewedDamage),
                        $"turn {turn}: 예고 {previewedDamage} ≠ 실제 {applied} — 예고=집행(R-8) 위반.");
                }
            }

            Assert.That(attacksObserved, Is.GreaterThan(0), "공격이 한 번도 성립하지 않았다 — 검증이 빈 채로 통과하고 있다.");
        }

        [Test]
        public void RollsVaryAcrossTurnsWithinBounds()
        {
            // 사거리 밖 고정 몬스터 — 피해 없이 턴만 돌려 의도 잠금마다의 굴림 분포를 본다.
            var state = CreateState(CatalogWith(damage: 3, jitter: 2, movePerTurn: 0), new HexCoord(0, 0), new HexCoord(3, 0));

            var observed = new HashSet<int>();
            for (var turn = 0; turn < 30; turn++)
            {
                var rolled = state.Monsters.Single().SelectedAttackPatternDamage;
                Assert.That(rolled, Is.InRange(1, 5), $"turn {turn}");
                observed.Add(rolled);
                RunFullTurn(state);
            }

            Assert.That(observed.Count, Is.GreaterThan(1), "30턴 동안 한 값뿐이다 — 턴별 굴림이 죽었다.");
        }

        [Test]
        public void JitterlessPatternKeepsFixedDamage()
        {
            var state = CreateState(CatalogWith(damage: 5, jitter: 0, movePerTurn: 0), new HexCoord(0, 0), new HexCoord(3, 0));

            for (var turn = 0; turn < 5; turn++)
            {
                Assert.That(state.Monsters.Single().SelectedAttackPatternDamage, Is.EqualTo(5),
                    $"turn {turn}: 변주 없는 패턴의 수치가 흔들렸다 — 하위호환 위반.");
                RunFullTurn(state);
            }
        }

        [Test]
        public void SuspendRoundtripPreservesTheCurrentRoll()
        {
            var state = CreateState(CatalogWith(damage: 5, jitter: 2, movePerTurn: 0), new HexCoord(0, 0), new HexCoord(3, 0));
            RunFullTurn(state);
            var rolledBefore = state.Monsters.Single().SelectedAttackPatternDamage;

            var snapshot = state.CreateSuspendSnapshot();
            var resume = CreateState(CatalogWith(damage: 5, jitter: 2, movePerTurn: 0), new HexCoord(0, 0), new HexCoord(3, 0));
            resume.RestoreFromSuspend(snapshot);

            Assert.That(resume.Monsters.Single().SelectedAttackPatternDamage, Is.EqualTo(rolledBefore),
                "재개 직후의 굴림값이 서스펜드 시점과 다르다 — 재개 첫 턴의 예고가 집행과 어긋난다.");
        }

        [Test]
        public void ShippingPatternsAuthorJitterOnRegularMonstersOnly()
        {
            // 출하 저작 감사 — 바인딩 재조립(phaseMin 오버라이드가 걸린 패턴은 재구성된다)이
            // damageJitter를 떨어뜨리면 여기서 잡힌다.
            var catalog = CombatCatalogFactory.CreateMonsterCatalog(CombatConfig.Default);
            var jittered = new HashSet<string>
            {
                "A001", "A002", "A027", "A015", "A016", "A011", "A012", "A026",
                "A013", "A014", "A008", "A009", "A010", "A025",
                // 요괴 S2: 두두리(구 도깨비) 기본 공격. A035(저주 축·피해 2)와 A036(넉백+속박)은
                // 수치가 작거나 부가 효과가 본체라 변주를 걸지 않았다.
                "A034",
                // S3 두억시니 세 패턴. 지대 저작(A041·A042)도 피해 본체는 일반 몬스터 문법이라 변주를 건다.
                "A040", "A041", "A042",
                // S4 어둑시니 공격 둘(A039 흩어지기는 2026-09-04 완전 삭제됨).
                "A037", "A038",
                // 2026-08-30 6종 개편: 거구귀 피해 패턴 둘(A050 입 벌리기는 자기부여라 변주 없음) +
                // 야광귀 중거리(A051 엽전 채기는 피해 2·절도가 본체라 변주 없음) + 그슨새 기본 공격.
                // 폐기된 여우불(A032·A033)·구미호(A043·A044) 자리를 이들이 받았다.
                "A048", "A049", "A052", "A053",
            };

            foreach (var entry in catalog.Entries)
            {
                foreach (var pattern in entry.AttackPatterns)
                {
                    var expected = jittered.Contains(pattern.Id) ? 1 : 0;
                    Assert.That(pattern.DamageJitter, Is.EqualTo(expected),
                        $"{entry.Id}/{pattern.Id}: 출하 damageJitter 저작이 어긋났다(보스·터렛·기물은 0이어야 한다).");
                }
            }
        }
    }
}


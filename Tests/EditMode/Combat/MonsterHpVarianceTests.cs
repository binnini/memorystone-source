using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 스폰 시 체력 변주(StS식 개체차, hpVariancePct) 감사. 계약: ①시드 없음/변주 0 = 카탈로그
    /// 고정값(하위호환) ②같은 시드 = 같은 체력(런 세이브 재현) ③굴림은 스폰 id 기반이라 스폰
    /// 목록 순서와 무관 ④범위는 ±% 반올림 경계 안·하한 1.
    /// </summary>
    public sealed class MonsterHpVarianceTests
    {
        private const int BaseHp = 20;
        private const int VariancePct = 10;

        private static MonsterCatalogDefinition CreateCatalog(int hpVariancePct, int hp = BaseHp)
        {
            return new MonsterCatalogDefinition("test-catalog", "Test", new[]
            {
                new MonsterCatalogEntry(
                    "MV01", "변주몹", "test-melee", "B001",
                    detectionRange: 6, movePerTurn: 2, hp: hp,
                    hpVariancePct: hpVariancePct),
            });
        }

        private static HexMapData CreateMapWithSpawns(params HexMonsterSpawnRef[] spawnRefs)
        {
            var cells = Enumerable.Range(0, spawnRefs.Length + 2)
                .Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false))
                .ToArray();
            return new HexMapData(cells, monsterSpawnRefs: spawnRefs);
        }

        private static HexMonsterSpawnRef[] Spawns(int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => new HexMonsterSpawnRef($"spawn-{i}", "MV01", new HexCoord(i, 0), "primary_pressure"))
                .ToArray();
        }

        [Test]
        public void NoSeedKeepsCatalogHpExactly()
        {
            var configs = CombatState.ResolveMonsterConfigsFromBoardSpawns(
                CreateMapWithSpawns(Spawns(4)), CombatConfig.Default, CreateCatalog(VariancePct));

            Assert.That(configs.Select(monster => monster.MaxHp), Is.All.EqualTo(BaseHp),
                "시드 없음(랜덤화 off·구세이브)은 고정 체력이어야 한다 — 하위호환 계약.");
        }

        [Test]
        public void ZeroVarianceKeepsCatalogHpEvenWithSeed()
        {
            var configs = CombatState.ResolveMonsterConfigsFromBoardSpawns(
                CreateMapWithSpawns(Spawns(4)), CombatConfig.Default, CreateCatalog(0), hpVarianceSeed: 123);

            Assert.That(configs.Select(monster => monster.MaxHp), Is.All.EqualTo(BaseHp),
                "hpVariancePct 미저작 몬스터는 시드가 있어도 고정 체력이다 — 옵트인 계약.");
        }

        [Test]
        public void SameSeedRollsIdenticalHpPerSpawn()
        {
            var map = CreateMapWithSpawns(Spawns(6));
            var catalog = CreateCatalog(VariancePct);

            var first = CombatState.ResolveMonsterConfigsFromBoardSpawns(map, CombatConfig.Default, catalog, hpVarianceSeed: 777);
            var second = CombatState.ResolveMonsterConfigsFromBoardSpawns(map, CombatConfig.Default, catalog, hpVarianceSeed: 777);

            Assert.That(
                second.Select(monster => (monster.SpawnRefId, monster.MaxHp)),
                Is.EqualTo(first.Select(monster => (monster.SpawnRefId, monster.MaxHp))));
        }

        [Test]
        public void SpawnOrderDoesNotChangeEachMonstersRoll()
        {
            var spawns = Spawns(6);
            var catalog = CreateCatalog(VariancePct);

            var forward = CombatState.ResolveMonsterConfigsFromBoardSpawns(
                CreateMapWithSpawns(spawns), CombatConfig.Default, catalog, hpVarianceSeed: 777);
            var reversed = CombatState.ResolveMonsterConfigsFromBoardSpawns(
                CreateMapWithSpawns(spawns.Reverse().ToArray()), CombatConfig.Default, catalog, hpVarianceSeed: 777);

            var forwardById = forward.ToDictionary(monster => monster.SpawnRefId, monster => monster.MaxHp);
            foreach (var monster in reversed)
            {
                Assert.That(monster.MaxHp, Is.EqualTo(forwardById[monster.SpawnRefId]),
                    $"스폰 '{monster.SpawnRefId}'의 굴림이 목록 순서에 흔들렸다 — 스폰 id 기반 결정성 위반.");
            }
        }

        [Test]
        public void SeedSweepStaysWithinAuthoredBoundsAndActuallyVaries()
        {
            // BaseHp 20 ±10% → [18, 22].
            var map = CreateMapWithSpawns(Spawns(4));
            var catalog = CreateCatalog(VariancePct);
            var observed = new HashSet<int>();

            for (var seed = 0; seed < 200; seed++)
            {
                foreach (var monster in CombatState.ResolveMonsterConfigsFromBoardSpawns(map, CombatConfig.Default, catalog, seed))
                {
                    Assert.That(monster.MaxHp, Is.InRange(18, 22), $"seed {seed}: {monster.SpawnRefId}");
                    observed.Add(monster.MaxHp);
                }
            }

            Assert.That(observed.Count, Is.GreaterThan(1), "스윕 200에서 체력이 한 값뿐이다 — 굴림이 죽었다.");
            Assert.That(observed.Min(), Is.EqualTo(18), "하한이 한 번도 안 나왔다 — 경계 포함 추첨이 아니다.");
            Assert.That(observed.Max(), Is.EqualTo(22), "상한이 한 번도 안 나왔다 — 경계 포함 추첨이 아니다.");
        }

        [Test]
        public void TinyHpNeverRollsBelowOne()
        {
            var map = CreateMapWithSpawns(Spawns(3));
            var catalog = CreateCatalog(hpVariancePct: 50, hp: 1);

            for (var seed = 0; seed < 50; seed++)
            {
                foreach (var monster in CombatState.ResolveMonsterConfigsFromBoardSpawns(map, CombatConfig.Default, catalog, seed))
                {
                    Assert.That(monster.MaxHp, Is.GreaterThanOrEqualTo(1), $"seed {seed}");
                }
            }
        }

        [Test]
        public void EntryClampsVarianceToAuthoringBounds()
        {
            Assert.That(new MonsterCatalogEntry("M-a", "a", "t", "B001", 6, 2, 20, hpVariancePct: -5).HpVariancePct, Is.EqualTo(0));
            Assert.That(new MonsterCatalogEntry("M-b", "b", "t", "B001", 6, 2, 20, hpVariancePct: 99).HpVariancePct, Is.EqualTo(50));
        }

        [Test]
        public void ShippingCatalogAuthorsVarianceOnRegularMonstersOnly()
        {
            // 출하 저작 감사: 일반 5종 ±10%, 보스·기물·터렛은 고정(보스는 페이즈 임계와 얽힌다).
            var catalog = CombatCatalogFactory.CreateMonsterCatalog(CombatConfig.Default);
            var byId = catalog.Entries.ToDictionary(entry => entry.Id, entry => entry.HpVariancePct);

            Assert.That(new[] { "M001", "M003", "M004", "M005", "M006" }.Select(id => byId[id]),
                Is.All.EqualTo(10), "일반 몬스터 5종의 hpVariancePct 저작이 유실됐다.");
            Assert.That(byId["M002"], Is.EqualTo(0), "보스는 체력 변주 금지(페이즈 임계 결합).");
            foreach (var fixture in new[] { "M901", "M902", "M903", "M904", "M905" })
            {
                Assert.That(byId[fixture], Is.EqualTo(0), $"기물 '{fixture}'는 체력 변주 금지.");
            }
        }
    }
}

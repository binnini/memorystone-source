using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 저주 카드 <b>풀</b> 저작(요괴 트랙 §3-3 · 도깨비 A035)의 파서·추첨·출하 데이터 계약.
    ///
    /// <para>이 스위트가 지키는 핵심은 <b>추첨이 순수 함수라는 것</b>이다. 이 프로젝트의 전투 RNG는
    /// 무시드이고 세이브는 full-snapshot이라(DEC-2026-07-18-02), 주변 RNG로 카드를 뽑으면 전투 중
    /// 저장·복원 뒤에 다른 저주가 나올 수 있다 — 플레이어에게는 "저장했더니 덱이 달라졌다"로 보인다.</para>
    /// </summary>
    public sealed class MonsterCursePoolTests
    {
        private static readonly string[] Pool = { "X05", "X07", "X11" };

        // ------------------------------------------------------------------ 파서

        [Test]
        public void EmptyAuthoringIsPoolLess()
        {
            Assert.That(MonsterCurseCardPool.TryParse(null, out var none, out _), Is.True);
            Assert.That(none, Is.Empty, "저작하지 않은 패턴이 대다수다 — 빈 값은 실패가 아니다.");
            Assert.That(MonsterCurseCardPool.TryParse("   ", out var blank, out _), Is.True);
            Assert.That(blank, Is.Empty);
        }

        [Test]
        public void ParserRejectsEmptyAndDuplicateEntries()
        {
            Assert.That(MonsterCurseCardPool.TryParse("X05;;X07", out _, out var empty), Is.False);
            Assert.That(empty, Does.Contain("empty entry"));

            Assert.That(MonsterCurseCardPool.TryParse("X05;X05", out _, out var duplicate), Is.False);
            Assert.That(duplicate, Does.Contain("duplicate"),
                "중복은 가중치 저작이 아니라 실수다 — 가중치가 필요하면 컬럼을 따로 만든다.");

            Assert.That(MonsterCurseCardPool.TryParse("X05; X07 ;X11", out var trimmed, out _), Is.True);
            Assert.That(trimmed, Is.EqualTo(Pool), "공백은 다듬는다.");
        }

        // ------------------------------------------------------------------ 추첨

        [Test]
        public void PickIsPureAndStaysInsideThePool()
        {
            var seed = MonsterCurseCardPool.MixSeed("M008|A035", 4);
            Assert.That(MonsterCurseCardPool.Pick(Pool, seed), Is.EqualTo(MonsterCurseCardPool.Pick(Pool, seed)),
                "같은 시드면 같은 카드 — 저장·복원 뒤에도 같은 저주가 나온다.");

            for (var turn = 1; turn <= 40; turn++)
            {
                var picked = MonsterCurseCardPool.Pick(Pool, MonsterCurseCardPool.MixSeed("M008|A035", turn));
                Assert.That(Pool, Does.Contain(picked));
            }

            Assert.That(MonsterCurseCardPool.Pick(Array.Empty<string>(), seed), Is.Empty, "빈 풀은 빈 결과.");
        }

        [Test]
        public void PickSpreadsAcrossThePoolAsTurnsPass()
        {
            // 풀이 한 카드로 고정되면 "이 중 하나"라는 저작 의도가 죽는다. 40턴이면 세 종류가 다 나와야 한다.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var turn = 1; turn <= 40; turn++)
            {
                seen.Add(MonsterCurseCardPool.Pick(Pool, MonsterCurseCardPool.MixSeed("M008|A035", turn)));
            }

            Assert.That(seen, Is.EquivalentTo(Pool));
        }

        [Test]
        public void PickDistinctNeverRepeatsAndClampsToPoolSize()
        {
            var seed = MonsterCurseCardPool.MixSeed("M008|aftermath", 3);
            var two = MonsterCurseCardPool.PickDistinct(Pool, 2, seed);
            // 개수 고정 유지: 저작이 아니라 **요청한 수**다(PickDistinct(Pool, 2, …)).
            Assert.That(two, Has.Count.EqualTo(2));
            Assert.That(two.Distinct().Count(), Is.EqualTo(2), "같은 저주를 두 장 넣지 않는다.");
            Assert.That(two, Is.EqualTo(MonsterCurseCardPool.PickDistinct(Pool, 2, seed)), "순수 함수.");

            Assert.That(MonsterCurseCardPool.PickDistinct(Pool, 9, seed), Is.EquivalentTo(Pool),
                "풀보다 많이 요구하면 풀 전체 — 수를 채우려고 중복을 넣지 않는다.");
            Assert.That(MonsterCurseCardPool.PickDistinct(Pool, 0, seed), Is.Empty);
        }

        // ------------------------------------------------------------------ 임포트 가드

        [Test]
        public void ConverterRejectsBothColumnsAndSingleEntryPools()
        {
            var both = Assert.Throws<ArgumentException>(() => Convert(pool: "X05;X07", cardId: "X08"));
            Assert.That(both.Message, Does.Contain("둘 중 하나만"),
                "어느 쪽이 이기는지 저작자에게 안 보이는 조합은 임포트에서 죽인다.");

            var single = Assert.Throws<ArgumentException>(() => Convert(pool: "X05", cardId: string.Empty));
            Assert.That(single.Message, Does.Contain("single-entry"),
                "뽑을 것이 하나면 풀이 아니라 injectStatusCardId다.");

            var duplicate = Assert.Throws<ArgumentException>(() => Convert(pool: "X05;X05", cardId: string.Empty));
            Assert.That(duplicate.Message, Does.Contain("duplicate"));

            var ok = Convert(pool: "X05;X07;X11", cardId: string.Empty);
            var pattern = ok.MonsterCatalog.Entries.Single().AttackPatterns.Single();
            Assert.That(pattern.InjectStatusCardPool, Is.EqualTo(new[] { "X05", "X07", "X11" }));
            Assert.That(pattern.HasInjectStatusCardPool, Is.True);
            Assert.That(pattern.InjectStatusCardId, Is.Empty);
        }

        // ------------------------------------------------------------------ 출하 데이터

        [Test]
        [Category("ShippingData")]
        public void ShippingCursePoolsResolveToCurseCardsOutsideTheDeck()
        {
            // 풀 저작은 카드 카탈로그와 <b>다른 CSV</b>라 오타가 임포트에서 안 잡힌다 — 런타임에서는
            // 조용히 "아무 카드도 안 섞임"이 된다(TryInjectStatusCard가 false를 돌려줄 뿐이다).
            // 그래서 여기서 대조한다.
            var curseCards = ReadCurseCardIds();

            foreach (var pool in ReadAuthoredPatternPools())
            {
                foreach (var cardId in pool.Value)
                {
                    Assert.That(curseCards, Does.Contain(cardId),
                        $"패턴 {pool.Key}의 풀 카드 '{cardId}'가 cards.csv의 덱 미포함 저주가 아니다.");
                }
            }

            foreach (var aftermath in ReadAuthoredAftermathPools())
            {
                foreach (var cardId in aftermath.Value)
                {
                    Assert.That(curseCards, Does.Contain(cardId),
                        $"몬스터 {aftermath.Key}의 심술 풀 카드 '{cardId}'가 cards.csv의 덱 미포함 저주가 아니다.");
                }
            }
        }

        [Test]
        [Category("ShippingData")]
        public void DuduriAndYagwanggwiAreAuthoredAsTheirDesignedAxes()
        {
            var catalog = ShippingMonsterCatalog();

            // 두두리 — 도깨비 저작을 승계했다. 2026-09-04 리워크: 「심술」 삭제 확정 — 특성 없음
            // (삼목구·호랑 선생 선례). 저주 풀 추첨은 A035(그슨새 속삭임) 쪽 축으로만 남는다.
            var duduri = catalog.Entries.Single(entry => entry.Id == "M008");
            Assert.That(duduri.DisplayName, Is.EqualTo("두두리"));
            Assert.That(duduri.OnDeathEffectRef, Is.Empty, "심술은 2026-09-04 삭제됐다 — 특성 없음 확정.");

            // 🔴 소환(말뚝 세우기)은 2026-08-30 사용자 확정으로 걷어냈다 — 자리를 막는 기물이 게임 흐름을
            //    답답하게 만든다는 판정이다. 그 자리는 중거리 끌어당김이 대신한다.
            //    되살리지 말 것: 두두리에게 소환을 다시 붙이면 같은 판정을 다시 받는다.
            Assert.That(duduri.AttackPatterns.Any(pattern => pattern.HasSummon), Is.False,
                "두두리는 더 이상 소환하지 않는다(2026-08-30 확정).");

            var pull = duduri.AttackPatterns.Single(pattern => pattern.Id == "A054");
            Assert.That(pull.KnockbackDistance, Is.Negative,
                "낚아채기는 당긴다 — 부호가 방향이다(§16.1). 양수면 씨름 걸기와 같은 밀치기가 되어 어휘가 겹친다.");

            // 야광귀 — 훔치고 달아나고, 죽으면 돌려준다. 셋이 한 몸이라 함께 단언한다.
            // 2026-09-04 소매치기 리워크: 속박 축 은퇴 — 훔치는 것이 발이 아니라 <b>엽전 15</b>가 됐다.
            var yagwanggwi = catalog.Entries.Single(entry => entry.Id == "M014");
            Assert.That(yagwanggwi.OnDeathEffectRef, Is.EqualTo(MonsterDeathAftermath.RestoreRef));
            Assert.That(MonsterDeathAftermath.TryParse(
                yagwanggwi.OnDeathEffectRef, yagwanggwi.OnDeathEffectParam, out var restore, out var restoreError),
                Is.True, restoreError);
            Assert.That(restore.RestoresMoney, Is.True, "처치 시에만 전액 반환 — 뒤끝의 유일한 이로운 갈래.");

            var steal = yagwanggwi.AttackPatterns.Single(pattern => pattern.Id == "A051");
            Assert.That(steal.StatusEffects, Is.Empty, "속박은 2026-09-04 제거됐다 — 발이 아니라 엽전을 훔친다.");
            Assert.That(steal.StealMoneyAmount, Is.EqualTo(15), "일반 처치 보상 30~50 스케일의 절반 무게.");
            Assert.That(steal.CooldownTurns, Is.EqualTo(2), "cd 1→2(2026-09-04).");
            Assert.That(steal.SelfTeleportRadius, Is.GreaterThan(0), "훔친 턴에 달아나지 않으면 절도가 성립하지 않는다.");

            // 거구귀 — 담력 시험(2026-09-04 겁먹음 대체): 재설정형 힘 = (거리−1)×2·상한 6.
            var geogugwi = catalog.Entries.Single(candidate => candidate.Id == "M012");
            Assert.That(geogugwi.AgitationConditionRef, Is.EqualTo(MonsterAgitationCondition.StrengthDistanceRef));
            Assert.That(geogugwi.AgitationMaxStacks, Is.EqualTo(6));

            // 그슨새 — 홀림(2026-09-04 재정의): 고립 힘 상승이 아니라 오라 봉인(특성 1슬롯).
            var geuseunsae = catalog.Entries.Single(candidate => candidate.Id == "M013");
            Assert.That(geuseunsae.AgitationConditionRef, Is.Empty, "구 고립 조건은 폐기됐다.");
            Assert.That(geuseunsae.AgitationMaxStacks, Is.Zero);
            Assert.That(MonsterAuraSeal.TryParse(
                geuseunsae.HiddenTraitRef, geuseunsae.HiddenTraitParam, out var aura, out var auraError),
                Is.True, auraError);
            Assert.That(aura.Radius, Is.EqualTo(2));
            Assert.That(aura.Cards, Is.EqualTo(1));

            // 파서 가드: 여섯 모두 쿨다운 0 · 거리창 없는 기본 패턴을 하나씩 가진다.
            foreach (var id in new[] { "M008", "M009", "M010", "M012", "M013", "M014" })
            {
                var entry = catalog.Entries.Single(candidate => candidate.Id == id);
                Assert.That(
                    entry.AttackPatterns.Any(pattern =>
                        pattern.CooldownTurns == 0 && pattern.DistMin == 0 && pattern.DistMax == int.MaxValue),
                    Is.True,
                    $"{entry.Id}: 기본 패턴이 없으면 어떤 거리에서 후보가 0이 된다.");
            }
        }

        // ------------------------------------------------------------------ 픽스처

        private static MonsterCatalogCsvBundle Convert(string pool, string cardId)
        {
            const string monsterHeader =
                "monsterId,displayName,archetype,behaviorProfileRef,hp,detectionRange,movePerTurn,attackSpeed,status,visualPrefabPath\n";
            var patterns =
                "patternId,displayName,range,damage,effectRef,targeting,vfxCueId,shapeId,statusEffects,"
                + "statusEffectDurationTurns,cooldownTurns,injectStatusCardId,injectStatusCardPool\n"
                + $"A001,Basic,1,3,attack.damage,player_in_range,V001,single,,2,0,{cardId},{pool}\n";
            return MonsterCatalogCsvConverter.Convert(new MonsterCatalogCsvSource(
                monsterHeader + "M001,Tester,test-melee,B001,30,6,2,1,prototype,\n",
                patterns,
                "monsterId,patternId,order,enabled,overrideWeight,designerNote\nM001,A001,1,true,,\n",
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_vfx_cues.csv")),
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_sound_cues.csv"))));
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

        private static string ReadMonsterCsv(string fileName) =>
            File.ReadAllText(Path.Combine(CombatCsvPaths.MonsterDirectory, fileName), Encoding.UTF8);

        /// <summary>cards.csv에서 "덱에 안 들어가는 저주" id 집합 — 주입 대상이 될 수 있는 카드들.</summary>
        private static HashSet<string> ReadCurseCardIds()
        {
            var table = CsvTable.Parse(
                File.ReadAllText(CombatCsvPaths.CardsCsv, Encoding.UTF8), "cards.csv");
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                if (!row.TryGet("id", out var id) || string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (row.TryGet("includeInDecks", out var include)
                    && string.Equals(include.Trim(), "FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(id.Trim());
                }
            }

            return result;
        }

        private static IEnumerable<KeyValuePair<string, string[]>> ReadAuthoredPatternPools()
        {
            var table = CsvTable.Parse(ReadMonsterCsv("monster_attack_patterns.csv"), "monster_attack_patterns.csv");
            foreach (var row in table.Rows)
            {
                if (!row.TryGet("injectStatusCardPool", out var raw) || string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                Assert.That(MonsterCurseCardPool.TryParse(raw, out var pool, out var error), Is.True, error);
                row.TryGet("patternId", out var patternId);
                yield return new KeyValuePair<string, string[]>(patternId, pool);
            }
        }

        private static IEnumerable<KeyValuePair<string, string[]>> ReadAuthoredAftermathPools()
        {
            var table = CsvTable.Parse(ReadMonsterCsv("monster_catalog.csv"), "monster_catalog.csv");
            foreach (var row in table.Rows)
            {
                if (!row.TryGet("onDeathEffectRef", out var effectRef)
                    || effectRef.Trim() != MonsterDeathAftermath.CurseRef)
                {
                    continue;
                }

                row.TryGet("onDeathEffectParam", out var param);
                row.TryGet("monsterId", out var monsterId);
                Assert.That(MonsterDeathAftermath.TryParse(effectRef.Trim(), param, out var spec, out var error),
                    Is.True, error);
                yield return new KeyValuePair<string, string[]>(monsterId, spec.CursePool);
            }
        }
    }
}

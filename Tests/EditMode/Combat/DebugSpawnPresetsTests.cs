using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 판정용 소환 프리셋(<see cref="DebugSpawnPresets"/>)이 출하 카탈로그를 실제로 가리키는지 잠근다.
    ///
    /// <para>
    /// 🔴 이 게이트가 없으면 드리프트가 <b>조용하다</b>: id가 카탈로그에서 사라져도 버튼은 그대로
    /// 있고 아무것도 안 서는 것으로만 드러난다 — 그리고 그건 실플레이 판정 도중에나 보인다.
    /// (요괴 6종 개편처럼 카탈로그가 통째로 갈리는 일이 이미 있었다.)
    /// </para>
    /// </summary>
    public sealed class DebugSpawnPresetsTests
    {
        private static MonsterCatalogDefinition ShippingMonsterCatalog()
        {
            AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8)));
            return MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory).MonsterCatalog;
        }

        [Test]
        public void EveryPresetIdResolvesInShippingCatalog()
        {
            var catalog = ShippingMonsterCatalog();
            Assert.That(catalog.Entries, Is.Not.Empty, "출하 몬스터 카탈로그가 비면 판정 자체가 무의미하다.");

            foreach (var preset in DebugSpawnPresets.All)
            {
                Assert.That(preset.Value, Is.Not.Empty, $"프리셋 '{preset.Key}'가 비어 있다.");
                foreach (var id in preset.Value)
                {
                    Assert.That(
                        catalog.TryGetEntry(id, out _),
                        Is.True,
                        $"프리셋 '{preset.Key}'의 {id}가 monster_catalog.csv에 없다 — 패널 버튼이 조용히 죽는다.");
                }
            }
        }

        [Test]
        public void PresetsDoNotOverlapAndHaveNoDuplicates()
        {
            foreach (var preset in DebugSpawnPresets.All)
            {
                Assert.That(
                    preset.Value.Distinct().Count(),
                    Is.EqualTo(preset.Value.Count),
                    $"프리셋 '{preset.Key}'에 같은 id가 두 번 들어 있다 — 같은 몬스터가 두 마리 선다.");
            }

            var turretsAndProps = DebugSpawnPresets.Turrets.Intersect(DebugSpawnPresets.Props).ToArray();
            Assert.That(turretsAndProps, Is.Empty, "터렛과 프롭 프리셋이 겹치면 「나란히 비교」가 흐려진다.");
        }

        [Test]
        public void YogoePresetHoldsTheSixShippedYogoe()
        {
            // 여섯 합동 판정이 이 프리셋의 존재 이유다 — 수가 달라지면 판정 대상이 달라진 것이므로
            // 프리셋과 함께 판정 문서도 갱신돼야 한다.
            Assert.That(DebugSpawnPresets.Yogoe.Count, Is.EqualTo(6));

            var catalog = ShippingMonsterCatalog();
            foreach (var id in DebugSpawnPresets.Yogoe)
            {
                Assert.That(catalog.TryGetEntry(id, out var entry), Is.True, id);
                Assert.That(
                    entry.Archetype, Does.Not.Contain("prop"),
                    $"{id}는 요괴여야 한다 — 프롭이 섞이면 합동 인상 판정이 오염된다.");
            }
        }
    }
}

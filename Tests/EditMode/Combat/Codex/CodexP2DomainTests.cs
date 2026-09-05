using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// P2 도메인 넷(유물 · 함정 · 소모품 · 몬스터)이 <b>출하 저작을 그대로</b> 싣는지 고정한다.
    /// <para>
    /// 각 도메인의 계약은 같다: 항목 수가 저작과 일치하고, 이름·설명이 저작에서 오며(도감이 지어내지
    /// 않는다), 범위 도해가 있는 도메인은 그 도해가 저작된 반경·형상과 어긋나지 않는다.
    /// </para>
    /// </summary>
    public sealed class CodexP2DomainTests
    {
        private static readonly Color Accent = Color.white;

        [OneTimeSetUp]
        public void LoadShippingCatalogs()
        {
            CodexShippingDomains.EnsureAttackShapeLibrary();
        }

        // ── 유물 ──────────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void RelicDomainCarriesEveryAuthoredRelicWithItsAuthoredText()
        {
            var catalog = LoadRelicCatalog();
            var domain = new CodexRelicDomain(catalog, Accent);

            Assert.That(domain.Entries, Has.Count.EqualTo(catalog.Entries.Count));
            // 🔑 개수 고정을 **일부러** 유지한다(2026-08-31 T4 판정). 위 줄이 이미 "도감이 저작을
            // 전부 싣는다"는 계약을 지키므로 이 줄은 커버리지가 아니라 **문서 동기화 게이트**다 —
            // 유물이 늘면 도감 계획 §1.1의 도메인 표도 같이 고쳐야 하고, 그걸 강제하는 장치가 이것뿐이다.
            // 늘릴 때 여기가 빨개지는 것은 거짓 경보가 아니라 의도된 알림이다.
            Assert.That(catalog.Entries, Has.Count.EqualTo(27), "유물 수가 바뀌었다면 계획 §1.1도 함께 바뀌어야 한다. 감투 삭제로 28 → 27(DEC-2026-08-31-01 D1).");

            foreach (var definition in catalog.Entries)
            {
                var entry = domain.Entries.Single(candidate => candidate.Id == definition.Id);
                Assert.That(entry.DisplayName, Is.EqualTo(definition.DisplayName));
                Assert.That(
                    entry.Description, Is.EqualTo(definition.Description),
                    $"{definition.Id}: 도감이 설명을 지어내면 저작과 갈라진다.");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void RelicDomainSplitsAlwaysOnFromTriggered()
        {
            var catalog = LoadRelicCatalog();
            var domain = new CodexRelicDomain(catalog, Accent);
            var chips = CodexListQuery.CollectChips(domain.Entries);

            Assert.That(chips, Is.EquivalentTo(new[] { "상시", "발동" }));

            foreach (var definition in catalog.Entries)
            {
                var entry = domain.Entries.Single(candidate => candidate.Id == definition.Id);
                var expected = definition.TriggerKind == RelicTriggerKind.None ? "상시" : "발동";
                Assert.That(entry.FilterChip, Is.EqualTo(expected), $"{definition.Id}");
            }
        }

        [Test]
        public void RelicDomainNeverShowsCursesBecauseThoseAreCards()
        {
            // Q3 확정 — 저주는 카드 도메인의 칩이다. 유물 도메인에 끌어오면 같은 것이 두 곳에 뜬다.
            var domain = new CodexRelicDomain(LoadRelicCatalog(), Accent);
            Assert.That(
                domain.Entries.Any(entry => entry.Id.StartsWith("X", System.StringComparison.Ordinal)),
                Is.False);
        }

        // ── 함정 ──────────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void TrapDomainCarriesEveryAuthoredPreset()
        {
            var catalog = LoadTrapCatalog();
            var domain = new CodexTrapDomain(catalog, null, Accent);

            Assert.That(domain.Entries, Has.Count.EqualTo(catalog.Presets.Count));
            // 🔑 개수 상수를 뺐다(2026-08-31 T4). 위 줄이 "도감이 저작 프리셋을 전부 싣는다"를 이미
            // 지키므로 "오늘 17개"는 계약이 아니라 오늘의 저작이었다 — 함정을 하나 추가할 때마다
            // 무관한 이 테스트가 깨지는 것이 유일한 효과였다.
            Assert.That(catalog.Presets, Is.Not.Empty, "출하 함정 프리셋을 하나도 못 읽었다 — 위 대조가 빈 채로 통과한다.");
        }

        [Test]
        [Category("ShippingData")]
        public void TrapDiagramCoversExactlyTheAuthoredRadius()
        {
            var catalog = LoadTrapCatalog();
            var domain = new CodexTrapDomain(catalog, null, Accent);

            foreach (var preset in catalog.Presets)
            {
                var entry = domain.Entries.Single(candidate => candidate.Id == preset.PresetId);
                Assert.That(entry.RangeSource, Is.Not.Null, $"{preset.PresetId}: 함정은 언제나 덮는 칸이 있다.");

                var diagram = entry.RangeSource.Resolve(0, HexDirection.East, 0);
                var expected = HexArea.CellsWithin(CodexRangeArena.Origin, preset.Radius).Count();

                Assert.That(
                    diagram.ShapeCells, Has.Count.EqualTo(expected),
                    $"{preset.PresetId}: 반경 {preset.Radius}가 덮는 칸 수와 도해가 다르다.");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void DeprecatedBurnTrapIsHiddenFromChipsButKeptWithAWarning()
        {
            // 계획 §9-6. 지금 출하 저작에는 Burn이 없지만, 되살아나면 이 시험이 잡는다.
            var catalog = LoadTrapCatalog();
            var domain = new CodexTrapDomain(catalog, null, Accent);

            var burn = catalog.Presets.Where(preset => preset.EffectKind == HexTrapEffectKind.Burn).ToList();
            var burnEntries = domain.Entries
                .Where(entry => burn.Any(preset => preset.PresetId == entry.Id))
                .ToList();

            Assert.That(burnEntries, Has.Count.EqualTo(burn.Count));
            foreach (var entry in burnEntries)
            {
                Assert.That(entry.FilterChip, Is.Empty, "폐기값은 칩에 오르지 않는다.");
                Assert.That(entry.AuthoringWarning, Is.Not.Empty, "폐기값은 디버그 뷰에 경고를 남긴다.");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void TrapSentencesAgreeWithKoreanParticlesInsteadOfEscapingToBrackets()
        {
            // "중독을(를) 3턴" 같은 도피 표기는 도감에서만 어색해진다 — 카드 설명이 쓰는 그 해소기를 쓴다.
            var domain = new CodexTrapDomain(LoadTrapCatalog(), null, Accent);

            foreach (var entry in domain.Entries)
            {
                Assert.That(entry.Description, Does.Not.Contain("(를)"), entry.Id);
                Assert.That(entry.Description, Does.Not.Contain("(을)"), entry.Id);
            }

            var poison = domain.Entries.Single(candidate => candidate.Id == "poison_cloud");
            Assert.That(poison.Description, Does.Contain("중독을"));
        }

        [Test]
        public void TrapIconFollowsTheSameMappingTheRulesUse()
        {
            // 아이콘 판정을 도감이 따로 짜면 실제로 걸리는 상태이상과 갈라진다.
            foreach (HexTrapEffectKind kind in System.Enum.GetValues(typeof(HexTrapEffectKind)))
            {
                if (!CombatState.TryGetTrapStatusEffectKind(kind, out var statusKind))
                {
                    Assert.That(
                        () => CombatState.ToTrapStatusEffectKind(kind), Throws.Exception,
                        $"{kind}: 물어보는 쪽이 false면 집행은 반드시 던져야 한다(두 답이 갈리면 안 된다).");
                    continue;
                }

                Assert.That(CombatState.ToTrapStatusEffectKind(kind), Is.EqualTo(statusKind), $"{kind}");
            }
        }

        // ── 소모품 ────────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void ConsumableDomainCarriesEveryAuthoredItemWithItsAuthoredText()
        {
            var catalog = LoadConsumableCatalog();
            var domain = new CodexConsumableItemDomain(catalog, Accent);

            Assert.That(domain.Entries, Has.Count.EqualTo(catalog.Entries.Count));
            // 🔑 개수 상수를 뺐다(2026-08-31 T4). 위 줄이 계약을 이미 지킨다. 소모품 12종이라는
            // 확정 콘텐츠 범위는 ConsumableItemTests.ShippingCatalogParsesTwelveItems가 잠그므로,
            // 같은 숫자를 여기 한 벌 더 두면 저작 하나에 두 곳이 깨질 뿐이다.
            Assert.That(catalog.Entries, Is.Not.Empty, "출하 소모품을 하나도 못 읽었다 — 위 대조가 빈 채로 통과한다.");

            foreach (var definition in catalog.Entries)
            {
                var entry = domain.Entries.Single(candidate => candidate.Id == definition.Id);
                Assert.That(entry.DisplayName, Is.EqualTo(definition.DisplayName));
                Assert.That(entry.Description, Is.EqualTo(definition.Description));
                Assert.That(entry.FilterChip, Is.EqualTo(definition.Category), "칩은 저작 분류를 그대로 쓴다.");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void OnlyRadiusItemsGetADiagramAndItMatchesTheAuthoredRadius()
        {
            var catalog = LoadConsumableCatalog();
            var domain = new CodexConsumableItemDomain(catalog, Accent);

            var withRadius = 0;
            foreach (var definition in catalog.Entries)
            {
                var entry = domain.Entries.Single(candidate => candidate.Id == definition.Id);

                if (definition.Radius <= 0)
                {
                    // 반경 0에 도해를 그리면 "한 칸을 겨눈다"로 읽히는데 실제로는 겨냥 자체가 없다.
                    Assert.That(entry.RangeSource, Is.Null, $"{definition.Id}: 반경이 없으면 도해도 없다.");
                    continue;
                }

                withRadius++;
                var diagram = entry.RangeSource.Resolve(0, HexDirection.East, 0);
                Assert.That(
                    diagram.ShapeCells,
                    Has.Count.EqualTo(HexArea.CellsWithin(CodexRangeArena.Origin, definition.Radius).Count()),
                    $"{definition.Id}");
            }

            Assert.That(withRadius, Is.GreaterThan(0), "반경 아이템이 하나도 없으면 위 비교가 무의미하다.");
        }

        // ── 몬스터 ────────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void MonsterDomainCarriesEveryAuthoredMonster()
        {
            var catalog = LoadMonsterCatalog();
            var domain = new CodexMonsterDomain(catalog, Accent);

            Assert.That(domain.Entries, Has.Count.EqualTo(catalog.Entries.Count));
            // 🔑 개수 상수를 뺐다(2026-08-31 T4). 위 줄이 "도감이 저작 몬스터를 전부 싣는다"를 이미
            // 지킨다. 저작 행 수와의 대조는 MonsterCatalogCsvConverterTests가 맡는다 —
            // 몬스터 한 마리를 늘릴 때 깨져야 할 곳은 거기지 도감 도메인 테스트가 아니다.
            Assert.That(catalog.Entries, Is.Not.Empty, "출하 몬스터를 하나도 못 읽었다 — 위 대조가 빈 채로 통과한다.");
        }

        [Test]
        [Category("ShippingData")]
        public void EveryMonsterPatternGetsItsOwnDiagramVariant()
        {
            // 몬스터 한 마리가 여러 형상을 치므로 하나만 보여 주면 나머지를 감추는 셈이 된다.
            var catalog = LoadMonsterCatalog();
            var domain = new CodexMonsterDomain(catalog, Accent);
            var checkedPatterns = 0;

            foreach (var monster in catalog.Entries)
            {
                var entry = domain.Entries.Single(candidate => candidate.Id == monster.Id);
                var patterns = monster.AttackPatterns ?? System.Array.Empty<MonsterAttackPattern>();

                if (patterns.Length == 0)
                {
                    Assert.That(entry.RangeSource, Is.Null, $"{monster.Id}: 패턴이 없으면 도해도 없다.");
                    continue;
                }

                Assert.That(entry.RangeSource.Variants, Has.Count.EqualTo(patterns.Length), $"{monster.Id}");

                for (var i = 0; i < patterns.Length; i++)
                {
                    var pattern = patterns[i];
                    var diagram = entry.RangeSource.Resolve(i, HexDirection.East, 0);

                    if (string.IsNullOrWhiteSpace(pattern.ShapeId))
                    {
                        // 형상 없는 패턴은 사거리 원판으로 해소된다 — 형상 도해를 내면 거짓말이 된다.
                        Assert.That(diagram.ShapeCells, Is.Not.Empty, $"{monster.Id}/{pattern.Id}");
                    }
                    else
                    {
                        var execution = AttackShapeLibrary
                            .GetAffectedCells(pattern.ShapeId, CodexRangeArena.Origin, HexDirection.East, 0)
                            .ToList();
                        Assert.That(
                            diagram.ShapeCells, Is.EquivalentTo(execution),
                            $"{monster.Id}/{pattern.Id}({pattern.ShapeId}): 도해가 집행 형상과 다르다.");
                    }

                    checkedPatterns++;
                }
            }

            Assert.That(checkedPatterns, Is.GreaterThan(10), "패턴 대조가 실제로 돌지 않았다.");
        }

        // ── 도메인 공통 ───────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void EveryDomainGivesEveryEntryANameAndAChipSoTheGridIsNeverBlank()
        {
            var domains = new ICodexDomain[]
            {
                new CodexRelicDomain(LoadRelicCatalog(), Accent),
                new CodexTrapDomain(LoadTrapCatalog(), null, Accent),
                new CodexConsumableItemDomain(LoadConsumableCatalog(), Accent),
                new CodexMonsterDomain(LoadMonsterCatalog(), Accent),
            };

            foreach (var domain in domains)
            {
                Assert.That(domain.Entries, Is.Not.Empty, $"{domain.Id}: 빈 도메인은 레일에 올리지 않는다.");

                foreach (var entry in domain.Entries)
                {
                    Assert.That(entry.Id, Is.Not.Empty, $"{domain.Id}: id 없는 항목은 해금 키를 못 만든다.");
                    Assert.That(entry.DisplayName, Is.Not.Empty, $"{domain.Id}/{entry.Id}");
                    Assert.That(
                        entry.Thumbnail.Label, Is.Not.Empty,
                        $"{domain.Id}/{entry.Id}: 폴백 썸네일은 이름 전체를 싣는다(Q9).");
                }
            }
        }

        // ── 출하 카탈로그 로더 ────────────────────────────────────────────

        // 로더는 공유 자리(CodexShippingDomains)에 있다 — 도메인을 가로지르는 시험이 같은 조립을
        // 써야 하기 때문이다. 여기서는 이름만 짧게 빌린다.
        private static RelicCatalogDefinition LoadRelicCatalog() =>
            CodexShippingDomains.LoadRelicCatalog();

        private static ConsumableItemCatalogDefinition LoadConsumableCatalog() =>
            CodexShippingDomains.LoadConsumableCatalog();

        private static TrapPresetCatalog LoadTrapCatalog() =>
            CodexShippingDomains.LoadTrapCatalog();

        private static MonsterCatalogDefinition LoadMonsterCatalog() =>
            CodexShippingDomains.LoadMonsterCatalog();
    }
}

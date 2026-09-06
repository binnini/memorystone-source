using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 키워드-문안 정합 감사(2026-08-06 키워드 체계화 패스 → 2026-09-06 P5 키워드 명시화).
    /// 키워드는 이제 카드 클래스가 <see cref="CardBehavior.Keywords"/>로 <b>선언</b>하고 장식기는 선언된 것만 건다 —
    /// 부분 문자열 우연 매칭(해체·회수 같은 범용어)이 카드에 걸릴 길이 없다. 대신 선언과 문안이 갈라지는 두 방향을 잰다:
    /// ① 선언한 키워드는 문안(설명·선택지 문안·연마 문안)에 실제로 등장해야 한다(빠지면 툴팁이 조용히 끊긴다).
    /// ② 문안에 등장하는 규칙 키워드는 전부 선언돼 있어야 한다(선언을 빠뜨리면 강조가 조용히 사라진다).
    /// </summary>
    [Category("ShippingData")]
    public sealed class CardKeywordTextBindingTests
    {
        private static KeywordCatalogDefinition LoadKeywordCatalog()
            => KeywordCatalogCsv.ConvertFile(CombatCsvPaths.GameKeywordsCsv);

        private static IReadOnlyList<CardCatalogCsvRow> LoadCardRows()
            => CardCatalogAsset.ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv));

        private static string LinkMarkup(string keyword) => $"<link=\"kw:{keyword}\"";

        /// <summary>카드가 화면에 내는 문안 전부 — 설명·갈림길 선택지 문안·연마 문안.</summary>
        private static string AllCardText(CardCatalogCsvRow row)
            => string.Join("\n", new[] { row.Description, row.ChoiceTexts, row.DescriptionUpgraded }.Where(text => !string.IsNullOrWhiteSpace(text)));

        [Test]
        public void EveryDeclaredKeywordExistsInTheCatalog()
        {
            var catalog = LoadKeywordCatalog();
            var failures = new List<string>();
            foreach (var id in CardBehaviorRegistry.RegisteredIds)
            {
                foreach (var keyword in CardBehaviorRegistry.Get(id).Keywords)
                {
                    if (!catalog.TryGet(keyword, out _))
                    {
                        failures.Add($"{id}: 선언한 키워드 '{keyword}'가 game_keywords.csv에 없다 — 원형(키워드 컬럼)으로 선언해야 한다.");
                    }
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void EveryDeclaredKeywordAppearsInTheCardText()
        {
            var catalog = LoadKeywordCatalog();
            var rows = LoadCardRows().ToDictionary(row => row.Id);
            var failures = new List<string>();

            foreach (var id in CardBehaviorRegistry.RegisteredIds)
            {
                var keywords = CardBehaviorRegistry.Get(id).Keywords;
                if (keywords.Count == 0)
                {
                    continue;
                }

                if (!rows.TryGetValue(id, out var row))
                {
                    failures.Add($"{id}: cards.csv에 없음 — 등록부와 카탈로그가 갈라졌다.");
                    continue;
                }

                var decorated = CardKeywordDecorator.Decorate(AllCardText(row), catalog, keywords);
                foreach (var keyword in keywords)
                {
                    if (!decorated.Contains(LinkMarkup(keyword)))
                    {
                        failures.Add($"{id}: 선언한 '{keyword}'가 문안에 없다 — 툴팁이 조용히 끊긴다: \"{AllCardText(row)}\"");
                    }
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void EveryRuleKeywordInTheCardTextIsDeclared()
        {
            var catalog = LoadKeywordCatalog();
            var failures = new List<string>();

            foreach (var row in LoadCardRows())
            {
                var declared = CardBehaviorRegistry.TryGet(row.Id, out var behavior)
                    ? behavior.Keywords
                    : (IReadOnlyList<string>)System.Array.Empty<string>();
                // 카탈로그 전체를 부분 문자열로 훑어 「문안이 말하는 키워드」를 얻는다 — 선언이 그것을 전부 덮어야 한다.
                var mentioned = CardKeywordDecorator.Decorate(AllCardText(row), catalog);
                foreach (var entry in catalog.Entries)
                {
                    if (mentioned.Contains(LinkMarkup(entry.Keyword)) && !declared.Contains(entry.Keyword))
                    {
                        failures.Add($"{row.Id}: 문안에 '{entry.Keyword}'가 있는데 클래스가 선언하지 않았다 — 의도면 Keywords에 추가, 우연 매칭이면 문안을 바꾼다: \"{AllCardText(row)}\"");
                    }
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        /// <summary>장식은 선언을 따른다 — 같은 단어가 있어도 선언하지 않은 카드는 걸리지 않고, 카탈로그 밖 카드는 문안이 그대로다.</summary>
        [Test]
        public void DecorationFollowsTheDeclarationNotTheSubstring()
        {
            var catalog = LoadKeywordCatalog();
            const string text = "방어막 3을 얻고 소멸합니다.";

            var basicBlock = CardKeywordDecorator.Decorate(text, catalog, CardBehaviorRegistry.Get(CardIds.BasicBlock).Keywords);
            Assert.That(basicBlock, Does.Contain(LinkMarkup("방어막")), "D00은 방어막을 선언한다.");
            Assert.That(basicBlock, Does.Not.Contain(LinkMarkup("소멸")), "D00은 소멸을 선언하지 않았으므로 단어가 있어도 걸리지 않는다.");

            var basicStrike = CardKeywordDecorator.Decorate(text, catalog, CardBehaviorRegistry.Get(CardIds.BasicStrike).Keywords);
            Assert.That(basicStrike, Is.EqualTo(text), "선언이 없는 카드의 문안은 그대로다.");
        }

        /// <summary>
        /// CSV(SOT)와 베이크 에셋의 동기 검사. '힘'·'미지' 2행이 베이크 없이 출하됐던 회귀의 재발 방지 —
        /// 런타임은 베이크본만 읽으므로 이 검사가 깨져 있으면 새 키워드는 게임에 존재하지 않는 것과 같다.
        /// 고치는 법: 메뉴 "Seoul Playup/Combat/Bake Game Keyword Catalog".
        /// </summary>
        [Test]
        public void BakedResourcesCatalogMatchesCsv()
        {
            var csv = LoadKeywordCatalog();
            var baked = Resources.Load<CardKeywordCatalog>(CardKeywordCatalog.DefaultResourcesPath);
            Assert.That(baked, Is.Not.Null, "베이크 에셋이 없다 — Bake Game Keyword Catalog 메뉴를 실행할 것.");

            var csvKeywords = csv.Entries.Select(e => e.Keyword).OrderBy(k => k, System.StringComparer.Ordinal).ToList();
            var bakedKeywords = baked.Entries.Select(e => e.keyword).OrderBy(k => k, System.StringComparer.Ordinal).ToList();
            Assert.That(bakedKeywords, Is.EqualTo(csvKeywords),
                "game_keywords.csv와 베이크 에셋이 갈라졌다 — Bake Game Keyword Catalog 메뉴로 재베이크할 것.");
        }
    }
}

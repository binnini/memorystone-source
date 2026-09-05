using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 키워드-텍스트 정합 감사(2026-08-06 키워드 체계화 패스). 키워드 바인딩이 컬럼이 아니라
    /// 설명 텍스트 substring 매칭이라, 문안이 단어를 빠뜨리면 <b>조용히</b> 미연결이 된다 —
    /// 실제로 횃불(U04)·상태 카드(X01~X03)가 그렇게 끊어진 채 출하돼 있었다.
    /// 기대 목록은 docs/design/keyword-systematization-and-sts-insights.md §2.3의 확정 표가 정본.
    ///
    /// 범용어 키워드(해체 등)의 <b>오매칭</b>도 여기서 잡는다: 새 키워드/문안이 의도하지 않은
    /// 카드에 걸리면 그 카드의 기대 목록에 없으므로 아래 역방향 검사가 실패한다.
    /// </summary>
    [Category("ShippingData")]
    public sealed class CardKeywordTextBindingTests
    {
        /// <summary>카드 id → 설명 텍스트에 걸려야 하는 키워드(원형) 전부. 여기 없는 카드는 키워드 0이 기대값.</summary>
        private static readonly Dictionary<string, string[]> ExpectedKeywordsByCard = new Dictionary<string, string[]>
        {
            ["M05"] = new[] { "민첩" },
            ["A03"] = new[] { "갈림길" },
            ["A08"] = new[] { "복사" },
            ["A09"] = new[] { "아지랑이" },
            ["A10"] = new[] { "소멸" },
            ["A11"] = new[] { "속박" },
            ["A12"] = new[] { "전염" },
            ["A13"] = new[] { "소멸" },
            ["A14"] = new[] { "쇠약" },
            ["D00"] = new[] { "방어막" },
            ["D01"] = new[] { "방어막" },
            ["D02"] = new[] { "무적", "강화" },
            ["D03"] = new[] { "반사" },
            ["D04"] = new[] { "속박", "방어막" },
            ["D05"] = new[] { "무적", "소멸" },
            ["D06"] = new[] { "속박", "방어막", "정화" },
            ["D07"] = new[] { "방어막", "유지" },
            ["M07"] = new[] { "유지" },
            ["S00"] = new[] { "탐색" },
            ["S01"] = new[] { "탐색" },
            ["S02"] = new[] { "탐색" },
            ["S03"] = new[] { "탐색", "기절" },
            ["S04"] = new[] { "탐색" },
            // 2026-08-20 #5: S05가 「범위 1을 탐색하고 발견된 함정을 해체합니다」로 진짜 정찰이 되며 탐색 획득.
            ["S05"] = new[] { "탐색", "해체" },
            ["S06"] = new[] { "탐색", "허점" },
            ["F01"] = new[] { "장판" },
            ["F02"] = new[] { "장판" },
            ["F03"] = new[] { "속박", "장판" },
            ["F04"] = new[] { "흡혈", "장판" },
            ["F05"] = new[] { "장판" },
            ["U02"] = new[] { "갈림길", "소멸" },
            ["U03"] = new[] { "정화" },
            ["U04"] = new[] { "등불" },
            ["X01"] = new[] { "사용 불가", "아지랑이" },
            ["X04"] = new[] { "소멸" },
            ["X05"] = new[] { "사용 불가" },
            ["X06"] = new[] { "사용 불가" },
            ["X07"] = new[] { "사용 불가" },
            ["X08"] = new[] { "사용 불가" },
            ["X09"] = new[] { "사용 불가", "쇠약" },
            ["X10"] = new[] { "사용 불가", "실명" },
            ["X11"] = new[] { "사용 불가", "봉인", "아지랑이" },
            // 원귀는 「손에 있는 동안 허점 1턴 부여」로 바뀌며 허점 어휘를 실제로 들고 있다(#18).
            ["X12"] = new[] { "사용 불가", "허점" },
            ["X02"] = new[] { "사용 불가" },
            ["X03"] = new[] { "사용 불가", "봉인" },
        };

        private static KeywordCatalogDefinition LoadKeywordCatalog()
            => KeywordCatalogCsv.ConvertFile(CombatCsvPaths.GameKeywordsCsv);

        private static IReadOnlyList<CardCatalogCsvRow> LoadCardRows()
            => CardCatalogAsset.ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv));

        private static string LinkMarkup(string keyword) => $"<link=\"kw:{keyword}\"";

        [Test]
        public void EveryExpectedKeywordDecoratesItsCardText()
        {
            var catalog = LoadKeywordCatalog();
            var rows = LoadCardRows().ToDictionary(row => row.Id);
            var failures = new List<string>();

            foreach (var pair in ExpectedKeywordsByCard)
            {
                if (!rows.TryGetValue(pair.Key, out var row))
                {
                    failures.Add($"{pair.Key}: cards.csv에 없음 — 기대 목록이 낡았다.");
                    continue;
                }

                var decorated = CardKeywordDecorator.Decorate(row.Description, catalog);
                foreach (var keyword in pair.Value)
                {
                    if (!decorated.Contains(LinkMarkup(keyword)))
                    {
                        failures.Add($"{pair.Key}: '{keyword}' 미연결 — 설명 문안에 키워드 단어가 없다: \"{row.Description}\"");
                    }
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void NoCardGainsAnUnexpectedKeyword()
        {
            var catalog = LoadKeywordCatalog();
            var failures = new List<string>();

            foreach (var row in LoadCardRows())
            {
                var expected = ExpectedKeywordsByCard.TryGetValue(row.Id, out var keywords)
                    ? keywords
                    : System.Array.Empty<string>();
                var decorated = CardKeywordDecorator.Decorate(row.Description, catalog);

                foreach (var entry in catalog.Entries)
                {
                    if (decorated.Contains(LinkMarkup(entry.Keyword)) && !expected.Contains(entry.Keyword))
                    {
                        failures.Add($"{row.Id}: 예상 밖 키워드 '{entry.Keyword}' 매칭 — 오매칭이면 문안이나 키워드 어휘를 바꾸고 의도면 기대 목록에 추가: \"{row.Description}\"");
                    }
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
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

        /// <summary>갈림길 옵션 문안은 별도 CSV(2중 표면)라 카드 설명 검사에 안 걸린다 — 회수(U02)만 직접 잠근다.</summary>
        [Test]
        public void ChoiceOptionTextKeepsRecoverKeyword()
        {
            var text = File.ReadAllText(CombatCsvPaths.CardChoiceOptionsCsv);
            Assert.That(text, Does.Contain("회수"),
                "U02 recover 옵션 문안에서 '회수' 키워드 단어가 빠졌다.");
        }
    }
}

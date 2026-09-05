using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// game_keywords.csv 효과문의 표준 서술 게이트(WS-I, DEC-2026-08-19-08).
    ///
    /// 배경: 효과문에 숫자가 리터럴로 박혀 있으면 연마(쇠약 30→50)·함정 변주(중독 1 vs 3)와
    /// 반드시 어긋난다 — I-01/I-06이 그렇게 출하됐다. 크기가 있는 키워드는 {값} 토큰으로 저작하고
    /// 표면이 실값/저작값으로 해소한다(<see cref="KeywordEffectText"/>). 이 게이트가 재발을 막는다.
    /// </summary>
    [Category("ShippingData")]
    public sealed class KeywordTextConsistencyTests
    {
        private static KeywordCatalogDefinition LoadKeywordCatalog()
            => KeywordCatalogCsv.ConvertFile(CombatCsvPaths.GameKeywordsCsv);

        private const string ValueToken = "{값}";

        [Test]
        public void FixedValueKeywordsUseTheValueTokenNotALiteral()
        {
            var failures = new List<string>();
            foreach (var entry in LoadKeywordCatalog().Entries)
            {
                if (entry.ValueKind != KeywordValueKind.Fixed)
                {
                    continue;
                }

                if (!entry.Effect.Contains(ValueToken))
                {
                    failures.Add($"{entry.Keyword}: 고정 값 키워드인데 효과문에 {ValueToken} 토큰이 없다 — 리터럴 숫자는 연마·변주와 어긋난다.");
                }

                if (string.IsNullOrWhiteSpace(entry.Value))
                {
                    failures.Add($"{entry.Keyword}: 값종류=고정인데 값 컬럼이 비어 있다 — 정적 표면이 토큰을 해소할 수 없다.");
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void ValueTokenAlwaysHasAnAuthoredValueToResolveTo()
        {
            var failures = new List<string>();
            foreach (var entry in LoadKeywordCatalog().Entries)
            {
                if (entry.Effect.Contains(ValueToken) && string.IsNullOrWhiteSpace(entry.Value))
                {
                    failures.Add($"{entry.Keyword}: 효과문에 {ValueToken}이 있는데 값 컬럼이 비어 있다.");
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        /// <summary>
        /// 지속시간 의미론 게이트(WS-I §4): 상태이상 틱·감소는 전부 <b>내 턴 시작</b> 경계에서 일어난다 —
        /// 「턴 종료 시」를 말하는 상태이상/버프 효과문은 집행과 어긋난 서술이다(I-01 중독이 그랬다).
        /// 손패 훅으로 실제 턴말에 발동하는 「카드 효과」 분류(아지랑이·유지)는 게이트 밖이다.
        /// </summary>
        [Test]
        public void StatusKeywordsNeverClaimTurnEndTiming()
        {
            var failures = new List<string>();
            foreach (var entry in LoadKeywordCatalog().Entries)
            {
                if (entry.Category == "카드 효과")
                {
                    continue;
                }

                if (entry.Effect.Contains("턴 종료 시"))
                {
                    failures.Add($"{entry.Keyword}: 상태이상·버프 서술에 「턴 종료 시」 — 틱·감소는 내 턴 시작 경계다.");
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void EffectTextResolvesAuthoredAndLiveValues()
        {
            var catalog = LoadKeywordCatalog();
            Assert.That(catalog.TryGet("중독", out var poison), Is.True);

            Assert.That(
                KeywordEffectText.Resolve(poison),
                Does.Contain("3의 피해"),
                "정적 표면(카드 호버·도감)은 값 컬럼(3)으로 해소한다.");
            Assert.That(
                KeywordEffectText.Resolve(poison, liveAmount: 1),
                Does.Contain("1의 피해"),
                "HUD는 살아 있는 효과의 실값(함정 중독 1)으로 해소한다 — I-01의 1/3 동시 표기가 여기서 사라진다.");

            Assert.That(catalog.TryGet("쇠약", out var weaken), Is.True);
            Assert.That(
                KeywordEffectText.Resolve(weaken, liveAmount: 50),
                Does.Contain("50%"),
                "연마된 쇠약(A14+ 50%)은 실값으로 말한다 — I-06.");
        }

        /// <summary>몬스터 어휘 절이 조용히 생략하던 특성(견고)이 다시 빠지지 않게 잠근다(WS-I §2 지도 ⑧-2).</summary>
        [Test]
        public void MonsterTraitKeywordsExistInTheCatalog()
        {
            var catalog = LoadKeywordCatalog();
            foreach (var keyword in new[] { "약오름", "맷집", "뒤끝", "견고" })
            {
                Assert.That(catalog.TryGet(keyword, out _), Is.True, $"특성 키워드 '{keyword}' 행이 game_keywords.csv에 없다 — 몬스터 툴팁 어휘 절이 조용히 생략된다.");
            }
        }
    }
}

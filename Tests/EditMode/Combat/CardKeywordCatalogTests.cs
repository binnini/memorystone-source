using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CardKeywordCatalogTests
    {
        private static KeywordCatalogDefinition LoadDesignerCatalog()
            => KeywordCatalogCsv.ConvertFile(CombatCsvPaths.GameKeywordsCsv);

        [Test]
        public void ConvertsDesignerGameKeywordsCsv()
        {
            var catalog = LoadDesignerCatalog();

            // P2(묶음 B)에서 무장 해제·쇠약·허점 3종이 붙어 15 → 18,
            // P2.7(묶음 D)에서 봉인·상태 카드·횃불 3종이 더 붙어 21.
            // 보스 레이드 머지(/main/dev/boss)에서 '보스 기운'(BossAura) 키워드가 더 붙어 22.
            // 힘(Might)·미지(Unknown)가 더해져 24 — 상태 kind는 표시명으로 키워드를 조인하므로 kind를 늘리면 여기도 는다.
            // 키워드 체계화 패스(2026-08-06): 상태 카드→사용 불가 대체 · 횃불→등불 개명에 신규 10종
            // (흡혈·전염·무적·장판·탐색·회수·해체·끌어당김·도약·취약 부위)이 붙어 34.
            // T2(2026-08-06): 저주·아지랑이 2종이 붙어 36.
            // T2 페이즈 C(2026-08-06): 수호·은신 2종(신규 StatusEffectKind 17·18)이 붙어 38.
            // T5-2(2026-08-07): 「유지」(retainOnTurnEnd)가 붙어 39.
            // T7-2(2026-08-07): 신규 적 문법 3행(약오름·맷집·뒤끝, 분류=적)이 붙어 42.
            // WS-I(2026-08-19, DEC-2026-08-19-08): 견고 행 신설(어휘 절 조용한 생략 해소)로 43.
            // 2026-09-01 사용자 확정: 「신발 잃음」 어휘 은퇴(속박으로 대체) — 49에서 하나 줄었다.
            // 2026-09-04 특성 일원화: 담력 시험·홀림·소매치기 3행 신설로 51. 이때 분류 「적」 중
            // 특성인 것들(견고·약오름·맷집·뒤끝·심술·밀어붙이기·어둠 먹기)이 「특성」으로 옮겨졌고,
            // 몬스터 은신 특성이 휴면이던 플레이어 「은신」 버프 행을 물려받았다(monster_traits.csv).
            // 2026-09-05 수호 재충전(특성) 1행 신설로 52.
            Assert.That(catalog.Entries.Count, Is.EqualTo(52));

            // 실명(C-1): 상태이상 이름은 game_keywords.csv와 status_effects.csv를 잇는 조인 키다
            // (StatusEffectCatalogWiringTests.EveryStatusDisplayNameResolvesAGameKeyword 참고).
            Assert.That(catalog.TryGet("실명", out var blind), Is.True);
            Assert.That(blind.Category, Is.EqualTo("상태이상"));
            Assert.That(blind.Effect, Does.Contain("시야"));

            Assert.That(catalog.TryGet("속박", out var immobilize), Is.True); // 속박
            Assert.That(immobilize.Category, Is.EqualTo("상태이상")); // 상태이상
            Assert.That(immobilize.Effect, Does.Contain("이동 카드를 사용할 수 없습니다")); // WS-I 표준 서술 — 지속은 부여마다 달라 문안이 턴을 말하지 않는다.

            // Parenthesised keyword is stored in its canonical 원형 form.
            Assert.That(catalog.TryGet("밀치기(넉백)", out var knockback), Is.True); // 밀치기(넉백)
            Assert.That(knockback.Category, Is.EqualTo("상태이상"));

            // 정화 is a card effect, not a status: it resolves instantly and never becomes an ActiveEffect (D1/D9).
            Assert.That(catalog.TryGet("정화", out var cleanse), Is.True); // 정화
            Assert.That(cleanse.Category, Is.EqualTo("카드 효과")); // 카드 효과
            Assert.That(cleanse.ValueKind, Is.EqualTo(KeywordValueKind.None));
            Assert.That(cleanse.Effect, Does.Contain("해제")); // ...모든 상태이상을 해제합니다.

            Assert.That(catalog.TryGet("존재하지않음", out _), Is.False);
        }

        [Category("ShippingData")]
        [Test]
        public void ParenthesisedKeywordExpandsToBaseAndInnerAliases()
        {
            var catalog = LoadDesignerCatalog();

            // 밀치기 and 넉백 both resolve to the same canonical entry, but the literal "밀치기(넉백)" is not a term.
            var terms = catalog.MatchTermsLongestFirst.Select(t => t.Term).ToList();
            Assert.That(terms, Does.Contain("밀치기")); // 밀치기
            Assert.That(terms, Does.Contain("넉백")); // 넉백
            Assert.That(terms, Does.Not.Contain("밀치기(넉백)"));

            // Monster pattern text uses 넉백; decoration must wrap it with the canonical link id.
            var decorated = CardKeywordDecorator.Decorate("넉백 2칸", catalog); // "넉백 2칸"
            Assert.That(decorated, Does.Contain("<link=\"kw:밀치기(넉백)\"><i><b>넉백</b></i></link>"));
        }

        [Category("ShippingData")]
        [Test]
        public void DecoratesKeywordWithEmphasisAndLink()
        {
            var catalog = LoadDesignerCatalog();

            var decorated = CardKeywordDecorator.Decorate("선택한 적을 속박 2턴으로 만듭니다.", catalog); // ...속박 2턴으로...
            Assert.That(decorated, Does.Contain("<link=\"kw:속박\"><i><b>속박</b></i></link>"));
        }

        [Test]
        public void LongestKeywordWinsAtSameStartIndex()
        {
            var catalog = new KeywordCatalogDefinition(new[]
            {
                new KeywordDefinition("카드 효과", "방어", "shorter"),    // 방어
                new KeywordDefinition("카드 효과", "방어막", "longer"), // 방어막
            });

            var decorated = CardKeywordDecorator.Decorate("방어막을 얻습니다", catalog); // 방어막을 얻습니다
            Assert.That(decorated, Does.Contain("<i><b>방어막</b></i>"));
            Assert.That(decorated, Does.Not.Contain("<i><b>방어</b></i>막"));
        }

        [Category("ShippingData")]
        [Test]
        public void TextWithoutKeywordsIsUnchanged()
        {
            var catalog = LoadDesignerCatalog();
            const string plain = "최대 3칸 이동합니다."; // 최대 3칸 이동합니다.
            Assert.That(CardKeywordDecorator.Decorate(plain, catalog), Is.EqualTo(plain));
        }

        [Category("ShippingData")]
        [Test]
        public void DecorationIsIdempotent()
        {
            var catalog = LoadDesignerCatalog();
            const string source = "소멸합니다. 이 카드를 복사합니다."; // 소멸합니다. 이 카드를 복사합니다.

            var once = CardKeywordDecorator.Decorate(source, catalog);
            var twice = CardKeywordDecorator.Decorate(once, catalog);
            Assert.That(twice, Is.EqualTo(once));
            Assert.That(once, Does.Contain("<i><b>소멸</b></i>")); // 소멸
            Assert.That(once, Does.Contain("<i><b>복사</b></i>")); // 복사
        }

        [Test]
        public void NullCatalogReturnsPlainText()
        {
            const string source = "속박 2턴"; // 속박 2턴
            Assert.That(CardKeywordDecorator.Decorate(source, null), Is.EqualTo(source));
        }
    }
}

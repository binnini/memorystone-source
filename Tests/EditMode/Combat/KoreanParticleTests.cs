using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Card copy is authored as templates, so the particle is written before the token's value is
    /// known. These lock the agreement rules that let "{Shape}를" over "범위 1" print "범위 1을".
    /// </summary>
    public sealed class KoreanParticleTests
    {
        [TestCase("1", "을")]   // 일
        [TestCase("2", "를")]   // 이
        [TestCase("3", "을")]   // 삼
        [TestCase("4", "를")]   // 사
        [TestCase("5", "를")]   // 오
        [TestCase("6", "을")]   // 육
        [TestCase("7", "을")]   // 칠
        [TestCase("8", "을")]   // 팔
        [TestCase("9", "를")]   // 구
        [TestCase("0", "을")]   // 영
        [TestCase("10", "을")]  // 십
        public void DigitsAgreeWithHowTheyAreReadAloud(string value, string expected)
        {
            Assert.That(KoreanParticle.Agree(value, "를"), Is.EqualTo(expected));
        }

        [TestCase("범위 1", "을")]
        [TestCase("범위 2", "를")]
        [TestCase("가까운 부채꼴", "을")]
        [TestCase("협공", "을")]
        [TestCase("인접 고리", "를")]
        [TestCase("V 분할", "을")]
        public void ShapeNamesAgreeWithTheirFinalSyllable(string value, string expected)
        {
            Assert.That(KoreanParticle.Agree(value, "를"), Is.EqualTo(expected));
        }

        [Test]
        public void EveryParticlePairSwitchesOnTheFinalConsonant()
        {
            Assert.That(KoreanParticle.Agree("범위 1", "가"), Is.EqualTo("이"));
            Assert.That(KoreanParticle.Agree("범위 2", "이"), Is.EqualTo("가"));
            Assert.That(KoreanParticle.Agree("범위 1", "는"), Is.EqualTo("은"));
            Assert.That(KoreanParticle.Agree("범위 2", "은"), Is.EqualTo("는"));
            Assert.That(KoreanParticle.Agree("범위 1", "와"), Is.EqualTo("과"));
            Assert.That(KoreanParticle.Agree("범위 2", "과"), Is.EqualTo("와"));
        }

        [Test]
        public void RieulTakesTheOpenFormOfEuroRo()
        {
            // 'ㄹ' patterns like an open syllable for 으로/로.
            Assert.That(KoreanParticle.Agree("1", "으로"), Is.EqualTo("로"));   // 일
            Assert.That(KoreanParticle.Agree("2", "으로"), Is.EqualTo("로"));   // 이, no 받침
            Assert.That(KoreanParticle.Agree("3", "으로"), Is.EqualTo("으로")); // 삼
            Assert.That(KoreanParticle.Agree("서울", "으로"), Is.EqualTo("로"));
            Assert.That(KoreanParticle.Agree("정찰", "로"), Is.EqualTo("로"));
            Assert.That(KoreanParticle.Agree("범위 3", "로"), Is.EqualTo("으로"));
        }

        [Test]
        public void ResolveTokensFixesTheParticleThatFollowsTheToken()
        {
            string Lookup(string token) => token == "Shape" ? "범위 1" : token == "Damage" ? "1" : null;

            Assert.That(KoreanParticle.ResolveTokens("{Shape}를 탐색합니다.", Lookup),
                Is.EqualTo("범위 1을 탐색합니다."));
            Assert.That(KoreanParticle.ResolveTokens("피해 {Damage}를 줍니다.", Lookup),
                Is.EqualTo("피해 1을 줍니다."));
        }

        [Test]
        public void ResolveTokensLeavesTextThatOnlyLooksLikeAParticle()
        {
            string Lookup(string token) => token == "Damage" ? "1" : null;

            // "이상" is a word, not a subject marker, so the "이" must survive untouched.
            Assert.That(KoreanParticle.ResolveTokens("피해 {Damage}이상", Lookup),
                Is.EqualTo("피해 1이상"));
            // A token with no particle after it is a plain substitution.
            Assert.That(KoreanParticle.ResolveTokens("피해 {Damage} 적용", Lookup),
                Is.EqualTo("피해 1 적용"));
        }

        [Test]
        public void UnknownTokensAreLeftInPlaceSoTyposStayVisible()
        {
            Assert.That(KoreanParticle.ResolveTokens("{Nope}를 확인", token => null),
                Is.EqualTo("{Nope}를 확인"));
        }

        [Test]
        public void ParticleAtEndOfStringStillAgrees()
        {
            string Lookup(string token) => "범위 1";
            Assert.That(KoreanParticle.ResolveTokens("{Shape}를", Lookup), Is.EqualTo("범위 1을"));
        }

        /// <summary>
        /// 실효 수치 강조가 값을 리치 텍스트로 감싸므로 조사는 태그가 아니라 <b>숫자</b>를 보고 정해져야 한다.
        /// 태그를 건너뛰지 않으면 마지막 글자가 '>'라 항상 받침 없음으로 판정돼 "6를"이 찍힌다.
        /// </summary>
        [Test]
        public void RichTextTagsDoNotDecideParticleAgreement()
        {
            string Colored(string token) => "<color=#6FE07A>6</color>";
            Assert.That(KoreanParticle.ResolveTokens("피해 {Damage}를 줍니다.", Colored),
                Is.EqualTo("피해 <color=#6FE07A>6</color>을 줍니다."),
                "6은 '육'이라 받침이 있다 — 태그 너머의 숫자가 조사를 정해야 한다.");

            string ColoredTwo(string token) => "<color=#FF7A7A>2</color>";
            Assert.That(KoreanParticle.ResolveTokens("피해 {Damage}를 줍니다.", ColoredTwo),
                Is.EqualTo("피해 <color=#FF7A7A>2</color>를 줍니다."),
                "2는 '이'라 받침이 없다 — 감소 색이어도 판정 축은 같다.");
        }
    }
}

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// game_keywords.csv 효과문의 <c>{값}</c> 토큰 해소 단일 지점(WS-I, DEC-2026-08-19-08).
    ///
    /// 계약: 효과문에 숫자를 리터럴로 박지 않는다 — 크기가 있는 키워드는 <c>{값}</c>으로 저작하고,
    /// HUD처럼 살아 있는 인스턴스를 아는 표면은 실값(연마·함정 변주 반영)을, 카드 호버·도감처럼
    /// 정적인 표면은 CSV 값 컬럼을 넣는다. 이 규약 덕에 「쇠약 30%」가 연마 후에도 30%라고
    /// 우기던 괴리(I-06)와 중독 1/3 동시 표기(I-01)가 같은 자리에서 사라진다.
    /// <c>KeywordTextConsistencyTests</c>가 리터럴 숫자 금지와 {값}↔값 컬럼 짝을 잠근다.
    /// </summary>
    public static class KeywordEffectText
    {
        public const string ValueToken = "값";

        /// <summary>정적 표면(카드 호버·도감·몬스터 어휘 절)용 — CSV 값 컬럼으로 해소.</summary>
        public static string Resolve(KeywordDefinition definition)
            => Resolve(definition, liveAmount: 0);

        /// <summary>
        /// HUD 표면용 — 살아 있는 효과의 실값이 있으면 그것으로, 없으면 값 컬럼으로 해소한다.
        /// 조사(을/를…)는 <see cref="KoreanParticle"/>이 치환값에 맞춰 준다.
        /// </summary>
        public static string Resolve(KeywordDefinition definition, int liveAmount)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Effect))
            {
                return string.Empty;
            }

            return KoreanParticle.ResolveTokens(definition.Effect, token =>
            {
                if (token != ValueToken)
                {
                    return null;
                }

                if (liveAmount > 0)
                {
                    return liveAmount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                var authored = definition.Value?.Trim();
                return string.IsNullOrEmpty(authored) ? null : authored;
            });
        }
    }
}

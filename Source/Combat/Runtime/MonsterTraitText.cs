namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 특성 한 줄을 <b>화면에 쓸 글자</b>로 푸는 단일 지점. 제목은 monster_traits.csv,
    /// 설명문은 game_keywords.csv — 어느 쪽도 여기에 문자열을 새로 적지 않는다.
    ///
    /// <para>🔑 이 파일이 생긴 이유가 D6이다: 같은 특성을 키워드 카탈로그와 배지 툴팁이 각각
    /// 자기 문장으로 설명하고 있었다. 뜻이 같아 티가 안 났을 뿐, 한쪽만 고치는 날 갈라진다.
    /// 이제 <b>읽는 자리가 하나</b>라 갈라질 표면이 없다.</para>
    ///
    /// <para>🔑 <paramref name="descriptionValue"/>는 <c>{값}</c> 토큰이 해소될 실값이다 —
    /// 맷집의 재장전 턴, 약오름의 상한, 홀림의 봉인 장수처럼 <b>몬스터마다 다른</b> 수치라
    /// CSV 값 컬럼을 그대로 찍으면 다른 저작에서 화면이 거짓말한다(D7). 0이면 CSV 값으로 떨어진다.</para>
    /// </summary>
    public static class MonsterTraitText
    {
        public static bool TryGet(string traitId, int descriptionValue, out string title, out string description)
            => TryGet(
                MonsterTraitCatalogProvider.Active,
                CardKeywordCatalogProvider.Active,
                traitId,
                descriptionValue,
                out title,
                out description);

        /// <summary>카탈로그를 <b>인자로</b> 받는 갈래 — 테스트가 픽스처를 넣을 수 있어야 한다.</summary>
        public static bool TryGet(
            MonsterTraitCatalogDefinition traits,
            KeywordCatalogDefinition keywords,
            string traitId,
            int descriptionValue,
            out string title,
            out string description)
        {
            title = string.Empty;
            description = string.Empty;
            if (traits == null || !traits.TryGet(traitId, out var trait))
            {
                return false;
            }

            title = trait.DisplayName;
            if (keywords != null && keywords.TryGet(trait.KeywordRef, out var keyword))
            {
                description = KeywordEffectText.Resolve(keyword, descriptionValue);
            }

            return true;
        }

        /// <summary>
        /// 이 특성의 설명문이 <c>{값}</c>에 넣을 값. 저작이 여러 컬럼에 흩어져 있으므로
        /// <see cref="MonsterTraitResolver"/>와 같은 이유로 유도를 한 곳에 모은다.
        /// 0 = 몬스터별 값이 없다(설명문이 CSV 값 컬럼으로 해소된다).
        /// </summary>
        public static int ResolveDescriptionValue(MonsterCatalogEntry entry, string traitId)
        {
            switch (traitId)
            {
                case MonsterTraitIds.Agitation:
                    // 문안이 말하는 것은 「최대 {값}」이지 지금 스택이 아니다 — 상한을 넣는다.
                    return entry.AgitationMaxStacks;

                case MonsterTraitIds.Toughness:
                    return entry.ToughnessReloadTurns;

                case MonsterTraitIds.GuardCycle:
                    return entry.GuardRechargeTurns;

                case MonsterTraitIds.AuraSeal:
                    return MonsterAuraSeal.TryParse(entry.HiddenTraitRef, entry.HiddenTraitParam, out var seal, out _)
                        ? seal.Cards
                        : 0;

                case MonsterTraitIds.Pickpocket:
                    return MaxStealAmount(entry);

                case MonsterTraitIds.StrengthDistance:
                    // 문안의 {값}은 「최대」다(2026-09-05 3차 #4-5-8) — 거리 1칸당 2는 규칙 상수라 리터럴로 둔다.
                    return entry.AgitationMaxStacks;

                case MonsterTraitIds.Aftermath:
                    // 「사망 시 {값}칸 내의 플레이어에게」 — 뒤끝 갈래마다 반경이 저작된다.
                    return MonsterDeathAftermath.TryParse(entry.OnDeathEffectRef, entry.OnDeathEffectParam, out var aftermath, out _)
                        ? aftermath.Radius
                        : 0;

                case MonsterTraitIds.Stealth:
                    // 「들킨 후 {값}턴 후에 다시 은신」 — revealTurns 저작(기본 4).
                    return MonsterHiddenTrait.TryParse(entry.HiddenTraitRef, entry.HiddenTraitParam, out var hidden, out _)
                        ? hidden.RevealTurns
                        : 0;

                default:
                    return 0;
            }
        }

        private static int MaxStealAmount(MonsterCatalogEntry entry)
        {
            var patterns = entry.AttackPatterns;
            if (patterns == null)
            {
                return 0;
            }

            var max = 0;
            for (var i = 0; i < patterns.Length; i++)
            {
                if (patterns[i].StealMoneyAmount > max)
                {
                    max = patterns[i].StealMoneyAmount;
                }
            }

            return max;
        }
    }
}

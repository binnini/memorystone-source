using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Shared bridge from a runtime <see cref="StatusEffectKind"/> / <see cref="ActiveEffect"/> to its
    /// designer-authored game keyword (game_keywords.csv via <see cref="CardKeywordCatalogProvider"/>).
    /// Produces the title + body lines used by the status-effect HUD hover tooltip (REQ1) and reused by
    /// the monster info panel. Fixed-magnitude keywords (중독/둔화/파열/반사/강화) show the authored number that
    /// already lives inline in the 효과 text; dynamic keywords (민첩) additionally surface the live runtime
    /// amount so the displayed number tracks the actual effect instance.
    /// </summary>
    public static class StatusEffectTooltipContent
    {
        /// <summary>Canonical Korean keyword (원형) for a status kind, matching game_keywords.csv 키워드.</summary>
        public static string KeywordFor(StatusEffectKind kind) => StatusEffectInfo.DisplayName(kind);

        /// <summary>Tooltip title = the keyword name.</summary>
        public static string Title(StatusEffectKind kind) => KeywordFor(kind);

        /// <summary>분류 (e.g. 상태이상 / 버프) from the catalog, or empty when unavailable.</summary>
        public static string Category(StatusEffectKind kind)
            => TryGetDefinition(kind, out var def) ? def.Category : string.Empty;

        /// <summary>
        /// Description sentence for the keyword. {값} 토큰은 살아 있는 효과의 실값(연마·함정 변주 반영)으로
        /// 해소된다(<see cref="KeywordEffectText"/>, WS-I) — "쇠약 30%" 문안이 연마 후에도 30%를 우기던
        /// 괴리(I-06)가 여기서 사라진다. 토큰 없는 유동(Dynamic) 키워드는 기존대로 실값을 덧붙인다.
        /// </summary>
        public static string Description(ActiveEffect effect)
        {
            if (!TryGetDefinition(effect.Kind, out var def) || string.IsNullOrWhiteSpace(def.Effect))
            {
                return string.Empty;
            }

            var liveAmount = def.ValueKind != KeywordValueKind.None ? effect.Amount : 0;
            var resolved = KeywordEffectText.Resolve(def, liveAmount);
            if (def.ValueKind == KeywordValueKind.Dynamic && effect.Amount > 0
                && !def.Effect.Contains("{" + KeywordEffectText.ValueToken + "}"))
            {
                return $"{resolved} (현재 수치 {effect.Amount})";
            }

            return resolved;
        }

        /// <summary>
        /// 남은 지속 줄.
        ///
        /// <para>🔴 <b>숫자만 찍으면 안 된다 — 같은 「2」가 세는 대상이 종류마다 다르다.</b>
        /// 중독의 2는 「앞으로 2번 더 발동한다」(틱 횟수)이고, 둔화의 2는 「2턴 동안 붙어 있다」(지속
        /// 구간)이며, 수호의 저작 지속 1은 <b>아무 의미도 없다</b>(턴이 아니라 소비로 사라진다 —
        /// <see cref="StatusEffectExpirePolicy.OnConsume"/>). 셋을 전부 "남은 N턴"으로 찍으면
        /// 플레이어가 배지를 보고 다음 턴을 예측할 수 없다. 그래서 이 함수가 <b>세는 대상을 문면에
        /// 드러낸다</b>.</para>
        ///
        /// <para>만료가 없는 효과는 턴 수를 말할 수 없다 — 힘처럼 런 영구로 투영된 항목
        /// (RemainingTurns 0)과 지속 0으로 저작된 보스 기운이 여기 해당한다. 이 분기가 없으면
        /// 툴팁이 "남은 0턴"이라고 찍어 곧 사라진다는 거짓말을 한다.</para>
        ///
        /// <para>⚠️ 알려진 갭: 횃불은 지속시간과 <b>수치(반경)</b>가 함께 깎이는 유일한 종류라
        /// "3턴 동안 유지"가 절반만 참이다(3턴 내내 같은 반경이 아니다). 종류 하드코딩을 하나 더
        /// 늘리지 않으려고 여기서 다루지 않았다 — 고친다면 저작 축(<c>valueMode</c>)으로 유도할 것.</para>
        /// </summary>
        public static string RemainingTurnsLine(ActiveEffect effect)
        {
            var policy = StatusEffectInfo.ExpirePolicy(effect.Kind);

            // 소비형(수호)은 턴으로 만료되지 않는다. 남은 것은 턴이 아니라 「횟수」다.
            if (policy == StatusEffectExpirePolicy.OnConsume)
            {
                return effect.Amount > 0 ? $"{effect.Amount}회 남음" : "소진됨";
            }

            if (effect.RemainingTurns <= 0)
            {
                return "지속: 영구";
            }

            // 틱형(중독)은 턴 경계마다 한 번씩 「발동」한다 — 남은 수가 곧 남은 발동 횟수다.
            // 그 외(존재형·값형)는 그 턴 수 동안 「붙어 있다」.
            return policy == StatusEffectExpirePolicy.TurnStartAfterTick
                ? $"{effect.RemainingTurns}번 더 발동"
                : $"{effect.RemainingTurns}턴 동안 유지";
        }

        /// <summary>
        /// 배지에 찍을 숫자 한 조각. 🔴 <b>소비형(수호)은 남은 턴이 아니라 남은 「횟수」다</b> —
        /// 저작 지속 1을 그대로 찍으면 "1턴 뒤 사라진다"로 읽히지만 실제로는 한 번 막을 때까지 남는다.
        /// 툴팁(<see cref="RemainingTurnsLine"/>)과 <b>같은 판정</b>을 쓰도록 여기 모았다 — 어휘가
        /// 두 벌이면 반드시 갈린다.
        /// </summary>
        public static string BadgeNumber(ActiveEffect effect)
        {
            if (StatusEffectInfo.ExpirePolicy(effect.Kind) == StatusEffectExpirePolicy.OnConsume)
            {
                return effect.Amount > 0 ? effect.Amount.ToString() : string.Empty;
            }

            return effect.RemainingTurns > 0 ? effect.RemainingTurns.ToString() : string.Empty;
        }

        private static bool TryGetDefinition(StatusEffectKind kind, out KeywordDefinition def)
        {
            def = null;
            var catalog = CardKeywordCatalogProvider.Active;
            return catalog != null && catalog.TryGet(KeywordFor(kind), out def);
        }
    }
}

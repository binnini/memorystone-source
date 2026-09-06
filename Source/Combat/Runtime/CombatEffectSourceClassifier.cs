using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Classifies presentation source references emitted by combat runtime effects.
    /// Card source refs are intentionally CSV catalog ids only (A01, S01, ...).
    /// </summary>
    public static class CombatEffectSourceClassifier
    {
        /// <summary>
        /// 이 효과가 <b>플레이어 공격 카드</b>에서 나왔는가. 공격 카메라 흔들림
        /// (<c>CombatCameraController</c>)과 공격 효과음(<c>CombatAudioPresenter</c>)이 이걸로 갈린다.
        ///
        /// 2026-08-31 T3 이전에는 ID 15개(A00~A14)를 switch에 손으로 나열했다. 같은 정보가 이미
        /// <c>cards.csv</c>의 <c>type</c> 컬럼에 있었으므로 그 switch는 <b>저작의 사본</b>이었고,
        /// 사본은 원본과 어긋난다. 어긋나도 조용하다 — 컴파일도 되고 게임도 뜨고, 그 카드만
        /// 흔들림과 효과음이 빠진다. 증상이 "연출이 좀 밋밋하다"로만 나타나 원인까지 오래 걸린다.
        /// 이제 저작을 직접 읽으므로 사본이 없고, 어긋날 자리도 없다.
        /// </summary>
        /// <param name="catalog">
        /// 🔴 <b>호출부가 들고 있는 것을 넘긴다</b>(연출 쪽은 <c>CombatState.CardCatalog</c>).
        /// 코어(<c>Combat.Runtime</c>)가 카탈로그를 스스로 찾아 나서면 새 의존이나 전역 상태가
        /// 생긴다 — 이 분류기는 인자로만 받는다.
        /// </param>
        public static bool IsPlayerAttackCardSource(string sourceRef, CardCatalogDefinition catalog)
        {
            return HasAuthoredActionType(sourceRef, catalog, CardEffectType.Attack);
        }

        /// <summary>
        /// True for the presentation-only announce raised the moment a field object is installed
        /// (<see cref="CardEffectRefs.FieldPlacement"/>). It exists so the footprint can be shown and
        /// labelled 설치, but nothing has been hit, healed or afflicted yet — the tick that does that runs
        /// at the next overall-turn start and carries its own <c>field.*</c> ref.
        ///
        /// Channels that speak for an actual impact must stay silent for it: a hit sound or a camera shake
        /// on placement reads as "something was struck" when nothing was. The placement's own audio is the
        /// cast cue (<c>card.field.cast</c>), already raised when the card is played.
        /// </summary>
        public static bool IsFieldPlacementAnnounce(string sourceRef)
        {
            return string.Equals(sourceRef, CardEffectRefs.FieldPlacement, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Any presentation-only effect raised on the field itself rather than on a unit: the placement
        /// announce above, and the centre announce each tick raises so its footprint is legible. Both carry
        /// no amount — the real numbers ride on the per-unit effects that follow.
        ///
        /// Deliberately keyed on shape (field target, no amount) rather than on a list of field refs. The
        /// list version silently missed <c>field.lifesteal</c> when F04 was added, which put a monster hit
        /// sound and a camera shake on a tick that had hit nobody yet.
        /// </summary>
        public static bool IsFieldAreaAnnounce(EffectResultEvent effect)
        {
            return string.Equals(effect.TargetUnitId, "field", System.StringComparison.Ordinal)
                && effect.AppliedAmount <= 0;
        }

        /// <summary>
        /// Defend cards that nullify the monster action's damage outright instead of granting block
        /// (D02 보호구역 안에서 도발, D05 부적 방패). They report zero block, so presentation has to be told
        /// they did something: without this they read as a card that was played and did nothing at all.
        /// </summary>
        public static bool IsDamageImmunitySource(string sourceRef)
        {
            // 효과 종류 키 하나로 판정한다(트랙 ②, 2026-09-06). 예전엔 카드별 behaviorId 두 개를 나열했다 —
            // 카드가 늘면 목록도 늘어야 했고, 규칙층(TryPlayerDefend)이 이미 「면역이었는가」를 알고 있으므로 그쪽이 키를 찍는다.
            return string.Equals(sourceRef, CardEffectRefs.DefendDamageImmunity, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// 이 효과가 <b>정찰 카드</b>에서 나왔는가(<c>card.scout.resolve</c> 효과음).
        /// 공격과 같은 이유로 S00~S06 나열을 지우고 저작을 직접 읽는다
        /// (<see cref="IsPlayerAttackCardSource"/> 주석 참고).
        /// </summary>
        public static bool IsScoutCardSource(string sourceRef, CardCatalogDefinition catalog)
        {
            return HasAuthoredActionType(sourceRef, catalog, CardEffectType.Scout);
        }

        /// <summary>
        /// ⚠️ <b><c>ActionType</c>을 본다 — <c>GameplayType</c>이 아니다.</b> <c>GameplayType</c>은
        /// <c>ResolveGameplayType(behaviorId, …)</c>가 덮어쓸 수 있어 CSV <c>type</c> 컬럼과 1:1이
        /// 아니다. <c>ActionType</c>만 <c>type</c> 컬럼과 정확히 1:1이므로, "저작이 공격이라고 한
        /// 카드"의 정의가 여기서 흔들리지 않는다.
        ///
        /// 카탈로그가 없으면 <c>false</c>다. 카드가 아닌 출처(몬스터 패턴 <c>monster.pattern.*</c>,
        /// 함정, 필드 <c>field.*</c>)도 여기로 들어오는데 그것들은 카탈로그에 없으므로 자연히
        /// false가 된다 — 예전 switch의 default와 같은 답이다.
        /// </summary>
        private static bool HasAuthoredActionType(string sourceRef, CardCatalogDefinition catalog, CardEffectType authoredType)
        {
            if (catalog == null || string.IsNullOrEmpty(sourceRef))
            {
                return false;
            }

            var entries = catalog.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Id, sourceRef, System.StringComparison.Ordinal))
                {
                    return entries[i].ActionType == authoredType;
                }
            }

            return false;
        }
    }
}

using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 카탈로그 저작(<see cref="CardCatalogEntry"/>)을 카드 뷰가 읽는
    /// <see cref="CombatCardSnapshot"/>으로 옮긴다. 전투 상태가 없는 로비에서도 카드를 그릴 수 있는
    /// 이유가 이 어댑터다 — 보상 팝업(<c>CardRewardPopupView</c>)이 이미 같은 방식을 쓴다.
    /// </summary>
    public static class CodexCardSnapshotFactory
    {
        public static CombatCardSnapshot FromCatalogEntry(CardCatalogEntry entry, string catalogSourceId = "")
        {
            if (entry == null)
            {
                return default;
            }

            var isStatusCard = entry.ActionType == CardEffectType.Status;

            return new CombatCardSnapshot(
                entry.Id,
                ToCombatCardKind(entry.ActionType),
                entry.DisplayName,
                ResolveDescription(entry, catalogSourceId),
                entry.Amount,
                // 도감 카드는 손에 든 카드가 아니다. '지금 낼 수 있는가'는 전투 상태가 정하는 값이라
                // 도감에서는 물을 수 없고, 물으면 안 된다 — 카드가 회색으로 뜨는 것은 거짓말이다.
                isUsable: true,
                isDiscarded: false,
                status: string.Empty,
                cost: entry.Cost,
                range: entry.Range,
                pile: "Codex",
                catalogSourceId: entry.SourceTrace,
                effectRef: entry.EffectRef,
                phaseAvailability: entry.PhaseAvailability,
                playMode: entry.PlayMode,
                fieldObjectKind: entry.FieldObjectKind,
                durationTurns: entry.DurationTurns,
                areaRadius: entry.AreaRadius,
                choiceOptions: entry.ChoiceOptions,
                choiceOptionTexts: entry.ChoiceOptionTexts,
                illustrationId: entry.PresentationRef.IllustrationId,
                targetMode: entry.TargetMode,
                isStatusCard: isStatusCard);
        }

        /// <summary>
        /// 저작 설명의 <c>{Damage}</c>·<c>{Shape}</c>·<c>{HitCount}</c> 토큰을 저작값으로 채운다.
        /// 치환하지 않으면 카드에 중괄호가 글자 그대로 뜬다 — 전투는 스냅샷을 만들 때 이미 채우고
        /// 있고, 도감은 그 경로를 타지 않으므로 여기서 같은 함수를 부른다.
        /// </summary>
        public static string ResolveDescription(CardCatalogEntry entry, string catalogSourceId)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            var sourceId = string.IsNullOrWhiteSpace(catalogSourceId) ? entry.SourceTrace : catalogSourceId;
            return CombatState.ResolveAuthoredDescription(
                entry.ToCardDefinition(string.IsNullOrWhiteSpace(sourceId) ? "codex" : sourceId));
        }

        /// <summary>
        /// 저작의 효과 종류를 카드 얼굴이 읽는 종류로 옮긴다.
        /// <para>
        /// ⚠️<see cref="CardEffectType.Status"/>는 대응하는 <see cref="CombatCardKind"/>가 없어
        /// <see cref="CombatCardKind.Move"/>로 떨어진다. 그래서 <b>저주 판정은 종류가 아니라
        /// <c>isStatusCard</c>로</b> 해야 한다 — 종류만 보면 저주 카드에 "이동"이 붙는다.
        /// </para>
        /// </summary>
        public static CombatCardKind ToCombatCardKind(CardEffectType effectType)
        {
            switch (effectType)
            {
                case CardEffectType.Move: return CombatCardKind.Move;
                case CardEffectType.Attack: return CombatCardKind.Attack;
                case CardEffectType.Defend: return CombatCardKind.Defend;
                case CardEffectType.Scout: return CombatCardKind.Scout;
                case CardEffectType.Investigate: return CombatCardKind.Investigate;
                case CardEffectType.FieldObject: return CombatCardKind.FieldObject;
                case CardEffectType.Buff: return CombatCardKind.Buff;
                case CardEffectType.Utility: return CombatCardKind.Utility;
                default: return CombatCardKind.Move;
            }
        }
    }
}

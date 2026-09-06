using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>
    /// 연마 적용 지점(P4). 인스턴스의 <see cref="CardDefinition.UpgradeLevel"/>이 1 이상이면 카드 클래스의
    /// <see cref="CardBehavior.Upgrade"/>로 정의를 치환하고 표기(이름 <c>+</c> 접미 · <c>descriptionUpgraded</c> 문안)를 얹는다.
    /// 정의 시점 치환이라 필드 카드의 배치 시점 저작값에도 연마가 먹는다(DEC-2026-08-18-02의 계약 유지) —
    /// 덱 생성(<c>CombatState.CreateDeck</c>)·세이브 복원(<c>PlayerDeckSaveData</c>)·연마 실행(<c>CombatState.Refine</c>)이 부른다.
    /// </summary>
    public static class CardUpgrades
    {
        /// <summary>연마 카드의 카드명 접미(DEC-2026-08-18-02 표기 규칙).</summary>
        public const string NameSuffix = "+";

        public static CardDefinition Resolve(CardDefinition card)
        {
            if (card == null || card.UpgradeLevel < 1)
            {
                return card;
            }

            var upgraded = CardBehaviorRegistry.Resolve(card).Upgrade(card, card.UpgradeLevel);
            if (upgraded == null)
            {
                // 연마 불가 카드에 연마 단계가 실려 왔다(옛 세이브·픽스처) — 정의는 저작값 그대로 둔다.
                return card;
            }

            var name = upgraded.DisplayName ?? string.Empty;
            return upgraded.With(
                displayName: name.EndsWith(NameSuffix, System.StringComparison.Ordinal) ? name : name + NameSuffix,
                description: string.IsNullOrWhiteSpace(upgraded.DescriptionUpgraded) ? null : upgraded.DescriptionUpgraded);
        }
    }
}

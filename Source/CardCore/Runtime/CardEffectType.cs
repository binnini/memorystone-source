namespace SeoulPlayup.CardCore
{
    public enum CardEffectType
    {
        Move,
        Attack,
        Defend,
        Scout,
        Investigate,
        FieldObject,
        Buff,
        Utility,

        /// <summary>
        /// 상태 카드(C-17 / D-17): 전투 중 덱에 삽입되는 <b>사용 불가</b> 카드. 손패 한 칸을 먹는 것 자체가
        /// 비용이고, 종류마다 부가 효과(턴말 소멸 / 턴말 피해 / 손에 있는 동안 봉인)가 붙는다.
        ///
        /// ⚠️이 값은 <b>세 게이트 면 전부</b>에서 명시적으로 거부돼야 한다
        /// (<c>CanUseActionCard</c> / <c>IsCardUsable</c> / <c>GetCardStatus</c>) — 한 곳이라도 default로
        /// 새면 "사용 가능한 상태 카드"가 된다(계획 R-3). 베이크된 카탈로그에 int로 직렬화되므로 맨 뒤에 추가한다.
        /// </summary>
        Status
    }

    public enum CardGameplayType
    {
        Move,
        Attack,
        Defend,
        Scout,
        Field,
        Buff,
        Utility
    }

    public enum CardCostMode
    {
        Fixed,
        SpendAll,
        MaxKi,
        Free
    }

    public enum CardTargetMode
    {
        None,
        Self,
        Enemy,
        Tile,
        SelfOrEnemy,
        RandomReachable,
        OptionThenTarget,
        SelfArea
    }

    public enum CardScalingMode
    {
        Flat,
        MovedThisTurn,
        SpentKi,
        AttackCardsInHand,
        ObjectsInRevealArea,
        DistanceTable,
        // Appended: the value is serialized in baked catalogs, so new modes go at the end.
        ExiledCards
    }

    public enum CardUsePhase
    {
        Default,
        Movement,
        Action,
        BothIfApproved
    }

    public enum CardPlayMode
    {
        ManualTarget,
        Self,
        MovementDestination,
        AutoOnMove,
        Choice
    }

    public enum CardFieldObjectKind
    {
        None,
        FogReveal,
        FieldDamage,
        ConditionalHeal,
        MassImmobilize,
        // Appended: the value is serialized in baked catalogs, so new kinds go at the end.
        LifestealDamage
    }

    public enum CardCatalogStatus
    {
        MigrationSeed,
        Draft,
        Placeholder,
        Approved,
        Deprecated
    }
}

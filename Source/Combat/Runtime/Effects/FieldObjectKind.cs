namespace SeoulPlayup.Combat.Runtime
{
    public enum FieldObjectKind
    {
        FogReveal,
        FieldDamage,
        ConditionalHeal,
        MassImmobilize,
        // Appended: the value is serialized, so new kinds go at the end.
        LifestealDamage,

        /// <summary>
        /// 상태이상 지대(요괴 트랙 §4-3 · 두억시니). 밟고 있는 <b>플레이어에게만</b> 저작된 상태이상을
        /// 건다 — 통행을 막지 않으므로 몬스터의 길찾기·배치·도달성 판정을 아예 지나가지 않는다.
        /// (통행 불가 갈래를 폐기하고 이쪽으로 온 이유가 그것이다.)
        /// </summary>
        StatusZone
    }
}

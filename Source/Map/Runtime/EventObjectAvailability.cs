namespace SeoulPlayup.Map.Runtime
{
    /// <summary>
    /// 이벤트 오브젝트를 출하에서 쓸지 말지의 <b>단일 스위치</b>.
    /// <see cref="ServiceObjectAvailability"/>(잡화점·캠핑카·공작소)와 같은 문법이고, 이쪽은
    /// 서비스가 아닌 1회 소비 이벤트 오브젝트를 맡는다.
    ///
    /// <para>
    /// 🔴 끄는 자리는 <b>두 곳뿐</b>이다: ①시각(맵 오브젝트 스폰 — <c>MapObjectVisualRegistry</c>)
    /// ②트리거(밟았을 때 — <c>MapCombatController.Rewards</c>). 둘 중 하나만 끄면
    /// 「보이는데 안 되는 물건」이나 「안 보이는데 밟히는 칸」이 된다.
    /// </para>
    /// </summary>
    public static class EventObjectAvailability
    {
        /// <summary>
        /// 저주받은 인형뽑기(유물 1 + 저주 카드 1의 묶음 계약)를 세우고 밟을 수 있게 할지.
        /// 2026-09-01 사용자 확정으로 <b>꺼져 있다</b>.
        ///
        /// <para>🔑 지금 출하 맵 어디에도 이 오브젝트는 저작돼 있지 않다(전 맵 실측 0건). 그래도
        /// 스위치를 두는 이유는, 저작으로 다시 들어오는 순간 조용히 되살아나는 것을 막기 위해서다 —
        /// 「데이터에 없으니 괜찮다」는 다음 맵 저작 한 줄이면 깨진다.</para>
        ///
        /// <para>규칙·카탈로그·도감 엔트리는 그대로 두었다. 되살리려면 이 값을 뒤집고 맵에 엔트리를
        /// 저작하면 된다(<see cref="ServiceObjectAvailability"/>의 공작소와 같은 관계).</para>
        /// </summary>
        public static readonly bool CursedGachaMachineEnabled = false;
    }
}

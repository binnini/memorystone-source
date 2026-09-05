namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// <c>status_effects.csv</c>의 동작 컬럼 값 공간. **문자열이 아니라 enum인 것이 핵심 계약이다**:
    /// 저작이 만들어 낼 수 있는 값을 소비 코드가 있는 값으로만 제한한다(문서 트랙 교훈 —
    /// "규칙을 데이터로 뺄 땐 '저작으로 규칙을 깰 수 있는가'를 따질 것"). 파서가 모르는 값을 거부하므로
    /// 오타나 미구현 값이 조용히 무시되는 경로가 없다.
    ///
    /// 값을 추가할 때는 <b>소비 코드를 먼저 만들고</b> 여기 값을 더한다. 순서를 뒤집으면
    /// "저작은 됐는데 아무 일도 안 일어나는" 죽은 컬럼이 다시 생긴다(그게 이 페이즈가 고친 상태다).
    /// </summary>
    public enum StatusEffectValueMode
    {
        /// <summary>Amount를 읽지 않는다. 존재 여부만으로 동작하는 상태(속박·기절).</summary>
        None,

        /// <summary>턴 시작 틱마다 Amount만큼 피해(중독).</summary>
        DamagePerTurn,

        /// <summary>이동 범위 +Amount(민첩).</summary>
        MoveRangeBonus,

        /// <summary>이동 범위 −Amount(둔화).</summary>
        MoveRangePenalty,

        /// <summary>획득 방어도 −Amount(파열).</summary>
        BlockGainPenalty,

        /// <summary>
        /// 반사 on/off 게이트(반사). ⚠️Amount는 <b>퍼센트가 아니다</b> — 런타임은 `> 0`인지만 보고
        /// 반사량은 고정 50%(`incoming / 2`)다. 옛 이름 `ReflectPercent`는 저작자에게 값이 배율인 것처럼
        /// 보이게 했고 실제로는 50을 100으로 바꿔도 아무 일도 일어나지 않는다.
        /// </summary>
        ReflectGate,

        /// <summary>가하는 피해 +Amount%(강화).</summary>
        DamageDealtBonusPercent,

        /// <summary>시야 반경 −Amount(실명).</summary>
        VisionRangePenalty,

        /// <summary>
        /// 가하는 피해 −Amount%(쇠약). <see cref="DamageDealtBonusPercent"/>와 같은 지점에서 합산되며
        /// 부호만 반대다 — 부호를 축 이름이 들고 있어서 저작이 뒤집을 수 없다.
        /// </summary>
        DamageDealtPenaltyPercent,

        /// <summary>
        /// 받는 <b>타격 1회당</b> 피해 +Amount(허점). ⚠️퍼센트가 아니라 고정치다(D-10) — 배율로 바꾸면
        /// 다타 카드와의 시너지라는 설계 의도가 사라진다. 배율 항(강화·쇠약)이 곱해진 <b>뒤</b>에 더해진다.
        /// </summary>
        IncomingDamageBonusFlat,

        /// <summary>손패에서 사용 불가로 잠기는 카드 수(봉인). 피해가 아니라 카드 사용 검사에서 읽힌다.</summary>
        SealedCardCount,

        /// <summary>
        /// 시야 반경 +Amount(횃불). <see cref="VisionRangePenalty"/>와 같은 지점에서 부호만 반대로 합산된다.
        /// ⚠️축은 "얼마나 밝은가"만 말한다 — 매 턴 줄어드는 것은 횃불이라는 <b>종류</b>의 수명 규칙이지
        /// 이 축의 성질이 아니다(그래서 감쇠는 kind로 분기한다).
        /// </summary>
        VisionRangeBonus,

        /// <summary>
        /// 해로운 상태이상을 무효화하는 남은 충전 수(수호, T2 페이즈 C). 카운터 스택 문법 —
        /// Amount는 턴이 아니라 <b>소비</b>로 줄어들며(짝 규칙은 <see cref="StatusEffectExpirePolicy.OnConsume"/>),
        /// 읽기 지점은 상태이상 부여 관문(<c>CombatState.AddDurationStatusEffectCore</c>) 하나다.
        /// 직렬화 안정을 위해 맨 뒤에 추가한다.
        /// </summary>
        StatusNegationCharges,

        /// <summary>
        /// 가하는 피해 +Amount(<b>고정</b> · 힘). <see cref="DamageDealtBonusPercent"/>(강화)와 이름이
        /// 비슷하나 축이 다르다 — 저쪽은 배율(1점 = +1%)이고 이쪽은 그냥 더한다. 배율 항이 곱해진
        /// <b>뒤에</b> 더해지는 것도 <see cref="IncomingDamageBonusFlat"/>(허점)과 같은 문법이다.
        ///
        /// <para>🔑 2026-09-04에 생겼다. 종전에는 약오름이 이 축이 없어 전용 카운터를 피해 산식에
        /// 직접 더했고, 그래서 「스택은 있는데 상태이상 목록엔 없는」 값이 하나 떠 있었다 —
        /// 담력 시험이 같은 카운터를 빌려 쓰면서 배지가 자기를 「약오름」이라 부르는 사고까지 났다.
        /// 이제 둘 다 <b>힘</b>을 부여하고, 피해는 이 축 하나로 들어온다.</para>
        ///
        /// <para>⚠️ 플레이어의 힘은 이 축을 <b>타지 않는다</b> — 런 영구 스탯
        /// (<c>PlayerInventoryState.MightStacks</c>)이고 상태이상 등록부에 들어가지 않는 투영 사본이라
        /// (<c>PlayerStatusEffectQuery.ProjectRunPermanentEffects</c>) 여기서 두 번 세지 않는다.</para>
        /// </summary>
        DamageDealtBonusFlat
    }

    /// <summary>
    /// Amount(또는 존재 여부)를 읽는 시점. **서술 전용 컬럼이다** — 실제 읽기 지점은
    /// <see cref="StatusEffectValueMode"/> 배선이 결정하므로 여기에 두 번째 디스패처를 만들지 않는다.
    /// 대신 `StatusEffectCatalogCsv`가 (valueMode → timing) 짝을 검증해서, 서술이 실제와 갈라지면
    /// 파싱이 실패한다. 디스패처를 만들었다면 그것이야말로 "저작으로 규칙을 깨는" 표면이 됐을 것이다.
    /// </summary>
    public enum StatusEffectTiming
    {
        TurnStart,
        BeforeMoveRangeCalc,
        OnActionCheck,
        OnBlockGain,
        OnIncomingDamage,
        OnOutgoingDamage,
        BeforeVisionCalc,

        /// <summary>상태이상 부여 관문에서 읽힌다(수호의 충전 소비). 직렬화 안정을 위해 맨 뒤에 추가한다.</summary>
        OnStatusApply
    }

    /// <summary>
    /// 같은 종류를 다시 걸었을 때의 병합 규칙. 두 값 모두 <b>인스턴스는 하나로 병합</b>하고
    /// 지속시간은 더 긴 쪽을 취한다. 차이는 Amount뿐이다.
    /// </summary>
    public enum StatusEffectStackPolicy
    {
        /// <summary>Amount는 더 큰 쪽(둔화 1이 두 번 겹쳐도 1).</summary>
        RefreshDuration,

        /// <summary>Amount를 합산(중독 3 + 3 = 6).</summary>
        Add
    }

    /// <summary>
    /// 만료 시점. 두 값 모두 턴 시작 경계에서 지속시간이 깎이며, 차이는 <b>적용 턴의 유예</b>다.
    /// </summary>
    public enum StatusEffectExpirePolicy
    {
        /// <summary>
        /// 적용된 턴의 경계는 건너뛰고 그 다음 경계부터 깎인다 — "N턴 동안"에 적용 턴을 포함하지 않는다.
        /// 존재만으로 동작하는 상태(속박·기절)와 값 상태(둔화·실명·민첩·반사·강화·파열) 전부 여기.
        /// </summary>
        TurnEnd,

        /// <summary>
        /// 턴 시작에 틱을 먼저 돌리고 나서 깎는다(중독). 틱형이라 적용 턴 유예를 받지 않는다 —
        /// 유예를 주면 "이번 턴부터 아프다"가 성립하지 않는다.
        /// </summary>
        TurnStartAfterTick,

        /// <summary>
        /// 턴으로 만료되지 않는다(수호, T2 페이즈 C — 카운터 스택 문법). 턴 경계 틱을 통째로 건너뛰고,
        /// 소비 지점이 Amount를 깎아 0이 되는 순간 제거한다. 직렬화 안정을 위해 맨 뒤에 추가한다.
        /// </summary>
        OnConsume
    }
}

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 페이즈 전환을 판정하는 지표. 지표가 무엇이든 런타임은 단 하나의 정규화된
    /// <b>progress</b> 값(0에서 시작해 단조 증가)으로 환산해 다루므로,
    /// <see cref="BossPhaseDefinition.ProgressThreshold"/>는 항상 오름차순이고 1페이즈는 0이다.
    /// 새 지표를 추가하려면 이 enum 끝에 append하고 CombatState의 progress 환산에 분기를 더한다.
    /// </summary>
    public enum BossPhaseMetricKind
    {
        /// <summary>
        /// 보스 기믹이 누적한 흡수 스택. progress = 누적 스택 수.
        /// 저작 threshold를 그대로 progress로 쓴다(불가살: 0/100/200).
        /// </summary>
        AbsorbedStacks = 0,

        /// <summary>
        /// 보스의 현재 체력 비율. 저작 threshold는 "현재 체력이 최대 체력의 이 비율(%) <b>이하</b>로
        /// 떨어지면 진입"이라는 뜻이라 자연히 내림차순(100/50/25)으로 적는다. 파서가
        /// progress = 100 - threshold 로 정규화하므로 내부 표현은 다른 지표와 동일하게 오름차순이다.
        /// </summary>
        HpRatioBelow = 1,

        /// <summary>
        /// 전투 전체 턴 수. progress = <c>OverallTurnNumber - 1</c>(1턴째가 0).
        /// 저작 threshold는 "전투 시작 후 경과 턴 수"이며 그대로 progress로 쓴다.
        /// </summary>
        TurnCount = 2,
    }
}

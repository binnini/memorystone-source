namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 보스 기믹 의도 배지(2026-09-03 ⑥)의 글리프·제목·설명 사전. 기믹 id(<c>boss_profiles.csv</c>
    /// <c>mechanicId</c>)가 키다 — 렌더러(<see cref="CombatActorMarkerPresenter"/>)와 툴팁
    /// (<see cref="MonsterBadgeTooltipContent"/>)이 같은 사전을 읽어야 글자와 설명이 갈라지지 않는다.
    /// 미등록 id는 일반 문구로 떨어진다(새 기믹이 배지 없이 조용히 새지 않게 하는 폴백).
    /// </summary>
    internal static class BossGimmickBadgeText
    {
        public static string Glyph(string mechanicId)
        {
            switch (mechanicId)
            {
                case "iron-scrap":
                    return "살";
                case "trap-volley":
                    return "함";
                case "annihilation":
                    return "멸";
                case "scrap-chain":
                    return "사";
                default:
                    return "기";
            }
        }

        public static string Title(string mechanicId)
        {
            switch (mechanicId)
            {
                case "iron-scrap":
                    return "철조각 살포";
                case "trap-volley":
                    return "함정 배치";
                case "annihilation":
                    return "전멸기";
                case "scrap-chain":
                    return "철조각 사슬";
                default:
                    return "보스 기믹";
            }
        }

        public static string Description(string mechanicId)
        {
            switch (mechanicId)
            {
                case "iron-scrap":
                    return "이번 턴에 공격 대신 철조각을 살포합니다. 다 자란 철조각은 흡수되어 보스를 성장시킵니다 — 그 전에 부수세요.";
                case "trap-volley":
                    return "이번 턴에 공격 대신 함정을 심습니다. 심은 함정은 정찰로만 찾을 수 있습니다.";
                case "annihilation":
                    return "이번 턴에 공격 대신 전멸기를 개시합니다. 안전지대 후보로 피하세요 — 정찰이 진위를 알려줍니다.";
                case "scrap-chain":
                    return "살아있는 철조각으로 전격 사슬을 준비합니다. 철조각을 부수면 그 가닥이 사라집니다.";
                default:
                    return "이번 턴에 공격 대신 고유 기믹을 사용합니다.";
            }
        }
    }
}

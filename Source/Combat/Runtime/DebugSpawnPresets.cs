using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 실플레이 판정용 소환 프리셋(2026-08-31). 「나란히 놓고 비교」가 판정의 전부인 묶음들 —
    /// 터렛 3레벨의 실루엣 차, 프롭 2종의 가독, 요괴 6종의 합동 인상.
    ///
    /// <para>
    /// 표현 계층이 아니라 여기 사는 이유는 <b>시험이 물 수 있게</b>다: id는
    /// <c>monster_catalog.csv</c>의 정본을 가리키므로 카탈로그가 바뀌면 조용히 아무것도 안 서는
    /// 버튼이 된다 — 그 드리프트를 EditMode 게이트가 잡는다.
    /// </para>
    /// </summary>
    public static class DebugSpawnPresets
    {
        /// <summary>자동 공격 터렛 LV1·2·3 — 레벨 차가 실루엣으로 읽히는지 판정용.</summary>
        public static readonly IReadOnlyList<string> Turrets = new[] { "M902", "M903", "M904" };

        /// <summary>철조각·공사장 고깔 — 이름대로 읽히는지 판정용.</summary>
        public static readonly IReadOnlyList<string> Props = new[] { "M901", "M905" };

        /// <summary>거구귀·그슨새·두두리·두억시니·야광귀·어둑시니 — 여섯 합동 판정용.</summary>
        public static readonly IReadOnlyList<string> Yogoe =
            new[] { "M012", "M013", "M008", "M010", "M014", "M009" };

        /// <summary>게이트가 전수 확인할 수 있게 모든 프리셋을 이름과 함께 돌려준다.</summary>
        public static IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> All
        {
            get
            {
                yield return new KeyValuePair<string, IReadOnlyList<string>>("Turrets", Turrets);
                yield return new KeyValuePair<string, IReadOnlyList<string>>("Props", Props);
                yield return new KeyValuePair<string, IReadOnlyList<string>>("Yogoe", Yogoe);
            }
        }
    }
}

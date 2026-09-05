using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 몬스터 스폰 역할(<c>MonsterConfig.SpawnRole</c> / 맵 스폰 ref의 role) 어휘와 판별 술어의 정본.
    /// 역할 문자열이 규칙·표현 양쪽에 흩어져 하드코딩되어 있으면 새 역할을 추가할 때마다 누락 지점이
    /// 생기므로(보스 기물이 몬스터 수 세기·보상 추첨·예고 UI로 새는 사고), 비교는 전부 여기를 지나간다.
    ///
    /// 역할은 저작 데이터에서 오는 자유 문자열이라 대소문자 무시로 비교한다.
    /// </summary>
    public static class MonsterSpawnRoles
    {
        /// <summary>평범한 조우. 저작에서 role을 비우면 이것과 동등하게 취급된다.</summary>
        public const string NormalEnemy = "normal-enemy";

        /// <summary>정예. 보상 등급이 올라간다.</summary>
        public const string Elite = "elite";

        /// <summary>보스. 목표(기억결) 각성 게이트와 보스 페이즈/연출이 여기에 걸린다.</summary>
        public const string Boss = "boss";

        /// <summary>
        /// 보스 소속 기물(철조각·토템류). 몬스터로 호스팅되어 HP·피격·사망·세이브를 공짜로 얻지만
        /// <b>몬스터로 세어지지 않는다</b>: 목표 각성 판정, 보상 추첨, 인텐트 예고, 행동 계획,
        /// 인트로 featured 선정에서 모두 제외된다.
        /// </summary>
        public const string BossProp = "boss-prop";

        /// <summary>
        /// 함정이 부른 몬스터(C-7 터렛 · C-10 사이렌 / D-12). <b>보스 기물과 달리 정식 위협으로 센다</b> —
        /// 승리 판정·보상·의도 예고·행동 계획에서 아무것도 제외되지 않는다. 밟아서 부른 적을 처리하지
        /// 않아도 이기는 함정은 함정이 아니기 때문이다. 역할을 따로 두는 것은 사후 추적(디버그·랩)용이며,
        /// 규칙 분기는 이 값에 걸지 않는다.
        /// </summary>
        public const string TrapSpawn = "trap-spawn";

        /// <summary>
        /// 플레이어가 설치한 기물(결계 구슬, T4-2). 보스 기물과 같은 호스팅(HP·피격·점유·세이브 공짜)
        /// 이며 규칙 제외도 동일하다(승리 판정·보상·예고·행동 계획·처치 유물 훅 제외 — <see cref="IsProp"/>).
        /// 보스 기믹(흡수·사슬·볼리 계수)에는 절대 걸리지 않는다 — 그쪽 술어는 <see cref="IsBossProp"/>를 유지한다.
        /// </summary>
        public const string PlayerProp = "player-prop";

        /// <summary>
        /// 몬스터가 부른 기물(두두리의 나무 말뚝). 플레이어 기물과 <b>같은 규칙 제외</b>(승리 판정·보상·
        /// 예고·행동 계획 밖)이며 보스 기믹에도 걸리지 않는다 — 다른 점은 세운 주체뿐이다.
        ///
        /// <para>🔴 이 역할이 없으면 「막는 물건」이 「죽여야 하는 적」이 된다: 소환물의 기본 역할은
        /// 정식 위협(<c>trap-spawn</c>)이라, 두두리를 잡고도 말뚝을 전부 부수기 전에는 전투가 끝나지 않는다.</para>
        /// </summary>
        public const string SummonProp = "summon-prop";

        public static bool IsSummonProp(string role) => Matches(role, SummonProp);

        public static bool IsNormalEnemy(string role) => string.IsNullOrWhiteSpace(role) || Matches(role, NormalEnemy);

        public static bool IsTrapSpawn(string role) => Matches(role, TrapSpawn);

        public static bool IsElite(string role) => Matches(role, Elite);

        public static bool IsBoss(string role) => Matches(role, Boss);

        public static bool IsBossProp(string role) => Matches(role, BossProp);

        public static bool IsPlayerProp(string role) => Matches(role, PlayerProp);

        /// <summary>몬스터로 세지 않는 기물 전부(보스 기물 + 플레이어 기물 + 소환 기물).</summary>
        public static bool IsProp(string role) => IsBossProp(role) || IsPlayerProp(role) || IsSummonProp(role);

        private static bool Matches(string role, string expected) =>
            string.Equals(role, expected, StringComparison.OrdinalIgnoreCase);
    }
}

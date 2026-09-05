using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// <c>behaviorProfileRef</c>(monster_catalog.csv)의 등록표 — 요괴 트랙 §4-6 ①.
    ///
    /// <para>🔴 이 컬럼은 오랫동안 <b>죽은 컬럼</b>이었다: 형식 검사(<c>^B[0-9]{3}$</c>)·저장·도감 표시까지
    /// 되어 있는데 값을 읽어 행동을 고르는 코드가 없었고, 전 몬스터가 B001을 공유했다. 2026-08-30에 여기가
    /// 그 구멍을 이었고, 2026-09-05(DEC-2026-09-05-03)부터는 트리 인스턴스를 만드는 팩토리가 아니라
    /// <b>계획기의 의도 선택 분기</b>(<c>MonsterAiPlanner.SelectMovementIntent</c>)가 이 값을 직접 읽는다.
    /// 프로파일이 늘면 여기 상수와 그 분기에 함께 는다 — 형식(트리 모양)이 아니라 코드 갈래가 성격이다.</para>
    /// </summary>
    internal static class MonsterBehaviorProfileRegistry
    {
        /// <summary>기본 프로파일 — 감지 안이면 추격·사거리 안이면 공격.</summary>
        public const string DefaultProfileRef = "B001";

        /// <summary>매복형(어둑시니). 거리 밖이면 제자리에서 기다린다.</summary>
        public const string AmbushProfileRef = "B002";

        /// <summary>
        /// 매복이 풀리는 거리. 어둑시니의 카탈로그 감지 8·이동 3 기준으로 절반쯤에서 움직이기 시작한다 —
        /// 판 반대편에서부터 걸어오면 "숨어서 기다린다"가 성립하지 않는다.
        /// ⚠️ 실제 감지는 카탈로그 <c>detectionRange</c>가 아니라 씬 <c>enemyChaseRange</c>(6)다 — 2026-09-05 S3 조사, 99-open #12.
        /// </summary>
        public const int AmbushRange = 4;

        private static readonly string[] Registered = { DefaultProfileRef, AmbushProfileRef };

        /// <summary>등록된 프로파일인가. 저작 검증이 아니라 진단용이다(미등록은 기본으로 내려앉는다).</summary>
        public static bool IsRegistered(string profileRef) =>
            !string.IsNullOrWhiteSpace(profileRef) && Array.IndexOf(Registered, profileRef.Trim()) >= 0;

        /// <summary>매복 분기를 타는 프로파일인가. 미등록·빈 값은 기본(false).</summary>
        public static bool IsAmbush(string profileRef) =>
            !string.IsNullOrWhiteSpace(profileRef)
            && string.Equals(profileRef.Trim(), AmbushProfileRef, StringComparison.Ordinal);
    }
}

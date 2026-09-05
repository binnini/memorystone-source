using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 하나의 고유 기믹. 몬스터 행동 결의 창(<c>ResolveBossMechanicsStep</c>) 안에서 매 몬스터
    /// 페이즈에 한 번 호출되며, 페이즈 지표를 올리는 것 외의 규칙 변경은 전부 여기서 일어난다.
    ///
    /// 계약:
    /// <list type="bullet">
    /// <item>순수 C#. <c>UnityEngine</c>을 참조하지 않는다.</item>
    /// <item>보스 id를 하드코딩하지 않는다 — 대상 보스는 <see cref="BossMechanicContext"/>로 주어진다.</item>
    /// <item>수치는 <see cref="BossProfileDefinition.MechanicParams"/>에서 읽는다(코드 상수 금지).
    ///       필요한 키는 <see cref="RequiredParamKeys"/>로 선언하면 파서가 누락 저작을 거부한다.</item>
    /// <item>페이즈 전환 판정은 하지 않는다 — 지표만 올리고 전환은 호출부가 단일 지점에서 처리한다.</item>
    /// <item>새 RNG를 만들지 않는다(고정 시드 재현성). 무작위가 필요하면 컨텍스트가 제공하는 것을 쓴다.</item>
    /// </list>
    ///
    /// 새 기믹 추가 = 이 인터페이스 구현체 1개 + <see cref="BossMechanicRegistry"/> 등록 1줄.
    /// </summary>
    internal interface IBossMechanic
    {
        /// <summary>CSV <c>mechanicId</c>와 일치하는 디스패치 키.</summary>
        string MechanicId { get; }

        /// <summary>
        /// 이 기믹이 반드시 저작되어 있어야 하는 <c>mechanicParams</c> 키들. 파서가 누락을 에러로 거부하므로
        /// 기믹 코드는 기본값 폴백으로 조용히 다른 동작을 하지 않는다.
        /// </summary>
        IReadOnlyList<string> RequiredParamKeys { get; }

        /// <summary>
        /// 키 존재만으로는 잡히지 않는 저작 오류를 파싱 시점에 거부한다(값의 형식, 페이즈 수와의 정합 등).
        /// <see cref="RequiredParamKeys"/>가 "있는가"라면 이쪽은 "말이 되는가"다.
        ///
        /// 페이즈 행이 전부 읽힌 <b>뒤에</b> 호출되므로 <paramref name="phaseCount"/>를 쓸 수 있다 —
        /// 페이즈별 리스트 저작(예: 살포 개수 <c>5|6|7</c>)의 길이 검증이 여기 있어야 하는 이유다.
        /// 위반은 <see cref="System.ArgumentException"/>으로 던진다(다른 파서 검증과 같은 계약).
        /// </summary>
        void ValidateParams(IReadOnlyDictionary<string, string> mechanicParams, int phaseCount);

        void Resolve(BossMechanicContext context);

        /// <summary>
        /// 이번 몬스터 페이즈에 이 기믹이 보스의 <b>일반 공격을 대체</b>하는가. true면 보스는 이 턴에
        /// 공격하지 않는다(예고도 서지 않는다).
        ///
        /// <see cref="Resolve"/>가 아니라 별도 질의인 이유: 공격 예고는 플레이어 이동이 끝난 시점에
        /// 이미 커밋되는데 기믹 결의는 그보다 <b>뒤</b>에 돈다. 결의에서 플래그를 세우면 "예고는 떴는데
        /// 공격이 안 나가는" 어긋남이 생기므로, 예고와 결의가 <b>같은 술어</b>를 읽어야 한다.
        /// 따라서 이 메서드는 상태를 바꾸지 않아야 하며(순수 질의), 같은 턴 안에서 여러 번 불릴 수 있다.
        /// </summary>
        bool SuppressesMonsterAttackThisTurn(BossMechanicContext context);
    }

    /// <summary>
    /// 아레나가 <b>봉인되는 순간</b>(조우) 한 번 불리는 선택적 훅(2026-09-05 후속 #1). 조우 턴의 첫 결의는
    /// 쿨다운 0이라 살포가 예고 없이 즉시 떨어졌다 — 기믹이 이 훅에서 첫 살포 칸을 미리 뽑아 두면 조우 턴의
    /// 플레이어 행동 페이즈에 이미 붉은 해치가 서 있고, 살포는 그 저장본을 쓴다(예고=배치). 결의 순서와 무관하게
    /// 「봉인 → 플레이어 시작 좌표 확정 → 훅」이라 예고 칸이 플레이어 자리를 피한다.
    /// </summary>
    internal interface IBossArenaSealedMechanic
    {
        void OnArenaSealed(BossMechanicContext context);
    }
}

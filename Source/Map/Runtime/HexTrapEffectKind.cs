using System;

namespace SeoulPlayup.Map.Runtime
{
    [Serializable]
    public enum HexTrapEffectKind
    {
        Damage,

        /// <summary>
        /// DEPRECATED (D-1, docs/design/status-trap-expansion.md): 화상은 중독과 체감이 겹쳐 부결됐고,
        /// 대응하는 <c>StatusEffectKind</c>가 없어 밟는 순간 크래시하던 유령 값이다. 저작 표면(프리셋
        /// 카탈로그·툴팁)에서는 제거했지만 <b>멤버 자체는 지우지 않는다</b> — 이 enum은 맵 소스 에셋과
        /// 세이브에 int로 직렬화되므로 삭제하면 뒤의 값이 전부 한 칸씩 밀린다. 신규 저작 금지.
        /// (인스펙터 드롭다운은 <c>[Obsolete]</c>를 무시하므로 저작 차단은 감사 테스트
        /// <c>ShippingMapTrapAuditTests</c>가 맡는다.)
        /// </summary>
        Burn,

        Poison,
        Stun,
        Slow,

        /// <summary>시야 감소. C-6에서 <c>StatusEffectKind.Blind</c>(실명)로 해소된다.</summary>
        VisionDown,

        /// <summary>
        /// 텔레포트(C-8 / D-3). <c>Amount</c>는 지속 턴이 아니라 <b>이동 반경 R</b>이고
        /// <c>DurationTurns</c>는 쓰지 않는다. 착지 칸에서 다른 함정은 연쇄 발동하지만
        /// 텔레포트→텔레포트만 차단된다. 맨 뒤 append(직렬화 값 안정).
        /// </summary>
        Teleport,

        /// <summary>
        /// 몬스터 스폰(C-7 터렛 + C-10 사이렌 / D-12). 두 후보가 <b>같은 효과 하나를 공유</b>한다 —
        /// 차이는 저작뿐이다: 터렛은 이동 0 몬스터를, 사이렌은 일반 몬스터를 부른다.
        /// <c>Amount</c>는 지속 턴이 아니라 <b>스폰 수</b>이고, 무엇을 부를지는
        /// <c>HexTrapEffectData.MonsterDefinitionId</c>가 정한다(<c>DurationTurns</c>는 쓰지 않는다).
        /// 맨 뒤 append(직렬화 값 안정).
        /// </summary>
        SpawnMonsters,

        /// <summary>
        /// 상태 카드 삽입(C-17 / D-17). <c>Amount</c>는 <b>삽입 장수</b>, 무엇을 넣을지는
        /// <c>HexTrapEffectData.StatusCardId</c>가 정한다(<c>DurationTurns</c>는 쓰지 않는다).
        /// 맨 뒤 append(직렬화 값 안정).
        /// </summary>
        InjectStatusCard,

        /// <summary>
        /// 쇠약 가스(T1, 2026-08-06). <c>StatusEffectKind.Weaken</c>으로 해소 — 가하는 피해 −Amount%.
        /// 맨 뒤 append(직렬화 값 안정).
        /// </summary>
        Weaken,

        /// <summary>
        /// 금제 부적(T1, 2026-08-06). <c>StatusEffectKind.Disarm</c>으로 해소 — 공격 카드 봉인.
        /// Disarm은 하드 CC 면역창(속박·기절)의 비대상이라 반복 부여가 막히지 않는다 — 저작은
        /// oneShot + 짧은 지속(1턴)을 기본으로 한다(사용자 결정). 맨 뒤 append(직렬화 값 안정).
        /// </summary>
        Disarm
    }
}

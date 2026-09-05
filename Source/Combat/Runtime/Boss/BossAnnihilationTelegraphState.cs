using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 진행 중인 전멸기 예고 하나의 공개 투영(§13.5). 표현 계층이 "이 칸은 위험"만으로는 만들 수 없는
    /// 두 가지 — <b>언제</b>(<see cref="TurnsRemaining"/>)와 <b>얼마나</b>(<see cref="Damage"/>) — 를
    /// 함께 실어 준다. 예고 오버레이가 평범한 몬스터 공격 예고와 같은 위험 레이어를 공유하므로
    /// (같은 붉은 해치), 둘을 가르는 경고 아이콘과 호버 툴팁이 이 정보를 소비한다.
    ///
    /// 규칙 상태를 복제하지 않는다: 매 조회마다 페이즈 트랙과 보스 프로필에서 유도한다.
    /// </summary>
    public readonly struct BossAnnihilationTelegraphState
    {
        public BossAnnihilationTelegraphState(
            string bossUnitId,
            string bossDisplayName,
            IReadOnlyList<HexCoord> cells,
            int turnsRemaining,
            int damage)
        {
            BossUnitId = bossUnitId ?? string.Empty;
            BossDisplayName = bossDisplayName ?? string.Empty;
            Cells = cells ?? Array.Empty<HexCoord>();
            TurnsRemaining = Math.Max(0, turnsRemaining);
            Damage = Math.Max(0, damage);
        }

        public string BossUnitId { get; }

        /// <summary>툴팁 제목에 쓰는 보스 표시 이름(비어 있을 수 있다).</summary>
        public string BossDisplayName { get; }

        /// <summary>이번 폭발이 덮을 칸. 예고 시점에 확정되어 그대로 집행된다(예고=명중).</summary>
        public IReadOnlyList<HexCoord> Cells { get; }

        /// <summary>폭발까지 남은 몬스터 행동 수. 1이면 다음 몬스터 행동에 터진다.</summary>
        public int TurnsRemaining { get; }

        /// <summary>예고된 칸에 서 있을 때 들어올 저작 피해(방어·경감 적용 전).</summary>
        public int Damage { get; }
    }
}

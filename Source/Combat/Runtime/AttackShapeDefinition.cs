using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 형상이 인접 6칸(공용 링)을 어떻게 다루는지. 저작 컬럼 <c>adjacency</c>의 어휘이며,
    /// 예전 bool <c>includeAdjacentRing</c>을 대체한다(요괴 5종 트랙 §2-2).
    /// </summary>
    public enum AttackShapeAdjacency
    {
        /// <summary>인접 6칸이 자동으로 깔린다 — 기존 <c>true</c>.</summary>
        Full = 0,

        /// <summary>인접 6칸이 없고 거리 1 저작도 <b>거부</b>한다 — 기존 <c>false</c>.
        /// "붙으면 안전" 계약을 파서가 보증하는 값이다.</summary>
        None = 1,

        /// <summary>인접 6칸이 자동으로 깔리지는 않지만 거리 1 오프셋을 <b>직접 저작</b>할 수 있다.
        /// 인접을 원하는 만큼만 채우는 형상이 여기 온다.</summary>
        Open = 2,

        /// <summary>몸 둘레(2026-09-04 리워크 §3-B) — 셀 집합을 저작하지 않고 <b>몬스터 footprint의
        /// 이웃 − 점유 칸</b>으로 실행 시점에 편다(tri = 9칸 · single = 인접 6칸 폴백 · 원판 r1 = 12칸).
        /// 원점·조준 방향 개념이 없다(회전 없음 — MonsterFootprints §13.4 규약). 2026-09-05부터 offsets를
        /// <b>더할 수</b> 있다 — 둘레 위에 원점(조준 방향 앞 몸통 칸) 기준 회전 오프셋을 합집합으로 얹는다
        /// (삼각 할퀴기 = 둘레 9 + 앞쪽 부채). 둘레 자체는 여전히 footprint가 정본이다.</summary>
        Body = 3,
        /// <summary>둘레의 둘레(2026-09-05 결정 8 · 몸 크기 전용 패턴) — footprint에서 거리 2 껍질을 실행 시점에
        /// 편다(tri = 15칸 · single = 거리 2 링 12칸 · 원판 r1 = 18칸). 몸에 붙은 둘레 칸과 거리 3 밖이 안전 —
        /// 「붙어라」 계약을 footprint가 보증한다. 원점·조준 방향 없음. offsets는 비워야 한다.</summary>
        BodyShell = 4,
    }

    public readonly struct AttackShapeDefinition
    {
        public AttackShapeDefinition(string id, HexCoord[] offsets, AttackShapeAdjacency adjacency = AttackShapeAdjacency.Full)
        {
            Id = id;
            Offsets = offsets;
            Adjacency = adjacency;
        }

        public string Id { get; }
        // Additional relative offsets beyond the universal adjacent 1-hex ring.
        // Offsets are stored in canonical East-facing space. (1,0) = one step forward.
        public HexCoord[] Offsets { get; }

        // 인접 링 어휘(§13.3 → 요괴 트랙 §2-2). Full이 기본이라 모든 legacy 형상과 플레이어 카드가
        // 바이트 동일하게 남는다. None은 밴드형(donut/artillery)의 "붙으면 안전"을 파서가 지키고,
        // Open은 인접 칸을 부분적으로만 채우고 싶은 형상이 쓴다.
        public AttackShapeAdjacency Adjacency { get; }

        /// <summary>런타임 합성 판정 — 인접 6칸을 <b>자동으로</b> 더할지. Open은 저작 오프셋만 쓴다.</summary>
        public bool IncludeAdjacentRing => Adjacency == AttackShapeAdjacency.Full;
    }
}

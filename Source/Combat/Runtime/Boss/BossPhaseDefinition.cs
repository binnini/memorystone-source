using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 한 페이즈의 저작 데이터(<c>boss_phases.csv</c> 한 행). 값은 <b>기본 스탯 대비 누적 목표치</b>다:
    /// 2페이즈 <c>maxHpBonus=24</c>, 3페이즈 <c>maxHpBonus=36</c>이면 3페이즈의 총 보너스가 36이라는 뜻이며
    /// 24+36이 아니다. 이렇게 두면 지표가 한 번에 크게 뛰어 페이즈를 건너뛰어도 결과가 정확하고,
    /// 같은 페이즈를 두 번 적용해도 값이 어긋나지 않는다.
    /// </summary>
    public sealed class BossPhaseDefinition
    {
        public BossPhaseDefinition(
            string bossId,
            int phaseIndex,
            int authoredThreshold,
            int progressThreshold,
            int strengthBonusPercent,
            int maxHpBonus,
            int patternPhaseMin,
            float visualScale,
            int footprintRadius,
            StatusEffectKind? auraStatusKind = null,
            string designerNote = "",
            MonsterFootprintShape footprintShape = MonsterFootprintShape.Single)
        {
            BossId = string.IsNullOrWhiteSpace(bossId)
                ? throw new ArgumentException("Boss phase bossId is required.", nameof(bossId))
                : bossId;
            PhaseIndex = phaseIndex;
            AuthoredThreshold = authoredThreshold;
            ProgressThreshold = Math.Max(0, progressThreshold);
            StrengthBonusPercent = Math.Max(0, strengthBonusPercent);
            MaxHpBonus = Math.Max(0, maxHpBonus);
            PatternPhaseMin = Math.Max(0, patternPhaseMin);
            VisualScale = visualScale <= 0f ? 1f : visualScale;
            FootprintRadius = Math.Max(0, footprintRadius);
            FootprintShape = footprintShape;
            AuraStatusKind = auraStatusKind;
            DesignerNote = designerNote ?? string.Empty;
        }

        public string BossId { get; }

        /// <summary>1부터 시작하는 연속 페이즈 번호.</summary>
        public int PhaseIndex { get; }

        /// <summary>CSV에 적힌 원본 threshold. 진단 메시지·저작 왕복용으로만 보관한다.</summary>
        public int AuthoredThreshold { get; }

        /// <summary>정규화된 진입 임계값(<see cref="BossPhaseMetricKind"/> 참고). 오름차순, 1페이즈는 0.</summary>
        public int ProgressThreshold { get; }

        /// <summary>이 페이즈에서 보스가 갖는 강화(%) 총량. 몬스터 피해 스케일링에 그대로 더해진다.</summary>
        public int StrengthBonusPercent { get; }

        /// <summary>기본 최대 체력 대비 누적 증가량. 감소는 불가하므로 페이즈가 올라갈 때 줄어들 수 없다.</summary>
        public int MaxHpBonus { get; }

        /// <summary>
        /// 이 페이즈에서 해금되는 공격 패턴 게이트 레벨.
        /// <c>monster_pattern_bindings.csv</c>의 <c>phaseMin</c>이 이 값 이하인 패턴만 후보가 된다.
        /// </summary>
        public int PatternPhaseMin { get; }

        /// <summary>표현 계층이 소비하는 보스 모델 배율(규칙에는 영향 없음).</summary>
        public float VisualScale { get; }

        /// <summary>
        /// 물리 점유 반경(0=1칸, 1=7칸, 2=19칸). P0~P5에서는 <b>소비되지 않는다</b> —
        /// 멀티타일 점유는 별도 마일스톤(P6)이며, 컬럼은 저작을 미리 받기 위해 존재한다.
        /// </summary>
        public int FootprintRadius { get; }

        /// <summary>
        /// 페이즈별 몸 <b>형상</b>(2026-09-04 §12 — 불가살 P2 tri). 원판 반경과 별개 축이며 tri는
        /// <c>footprintRadius=0</c>과만 함께 저작한다(파서 가드). 몬스터 tri 규약(회전 없음·원점=앞 몸통 칸·
        /// 거리=몸통 최솟값)을 그대로 태운다 — 페이즈 전환 연출과 몸 성장이 겹쳐 「커지는 보스」가 규칙으로 읽힌다.
        /// 저작 표면은 <c>boss_phases.csv</c>의 <c>footprintShape</c>(빈 값=원판/한 칸 · <c>tri</c>).
        /// </summary>
        public MonsterFootprintShape FootprintShape { get; }

        /// <summary>
        /// 이 페이즈에서 보스에게 붙는 아우라 상태이상. P0에서는 저작만 통과시키고 소비하지 않는다(P4).
        /// </summary>
        public StatusEffectKind? AuraStatusKind { get; }

        public string DesignerNote { get; }
    }
}

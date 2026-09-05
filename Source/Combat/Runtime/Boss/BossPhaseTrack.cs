using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 전투 중 보스 한 마리의 페이즈 런타임 상태. 보스 2체 동시 등장을 수용하려고
    /// <see cref="CombatState"/>가 리스트로 보유한다(보스별 1개).
    ///
    /// <see cref="CurrentPhase"/>는 <c>CombatState.SetBossPhase</c>만 바꾼다(하강 금지 단일 변이점).
    /// 외부 공개 투영은 <see cref="BossPhaseState"/>가 담당한다.
    /// </summary>
    internal sealed class BossPhaseTrack : IWeakSpotSlot
    {
        public BossPhaseTrack(string bossUnitId, string bossDefinitionId)
        {
            BossUnitId = bossUnitId ?? string.Empty;
            BossDefinitionId = bossDefinitionId ?? string.Empty;
            CurrentPhase = 1;
        }

        /// <summary>보스 몬스터의 런타임 유닛 id(같은 정의의 보스가 둘 있어도 구분된다).</summary>
        public string BossUnitId { get; }

        /// <summary>보스 카탈로그 조인 키(=monsterId).</summary>
        public string BossDefinitionId { get; }

        /// <summary>1-based 현재 페이즈. 절대 내려가지 않는다.</summary>
        public int CurrentPhase { get; set; }

        /// <summary>정규화된 지표 진행값. <see cref="BossPhaseDefinition.ProgressThreshold"/>와 직접 비교된다.</summary>
        public int MetricProgress { get; set; }

        /// <summary>기믹이 누적한 흡수 스택(<see cref="BossPhaseMetricKind.AbsorbedStacks"/>의 원천).</summary>
        public int AbsorbedStacks { get; set; }

        /// <summary>
        /// 지금까지 실제로 적용한 최대 체력 보너스. 페이즈 정의값은 누적 목표치이므로,
        /// 적용은 항상 (목표 - 적용됨) 차분으로 이뤄진다. 페이즈를 건너뛰어도 정확하고
        /// 재적용에 멱등이며, 서스펜드 왕복에서 이중 적용을 막는다.
        /// </summary>
        public int AppliedMaxHpBonus { get; set; }

        /// <summary>
        /// 기믹이 쓰는 턴 카운터(철조각의 살포 쿨다운). 트랙이 들고 있는 이유는 서스펜드 왕복이
        /// 공짜로 따라오기 때문이다 — 왕복하지 않으면 저장/재개로 주기를 되감는 세이브 스컴이 된다.
        /// 의미는 기믹이 정하고, 규칙 계층은 이 값을 해석하지 않는다.
        /// </summary>
        public int MechanicCooldownTurns { get; set; }

        /// <summary>
        /// 마지막으로 기물 살포가 <b>실제로 일어난</b> 전체 턴 번호(한 번도 없으면 0).
        ///
        /// 왜 필요한가: 살포 턴에는 보스가 공격을 건너뛴다(사용자 확정). 그런데 공격 예고는 플레이어
        /// 이동이 끝난 시점에 커밋되고 기믹 결의는 그보다 뒤에 도는데, 결의가 <see cref="MechanicCooldownTurns"/>를
        /// 곧바로 다음 주기로 되감아 버린다 — 쿨다운만 보면 <b>같은 턴 안에서 답이 뒤집힌다</b>
        /// (예고 시점 "살포함" → 공격 결의 시점 "살포 안 함" → 예고 없이 공격이 나간다).
        /// 이 래치가 결의 이후의 질의에 같은 답을 준다.
        ///
        /// 서스펜드 왕복 대상이 아니다: 저장은 턴 경계에서만 일어나므로 재개 시점에 "이번 턴에 살포했다"가
        /// 참인 경우가 없고, 왕복하지 않아도 다음 턴의 쿨다운 예측이 그대로 성립한다.
        /// </summary>
        public int LastPropVolleyOverallTurn { get; set; }

        // --- 전멸기(annihilation · §13.5) 상태. 철조각과 한 보스에 공존하므로 MechanicCooldownTurns를
        // 공유할 수 없어 전용 필드를 갖는다. 셋 다 서스펜드 왕복 대상 — 왕복하지 않으면 재개로
        // 주기가 되감기거나(쿨다운), 예고가 증발해 다음 행동에 예고 없이 터진다(예고 상태).

        /// <summary>전멸기 발동 주기 카운터.</summary>
        public int AnnihilationCooldownTurns { get; set; }

        /// <summary>예고에서 폭발까지 남은 몬스터 페이즈 수(예고 없음 = 0).</summary>
        public int AnnihilationTelegraphTurnsRemaining { get; set; }

        /// <summary>예고된 폭발 칸(예고=명중 계약의 원천 — 폭발은 이 저장본을 그대로 쓴다).</summary>
        public List<SeoulPlayup.Map.Runtime.HexCoord> AnnihilationTelegraphCells { get; } =
            new List<SeoulPlayup.Map.Runtime.HexCoord>();

        /// <summary>
        /// 안전지대 <b>후보</b> 칸 전부(§20-B). 진짜와 가짜가 섞여 있고, 겉모습은 구분되지 않는다.
        /// 서스펜드 왕복 대상 — 왕복하지 않으면 저장/재개로 진위를 다시 굴리는 세이브 스컴이 된다.
        /// </summary>
        public List<SeoulPlayup.Map.Runtime.HexCoord> AnnihilationCandidateCells { get; } =
            new List<SeoulPlayup.Map.Runtime.HexCoord>();

        /// <summary>후보 중 <b>진짜</b>인 칸들(예고에서 빠지는 칸 = 실제 안전지대).</summary>
        public List<SeoulPlayup.Map.Runtime.HexCoord> AnnihilationRealSafeCells { get; } =
            new List<SeoulPlayup.Map.Runtime.HexCoord>();

        /// <summary>정찰로 진위가 <b>판별된</b> 후보들. 판별되면 <c>?</c>가 걷히고 아래가 드러난다.</summary>
        public List<SeoulPlayup.Map.Runtime.HexCoord> AnnihilationRevealedCandidates { get; } =
            new List<SeoulPlayup.Map.Runtime.HexCoord>();

        /// <summary>
        /// 보스가 마지막으로 <b>중앙 점프</b>한 전체 턴 번호(한 번도 없으면 0).
        ///
        /// <see cref="LastPropVolleyOverallTurn"/>과 정확히 같은 이유로 존재한다: 점프 턴에는 보스가
        /// 일반 공격을 하지 않는데, 쿨다운으로 "점프 턴"을 판정하면 결의가 쿨다운을 되감아
        /// <b>같은 턴 안에서 답이 뒤집힌다</b>(예고 시점 "점프함" → 공격 결의 시점 "점프 안 함" →
        /// 예고 없이 공격이 나간다). 이 래치가 결의 이후의 질의에 같은 답을 준다.
        /// </summary>
        public int LastAnnihilationJumpOverallTurn { get; set; }

        /// <summary>
        /// 전멸기 시퀀스가 마지막으로 <b>이 턴을 점유</b>한 전체 턴 번호(점프 턴 ~ 폭발 턴).
        /// 철조각 살포가 이 값을 읽어 양보한다(§20-B-9).
        ///
        /// 래치가 필요한 이유는 <b>폭발 턴</b> 때문이다: 폭발이 해소되면 예고가 지워지고 쿨다운이
        /// 다음 주기로 세팅되므로, 결의 뒤에 물으면 "전멸기 진행 중 아님"이 되어 같은 턴에 살포가
        /// 끼어든다. 래치가 있으면 기믹 실행 순서(<c>mechanicId</c>의 <c>|</c> 순서)와 무관해진다.
        /// </summary>
        public int LastAnnihilationOccupiedOverallTurn { get; set; }

        // --- 철조각 사슬(scrap-chain · §21.8 제안 2). 전용 카운터·예고 상태(서스펜드 왕복 대상).

        /// <summary>사슬 발동 주기 카운터. 왕복 근거는 <see cref="MechanicCooldownTurns"/>와 같다.</summary>
        public int ScrapChainCooldownTurns { get; set; }

        /// <summary>
        /// 예고된 사슬 가닥들(보스 → 철조각 스포크 · 예고=명중 계약의 원천 — 명중은 이 저장본을 그대로
        /// 쓴다). 가닥이 철조각 유닛 id를 물고 있는 이유는 <b>파괴 보상</b> 때문이다: 예고 후 그 철조각이
        /// 죽으면 그 가닥만 무효가 된다. 서스펜드 왕복 대상 — 왕복하지 않으면 재개 직후 예고 없이
        /// 명중하거나 예고가 증발한다.
        /// </summary>
        public List<ScrapChainStrand> ScrapChainStrands { get; } = new List<ScrapChainStrand>();

        /// <summary>
        /// 철조각 살포 <b>예고</b> 칸(2026-09-05 실플레이 요구). 쿨다운이 0에 닿는 결의에서 다음 페이즈의 배치를
        /// 미리 뽑아 두고, 살포는 이 저장본을 먼저 쓴다(예고=배치 · 자리가 막히면 그 칸만 새로 뽑는다).
        /// 서스펜드 왕복 대상 — 왕복하지 않으면 재개 직후 예고 없이 살포된다.
        /// </summary>
        public List<SeoulPlayup.Map.Runtime.HexCoord> PropVolleyTelegraphCells { get; } = new List<SeoulPlayup.Map.Runtime.HexCoord>();

        // --- 함정 배치(trap-volley · §21.5). 철조각·전멸기와 한 보스에 공존하므로 전용 카운터를 갖는다.

        /// <summary>
        /// 함정 배치 주기 카운터. 서스펜드 왕복 대상 — 왕복하지 않으면 저장/재개로 주기가 되감겨
        /// 재개할 때마다 볼리가 한 번 더 깔린다(<see cref="MechanicCooldownTurns"/>와 같은 이유).
        /// </summary>
        public int TrapVolleyCooldownTurns { get; set; }

        /// <summary>
        /// 마지막으로 함정 배치가 <b>실제로 일어난</b> 전체 턴 번호(한 번도 없으면 0).
        /// 존재 이유·비직렬화 근거는 <see cref="LastPropVolleyOverallTurn"/>과 완전히 같다
        /// (배치 턴 공격 억제 질의가 결의 뒤에도 같은 답을 내기 위한 래치).
        /// </summary>
        public int LastTrapVolleyOverallTurn { get; set; }

        /// <summary>
        /// <see cref="HadLivingPropsAtMonsterPhaseStart"/>가 계산된 전체 턴 번호(0 = 아직 없음).
        /// 값은 턴마다 한 번만 관측되고 그 턴 내내 고정된다.
        /// </summary>
        public int PropPresenceObservedOverallTurn { get; set; }

        /// <summary>
        /// 이번 몬스터 페이즈가 시작될 때 판에 이 보스의 살아있는 기물이 있었는가(§21.7 양방향 배타의
        /// 턴 시작 래치). 전멸기는 이 값이 참인 동안 새 점프를 시작하지 않는다.
        ///
        /// 래치인 이유는 <b>흡수 턴</b> 때문이다: 철조각 흡수가 결의 중에 판을 비우므로, "지금 기물이
        /// 있나"를 살아있는 값으로 물으면 <c>mechanicId</c> 문자열 순서에 따라 같은 턴의 답이 갈린다
        /// (<c>iron-scrap|annihilation</c>이면 흡수 뒤 질의 = 같은 턴 발동, 반대면 양보) — §20.3이
        /// 금지한 바로 그 패턴이다. 페이즈 시작 시점 값으로 고정하면 순서 독립이 된다:
        /// <b>"철조각이 다 사라진 다음 턴부터."</b>
        ///
        /// 서스펜드 왕복 대상이 아니다(<see cref="LastPropVolleyOverallTurn"/>과 같은 이유): 저장은
        /// 턴 경계에서만 일어나고, 그 시점의 판은 이번 턴의 결의가 아직 아무것도 바꾸지 않은 상태라
        /// 재개 후 첫 질의의 재관측이 저장 전과 같은 값을 낸다.
        /// </summary>
        public bool HadLivingPropsAtMonsterPhaseStart { get; set; }

        // --- 수호(guard · DEC-2026-09-03-03).

        /// <summary>
        /// 수호 충전을 마지막으로 부여한 페이즈(0 = 아직 없음). 페이즈당 1회 부여의 래치다.
        /// 서스펜드 왕복 대상 — 왕복하지 않으면 재개할 때마다 현재 페이즈 몫이 다시 부여되는
        /// 세이브 스컴이 된다(쿨다운 왕복과 같은 이유).
        /// </summary>
        public int GuardChargesGrantedPhase { get; set; }

        // --- 취약 부위(weak-spot · §20-A). 셋 다 서스펜드 왕복 대상 — 왕복하지 않으면 저장/재개로
        // 자리를 다시 굴리는 세이브 스컴이 된다(RNG 4곳 산재로 시드 재현이 불가능하다는 기존 판정에 따라,
        // 재현이 아니라 저장으로 해결한다).

        /// <summary>취약 부위의 <b>중심 기준 축좌표 오프셋</b>. 절대 좌표가 아니다 — 보스가 걷거나
        /// 전멸기로 중앙 점프하면 절대 좌표는 몸 밖에 남는다.</summary>
        public int WeakSpotOffsetQ { get; private set; }

        /// <inheritdoc cref="WeakSpotOffsetQ"/>
        public int WeakSpotOffsetR { get; private set; }

        /// <summary>취약 부위가 선정되어 있는가. 반경 0(1페이즈)이면 false다.</summary>
        public bool HasWeakSpot { get; private set; }

        /// <summary>
        /// 취약 부위 판명이 유지되는 남은 몬스터 페이즈 수(0 = 미판명). <b>bool이 아니다</b>.
        ///
        /// 🔑 이 한 값이 <b>자리와 판명을 함께 지배</b>한다: 0이면 매 턴 자리를 다시 뽑고, 0보다 크면
        /// 자리를 고정한 채 카운터만 준다. bool + 별도 주기 카운터로 쪼개면 "자리는 바뀌었는데 판명이
        /// 남아 있는" 상태가 표현 가능해지고, 그 상태에는 정의된 의미가 없다.
        /// </summary>
        public int WeakSpotKnownTurnsRemaining { get; set; }

        /// <summary>취약 부위를 새 오프셋으로 정한다(재선정의 단일 변이점).</summary>
        public void SetWeakSpotOffset(int offsetQ, int offsetR)
        {
            WeakSpotOffsetQ = offsetQ;
            WeakSpotOffsetR = offsetR;
            HasWeakSpot = true;
        }

        /// <summary>취약 부위를 없앤다(반경 0 페이즈). 판명도 함께 사라진다.</summary>
        public void ClearWeakSpot()
        {
            WeakSpotOffsetQ = 0;
            WeakSpotOffsetR = 0;
            HasWeakSpot = false;
            WeakSpotKnownTurnsRemaining = 0;
        }
    }

    /// <summary>
    /// 사슬 한 가닥(§21.8 제안 2): 어느 철조각으로 뻗는 선이며 어떤 칸들을 지나는가. 칸 집합은 예고
    /// 시점에 굳는다(예고=명중) — 살아있는 가닥인지는 <c>PropUnitId</c>의 생존으로 매번 유도한다.
    /// </summary>
    internal sealed class ScrapChainStrand
    {
        public ScrapChainStrand(string propUnitId, IEnumerable<SeoulPlayup.Map.Runtime.HexCoord> cells)
        {
            PropUnitId = propUnitId ?? string.Empty;
            Cells = new List<SeoulPlayup.Map.Runtime.HexCoord>(
                cells ?? System.Linq.Enumerable.Empty<SeoulPlayup.Map.Runtime.HexCoord>());
        }

        public string PropUnitId { get; }
        public List<SeoulPlayup.Map.Runtime.HexCoord> Cells { get; }
    }
}

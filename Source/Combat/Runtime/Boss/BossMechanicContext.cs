using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// <see cref="IBossMechanic"/>가 전투 상태에 닿는 유일한 표면. 기믹이 만질 수 있는 것을
    /// 명시적으로 고정해 두면 "기믹이 규칙 전체를 헤집는" 사고를 구조적으로 막을 수 있다.
    /// 기믹이 새 능력을 필요로 할 때마다 여기에 메서드를 추가하고, 그 메서드가 왜 안전한지를 적는다.
    /// </summary>
    internal sealed class BossMechanicContext
    {
        private readonly CombatState state;
        private readonly MonsterRuntime boss;
        private readonly BossPhaseTrack track;

        internal BossMechanicContext(CombatState state, MonsterRuntime boss, BossPhaseTrack track, BossProfileDefinition profile)
        {
            this.state = state;
            this.boss = boss;
            this.track = track;
            Profile = profile;
        }

        public BossProfileDefinition Profile { get; }

        public string BossUnitId => boss.Id;
        public HexCoord BossCoord => boss.Coord;
        public CombatantState BossCombatant => boss.Combatant;

        /// <summary>1-based 현재 페이즈(이번 결의에서 아직 전환되기 전 값).</summary>
        public int CurrentPhase => track.CurrentPhase;

        public BossPhaseDefinition CurrentPhaseDefinition => Profile.GetPhase(track.CurrentPhase);

        public int OverallTurnNumber => state.OverallTurnNumber;

        /// <summary>
        /// 페이즈 지표용 흡수 스택을 더한다. 스택은 절대 줄지 않으므로 음수는 무시한다
        /// (지표 단조 증가 = 페이즈 하강 불가의 근거).
        /// </summary>
        public void AddAbsorbedStacks(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            track.AbsorbedStacks += amount;
        }

        /// <summary>
        /// 이 보스에 소속된 살아있는 기물들. 나이(스폰 후 경과한 몬스터 페이즈 수) 순으로 오래된 것부터.
        /// 죽은 기물은 포함하지 않는다 — 플레이어가 파괴한 것은 흡수될 수 없다는 게 이 기믹의 요점이다.
        /// </summary>
        public IReadOnlyList<BossPropView> GetLivingProps(string propDefinitionId)
        {
            return state.GetLivingBossProps(boss.Id, propDefinitionId);
        }

        /// <summary>
        /// 기물을 보스가 흡수한다: 보드에서 <b>조용히</b> 사라진다(사망이 아니므로 사망 연출·보상 없음).
        /// 반환값은 실제로 흡수되었는지 — 이미 죽었거나 없는 id는 false.
        /// </summary>
        /// <param name="blastRadius">흡수 폭발이 피해를 주는 반경(0이면 연출만).</param>
        /// <param name="blastDamage">폭발이 플레이어에게 주는 피해(0이면 연출만).</param>
        public bool AbsorbProp(string propUnitId, int blastRadius = 0, int blastDamage = 0)
        {
            return state.AbsorbBossProp(boss.Id, boss.Coord, propUnitId, blastRadius, blastDamage);
        }

        /// <summary>이번 몬스터 페이즈에 이 보스가 기물 살포를 했다고 래치한다(공격 억제 질의의 원천).</summary>
        public void MarkPropVolleyCastThisTurn()
        {
            track.LastPropVolleyOverallTurn = state.OverallTurnNumber;
        }

        /// <summary>이번 몬스터 페이즈에 이미 기물 살포가 일어났는가.</summary>
        public bool DidCastPropVolleyThisTurn =>
            track.LastPropVolleyOverallTurn != 0 && track.LastPropVolleyOverallTurn == state.OverallTurnNumber;

        /// <summary>
        /// 기믹이 자유롭게 쓰는 턴 카운터(살포 쿨다운 등). 서스펜드 왕복 대상이라 재개 후에도
        /// 주기가 이어진다 — 왕복하지 않으면 저장/불러오기로 쿨다운을 리셋하는 세이브 스컴이 된다.
        /// </summary>
        public int MechanicCooldownTurns
        {
            get => track.MechanicCooldownTurns;
            set => track.MechanicCooldownTurns = value < 0 ? 0 : value;
        }

        /// <summary>
        /// 기물 <paramref name="count"/>개를 한 번에 살포하고 실제로 놓인 개수를 돌려준다.
        /// 배치 기준점·링 회전·최소 간격 규칙은 <see cref="CombatState.SpawnBossPropVolley"/>가 갖는다.
        /// 요청보다 적게 놓였다면 그 사실이 <c>LastBossPropVolleyReport</c>에 남는다.
        /// </summary>
        public int SpawnPropVolley(string propDefinitionId, int count, int ringRadius, int minSpacing)
        {
            return state.SpawnBossPropVolley(boss.Id, propDefinitionId, count, ringRadius, minSpacing);
        }

        /// <summary>다음 페이즈의 살포 칸을 미리 뽑아 예고한다(2026-09-05). 규칙은 <see cref="CombatState.PlanBossPropVolley"/>.</summary>
        public int PlanPropVolley(int count, int ringRadius, int minSpacing)
        {
            return state.PlanBossPropVolley(boss.Id, count, ringRadius, minSpacing);
        }

        // --- 철조각 사슬(scrap-chain · §21.8 제안 2). 전용 카운터·예고 상태.

        /// <summary>사슬 발동 주기 카운터(서스펜드 왕복).</summary>
        public int ScrapChainCooldownTurns
        {
            get => track.ScrapChainCooldownTurns;
            set => track.ScrapChainCooldownTurns = value < 0 ? 0 : value;
        }

        /// <summary>사슬 예고가 걸려 있는가(살아있는 가닥 유무와 무관 — 해소가 예고를 지운다).</summary>
        public bool HasScrapChainTelegraph => track.ScrapChainStrands.Count > 0;

        /// <summary>살아있는 철조각 각각으로 스포크 예고를 건다. 규칙은 <see cref="CombatState.BeginBossScrapChainTelegraph"/>.</summary>
        public void BeginScrapChainTelegraph(string propDefinitionId)
        {
            state.BeginBossScrapChainTelegraph(track, boss, propDefinitionId);
        }

        /// <summary>예고 시점 저장본으로 사슬을 명중시킨다(죽은 철조각의 가닥은 무효). 규칙은
        /// <see cref="CombatState.ResolveBossScrapChainHit"/>.</summary>
        public void ResolveScrapChainHit(int damage, int rootTurns)
        {
            state.ResolveBossScrapChainHit(track, boss, damage, rootTurns);
        }

        // --- 함정 배치(trap-volley · §21.5). MechanicCooldownTurns(철조각)와 별개의 전용 카운터.

        /// <summary>함정 배치 주기 카운터(서스펜드 왕복).</summary>
        public int TrapVolleyCooldownTurns
        {
            get => track.TrapVolleyCooldownTurns;
            set => track.TrapVolleyCooldownTurns = value < 0 ? 0 : value;
        }

        /// <summary>이번 몬스터 페이즈에 이 보스가 함정 배치를 했다고 래치한다(공격 억제 질의의 원천).</summary>
        public void MarkTrapVolleyCastThisTurn()
        {
            track.LastTrapVolleyOverallTurn = state.OverallTurnNumber;
        }

        /// <summary>이번 몬스터 페이즈에 이미 함정 배치가 일어났는가.</summary>
        public bool DidCastTrapVolleyThisTurn =>
            track.LastTrapVolleyOverallTurn != 0 && track.LastTrapVolleyOverallTurn == state.OverallTurnNumber;

        /// <summary>아직 소진되지 않은 런타임(보스 배치) 함정 수(볼리 상한 가드용).</summary>
        public int CountArmedRuntimeTraps()
        {
            return state.CountArmedRuntimeTraps();
        }

        /// <summary>
        /// 함정 <paramref name="count"/>개를 한 번에 배치하고 실제로 놓인 개수를 돌려준다.
        /// 배치 기준점·링 회전·최소 간격 규칙은 <see cref="CombatState.SpawnBossTrapVolley"/>가
        /// 갖는다(발견은 정찰 전까지 숨김 — DEC-2026-09-03-01). 요청보다 적게 놓였다면 그 사실이
        /// <c>LastBossTrapVolleyReport</c>에 남는다.
        /// </summary>
        public int SpawnTrapVolley(int count, int ringRadius, int minSpacing, HexTrapEffectData effect)
        {
            return state.SpawnBossTrapVolley(boss.Id, count, ringRadius, minSpacing, effect);
        }

        /// <summary>
        /// 살포 동반 함정(2026-09-05 결정 2): 효과 목록 하나당 함정 하나를 <b>봉인된 아레나 전역</b>에 심고
        /// 실제로 놓인 개수를 돌려준다. 자리는 무작위(기물 RNG 공유)·최소 간격 그리디이며, 자리가 모자라면
        /// 개수를 줄인다. 발견은 정찰 전까지 숨김(DEC-2026-09-03-01). 규칙은 <see cref="CombatState.SpawnBossArenaTraps"/>.
        /// </summary>
        public int SpawnArenaTrapVolley(IReadOnlyList<HexTrapEffectData> effects, int minSpacing)
        {
            return state.SpawnBossArenaTraps(boss.Id, effects, minSpacing);
        }

        /// <summary>정의 id가 같은 살아있는 몬스터 수(터렛 상한 가드용).</summary>
        public int CountLivingMonstersOfDefinition(string definitionId)
        {
            return state.CountLivingMonstersOfDefinition(definitionId);
        }

        /// <summary>기물 배치 RNG에서 <c>[0, exclusiveMax)</c> 정수를 뽑는다(저주 카드 풀 추첨용 — 같은 RNG라 테스트가 고정할 수 있다).</summary>
        public int NextBossPropRandom(int exclusiveMax)
        {
            return state.NextBossPropRandom(exclusiveMax);
        }

        /// <summary>
        /// 보스 위에 알림 플로팅을 띄운다(특성 알림 채널 · <see cref="EffectKind.MonsterTraitTriggered"/>).
        /// 문안은 <see cref="MonsterTraitAnnouncement.TryGetText"/>가 ref로 해소한다.
        /// </summary>
        public void AnnounceBoss(string sourceRef, int amount = 0)
        {
            state.AnnounceBoss(boss, sourceRef, amount);
        }

        /// <summary>
        /// 보스에게 방어막(Block)을 부여한다(§21.8 제안 4 — 배치 턴에 몸을 굳힌다). 흡수는
        /// <see cref="CombatantState.ApplyDamage"/>가 플레이어와 공유하므로 부여만 뚫으면 되고,
        /// 몬스터 Block은 턴 시작 소거가 없어 <b>부술 때까지 남는다</b>(연출·안전 가드는 CombatState).
        /// </summary>
        public void AddBossBlock(int amount)
        {
            state.GrantBossGuardBlock(boss, amount);
        }

        // --- 수호(guard · DEC-2026-09-03-03).

        /// <summary>
        /// 현재 페이즈 몫의 수호 충전이 아직 부여되지 않았으면 부여한다(래치 판정 포함).
        /// 부여의 정본은 페이즈 진입(<c>ApplyBossPhaseEntry</c>)이고 이 호출은 조우 후 첫 결의의
        /// 캐치업이다 — 규칙·래치는 <see cref="CombatState.TryGrantBossGuardChargesForCurrentPhase"/> 한 곳에 있다.
        /// </summary>
        public void GrantPendingGuardCharges()
        {
            state.TryGrantBossGuardChargesForCurrentPhase(track, Profile, boss);
        }

        // --- 전멸기(annihilation · §13.5). 철조각과 한 보스에 공존하므로 MechanicCooldownTurns와
        // 별개의 전용 카운터·예고 상태를 쓴다. 전부 트랙 소유 = 서스펜드 왕복 대상.

        /// <summary>전멸기 발동 주기 카운터(서스펜드 왕복).</summary>
        public int AnnihilationCooldownTurns
        {
            get => track.AnnihilationCooldownTurns;
            set => track.AnnihilationCooldownTurns = value < 0 ? 0 : value;
        }

        /// <summary>예고에서 폭발까지 남은 몬스터 페이즈 수(서스펜드 왕복).</summary>
        public int AnnihilationTelegraphTurnsRemaining
        {
            get => track.AnnihilationTelegraphTurnsRemaining;
            set => track.AnnihilationTelegraphTurnsRemaining = value < 0 ? 0 : value;
        }

        /// <summary>예고가 진행 중인가(예고 칸 존재 기준 — 턴 수는 0이어도 폭발 직전 상태일 수 있다).</summary>
        public bool HasAnnihilationTelegraph => track.AnnihilationTelegraphCells.Count > 0;

        /// <summary>
        /// 전멸기를 개시한다(중앙 점프 → 착지 처리 → 후보 3곳 → 예고). 예고된 칸 수를 돌려주며,
        /// 후보를 세울 수 없으면 아무것도 하지 않고 0을 돌려준다 — <b>점프도 하지 않는다</b>.
        /// 규칙은 <see cref="CombatState.BeginBossAnnihilation"/>이 갖는다.
        /// </summary>
        public int BeginAnnihilation(CombatState.BossAnnihilationRequest request)
        {
            return state.BeginBossAnnihilation(track, boss, request);
        }

        /// <summary>발동 가능한가를 상태 변경 없이 확인한다(점프 턴 공격 억제 질의가 쓰는 순수 질의).</summary>
        public bool CanBeginAnnihilation(CombatState.BossAnnihilationRequest request)
        {
            return state.CanBeginBossAnnihilation(boss, request);
        }

        /// <summary>이번 몬스터 페이즈에 보스가 중앙 점프했다고 래치한다(공격 억제 질의의 원천).</summary>
        public void MarkAnnihilationJumpedThisTurn()
        {
            track.LastAnnihilationJumpOverallTurn = state.OverallTurnNumber;
        }

        /// <summary>이번 몬스터 페이즈에 이미 중앙 점프가 일어났는가.</summary>
        public bool DidAnnihilationJumpThisTurn =>
            track.LastAnnihilationJumpOverallTurn != 0
            && track.LastAnnihilationJumpOverallTurn == state.OverallTurnNumber;

        /// <summary>이번 몬스터 페이즈를 전멸기 시퀀스가 점유했다고 래치한다(철조각 양보의 원천).</summary>
        public void MarkAnnihilationOccupiedThisTurn()
        {
            track.LastAnnihilationOccupiedOverallTurn = state.OverallTurnNumber;
        }

        /// <summary>이번 몬스터 페이즈를 전멸기 시퀀스가 이미 점유했는가.</summary>
        public bool DidAnnihilationOccupyThisTurn =>
            track.LastAnnihilationOccupiedOverallTurn != 0
            && track.LastAnnihilationOccupiedOverallTurn == state.OverallTurnNumber;

        /// <summary>
        /// 전멸기 시퀀스가 이번 턴을 점유하는가(§20-B-9). 철조각 살포가 이 값을 읽어 양보한다.
        /// 판정은 전멸기 로직 옆에 <b>한 번만</b> 구현되어 있고 여기는 그 위임이다 — 중복 판정 = 드리프트.
        /// </summary>
        public bool WillAnnihilationOccupyThisTurn => AnnihilationMechanic.WillOccupyThisTurn(this);

        /// <summary>
        /// 이번 몬스터 페이즈가 시작될 때 판에 이 보스의 살아있는 기물이 있었는가(§21.7 래치).
        /// 전멸기가 이 값을 읽어 양보한다 — 살아있는 값이 아니라 래치인 이유는
        /// <see cref="BossPhaseTrack.HadLivingPropsAtMonsterPhaseStart"/> 주석 참조.
        /// </summary>
        public bool HadLivingPropsAtMonsterPhaseStart => state.HadLivingPropsAtMonsterPhaseStart(track, boss);

        /// <summary>예고 시점에 저장된 칸 집합으로 폭발을 해소하고 예고를 지운다(예고=명중).</summary>
        public void ResolveAnnihilationBlast(int damage)
        {
            state.ResolveBossAnnihilationBlast(track, boss, damage);
        }

        // --- 취약 부위(weak-spot · §20-A).

        /// <summary>
        /// 취약 부위를 한 몬스터 페이즈 진행시킨다: 판명 중이면 카운터만 줄이고(자리 고정),
        /// 미판명이면 자리를 다시 뽑는다. 규칙은 <c>CombatState.AdvanceBossWeakSpot</c>이 갖는다.
        /// </summary>
        public void AdvanceWeakSpot()
        {
            state.AdvanceBossWeakSpot(track, boss);
        }
    }

    /// <summary>기믹이 보는 기물 한 개의 읽기 전용 투영.</summary>
    internal readonly struct BossPropView
    {
        public BossPropView(string unitId, string definitionId, HexCoord coord, int ageTurns)
        {
            UnitId = unitId ?? string.Empty;
            DefinitionId = definitionId ?? string.Empty;
            Coord = coord;
            AgeTurns = ageTurns;
        }

        public string UnitId { get; }
        public string DefinitionId { get; }
        public HexCoord Coord { get; }

        /// <summary>스폰 후 지나간 몬스터 페이즈 수(스폰된 턴에는 0).</summary>
        public int AgeTurns { get; }
    }
}

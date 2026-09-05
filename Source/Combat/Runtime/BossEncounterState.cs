using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 조우 상태 협력자(3단계 구조 리팩토링 3-B · 2026-09-04). 옛 <c>CombatState.Boss*.cs</c> 8파셜의 본문을
    /// <b>무변경</b>으로 옮겼고, 호스트 상태에는 <see cref="IBossEncounterHost"/>를 통해서만 닿는다 — 사설 필드 직접 접근 0.
    /// 소유 상태: 보스 카탈로그·페이즈 트랙·직전 전환/기물 이벤트·봉인 아레나 id. CombatState의 공개 API는 위임으로 유지된다.
    /// </summary>
    internal sealed partial class BossEncounterState
    {
        private readonly IBossEncounterHost host;
        private readonly BossCatalogDefinition bossCatalog;

        public BossEncounterState(IBossEncounterHost host, BossCatalogDefinition bossCatalog)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.bossCatalog = bossCatalog ?? BossCatalogDefinition.Empty;
        }

        /// <summary>서스펜드·테스트용 트랙 목록(살아 있는 목록 — 복원은 Clear/Add로).</summary>
        internal List<BossPhaseTrack> Tracks => bossPhaseTracks;

        private readonly List<BossPhaseTrack> bossPhaseTracks = new List<BossPhaseTrack>();

        // 이번 몬스터 행동 결의 창에서 일어난 보스 페이즈 전환들. 표현 계층(타임라인 조립기)이 읽어
        // BossPhaseTransition 비트로 연출한다. ResolveBossMechanicsStep 진입 시 비워지므로 항상 "직전 결의"만 담는다.
        private readonly List<BossPhaseTransition> lastBossPhaseTransitions = new List<BossPhaseTransition>();

        /// <summary>이 전투에 보스 프로필이 걸린 보스가 하나라도 있는가.</summary>
        public bool HasBossPhaseTrack => bossPhaseTracks.Count > 0;

        public IReadOnlyList<BossPhaseState> BossPhases =>
            bossPhaseTracks.Select(CreateBossPhaseState).ToList();

        /// <summary>
        /// 직전 몬스터 행동 결의에서 올라간 보스 페이즈 전환들(표현 전용). 보스가 없거나 전환이 없었으면 비어 있다.
        /// 타임라인 조립기가 <see cref="Timeline.CombatTimelineEventKind.BossPhaseTransition"/> 비트로 소비한다.
        /// </summary>
        public IReadOnlyList<BossPhaseTransition> LastBossPhaseTransitions => lastBossPhaseTransitions;

        public bool TryGetBossPhaseState(string bossUnitId, out BossPhaseState state)
        {
            var track = FindBossPhaseTrack(bossUnitId);
            if (track == null)
            {
                state = default;
                return false;
            }

            state = CreateBossPhaseState(track);
            return true;
        }

        /// <summary>
        /// dev 랩 전용: 지표와 무관하게 보스를 다음 페이즈로 강제 전환한다. 실제 전환의 단일 변이점
        /// (<see cref="SetBossPhase"/>)을 그대로 타므로 스탯 적용·<see cref="BossPhaseChanged"/>(BGM)·
        /// <see cref="LastBossPhaseTransitions"/> 기록 규약이 실플레이와 같다. 결의 창 진입부와 같은
        /// "직전 전환만" 계약을 지키기 위해 기록을 비우고 시작한다. <paramref name="bossUnitId"/>가
        /// 비면 첫 트랙. 트랙 없음·사망 보스·최종 페이즈는 거부한다(페이즈 단조 계약 유지).
        /// </summary>
        public bool DebugAdvanceBossPhase(string bossUnitId, out int fromPhase, out int toPhase)
        {
            fromPhase = 0;
            toPhase = 0;
            var track = string.IsNullOrEmpty(bossUnitId) ? bossPhaseTracks.FirstOrDefault() : FindBossPhaseTrack(bossUnitId);
            if (track == null || !bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
            {
                return false;
            }

            var boss = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, track.BossUnitId, StringComparison.Ordinal));
            if (boss == null || boss.Combatant.IsDead || track.CurrentPhase >= profile.PhaseCount)
            {
                return false;
            }

            lastBossPhaseTransitions.Clear();
            fromPhase = track.CurrentPhase;
            SetBossPhase(track, profile, track.CurrentPhase + 1);
            toPhase = track.CurrentPhase;
            return toPhase > fromPhase;
        }

        /// <summary>
        /// 보스 프로필이 있는 보스 몬스터마다 페이즈 트랙을 만든다(이미 있으면 유지). 생성자와
        /// 서스펜드 복원, 런타임 보스 스폰이 모두 이 한 곳을 호출한다.
        /// </summary>
        internal void EnsureBossPhaseTracks()
        {
            if (bossCatalog == null || bossCatalog.Profiles.Count == 0)
            {
                return;
            }

            foreach (var monster in host.Monsters)
            {
                if (!CombatState.IsBossMonster(monster) || !bossCatalog.TryGetProfile(monster.DefinitionId, out _))
                {
                    continue;
                }

                if (FindBossPhaseTrack(monster.Id) != null)
                {
                    continue;
                }

                bossPhaseTracks.Add(new BossPhaseTrack(monster.Id, monster.DefinitionId));
            }
        }

        internal BossPhaseTrack FindBossPhaseTrack(string bossUnitId)
        {
            if (string.IsNullOrEmpty(bossUnitId))
            {
                return null;
            }

            return bossPhaseTracks.FirstOrDefault(track => string.Equals(track.BossUnitId, bossUnitId, StringComparison.Ordinal));
        }

        private BossPhaseState CreateBossPhaseState(BossPhaseTrack track)
        {
            bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile);
            var phase = profile?.GetPhase(track.CurrentPhase);
            var nextThreshold = 0;
            if (profile != null && track.CurrentPhase < profile.PhaseCount)
            {
                nextThreshold = profile.GetPhase(track.CurrentPhase + 1).ProgressThreshold;
            }

            return new BossPhaseState(
                track.BossUnitId,
                track.BossDefinitionId,
                profile?.DisplayName ?? string.Empty,
                profile?.PhaseMetric ?? BossPhaseMetricKind.AbsorbedStacks,
                track.CurrentPhase,
                profile?.PhaseCount ?? 1,
                track.MetricProgress,
                nextThreshold,
                track.AbsorbedStacks,
                phase?.VisualScale ?? 1f,
                phase?.FootprintRadius ?? 0,
                profile?.GetBgmCueId(track.CurrentPhase) ?? string.Empty,
                phase?.AuraStatusKind);
        }

        /// <summary>
        /// 보스 기믹 + 페이즈 전환 판정. 몬스터 행동 결의 창(<see cref="ResolveMonsterAction"/>) 안,
        /// 공격 결의 직전에 단 한 번 돈다: 이 위치는 이펙트 버퍼링/지연 상태 창 안이라
        /// <c>RaiseEffect</c>가 연출 타임라인에 자동으로 스케줄되고, 직후 종료 판정이 이미 돈다.
        /// 지표 평가와 전환은 <b>여기 한 곳에서만</b> 이뤄진다.
        /// </summary>
        internal void ResolveBossMechanicsStep()
        {
            if (bossPhaseTracks.Count == 0)
            {
                return;
            }

            // 이 결의에서 일어나는 전환만 담도록, 지표 평가/전환 판정에 앞서 비운다. 표현 계층은 결의 직후에 읽는다.
            lastBossPhaseTransitions.Clear();
            // 기물 살포/흡수 기록도 같은 규약이다 — 표현 계층은 결의 직후 "직전 결의"만 읽는다.
            ClearLastBossPropEvents();

            // 기물 나이는 보스 수와 무관하게 페이즈당 정확히 한 번만 늘어야 하므로 기믹 안이 아니라 여기서 돈다.
            AdvanceBossPropAges();

            // 기믹이 몬스터 리스트를 건드리므로(기물 흡수·소환) 트랙 사본을 순회한다.
            foreach (var track in bossPhaseTracks.ToList())
            {
                var boss = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, track.BossUnitId, StringComparison.Ordinal));
                if (boss == null || boss.Combatant.IsDead)
                {
                    continue;
                }

                // 🔴🔴 아레나에 들어가기 전의 보스는 <b>기믹도 돌지 않는다</b>(2026-09-02 #17).
                //
                // <para>2026-09-02 #4의 게이트는 <c>ClassifyMonsterActivity</c>(행동)와 표시 술어에만 걸렸는데,
                // 기믹 결의는 <b>활동 분류 밖</b>이라 그대로 돌았다 — 보스가 숨어 있는 동안 무대 뒤에서
                // 철조각을 살포하고 스스로 흡수해, 실측으로 <b>25턴 만에 3페이즈(스택 165·최대 체력 48→84)</b>가
                // 됐다. 플레이어가 도착했을 때는 살포 주기가 이미 한참 진행돼 「기믹이 한 번도 안 나오는」
                // 보스전이 된다.</para>
                //
                // <para>🔑 <b>새 술어를 만들지 않는다</b>: 행동·표시와 <b>같은</b>
                // <see cref="IsBossAwaitingArenaEncounter"/>를 본다. 다른 술어를 세우면 셋이 갈라져
                // 한쪽만 열리는 지금 같은 사고가 다시 난다.</para>
                //
                // <para>지표 평가·페이즈 전환까지 통째로 건너뛴다 — 전환만 남기면 흡수 없이도
                // 턴수 지표(<c>TurnNumber</c>) 보스가 무대 뒤에서 페이즈를 올린다.</para>
                if (IsBossAwaitingArenaEncounter(boss))
                {
                    continue;
                }

                if (!bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
                {
                    continue;
                }

                // §21.7 래치를 기믹 결의 <b>전에</b> 확정한다. 이 줄이 없으면 이번 턴의 첫 질의가
                // 철조각 흡수(자기 Resolve 맨 앞) <b>뒤에</b> 올 수 있고, 그때의 재관측은 이미 비워진
                // 판을 "페이즈 시작 상태"로 오인해 순서 의존이 되살아난다.
                HadLivingPropsAtMonsterPhaseStart(track, boss);

                // §13.5: 한 보스가 기믹을 여럿 가질 수 있다(철조각|전멸기). 저작 순서대로 해소한다.
                foreach (var mechanicId in profile.MechanicIds)
                {
                    if (BossMechanicRegistry.TryGet(mechanicId, out var mechanic))
                    {
                        mechanic.Resolve(new BossMechanicContext(host.Self, boss, track, profile));
                    }
                }

                track.MetricProgress = BossPhaseProgress.Evaluate(
                    profile.PhaseMetric,
                    track.AbsorbedStacks,
                    boss.Combatant.Hp,
                    boss.Combatant.MaxHp,
                    host.OverallTurnNumber);
                SetBossPhase(track, profile, profile.ResolvePhaseForProgress(track.MetricProgress));
            }
        }

        /// <summary>
        /// §21.7 양방향 배타의 턴 시작 래치: 이번 몬스터 페이즈가 시작될 때 판에 이 보스의 살아있는
        /// 기물이 있었는가. 턴마다 첫 질의에서 한 번 관측하고 그 턴 내내 같은 답을 준다.
        ///
        /// <para>첫 질의는 두 경로로 온다 — 공격 예고 커밋(플레이어 이동 종료)과
        /// <see cref="ResolveBossMechanicsStep"/>의 eager 확정. 둘 다 이번 턴의 기믹 결의가 판을
        /// 바꾸기 <b>전</b>이므로 어느 쪽이 먼저여도 같은 값이 잡힌다. 흡수 뒤의 늦은 첫 질의는
        /// eager 확정이 구조적으로 막는다.</para>
        /// </summary>
        internal bool HadLivingPropsAtMonsterPhaseStart(BossPhaseTrack track, MonsterRuntime boss)
        {
            if (track.PropPresenceObservedOverallTurn != host.OverallTurnNumber)
            {
                track.PropPresenceObservedOverallTurn = host.OverallTurnNumber;
                track.HadLivingPropsAtMonsterPhaseStart = host.Monsters.Any(monster =>
                    !monster.Combatant.IsDead
                    && MonsterSpawnRoles.IsBossProp(monster.SpawnRole)
                    && string.Equals(monster.OwnerUnitId, boss.Id, StringComparison.Ordinal));
            }

            return track.HadLivingPropsAtMonsterPhaseStart;
        }

        /// <summary>
        /// 페이즈의 단일 변이점(<c>SetPhase</c> 패턴 미러링): 같은 값은 no-op, 하강은 금지.
        /// 규칙 적용 → 이벤트 발화 순서를 지켜서, 구독자가 보는 상태는 항상 전환이 끝난 상태다.
        /// </summary>
        private void SetBossPhase(BossPhaseTrack track, BossProfileDefinition profile, int next)
        {
            if (track == null || profile == null)
            {
                return;
            }

            var clamped = Math.Min(Math.Max(next, 1), profile.PhaseCount);
            if (clamped <= track.CurrentPhase)
            {
                return;
            }

            var previous = track.CurrentPhase;
            track.CurrentPhase = clamped;
            ApplyBossPhaseEntry(track, profile);
            // 표현 계층이 결의 직후 읽어 BossPhaseTransition 연출 비트를 만든다. 이벤트 발화와 같은 규약이라
            // 규칙 적용이 끝난 뒤에 기록한다(구독자·표현이 보는 상태는 항상 전환이 끝난 상태다).
            lastBossPhaseTransitions.Add(new BossPhaseTransition(track.BossUnitId, previous, clamped));
            host.RaiseBossPhaseChanged(track.BossUnitId, previous, clamped);
        }

        /// <summary>
        /// 페이즈 진입 효과 적용. 페이즈 정의값은 기본 스탯 대비 <b>누적 목표치</b>라서 최대 체력은
        /// 차분만 올린다 — 페이즈를 건너뛰어도 정확하고, 강화(%)는 트랙에서 직접 읽히므로
        /// (<see cref="GetBossPhaseStrengthBonusPercent"/>) 여기서 상태이상을 만들지 않는다.
        /// </summary>
        private void ApplyBossPhaseEntry(BossPhaseTrack track, BossProfileDefinition profile)
        {
            var phase = profile.GetPhase(track.CurrentPhase);
            var boss = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, track.BossUnitId, StringComparison.Ordinal));
            if (boss == null)
            {
                return;
            }

            var maxHpDelta = phase.MaxHpBonus - track.AppliedMaxHpBonus;
            if (maxHpDelta > 0)
            {
                boss.Combatant.IncreaseMaxHp(maxHpDelta);
                track.AppliedMaxHpBonus = phase.MaxHpBonus;
            }

            // footprintRadius는 페이즈별 저작이라 전환이 곧 <b>몸 크기 변화</b>다. 점유 등록은 유도값이
            // 아니라 캐시(runtimeStates)이므로 여기서 다시 깔지 않으면 커진 몸이 이동 차단에 반영되지
            // 않는다 — 오버레이·히트테스트는 매번 계산해서 멀쩡하고 <b>이동만</b> 낡는 조용한 버그가 된다.
            // (세 페이즈 반경이 같던 동안에는 드러나지 않았다.)
            host.UpdateOccupancy();

            // 커진 몸이 결계 링·철조각·필드 오브젝트를 덮었으면 보스를 유효한 중심으로 옮긴다(§22.5-1).
            // 🔴 <b>플레이어 밀어내기보다 먼저</b>여야 한다 — 반대로 하면 밀어낸 뒤 보스가 떠나
            // 헛되이 함정만 밟히고, 플레이어가 도로 몸통 안에 남을 수도 있다.
            PullGrownBossIntoValidCentre(boss);

            // 커진 몸이 플레이어를 삼켰으면 밖으로 밀어낸다(§16.2). 점유를 다시 깐 <b>뒤</b>에 해야
            // 목적지 후보의 점유 판정이 새 몸 기준으로 이뤄진다.
            PushPlayerOutOfGrownBossFootprint();

            // 취약 부위 강제 재선정(§20-A-3). 페이즈 진입은 <b>반경이 바뀌어 후보 집합 자체가 바뀌는</b>
            // 지점이므로 판명 중이어도 다시 뽑는다 — 옛 오프셋은 새 몸에서 가장자리가 아닐 수 있다.
            // 조우 직후에는 아직 기믹 결의가 돌지 않았으므로 여기가 초기 선정 지점이기도 하다.
            ReselectBossWeakSpot(track, boss);

            // 함정 재무장(구 arenaTrapRefillOnPhaseEntry · §18)은 §21.5(결정 4)로 삭제됐다 —
            // 함정 공급은 이제 trap-volley 기믹이 맡고, "해체한 함정이 되살아나는" §18.5의 미해결
            // 결함도 계약과 함께 사라졌다.

            // 수호(guard · DEC-2026-09-03-03): 페이즈 진입이 부여의 정본이다. 기믹 결의는 페이즈
            // 평가보다 먼저 돌아 결의 안에서만 부여하면 전환 턴의 몫이 한 턴 늦는다 — 진입 즉시
            // 부여해야 "페이즈 초입 몇 장을 흘려보낸다"는 의도가 전환 턴부터 선다.
            TryGrantBossGuardChargesForCurrentPhase(track, profile, boss);
        }

        /// <summary>
        /// 현재 페이즈 몫의 수호 충전을 아직 부여하지 않았으면 부여한다(guard 기믹 저작 보스 전용 ·
        /// DEC-2026-09-03-03). 페이즈 진입(<see cref="ApplyBossPhaseEntry"/>)과 조우 후 첫 기믹 결의
        /// (<see cref="GuardMechanic"/> 캐치업)가 함께 부르며, 래치(<see cref="BossPhaseTrack.GuardChargesGrantedPhase"/>
        /// · 서스펜드 왕복)가 페이즈당 1회를 강제한다. 페이즈를 건너뛰면(1→3) 건너뛴 몫은 부여하지
        /// 않는다 — 충전은 "진입 보상"이지 누적 권리가 아니다.
        /// </summary>
        internal void TryGrantBossGuardChargesForCurrentPhase(BossPhaseTrack track, BossProfileDefinition profile, MonsterRuntime boss)
        {
            if (track == null || profile == null || boss == null || boss.Combatant.IsDead
                || !profile.MechanicIds.Contains(GuardMechanicParams.MechanicId)
                || track.GuardChargesGrantedPhase >= track.CurrentPhase)
            {
                return;
            }

            track.GuardChargesGrantedPhase = track.CurrentPhase;

            var chargesByPhase = profile.GetMechanicIntList(GuardMechanicParams.ChargesByPhase);
            var charges = chargesByPhase.Count == 0
                ? 0
                : chargesByPhase[Math.Min(Math.Max(track.CurrentPhase, 1), chargesByPhase.Count) - 1];
            if (charges > 0)
            {
                GrantBossGuardCharges(boss, charges);
            }
        }

        /// <summary>
        /// 보스에게 수호 충전을 부여한다(guard 기믹 · DEC-2026-09-03-03). 플레이어 수호
        /// (<see cref="GrantGuardCharge"/>)와 같은 상태(<see cref="StatusEffectKind.Guard"/>)를 쓰므로
        /// 소비는 유닛 일반화된 부여 관문 하나가 맡는다. 플레이어의 "최대 1 충전" 게이트는 유물 계약이라
        /// 따르지 않는다 — 보스 충전 수는 페이즈 저작(guardChargesByPhase)이 정하고 잔여분과 합산된다.
        /// </summary>
        internal void GrantBossGuardCharges(MonsterRuntime boss, int amount)
        {
            if (boss == null || amount <= 0 || boss.Combatant.IsDead)
            {
                return;
            }

            const string SourceRef = "boss.phase.guard";
            // 수명은 턴이 아니라 소비다(OnConsume) — turns 값은 IsExpired(>0)만 만족하면 된다(플레이어 부여와 동일).
            host.AddDurationStatusEffect(StatusEffectKind.Guard, boss.Id, 1, amount, SourceRef);
            host.RaiseStatusEffect(StatusEffectKind.Guard, boss.Coord, 0, amount, boss.Id, SourceRef);
        }

        /// <summary>
        /// 보스 주기 기믹(철조각 살포·함정 배치)의 다음 발동까지 남은 몬스터 페이즈 수 투영(HUD용 ·
        /// 2026-09-03 ⑥). <b>1 = 다가오는 몬스터 페이즈에 발동</b>(쿨다운 C의 발동은 C+1페이즈 뒤 —
        /// C번의 감소 턴 뒤 0에서 발동한다). 저작이 없거나 조우 전·페이즈 미달이면 해당 항목 -1.
        /// 전멸기 시퀀스 양보로 실제 발동이 1~3턴 밀릴 수 있다 — 그 구간은 전멸기 예고가 화면을
        /// 차지하므로(HUD가 그쪽을 우선한다) 카운트다운이 화면과 다투지 않는다.
        /// </summary>
        public void GetBossGimmickCountdowns(string bossUnitId, out int propVolleyTurnsRemaining, out int trapVolleyTurnsRemaining)
        {
            propVolleyTurnsRemaining = -1;
            trapVolleyTurnsRemaining = -1;

            var track = FindBossPhaseTrack(bossUnitId);
            var boss = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, bossUnitId, StringComparison.Ordinal));
            if (track == null || boss == null || boss.Combatant.IsDead
                || IsBossAwaitingArenaEncounter(boss)
                || !bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
            {
                return;
            }

            if (profile.MechanicIds.Contains("iron-scrap"))
            {
                propVolleyTurnsRemaining = track.MechanicCooldownTurns + 1;
            }

            if (profile.MechanicIds.Contains("trap-volley")
                && track.CurrentPhase >= profile.GetMechanicInt(TrapVolleyMechanicParams.PhaseMin))
            {
                trapVolleyTurnsRemaining = track.TrapVolleyCooldownTurns + 1;
            }
        }

        /// <summary>
        /// 보스가 현재 페이즈에서 갖는 강화(%). 몬스터 피해 스케일링이 상태이상 강화와 <b>합산</b>해서
        /// 읽는다. 상태이상 파이프라인을 쓰지 않는 이유: 페이즈 강화는 만료되지 않는 고유 스탯이라
        /// 지속 턴 감소를 타면 안 되고, 디버프 아이콘으로 보여서도 안 된다.
        /// </summary>
        internal int GetBossPhaseStrengthBonusPercent(string bossUnitId)
        {
            var track = FindBossPhaseTrack(bossUnitId);
            if (track == null || !bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
            {
                return 0;
            }

            return profile.GetPhase(track.CurrentPhase).StrengthBonusPercent;
        }

        /// <summary>
        /// 이 몬스터의 공격 패턴 게이트 레벨. 패턴 바인딩의 <c>phaseMin</c>이 이 값 이하인 패턴만
        /// 선택 후보가 된다. 보스가 아닌 몬스터(및 프로필 없는 보스)는 0이므로, 기존 저작은
        /// <c>phaseMin</c> 컬럼이 비어 있는 채로 그대로 통과한다.
        /// </summary>
        internal int GetMonsterPatternPhaseGateInternal(MonsterRuntime monster)
        {
            if (monster == null)
            {
                return 0;
            }

            var track = FindBossPhaseTrack(monster.Id);
            if (track == null || !bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
            {
                return 0;
            }

            return profile.GetPhase(track.CurrentPhase).PatternPhaseMin;
        }
    }
}

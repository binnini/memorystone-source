using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 전투 중 몬스터 한 마리의 런타임 상태(좌표, 인텐트, 턴 계획, 공격 패턴 쿨다운 등).
    /// 원래 CombatState의 private nested 타입이었으나 몬스터 AI 분리를 위해 최상위로 승격했다.
    /// internal + InternalsVisibleTo로 기존 접근 범위(Combat/EditModeTests)를 그대로 보존한다.
    /// 외부 공개 투영은 별도의 <see cref="MonsterRuntimeState"/>가 담당한다.
    /// </summary>
    internal sealed class MonsterRuntime
    {
        public MonsterRuntime(string id, HexCoord coord, CombatantState combatant, string catalogSourceId, string definitionId, string spawnRefId, string spawnRole, int attackSpeed = 1, MonsterAttackPattern[] attackPatterns = null, IReadOnlyList<HexCoord> patrolArea = null, int movePerTurn = 1)
        {
            Id = id;
            Coord = coord;
            SpawnCoord = coord;
            Combatant = combatant;
            CatalogSourceId = catalogSourceId ?? string.Empty;
            DefinitionId = definitionId ?? string.Empty;
            SpawnRefId = spawnRefId ?? string.Empty;
            SpawnRole = spawnRole ?? string.Empty;
            Intent = new EnemyIntent(EnemyIntentType.Patrol, 0, coord, coord);
            LockedFacingIntent = Intent;
            IntentPredictedMoveCoord = coord;
            TurnPlan = MonsterTurnPlan.Inactive(coord);
            AttackSpeed = attackSpeed <= 0 ? 1 : attackSpeed;
            // 0은 "저작 안 됨"이 아니라 <b>고정 포탑</b>이다(D-5, C-7 터렛). 예전에는 여기서 0을 1로
            // 끌어올려 이동 0을 저작할 방법이 아예 없었다 — 두 구성 지점(InitializeMonsters·서스펜드 복원)이
            // 카탈로그 엔트리가 없을 때 이미 지역 변수 1로 시작하므로, "값이 안 왔다"는 여기 도달하지 않는다.
            MovePerTurn = System.Math.Max(0, movePerTurn);
            AttackPatterns = attackPatterns != null && attackPatterns.Length > 0
                ? attackPatterns
                : new[] { MonsterAttackPattern.CreateBasicDamage($"{Id}.basic-attack", "Basic Attack", 1, 0) };
            AttackPatternIndex = 0;
            PatrolArea = patrolArea == null ? new List<HexCoord>() : patrolArea.Distinct().OrderBy(coord => coord).ToList();
        }

        public string Id { get; }
        public HexCoord Coord { get; set; }
        public HexCoord SpawnCoord { get; }
        public CombatantState Combatant { get; }
        public string CatalogSourceId { get; }
        public string DefinitionId { get; }
        public string SpawnRefId { get; }
        public string SpawnRole { get; }
        public EnemyIntent Intent { get; set; }
        public EnemyIntent LockedFacingIntent { get; set; }
        public HexCoord IntentPredictedMoveCoord { get; set; }
        public MonsterTurnPlan TurnPlan { get; set; }
        public MonsterFsmMemory FsmMemory { get; } = new MonsterFsmMemory();
        public bool PendingAttackIntent { get; set; }
        public MonsterActivityState ActivityState { get; set; } = MonsterActivityState.ActiveThreat;
        public int AttackSpeed { get; }
        public int MovePerTurn { get; }

        /// <summary>몸 형상(카탈로그 <c>footprint</c>). 두 구성 지점(InitializeMonsters·서스펜드 복원)이 엔트리에서 옮겨 적는다.</summary>
        public MonsterFootprintShape FootprintShape { get; set; } = MonsterFootprintShape.Single;
        public MonsterAttackPattern[] AttackPatterns { get; }
        public int AttackPatternIndex { get; set; }

        /// <summary>
        /// 이번 의도의 피해 굴림 오프셋(턴별 수치 변주, DEC-2026-08-19-02). 의도 잠금 시
        /// MonsterAiPlanner가 패턴의 <c>DamageJitter</c> 범위 [−J, +J]에서 1회 굴려 쓴다 — 「굴린
        /// 값이 상태」라 예고와 집행(<c>ResolveMonsterAttackDamageToPlayer</c>)이 같은 값을 소비하고
        /// (R-8), 서스펜드에도 이 값이 실린다(RNG 상태가 아니라 — DEC-2026-07-18-02 스냅샷 원칙).
        /// 변주 없는 패턴이 선택되면 계획부가 0으로 되돌린다.
        /// </summary>
        public int AttackDamageRollOffset { get; set; }

        public IReadOnlyList<HexCoord> PatrolArea { get; }

        /// <summary>
        /// 이번 턴의 계획이 <b>도약</b>인가(§17). 계획부가 매 턴 <see cref="TurnPlan"/>과 함께 다시 세우는
        /// <b>일시값</b>이라 <b>직렬화하지 않는다</b> — 서스펜드에서 살아 돌아와도 어차피 그 턴의 계획을
        /// 다시 세우므로, 왕복시키면 낡은 계획이 새 계획을 덮어쓰는 위험만 생긴다.
        /// 해소부는 이 플래그를 보고 걷기 게이트(이동 차단·이동력)보다 <b>앞에서</b> 순간 이동시킨다.
        /// </summary>
        public bool PlannedLeap { get; set; }

        /// <summary>
        /// <b>랩 전용</b> 강제 패턴 핀(-1 = 없음). 0 이상이면 계획부의 패턴 <b>선택</b>을 건너뛰고
        /// 이 인덱스를 그대로 커밋한다.
        ///
        /// <para>⚠️ 이것은 선택기의 <b>우회</b>다 — 복제가 아니다. 그래서 "실게임에서 이 패턴이 실제로
        /// 뽑히는 상황이 나오는가"에는 답하지 못한다(그 물음은 랩의 「조건 만들기」가 답한다). 겨냥 칸·
        /// 피해·상태이상은 커밋 이후 전부 실제 경로를 타므로 <b>연출과 형상 확인에는 그대로 유효</b>하다.</para>
        ///
        /// <para><see cref="PlannedLeap"/>과 같은 성격의 <b>일시값</b>이라 직렬화하지 않는다.</para>
        /// </summary>
        public int ForcedAttackPatternIndex { get; set; } = -1;

        /// <summary>
        /// 이 몬스터를 소유한 보스의 유닛 id. <c>boss-prop</c> 기물만 채워지며(보스 2체 동시 등장 시
        /// 어느 보스의 기물인지 구분한다), 그 외에는 빈 문자열이다. 서스펜드 왕복 대상.
        /// </summary>
        public string OwnerUnitId { get; set; } = string.Empty;

        /// <summary>
        /// 스폰 후 지나간 몬스터 페이즈 수(스폰된 턴에는 0). 보스 기물의 "성숙"(흡수 가능해지는 시점)
        /// 판정에만 쓰인다. 서스펜드 왕복 대상 — 잃으면 재개 후 성숙이 초기화되어 세이브 스컴이 된다.
        /// </summary>
        public int AgeTurns { get; set; }

        /// <summary>
        /// 약오름(T7-2) 현재 힘 스택(0~카탈로그 상한). 스택 1당 공격 피해 +1(flat —
        /// ResolveMonsterAttackDamageToPlayer가 소비). 서스펜드 왕복 대상 — 잃으면 재개가 스택을
        /// 공짜로 씻어 세이브 스컴이 된다.
        /// </summary>
        public int AgitationStacks { get; set; }

        /// <summary>
        /// 처치 보상을 이미 지급했는가(2026-09-05 실플레이 #7). 보상 흐름의 래치(rewardedMonsterIds)는
        /// 뷰가 들고 있어 로비로 나가면 사라지고, 이어하기로 복원된 시체는 전부 「방금 죽은 적」으로
        /// 보여 보상을 한 번 더 뿌렸다. 중단 저장을 건너 살아남는 정본은 이 플래그다.
        /// </summary>
        public bool RewardClaimed { get; set; }

        /// <summary>
        /// 소매치기(야광귀 · 2026-09-04 §2-D)가 지금까지 훔쳐 간 엽전 누적액. 처치되면 전액 반환되고
        /// (aftermath.restore kind=Money), 처치 못 하고 전투가 끝나면 개체와 함께 사라진다(손실).
        ///
        /// <para>🔴 서스펜드 왕복 대상이다 — 잃으면 재개가 훔친 돈을 증발시켜 「쫓아가 잡을 이유」가
        /// 세이브 한 번에 사라진다.</para>
        /// </summary>
        public int StolenMoney { get; set; }

        /// <summary>
        /// 죽으면서 <b>돌려준</b> 엽전(2026-09-05). <see cref="StolenMoney"/>는 반환과 함께 0이 되므로,
        /// 전리품 목록이 「(반환)」 줄을 세우려면 액수가 어딘가 남아 있어야 한다 — 그 자리다.
        /// 지갑에는 이미 들어간 값이라 이 필드는 <b>표시 전용</b>이다.
        /// </summary>
        public int RestoredMoney { get; set; }

        /// <summary>
        /// 맷집(T7-2) 소진 래치. false = 다음 카드 피격 피해가 반감된다. 서스펜드 왕복 대상 —
        /// 잃으면 재개가 맷집을 공짜로 재충전해 세이브 스컴이 된다.
        /// </summary>
        public bool ToughnessSpent { get; set; }

        /// <summary>
        /// 맷집 재장전 진행 — 소진 후 <b>연속</b> 비감지 턴 수. 감지되면 0으로 되감기고,
        /// 카탈로그의 재장전 턴에 도달하면 래치가 풀린다. 서스펜드 왕복 대상.
        /// </summary>
        public int ToughnessReloadProgress { get; set; }

        /// <summary>수호 재충전 진행(2026-09-05 결정 1) — 마지막 충전 이후 지난 턴 수. 서스펜드 왕복 대상.</summary>
        public int GuardRechargeProgress { get; set; }

        /// <summary>
        /// 은신(요괴 §4-1)이 <b>드러나 있는</b> 남은 턴. 0 = 숨어 있음.
        /// <para>공격이 해소되거나(예고 시점이 아니다) 정찰에 맞으면 저작된 턴 수로 채워지고, 턴 경계마다
        /// 1씩 줄어 0이 되면 다시 숨는다. 세이브 왕복 대상 — 잃으면 재개 순간 몬스터가 도로 사라진다.</para>
        /// </summary>
        public int StealthRevealTurnsRemaining { get; set; }

        // Remaining cooldown (monster turns) per attack-pattern index. Patterns on cooldown are excluded
        // from selection so status-effect attacks cannot be used on consecutive turns. Indexed by the
        // position in AttackPatterns; absent/zero means available.
        private readonly Dictionary<int, int> attackPatternCooldowns = new Dictionary<int, int>();

        public bool IsAttackPatternOnCooldown(int index)
        {
            return attackPatternCooldowns.TryGetValue(index, out var remaining) && remaining > 0;
        }

        public void StartAttackPatternCooldown(int index)
        {
            if (AttackPatterns == null || index < 0 || index >= AttackPatterns.Length)
            {
                return;
            }

            var cooldown = AttackPatterns[index].CooldownTurns;
            if (cooldown <= 0)
            {
                return;
            }

            // +1 absorbs the decrement that runs at this same turn boundary, so the pattern is skipped for
            // exactly CooldownTurns subsequent monster turns. See TickAttackPatternCooldowns.
            attackPatternCooldowns[index] = cooldown + 1;
        }

        public void TickAttackPatternCooldowns()
        {
            if (attackPatternCooldowns.Count == 0)
            {
                return;
            }

            foreach (var index in attackPatternCooldowns.Keys.ToList())
            {
                var remaining = attackPatternCooldowns[index] - 1;
                if (remaining <= 0)
                {
                    attackPatternCooldowns.Remove(index);
                }
                else
                {
                    attackPatternCooldowns[index] = remaining;
                }
            }
        }

        public MonsterAttackPattern CurrentAttackPattern =>
            AttackPatterns != null && AttackPatterns.Length > 0
                ? AttackPatterns[AttackPatternIndex % AttackPatterns.Length]
                : MonsterAttackPattern.CreateBasicDamage($"{Id}.basic-attack", "Basic Attack", 1, 0);

        // --- Suspend(전투 중 저장) 왕복용. 원래 CombatState.Suspend.cs의 partial 확장이었다. ---

        public Dictionary<int, int> CloneAttackPatternCooldowns()
        {
            return new Dictionary<int, int>(attackPatternCooldowns);
        }

        public void RestoreCooldowns(IReadOnlyDictionary<int, int> cooldowns)
        {
            attackPatternCooldowns.Clear();
            if (cooldowns == null)
            {
                return;
            }

            foreach (var pair in cooldowns)
            {
                if (pair.Value > 0)
                {
                    attackPatternCooldowns[pair.Key] = pair.Value;
                }
            }
        }
    }

    /// <summary>
    /// 몬스터 턴 하나의 이동/공격 계획 스냅샷. MonsterRuntime과 함께 최상위로 승격했다.
    /// </summary>
    internal readonly struct MonsterTurnPlan
    {
        public MonsterTurnPlan(
            EnemyIntent movementIntent,
            HexCoord plannedMoveCoord,
            EnemyIntent attackFacingIntent,
            bool isActive,
            bool canAttack)
        {
            MovementIntent = movementIntent;
            PlannedMoveCoord = plannedMoveCoord;
            AttackFacingIntent = attackFacingIntent;
            IsActive = isActive;
            CanAttack = canAttack;
        }

        public EnemyIntent MovementIntent { get; }
        public HexCoord PlannedMoveCoord { get; }
        public EnemyIntent AttackFacingIntent { get; }
        public bool IsActive { get; }
        public bool CanAttack { get; }

        public static MonsterTurnPlan Inactive(HexCoord coord)
        {
            var idleIntent = new EnemyIntent(EnemyIntentType.Patrol, 0, coord, coord);
            return new MonsterTurnPlan(idleIntent, coord, idleIntent, isActive: false, canAttack: false);
        }

        public MonsterTurnPlan WithResolvedMove(HexCoord resolvedCoord, bool canAttack)
        {
            var targetCoord = AttackFacingIntent.PlayerCoord;
            var attackFacingIntent = new EnemyIntent(
                AttackFacingIntent.Type,
                resolvedCoord.DistanceTo(targetCoord),
                resolvedCoord,
                targetCoord);

            return new MonsterTurnPlan(
                MovementIntent,
                resolvedCoord,
                attackFacingIntent,
                IsActive,
                CanAttack && canAttack);
        }
    }

    /// <summary>
    /// 몬스터 행동 해소 기록(<see cref="MonsterActionResolutionRecord"/>)의 빌더.
    /// MonsterRuntime과 함께 최상위로 승격했다.
    /// </summary>
    internal sealed class MonsterActionResolutionBuilder
    {
        public MonsterActionResolutionBuilder(string monsterId, MonsterActivityState activityBefore, HexCoord beforeCoord, bool wasVisibleBefore, int actionOrder)
        {
            MonsterId = monsterId;
            ActivityBefore = activityBefore;
            BeforeCoord = beforeCoord;
            WasVisibleBefore = wasVisibleBefore;
            ActionOrder = actionOrder;
        }

        public string MonsterId { get; }
        public MonsterActivityState ActivityBefore { get; }
        public HexCoord BeforeCoord { get; }
        public bool WasVisibleBefore { get; }
        public int ActionOrder { get; }
        public bool AttackedPlayer { get; set; }
        public bool AffectedPlayer { get; set; }
        public int DamageToPlayer { get; set; }
        public int AttackOrder { get; set; } = -1;
        public string AttackPatternId { get; set; } = string.Empty;
        public string AttackAnimationTrigger { get; set; } = string.Empty;
        public string PresentationGroupId { get; set; } = string.Empty;
        public IReadOnlyList<HexCoord> MovePath { get; set; } = System.Array.Empty<HexCoord>();
        public bool MissedAttack { get; set; }
        /// <summary>이번 이동이 도약(§17)이었다 — 연출이 걷기 슬라이드 대신 점프 비트를 낸다(§28 W7).</summary>
        public bool LeapedMove { get; set; }
        public bool KnockedBackPlayer { get; set; }
        public HexCoord? PlayerKnockbackFrom { get; set; }
        public HexCoord? PlayerKnockbackTo { get; set; }
        public bool VisibleAtAttackTime { get; set; }
        public bool SelfTargetedAttack { get; set; }

        /// <summary>
        /// 이 행동을 은신한 채 했는가(2026-09-01 #1). 행동이 끝난 뒤 노출되더라도 이 값은 그대로다 —
        /// 그래야 방금의 기습이 「보이던 공격」으로 되돌아가지 않는다.
        /// </summary>
        public bool HiddenByStealthDuringAction { get; set; }

        /// <summary>예고(커밋) 시점에 겨눈 칸. 연출의 모델 회전이 규칙의 형상 방향과 같은 값을 보도록 옮긴다(#7).</summary>
        public HexCoord? AimCoord { get; set; }

        /// <summary>
        /// 「밀어붙이기」(요괴 §4-5) 전진 — 공격을 마친 <b>뒤</b>의 한 칸이다.
        /// <para>🔴 이 칸을 <see cref="ToRecord"/>의 afterCoord에 섞으면 안 된다. 그러면 걷기 비트가
        /// 전진분까지 삼켜 "두 칸 걸어와서 때렸다"로 보이고, 공격 앵커도 실제 때린 자리보다 한 칸
        /// 앞으로 밀린다. 공격은 <see cref="AdvancedFrom"/>에서 나가고, 전진은 그 뒤 별도 스텝이다.</para>
        /// </summary>
        public HexCoord? AdvancedFrom { get; set; }

        public HexCoord? AdvancedTo { get; set; }

        public MonsterActionResolutionRecord ToRecord(HexCoord afterCoord, bool isVisibleAfter)
        {
            return new MonsterActionResolutionRecord(
                MonsterId,
                ActivityBefore,
                BeforeCoord,
                afterCoord,
                WasVisibleBefore,
                isVisibleAfter,
                AttackedPlayer,
                AffectedPlayer,
                DamageToPlayer,
                ActionOrder,
                AttackOrder,
                AttackPatternId,
                AttackAnimationTrigger,
                PresentationGroupId,
                MovePath,
                MissedAttack,
                LeapedMove,
                KnockedBackPlayer,
                PlayerKnockbackFrom,
                PlayerKnockbackTo,
                VisibleAtAttackTime,
                SelfTargetedAttack,
                AimCoord,
                AdvancedFrom,
                AdvancedTo,
                HiddenByStealthDuringAction);
        }
    }
}

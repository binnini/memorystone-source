using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 불가살 1호 기믹 — 철조각 흡수 성장. 보스가 주기적으로 철조각(prop 몬스터) 한 무더기를 뿌리고,
    /// 성숙한 것을 흡수해 페이즈 지표(흡수 스택)를 쌓는다. 플레이어의 대응은 성숙 전에 부수는 것이며,
    /// 그 압박이 이 보스전의 축이다.
    ///
    /// 압박의 형태는 <b>매 턴 한 개씩 새는 것이 아니라 주기적인 큰 파도</b>다(1차 실플레이 결과 확정):
    /// N턴에 한 번 여러 개가 한꺼번에 깔리고, 플레이어는 성숙까지 남은 몇 턴 안에 그것들을 처리해야 한다.
    ///
    /// <para><b>2026-09-05 결정 2 — 살포가 함정도 심는다.</b> 옛 별도 기믹(trap-volley·5턴 주기·링 r2·피해 2)은
    /// 은퇴했고, 살포 턴에 아레나 <b>전역</b>에 강력한 함정(둔화·기절·저주·터렛)을 페이즈별 풀만큼 심는다.
    /// 함정은 정찰 전까지 숨어 있고 화면에는 「함정 설치!」 알림만 뜬다(사용자 확정 Q14). 방어막(옛
    /// trapVolleyGuardBlock)도 살포 턴으로 옮겨 왔다. 주기는 페이즈별(<c>volleyIntervalByPhase</c>)로 갈라져
    /// 1페이즈는 빨리 진화하고 2페이즈는 여유를 둔다(Q18).</para>
    ///
    /// 보스 id를 모른다 — 대상은 컨텍스트가 주고, 수치는 전부 <c>mechanicParams</c> 저작이다.
    /// 다른 보스가 다른 prop·다른 속도로 같은 기믹을 쓰는 것도 저작만으로 가능하다.
    /// </summary>
    internal sealed class IronScrapMechanic : IBossMechanic, IBossArenaSealedMechanic
    {
        public string MechanicId => "iron-scrap";

        public IReadOnlyList<string> RequiredParamKeys => new[]
        {
            IronScrapMechanicParams.PropId,
            IronScrapMechanicParams.StackPerProp,
            IronScrapMechanicParams.MaturityTurns,
            IronScrapMechanicParams.VolleyIntervalTurns,
            IronScrapMechanicParams.VolleyByPhase,
            IronScrapMechanicParams.RingRadius,
            IronScrapMechanicParams.MinSpacing,
            IronScrapMechanicParams.MaxAlive
        };

        public void ValidateParams(IReadOnlyDictionary<string, string> mechanicParams, int phaseCount)
        {
            mechanicParams.TryGetValue(IronScrapMechanicParams.VolleyByPhase, out var volleyValue);
            var volleyByPhase = BossProfileDefinition.ParseIntList(volleyValue, IronScrapMechanicParams.VolleyByPhase);
            if (volleyByPhase.Count != phaseCount)
            {
                throw new ArgumentException(
                    $"'{IronScrapMechanicParams.VolleyByPhase}' must have exactly one entry per phase " +
                    $"(expected {phaseCount}, got {volleyByPhase.Count}).");
            }

            for (var i = 0; i < volleyByPhase.Count; i++)
            {
                if (volleyByPhase[i] < 0)
                {
                    throw new ArgumentException(
                        $"'{IronScrapMechanicParams.VolleyByPhase}' phase {i + 1} count cannot be negative.");
                }
            }

            RequirePositive(mechanicParams, IronScrapMechanicParams.VolleyIntervalTurns);
            RequirePositive(mechanicParams, IronScrapMechanicParams.RingRadius);
            RequirePositive(mechanicParams, IronScrapMechanicParams.MinSpacing);
            RequirePositive(mechanicParams, IronScrapMechanicParams.MaxAlive);

            // 주기 <= 성숙이면 살포 시점에 이전 볼리가 아직 판에 남아 기물이 누적된다. 그 상태가 곧
            // maxAlive 가드에 걸려 볼리가 조용히 잘리는 저작 사고이므로, 파싱 시점에 거부한다.
            var interval = ReadInt(mechanicParams, IronScrapMechanicParams.VolleyIntervalTurns);
            var maturity = ReadInt(mechanicParams, IronScrapMechanicParams.MaturityTurns);
            if (interval <= maturity)
            {
                throw new ArgumentException(
                    $"'{IronScrapMechanicParams.VolleyIntervalTurns}' ({interval}) must be greater than " +
                    $"'{IronScrapMechanicParams.MaturityTurns}' ({maturity}); otherwise volleys pile up and get silently truncated.");
            }

            // 페이즈별 주기(선택). 있으면 페이즈 수와 같은 길이 · 각 값이 성숙보다 커야 한다(같은 이유).
            mechanicParams.TryGetValue(IronScrapMechanicParams.VolleyIntervalByPhase, out var intervalValue);
            var intervalByPhase = BossProfileDefinition.ParseIntList(intervalValue, IronScrapMechanicParams.VolleyIntervalByPhase);
            if (intervalByPhase.Count > 0)
            {
                if (intervalByPhase.Count != phaseCount)
                {
                    throw new ArgumentException(
                        $"'{IronScrapMechanicParams.VolleyIntervalByPhase}' must have exactly one entry per phase " +
                        $"(expected {phaseCount}, got {intervalByPhase.Count}).");
                }

                for (var i = 0; i < intervalByPhase.Count; i++)
                {
                    if (intervalByPhase[i] <= maturity)
                    {
                        throw new ArgumentException(
                            $"'{IronScrapMechanicParams.VolleyIntervalByPhase}' phase {i + 1} ({intervalByPhase[i]}) must be greater than " +
                            $"'{IronScrapMechanicParams.MaturityTurns}' ({maturity}).");
                    }
                }
            }

            ValidateTrapParams(mechanicParams, phaseCount);
        }

        /// <summary>
        /// 살포 동반 함정 저작 검증. 풀이 비어 있으면 나머지 키는 묻지 않는다(함정 없는 보스 저작 하위호환).
        /// 풀이 있으면: 페이즈 수와 길이 일치 · 저주가 있으면 카드 풀 필수 · 터렛이 있으면 페이즈별 몬스터 id 필수.
        /// 지속·간격·상한은 양수여야 한다 — 0이면 「밟아도 아무 일 없는」 유령 함정이거나 아레나가 통째로 함정이 된다.
        /// </summary>
        private static void ValidateTrapParams(IReadOnlyDictionary<string, string> mechanicParams, int phaseCount)
        {
            mechanicParams.TryGetValue(IronScrapMechanicParams.VolleyTrapPoolByPhase, out var poolValue);
            var pools = BossVolleyTrapPool.ParseByPhase(poolValue, IronScrapMechanicParams.VolleyTrapPoolByPhase);
            if (pools.Count == 0)
            {
                return;
            }

            if (pools.Count != phaseCount)
            {
                throw new ArgumentException(
                    $"'{IronScrapMechanicParams.VolleyTrapPoolByPhase}' must have exactly one entry per phase " +
                    $"(expected {phaseCount}, got {pools.Count}).");
            }

            var usesCurse = false;
            var usesTurret = false;
            var usesSlow = false;
            var usesStun = false;
            foreach (var pool in pools)
            {
                foreach (var entry in pool)
                {
                    usesCurse |= entry.Kind == IronScrapMechanicParams.TrapKindCurse;
                    usesTurret |= entry.Kind == IronScrapMechanicParams.TrapKindTurret;
                    usesSlow |= entry.Kind == IronScrapMechanicParams.TrapKindSlow;
                    usesStun |= entry.Kind == IronScrapMechanicParams.TrapKindStun;
                }
            }

            RequirePositive(mechanicParams, IronScrapMechanicParams.VolleyTrapMinSpacing);
            RequirePositive(mechanicParams, IronScrapMechanicParams.VolleyTrapMaxArmed);
            if (usesSlow)
            {
                RequirePositive(mechanicParams, IronScrapMechanicParams.VolleyTrapSlowTurns);
            }

            if (usesStun)
            {
                RequirePositive(mechanicParams, IronScrapMechanicParams.VolleyTrapStunTurns);
            }

            if (usesCurse)
            {
                mechanicParams.TryGetValue(IronScrapMechanicParams.VolleyTrapCursePool, out var curseValue);
                if (BossVolleyTrapPool.ParseIdList(curseValue).Count == 0)
                {
                    throw new ArgumentException(
                        $"'{IronScrapMechanicParams.VolleyTrapPoolByPhase}' authors Curse traps but '{IronScrapMechanicParams.VolleyTrapCursePool}' is empty — 어느 저주 카드를 섞을지 알 수 없다.");
                }
            }

            if (usesTurret)
            {
                mechanicParams.TryGetValue(IronScrapMechanicParams.VolleyTurretByPhase, out var turretValue);
                var turrets = BossVolleyTrapPool.ParsePhaseStringList(turretValue);
                if (turrets.Count != phaseCount)
                {
                    throw new ArgumentException(
                        $"'{IronScrapMechanicParams.VolleyTurretByPhase}' must have exactly one monsterId per phase " +
                        $"(expected {phaseCount}, got {turrets.Count}) because the pool authors Turret traps.");
                }

                for (var i = 0; i < turrets.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(turrets[i]))
                    {
                        throw new ArgumentException(
                            $"'{IronScrapMechanicParams.VolleyTurretByPhase}' phase {i + 1} monsterId is empty.");
                    }
                }

                RequirePositive(mechanicParams, IronScrapMechanicParams.VolleyTurretMaxAlive);
            }

            if (ReadInt(mechanicParams, IronScrapMechanicParams.VolleyGuardBlock) < 0)
            {
                throw new ArgumentException($"'{IronScrapMechanicParams.VolleyGuardBlock}' cannot be negative.");
            }
        }

        public void Resolve(BossMechanicContext context)
        {
            var propId = context.Profile.GetMechanicString(IronScrapMechanicParams.PropId);
            if (string.IsNullOrWhiteSpace(propId))
            {
                return;
            }

            var stackPerProp = context.Profile.GetMechanicInt(IronScrapMechanicParams.StackPerProp);
            var maturityTurns = context.Profile.GetMechanicInt(IronScrapMechanicParams.MaturityTurns);
            var maxAlive = context.Profile.GetMechanicInt(IronScrapMechanicParams.MaxAlive);
            var blastRadius = context.Profile.GetMechanicInt(IronScrapMechanicParams.BlastRadius);
            var blastDamage = context.Profile.GetMechanicInt(IronScrapMechanicParams.BlastDamage);

            // ① 성숙한 철조각을 흡수한다. 오래된 것부터(GetLivingProps가 나이순) 처리한다.
            // 흡수는 그 자리에서 터지며(연출 + 반경 피해), 힘이 보스에게 빨려 들어간다.
            var absorbed = 0;
            foreach (var prop in context.GetLivingProps(propId))
            {
                if (prop.AgeTurns < maturityTurns)
                {
                    continue;
                }

                if (context.AbsorbProp(prop.UnitId, blastRadius, blastDamage))
                {
                    absorbed++;
                }
            }

            if (absorbed > 0)
            {
                context.AddAbsorbedStacks(absorbed * stackPerProp);
                // 「+N 흡수」 — 흡수로 얻은 스택은 HUD 게이지 말고는 아무 데도 안 떴다(2026-09-05 Q20).
                context.AnnounceBoss(BossPropAnnouncements.AbsorbedRef, absorbed * stackPerProp);
            }

            // ②-0 전멸기 시퀀스가 이번 턴을 점유하면 살포하지 않는다(§20-B-9 · 사용자 결정 4).
            // ⚠️ <b>쿨다운을 소모하기 전에</b> 빠져나온다: 소모하면 살포가 계속 뒤로 밀려 결국 사라진다.
            // "대기"여야 하고, 전멸기 시퀀스가 끝난 다음 턴에 곧바로 살포된다.
            // 금지 대상은 <b>살포뿐</b>이다 — 흡수(①)는 계속 허용한다. 흡수까지 막으면 성숙 타이밍이
            // 밀려 "3턴 안에 부숴라"는 계약이 꼬인다. (전멸기를 저작하지 않은 보스에서는 항상 false.)
            if (context.WillAnnihilationOccupyThisTurn)
            {
                return;
            }

            // ② 살포는 주기적이다. 쿨다운이 0인 턴에만 한 무더기를 뿌린다(첫 결의에서 바로 한 번).
            if (context.MechanicCooldownTurns > 0)
            {
                context.MechanicCooldownTurns--;
                if (context.MechanicCooldownTurns == 0)
                {
                    // 다음 몬스터 페이즈에 살포한다 — 그 칸을 지금 뽑아 예고한다(2026-09-05 실플레이 요구:
                    // 「살포 예고 오버레이」). 살포는 이 저장본을 먼저 쓰므로 예고=배치다.
                    context.PlanPropVolley(
                        ResolveVolleyCount(context, propId, maxAlive),
                        context.Profile.GetMechanicInt(IronScrapMechanicParams.RingRadius),
                        context.Profile.GetMechanicInt(IronScrapMechanicParams.MinSpacing));
                }

                return;
            }

            // maxAlive는 이제 상한 노브가 아니라 저작 사고 가드다(주기 > 성숙이면 여기 남은 기물은 0).
            var count = ResolveVolleyCount(context, propId, maxAlive);
            if (count <= 0)
            {
                return;
            }

            var placed = context.SpawnPropVolley(
                propId,
                count,
                context.Profile.GetMechanicInt(IronScrapMechanicParams.RingRadius),
                context.Profile.GetMechanicInt(IronScrapMechanicParams.MinSpacing));
            if (placed <= 0)
            {
                // 🔴 2026-09-05 실플레이(「1페이즈에 살포를 안 쓴다」): 종전엔 쿨다운을 <b>배치 전에</b> 되감아,
                // 자리가 없어 한 개도 못 놓은 턴이 주기 하나를 통째로 삼켰다. 살포는 「쿨이 돌면 반드시」여야
                // 하므로 실패한 턴은 쿨다운을 0에 둔 채 다음 몬스터 페이즈에 다시 시도한다.
                return;
            }

            // 이번 턴이 주기의 첫 턴이므로 남은 대기는 interval - 1이다. interval을 그대로 넣으면
            // 실제 간격이 한 턴씩 길어진다(10턴 저작인데 11턴마다 터진다). 주기는 <b>살포 시점의 페이즈</b>
            // 것을 읽는다 — 페이즈가 살포와 흡수 사이에 오르면 다음 살포까지의 간격은 옛 페이즈 값이다.
            // 🔑 <b>실제로 놓인 뒤에만</b> 되감는다(위 실패 경로 참조).
            context.MechanicCooldownTurns = ResolveVolleyInterval(context) - 1;

            // 살포가 실제로 일어난 턴을 래치한다 — 이 턴의 공격 억제 질의가 결의 <b>뒤에도</b>
            // 같은 답을 내려면 쿨다운이 아니라 이 값을 봐야 한다(BossPhaseTrack 주석 참조).
            context.MarkPropVolleyCastThisTurn();
            context.AnnounceBoss(BossPropAnnouncements.VolleyRef, placed);

            // ③ 살포 턴에 함정을 심는다(결정 2). 살포가 실제로 일어난 턴에만 — 자리가 없어 철조각을 한 개도
            // 못 놓은 턴에 함정·방어막만 얻으면 「뿌리는 대가로 굳는다」는 교환이 공짜가 된다.
            var trapsPlaced = PlaceVolleyTraps(context);
            if (trapsPlaced > 0)
            {
                // 함정은 숨어 있으므로(Q14) 화면에 남는 유일한 흔적이 이 한 줄이다. 개수는 말하지 않는다 —
                // 「몇 개가 어디에」는 정찰이 답할 질문이다.
                context.AnnounceBoss(BossPropAnnouncements.TrapsPlacedRef);
            }

            // ④ 살포하며 몸을 굳힌다(§21.8 제안 4 · 옛 함정 배치 방어막의 후계). 견고 특성이 있으면 부술 때까지 남는다.
            var guardBlock = context.Profile.GetMechanicInt(IronScrapMechanicParams.VolleyGuardBlock);
            if (guardBlock > 0)
            {
                context.AddBossBlock(guardBlock);
            }
        }

        /// <summary>
        /// 이번 페이즈 풀의 함정을 아레나 전역에 심고 실제로 놓인 개수를 돌려준다. 종류별 효과 데이터는
        /// 여기서 맵 함정 어휘(<see cref="HexTrapEffectData"/>)로 번역한다. 터렛은 살아있는 터렛이 상한 이상이면
        /// 그 몫을 건너뛴다(다른 종류로 바꾸지 않는다 — 풀 비율이 저작 의도다).
        /// </summary>
        private static int PlaceVolleyTraps(BossMechanicContext context)
        {
            var pools = BossVolleyTrapPool.ParseByPhase(
                context.Profile.GetMechanicString(IronScrapMechanicParams.VolleyTrapPoolByPhase),
                IronScrapMechanicParams.VolleyTrapPoolByPhase);
            if (pools.Count == 0)
            {
                return 0;
            }

            var phaseIndex = Math.Min(Math.Max(context.CurrentPhase, 1), pools.Count) - 1;
            var pool = pools[phaseIndex];
            if (pool.Count == 0)
            {
                return 0;
            }

            var maxArmed = context.Profile.GetMechanicInt(IronScrapMechanicParams.VolleyTrapMaxArmed);
            var budget = Math.Max(0, maxArmed - context.CountArmedRuntimeTraps());
            if (budget <= 0)
            {
                return 0;
            }

            var cursePool = BossVolleyTrapPool.ParseIdList(
                context.Profile.GetMechanicString(IronScrapMechanicParams.VolleyTrapCursePool));
            var turrets = BossVolleyTrapPool.ParsePhaseStringList(
                context.Profile.GetMechanicString(IronScrapMechanicParams.VolleyTurretByPhase));
            var turretId = turrets.Count > phaseIndex ? turrets[phaseIndex] : string.Empty;
            var turretMaxAlive = context.Profile.GetMechanicInt(IronScrapMechanicParams.VolleyTurretMaxAlive);
            var slowTurns = context.Profile.GetMechanicInt(IronScrapMechanicParams.VolleyTrapSlowTurns, 1);
            var stunTurns = context.Profile.GetMechanicInt(IronScrapMechanicParams.VolleyTrapStunTurns, 1);

            var effects = new List<HexTrapEffectData>();
            foreach (var entry in pool)
            {
                for (var i = 0; i < entry.Count; i++)
                {
                    if (effects.Count >= budget)
                    {
                        break;
                    }

                    switch (entry.Kind)
                    {
                        case IronScrapMechanicParams.TrapKindSlow:
                            // 둔화 amount = 이동력 감소량(MoveRangePenalty). 카탈로그 기본치(1)와 맞춘다.
                            effects.Add(new HexTrapEffectData(HexTrapEffectKind.Slow, 1, slowTurns));
                            break;
                        case IronScrapMechanicParams.TrapKindStun:
                            effects.Add(new HexTrapEffectData(HexTrapEffectKind.Stun, 1, stunTurns));
                            break;
                        case IronScrapMechanicParams.TrapKindCurse:
                            if (cursePool.Count > 0)
                            {
                                var cardId = cursePool[context.NextBossPropRandom(cursePool.Count)];
                                effects.Add(new HexTrapEffectData(HexTrapEffectKind.InjectStatusCard, 1, 0, null, cardId));
                            }

                            break;
                        case IronScrapMechanicParams.TrapKindTurret:
                            if (!string.IsNullOrWhiteSpace(turretId)
                                && context.CountLivingMonstersOfDefinition(turretId) + CountTurretEffects(effects, turretId) < turretMaxAlive)
                            {
                                effects.Add(new HexTrapEffectData(HexTrapEffectKind.SpawnMonsters, 1, 0, turretId));
                            }

                            break;
                    }
                }
            }

            if (effects.Count == 0)
            {
                return 0;
            }

            return context.SpawnArenaTrapVolley(
                effects,
                context.Profile.GetMechanicInt(IronScrapMechanicParams.VolleyTrapMinSpacing, 1));
        }

        private static int CountTurretEffects(List<HexTrapEffectData> effects, string turretId)
        {
            var count = 0;
            foreach (var effect in effects)
            {
                if (effect.Kind == HexTrapEffectKind.SpawnMonsters
                    && string.Equals(effect.MonsterDefinitionId, turretId, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 살포는 그 턴의 보스 <b>행동</b>이다 — 살포한 턴에는 일반 공격을 하지 않는다(사용자 확정).
        ///
        /// 결의 전(예고 커밋 시점)과 결의 후(공격 결의 시점) 모두에서 같은 답이 나와야 한다:
        /// 앞의 항은 "이번 턴에 살포할 것이다"(쿨다운 0 + 실제로 놓을 것이 있음), 뒤의 항은
        /// "이번 턴에 살포했다"(래치). 결의가 쿨다운을 되감으므로 앞의 항만으로는 답이 뒤집힌다.
        /// 상태를 바꾸지 않는 순수 질의다.
        /// </summary>
        public bool SuppressesMonsterAttackThisTurn(BossMechanicContext context)
        {
            if (context.DidCastPropVolleyThisTurn)
            {
                return true;
            }

            // 전멸기에 양보한 턴에는 살포가 없으므로 <b>공격도 막지 않는다</b>. 이 항이 없으면
            // 보스가 일어나지 않을 살포 때문에 공격을 건너뛰는 빈 턴이 생긴다.
            if (context.WillAnnihilationOccupyThisTurn)
            {
                return false;
            }

            if (context.MechanicCooldownTurns > 0)
            {
                return false;
            }

            var propId = context.Profile.GetMechanicString(IronScrapMechanicParams.PropId);
            return !string.IsNullOrWhiteSpace(propId)
                   && ResolveVolleyCount(context, propId, context.Profile.GetMechanicInt(IronScrapMechanicParams.MaxAlive)) > 0;
        }

        /// <summary>
        /// 조우(봉인) 시점에 첫 살포 칸을 예고한다(2026-09-05 후속 #1). 조우 턴 결의는 쿨다운 0으로 곧장 살포하므로
        /// 예고 단계가 없었다 — 여기서 뽑아 두면 그 결의가 저장본을 먼저 쓴다. 쿨다운이 남아 있으면(저작이 첫 살포를
        /// 늦추는 경우) 결의 쪽 「쿨다운이 0에 닿는 턴」 예고가 그대로 맡는다.
        /// </summary>
        public void OnArenaSealed(BossMechanicContext context)
        {
            if (context.MechanicCooldownTurns > 0 || context.WillAnnihilationOccupyThisTurn)
            {
                return;
            }

            var propId = context.Profile.GetMechanicString(IronScrapMechanicParams.PropId);
            if (string.IsNullOrWhiteSpace(propId))
            {
                return;
            }

            context.PlanPropVolley(
                ResolveVolleyCount(context, propId, context.Profile.GetMechanicInt(IronScrapMechanicParams.MaxAlive)),
                context.Profile.GetMechanicInt(IronScrapMechanicParams.RingRadius),
                context.Profile.GetMechanicInt(IronScrapMechanicParams.MinSpacing));
        }

        /// <summary>이번 살포 시점의 주기: 페이즈별 저작이 있으면 그것, 없으면 단일 주기.</summary>
        private static int ResolveVolleyInterval(BossMechanicContext context)
        {
            var byPhase = context.Profile.GetMechanicIntList(IronScrapMechanicParams.VolleyIntervalByPhase);
            if (byPhase.Count > 0)
            {
                return byPhase[Math.Min(Math.Max(context.CurrentPhase, 1), byPhase.Count) - 1];
            }

            return context.Profile.GetMechanicInt(IronScrapMechanicParams.VolleyIntervalTurns);
        }

        /// <summary>이번 페이즈에 실제로 놓으려 시도할 기물 수(요청 개수를 maxAlive 가드로 자른 값).</summary>
        private static int ResolveVolleyCount(BossMechanicContext context, string propId, int maxAlive)
        {
            var volleyByPhase = context.Profile.GetMechanicIntList(IronScrapMechanicParams.VolleyByPhase);
            var requested = volleyByPhase.Count == 0
                ? 0
                : volleyByPhase[Math.Min(Math.Max(context.CurrentPhase, 1), volleyByPhase.Count) - 1];

            var alive = context.GetLivingProps(propId).Count;
            return Math.Min(requested, Math.Max(0, maxAlive - alive));
        }

        private static void RequirePositive(IReadOnlyDictionary<string, string> mechanicParams, string key)
        {
            if (ReadInt(mechanicParams, key) <= 0)
            {
                throw new ArgumentException($"'{key}' must be a positive integer.");
            }
        }

        private static int ReadInt(IReadOnlyDictionary<string, string> mechanicParams, string key)
        {
            return mechanicParams.TryGetValue(key, out var value)
                   && int.TryParse(
                       value,
                       System.Globalization.NumberStyles.Integer,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out var parsed)
                ? parsed
                : 0;
        }
    }

    /// <summary>
    /// 철조각 살포·흡수·함정 설치의 <b>알림</b> source ref(플로팅 텍스트 전용 · 2026-09-05 Q20 확정 문안).
    /// 연출 큐(<c>BossPropSourceRefs</c> — 살포 캐스트·착지·흡수 폭발의 <b>그림</b>)와는 별개 채널이다:
    /// 그 큐들은 <c>targetUnitId="field"</c>·수치 0이라 플로팅 게이트에 걸려 침묵하므로(지대 생성 알림과
    /// 같은 이유), 문안은 특성 알림 채널(<see cref="EffectKind.MonsterTraitTriggered"/>)로 따로 나간다.
    /// 문안 자체는 <see cref="MonsterTraitAnnouncement.TryGetText"/> 한 표가 든다.
    /// </summary>
    public static class BossPropAnnouncements
    {
        /// <summary>살포 캐스트. amount = 놓인 철조각 수 → 「철조각 ×N」.</summary>
        public const string VolleyRef = "boss.prop.volley.count";
        /// <summary>흡수. amount = 이번 결의에 얻은 스택 → 「+N 흡수」.</summary>
        public const string AbsorbedRef = "boss.prop.absorbed";
        /// <summary>함정 설치(개수·위치는 말하지 않는다) → 「함정 설치!」.</summary>
        public const string TrapsPlacedRef = "boss.trap.placed";
    }
}

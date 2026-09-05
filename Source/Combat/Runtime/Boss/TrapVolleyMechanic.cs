using System;
using System.Collections.Generic;
using System.Globalization;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 함정 배치 기믹(§21.5 — 결정 4). 함정의 유일한 공급원이다: 정적 저작(맵 trapRefs)은 보스전에서
    /// 0으로 비웠고, 페이즈 재무장 계약은 삭제됐다 — 보스가 주기적으로 심는 것만이 판의 함정이다.
    ///
    /// 형태는 철조각 살포(<see cref="IronScrapMechanic"/>)의 미러다:
    /// <list type="bullet">
    /// <item>주기 배치(쿨다운 0인 턴에 한 볼리) · 배치한 턴에는 일반 공격을 하지 않는다.</item>
    /// <item>🔴 <b>전멸기 시퀀스가 점유한 턴에는 배치하지 않는다</b>(§21.5 사용자 확정). 런타임 다량
    ///       배치는 저작으로 안전지대 틈을 보장할 수 없으므로, 시퀀스 중 배치 금지가 그 계약
    ///       ("반드시 도달할 수 있는 안전지대")의 방어선이다.</item>
    /// <item>⚠️ 양보한 턴의 쿨다운은 소모하지 않는다(철조각 규칙) — 소모하면 배치가 계속 뒤로 밀려
    ///       결국 사라진다. "대기"여야 하고, 시퀀스가 끝난 다음 턴에 곧바로 배치된다.</item>
    /// </list>
    ///
    /// 보스 id를 모른다 — 대상은 컨텍스트가 주고, 수치는 전부 <c>mechanicParams</c> 저작이다.
    /// </summary>
    internal sealed class TrapVolleyMechanic : IBossMechanic
    {
        public string MechanicId => "trap-volley";

        public IReadOnlyList<string> RequiredParamKeys { get; } = new[]
        {
            TrapVolleyMechanicParams.PhaseMin,
            TrapVolleyMechanicParams.IntervalTurns,
            TrapVolleyMechanicParams.CountByPhase,
            TrapVolleyMechanicParams.RingRadius,
            TrapVolleyMechanicParams.MinSpacing,
            TrapVolleyMechanicParams.EffectKind,
            TrapVolleyMechanicParams.EffectAmount,
            TrapVolleyMechanicParams.EffectDurationTurns,
            TrapVolleyMechanicParams.MaxArmed,
            TrapVolleyMechanicParams.GuardBlock
        };

        /// <summary>배치를 허용하는 효과 종류. 🔴 <c>Stun</c>은 안전지대 도달 보장(거리 = 이동력 전제)을
        /// 깨므로 금지(§18.5 판정) · <c>Burn</c>은 폐기 · <c>SpawnMonsters</c>/<c>InjectStatusCard</c>는
        /// 전용 저작 컬럼(monsterDefinitionId·statusCardId)이 이 기믹에 배관되어 있지 않아 거부한다.</summary>
        private static readonly HexTrapEffectKind[] AllowedEffectKinds =
        {
            HexTrapEffectKind.Damage,
            HexTrapEffectKind.Poison,
            HexTrapEffectKind.Slow,
            HexTrapEffectKind.VisionDown,
            HexTrapEffectKind.Teleport,
            HexTrapEffectKind.Weaken,
            HexTrapEffectKind.Disarm
        };

        public void ValidateParams(IReadOnlyDictionary<string, string> mechanicParams, int phaseCount)
        {
            var phaseMin = RequireInt(mechanicParams, TrapVolleyMechanicParams.PhaseMin);
            if (phaseMin < 1 || phaseMin > phaseCount)
            {
                throw new ArgumentException(
                    $"mechanicParams '{TrapVolleyMechanicParams.PhaseMin}' ({phaseMin}) must be within 1..{phaseCount} (phase count).");
            }

            RequirePositive(mechanicParams, TrapVolleyMechanicParams.IntervalTurns);
            RequirePositive(mechanicParams, TrapVolleyMechanicParams.RingRadius);
            RequirePositive(mechanicParams, TrapVolleyMechanicParams.MinSpacing);
            RequirePositive(mechanicParams, TrapVolleyMechanicParams.MaxArmed);

            mechanicParams.TryGetValue(TrapVolleyMechanicParams.CountByPhase, out var countValue);
            var countByPhase = BossProfileDefinition.ParseIntList(countValue, TrapVolleyMechanicParams.CountByPhase);
            if (countByPhase.Count != phaseCount)
            {
                throw new ArgumentException(
                    $"'{TrapVolleyMechanicParams.CountByPhase}' must have exactly one entry per phase " +
                    $"(expected {phaseCount}, got {countByPhase.Count}).");
            }

            for (var i = 0; i < countByPhase.Count; i++)
            {
                if (countByPhase[i] < 0)
                {
                    throw new ArgumentException(
                        $"'{TrapVolleyMechanicParams.CountByPhase}' phase {i + 1} count cannot be negative.");
                }
            }

            var kind = RequireEffectKind(mechanicParams);
            if (Array.IndexOf(AllowedEffectKinds, kind) < 0)
            {
                throw new ArgumentException(
                    $"'{TrapVolleyMechanicParams.EffectKind}' ({kind}) is not allowed for boss trap volleys — " +
                    "Stun은 안전지대 도달 보장을 깨고(§18.5) · Burn은 폐기 · SpawnMonsters/InjectStatusCard는 전용 저작 컬럼이 배관되어 있지 않다. " +
                    $"허용: {string.Join(", ", AllowedEffectKinds)}.");
            }

            var amount = RequireInt(mechanicParams, TrapVolleyMechanicParams.EffectAmount);
            var duration = RequireInt(mechanicParams, TrapVolleyMechanicParams.EffectDurationTurns);
            if (amount < 0 || duration < 0)
            {
                throw new ArgumentException(
                    $"'{TrapVolleyMechanicParams.EffectAmount}'/'{TrapVolleyMechanicParams.EffectDurationTurns}' cannot be negative.");
            }

            // HexTrapEffectData.IsConfigured와 같은 조건 — 여기서 막지 않으면 "밟아도 아무 일 없는" 함정이
            // 파싱을 통과해 런타임에서 조용히 걸러진다(배치 개수만 소모하는 유령 저작).
            if (amount <= 0 && duration <= 0)
            {
                throw new ArgumentException(
                    $"'{TrapVolleyMechanicParams.EffectAmount}'와 '{TrapVolleyMechanicParams.EffectDurationTurns}'가 둘 다 0이면 밟아도 아무 일이 일어나지 않는 함정이다.");
            }

            if (RequireInt(mechanicParams, TrapVolleyMechanicParams.GuardBlock) < 0)
            {
                throw new ArgumentException($"'{TrapVolleyMechanicParams.GuardBlock}' cannot be negative.");
            }
        }

        public void Resolve(BossMechanicContext context)
        {
            if (context.CurrentPhase < context.Profile.GetMechanicInt(TrapVolleyMechanicParams.PhaseMin))
            {
                return;
            }

            // 🔴 전멸기 시퀀스가 점유한 턴에는 배치하지 않는다(§21.5). ⚠️ 쿨다운을 소모하기 <b>전에</b>
            // 빠져나온다(철조각 ②-0과 같은 형태) — 소모하면 배치가 계속 뒤로 밀려 결국 사라진다.
            if (context.WillAnnihilationOccupyThisTurn)
            {
                return;
            }

            if (context.TrapVolleyCooldownTurns > 0)
            {
                context.TrapVolleyCooldownTurns--;
                return;
            }

            // 이번 턴이 주기의 첫 턴이므로 남은 대기는 interval - 1(철조각과 같은 off-by-one 교훈).
            context.TrapVolleyCooldownTurns =
                context.Profile.GetMechanicInt(TrapVolleyMechanicParams.IntervalTurns) - 1;

            var count = ResolveVolleyCount(context);
            if (count <= 0)
            {
                return;
            }

            if (context.SpawnTrapVolley(
                    count,
                    context.Profile.GetMechanicInt(TrapVolleyMechanicParams.RingRadius),
                    context.Profile.GetMechanicInt(TrapVolleyMechanicParams.MinSpacing),
                    BuildEffect(context)) > 0)
            {
                // 배치가 실제로 일어난 턴을 래치한다 — 공격 억제 질의가 결의 뒤에도 같은 답을 내려면
                // 쿨다운이 아니라 이 값을 봐야 한다(철조각·전멸기와 같은 래치 규약).
                context.MarkTrapVolleyCastThisTurn();

                // §21.8 제안 4: 배치하며 몸을 굳힌다. 배치가 <b>실제로</b> 일어났을 때만 — 자리가 없어
                // 한 개도 못 놓은 턴에 방어막만 얻으면 "심는 대가로 굳는다"는 교환이 공짜가 된다.
                var guardBlock = context.Profile.GetMechanicInt(TrapVolleyMechanicParams.GuardBlock);
                if (guardBlock > 0)
                {
                    context.AddBossBlock(guardBlock);
                }
            }
        }

        /// <summary>
        /// 배치는 그 턴의 보스 <b>행동</b>이다 — 배치한 턴에는 일반 공격을 하지 않는다(철조각 살포와
        /// 같은 문법: "판에 무언가를 까는 턴"은 그 자체가 행동이다). 결의 전(예고 커밋)과 결의 후
        /// 모두에서 같은 답이 나와야 하므로 래치 + 순수 질의 두 항이다.
        /// </summary>
        public bool SuppressesMonsterAttackThisTurn(BossMechanicContext context)
        {
            if (context.DidCastTrapVolleyThisTurn)
            {
                return true;
            }

            // 전멸기에 양보한 턴에는 배치가 없으므로 공격도 막지 않는다(철조각과 같은 항 — 없으면
            // 일어나지 않을 배치 때문에 보스가 빈 턴을 보낸다).
            if (context.WillAnnihilationOccupyThisTurn)
            {
                return false;
            }

            if (context.CurrentPhase < context.Profile.GetMechanicInt(TrapVolleyMechanicParams.PhaseMin)
                || context.TrapVolleyCooldownTurns > 0)
            {
                return false;
            }

            return ResolveVolleyCount(context) > 0;
        }

        /// <summary>이번 페이즈에 실제로 놓으려 시도할 함정 수(maxArmed 가드로 자른 값).</summary>
        private static int ResolveVolleyCount(BossMechanicContext context)
        {
            var countByPhase = context.Profile.GetMechanicIntList(TrapVolleyMechanicParams.CountByPhase);
            var requested = countByPhase.Count == 0
                ? 0
                : countByPhase[Math.Min(Math.Max(context.CurrentPhase, 1), countByPhase.Count) - 1];

            var armed = context.CountArmedRuntimeTraps();
            var maxArmed = context.Profile.GetMechanicInt(TrapVolleyMechanicParams.MaxArmed);
            return Math.Min(requested, Math.Max(0, maxArmed - armed));
        }

        private static HexTrapEffectData BuildEffect(BossMechanicContext context)
        {
            var kindValue = context.Profile.GetMechanicString(TrapVolleyMechanicParams.EffectKind);
            if (!Enum.TryParse<HexTrapEffectKind>(kindValue, out var kind))
            {
                // ValidateParams가 이미 막았으므로 정상 경로에서는 도달하지 않는다(방어 가드).
                return default;
            }

            return new HexTrapEffectData(
                kind,
                context.Profile.GetMechanicInt(TrapVolleyMechanicParams.EffectAmount),
                context.Profile.GetMechanicInt(TrapVolleyMechanicParams.EffectDurationTurns));
        }

        private static HexTrapEffectKind RequireEffectKind(IReadOnlyDictionary<string, string> mechanicParams)
        {
            if (!mechanicParams.TryGetValue(TrapVolleyMechanicParams.EffectKind, out var raw)
                || !Enum.TryParse<HexTrapEffectKind>(raw?.Trim(), out var kind))
            {
                throw new ArgumentException(
                    $"mechanicParams '{TrapVolleyMechanicParams.EffectKind}' must be a HexTrapEffectKind member name (got '{raw}').");
            }

            return kind;
        }

        private static void RequirePositive(IReadOnlyDictionary<string, string> mechanicParams, string key)
        {
            if (RequireInt(mechanicParams, key) <= 0)
            {
                throw new ArgumentException($"'{key}' must be a positive integer.");
            }
        }

        private static int RequireInt(IReadOnlyDictionary<string, string> mechanicParams, string key)
        {
            if (!mechanicParams.TryGetValue(key, out var raw)
                || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new ArgumentException($"mechanicParams '{key}' must be an integer.");
            }

            return value;
        }
    }
}

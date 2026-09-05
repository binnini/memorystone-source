using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// P2.7 묶음 D의 독립 두 항목: 횃불(C-14 / D-15)과 함정 해제(C-13 / D-14).
    /// 상태 카드·봉인과 달리 서로 의존하지 않아 나란히 붙었다.
    /// </summary>
    public sealed partial class CombatState
    {
        // ------------------------------------------------------------------ C-14 횃불

        /// <summary>
        /// 등불 반경 상한. 획득 표면이 둘(유틸리티 카드 · 소모품)이고 재적용이 병합되므로, 저작이나
        /// 수치 보정으로 반경이 무한정 자라면 암시야가 무의미해진다 — 부여 지점에서 한 번 자른다.
        /// 병합은 Max이므로(<c>AddDurationStatusEffectCore</c>) 여기서 자르면 합쳐진 값도 넘지 못한다.
        /// 지속시간 = 반경이라는 계약 때문에 이 상한은 <b>턴 수 상한이기도 하다</b>.
        /// </summary>
        internal const int MaxTorchLightRadius = 3;

        /// <summary>
        /// 횃불 부여의 <b>단일 지점</b>. 획득 표면이 둘(유틸리티 카드 · 필드 오브젝트)이라 규칙을
        /// 양쪽에 복사하면 한쪽만 고쳐지는 날이 온다.
        ///
        /// 🔑<b>지속시간 = 초기 반경</b>으로 건다. 횃불은 매 턴 반경이 1씩 줄어드는데(D-15), 수명과
        /// 밝기를 따로 저작하게 두면 "반경 3인데 5턴 간다"처럼 <b>둘 중 하나가 무의미해지는</b> 저작이
        /// 가능해진다. 여기서 묶어 두면 두 시계가 항상 함께 0에 닿는다.
        /// </summary>
        internal void GrantTorchLight(int radius, string sourceRef)
        {
            var amount = Math.Min(MaxTorchLightRadius, Math.Max(0, radius));
            if (amount <= 0)
            {
                return;
            }

            AddDurationStatusEffect(StatusEffectKind.TorchLight, PlayerUnitId, amount, amount, sourceRef);
            RaiseStatusEffect(StatusEffectKind.TorchLight, PlayerCoord, 0, amount, PlayerUnitId, sourceRef);
        }


        // ------------------------------------------------------------------ C-13 함정 해체

        /// <summary>
        /// 범위 안의 <b>발견된</b> 함정을 전부 해체한다. 소진 처리는 기존 배관을 그대로 탄다:
        /// <c>consumedTrapIds</c>에 넣고 발견 표시를 지운다 — 밟아서 소모된 일회성 함정과 완전히 같은
        /// 종착점이라 세이브 왕복도 이미 되어 있다. 좌표당 한 번 「해체!」 텍스트를 함정 자리에 띄운다
        /// (<see cref="EffectKind.TrapDisarmed"/> — 무VFX 텍스트 전용 kind, FogReveal 차용은
        /// EveryScoutCardSharesTheSameRevealCueAndPacing 계약에 걸려 기각된 전력).
        /// </summary>
        internal void DisarmRevealedTrapsInArea(HexCoord center, int radius, string sourceCardId)
        {
            var disarmedCoords = new List<HexCoord>();
            foreach (var trap in AllTrapRefs)
            {
                if (center.DistanceTo(trap.Coord) > Math.Max(0, radius)
                    || consumedTrapIds.Contains(trap.TrapId)
                    || !visibilityRuntime.IsTrapRevealed(trap.Coord))
                {
                    continue;
                }

                consumedTrapIds.Add(trap.TrapId);
                if (!disarmedCoords.Contains(trap.Coord))
                {
                    disarmedCoords.Add(trap.Coord);
                }
            }

            foreach (var coord in disarmedCoords)
            {
                visibilityRuntime.ClearTrapReveal(coord);
                RaiseEffect(
                    EffectKind.TrapDisarmed,
                    coord,
                    0,
                    0,
                    PlayerUnitId,
                    sourceCardId,
                    sourceUnitId: PlayerUnitId,
                    sourceActorKind: "player",
                    sourceCardId: sourceCardId);
            }
        }
    }
}

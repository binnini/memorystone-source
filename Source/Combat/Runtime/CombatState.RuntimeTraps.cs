using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 런타임 함정 레지스트리(3-A · 2026-09-04, 옛 <c>CombatState.BossTraps.cs</c>에서 본문 무변경 이동).
    /// 맵 저작 함정(불변) 위에 전투가 소유하는 가변 목록을 한 겹 얹는다 — 트리거·주기 발동·발견·해체·소진은 전부
    /// <see cref="AllTrapRefs"/>를 읽으므로 두 출처의 함정이 같은 규칙을 탄다. 지금 유일한 공급자는 보스
    /// trap-volley 기믹(<c>CombatState.BossTraps.cs</c>)이지만 레지스트리 자체는 보스를 모른다 — 서스펜드 왕복·정찰 발견·
    /// 소진 기록이 전부 코어 관심사라 「보스」 파일에 둘 이유가 없었다.
    /// </summary>
    public sealed partial class CombatState
    {
        private readonly List<HexTrapData> runtimeTrapRefs = new List<HexTrapData>();

        /// <summary>
        /// 런타임 함정 id 시퀀스. 서스펜드 왕복 대상 — 왕복하지 않으면 재개 후 새 함정이 살아 있는
        /// 함정과 같은 id를 받아 소진 기록(<c>consumedTrapIds</c>)이 엉킨다(runtimeSpawnSequence 선례).
        /// </summary>
        private int runtimeTrapSequence;

        /// <summary>
        /// 규칙·표현이 읽는 함정 전체 = 맵 저작 함정 + 런타임(보스 배치) 함정. 함정을 소비하는 코드는
        /// <c>Map.TrapRefs</c>가 아니라 반드시 이것을 읽어야 한다 — 맵만 읽으면 보스 배치 함정이
        /// 그 소비처에서만 조용히 존재하지 않는 갭이 된다.
        /// </summary>
        public IEnumerable<HexTrapData> AllTrapRefs => Map.TrapRefs.Concat(runtimeTrapRefs);

        /// <summary>런타임(보스 배치) 함정 목록(서스펜드 왕복 대상).</summary>
        public IReadOnlyList<HexTrapData> RuntimeTrapRefs => runtimeTrapRefs;

        /// <summary>아직 소진되지 않은 런타임 함정 수(볼리 상한 가드용).</summary>
        internal int CountArmedRuntimeTraps()
        {
            return runtimeTrapRefs.Count(trap => !(trap.OneShot && consumedTrapIds.Contains(trap.TrapId)));
        }

        /// <summary>
        /// 런타임 함정 한 개를 판에 추가한다. <b>발견 상태로 만들지 않는다</b> — 저작 함정과 같이
        /// 정찰이 유일한 발견 경로다(파일 헤더의 번복 결정 참조). 밟기형 · 일회성 · 반경 0 ·
        /// 플레이어 전용으로 고정한다 — 저작 축을 늘리는 것은 실플레이 판정 뒤의 일이다.
        /// </summary>
        private void AddRuntimeTrap(HexCoord coord, HexTrapEffectData effect)
        {
            string trapId;
            do
            {
                runtimeTrapSequence++;
                trapId = $"boss-trap-{runtimeTrapSequence}";
            } while (runtimeTrapRefs.Any(trap => string.Equals(trap.TrapId, trapId, StringComparison.Ordinal)));

            runtimeTrapRefs.Add(new HexTrapData(
                trapId,
                coord,
                radius: 0,
                effects: new[] { effect },
                affectsPlayer: true,
                affectsMonsters: false,
                oneShot: true,
                triggerOnEnter: true,
                periodTurns: 0));
        }

        /// <summary>
        /// 정찰 범위 안의 <b>런타임(보스 배치)</b> 함정을 발견 상태로 만든다 —
        /// <see cref="HexVisibilityRuntime.RevealTrapsInArea"/>(맵 저작 함정 전용)의 런타임 짝.
        /// 정찰 계열 진입점(<see cref="TryPlayerScout"/> · 랩 디버그)이 둘을 항상 나란히 불러야
        /// 두 출처의 함정이 같은 발견 규칙을 탄다.
        /// </summary>
        private void RevealRuntimeTrapsInArea(HexCoord center, int radius)
        {
            if (radius < 0)
            {
                return;
            }

            foreach (var trap in runtimeTrapRefs)
            {
                if (center.DistanceTo(trap.Coord) <= radius)
                {
                    visibilityRuntime.RevealTrapAt(trap.Coord);
                }
            }
        }

        /// <summary>서스펜드 복원 전용: 런타임 함정 목록·시퀀스를 저장본 그대로 되살린다.</summary>
        internal void RestoreRuntimeTraps(IEnumerable<HexTrapData> traps, int sequence)
        {
            runtimeTrapRefs.Clear();
            if (traps != null)
            {
                runtimeTrapRefs.AddRange(traps.Where(trap => trap.IsConfigured));
            }

            runtimeTrapSequence = Math.Max(0, sequence);
        }
    }
}

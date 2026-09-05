using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 취약 부위 저장 슬롯. 보스는 <see cref="Boss.BossPhaseTrack"/>가, 형상 footprint 정예(2026-09-03)는
    /// <see cref="MonsterWeakSpotSlot"/>이 구현한다 — 규칙(<c>CombatState.BossWeakSpot.cs</c>)은 슬롯만 보므로
    /// 둘 사이에 분기가 없다. 남은 판명 턴 하나가 자리와 판명을 함께 지배한다는 계약(§20-A)도 그대로다.
    /// </summary>
    internal interface IWeakSpotSlot
    {
        int WeakSpotOffsetQ { get; }
        int WeakSpotOffsetR { get; }
        bool HasWeakSpot { get; }
        int WeakSpotKnownTurnsRemaining { get; set; }
        void SetWeakSpotOffset(int offsetQ, int offsetR);
        void ClearWeakSpot();
    }

    /// <summary>비보스 다중 칸 몬스터의 취약 부위 상태. 유닛 id로 <see cref="CombatState"/>가 든다(서스펜드 왕복 대상).</summary>
    internal sealed class MonsterWeakSpotSlot : IWeakSpotSlot
    {
        public int WeakSpotOffsetQ { get; private set; }
        public int WeakSpotOffsetR { get; private set; }
        public bool HasWeakSpot { get; private set; }
        public int WeakSpotKnownTurnsRemaining { get; set; }

        public void SetWeakSpotOffset(int offsetQ, int offsetR)
        {
            WeakSpotOffsetQ = offsetQ;
            WeakSpotOffsetR = offsetR;
            HasWeakSpot = true;
        }

        public void ClearWeakSpot()
        {
            WeakSpotOffsetQ = 0;
            WeakSpotOffsetR = 0;
            HasWeakSpot = false;
            WeakSpotKnownTurnsRemaining = 0;
        }
    }

    public sealed partial class CombatState
    {
        /// <summary>형상 footprint 정예의 취약 부위 슬롯(유닛 id 키). 보스는 여기 안 들어온다(트랙이 슬롯이다).</summary>
        private readonly Dictionary<string, MonsterWeakSpotSlot> monsterWeakSpots = new Dictionary<string, MonsterWeakSpotSlot>(StringComparer.Ordinal);

        /// <summary>
        /// 이 몬스터의 취약 부위 슬롯. 보스 → 페이즈 트랙, 형상 footprint 정예 → 슬롯(없으면 만들고 즉시 자리를 뽑는다 —
        /// 소환 직후 정찰해도 판명될 수 있게), 한 칸 몬스터 → null.
        /// </summary>
        private IWeakSpotSlot ResolveWeakSpotSlot(MonsterRuntime monster)
        {
            if (monster == null)
            {
                return null;
            }

            var track = HasBossPhaseTrack ? FindBossPhaseTrack(monster.Id) : null;
            if (track != null)
            {
                return track;
            }

            if (GetMonsterFootprintRadius(monster) > 0 || monster.FootprintShape == MonsterFootprintShape.Single)
            {
                return null;
            }

            if (!monsterWeakSpots.TryGetValue(monster.Id, out var slot))
            {
                slot = new MonsterWeakSpotSlot();
                monsterWeakSpots[monster.Id] = slot;
                ReselectWeakSpot(slot, monster);
            }

            return slot;
        }

        /// <summary>
        /// 비보스 다중 칸 몬스터의 취약 부위 진행. 보스는 <c>weak-spot</c> 기믹이 같은 규칙을 돌리므로
        /// 보스 기믹 결의 바로 뒤에 한 번 불린다 — 두 경로가 같은 몬스터 페이즈에 각각 한 번씩만 돈다.
        /// </summary>
        private void AdvanceMonsterWeakSpots()
        {
            foreach (var monster in monsters)
            {
                if (monster.Combatant.IsDead || IsBossMonster(monster))
                {
                    continue;
                }

                var slot = ResolveWeakSpotSlot(monster);
                if (slot == null)
                {
                    continue;
                }

                if (slot.WeakSpotKnownTurnsRemaining > 0)
                {
                    slot.WeakSpotKnownTurnsRemaining -= 1;
                    continue;
                }

                ReselectWeakSpot(slot, monster);
            }
        }
        // ── 슬롯 공통 재선정 알고리즘(3-A · 2026-09-04): 옛 CombatState.BossWeakSpot.cs에서 본문 무변경 이동.
        //    보스 트랙과 형상 정예 슬롯이 같은 함수를 지난다 — IWeakSpotSlot 곁이 제자리다.

        /// <summary>
        /// 슬롯 공통 재선정. 후보는 원판이면 가장자리 링(중심 제외), 형상 footprint(삼각형)면 <b>세 칸 전부</b>다 —
        /// 모델이 꼭짓점에 떠 있어 어느 칸도 「모델이 덮어 표시가 묻히는」 중심이 아니다. 후보가 없으면(한 칸) 지운다.
        /// </summary>
        private void ReselectWeakSpot(IWeakSpotSlot slot, MonsterRuntime monster)
        {
            slot.WeakSpotKnownTurnsRemaining = 0;

            var radius = GetMonsterFootprintRadius(monster);
            var shape = GetMonsterFootprintShape(monster);
            List<HexCoord> candidates;
            if (radius > 0)
            {
                candidates = EnumerateFootprintEdgeOffsets(radius).ToList();
            }
            else if (shape != MonsterFootprintShape.Single)
            {
                // P2 tri 보스(2026-09-04 §12)도 이 갈래로 들어온다 — 삼각형 정예와 같은 「세 칸 전부」 후보.
                candidates = MonsterFootprints.OffsetsOf(shape).ToList();
            }
            else
            {
                candidates = new List<HexCoord>();
            }

            if (candidates.Count == 0)
            {
                slot.ClearWeakSpot();
                return;
            }

            // 새 RNG를 만들지 않는다(IBossMechanic 계약) — 전투 RNG는 이미 네 곳에 흩어져 있어
            // 시드 재현이 불가능하고, 그래서 결과는 재현이 아니라 <b>저장</b>으로 지킨다(서스펜드 왕복).
            var picked = candidates[pushRng.Next(candidates.Count)];
            slot.SetWeakSpotOffset(picked.Q, picked.R);
        }

        /// <summary>반경 <paramref name="radius"/> 원판의 가장자리 링 오프셋(중심 거리 == 반경). 반경 1이면 6칸.</summary>
        private static IEnumerable<HexCoord> EnumerateFootprintEdgeOffsets(int radius)
        {
            for (var dq = -radius; dq <= radius; dq++)
            {
                var lower = Math.Max(-radius, -dq - radius);
                var upper = Math.Min(radius, -dq + radius);
                for (var dr = lower; dr <= upper; dr++)
                {
                    var offset = new HexCoord(dq, dr);
                    if (new HexCoord(0, 0).DistanceTo(offset) == radius)
                    {
                        yield return offset;
                    }
                }
            }
        }
    }
}

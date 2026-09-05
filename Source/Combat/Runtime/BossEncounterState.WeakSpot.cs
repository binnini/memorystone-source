using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>옛 <c>CombatState.BossWeakSpot.cs</c>의 본문(3-B · 본문 무변경 이동).</summary>
    internal sealed partial class BossEncounterState
    {
        /// <summary>취약 부위 피해 배수(%). 사용자 확정 상수 — 저작 노브를 두지 않는다(§20-A-5).</summary>
        internal const int BossWeakSpotDamagePercent = 200;

        /// <summary>정찰 한 번이 보장하는 판명 지속(몬스터 페이즈 수). 사용자 확정 상수.</summary>
        internal const int BossWeakSpotKnownTurns = 2;

        /// <summary>
        /// 이 몬스터의 취약 부위 실효 좌표. <b>판명 여부와 무관하게</b> 현재 자리를 돌려준다 —
        /// 정찰 판정(자기가 덮었는가)은 미판명 상태에서 물어야 하기 때문이다. 피해 배율은 이것이
        /// 아니라 <see cref="TryGetKnownBossWeakSpotCoord"/>를 봐야 한다.
        ///
        /// 저장은 <b>중심 기준 오프셋</b>이고 실효 좌표는 매번 <c>boss.Coord + offset</c>으로 유도한다:
        /// 절대 좌표로 저장하면 전멸기 중앙 점프(§20-B) 순간 취약 부위가 보스 몸 밖에 남는다.
        /// </summary>
        internal bool TryGetBossWeakSpotCoord(MonsterRuntime monster, out HexCoord coord)
        {
            coord = default;
            if (monster == null || monster.Combatant.IsDead)
            {
                return false;
            }

            var track = host.ResolveWeakSpotSlot(monster);
            if (track == null || !track.HasWeakSpot)
            {
                return false;
            }

            var candidate = new HexCoord(monster.Coord.Q + track.WeakSpotOffsetQ, monster.Coord.R + track.WeakSpotOffsetR);
            // 반경이 줄었거나(페이즈 저작 변경) 옛 세이브에서 온 오프셋이면 몸 밖일 수 있다.
            // 몸 밖 취약 부위는 의미가 없으므로 없는 것으로 취급한다(다음 결의에서 재선정된다).
            if (!host.IsMonsterOccupying(monster, candidate))
            {
                return false;
            }

            coord = candidate;
            return true;
        }

        /// <summary>
        /// <b>판명 중인</b> 취약 부위 좌표. 피해 배율·오버레이가 보는 유일한 표면이다.
        /// 미판명이면 false이므로 실제 피해도 미리보기도 100%로 수렴한다(§20-A-6의 누출 차단이
        /// 별도 분기가 아니라 <b>같은 함수 하나</b>로 성립하는 이유).
        /// </summary>
        internal bool TryGetKnownBossWeakSpotCoord(MonsterRuntime monster, out HexCoord coord)
        {
            coord = default;
            if (monster == null)
            {
                return false;
            }

            var track = host.ResolveWeakSpotSlot(monster);
            return track != null
                && track.WeakSpotKnownTurnsRemaining > 0
                && TryGetBossWeakSpotCoord(monster, out coord);
        }

        /// <summary>
        /// 이 공격이 <paramref name="monster"/>의 취약 부위를 때렸을 때의 배수(%). 판명 중이 아니거나
        /// 커버 칸이 취약 부위를 포함하지 않으면 100이다.
        ///
        /// AoE·shape는 커버 칸 중 <b>하나라도</b> 취약 부위면 취약타다 — "AoE는 어디를 맞은 건가"의
        /// 모호함을 없애고 넓은 카드에 약간의 보상을 준다(§20-A-5).
        /// </summary>
        internal int ResolveBossWeakSpotDamagePercent(MonsterRuntime monster, AttackCoverage coverage)
        {
            return TryGetKnownBossWeakSpotCoord(monster, out var weakSpot) && coverage.Covers(weakSpot)
                ? BossWeakSpotDamagePercent
                : 100;
        }

        /// <summary>
        /// 이 몬스터에게 취약 부위가 <b>저작되어 있는가</b>(판명 여부 무관 · T7-1 툴팁 어휘 고지용).
        /// 판명된 좌표·배율은 <see cref="GetBossWeakSpots"/>가 정본이다 — 이 술어는 좌표를 내주지
        /// 않으므로 §20-A-6의 미판명 누출 차단을 깨지 않는다.
        /// </summary>
        public bool MonsterHasBossWeakSpot(string monsterId)
        {
            if (string.IsNullOrEmpty(monsterId))
            {
                return false;
            }

            var monster = host.Monsters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, monsterId, StringComparison.Ordinal));
            return monster != null && TryGetBossWeakSpotCoord(monster, out _);
        }

        /// <summary>
        /// 살아 있는 보스들의 <b>판명된</b> 취약 부위(표현용). 미판명이면 아무것도 나오지 않는다 —
        /// <c>?</c>조차 띄우지 않는다(그건 "저기 어딘가"를 알려주는 셈이고, <c>?</c>는 전멸기 후보
        /// 전용 어휘다 · §20-B-6).
        /// </summary>
        public IReadOnlyList<BossWeakSpotState> GetBossWeakSpots()
        {
            List<BossWeakSpotState> result = null;
            // 보스(트랙)와 형상 footprint 정예(슬롯)를 한 순회로 — 살아 있는 몬스터가 정본이다.
            foreach (var boss in host.Monsters)
            {
                if (boss.Combatant.IsDead)
                {
                    continue;
                }

                var track = host.ResolveWeakSpotSlot(boss);
                if (track == null || track.WeakSpotKnownTurnsRemaining <= 0 || !TryGetBossWeakSpotCoord(boss, out var coord))
                {
                    continue;
                }

                if (host.Map != null && !host.Map.Contains(coord))
                {
                    continue;
                }

                result ??= new List<BossWeakSpotState>();
                result.Add(new BossWeakSpotState(
                    boss.Id,
                    coord,
                    track.WeakSpotKnownTurnsRemaining,
                    BossWeakSpotDamagePercent));
            }

            return (IReadOnlyList<BossWeakSpotState>)result ?? Array.Empty<BossWeakSpotState>();
        }

        /// <summary>판명된 취약 부위 칸들(오버레이 레이어 입력).</summary>
        public IReadOnlyList<HexCoord> BossWeakSpotCoords =>
            GetBossWeakSpots().Select(state => state.Coord).ToList();

        /// <summary>
        /// 매 몬스터 페이즈에 한 번 도는 취약 부위 진행(<c>weak-spot</c> 기믹이 부른다).
        ///
        /// <list type="bullet">
        /// <item>판명 중(<c>&gt; 0</c>)이면 <b>자리를 고정</b>하고 카운터만 1 줄인다.</item>
        /// <item>미판명(<c>== 0</c>)이면 <b>자리를 다시 뽑는다</b>.</item>
        /// </list>
        ///
        /// 정찰(플레이어 턴, 2로 세팅) → 이번 몬스터 페이즈 감소(2→1) → 다음 플레이어 턴에 1이
        /// 남아 여전히 판명 → 그 턴 몬스터 페이즈에 0 → 그 다음 결의에서 재선정. 이것이 "총 2턴"이다.
        /// </summary>
        internal void AdvanceBossWeakSpot(BossPhaseTrack track, MonsterRuntime boss)
        {
            if (track == null || boss == null)
            {
                return;
            }

            if (track.WeakSpotKnownTurnsRemaining > 0)
            {
                track.WeakSpotKnownTurnsRemaining -= 1;
                return;
            }

            ReselectBossWeakSpot(track, boss);
        }

        /// <summary>
        /// 취약 부위를 다시 뽑는다. 후보는 몸통 원판의 <b>가장자리 링</b>(중심 거리 == 반경)이며
        /// 중심은 제외한다 — 모델이 덮고 있어 표시가 묻히고 "옆구리를 노린다"가 읽히지 않는다.
        ///
        /// 재선정은 판명 0과 <b>같은 사건</b>이다(한 카운터가 둘을 지배하므로 구조적으로 어긋날 수 없다).
        /// 반경 0(1페이즈)이면 취약 부위 자체가 없다 — 몸이 한 칸이면 "어느 부위"가 성립하지 않는다.
        /// </summary>
        internal void ReselectBossWeakSpot(BossPhaseTrack track, MonsterRuntime boss)
        {
            if (track == null || boss == null)
            {
                return;
            }

            host.ReselectWeakSpot(track, boss);
        }

        /// <summary>
        /// 정찰이 취약 부위를 덮으면 판명한다(<b>유일한 판명 경로</b> · §20-A-4).
        ///
        /// 🔴 "때려서 알아내기"는 없다 — 남겨두면 다단 히트 카드가 정찰을 대체한다(1타로 판명되고
        /// 2타부터 2배가 붙으면 정찰 카드를 넣을 이유가 사라진다).
        ///
        /// ⚠️ 암시야 가시성과 분리한다: <c>visibilityRuntime</c>의 정찰 밝힘은 다음 턴에 Hinted로
        /// 감쇠하는 <b>지형 지식</b>이다. 판명을 거기에 얹으면 아레나가 밝은 동안 공짜가 된다.
        ///
        /// 판명 중에 다시 정찰하면 카운터를 갱신한다(자리는 그대로). 낭비지만 플레이어의 선택이다.
        /// </summary>
        internal void RevealBossWeakSpotsInScoutArea(HexCoord target, int revealRadius)
        {
            var radius = Math.Max(0, revealRadius);
            foreach (var boss in host.Monsters)
            {
                if (boss.Combatant.IsDead)
                {
                    continue;
                }

                var track = host.ResolveWeakSpotSlot(boss);
                if (track == null || !TryGetBossWeakSpotCoord(boss, out var coord))
                {
                    continue;
                }

                if (target.DistanceTo(coord) <= radius)
                {
                    track.WeakSpotKnownTurnsRemaining = BossWeakSpotKnownTurns;
                }
            }
        }
    }
}

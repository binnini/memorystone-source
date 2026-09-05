using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>옛 <c>CombatState.BossTraps.cs</c>의 본문(3-B · 본문 무변경 이동).</summary>
    internal sealed partial class BossEncounterState
    {
        /// <summary>
        /// 마지막 함정 볼리의 요청/실제 배치 기록(랩 관찰용 · <c>LastBossPropVolleyReport</c>와 같은
        /// 규약). 최소 간격 + 좁은 아레나에서 자리 부족은 실제로 일어나고, 기록이 없으면
        /// "왜 3개가 아니라 2개지"를 추적할 방법이 없다. 표현/디버그 전용이며 규칙은 읽지 않는다.
        /// </summary>
        public string LastBossTrapVolleyReport { get; private set; } = string.Empty;

        /// <summary>
        /// 보스가 함정 한 볼리를 배치한다. 실제로 놓인 개수를 돌려준다(요청보다 적을 수 있다).
        ///
        /// 배치 규칙은 기물 볼리(<see cref="SpawnBossPropVolley"/>)의 미러다: 아레나 중심(없으면 보스
        /// 좌표) 기준 반경 <paramref name="ringRadius"/> 링을 임의 각도로 회전시켜 균등 분할하고,
        /// 서로 <paramref name="minSpacing"/>칸 이상 간격을 그리디로 강제하며, 자리가 모자라면
        /// <b>간격을 낮추지 않고 개수를 줄인다</b>. 후보에서 플레이어 현재 칸·점유 칸·이미 무장된
        /// 함정 칸은 뺀다.
        /// </summary>
        internal int SpawnBossTrapVolley(
            string ownerBossUnitId,
            int count,
            int ringRadius,
            int minSpacing,
            HexTrapEffectData effect)
        {
            if (count <= 0 || !effect.IsConfigured)
            {
                return 0;
            }

            var owner = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, ownerBossUnitId, StringComparison.Ordinal));
            if (owner == null)
            {
                return 0;
            }

            var anchor = ResolveBossPropAnchor(owner.Coord);
            var radius = Math.Max(1, ringRadius);
            var spacing = Math.Max(1, minSpacing);

            // 링이 막혀 있을 수 있으므로 한 겹 안팎까지 후보로 받는다(기물 볼리와 같은 여유).
            var band = HexArea.CellsInBand(anchor, Math.Max(1, radius - 1), radius + 1)
                .Where(candidate => host.IsSpawnableCoord(candidate)
                                    && IsInsideActiveBossArena(candidate)
                                    && !HasArmedTrapAt(candidate))
                .ToList();

            var rotation = bossPropRng.NextDouble() * 2 * Math.PI;
            var placedCoords = new List<HexCoord>();
            var placed = 0;
            for (var slot = 0; slot < count; slot++)
            {
                var targetAngle = rotation + (2 * Math.PI * slot / count);
                var chosen = band
                    .Where(candidate => placedCoords.All(taken => taken.DistanceTo(candidate) >= spacing))
                    .OrderBy(candidate => HexArea.AngleDistance(HexArea.AngleFrom(anchor, candidate), targetAngle))
                    .ThenBy(candidate => Math.Abs(anchor.DistanceTo(candidate) - radius))
                    .ThenBy(candidate => candidate)
                    .Select(candidate => (HexCoord?)candidate)
                    .FirstOrDefault();
                if (!chosen.HasValue)
                {
                    // 남은 자리가 간격 조건을 만족하지 못한다. 간격을 깎느니 이 슬롯을 버린다.
                    continue;
                }

                host.AddRuntimeTrap(chosen.Value, effect);
                placedCoords.Add(chosen.Value);
                placed++;
            }

            LastBossTrapVolleyReport = placed == count
                ? $"boss-trap volley: {placed}/{count} placed (anchor {anchor} · ringRadius {radius} · minSpacing {spacing})."
                : $"boss-trap volley TRUNCATED: {placed}/{count} placed (anchor {anchor} · ringRadius {radius} · minSpacing {spacing}); not enough spaced-out room.";
            return placed;
        }

        /// <summary>마지막 아레나 전역 함정 배치 기록(랩 관찰용 · <see cref="LastBossTrapVolleyReport"/>와 같은 규약).</summary>
        public string LastBossArenaTrapReport { get; private set; } = string.Empty;

        /// <summary>
        /// 살포 동반 함정(2026-09-05 결정 2): <paramref name="effects"/> 하나당 함정 하나를 봉인된 아레나
        /// <b>전역</b>에 심는다. 후보 = 아레나 안 스폰 가능 칸 − 플레이어 현재 칸 − 이미 무장된 함정 칸 −
        /// 살아있는 기물 칸(기물은 스폰 가능 판정이 이미 거른다). 아레나가 없으면(고정형 보스 등) 보스 기준
        /// 반경 4 원판으로 폴백한다. 자리는 기물 RNG로 뽑고 <paramref name="minSpacing"/> 간격을 그리디로
        /// 강제하며, 자리가 모자라면 <b>간격을 낮추지 않고 개수를 줄인다</b>(기물·옛 함정 볼리와 같은 규칙).
        /// 발견은 정찰 전까지 숨김 — <see cref="CombatState.AddRuntimeTrap"/>가 발견 상태로 만들지 않는다.
        /// </summary>
        internal int SpawnBossArenaTraps(string ownerBossUnitId, IReadOnlyList<HexTrapEffectData> effects, int minSpacing)
        {
            if (effects == null || effects.Count == 0)
            {
                return 0;
            }

            var owner = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, ownerBossUnitId, StringComparison.Ordinal));
            if (owner == null)
            {
                return 0;
            }

            var spacing = Math.Max(1, minSpacing);
            IEnumerable<HexCoord> area = SealedBossArenaCoords.Count > 0
                ? SealedBossArenaCoords
                : HexArea.CellsWithin(owner.Coord, 4);
            var candidates = area
                .Where(candidate => host.IsSpawnableCoord(candidate)
                                    && candidate != host.PlayerCoord
                                    && !HasArmedTrapAt(candidate))
                .OrderBy(candidate => candidate)
                .ToList();

            var placedCoords = new List<HexCoord>();
            var placed = 0;
            foreach (var effect in effects)
            {
                if (!effect.IsConfigured)
                {
                    continue;
                }

                var open = candidates
                    .Where(candidate => placedCoords.All(taken => taken.DistanceTo(candidate) >= spacing))
                    .ToList();
                if (open.Count == 0)
                {
                    // 남은 자리가 간격 조건을 만족하지 못한다. 간격을 깎느니 이 함정을 버린다.
                    continue;
                }

                var chosen = open[bossPropRng.Next(open.Count)];
                host.AddRuntimeTrap(chosen, effect);
                placedCoords.Add(chosen);
                placed++;
            }

            LastBossArenaTrapReport = placed == effects.Count
                ? $"boss-arena traps: {placed}/{effects.Count} placed (minSpacing {spacing} · candidates {candidates.Count})."
                : $"boss-arena traps TRUNCATED: {placed}/{effects.Count} placed (minSpacing {spacing} · candidates {candidates.Count}); not enough spaced-out room.";
            return placed;
        }

        /// <summary>기물 RNG에서 <c>[0, exclusiveMax)</c>를 뽑는다(저주 카드 풀 추첨 — 테스트 고정 RNG를 그대로 탄다).</summary>
        internal int NextBossPropRandom(int exclusiveMax)
        {
            return exclusiveMax <= 1 ? 0 : bossPropRng.Next(exclusiveMax);
        }

        /// <summary>
        /// 보스 위 알림 플로팅(2026-09-05 Q20). 특성 알림 채널(<see cref="EffectKind.MonsterTraitTriggered"/>)을
        /// 빌린다 — 결계 안은 영구 공개라 은신 가시성 필터는 지나지 않는다.
        /// </summary>
        internal void AnnounceBoss(MonsterRuntime boss, string sourceRef, int amount)
        {
            if (boss == null || string.IsNullOrEmpty(sourceRef) || boss.Combatant.IsDead)
            {
                return;
            }

            host.RaiseEffect(
                EffectKind.MonsterTraitTriggered,
                boss.Coord,
                0,
                Math.Max(0, amount),
                boss.Id,
                sourceRef,
                sourceUnitId: boss.Id,
                sourceActorKind: "monster",
                targetActorKind: "monster");
        }

        /// <summary>
        /// 보스 방어막 부여(§21.8 제안 4). Block은 <see cref="CombatantState.ApplyDamage"/>가 피해보다
        /// 먼저 소모하므로 부여 한 줄이면 흡수까지 성립한다. 서스펜드는 몬스터 Block이 이미 왕복된다.
        /// 연출은 플레이어 방어 획득과 같은 <see cref="EffectKind.Block"/> 이벤트다("+N" 플로팅).
        /// </summary>
        internal void GrantBossGuardBlock(MonsterRuntime boss, int amount)
        {
            if (boss == null || amount <= 0 || boss.Combatant.IsDead)
            {
                return;
            }

            boss.Combatant.AddBlock(amount);
            host.RaiseEffect(
                EffectKind.Block,
                boss.Coord,
                0,
                amount,
                boss.Id,
                CombatState.BossTrapSourceRefs.GuardBlock,
                sourceUnitId: boss.Id,
                sourceActorKind: "monster",
                targetActorKind: "monster");
        }
    }
}

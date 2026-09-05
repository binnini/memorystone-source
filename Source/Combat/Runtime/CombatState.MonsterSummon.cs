using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 패턴 소환(요괴 트랙 §4-4 · 구미호 A047). 스폰 자체는 기존 함수
    /// (<see cref="TrySpawnMonsterAt"/> + <see cref="UpdateOccupancy"/>)를 그대로 쓰고, 신설은
    /// <b>패턴에서 부르는 경로</b>와 <b>동시 상한</b>·<b>밀림 규칙</b>뿐이다.
    ///
    /// <para>🔴 <b>Q3 확정 — 소환 자리는 막을 수 없다.</b> 예고된 칸이 플레이어·몬스터·오브젝트로 막혀
    /// 있으면 근처 빈 칸으로 <b>밀려나 어쨌든 소환된다</b>. 자리를 막아서 소환을 무력화할 수 있으면
    /// "부르는 쪽을 먼저 처리한다"가 아니라 "자리에 서 있으면 된다"가 정답이 되어, 패턴이 위협이 아니라
    /// 퍼즐 한 줄로 쪼그라든다.</para>
    ///
    /// <para>🔴 밀림은 <b>결정적</b>이다(거리 1 → 2 순, 축좌표 정렬 첫 빈 칸). 무작위로 밀면 세이브 재개마다
    /// 같은 상황이 다르게 풀려 플레이어가 학습할 수 없다 — 스폰 함정이 자리를 결정적으로 고르는 것과 같은 이유다.</para>
    /// </summary>
    public sealed partial class CombatState
    {
        /// <summary>소환된 적의 역할. 함정 소환과 같은 취급 = <b>정식 위협</b>(승리 판정·보상·예고 포함).</summary>
        internal const string SummonSpawnRole = MonsterSpawnRoles.TrapSpawn;

        /// <summary>
        /// 소환물의 역할은 <b>소환 대상 자신의 저작</b>이 정한다: 카탈로그 archetype이 기물(boss-prop /
        /// player-prop)이면 그대로 기물이고, 그 밖에는 정식 위협(<see cref="SummonSpawnRole"/>)이다.
        ///
        /// <para>🔴 이 갈래가 없으면 <b>장애물을 부르는 패턴을 만들 수 없다</b>. 두두리의 「말뚝 세우기」가
        /// 정식 위협으로 세어지면 두두리를 잡고도 말뚝을 전부 부수기 전에는 전투가 끝나지 않는다 —
        /// 「막는 물건」이 「죽여야 하는 적」이 되어 패턴의 뜻이 뒤집힌다.</para>
        /// </summary>
        private string ResolveSummonSpawnRole(string definitionId)
        {
            if (TryGetMonsterCatalogEntry(definitionId, out var entry)
                && MonsterSpawnRoles.IsProp(entry.Archetype))
            {
                return entry.Archetype;
            }

            return SummonSpawnRole;
        }

        /// <summary>밀림 탐색 반경 상한. 이 안에도 자리가 없으면 그 한 마리는 포기한다(판이 꽉 찬 경우).</summary>
        private const int SummonPushOutRadius = 2;

        /// <summary>
        /// 이 패턴이 지금 동시 상한에 걸려 있는가. 계획 레이어가 후보에서 빼는 데 쓴다 —
        /// 상한을 집행 시점에만 보면 예고는 뜨는데 아무것도 안 나오는 턴이 생긴다(예고=명중 위반).
        /// </summary>
        private bool IsSummonPatternCapped(MonsterRuntime monster, MonsterAttackPattern pattern)
        {
            if (monster == null || !pattern.HasSummon)
            {
                return false;
            }

            return CountAliveSummons(monster, pattern.SummonMonsterDefinitionId) >= pattern.SummonMaxAlive;
        }

        /// <summary>이 소환자가 살려 둔 같은 종류의 적 수. 소유자를 보므로 다른 구미호의 여우불은 세지 않는다.</summary>
        private int CountAliveSummons(MonsterRuntime summoner, string definitionId)
        {
            return monsters.Count(candidate =>
                !candidate.Combatant.IsDead
                && string.Equals(candidate.DefinitionId, definitionId, StringComparison.Ordinal)
                && string.Equals(candidate.OwnerUnitId, summoner.Id, StringComparison.Ordinal));
        }

        /// <summary>
        /// 예고와 <b>같은 칸</b>을 돌려준다 — 소환 자리 표식(§4-4)과 실제 스폰이 갈라지지 않도록
        /// 계획·예고·집행이 이 함수 하나를 공유한다. 회전·몸 반경 보정은 형상 해소와 같은 규약이다.
        /// </summary>
        internal IReadOnlyList<HexCoord> GetSummonSeatCoords(MonsterRuntime monster, MonsterAttackPattern pattern)
        {
            if (monster == null || !pattern.HasSummon || string.IsNullOrEmpty(pattern.ShapeId))
            {
                return Array.Empty<HexCoord>();
            }

            var origin = monster.Coord;
            var bodyRadius = GetMonsterFootprintRadius(monster);
            var facingIntent = monster.TurnPlan.IsActive ? monster.TurnPlan.AttackFacingIntent : monster.LockedFacingIntent;
            var attackDir = origin.ApproximateDirection(facingIntent.PlayerCoord);
            for (var i = 0; i < bodyRadius; i++)
            {
                origin = origin.Neighbor(attackDir);
            }

            return AttackShapeLibrary
                .GetAffectedCells(pattern.ShapeId, monster.Coord, attackDir, bodyRadius)
                .Where(coord => Map.TryGetCell(coord, out _))
                .Take(Math.Max(0, pattern.SummonCount))
                .ToList();
        }

        /// <summary>
        /// 소환 집행. 공격이 <b>실제로 성립한</b> 자리에서 불린다(지대·방어막과 같은 규약).
        /// </summary>
        private void ResolveMonsterSummonPattern(MonsterRuntime monster, MonsterAttackPattern pattern)
        {
            if (monster == null || monster.Combatant.IsDead || !pattern.HasSummon)
            {
                return;
            }

            // 계획이 상한을 이미 봤지만 집행 시점에 다시 본다 — 같은 턴에 다른 소환자가 채웠을 수 있다.
            var budget = pattern.SummonMaxAlive - CountAliveSummons(monster, pattern.SummonMonsterDefinitionId);
            if (budget <= 0)
            {
                return;
            }

            UpdateOccupancy();
            var placed = 0;
            foreach (var seat in GetSummonSeatCoords(monster, pattern))
            {
                if (placed >= budget)
                {
                    break;
                }

                if (!TryResolveSummonCoord(seat, out var coord))
                {
                    continue;
                }

                if (!TrySpawnMonsterAt(
                        pattern.SummonMonsterDefinitionId,
                        coord,
                        ResolveSummonSpawnRole(pattern.SummonMonsterDefinitionId),
                        maxHp: 0,
                        monsterId: null,
                        out var spawnedId))
                {
                    continue;
                }

                var spawned = monsters.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, spawnedId, StringComparison.Ordinal));
                if (spawned != null)
                {
                    // 소유자를 남겨야 동시 상한이 "이 구미호가 부른 것"만 센다.
                    spawned.OwnerUnitId = monster.Id;
                }

                placed++;
                RaiseEffect(
                    EffectKind.FogReveal,
                    coord,
                    0,
                    0,
                    spawnedId,
                    ToMonsterPatternSourceRef(pattern),
                    sourceUnitId: monster.Id,
                    sourceActorKind: "monster",
                    targetActorKind: "monster",
                    sourcePatternId: pattern.Id);
            }

            if (placed > 0)
            {
                UpdateOccupancy();
            }
        }

        /// <summary>
        /// 🔴 Q3 밀림 규칙. 예고 칸이 비어 있으면 그대로, 막혀 있으면 <b>거리 1 → 2 순</b>으로 훑어
        /// 축좌표(q, r) 정렬 <b>첫</b> 빈 칸에 앉힌다. 무작위가 아니므로 같은 상황은 늘 같게 풀린다.
        /// </summary>
        private bool TryResolveSummonCoord(HexCoord seat, out HexCoord coord)
        {
            coord = seat;
            if (IsSpawnableCoord(seat))
            {
                return true;
            }

            for (var radius = 1; radius <= SummonPushOutRadius; radius++)
            {
                var ring = Map.AllCells
                    .Select(cell => cell.Coord)
                    .Where(candidate => seat.DistanceTo(candidate) == radius && IsSpawnableCoord(candidate))
                    .OrderBy(candidate => candidate.Q)
                    .ThenBy(candidate => candidate.R)
                    .ToList();
                if (ring.Count > 0)
                {
                    coord = ring[0];
                    return true;
                }
            }

            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 런타임 몬스터 스폰. 원래 <c>DebugSandboxSpawnMonsterAt</c>(디버그 전용)만 있던 경로를 프로덕션
    /// 표면으로 승격한 것이다 — 보스 기믹이 기물을 뿌리려면 정규 스폰이 필요하고, 검증(맵 안·걸을 수 있음·
    /// 비어 있음)을 한 곳에 모아 두어야 기믹마다 다시 틀리지 않는다.
    ///
    /// 정규 저작 스폰(맵 스폰 ref)과의 차이: 여기서 만든 몬스터는 <c>SpawnRefId</c>가 비어 있다.
    /// </summary>
    public sealed partial class CombatState
    {
        private const string RuntimeSpawnIdPrefix = "monster-runtime-";
        private int runtimeSpawnSequence;

        /// <summary>
        /// 몬스터 하나를 좌표에 스폰한다. 실패 사유는 <see cref="LastFailureReason"/>에 남는다.
        /// <paramref name="maxHp"/>가 0 이하면 카탈로그 Hp를 쓴다.
        /// </summary>
        internal bool TrySpawnMonsterAt(
            string definitionId,
            HexCoord coord,
            string spawnRole,
            int maxHp,
            string monsterId,
            out string spawnedId)
        {
            spawnedId = string.Empty;
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                LastFailureReason = "Spawn: no monster definition id given.";
                return false;
            }

            if (!IsSpawnableCoord(coord))
            {
                LastFailureReason = $"Spawn: {coord} is outside the map, not walkable, or already occupied.";
                return false;
            }

            var hp = maxHp;
            if (monsterCatalog != null && monsterCatalog.TryGetEntry(definitionId, out var entry))
            {
                if (hp <= 0)
                {
                    hp = entry.Hp;
                }

                // 형상 footprint(삼각형 정예)는 앵커만이 아니라 몸 전체가 설 수 있어야 한다.
                var offsets = MonsterFootprints.OffsetsOf(entry.FootprintShape);
                for (var i = 0; i < offsets.Count; i++)
                {
                    var bodyCoord = coord + offsets[i];
                    if (bodyCoord != coord && !IsSpawnableCoord(bodyCoord))
                    {
                        LastFailureReason = $"Spawn: body cell {bodyCoord} of '{definitionId}' is outside the map, not walkable, or already occupied.";
                        return false;
                    }
                }
            }
            else
            {
                LastFailureReason = $"Spawn: monster definition '{definitionId}' is not in the catalog.";
                return false;
            }

            var id = string.IsNullOrWhiteSpace(monsterId) ? CreateRuntimeSpawnMonsterId() : monsterId;
            if (monsters.Any(monster => string.Equals(monster.Id, id, StringComparison.Ordinal)))
            {
                LastFailureReason = $"Spawn: monster id '{id}' already exists.";
                return false;
            }

            var config = new MonsterConfig(
                id,
                coord,
                hp,
                monsterCatalog.SourceId,
                definitionId,
                spawnRefId: string.Empty,
                spawnRole: spawnRole ?? string.Empty);

            var before = monsters.Count;
            InitializeMonsters(new[] { config }, Config);
            if (monsters.Count == before)
            {
                LastFailureReason = $"Spawn: monster '{definitionId}' could not be created.";
                return false;
            }

            UpdateOccupancy();
            // 새로 태어난 몬스터 하나만 분류·계획한다. 전체 재분류(RefreshMonsterActivityStatesForAction)를
            // 부르면 안 된다: 기믹 스폰은 몬스터 행동 결의 창 안에서 일어나므로, 그 시점에 다른 몬스터의
            // 커밋된 TurnPlan이 초기화되거나 다시 세워질 수 있고 — 플레이어가 이미 본 예고와 실제 명중이
            // 어긋난다. 스폰은 다른 몬스터의 가시성·거리를 바꾸지 않으니 재분류할 이유도 없다.
            var spawned = monsters.FirstOrDefault(monster => string.Equals(monster.Id, id, StringComparison.Ordinal));
            if (spawned != null)
            {
                spawned.ActivityState = ClassifyMonsterActivity(spawned);
                if (spawned.ActivityState == MonsterActivityState.Dormant)
                {
                    spawned.PendingAttackIntent = false;
                    spawned.IntentPredictedMoveCoord = spawned.Coord;
                    spawned.TurnPlan = MonsterTurnPlan.Inactive(spawned.Coord);
                }
                else
                {
                    RefreshMonsterTurnPlan(spawned);
                }
            }

            spawnedId = id;
            LastFailureReason = string.Empty;
            return true;
        }

        /// <summary>맵 안 + 걸을 수 있음 + 이동 차단 오브젝트 없음 + 아무도 점유하지 않음.</summary>
        private bool IsSpawnableCoord(HexCoord coord)
        {
            return Map.TryGetCell(coord, out var cell)
                   && cell.BaseWalkable
                   && !Map.HasMovementBlockingObject(coord)
                   && !runtimeStates.ContainsKey(coord)
                   && coord != PlayerCoord;
        }

        /// <summary>
        /// 미사용 스폰 id. 카운터만 쓰면 서스펜드 복원 후(카운터는 0으로 돌아가지만 이미 그 id를 가진
        /// 몬스터가 되살아나 있다) 충돌해서 스폰이 조용히 실패한다 — 그래서 실제 사용 여부까지 확인한다.
        /// </summary>
        private string CreateRuntimeSpawnMonsterId()
        {
            while (true)
            {
                runtimeSpawnSequence++;
                var id = $"{RuntimeSpawnIdPrefix}{runtimeSpawnSequence}";
                if (!monsters.Any(monster => string.Equals(monster.Id, id, StringComparison.Ordinal)))
                {
                    return id;
                }
            }
        }


    }
}

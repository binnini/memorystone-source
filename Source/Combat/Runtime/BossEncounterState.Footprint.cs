using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>옛 <c>CombatState.BossFootprint.cs</c>의 본문(3-B · 본문 무변경 이동).</summary>
    internal sealed partial class BossEncounterState
    {
        /// <summary>
        /// 이 몬스터의 물리 점유 반경(0 = 단일 칸, 현행 모든 비보스 몬스터). 보스 페이즈 트랙이 있는
        /// 보스만 0이 아닐 수 있다. 핫패스(UpdateOccupancy·히트테스트)에서 매번 불리므로
        /// BossPhaseState 투영을 만들지 않고 트랙·프로필에서 직접 읽는다.
        /// </summary>
        internal int GetMonsterFootprintRadius(MonsterRuntime monster)
        {
            if (monster == null || bossPhaseTracks.Count == 0 || !CombatState.IsBossMonster(monster))
            {
                return 0;
            }

            var track = FindBossPhaseTrack(monster.Id);
            if (track == null || !bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
            {
                return 0;
            }

            return Math.Max(0, profile.GetPhase(track.CurrentPhase).FootprintRadius);
        }

        /// <summary>
        /// 페이즈 저작이 덮어쓰는 몸 <b>형상</b>(2026-09-04 §12 — 불가살 P2 tri). Single이면 덮지 않는다 —
        /// 최종 형상 유도는 <c>CombatState.GetMonsterFootprintShape</c>가 카탈로그 형상과 합쳐서 한다.
        /// 반경 게터와 같은 이유로 트랙·프로필에서 직접 읽는다(핫패스·투영 없음).
        /// </summary>
        internal MonsterFootprintShape GetPhaseFootprintShapeOverride(MonsterRuntime monster)
        {
            if (monster == null || bossPhaseTracks.Count == 0 || !CombatState.IsBossMonster(monster))
            {
                return MonsterFootprintShape.Single;
            }

            var track = FindBossPhaseTrack(monster.Id);
            if (track == null || !bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
            {
                return MonsterFootprintShape.Single;
            }

            return profile.GetPhase(track.CurrentPhase).FootprintShape;
        }

        /// <summary>
        /// 보스가 커지면서 플레이어를 몸통 안에 삼켰으면 밖으로 밀어낸다(§16.2). 페이즈 진입에서만 불린다 —
        /// 반경이 커지는 순간이 유일하게 겹침이 생기는 지점이다.
        ///
        /// ⚠️ 일반 넉백(<see cref="ApplyDirectionalKnockback"/>)으로는 못 민다: 몸통 칸이 전부 점유로
        /// 등록돼 있어 한 칸 나아가기도 전에 장애물로 막힌다. 그래서 <b>몸통 바로 바깥 링</b>에서
        /// 목적지를 직접 고르고 그리로 옮긴다(경로를 걷지 않는다 — 밀려나는 것은 몸 밖으로 튕기는 사건이다).
        /// 밀 자리가 없으면 겹친 채로 둔다: 억지로 먼 곳에 떨어뜨리는 것보다 낫고, 히트테스트·이동 계약은
        /// 겹친 상태에서도 성립한다(플레이어가 점유 주인, 보스는 멤버십으로 잡힘).
        /// </summary>
        private void PushPlayerOutOfGrownBossFootprint()
        {
            if (host.Player.IsDead)
            {
                return;
            }

            foreach (var monster in host.Monsters)
            {
                if (monster.Combatant.IsDead)
                {
                    continue;
                }

                // 원판·형상 공통 — 몸이 여러 칸으로 자라며 플레이어 칸을 점유 멤버십으로 삼켰는가
                // (tri 성장은 반경이 0 그대로라 반경 비교로는 안 잡힌다 · 2026-09-04 §12).
                if (!host.IsMultiCellMonster(monster) || !host.IsMonsterOccupying(monster, host.PlayerCoord))
                {
                    continue;
                }

                var landing = FindNearestCellOutsideFootprint(monster);
                if (!landing.HasValue)
                {
                    continue;
                }

                var from = host.PlayerCoord;
                host.SetPlayerCoord(landing.Value);
                host.UpdateOccupancy();
                host.ResolveTrapTriggersAt(host.PlayerCoord);
                host.RefreshPlayerVision();
                host.RaiseEffect(
                    EffectKind.Knockback,
                    from,
                    0,
                    from.DistanceTo(host.PlayerCoord),
                    CombatState.PlayerUnitId,
                    "knockback",
                    sourceUnitId: monster.Id,
                    sourceActorKind: "monster",
                    targetActorKind: "player");
                return;
            }
        }

        /// <summary>
        /// 커진 몸이 설 수 없는 자리를 덮었으면 보스를 가장 가까운 유효 중심으로 옮긴다(§22.5-1).
        /// 페이즈 진입에서만 불린다.
        ///
        /// <para>🔑 <b>성장은 이동이 아니다.</b> 그래서 걷기에 붙은 원판 클리어런스(§21.3)가 여기에는
        /// 걸리지 않는다 — 보스가 결계 링 옆이나 철조각 옆에 선 채로 페이즈가 오르면 커진 몸이 그것들을
        /// 덮는다. 걷기가 열린 §15.4 이후로 있던 결함이고, 반경이 바뀌는 이 지점이 유일한 발생점이다.</para>
        ///
        /// <para>⚠️ 플레이어는 <b>막지 않는다</b>. 바로 뒤 <see cref="PushPlayerOutOfGrownBossFootprint"/>가
        /// 밀어내므로 여기서까지 피하면 보스가 플레이어에게서 달아나는 그림이 된다. 순서가 규칙이다:
        /// 보스를 먼저 유효한 자리로 옮기고, 그 자리 기준으로 플레이어를 밀어낸다 — 반대로 하면
        /// 밀어낸 뒤 보스가 떠나 헛되이 함정만 밟힌다.</para>
        ///
        /// <para>밀 자리가 없으면 <b>겹친 채로 둔다</b>(<see cref="PushPlayerOutOfGrownBossFootprint"/>와
        /// 같은 태도). 억지로 먼 곳에 떨어뜨리면 "커졌다"가 아니라 "순간이동했다"로 읽히고, 겹친
        /// 상태에서도 히트테스트·이동 계약은 성립한다.</para>
        /// </summary>
        private void PullGrownBossIntoValidCentre(MonsterRuntime boss)
        {
            // 몸 도달 반경(원판=footprintRadius · tri=1). 탐색·연출 반경이 이 값에서 나온다.
            var reach = GetMonsterFootprintReach(boss);
            if (boss == null || boss.Combatant.IsDead || !host.IsMultiCellMonster(boss) || IsValidBossFootprintCentre(boss, boss.Coord))
            {
                LastBossGrowthRepositionReport = string.Empty;
                return;
            }

            // 탐색 반경 = 도달 + 2. 그 밖까지 밀려나야 하는 상황이면 아레나 자체가 몸보다 좁다는 뜻이라,
            // 더 멀리 찾는 것보다 겹친 채로 두는 편이 낫다.
            HexCoord? best = null;
            var bestKey = (int.MaxValue, 0, 0);
            foreach (var coord in HexArea.CellsWithin(boss.Coord, reach + 2))
            {
                if (!IsValidBossFootprintCentre(boss, coord))
                {
                    continue;
                }

                var key = (boss.Coord.DistanceTo(coord), coord.Q, coord.R);
                if (key.CompareTo(bestKey) >= 0)
                {
                    continue;
                }

                bestKey = key;
                best = coord;
            }

            if (!best.HasValue)
            {
                LastBossGrowthRepositionReport = "성장한 몸이 겹쳤으나 옮길 유효 중심이 없어 겹친 채로 둔다";
                return;
            }

            var from = boss.Coord;
            boss.Coord = best.Value;
            // 점유는 유도값이 아니라 캐시다 — 옮기고 다시 깔지 않으면 이동 판정만 낡은 자리를 가리킨다.
            host.UpdateOccupancy();
            LastBossGrowthRepositionReport = $"성장 겹침 해소: {from.Q},{from.R} → {boss.Coord.Q},{boss.Coord.R}";

            host.RaiseEffect(
                EffectKind.Knockback,
                boss.Coord,
                reach,
                from.DistanceTo(boss.Coord),
                boss.Id,
                BossGrowthRepositionSourceRef,
                sourceUnitId: boss.Id,
                sourceActorKind: "monster",
                targetActorKind: "monster");
        }

        /// <summary>
        /// 이 중심에 반경 <paramref name="radius"/>의 몸을 놓을 수 있는가. 걷기의 원판 클리어런스
        /// (<see cref="MovementQuery.FootprintRadius"/>)와 <b>같은 질문</b>이지만 경로탐색을 거치지 않는다 —
        /// 성장은 걷는 사건이 아니라 제자리 확대라 진입 비용도 경로도 의미가 없다.
        ///
        /// <para>막는 것: 맵 밖 · 못 걷는 지형 · 이동 차단 오브젝트 · 결계 링과 필드 오브젝트
        /// (<c>TemporaryBlocked</c>) · 다른 living 몬스터(철조각 포함). 막지 <b>않는</b> 것: 플레이어.</para>
        /// </summary>
        private bool IsValidBossFootprintCentre(MonsterRuntime boss, HexCoord centre)
        {
            // 원판·형상 공통 — 몸 칸 집합은 페이즈에서 유도된 오프셋(원판 디스크 또는 tri) 그대로다.
            foreach (var offset in host.GetMonsterFootprintOffsets(boss))
            {
                var coord = centre + offset;
                if (!host.Map.TryGetCell(coord, out var cell)
                    || !cell.BaseWalkable
                    || !host.TerrainTraits.IsWalkable(cell.TerrainTypeId)
                    || host.Map.HasMovementBlockingObject(coord)
                    || !IsInsideActiveBossArena(coord))
                {
                    return false;
                }

                // 결계 링과 필드 오브젝트는 여기로 들어온다(UpdateOccupancy가 TemporaryBlocked로 깐다).
                // ⚠️ 점유 주인(OccupyingUnitId)은 보지 않는다 — 자기 몸도 플레이어도 여기서는 통과여야 한다.
                if (host.RuntimeStates.TryGetValue(coord, out var state) && state.TemporaryBlocked)
                {
                    return false;
                }

                if (IsCoordOccupiedByLivingMonsterOtherThan(coord, boss))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>가장 최근 페이즈 성장에서의 재배치 결과(랩 관찰용). 재배치가 없었으면 빈 문자열.</summary>
        public string LastBossGrowthRepositionReport { get; private set; } = string.Empty;

        private const string BossGrowthRepositionSourceRef = "boss.growth.reposition";

        /// <summary>몸이 앵커에서 닿는 최대 거리(원판=반경 · tri=1 · 한 칸=0). 탐색·연출 반경의 밑값.</summary>
        private int GetMonsterFootprintReach(MonsterRuntime monster)
        {
            var reach = 0;
            var offsets = host.GetMonsterFootprintOffsets(monster);
            var origin = new HexCoord(0, 0);
            for (var i = 0; i < offsets.Count; i++)
            {
                reach = Math.Max(reach, origin.DistanceTo(offsets[i]));
            }

            return reach;
        }

        /// <summary>
        /// 몸통 바로 바깥(점유 멤버십 밖)에서 지금 플레이어 자리와 가장 가까운 유효 칸.
        /// 동률은 좌표 순으로 끊어 재현 가능하게 한다(같은 상황이 매번 같은 결과여야 한다).
        /// 원판·형상(tri) 공통 — 점유 판정과 같은 술어(<c>IsMonsterOccupying</c>)를 지난다.
        /// </summary>
        private HexCoord? FindNearestCellOutsideFootprint(MonsterRuntime monster)
        {
            HexCoord? best = null;
            var bestKey = (int.MaxValue, 0, 0);
            foreach (var coord in HexArea.CellsWithin(monster.Coord, GetMonsterFootprintReach(monster) + 1))
            {
                if (host.IsMonsterOccupying(monster, coord))
                {
                    continue;
                }

                if (!host.Map.TryGetCell(coord, out var cell)
                    || !cell.BaseWalkable
                    || !host.TerrainTraits.IsWalkable(cell.TerrainTypeId)
                    || host.Map.HasMovementBlockingObject(coord)
                    || host.RuntimeStates.ContainsKey(coord))
                {
                    continue;
                }

                var key = (host.PlayerCoord.DistanceTo(coord), coord.Q, coord.R);
                if (key.CompareTo(bestKey) >= 0)
                {
                    continue;
                }

                bestKey = key;
                best = coord;
            }

            return best;
        }

        /// <summary>
        /// 살아 있는 <b>보스</b>의 점유 칸(표현용 — 점유 오버레이가 이 좌표를 그린다 · §13.4 C-4).
        /// 모델이 자기 칸을 정확히 덮지 않으므로, 이 오버레이 없이는 "어디에 설 수 있는지"를
        /// 읽을 수 없다(§11.3). 맵 밖 칸은 오버레이가 허공에 뜨므로 걸러 낸다.
        ///
        /// 조건은 <b>보스인가</b>이지 몸이 여러 칸인가가 아니다(§18): 반경 0(1칸)일 때 빼 버리면
        /// 1페이즈에만 피격 범위 표시가 사라져, 하필 보스가 <b>움직이는</b> 페이즈에 어디까지가
        /// 몸인지 읽을 수 없었다. 반경 0이면 중심 한 칸이 그려진다.
        /// </summary>
        public IReadOnlyList<HexCoord> BossFootprintCoords => GetBossFootprintCoords(null);

        /// <summary>
        /// <see cref="BossFootprintCoords"/>에 보스 단위 필터를 걸어 준다(#22). 프레젠테이션이
        /// "이 보스가 지금 보이는가"(암시야·스테이지 인트로·디버그 공개)를 판정해 넘기면, 안 보이는
        /// 보스의 점유 오버레이가 암시야를 뚫고 위치를 누설하지 않는다. 판정 자체는 뷰의 일이라
        /// (디버그 공개·시네마틱 강제 공개를 상태 계층은 모른다) 술어로 받는다. null이면 전부 포함.
        /// </summary>
        public IReadOnlyList<HexCoord> GetBossFootprintCoords(Func<string, bool> includeBossUnit)
        {
            List<HexCoord> result = null;
            foreach (var monster in host.Monsters)
            {
                // 보스는 몸이 한 칸이어도 그린다(§18). 비보스는 몸이 여러 칸(형상 footprint)일 때만 —
                // 2026-09-03 삼각형 정예도 불가살과 같은 붉은 테두리로 「어디까지가 몸인지」를 읽힌다.
                if (monster.Combatant.IsDead || (!CombatState.IsBossMonster(monster) && !host.IsMultiCellMonster(monster)))
                {
                    continue;
                }

                if (includeBossUnit != null && !includeBossUnit(monster.Id))
                {
                    continue;
                }

                result ??= new List<HexCoord>();
                foreach (var coord in host.EnumerateMonsterOccupiedCoords(monster))
                {
                    if (host.Map != null && host.Map.Contains(coord))
                    {
                        result.Add(coord);
                    }
                }
            }

            return (IReadOnlyList<HexCoord>)result ?? Array.Empty<HexCoord>();
        }
    }
}

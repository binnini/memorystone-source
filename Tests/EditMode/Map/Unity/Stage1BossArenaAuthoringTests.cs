#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    /// <summary>
    /// 출하 Stage_1의 보스 아레나 저작 감사.
    ///
    /// <para>🔴🔴 <b>이 시험이 없어서 실제로 아레나가 통째로 사라졌다</b>(2026-09-02). 지형을 고치면서
    /// 보스 스폰이 <c>monster-spawn-152-n111</c> → <c>monster-spawn-150-n113</c>으로 옮겨 갔는데,
    /// 아레나의 <c>bossSpawnRefId</c>는 옛 id를 가리키고 있었다. 그 상태로는 맵 로드가 <b>통째로</b>
    /// 실패하므로(<see cref="HexSparseMapAuthoringSource"/>의 하드 검증) 저작에서 areaRefs가 비워졌고,
    /// 그러자 <c>IsBossEncountered</c>가 「묶인 아레나가 없으면 존재만으로 조우」로 폴백해
    /// <b>보스 은닉·기믹 게이트가 둘 다 조용히 무효</b>가 됐다 — 화면 어디에도 오류가 뜨지 않는다.</para>
    ///
    /// <para>🔑 그래서 재는 것은 「맵이 로드되는가」가 아니라 <b>보스전이 성립하는 저작이 남아 있는가</b>다.
    /// 스폰을 다시 옮겨도 아레나만 따라오면 통과한다 — 좌표를 상수로 박지 않는 이유다.</para>
    /// </summary>
    [Category("ShippingData")]
    public sealed class Stage1BossArenaAuthoringTests
    {
        private const string Stage1SourcePath = "Assets/Data/Map/Authoring/Stage_1_Source.asset";

        /// <summary>아레나 표준 크기 = 반경 4(61칸). Stage 2도 같고, 보스 기믹 수치가 이 크기에 맞춰 조정돼 있다
        /// (철조각 살포 링 반경 3 · 전멸기 폴백 반경 4).</summary>
        private const int ArenaRadius = 4;
        private const int ArenaCellCount = 61;

        private static HexMapData LoadMap()
        {
            var source = AssetDatabase.LoadAssetAtPath<HexSparseMapAuthoringSource>(Stage1SourcePath);
            Assert.That(source, Is.Not.Null, $"Stage_1 저작 원본이 없다: {Stage1SourcePath}");
            Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
            return map;
        }

        /// <summary>
        /// 아레나 하나를 꺼내 온다. 🔑 <b>없을 때의 문장을 여기 한 곳에 둔다</b> — 각 시험이
        /// <c>Single()</c>을 직접 부르면 아레나가 사라졌을 때 「Sequence contains no matching element」만
        /// 나와서, 정작 가장 흔한 실패(저작이 통째로 비었다)의 이유가 화면에 안 뜬다.
        /// </summary>
        private static HexMapAreaRef LoadArena(HexMapData map)
        {
            var arenas = map.Areas.Where(area => area.IsBossArena).ToList();
            Assert.That(arenas, Has.Count.EqualTo(1),
                "보스 아레나가 정확히 하나여야 한다 — 0이면 결계·은닉·기믹 게이트가 통째로 죽고(폴백이 조용히 삼킨다), " +
                "둘이면 어느 쪽이 봉인될지 저작만 보고 알 수 없다.");
            return arenas[0];
        }

        [Test]
        public void Stage1HasABossArenaBoundToItsBossSpawn()
        {
            var map = LoadMap();
            var boss = map.MonsterSpawnRefs.FirstOrDefault(spawn => MonsterSpawnRolesShim.IsBoss(spawn.SpawnRole));
            Assert.That(boss.Id, Is.Not.Empty, "Stage_1에 role=boss 스폰이 없다.");

            var arena = LoadArena(map);
            Assert.That(arena.BossSpawnRefId, Is.EqualTo(boss.Id),
                "아레나가 묶인 스폰 id가 실제 보스 스폰과 다르다 — 스폰을 옮기면 아레나도 따라와야 한다.");
            Assert.That(arena.Contains(boss.Coord), Is.True, "보스가 자기 아레나 밖에 서 있다.");
        }

        [Test]
        public void Stage1BossArenaIsTheStandardRadiusFourRoom()
        {
            var map = LoadMap();
            var arena = LoadArena(map);

            Assert.That(arena.Coords.Count, Is.EqualTo(ArenaCellCount),
                $"아레나는 반경 {ArenaRadius}(={ArenaCellCount}칸)이어야 한다 — 보스 기믹 수치가 이 크기 기준으로 조정돼 있다.");

            // 모양까지 본다: 61칸이어도 흩뿌려져 있으면 「방」이 아니다.
            var center = arena.GetCenter();
            Assert.That(
                arena.Coords.All(coord => center.DistanceTo(coord) <= ArenaRadius),
                Is.True,
                "아레나 칸이 중심에서 반경 4를 넘는다 — 육각 방이 아니라 흩어진 집합이다.");
        }

        [Test]
        public void Stage1BossArenaIsWalkableThroughout()
        {
            var map = LoadMap();
            var arena = LoadArena(map);
            var walkable = new HashSet<HexCoord>(
                map.AllCells.Where(cell => cell.BaseWalkable).Select(cell => cell.Coord));
            var blocked = new HashSet<HexCoord>(
                map.ObjectRefs.Where(objectRef => objectRef.BlocksMovement).Select(objectRef => objectRef.Coord));

            var unwalkable = arena.Coords.Where(coord => !walkable.Contains(coord)).ToList();
            Assert.That(unwalkable, Is.Empty,
                $"아레나 안에 통행 불가 지형이 있다: {string.Join(", ", unwalkable)}");

            // 🔑 <b>안쪽</b>은 막는 물건이 없어야 한다 — 결계에 갇힌 판에 부술 수 없는 기둥이 서 있으면
            //    보스 기물·전멸기 안전칸이 그만큼 줄고, 그 손실은 저작만 봐서는 안 보인다.
            var obstructed = arena.Coords.Where(coord => blocked.Contains(coord)).ToList();
            Assert.That(obstructed, Is.Empty,
                $"아레나 안에 이동을 막는 오브젝트가 있다: {string.Join(", ", obstructed)}");
        }

        /// <summary>
        /// 결계는 아레나 <b>바깥 한 겹</b>을 막는다. 그 링이 맵 위에 실제로 있어야 담장이 그려진다 —
        /// 링이 통째로 맵 밖이면 「막긴 막는데 아무것도 안 보이는」 결계가 된다.
        /// </summary>
        [Test]
        public void Stage1BossArenaHasABoundaryRingOnTheMap()
        {
            var map = LoadMap();
            var arena = LoadArena(map);
            var ring = arena.EnumerateBoundaryRing().Where(coord => map.Contains(coord)).ToList();

            Assert.That(ring, Is.Not.Empty, "결계가 막을 경계 링이 맵 위에 하나도 없다.");
            Assert.That(ring.Count, Is.GreaterThanOrEqualTo(24),
                "경계 링 대부분이 맵 밖이다 — 아레나가 맵 가장자리에 걸쳐 있다는 뜻이라 담장이 반쪽만 그려진다.");
        }

        /// <summary>MonsterSpawnRoles는 Combat 어셈블리라 Map 시험에서 못 본다 — 판정만 옮겨 온다.</summary>
        private static class MonsterSpawnRolesShim
        {
            public static bool IsBoss(string role) =>
                string.Equals(role, "boss", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif

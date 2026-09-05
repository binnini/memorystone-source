using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    // Monster attack shape lookup. Every shape always includes the adjacent 1-hex ring
    // (unless the shape opts out). Shape offsets are additional cells beyond that base
    // ring, stored in canonical East-facing space:
    //   (1,0) = one step forward, (1,-1) = forward-left, (0,1) = forward-right.
    // At runtime, offsets are rotated to match the monster's actual attack direction.
    //
    // §27(안 A): 형상 저작 원본은 attack_shapes.csv다 — 이 클래스는 하드코딩 테이블이 아니라
    // 그 카탈로그를 담는 로더이며, 몬스터 패턴과 플레이어 카드가 같은 테이블을 읽는다.
    // 편집은 에디터 창(Seoul Playup/Combat/Attack Shape Editor) 또는 CSV 직접 편집으로 한다.
    public static class AttackShapeLibrary
    {
        public const string Single    = "single";
        public const string Line2     = "line-2";
        public const string Line3     = "line-3";
        public const string Line4     = "line-4";
        public const string ConeNear  = "cone-near";
        public const string ConeMid   = "cone-mid";
        public const string ConeWide  = "cone-wide";
        public const string TForward  = "t-forward";
        public const string CrossNear = "cross-near";
        public const string CrossFar  = "cross-far";
        public const string RingNear  = "ring-near";
        public const string VSplit    = "v-split";
        public const string Pincer    = "pincer";
        public const string Tremor    = "tremor";
        public const string RingFull2 = "ring-full-2";
        public const string Spiral3   = "spiral-3";
        public const string ConeLong  = "cone-long";
        public const string Donut2      = "donut-2";
        public const string Artillery3  = "artillery-3";

        private static Dictionary<string, AttackShapeDefinition> shapes;
        private static IReadOnlyList<AttackShapeDefinition> orderedShapes;
        private static string sourceName = "(uninitialized)";

        /// <summary>
        /// 저작 카탈로그로 (재)초기화한다. 빌드 경로는 <c>CombatCatalogTextAssetSource</c>가 몬스터
        /// 카탈로그 변환 <b>앞</b>에서 부른다 — 패턴 CSV 검증(<c>MonsterCatalogCsv</c>)이
        /// <see cref="TryGet"/>으로 shapeId를 대조하므로 순서가 계약이다.
        /// </summary>
        public static void Initialize(AttackShapeCatalogDefinition catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            shapes = catalog.Shapes.ToDictionary(shape => shape.Id, StringComparer.Ordinal);
            orderedShapes = catalog.Shapes.ToList();
            sourceName = catalog.SourceName;
        }

        /// <summary>지금 로드된 형상 정의 전부, 저작 순서 그대로(에디터 창·도표 도구용 읽기 뷰).</summary>
        public static IReadOnlyList<AttackShapeDefinition> AllShapes
        {
            get
            {
                _ = Table;
                return orderedShapes;
            }
        }

        /// <summary>
        /// 미초기화 시 출하 CSV를 파일로 직접 읽는 에디터/EditMode 테스트 폴백. 빌드에는 이 경로가
        /// 없으므로 명시적 예외를 던진다 — §19 선례("에디터에만 경로 폴백이 있어 조용한 갭")의
        /// 재발 방지: 조용히 빈 형상이 되어 공격이 무해해지는 대신, 초기화 배선 누락을 즉시 알린다.
        /// </summary>
        private static Dictionary<string, AttackShapeDefinition> Table
        {
            get
            {
                if (shapes == null && File.Exists(CombatCsvPaths.AttackShapesCsv))
                {
                    Initialize(AttackShapeCatalogCsv.ConvertText(
                        File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8)));
                }

                if (shapes == null)
                {
                    throw new InvalidOperationException(
                        "AttackShapeLibrary is not initialized — CombatCatalogTextAssetSource의 attackShapes"
                        + " TextAsset이 비었거나 CreateMonsterCatalog 이전에 Initialize가 불리지 않았다.");
                }

                return shapes;
            }
        }

        public static bool TryGet(string shapeId, out AttackShapeDefinition shape) =>
            Table.TryGetValue(shapeId ?? string.Empty, out shape);

        private static readonly HexCoord[] AdjacentOffsetsInternal =
        {
            new HexCoord(1, 0), new HexCoord(1, -1), new HexCoord(0, -1),
            new HexCoord(-1, 0), new HexCoord(-1, 1), new HexCoord(0, 1)
        };

        /// <summary>
        /// 공용 인접 6칸(정동 기준·고정 순서). <c>adjacency=full</c>이 실행 시점에 더하는 바로 그 칸들이라,
        /// 저작 검증(지대 오프셋이 형상 안인가 등)이 같은 목록을 봐야 판정이 두 벌이 되지 않는다.
        /// </summary>
        public static IReadOnlyList<HexCoord> AdjacentOffsets => AdjacentOffsetsInternal;

        // Returns all world cells hit by the monster shape: the universal adjacent
        // 1-hex ring (unless the shape opts out — ranged-band shapes like donut/artillery)
        // plus shape-specific additional cells rotated from canonical East-facing space.
        public static IEnumerable<HexCoord> GetAffectedCells(
            string shapeId, HexCoord monsterCoord, HexDirection attackDirection)
        {
            return GetAffectedCells(shapeId, monsterCoord, attackDirection, footprintRadius: 0);
        }

        // P6(§13.4 C-6): footprintRadius > 0(멀티셀 보스)이면 원점을 조준 방향의 몸통 가장자리
        // 칸으로 옮긴 뒤 shape를 편다. 중심 앵커 그대로면 인접 링과 근거리 오프셋이 보스 자신의
        // 점유 칸 위에 깔려, 저작된 도달 거리가 몸 반경만큼 잘려 나간다. 계획·해소·예고 오버레이가
        // 전부 이 함수를 공유하므로 예고=명중 계약은 자동 유지된다.
        public static IEnumerable<HexCoord> GetAffectedCells(
            string shapeId, HexCoord monsterCoord, HexDirection attackDirection, int footprintRadius)
        {
            return GetAffectedCells(shapeId, monsterCoord, attackDirection, new MonsterBodyShape(footprintRadius, null));
        }

        /// <summary>몸 기하(원판·형상)에서 원점을 유도해 shape를 편다. 원판은 위 오버로드와 같은 결과다.</summary>
        public static IEnumerable<HexCoord> GetAffectedCells(
            string shapeId, HexCoord monsterCoord, HexDirection attackDirection, MonsterBodyShape body)
        {
            if (!Table.TryGetValue(shapeId ?? string.Empty, out var shape))
            {
                return Enumerable.Empty<HexCoord>();
            }

            // 몸 둘레(2026-09-04 §3-B): 원점·회전을 거치지 않고 footprint 전체 기준으로 편다.
            // tri = 9칸 · single footprint = 인접 6칸(폴백 명세) · 원판 r1 = 12칸.
            if (shape.Adjacency == AttackShapeAdjacency.BodyShell)
            {
                return GetBodyShellCells(monsterCoord, body);
            }

            if (shape.Adjacency == AttackShapeAdjacency.Body)
            {
                // 둘레 ∪ 원점(앞 몸통 칸) 기준 회전 오프셋(2026-09-05 결정 8). 오프셋이 비면 옛 body와 동일.
                var perimeter = GetBodyPerimeterCells(monsterCoord, body, attackDirection);
                if (shape.Offsets.Length == 0)
                {
                    return perimeter;
                }

                var bodyOrigin = body.ResolveShapeOrigin(monsterCoord, attackDirection);
                var bodySteps = (6 - (int)attackDirection) % 6;
                return perimeter
                    .Concat(shape.Offsets.Select(offset => bodyOrigin + offset.RotateSteps(bodySteps)))
                    .Distinct();
            }

            var origin = body.ResolveShapeOrigin(monsterCoord, attackDirection);

            // RotateSteps(n) rotates clockwise; direction enum is counter-clockwise,
            // so (6 - d) % 6 clockwise steps align canonical East offsets to direction d.
            var rotationSteps = (6 - (int)attackDirection) % 6;
            var extraOffsets = shape.Offsets.Select(offset => offset.RotateSteps(rotationSteps));
            var offsets = shape.IncludeAdjacentRing ? AdjacentOffsetsInternal.Concat(extraOffsets) : extraOffsets;
            return offsets
                .Select(offset => origin + offset)
                .Distinct();
        }

        /// <summary>
        /// 몸 둘레 셀 집합 = footprint 점유 칸의 이웃 − 점유 칸. 형상 footprint(tri)는 이웃 합집합으로,
        /// 원판(반경 R·R=0이면 한 칸)은 중심 거리 R+1 링으로 편다 — 두 정의는 같은 「둘레」다.
        ///
        /// <para>🔴 <b>등 뒤는 비운다</b>(2026-09-05 사용자 확정): 형상 몸은 조준 방향 기준 <b>가장 뒤 칸</b>
        /// 하나를 둘레 계산에서 제외한다. 그 칸에만 닿는 이웃(tri에서 2칸)은 위협에서 빠져
        /// 「등 뒤로 파고들면 안 맞는다」가 성립한다 — 앞 두 칸과 함께 닿는 이웃은 그대로 위협이다.
        /// 몸이 회전하지 않으므로(<c>MonsterFootprints</c>) 앞뒤는 <b>이번 턴 조준 방향</b>이 정하고,
        /// 그 방향은 예고 시점에 잠긴다 — 그래서 잠긴 예고를 보고 등 뒤로 돌아갈 수 있다.</para>
        /// </summary>
        /// <summary>
        /// 둘레의 둘레(body-shell · 2026-09-05 결정 8): footprint 점유 칸에서 <b>그래프 거리 2</b>인 칸 집합
        /// (= 전체 둘레의 이웃 − 점유 − 둘레). 등 뒤 예외를 두지 않는다 — 이 형상의 안전지대는 방향이 아니라
        /// 「몸에 붙은 칸」과 「거리 3 밖」이라, 붙는 것이 항상 답이다. 원판(반경 R)은 중심 거리 R+2 링과 같다.
        /// </summary>
        private static IEnumerable<HexCoord> GetBodyShellCells(HexCoord monsterCoord, MonsterBodyShape body)
        {
            var occupied = new HashSet<HexCoord>();
            if (body.Offsets != null)
            {
                foreach (var offset in body.Offsets)
                {
                    occupied.Add(monsterCoord + offset);
                }
            }
            else
            {
                foreach (var cell in HexArea.CellsWithin(monsterCoord, Math.Max(0, body.Radius)))
                {
                    occupied.Add(cell);
                }
            }

            var perimeter = new HashSet<HexCoord>();
            foreach (var cell in occupied)
            {
                foreach (var adjacent in AdjacentOffsetsInternal)
                {
                    var neighbor = cell + adjacent;
                    if (!occupied.Contains(neighbor))
                    {
                        perimeter.Add(neighbor);
                    }
                }
            }

            var shell = new HashSet<HexCoord>();
            foreach (var cell in perimeter)
            {
                foreach (var adjacent in AdjacentOffsetsInternal)
                {
                    var neighbor = cell + adjacent;
                    if (!occupied.Contains(neighbor) && !perimeter.Contains(neighbor))
                    {
                        shell.Add(neighbor);
                    }
                }
            }

            return shell;
        }

        private static IEnumerable<HexCoord> GetBodyPerimeterCells(
            HexCoord monsterCoord, MonsterBodyShape body, HexDirection attackDirection)
        {
            if (body.Offsets != null)
            {
                var occupied = new HashSet<HexCoord>();
                foreach (var offset in body.Offsets)
                {
                    occupied.Add(monsterCoord + offset);
                }

                // 점유는 몸 전체로 잡고(둘레에서 몸통 칸을 빼야 하므로), 이웃을 <b>앞 칸들에서만</b> 편다.
                var rearOffset = body.ResolveRearOffset(attackDirection);
                var ring = new HashSet<HexCoord>();
                foreach (var offset in body.Offsets)
                {
                    if (rearOffset.HasValue && offset == rearOffset.Value)
                    {
                        continue;
                    }

                    var cell = monsterCoord + offset;
                    foreach (var adjacent in AdjacentOffsetsInternal)
                    {
                        var neighbor = cell + adjacent;
                        if (!occupied.Contains(neighbor))
                        {
                            ring.Add(neighbor);
                        }
                    }
                }

                return ring;
            }

            var perimeter = body.Radius + 1;
            var cells = new List<HexCoord>(6 * perimeter);
            var origin = new HexCoord(0, 0);
            for (var q = -perimeter; q <= perimeter; q++)
            {
                for (var r = Math.Max(-perimeter, -q - perimeter); r <= Math.Min(perimeter, -q + perimeter); r++)
                {
                    var offset = new HexCoord(q, r);
                    if (origin.DistanceTo(offset) == perimeter)
                    {
                        cells.Add(monsterCoord + offset);
                    }
                }
            }

            return cells;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 공격 형상 카탈로그. <c>attack_shapes.csv</c>가 저작 원본이고(§27 · 안 A),
    /// <see cref="AttackShapeLibrary"/>는 이 결과를 담는 로더다 — 몬스터 패턴과 플레이어 카드가
    /// 같은 카탈로그를 쓴다.
    /// </summary>
    public sealed class AttackShapeCatalogDefinition
    {
        public AttackShapeCatalogDefinition(string sourceName, IReadOnlyList<AttackShapeDefinition> shapes)
        {
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "attack-shapes" : sourceName;
            Shapes = shapes ?? throw new ArgumentNullException(nameof(shapes));
        }

        public string SourceName { get; }
        public IReadOnlyList<AttackShapeDefinition> Shapes { get; }
    }

    /// <summary>
    /// <c>attack_shapes.csv</c> 파서·검증. 스키마: <c>shapeId,adjacency,offsets,designerNote</c>.
    ///
    /// <para>offsets는 정동(East) 기준 축좌표 <c>q:r</c> 쌍의 <b>공백 구분</b> 목록(예: <c>2:0 3:0</c>).
    /// 쉼표를 쓰지 않는 것은 규약이다 — CSV 컬럼 밀림 사고(§19 선례: designerNote의 쉼표 하나가
    /// 수백 테스트를 동시에 깨뜨렸다)를 인코딩 수준에서 차단한다.</para>
    ///
    /// <para>인접 어휘(<c>adjacency</c>)는 네 값이다 — 예전 bool <c>includeAdjacentRing</c>을 대체한다:
    /// <list type="bullet">
    /// <item><c>full</c> — 인접 6칸 자동 추가(기존 <c>true</c>).</item>
    /// <item><c>none</c> — 인접 없음 + 거리 1 저작 <b>거부</b>(기존 <c>false</c>).</item>
    /// <item><c>open</c> — 인접 자동 추가 없음, 거리 1 저작 <b>허용</b>(인접을 원하는 만큼만 채운다).</item>
    /// <item><c>body</c> — 몸 둘레(2026-09-04 §3-B). 셀을 저작하지 않고 footprint 이웃 − 점유 칸으로
    /// 실행 시점에 편다. offsets는 <b>비워야 한다</b>(저작 칸이 있으면 footprint 유도와 어긋난다).</item>
    /// </list></para>
    ///
    /// <para>🔒 저작 가드(DEC-07-28-04 선례 — "저작으로 규칙을 깰 수 있는가"):
    /// <c>none</c>은 <b>모든 오프셋이 거리 2 이상</b>이고 오프셋이 비어 있지 않을 때만 허용한다.
    /// 이 가드가 "원거리 밴드는 붙으면 안전"이라는 회피 계약을 저작 실수로부터 지킨다 — 링 없는
    /// 형상에 거리 1 칸이 섞이면 그 계약이 무너진다. 붙어도 맞는 형상을 저작하고 싶을 때는
    /// <c>open</c>을 쓴다(계약을 깨는 게 아니라 애초에 그 계약을 걸지 않는 형상이다).</para>
    /// </summary>
    public static class AttackShapeCatalogCsv
    {
        private static readonly Regex ShapeIdPattern = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
        private static readonly Regex OffsetPattern = new Regex("^(-?\\d+):(-?\\d+)$", RegexOptions.Compiled);

        /// <summary>형상 오프셋이 나갈 수 있는 최대 거리. 현행 최장은 4(line-4·cone-long) — 6을 넘는
        /// 저작은 화면 밖 예고·성능 문제라 오타로 취급한다.</summary>
        public const int MaxOffsetDistance = 6;

        public static AttackShapeCatalogDefinition ConvertText(string csvText, string sourceName = "attack_shapes.csv")
        {
            var table = CsvTable.Parse(csvText, sourceName);
            var shapes = new List<AttackShapeDefinition>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in table.Rows)
            {
                var shapeId = Required(row, "shapeId");
                if (!ShapeIdPattern.IsMatch(shapeId))
                {
                    throw new ArgumentException($"{row.FileName}:{row.LineNumber} shapeId '{shapeId}' must be kebab-case ([a-z0-9-]).");
                }

                if (!seenIds.Add(shapeId))
                {
                    throw new ArgumentException($"{row.FileName}:{row.LineNumber} duplicate shapeId '{shapeId}'.");
                }

                var adjacency = ParseAdjacency(row);
                var offsets = ParseOffsets(row, shapeId);

                // body는 2026-09-05부터 오프셋을 <b>더할 수</b> 있다(둘레 ∪ 원점 기준 오프셋 · 결정 8).
                // body-shell은 여전히 footprint 유도만 — 껍질 위에 저작 칸을 섞으면 「붙으면 안전」을 파서가 못 지킨다.
                if (adjacency == AttackShapeAdjacency.BodyShell && offsets.Length > 0)
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} shape '{shapeId}' is adjacency=body-shell but authors offsets"
                        + $" ({string.Join(" ", offsets.Select(o => $"{o.Q}:{o.R}"))}) —"
                        + " body-shell은 footprint에서 셀을 유도하므로 저작 칸이 있으면 어느 쪽이 정본인지 못 읽는다.");
                }

                if (adjacency != AttackShapeAdjacency.Full
                    && adjacency != AttackShapeAdjacency.Body
                    && adjacency != AttackShapeAdjacency.BodyShell
                    && offsets.Length == 0)
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} shape '{shapeId}' opts out of the adjacent ring but has no offsets — it would hit nothing.");
                }

                if (adjacency == AttackShapeAdjacency.None)
                {
                    var tooClose = offsets.Where(offset => HexDistanceFromOrigin(offset) < 2).ToList();
                    if (tooClose.Count > 0)
                    {
                        throw new ArgumentException(
                            $"{row.FileName}:{row.LineNumber} shape '{shapeId}' is adjacency=none but has distance-1 offsets"
                            + $" ({string.Join(" ", tooClose.Select(o => $"{o.Q}:{o.R}"))}) —"
                            + " 링 없는 형상은 전 칸이 거리 2 이상이어야 '붙으면 안전' 계약이 성립한다."
                            + " 붙어도 맞아야 하는 형상이면 adjacency=open으로 저작한다.");
                    }
                }

                shapes.Add(new AttackShapeDefinition(shapeId, offsets, adjacency));
            }

            if (shapes.Count == 0)
            {
                throw new ArgumentException($"{sourceName} has no shape rows.");
            }

            return new AttackShapeCatalogDefinition(sourceName, shapes);
        }

        private static HexCoord[] ParseOffsets(CsvRow row, string shapeId)
        {
            var raw = Optional(row, "offsets");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<HexCoord>();
            }

            var seen = new HashSet<HexCoord>();
            var offsets = new List<HexCoord>();
            foreach (var token in raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var match = OffsetPattern.Match(token);
                if (!match.Success)
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} shape '{shapeId}' offset '{token}' is not 'q:r' (example: 2:0 3:0).");
                }

                var offset = new HexCoord(
                    int.Parse(match.Groups[1].Value),
                    int.Parse(match.Groups[2].Value));
                var distance = HexDistanceFromOrigin(offset);
                if (distance == 0)
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} shape '{shapeId}' contains the origin (0:0) — a shape cannot target its own anchor.");
                }

                if (distance > MaxOffsetDistance)
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} shape '{shapeId}' offset {offset.Q}:{offset.R} is distance {distance}"
                        + $" (max {MaxOffsetDistance}) — 오타로 취급한다.");
                }

                if (!seen.Add(offset))
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} shape '{shapeId}' has duplicate offset {offset.Q}:{offset.R}.");
                }

                offsets.Add(offset);
            }

            return offsets.ToArray();
        }

        private static int HexDistanceFromOrigin(HexCoord coord) => new HexCoord(0, 0).DistanceTo(coord);

        /// <summary>
        /// <c>adjacency</c> 컬럼(full/none/open). 옛 bool 컬럼(<c>includeAdjacentRing</c>)이 남은 CSV는
        /// 조용히 넘기지 않고 명시적으로 죽인다 — true/false를 어휘로 오해해 통과시키면 <c>open</c>이
        /// <c>full</c>로 읽히는 조용한 드리프트가 된다.
        /// </summary>
        private static AttackShapeAdjacency ParseAdjacency(CsvRow row)
        {
            if (!row.TryGet("adjacency", out _))
            {
                var hint = row.TryGet("includeAdjacentRing", out _)
                    ? " — 이 CSV는 옛 스키마(includeAdjacentRing)다. true→full · false→none으로 치환할 것."
                    : string.Empty;
                throw new ArgumentException(
                    $"{row.FileName}:{row.LineNumber} is missing required column 'adjacency'.{hint}");
            }

            var value = Required(row, "adjacency");
            switch (value.ToLowerInvariant())
            {
                case "full": return AttackShapeAdjacency.Full;
                case "none": return AttackShapeAdjacency.None;
                case "open": return AttackShapeAdjacency.Open;
                case "body": return AttackShapeAdjacency.Body;
                case "body-shell": return AttackShapeAdjacency.BodyShell;
                default:
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} adjacency must be full/none/open/body/body-shell, got '{value}'.");
            }
        }

        private static string Required(CsvRow row, string column)
        {
            if (!row.TryGet(column, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{row.FileName}:{row.LineNumber} is missing required column '{column}'.");
            }

            return value.Trim();
        }

        private static string Optional(CsvRow row, string column) => row.TryGet(column, out var value) ? value.Trim() : string.Empty;
    }
}

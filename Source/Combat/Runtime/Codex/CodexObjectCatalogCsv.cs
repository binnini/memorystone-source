using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 맵에 배치되는 오브젝트의 <b>도감 저작</b>(P6). 소스는
    /// <see cref="CombatCsvPaths.MapObjectsCsv"/>이고 런타임 직접 파싱형이다(베이크 없음) —
    /// <see cref="ConsumableItemCatalogCsv"/> · <see cref="RelicCatalogCsv"/>와 같은 결.
    /// <para>
    /// 🔴🔴 <b>이름이 <c>CodexObject</c>인 이유.</b> 소스 파일 이름은 계획 §7이 정한
    /// <c>map_objects.csv</c>를 그대로 쓰지만, 코드에서 "field object"는 이미
    /// <see cref="FieldObjectKind"/>(피해·회복 <b>장판</b>)를 뜻한다. 그리고 P6의 §8-1 판정(Q44)이
    /// 확정한 것이 바로 <b>그 둘은 다른 것</b>이라는 사실이다 — 장판은 <c>cards.csv</c>의
    /// <c>behaviorId</c>에서 나오고 이미 카드 도감에 실려 있다. 그래서 여기 타입에
    /// <c>FieldObject</c>를 쓰면 확정한 경계를 이름으로 도로 지워 버린다.
    /// </para>
    /// <para>
    /// 🔑 <b>여기 있는 것은 넷뿐이다</b>(Q45-㉮ 확정). <c>blocksMovement</c> · <c>objectType</c> 같은
    /// 게임플레이 값은 <c>MapObjectTypedCatalog</c>의 <c>Entry</c>가 계속 정본이고, 그 카탈로그는
    /// 프리팹을 폴더에 넣으면 자동 등록되는 흐름(<c>MapObjectVariantAutoRegistrar</c>)이 물고 있다.
    /// CSV가 그걸 가져오면 저작 흐름이 통째로 바뀌므로 <b>도감이 필요로 하는 것만</b> 옮겼다:
    /// 이름 · 설명 · 썸네일 키 · 도감 노출.
    /// </para>
    /// <para>
    /// 🔑 <c>id</c>와 <c>catalogRef</c>가 <b>따로인 이유</b>(Q46-A 확정). 카탈로그 키는
    /// <c>treasureChest_tmp</c>처럼 임시 모델 이름에 묶여 있어서, 정식 모델이 들어오면 그 키가 바뀐다.
    /// 해금 진행도는 <c>id</c>로 저장되므로 id가 카탈로그 키를 따라가면 모델 교체가 곧 진행도 초기화가
    /// 된다(P5가 몬스터를 프리팹이 아니라 id로 키잉한 것과 같은 문제). 모델이 바뀌면
    /// <c>catalogRef</c> 한 칸만 고친다.
    /// </para>
    /// </summary>
    public static class CodexObjectCatalogCsv
    {
        public static CodexObjectCatalog ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Map object CSV path is required.", nameof(path));
            }

            return ConvertText(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path));
        }

        public static CodexObjectCatalog ConvertText(string csvText, string sourceName = "map_objects.csv")
        {
            var table = CsvTable.Parse(csvText, string.IsNullOrWhiteSpace(sourceName) ? "map_objects.csv" : sourceName);
            var rows = new List<CodexObjectEntry>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenCatalogRefs = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in table.Rows)
            {
                var id = Required(row, "id");
                if (!seenIds.Add(id))
                {
                    throw new ArgumentException($"{row.FileName}:{row.LineNumber} duplicates id '{id}'.");
                }

                // 카탈로그 키가 겹치면 도감 두 칸이 같은 프리팹을 두고 같은 썸네일 파일을 다툰다.
                var catalogRef = Required(row, "catalogRef");
                if (!seenCatalogRefs.Add(catalogRef))
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} duplicates catalogRef '{catalogRef}'.");
                }

                var kindRaw = Required(row, "kind");
                if (!Enum.TryParse<CodexObjectKind>(kindRaw, ignoreCase: true, out var kind))
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} kind '{kindRaw}'는 interactive/landmark 중 하나여야 한다.");
                }

                // 🔴 이름 없는 행은 통과시키지 않는다. 도감이 지어낸 이름을 띄우지 않는다는 것이
                //    이 CSV가 존재하는 이유 자체다(계획 §10.4) — 빈 이름은 id가 화면에 새는 길이다.
                var displayName = Required(row, "displayNameKo");
                var description = Required(row, "descriptionKo");

                rows.Add(new CodexObjectEntry(
                    id,
                    catalogRef,
                    kind,
                    displayName,
                    description,
                    // 비우면 굽기 규약(codex_thumb_{id})이 id에서 파일 이름을 만든다.
                    Optional(row, "thumbnailRef"),
                    Bool(row, "visibleInCatalog", fallback: true)));
            }

            return new CodexObjectCatalog(rows);
        }

        private static string Required(CsvRow row, string column)
        {
            var value = Optional(row, column);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{row.FileName}:{row.LineNumber} missing required column '{column}'.");
            }

            return value;
        }

        private static string Optional(CsvRow row, string column) =>
            row.TryGet(column, out var value) ? value.Trim() : string.Empty;

        private static bool Bool(CsvRow row, string column, bool fallback)
        {
            var value = Optional(row, column);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            if (string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "FALSE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new ArgumentException(
                $"{row.FileName}:{row.LineNumber} column '{column}' must be TRUE/FALSE or empty.");
        }
    }
}

using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 오브젝트 도메인(P6). 저작은 <c>map_objects.csv</c>이고, 게임플레이 값은
    /// <see cref="MapObjectCatalogSet"/>이 계속 정본이다 — 이 도메인이 둘을 겹쳐 한 칸을 만든다.
    /// <para>
    /// 🔴🔴 <b>경계</b>(계획 §8-1 = Q44 확정): <b>오브젝트 도메인 = 맵에 배치되는 것.</b>
    /// 피해·회복 <b>장판</b>은 여기 없다 — 그것은 맵 저작물이 아니라 <c>cards.csv</c>의
    /// <c>behaviorId</c>에서 나오고(<c>CardCatalogAsset.ResolveFieldObjectKind</c>) 이미 카드 도메인에
    /// 실려 있다. 여기 넣으면 같은 것이 두 칸에 뜬다. 함정도 여기 없다 — 배치물이지만
    /// <c>TrapPresetCatalog</c>라는 제 저작 표면이 따로 있다.
    /// </para>
    /// <para>
    /// 🔴 <b><c>blocksVision</c>을 쓰지 않는다.</b> 649개 오브젝트에 저작돼 있지만 소비자가 0이고,
    /// 암시야 차폐는 <b>영구 미구현</b>으로 결정됐다(DEC-2026-07-28-03). 도감에
    /// "시야를 막습니다"라고 쓰면 플레이어에게 <b>없는 규칙</b>을 가르치는 셈이다.
    /// <c>blocksMovement</c>는 실제로 동작하므로 쓴다.
    /// </para>
    /// <para>
    /// 🔴 카탈로그·썸네일은 <b>직렬화 참조</b>로 받는다. <c>Resources</c> 폴백은 이 에셋들이
    /// <c>Resources/</c> 밖에 있어서 <b>빌드에서 언제나 <c>null</c></b>이 된다(계획 §2.2·§13.1).
    /// </para>
    /// </summary>
    public sealed class CodexObjectDomain : ICodexDomain
    {
        /// <summary>전투가 신호를 보낼 때 쓰는 것과 <b>같은 문자열</b>이어야 한다 —
        /// 그래서 값을 여기 적지 않고 아래층 상수를 가리킨다(P3 §5-5 지뢰).</summary>
        public const string DomainId = Combat.Runtime.CodexDomainIds.Object;

        private readonly List<CodexEntry> entries = new List<CodexEntry>();

        /// <param name="catalogSet">
        /// 게임플레이 값(프리팹 · <c>blocksMovement</c> · 타입)의 정본. <see langword="null"/>이면
        /// 항목은 CSV가 말하는 것(이름 · 설명)만으로 서고 상세의 수치 줄이 빠진다 —
        /// 시험 픽스처가 카탈로그 없이 도메인을 세울 수 있어야 한다.
        /// </param>
        /// <param name="thumbnails">구운 전용 썸네일(1층). 없으면 이름 전체 폴백(2층)으로 뜬다.</param>
        public CodexObjectDomain(
            CodexObjectCatalog catalog,
            Color accent,
            MapObjectCatalogSet catalogSet = null,
            CodexThumbnailCatalog thumbnails = null)
        {
            Accent = accent;
            Build(catalog, catalogSet, thumbnails);
        }

        public string Id => DomainId;

        public string Label => "오브젝트";

        public Color Accent { get; }

        /// <summary>시야에 들어와 본 오브젝트만 열린다 — 신호는 <c>SweepCodexMapObjects</c>가 보낸다.</summary>
        public bool AlwaysUnlocked => false;

        public IReadOnlyList<CodexEntry> Entries => entries;

        private void Build(
            CodexObjectCatalog catalog,
            MapObjectCatalogSet catalogSet,
            CodexThumbnailCatalog thumbnails)
        {
            if (catalog == null)
            {
                return;
            }

            foreach (var entry in catalog.Entries)
            {
                // 장식 90종을 거르는 축. 지금 CSV에는 노출 항목만 실려 있지만, 나중에 감사 목적으로
                // FALSE 행을 넣더라도 격자가 저절로 걸러지도록 여기서 한 번 더 본다.
                if (entry == null || !entry.VisibleInCatalog)
                {
                    continue;
                }

                var catalogEntry = ResolveCatalogEntry(catalogSet, entry.CatalogRef);

                // 🔑 프리팹이 아니라 CSV의 안정 id로 찾는다 — 임시 모델이 교체돼도 그림이 따라온다.
                var dedicated = thumbnails != null ? thumbnails.Resolve(entry.ResolveThumbnailKey()) : null;

                entries.Add(new CodexEntry(
                    id: entry.Id,
                    displayName: entry.DisplayName,
                    // 구운 그림은 칸 비율에 맞춰 여백째 렌더된다 — 잘라내면 실루엣이 뭉개진다.
                    thumbnail: CodexThumbnail.Resolve(
                        dedicated, entry.DisplayName, Accent, preserveAspect: dedicated != null),
                    subtitle: $"{entry.Id} · {KindLabel(entry.Kind)}",
                    description: entry.Description,
                    filterChip: KindLabel(entry.Kind),
                    metaChips: BuildMetaChips(entry, catalogEntry),
                    detailRows: BuildDetailRows(entry, catalogEntry),
                    debugRows: BuildDebugRows(entry, catalogEntry),
                    authoringWarning: BuildAuthoringWarning(entry, catalogSet, catalogEntry)));
            }
        }

        /// <summary>
        /// CSV의 <c>catalogRef</c>로 게임플레이 정본 엔트리를 되찾는다.
        /// <see langword="null"/>이면 CSV가 없어진 프리팹을 가리키고 있는 것이다 — 디버그 뷰에
        /// 경고로 뜬다(아래 <see cref="BuildAuthoringWarning"/>).
        /// <para>
        /// 🔑 조회를 여기서 다시 짜지 않고 카탈로그 자신에게 묻는다 — 밖에서 순회를 하나 더 만들면
        /// 중복 <c>objectRef</c> 처리 같은 규칙이 언젠가 카탈로그와 갈라진다.
        /// </para>
        /// </summary>
        private static RuntimeMapObjectPrefabCatalog.Entry ResolveCatalogEntry(
            MapObjectCatalogSet catalogSet,
            string catalogRef)
        {
            if (catalogSet == null || string.IsNullOrWhiteSpace(catalogRef))
            {
                return null;
            }

            return catalogSet.TryGetEntry(catalogRef, out var entry) ? entry : null;
        }

        private static IReadOnlyList<string> BuildMetaChips(
            CodexObjectEntry entry,
            RuntimeMapObjectPrefabCatalog.Entry catalogEntry)
        {
            var chips = new List<string>(2);

            if (catalogEntry != null && catalogEntry.BlocksMovement)
            {
                chips.Add("길 막음");
            }

            // 몇 칸을 차지하는가는 그림이 말해 주지 않는다 — 한 칸짜리는 굳이 적지 않는다.
            var footprint = catalogEntry?.FootprintOffsets;
            if (footprint != null && footprint.Count > 1)
            {
                chips.Add($"{footprint.Count}칸");
            }

            return chips;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDetailRows(
            CodexObjectEntry entry,
            RuntimeMapObjectPrefabCatalog.Entry catalogEntry)
        {
            var rows = new List<CodexDetailRow>(4)
            {
                new CodexDetailRow("갈래", KindLabel(entry.Kind)),
            };

            if (catalogEntry == null)
            {
                return rows;
            }

            rows.Add(new CodexDetailRow("밟을 수 있는가", catalogEntry.Interactable ? "밟으면 발동합니다" : "지나갈 수 없습니다"));
            rows.Add(new CodexDetailRow("길 막음", catalogEntry.BlocksMovement ? "예" : "아니오"));

            var footprint = catalogEntry.FootprintOffsets;
            rows.Add(new CodexDetailRow("차지하는 칸", $"{(footprint == null ? 1 : footprint.Count)}칸"));

            // 🔴 blocksVision 줄은 일부러 없다 — 위 머리말 참고(DEC-2026-07-28-03).
            return rows;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDebugRows(
            CodexObjectEntry entry,
            RuntimeMapObjectPrefabCatalog.Entry catalogEntry)
        {
            var rows = new List<CodexDetailRow>(7)
            {
                new CodexDetailRow("id", entry.Id),
                new CodexDetailRow("catalogRef", entry.CatalogRef),
                new CodexDetailRow("kind", entry.Kind.ToString()),
                new CodexDetailRow("thumbnailKey", entry.ResolveThumbnailKey()),
                new CodexDetailRow("visibleInCatalog", entry.VisibleInCatalog ? "TRUE" : "FALSE"),
            };

            if (catalogEntry == null)
            {
                rows.Add(new CodexDetailRow("objectType", "(카탈로그에서 못 찾음)"));
                return rows;
            }

            rows.Add(new CodexDetailRow("objectType", catalogEntry.ObjectType.ToString()));
            rows.Add(new CodexDetailRow("blocksMovement", catalogEntry.BlocksMovement ? "TRUE" : "FALSE"));
            rows.Add(new CodexDetailRow("interactable", catalogEntry.Interactable ? "TRUE" : "FALSE"));
            rows.Add(new CodexDetailRow("prefab", catalogEntry.Prefab == null ? "(없음)" : catalogEntry.Prefab.name));

            // 🔴 blocksVision은 저작돼 있지만 소비자가 0이다. 디버그 뷰에서는 "저작값이 이렇다"를
            //    보는 것이 맞으므로 남기되, 아무 효과가 없다는 것을 값 옆에 적어 둔다.
            rows.Add(new CodexDetailRow(
                "blocksVision", $"{(catalogEntry.BlocksVision ? "TRUE" : "FALSE")} (미구현 — DEC-2026-07-28-03)"));
            return rows;
        }

        private static string BuildAuthoringWarning(
            CodexObjectEntry entry,
            MapObjectCatalogSet catalogSet,
            RuntimeMapObjectPrefabCatalog.Entry catalogEntry)
        {
            if (catalogSet == null)
            {
                return string.Empty;
            }

            if (catalogEntry == null)
            {
                return $"catalogRef '{entry.CatalogRef}'를 오브젝트 카탈로그에서 찾지 못했다 — "
                       + "프리팹이 지워졌거나 이름이 바뀌었다.";
            }

            return catalogEntry.Prefab == null
                ? $"catalogRef '{entry.CatalogRef}'의 프리팹 참조가 비었다 — 썸네일을 구울 수 없다."
                : string.Empty;
        }

        private static string KindLabel(CodexObjectKind kind)
        {
            switch (kind)
            {
                case CodexObjectKind.Interactive: return "상호작용";
                case CodexObjectKind.Landmark: return "랜드마크";
                default: return kind.ToString();
            }
        }
    }
}

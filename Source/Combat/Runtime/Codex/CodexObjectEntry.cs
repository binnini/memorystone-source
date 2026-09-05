using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 오브젝트 도감 항목의 갈래(P6 §8-1 = Q44 확정: <b>오브젝트 도메인 = 맵에 배치되는 것</b>).
    /// <para>
    /// ⚠️ 여기 <c>FieldEffect</c> 같은 값이 없는 것은 빠뜨린 것이 아니다. 피해·회복 장판은 맵 저작이
    /// 아니라 <c>cards.csv</c>의 <c>behaviorId</c>에서 나오고(<see cref="FieldObjectKind"/>) 이미 카드
    /// 도감에 실려 있다. 여기 넣으면 같은 것이 두 도메인에 뜬다.
    /// </para>
    /// </summary>
    public enum CodexObjectKind
    {
        /// <summary>밟으면 무언가 일어나는 것 — 기억결 · 보상뽑기 · 인형뽑기 · 잡화점.</summary>
        Interactive,

        /// <summary>
        /// 밟을 수는 없지만 서울이라는 무대를 읽게 하는 것 — 롯데타워 · 롯데캐슬 · 관람차 · 자이로드롭 · 한옥 정자.
        /// <para>
        /// 🔴 데이터로는 <b>일반 건물과 구별할 방법이 없다</b>(실측: <c>HexMapObjectType.Landmark</c>로
        /// 저작된 엔트리가 0개다 — 롯데타워도 펜스도 똑같이 <c>Building</c>이다). 그래서 무엇을
        /// 랜드마크로 볼지는 자동 판정이 아니라 <b>이 CSV에 행을 넣는 것</b>이 유일한 표현이다.
        /// ⚠️ 그래서 <b>사람이 고르는 목록</b>이다(Q50 확정). 지하철 출입구 · 버스 정류장 · 옥외 광고판 ·
        /// 도로 표지판은 "거리 집기"이지 랜드마크가 아니라는 판정으로 빠졌다 — 특히 표지판 둘은
        /// 칸에서 서로 구별되지도 않았다(Q52).
        /// </para>
        /// </summary>
        Landmark,
    }

    /// <summary>
    /// 맵 오브젝트 한 종의 도감 저작 한 행. 게임플레이 값(<c>blocksMovement</c> 등)은 여기 없다 —
    /// 그쪽 정본은 <c>MapObjectTypedCatalog</c>다(Q45-㉮). 이유는
    /// <see cref="CodexObjectCatalogCsv"/> 머리말에 있다.
    /// </summary>
    public sealed class CodexObjectEntry
    {
        public CodexObjectEntry(
            string id,
            string catalogRef,
            CodexObjectKind kind,
            string displayName,
            string description,
            string thumbnailRef,
            bool visibleInCatalog)
        {
            Id = string.IsNullOrWhiteSpace(id)
                ? throw new ArgumentException("Map object codex id is required.", nameof(id))
                : id;
            CatalogRef = string.IsNullOrWhiteSpace(catalogRef)
                ? throw new ArgumentException("Map object codex catalogRef is required.", nameof(catalogRef))
                : catalogRef;
            Kind = kind;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Description = description ?? string.Empty;
            ThumbnailRef = thumbnailRef ?? string.Empty;
            VisibleInCatalog = visibleInCatalog;
        }

        /// <summary>
        /// 안정 식별자이자 <b>해금 진행도의 저장 키</b>. 카탈로그 키(<see cref="CatalogRef"/>)와 일부러
        /// 다르다 — 임시 모델이 정식 모델로 바뀌어도 이 값은 그대로여야 진행도가 살아남는다.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// <c>MapObjectTypedCatalog.Entry.ObjectRef</c>. 프리팹·차단 여부·타입을 여기서 되찾는다.
        /// <para>
        /// ⚠️ 맵 소스의 오브젝트도 같은 이름의 필드를 쓰는데 <b>몬스터 스폰은 거기에
        /// <c>M001</c>을 담는다</b> — 이 값으로 무엇을 찾을 때는 반드시 오브젝트 카탈로그 쪽만 본다.
        /// </para>
        /// </summary>
        public string CatalogRef { get; }

        public CodexObjectKind Kind { get; }

        public string DisplayName { get; }

        public string Description { get; }

        /// <summary>비면 <c>codex_thumb_{Id}</c> 규약대로 <see cref="Id"/>에서 파생한다.</summary>
        public string ThumbnailRef { get; }

        /// <summary>
        /// 도감 격자에 칸을 내주는가. 장식 90종(건물 53 + 소품 37)을 거르기 위한 축이다.
        /// <para>
        /// ⚠️ 타입으로는 못 거른다 — <c>PropObjectCatalog</c>조차 <c>objectType</c>이
        /// <c>Building</c>이고 <c>categoryLabel</c>만 다르다(실측).
        /// </para>
        /// </summary>
        public bool VisibleInCatalog { get; }

        /// <summary>썸네일 파일 이름의 바탕이 되는 키.</summary>
        public string ResolveThumbnailKey() =>
            string.IsNullOrWhiteSpace(ThumbnailRef) ? Id : ThumbnailRef;
    }

    public sealed class CodexObjectCatalog
    {
        private readonly List<CodexObjectEntry> entries = new List<CodexObjectEntry>();
        private readonly Dictionary<string, CodexObjectEntry> byId =
            new Dictionary<string, CodexObjectEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, CodexObjectEntry> byCatalogRef =
            new Dictionary<string, CodexObjectEntry>(StringComparer.Ordinal);

        public CodexObjectCatalog(IEnumerable<CodexObjectEntry> items)
        {
            foreach (var item in items ?? Array.Empty<CodexObjectEntry>())
            {
                if (item == null)
                {
                    continue;
                }

                entries.Add(item);
                byId[item.Id] = item;
                byCatalogRef[item.CatalogRef] = item;
            }
        }

        public IReadOnlyList<CodexObjectEntry> Entries => entries;

        public bool TryGet(string id, out CodexObjectEntry entry)
        {
            entry = null;
            return !string.IsNullOrWhiteSpace(id) && byId.TryGetValue(id, out entry);
        }

        /// <summary>
        /// 카탈로그 키 → 도감 항목. <b>해금 신호가 쓰는 방향</b>이다 — 전투가 아는 것은 맵에 놓인
        /// <c>objectRef</c>뿐이고, 진행도에 적어야 하는 것은 안정 <c>id</c>다.
        /// </summary>
        public bool TryGetByCatalogRef(string catalogRef, out CodexObjectEntry entry)
        {
            entry = null;
            return !string.IsNullOrWhiteSpace(catalogRef) && byCatalogRef.TryGetValue(catalogRef, out entry);
        }
    }

    /// <summary>
    /// 오브젝트 도감 저작의 조회 façade — <see cref="ConsumableItemCatalog"/>와 같은 패턴.
    /// <para>
    /// 🔑 <b>왜 정적인가.</b> 이 표를 읽어야 하는 것은 해금 훑기(<c>SweepCodexSightings</c>)인데, 그
    /// 훑기는 <see cref="CombatState"/> 안에 있고 <see cref="CombatState"/>의 생성자는 이미 카탈로그
    /// 인자를 열넷 받는다. 도감 <b>표시용</b> 표 하나 때문에 전투 생성자를 또 넓히면 전투 시험
    /// 수십 개가 그 인자를 몰라서 고쳐야 한다 — 반면 이 표가 없을 때의 올바른 동작은 "신호를 안 보낸다"로
    /// 조용하고 안전하다.
    /// </para>
    /// <para>
    /// 에디터·EditMode는 소스 CSV를 지연 로딩하고, 플레이어 빌드는 부팅 시 <see cref="Register"/>로
    /// TextAsset 파싱 결과를 주입한다(빌드에는 <c>Assets/</c> 경로가 없다).
    /// </para>
    /// </summary>
    public static class CodexObjectCatalogSource
    {
        private static CodexObjectCatalog catalog;

        public static void Register(CodexObjectCatalog objectCatalog)
        {
            catalog = objectCatalog ?? throw new ArgumentNullException(nameof(objectCatalog));
        }

        /// <summary>시험이 끼워 넣은 표를 물린다 — 다음 조회가 소스에서 다시 읽는다.</summary>
        public static void ResetForTests() => catalog = null;

        public static CodexObjectCatalog Current => catalog ??= LoadFromSource();

        private static CodexObjectCatalog LoadFromSource()
        {
            try
            {
                if (System.IO.File.Exists(CombatCsvPaths.MapObjectsCsv))
                {
                    return CodexObjectCatalogCsv.ConvertFile(CombatCsvPaths.MapObjectsCsv);
                }
            }
            catch
            {
                // 소스 CSV가 없는 컨텍스트(Register 이전의 플레이어 빌드)는 빈 표로 폴백한다 —
                // 도감 신호가 안 나갈 뿐 전투는 그대로 돈다.
            }

            return new CodexObjectCatalog(Array.Empty<CodexObjectEntry>());
        }
    }
}

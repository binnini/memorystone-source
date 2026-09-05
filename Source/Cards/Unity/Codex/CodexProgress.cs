using System;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 도감 진행도 — "무엇을 만났는가"의 정본. 계획 정본은 <c>docs/codex-plan.md</c>(P3 해금).
    /// <para>
    /// 🔴<b>기존 세이브 둘과 다른 층이다.</b> <c>CombatSuspendStore</c>는 전투 한 판의 스냅샷이고
    /// <c>PlayerRunSaveStore</c>는 런 하나의 세이브다 — 둘 다 런이 끝나면 뜻이 없어진다. 이것은
    /// <b>런을 넘어 산다</b>. 지워지는 것은 플레이어가 직접 지울 때뿐이다.
    /// </para>
    /// <para>
    /// 어셈블리 자리: 모델은 여기(<c>SeoulPlayup.Cards.Unity</c>)에 있고, 전투가 쓰는 쪽은
    /// <see cref="ICodexSightingSink"/>라는 <b>아래층 인터페이스</b>로만 닿는다. 전투 런타임
    /// (<c>SeoulPlayup.Combat.Runtime</c>)은 이 어셈블리를 참조할 수 없다 — 이쪽이 그쪽을 참조하므로
    /// 반대 방향은 순환이다. 도감 뷰(<c>SeoulPlayup.Flow</c>)와 전투 배선(<c>SeoulPlayup.Combat</c>)은
    /// 둘 다 이 어셈블리를 보므로 여기가 양쪽이 만나는 유일한 자리다.
    /// </para>
    /// <para>
    /// 순수 C#이다(<c>UnityEngine</c> 없음) — 파일 입출력은 <see cref="CodexProgressStore"/>가 맡고
    /// 여기는 집합만 든다. 그래야 왕복 테스트가 에디터 밖 개념으로 성립한다.
    /// </para>
    /// </summary>
    public sealed class CodexProgress : ICodexSightingSink
    {
        private readonly Dictionary<string, HashSet<string>> seenByDomain =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>
        /// 마지막 저장 이후 새로 열린 것이 있는가. 저장은 신호마다가 아니라 <b>구간이 끝날 때</b>
        /// 한 번 하므로(전투 종료 · 로비 복귀 · 앱 종료) 그 사이의 변경 여부를 여기서 센다.
        /// </summary>
        public bool IsDirty { get; private set; }

        /// <summary>도메인 하나에서 열린 항목 수. 레일 수집률은 뷰가 <c>CodexListQuery.Tally</c>로 센다.</summary>
        public int CountFor(string domainId)
        {
            return seenByDomain.TryGetValue(Normalize(domainId), out var set) ? set.Count : 0;
        }

        /// <summary>전 도메인을 통틀어 열린 항목 수. 저장 왕복 검증과 디버그 표시에 쓴다.</summary>
        public int TotalCount
        {
            get
            {
                var total = 0;
                foreach (var pair in seenByDomain)
                {
                    total += pair.Value.Count;
                }

                return total;
            }
        }

        /// <inheritdoc />
        public void MarkSeen(string domainId, string entryId)
        {
            var domain = Normalize(domainId);
            var entry = Normalize(entryId);
            if (domain.Length == 0 || entry.Length == 0)
            {
                // 빈 id를 걸러 내는 것이 여기인 이유: 호출부는 훑기라서 매번 검사하면 훑기가 지저분해진다.
                // 특히 프리셋 없는 런타임 함정(보스 배치)이 빈 presetId로 들어온다.
                return;
            }

            if (!seenByDomain.TryGetValue(domain, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                seenByDomain[domain] = set;
            }

            // 집합이라 두 번째부터는 아무 일도 일어나지 않는다 — IsDirty도 그래야 한다.
            // 안 그러면 훑기가 매 페이즈마다 저장을 부른다.
            if (set.Add(entry))
            {
                IsDirty = true;
            }
        }

        /// <summary>
        /// 이 항목이 열려 있는가. 뷰는 이것을 <c>CodexListQuery.Apply</c>·<c>Tally</c>에 술어로 넘긴다.
        /// </summary>
        public bool IsUnlocked(string domainId, string entryId)
        {
            var domain = Normalize(domainId);
            var entry = Normalize(entryId);
            return domain.Length != 0
                && entry.Length != 0
                && seenByDomain.TryGetValue(domain, out var set)
                && set.Contains(entry);
        }

        /// <summary>
        /// 도메인이 내놓은 항목에 쓸 해금 술어. <c>AlwaysUnlocked</c> 도메인(상태이상 · 개발용 형상)은
        /// 통째로 열린 것으로 본다 — Q6 확정. 규칙 참조표를 잠그면 플레이어가 규칙을 못 읽는다.
        /// </summary>
        public Func<CodexEntry, bool> PredicateFor(ICodexDomain domain)
        {
            if (domain == null || domain.AlwaysUnlocked)
            {
                return null;
            }

            var domainId = domain.Id;
            return entry => entry != null && IsUnlocked(domainId, entry.Id);
        }

        /// <summary>저장용 평면 스냅샷. 도메인 순서·항목 순서를 저작 순서가 아니라 정렬로 고정한다 —
        /// 같은 진행도가 매번 같은 파일이 돼야 diff로 확인할 수 있다.</summary>
        public CodexProgressSaveData ToSaveData()
        {
            var data = new CodexProgressSaveData();
            var domainIds = new List<string>(seenByDomain.Keys);
            domainIds.Sort(StringComparer.Ordinal);

            foreach (var domainId in domainIds)
            {
                var entries = new List<string>(seenByDomain[domainId]);
                entries.Sort(StringComparer.Ordinal);
                data.Domains.Add(new CodexProgressDomainSaveData { DomainId = domainId, EntryIds = entries });
            }

            return data;
        }

        /// <summary>
        /// 스냅샷을 덮어쓴다(합치지 않는다). 로드가 곧 "이 파일이 진행도의 전부"라는 뜻이라야
        /// 왕복이 항등이 된다.
        /// </summary>
        public void LoadFrom(CodexProgressSaveData data)
        {
            seenByDomain.Clear();
            IsDirty = false;
            if (data?.Domains == null)
            {
                return;
            }

            foreach (var domain in data.Domains)
            {
                if (domain?.EntryIds == null)
                {
                    continue;
                }

                foreach (var entryId in domain.EntryIds)
                {
                    MarkSeen(domain.DomainId, entryId);
                }
            }

            // 로드로 생긴 변경은 저장할 거리가 아니다 — 방금 읽은 그대로다.
            IsDirty = false;
        }

        /// <summary>저장을 마쳤다고 알린다. 저장에 실패했으면 부르지 않는다(다음 기회에 다시 쓴다).</summary>
        public void MarkSaved()
        {
            IsDirty = false;
        }

        public void Clear()
        {
            if (seenByDomain.Count > 0)
            {
                seenByDomain.Clear();
                IsDirty = true;
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>
    /// 직렬화 평면형. <c>JsonUtility</c>는 <c>Dictionary</c>도 <c>HashSet</c>도 못 다루므로
    /// 목록의 목록으로 편다(<c>CombatSuspendData</c>가 쓰는 것과 같은 규약).
    /// </summary>
    [Serializable]
    public sealed class CodexProgressSaveData
    {
        /// <summary>파일 형식 판. 늘릴 때 마이그레이션을 같이 짤 것.</summary>
        public int Version = 1;

        public List<CodexProgressDomainSaveData> Domains = new List<CodexProgressDomainSaveData>();
    }

    [Serializable]
    public sealed class CodexProgressDomainSaveData
    {
        public string DomainId = string.Empty;
        public List<string> EntryIds = new List<string>();
    }
}

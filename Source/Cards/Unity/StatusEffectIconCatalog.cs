using System;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Maps each <see cref="StatusEffectKind"/> to a UI sprite. Shared by the CardLane status-effect dock
    /// and the map overlay so status iconography stays separate from instant/system effects.
    /// Kinds without an assigned sprite fall back to the glyph + kind colour rendered by the dock.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Combat/Status Effect Icon Catalog", fileName = "StatusEffectIconCatalog")]
    public sealed class StatusEffectIconCatalog : ScriptableObject
    {
        [Serializable]
        private sealed class Entry
        {
            [SerializeField] private StatusEffectKind kind;
            [SerializeField] private Sprite sprite;

            public Entry()
            {
            }

            public Entry(StatusEffectKind kind, Sprite sprite)
            {
                this.kind = kind;
                this.sprite = sprite;
            }

            public StatusEffectKind Kind => kind;
            public Sprite Sprite => sprite;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        [Tooltip("Icon telegraphed on tiles where a monster attack will knock the player back. " +
                 "Knockback is not a StatusEffectKind, so it has a dedicated slot.")]
        /// <summary>특성 아이콘 한 칸. <see cref="Entry"/>(상태이상 kind 키)의 형제이며 키가 문자열 traitId다.</summary>
        [System.Serializable]
        public sealed class TraitEntry
        {
            [SerializeField] private string traitId;
            [SerializeField] private Sprite sprite;

            public TraitEntry()
            {
            }

            public TraitEntry(string traitId, Sprite sprite)
            {
                this.traitId = traitId;
                this.sprite = sprite;
            }

            public string TraitId => traitId;
            public Sprite Sprite => sprite;
        }

        [SerializeField] private Sprite knockbackSprite;

        [Tooltip("전멸기 안전지대의 미판별 후보에 뜨는 '?' 아이콘(§20-B-6). 상태이상이 아니라 맵 표식이라 " +
                 "밀치기와 같은 전용 슬롯을 쓴다. 비어 있으면 폰트 글리프 폴백으로 그려진다.\n" +
                 "⚠️ 아트는 받침(판) 없이 글리프만이어야 하고, 대비를 위해 어두운 키라인을 아트에 " +
                 "구워 넣어야 한다 — 받침을 되살리면 '칸과 같은 색이라 사라지는' 문제가 되돌아온다.")]
        [SerializeField] private Sprite safeZoneCandidateSprite;
        // §28 W4: 취약 부위 전용 아이콘(발주 대기). 비어 있으면 렌더러가 '약' 글리프 폴백을 그린다.
        [SerializeField] private Sprite weakSpotSprite;

        [Tooltip("전멸기 폭발 예고 칸에 뜨는 '!' 아이콘(§28 W5). 경고 채움 타일 위에 얹히므로 " +
                 "안전지대 '?'와 같은 규격 — 받침(판) 없이 글리프만, 어두운 키라인을 아트에 굽는다. " +
                 "비어 있으면 금색 '!' 글리프 폴백으로 그려진다.")]
        [SerializeField] private Sprite annihilationWarningSprite;

        [Tooltip("끌어당김(밀치기의 반대 방향) 맵 표식. 밀치기와 같은 축이지만 부호가 반대라 " +
                 "그림이 갈라져야 한다 — 밀치기 스프라이트의 좌우 미러로 때우면 '미는 힘'의 " +
                 "그림이 그대로 남아 오히려 헷갈린다. 비어 있으면 밀치기 폴백으로 떨어진다.")]
        [SerializeField] private Sprite pullSprite;

        [Tooltip("「저주 부여」 타일 표식(2026-09-01 W2). 이 칸에 서면 덱에 저주 카드가 섞인다는 예고다.\n" +
                 "🔑 상태이상이 아니라 맵 표식이라 밀치기·끌어당김과 같은 전용 슬롯을 쓴다 — 저주는 " +
                 "턴이 지나 풀리지 않고 덱에 영구히 남으므로 상태이상 어휘를 빌리면 안 된다.\n" +
                 "살아서 때리는 저주(A035·A028)와 죽으면서 남기는 저주(두두리 뒤끝)는 같은 그림을 쓴다 " +
                 "(2026-09-01 사용자 확정). 비어 있으면 「저」 글자 폴백으로 그려진다.")]
        [SerializeField] private Sprite curseSprite;

        // 아래 4개는 몬스터 네임플레이트 배지 전용 슬롯(§H). StatusEffectKind가 아니라 의도/몸에
        // 붙는 값이라 entries(kind 키)에 넣을 수 없다 — knockbackSprite·weakSpotSprite와 같은 선례다.
        // 비어 있으면 CombatActorMarkerPresenter가 글자 칩(「공」「약」「맷」「견」) 폴백을 그린다.

        [Tooltip("이번 턴 공격 의도 배지(NB-1). 옆에 실효 피해 숫자가 붙으므로 숫자와 시선을 다투면 안 된다.")]
        [SerializeField] private Sprite attackIntentSprite;

        [Tooltip("약오름 배지(NB-2) — 약이 올라 피해가 오른 상태. 옆에 스택 숫자가 붙는다.")]
        [SerializeField] private Sprite agitationSprite;

        [Tooltip("맷집 배지(NB-4) — 타고난 단단함(첫 피격 반감·1회). 준비/소진은 색이 아니라 " +
                 "스프라이트 유무와 무관하게 렌더러가 가른다. 견고와 어휘가 다르므로 그림도 갈라야 한다.")]
        [SerializeField] private Sprite toughnessSprite;

        [Tooltip("견고 배지(NB-5) — 쌓은 방어막이 턴이 지나도 안 풀린다. 방어막 계열에 더 짙게.")]
        [SerializeField] private Sprite sturdySprite;

        [Tooltip("방어막 배지 — 몬스터의 CombatantState.Block 잔량.\n" +
                 "🔑 이 슬롯은 아트를 새로 발주하지 않는다(§H-1 확정). 플레이어 HUD 방어도와 " +
                 "**같은 기능**이므로 같은 그림(Assets/Art/UI/Icons/ui_shield.png)을 재사용한다 — " +
                 "같은 어휘를 다른 그림으로 그리면 플레이어가 두 개념으로 읽는다.\n" +
                 "⚠️ 반사(Reflect)도 온전한 방패라 실루엣이 겹친다. 20px 실측에서 반사의 " +
                 "화살과 마젠타가 남아 구별되는 것을 확인했다(2026-08-10) — 반사 아트를 " +
                 "바꾸게 되면 이 겹침을 다시 판정할 것.")]
        [SerializeField] private Sprite blockSprite;

        [Tooltip("뒤끝 배지(NB-6) — 죽일 때 마지막 수를 남기는 특성. 20px 배지가 주 무대라 " +
                 "형상 하나로 읽혀야 한다. 비어 있으면 「뒤」 글자 칩 폴백으로 그려진다.")]
        [SerializeField] private Sprite aftermathSprite;

        /// <summary>
        /// 특성 아이콘 슬롯(2026-09-04) — 키는 monster_traits.csv의 traitId다. 종전에는 특성 하나에
        /// 이름 붙은 필드 하나였는데(agitationSprite·toughnessSprite…), 그러면 특성이 늘 때마다
        /// 카탈로그·프리젠터·테스트가 함께 늘어난다. 목록으로 두면 <b>아트가 오면 한 줄</b>이다.
        /// 비어 있는 특성은 글리프 칩으로 떨어진다.
        /// </summary>
        [SerializeField] private TraitEntry[] traitEntries = new TraitEntry[0];

        public Sprite GetSprite(StatusEffectKind kind)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].Kind == kind)
                {
                    return entries[i].Sprite;
                }
            }

            return null;
        }

        public Sprite KnockbackSprite => knockbackSprite;

        /// <summary>미판별 안전지대 후보의 <c>?</c> 아이콘(없으면 폰트 글리프 폴백).</summary>
        public Sprite SafeZoneCandidateSprite => safeZoneCandidateSprite;

        public Sprite WeakSpotSprite => weakSpotSprite;

        /// <summary>전멸기 폭발 예고의 <c>!</c> 아이콘(없으면 금색 글리프 폴백).</summary>
        public Sprite AnnihilationWarningSprite => annihilationWarningSprite;

        /// <summary>끌어당김 표식(없으면 밀치기 스프라이트 → 글리프 폴백 순으로 떨어진다).</summary>
        public Sprite PullSprite => pullSprite;

        /// <summary>「저주 부여」 타일 표식(없으면 「저」 글자 폴백).</summary>
        public Sprite CurseSprite => curseSprite;

        /// <summary>공격 의도 배지 아이콘(없으면 「공」 글자 칩 폴백).</summary>
        public Sprite AttackIntentSprite => attackIntentSprite;

        /// <summary>약오름 배지 아이콘(없으면 「약」 글자 칩 폴백).</summary>
        public Sprite AgitationSprite => agitationSprite;

        /// <summary>맷집 배지 아이콘(없으면 「맷」 글자 칩 폴백).</summary>
        public Sprite ToughnessSprite => toughnessSprite;

        /// <summary>견고 배지 아이콘(없으면 「견」 글자 칩 폴백).</summary>
        public Sprite SturdySprite => sturdySprite;

        /// <summary>방어막 배지 아이콘 — 플레이어 방어도와 같은 그림을 재사용한다(없으면 「막」 글자 칩 폴백).</summary>
        public Sprite BlockSprite => blockSprite;

        /// <summary>뒤끝 배지 아이콘(없으면 「뒤」 글자 칩 폴백).</summary>
        public Sprite AftermathSprite => aftermathSprite;

        /// <summary>traitId로 특성 아이콘을 찾는다. 없으면 null — 호출부가 글리프 칩으로 떨어진다.</summary>
        public Sprite GetTraitSprite(string traitId)
        {
            if (string.IsNullOrEmpty(traitId) || traitEntries == null)
            {
                return null;
            }

            for (var i = 0; i < traitEntries.Length; i++)
            {
                if (traitEntries[i] != null
                    && string.Equals(traitEntries[i].TraitId, traitId, System.StringComparison.Ordinal))
                {
                    return traitEntries[i].Sprite;
                }
            }

            return null;
        }

#if UNITY_INCLUDE_TESTS
        internal static StatusEffectIconCatalog CreateForTests(params (StatusEffectKind Kind, Sprite Sprite)[] mappings)
        {
            var catalog = CreateInstance<StatusEffectIconCatalog>();
            catalog.entries = new Entry[mappings.Length];
            for (var i = 0; i < mappings.Length; i++)
            {
                catalog.entries[i] = new Entry(mappings[i].Kind, mappings[i].Sprite);
            }

            return catalog;
        }

        internal void SetKnockbackSpriteForTests(Sprite sprite) => knockbackSprite = sprite;

        internal void SetPullSpriteForTests(Sprite sprite) => pullSprite = sprite;

        internal void SetCurseSpriteForTests(Sprite sprite) => curseSprite = sprite;

        internal void SetTraitSpritesForTests(params (string TraitId, Sprite Sprite)[] mappings)
        {
            traitEntries = new TraitEntry[mappings?.Length ?? 0];
            for (var i = 0; i < traitEntries.Length; i++)
            {
                traitEntries[i] = new TraitEntry(mappings[i].TraitId, mappings[i].Sprite);
            }
        }

        internal void SetBadgeSpritesForTests(
            Sprite attackIntent = null,
            Sprite agitation = null,
            Sprite toughness = null,
            Sprite sturdy = null,
            Sprite block = null,
            Sprite aftermath = null)
        {
            attackIntentSprite = attackIntent;
            agitationSprite = agitation;
            toughnessSprite = toughness;
            sturdySprite = sturdy;
            blockSprite = block;
            aftermathSprite = aftermath;
        }
#endif
    }
}

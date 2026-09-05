using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>썸네일이 어느 층에서 해소됐는가(<c>docs/codex-plan.md</c> §4.3).</summary>
    public enum CodexThumbnailLayer
    {
        /// <summary>1층 — 이 항목 전용으로 저작된 아트.</summary>
        Dedicated,

        /// <summary>2층 — 도메인 색조 + 이름 전체를 그리는 절차 생성 폴백(Q9 확정).</summary>
        ProceduralLabel,

        /// <summary>3층 — 이름조차 없을 때의 기본 이미지.</summary>
        Default,
    }

    /// <summary>
    /// 해소된 썸네일 한 장. <see cref="Layer"/>가 <see cref="CodexThumbnailLayer.Dedicated"/>가 아니면
    /// 디버그 뷰가 <c>FB</c> 배지를 단다 — 아트 발주 진척이 별도 추적표 없이 도감에서 보이는 것이
    /// 이 필드의 목적이다.
    /// </summary>
    public readonly struct CodexThumbnail
    {
        private CodexThumbnail(CodexThumbnailLayer layer, Sprite sprite, string label, Color tone, bool preserveAspect)
        {
            Layer = layer;
            Sprite = sprite;
            Label = label ?? string.Empty;
            Tone = tone;
            PreserveAspect = preserveAspect;
        }

        public CodexThumbnailLayer Layer { get; }

        /// <summary>1·3층에서만 채워진다. 2층은 <see langword="null"/>이고 <see cref="Label"/>을 그린다.</summary>
        public Sprite Sprite { get; }

        /// <summary>2층이 그리는 문구 = <b>이름 전체</b>. 첫 글자만 쓰지 않는다(Q9).</summary>
        public string Label { get; }

        /// <summary>폴백 배경 색조이자 셀 표시등 색. 도메인 강조색을 그대로 받는다.</summary>
        public Color Tone { get; }

        /// <summary>
        /// 아이콘처럼 여백을 남겨 통째로 보여야 하는가(<c>true</c>), 사진처럼 꽉 채워 잘라도 되는가.
        /// 상태이상 아이콘은 잘리면 뜻이 사라지므로 <c>true</c>다.
        /// </summary>
        public bool PreserveAspect { get; }

        public bool IsFallback => Layer != CodexThumbnailLayer.Dedicated;

        /// <summary>
        /// 3층 폴백을 순서대로 시도한다. 전용 아트 → 이름 전체 → 기본 이미지.
        /// <para>
        /// 이름이 비어 있고 기본 이미지도 없으면 <c>?</c>를 그린다 — 빈 칸을 내놓지 않는 것이
        /// 폴백의 존재 이유이므로, 어떤 입력에도 그릴 것이 남아야 한다.
        /// </para>
        /// </summary>
        public static CodexThumbnail Resolve(
            Sprite dedicated,
            string displayName,
            Color tone,
            Sprite defaultSprite = null,
            bool preserveAspect = false)
        {
            if (dedicated != null)
            {
                return new CodexThumbnail(CodexThumbnailLayer.Dedicated, dedicated, displayName, tone, preserveAspect);
            }

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return new CodexThumbnail(CodexThumbnailLayer.ProceduralLabel, null, displayName.Trim(), tone, false);
            }

            if (defaultSprite != null)
            {
                return new CodexThumbnail(CodexThumbnailLayer.Default, defaultSprite, string.Empty, tone, preserveAspect);
            }

            return new CodexThumbnail(CodexThumbnailLayer.ProceduralLabel, null, "?", tone, false);
        }
    }
}

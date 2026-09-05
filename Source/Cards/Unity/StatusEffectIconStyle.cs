using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Shared glyph + background styling for player status-effect icons. Keeps the CardLane
    /// status-effect dock visually consistent with the combat HUD.
    /// </summary>
    public static class StatusEffectIconStyle
    {
        /// <summary>
        /// 스프라이트가 없을 때만 보이는 글자 폴백. 정본은 <c>status_effects.csv</c>의 <c>glyph</c>(1단계 구조 리팩토링) —
        /// 여기 있던 20 case switch는 <see cref="StatusEffectInfo"/> 폴백으로 옮겼다. 색은 옮기지 않는다(사용자 결정, 아래 존치).
        /// </summary>
        public static string Glyph(StatusEffectKind kind) => StatusEffectInfo.Glyph(kind);

        public static Color BackgroundColor(StatusEffectKind kind, Color fallback)
        {
            switch (kind)
            {
                case StatusEffectKind.Immobilize: return new Color(0.24f, 0.5f, 0.86f, 0.72f);
                case StatusEffectKind.Poison: return new Color(0.36f, 0.56f, 0.18f, 0.72f);
                case StatusEffectKind.Stun: return new Color(0.96f, 0.74f, 0.18f, 0.72f);
                case StatusEffectKind.Slow: return new Color(0.18f, 0.45f, 0.86f, 0.72f);
                case StatusEffectKind.Rupture: return new Color(0.72f, 0.08f, 0.12f, 0.72f);
                case StatusEffectKind.Reflect: return new Color(0.78f, 0.56f, 0.96f, 0.72f);
                case StatusEffectKind.Agility: return new Color(0.1f, 0.72f, 0.72f, 0.72f);
                // 무적(I-19): 금빛 — 방어 계열이되 방어막(회백)·수호(부적)와 갈리는 「이번 턴 절대 방어」.
                case StatusEffectKind.Invincible: return new Color(0.92f, 0.78f, 0.28f, 0.72f);
                default: return fallback;
            }
        }
    }
}

using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 네임플레이트 배지 호버 툴팁의 제목·본문. <see cref="ObjectInfoTooltipContent"/>(타일 표식)와
    /// <see cref="StatusEffectTooltipContent"/>(상태이상 어휘)의 형제 — 상태 배지는 이미 있는 그 문구를
    /// 그대로 재사용하고, 여기서 새로 쓰는 것은 의도 배지(공격·다단·밀치기)와 방어막뿐이다.
    ///
    /// <para>🔑 <b>특성 문장은 여기에 없다</b>(2026-09-04). 규칙 문장의 정본은 game_keywords.csv이고
    /// <see cref="MonsterTraitText"/>가 그것을 편다 — 종전에는 같은 특성을 카탈로그와 이 파일이
    /// 각각 자기 문장으로 설명했고(D6), 뜻이 같아 티가 안 났을 뿐 한쪽만 고치는 날 갈라진다.
    /// 이 파일이 특성에 대해 여전히 아는 것은 <b>지금 상태</b> 한 줄뿐이다.</para>
    /// </summary>
    internal static class MonsterBadgeTooltipContent
    {
        private static readonly Color CategoryColor = new Color(0.66f, 0.72f, 0.82f, 1f);
        private static readonly Color BodyColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private const string IntentCategory = "이번 턴 행동 의도";

        /// <summary>
        /// 약오름·맷집·견고의 분류명. 코드 안에서는 "적 문법(T7-2)"이라 부르지만 플레이어에게는
        /// <b>특성</b>이다(사용자 확정 2026-08-10) — 특정 몬스터가 고유하게 갖는 성질이라 외부에서
        /// 걸리지도, 정화로 지워지지도 않는다는 뜻이 그 한 단어에 담긴다.
        /// </summary>
        private const string TraitCategory = "특성";

        /// <summary>
        /// 특성 배지 본문. <b>규칙 문장은 카탈로그가 쓴다</b>(game_keywords.csv) — 여기서 문장을 다시
        /// 적으면 그 순간 두 벌이 되고, 그게 D6이었다. 이 함수가 더하는 것은 카탈로그가 알 수 없는
        /// <b>지금 상태</b> 한 줄뿐이다("소진했다" · "저주 2장을 남긴다" · "엽전 15를 훔쳐 갔다").
        /// </summary>
        private static List<ObjectInfoTooltipHudPresenter.Line> BuildTraitLines(
            MonsterNameplateBadge badge,
            CombatState.MonsterAftermathPreview? aftermath,
            int descriptionValue)
        {
            var lines = new List<ObjectInfoTooltipHudPresenter.Line>(3)
            {
                new ObjectInfoTooltipHudPresenter.Line(TraitCategory, CategoryColor),
            };

            if (MonsterTraitText.TryGet(badge.TraitId, descriptionValue, out _, out var description)
                && !string.IsNullOrWhiteSpace(description))
            {
                lines.Add(new ObjectInfoTooltipHudPresenter.Line(description, BodyColor));
            }

            var state = ResolveTraitStateLine(badge, aftermath);
            if (!string.IsNullOrEmpty(state))
            {
                lines.Add(new ObjectInfoTooltipHudPresenter.Line(state, BodyColor));
            }

            return lines;
        }

        /// <summary>
        /// 「규칙은 저러한데 <b>지금은</b> 어떤가」 — <b>수치가 있는 현재 상태만</b> 말한다(2026-09-05 실플레이 3차 #5-1:
        /// 규칙 되풀이·뒤끝 갈래 설명 같은 부가 문장은 전부 걷어냈다). 힘 N 증가 · 소매치기 금액 N원 · 맷집 충전.
        /// </summary>
        private static string ResolveTraitStateLine(
            MonsterNameplateBadge badge, CombatState.MonsterAftermathPreview? aftermath)
        {
            switch (badge.TraitId)
            {
                case MonsterTraitIds.Toughness:
                    return badge.Dimmed ? "충전 중" : "충전 완료";

                case MonsterTraitIds.Agitation:
                case MonsterTraitIds.StrengthDistance:
                    return badge.Amount > 0 ? $"힘 {badge.Amount} 증가" : string.Empty;

                case MonsterTraitIds.Pickpocket:
                    return aftermath.HasValue && aftermath.Value.StolenMoney > 0
                        ? $"소매치기 금액 {aftermath.Value.StolenMoney}원"
                        : string.Empty;

                default:
                    return string.Empty;
            }
        }

        public static string Title(MonsterNameplateBadge badge)
        {
            switch (badge.Kind)
            {
                case MonsterNameplateBadgeKind.Trait:
                    return MonsterTraitText.TryGet(badge.TraitId, 0, out var traitTitle, out _)
                        ? traitTitle
                        : string.Empty;
                case MonsterNameplateBadgeKind.Attack:
                    return "공격";
                case MonsterNameplateBadgeKind.MultiHit:
                    return "다단 히트";
                case MonsterNameplateBadgeKind.Knockback:
                    return badge.Amount < 0
                        ? ObjectInfoTooltipContent.PullTitle
                        : ObjectInfoTooltipContent.KnockbackTitle;
                case MonsterNameplateBadgeKind.Shield:
                    return "방어막";
                case MonsterNameplateBadgeKind.SelfBuffStatus:
                    return StatusEffectTooltipContent.Title(badge.SelfBuffKind);
                case MonsterNameplateBadgeKind.SelfBuffShield:
                    return "방어막";
                case MonsterNameplateBadgeKind.BossGimmick:
                    return BossGimmickBadgeText.Title(badge.GimmickId);
                case MonsterNameplateBadgeKind.PropMaturity:
                    return "흡수 카운트다운";
                default:
                    return StatusEffectTooltipContent.Title(badge.Effect.Kind);
            }
        }

        public static List<ObjectInfoTooltipHudPresenter.Line> BuildLines(
            MonsterNameplateBadge badge,
            CombatState.MonsterAftermathPreview? aftermath = null,
            int descriptionValue = 0)
        {
            switch (badge.Kind)
            {
                case MonsterNameplateBadgeKind.Trait:
                    return BuildTraitLines(badge, aftermath, descriptionValue);
                case MonsterNameplateBadgeKind.Attack:
                {
                    // 2026-09-02 #10: 다단 히트 배지를 없앴으므로 그 배지가 말하던 것(몇 번 때리는가 ·
                    // 표시된 피해는 1회분이라는 것)을 여기서 대신 말한다 — 배지만 지우면 설명이 사라진다.
                    var attackLines = new List<ObjectInfoTooltipHudPresenter.Line>
                    {
                        new ObjectInfoTooltipHudPresenter.Line(IntentCategory, CategoryColor),
                        new ObjectInfoTooltipHudPresenter.Line($"이번 턴에 {badge.Amount}의 피해를 줍니다.", BodyColor),
                    };
                    if (badge.HitCount > 1)
                    {
                        attackLines.Add(new ObjectInfoTooltipHudPresenter.Line(
                            $"공격이 {badge.HitCount}회 반복됩니다(표시된 피해는 1회분).", BodyColor));
                    }

                    return attackLines;
                }
                case MonsterNameplateBadgeKind.MultiHit:
                    return new List<ObjectInfoTooltipHudPresenter.Line>
                    {
                        new ObjectInfoTooltipHudPresenter.Line(IntentCategory, CategoryColor),
                        new ObjectInfoTooltipHudPresenter.Line($"공격이 {badge.Amount}회 반복됩니다.", BodyColor),
                    };
                case MonsterNameplateBadgeKind.Knockback:
                    var distance = Mathf.Abs(badge.Amount);
                    var direction = badge.Amount < 0 ? "끌어당깁니다" : "밀어냅니다";
                    return new List<ObjectInfoTooltipHudPresenter.Line>
                    {
                        new ObjectInfoTooltipHudPresenter.Line(IntentCategory, CategoryColor),
                        new ObjectInfoTooltipHudPresenter.Line($"맞으면 {distance}칸 {direction}.", BodyColor),
                    };
                case MonsterNameplateBadgeKind.Shield:
                    return new List<ObjectInfoTooltipHudPresenter.Line>
                    {
                        new ObjectInfoTooltipHudPresenter.Line("방어", CategoryColor),
                        new ObjectInfoTooltipHudPresenter.Line($"피해를 {badge.Amount}까지 먼저 막아냅니다.", BodyColor),
                    };
                case MonsterNameplateBadgeKind.SelfBuffStatus:
                    return new List<ObjectInfoTooltipHudPresenter.Line>
                    {
                        new ObjectInfoTooltipHudPresenter.Line(IntentCategory, CategoryColor),
                        new ObjectInfoTooltipHudPresenter.Line(
                            badge.Amount > 0
                                ? $"이번 턴에 자신에게 「{StatusEffectTooltipContent.Title(badge.SelfBuffKind)}」을 {badge.Amount}턴 겁니다."
                                : $"이번 턴에 자신에게 「{StatusEffectTooltipContent.Title(badge.SelfBuffKind)}」을 겁니다.",
                            BodyColor),
                    };
                case MonsterNameplateBadgeKind.SelfBuffShield:
                    return new List<ObjectInfoTooltipHudPresenter.Line>
                    {
                        new ObjectInfoTooltipHudPresenter.Line(IntentCategory, CategoryColor),
                        new ObjectInfoTooltipHudPresenter.Line($"이번 턴에 방어막 {badge.Amount}을 두릅니다.", BodyColor),
                    };
                case MonsterNameplateBadgeKind.BossGimmick:
                    return new List<ObjectInfoTooltipHudPresenter.Line>
                    {
                        new ObjectInfoTooltipHudPresenter.Line(IntentCategory, CategoryColor),
                        new ObjectInfoTooltipHudPresenter.Line(BossGimmickBadgeText.Description(badge.GimmickId), BodyColor),
                    };
                case MonsterNameplateBadgeKind.PropMaturity:
                    return new List<ObjectInfoTooltipHudPresenter.Line>
                    {
                        new ObjectInfoTooltipHudPresenter.Line(IntentCategory, CategoryColor),
                        new ObjectInfoTooltipHudPresenter.Line(
                            badge.Amount <= 0
                                ? "다음 몬스터 페이즈에 보스에게 흡수되어 폭발합니다 — 지금이 마지막 기회입니다."
                                : $"{badge.Amount}턴 뒤 보스에게 흡수되어 폭발합니다. 그 전에 부수면 보스가 성장하지 못합니다.",
                            BodyColor),
                    };
                default:
                    return ObjectInfoTooltipContent.BuildStatusEffectLines(badge.Effect);
            }
        }
    }
}

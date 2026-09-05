using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    // Player-facing content for the object-info tooltip panel (status icons, knockback, scout-
    // revealed traps, treasure chests, memory stones): titles, palette, and line building.
    // Extracted from MapCombatController (P4 Stage 3); the hover judgment that decides *when* to
    // show these stays on the controller. Sibling of StatusEffectTooltipContent, which owns the
    // per-status-effect title/category/description strings.
    internal static class ObjectInfoTooltipContent
    {
        public static readonly Color StatusIconTitleColor = new Color(1f, 0.92f, 0.66f, 1f);
        private static readonly Color StatusIconCategoryColor = new Color(0.66f, 0.72f, 0.82f, 1f);
        private static readonly Color StatusIconEffectColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color StatusIconTurnsColor = new Color(1f, 0.86f, 0.48f, 1f);
        // W-08(WS-I, 어휘 표준): 카드·도감과 같은 「밀치기」로 통일 — 「넉백」은 별칭으로만 남는다.
        public const string KnockbackTitle = "밀치기";
        private const string KnockbackCategory = "제어 효과";
        // I-15: 밀치기는 「인접한 무작위 타일」이 아니다 — 공격자 반대 방향으로 표기된 칸수만큼 결정적으로 밀린다.
        private const string KnockbackDescription = "공격 반대 방향으로 표기된 칸수만큼 밀어냅니다.";
        private const string KnockbackHint = "밀려날 칸이 막혀 있으면 더 먼 칸으로 밀려나고, 이동할 수 없는 경우 제자리에 머뭅니다.";
        /// <summary>끌어당김(§16.1 음수 변위) — 밀치기와 <b>반대 방향</b>이라 제목·본문이 갈린다.</summary>
        public const string PullTitle = "끌어당김";
        private const string PullDescription = "대상을 공격자 쪽으로 끌어당깁니다.";
        private const string PullHint = "빈 칸이 없으면 더 가까운 칸까지만 끌려옵니다. 이동할 수 없는 경우 제자리에 머뭅니다.";
        /// <summary>「저주 부여」 타일 표식의 호버 제목(2026-09-01 W2).</summary>
        public const string CurseTitle = "저주 부여";
        private const string CurseCategory = "덱 오염";
        // 🔴 어느 카드가 오는지는 말하지 않는다 — 풀 추첨(A035 속삭임)은 명중 시점에 일어나므로
        // 예고가 앞질러 확정하면 화면이 거짓말을 한다.
        private const string CurseDescription = "이 공격에 맞으면 저주 카드가 덱에 섞입니다.";
        private const string CurseHint = "저주는 턴이 지나도 사라지지 않습니다. 상점에서 카드를 제거해야 없앨 수 있습니다.";
        public const string AnnihilationTitle = "전멸의 포효";
        public const string SafeZoneCandidateTitle = "안전지대 후보";
        public const string SafeZoneConfirmedTitle = "안전지대 확정";
        public const string WeakSpotTitle = "취약 부위";
        private const string AnnihilationCategory = "보스 기믹";
        private const string AnnihilationHint = "표시가 없는 칸으로 피해야 합니다. 반드시 도달할 수 있는 안전지대가 있습니다.";
        /// <summary>
        /// 🔑 <b>이름이 "보물상자"가 아닌 이유</b>(2026-08-09 사용자 확정, P6). 이 기물이 맵에서
        /// 실제로 쓰는 모델은 <c>ClawMachine_5000.fbx</c>(인형뽑기)이고, 여는 순간 돌리는 것도
        /// 상자 전용 표가 아니라 <see cref="GachaRewardRoller"/>의 뽑기 표다 — 코드 쪽은
        /// 이미 "상자는 곧 인형뽑기다(D-3)"라고 적어 두고 있었다. 화면에만 상자라고 적혀 있어
        /// 플레이어가 보는 것과 이름이 갈라져 있었다.
        /// <para>
        /// ⚠️ 내부 식별자(<c>HexMapObjectType.TreasureChest</c> · <c>objectType: "TreasureChest"</c> ·
        /// 맵 소스의 <c>treasurechest-…</c> objectId)는 <b>그대로 둔다</b> — 직렬화된 맵 저작과
        /// 세이브가 그 문자열에 묶여 있고, 이번 결정은 <b>표시 이름</b>에 관한 것이다.
        /// </para>
        /// </summary>
        public const string TreasureChestTitle = "보상뽑기";
        public static readonly Color TreasureTitleColor = new Color(1f, 0.84f, 0.4f, 1f);
        private static readonly Color TreasureBodyColor = new Color(0.92f, 0.9f, 0.82f, 1f);
        /// <summary>
        /// 🔑 상점의 표시 이름은 「잡화점」이다(2026-09-04 사용자 확정 — 종전 "야시장"(2026-08-18),
        /// 그 전은 "팝업 스토어". 모델·스타일 교체와 함께 개명했다).
        /// ⚠️ 내부 식별자(<c>HexMapObjectType.Shop</c> · <c>objectType: "Shop"</c> · <c>popup_shop</c>
        /// objectId/catalogRef)는 <b>그대로 둔다</b> — 보상뽑기(TreasureChest) 개명과 같은 원칙으로,
        /// 직렬화된 맵 저작·세이브가 그 문자열에 묶여 있고 이 결정은 표시 이름에 관한 것이다.
        /// </summary>
        public const string ShopTitle = "잡화점";
        public static readonly Color ShopTitleColor = new Color(0.98f, 0.62f, 0.86f, 1f);
        private static readonly Color ShopBodyColor = new Color(0.96f, 0.88f, 0.94f, 1f);
        public const string CamperVanTitle = "캠핑카";
        public static readonly Color CamperVanTitleColor = new Color(1f, 0.78f, 0.5f, 1f);
        private static readonly Color CamperVanBodyColor = new Color(0.97f, 0.9f, 0.82f, 1f);
        public const string WorkshopTitle = "공작소";
        public static readonly Color WorkshopTitleColor = new Color(0.6f, 0.84f, 0.94f, 1f);
        private static readonly Color WorkshopBodyColor = new Color(0.84f, 0.92f, 0.96f, 1f);
        public const string MemoryStoneTitle = "기억결";
        public static readonly Color MemoryStoneTitleColor = new Color(0.56f, 0.75f, 1f, 1f);
        private static readonly Color MemoryStoneBodyColor = new Color(0.82f, 0.9f, 1f, 1f);
        public const string RupturedGroundTitle = "부서진 땅";
        public static readonly Color RupturedGroundTitleColor = new Color(0.86f, 0.72f, 0.55f, 1f);
        private static readonly Color RupturedGroundBodyColor = new Color(0.90f, 0.84f, 0.76f, 1f);

        /// <summary>
        /// 부서진 땅 한 칸의 설명(2026-09-01 #18 · 2026-09-05 문안 개정). 무엇이 걸리고
        /// <b>몇 턴 남았는지</b>를 말한다 — 이 두 가지가 「돌아갈 것인가 가로지를 것인가」의 판단 전부다.
        /// 상태이상 이름은 상태 툴팁과 <b>같은 표</b>에서 온다(어휘 두 벌 금지).
        ///
        /// <para>🔴 「밟으면」이 아니라 <b>「서 있으면」</b>이다(사용자 확정). 지대는 밟고 지나가는
        /// 발판이 아니라 <b>서 있는 동안만</b> 효과가 붙는 땅이고, 규칙도 그렇게 바뀌었다
        /// (<c>CombatState.RefreshStandingStatusZoneEffects</c>) — 문안이 「밟으면」이면 한 번 밟은
        /// 대가가 따라온다고 읽혀 실제 규칙과 어긋난다.</para>
        /// </summary>
        public static List<ObjectInfoTooltipHudPresenter.Line> BuildRupturedGroundLines(
            StatusEffectKind statusKind, int remainingTurns)
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>(3)
            {
                new ObjectInfoTooltipHudPresenter.Line(
                    $"이 타일 위에 서 있으면 {StatusEffectInfo.DisplayName(statusKind)}을(를) 받습니다",
                    RupturedGroundBodyColor),
                new ObjectInfoTooltipHudPresenter.Line(
                    "벗어나면 곧바로 풀립니다", RupturedGroundBodyColor),
                new ObjectInfoTooltipHudPresenter.Line(
                    remainingTurns > 0 ? $"{remainingTurns}턴 뒤 원래대로 돌아옵니다" : "곧 원래대로 돌아옵니다",
                    RupturedGroundBodyColor)
            };
        }

        /// <summary>철조각 살포 예고 칸(2026-09-05 후속 #1). 오버레이(붉은 해치)와 같은 상태 술어
        /// (<c>GetBossPropVolleyTelegraphCells</c>)를 읽는 칸에만 뜬다 — 그림과 글자가 갈라질 수 없다.</summary>
        public const string BossPropVolleyTelegraphTitle = "철조각 살포 예고";
        public static readonly Color BossPropVolleyTelegraphTitleColor = new Color(1f, 0.55f, 0.32f, 1f);
        private static readonly Color BossPropVolleyTelegraphBodyColor = new Color(0.98f, 0.86f, 0.78f, 1f);

        /// <summary>
        /// 「다음 적 턴에 여기 철조각이 놓인다」 + 무엇을 할 수 있는가. 성숙 턴·폭발 반경은 놓인 <b>철조각의</b>
        /// 툴팁이 말한다(놓이기 전엔 개체가 없어 저작값을 물을 대상이 없다) — 여기서는 「막히는 칸」과
        /// 「부술 수 있다」만 말한다.
        /// </summary>
        public static List<ObjectInfoTooltipHudPresenter.Line> BuildBossPropVolleyTelegraphLines()
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>(3)
            {
                new ObjectInfoTooltipHudPresenter.Line("다음 적 턴에 보스가 이 칸에 철조각을 놓습니다", BossPropVolleyTelegraphBodyColor),
                new ObjectInfoTooltipHudPresenter.Line("철조각이 선 칸은 지나갈 수 없습니다", BossPropVolleyTelegraphBodyColor),
                new ObjectInfoTooltipHudPresenter.Line("익기 전에 부수면 보스가 흡수하지 못합니다", DimColor)
            };
        }

        public const string TrapTitle = "함정";
        public static readonly Color TrapTitleColor = new Color(0.96f, 0.45f, 0.38f, 1f);
        private static readonly Color TrapEffectColor = new Color(0.95f, 0.78f, 0.74f, 1f);
        private static readonly Color DimColor = new Color(0.72f, 0.74f, 0.78f, 1f);

        public static List<ObjectInfoTooltipHudPresenter.Line> BuildStatusEffectLines(ActiveEffect effect)
        {
            var lines = new List<ObjectInfoTooltipHudPresenter.Line>(3);
            var category = StatusEffectTooltipContent.Category(effect.Kind);
            if (!string.IsNullOrWhiteSpace(category))
            {
                lines.Add(new ObjectInfoTooltipHudPresenter.Line(category, StatusIconCategoryColor));
            }

            var description = StatusEffectTooltipContent.Description(effect);
            if (!string.IsNullOrWhiteSpace(description))
            {
                lines.Add(new ObjectInfoTooltipHudPresenter.Line(description, StatusIconEffectColor));
            }

            lines.Add(new ObjectInfoTooltipHudPresenter.Line(
                StatusEffectTooltipContent.RemainingTurnsLine(effect),
                StatusIconTurnsColor));
            return lines;
        }

        /// <summary>
        /// 전멸기 예고(§13.5) 툴팁. 평범한 공격 예고와 같은 붉은 해치를 공유하므로 — 화면에서 둘을
        /// 가르는 것은 경고 아이콘뿐이다 — 여기서 "언제·얼마나·어떻게 피하나"를 말로 마저 채운다.
        /// </summary>
        public static List<ObjectInfoTooltipHudPresenter.Line> BuildAnnihilationLines(int turnsRemaining, int damage)
        {
            var when = turnsRemaining <= 1
                ? "다음 적 행동에 이 칸이 폭발합니다."
                : $"{turnsRemaining}턴 후 이 칸이 폭발합니다.";
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line(AnnihilationCategory, StatusIconCategoryColor),
                new ObjectInfoTooltipHudPresenter.Line(when, StatusIconEffectColor),
                new ObjectInfoTooltipHudPresenter.Line($"피해 {damage}", StatusIconEffectColor),
                // §21.6 — "지금 장판을 까는 건 낭비"라는 정보가 읽히는 규칙이 되도록 예고 툴팁에 명시한다.
                new ObjectInfoTooltipHudPresenter.Line("폭발 범위의 설치물(장판)이 함께 파괴됩니다.", StatusIconEffectColor),
                new ObjectInfoTooltipHudPresenter.Line(AnnihilationHint, StatusIconTurnsColor),
            };
        }

        /// <summary>? 안전지대 후보 툴팁(§28 W5). 진짜/가짜는 색으로 말하지 않는다 — 정찰이 답할 질문이다.</summary>
        public static List<ObjectInfoTooltipHudPresenter.Line> BuildSafeZoneCandidateLines()
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line(AnnihilationCategory, StatusIconCategoryColor),
                new ObjectInfoTooltipHudPresenter.Line("전멸기 예고가 없는 안전지대 후보입니다. 진짜일 수도, 가짜일 수도 있습니다.", StatusIconEffectColor),
                new ObjectInfoTooltipHudPresenter.Line("정찰 카드로 밝히면 진짜 안전지대는 초록으로 확정 표시됩니다.", StatusIconTurnsColor),
            };
        }

        /// <summary>초록 확정 안전지대 툴팁(§28 W5 후속 T1). 후보 툴팁이 "정찰로 밝히면 초록으로
        /// 확정된다"고 예고한 그 결과 화면이다 — 두 문구는 한 쌍으로 읽힌다.</summary>
        public static List<ObjectInfoTooltipHudPresenter.Line> BuildSafeZoneConfirmedLines()
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line(AnnihilationCategory, StatusIconCategoryColor),
                new ObjectInfoTooltipHudPresenter.Line("정찰로 확인된 진짜 안전지대입니다.", StatusIconEffectColor),
                new ObjectInfoTooltipHudPresenter.Line("전멸기 폭발이 이 칸을 덮지 않습니다.", StatusIconTurnsColor),
            };
        }

        /// <summary>취약 부위 툴팁(§28 W4).</summary>
        public static List<ObjectInfoTooltipHudPresenter.Line> BuildWeakSpotLines(int damagePercent, int turnsRemaining)
        {
            var lines = new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line(AnnihilationCategory, StatusIconCategoryColor),
                // I-24(WS-I): 키워드 정의(「피해가 2배」)와 같은 어휘로 말한다 — 100%는 「2배」다.
                new ObjectInfoTooltipHudPresenter.Line(
                    damagePercent == 100
                        ? "이 칸을 공격하면 피해가 2배가 됩니다."
                        : $"이 칸을 공격하면 피해가 {damagePercent}% 증가합니다.",
                    StatusIconEffectColor),
            };
            if (turnsRemaining > 0)
            {
                lines.Add(new ObjectInfoTooltipHudPresenter.Line($"{turnsRemaining}턴 동안 유지됩니다.", StatusIconTurnsColor));
            }

            return lines;
        }

        public static List<ObjectInfoTooltipHudPresenter.Line> BuildKnockbackLines()
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line(KnockbackCategory, StatusIconCategoryColor),
                new ObjectInfoTooltipHudPresenter.Line(KnockbackDescription, StatusIconEffectColor),
                new ObjectInfoTooltipHudPresenter.Line(KnockbackHint, StatusIconTurnsColor),
            };
        }

        public static List<ObjectInfoTooltipHudPresenter.Line> BuildCurseLines()
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line(CurseCategory, StatusIconCategoryColor),
                new ObjectInfoTooltipHudPresenter.Line(CurseDescription, StatusIconEffectColor),
                new ObjectInfoTooltipHudPresenter.Line(CurseHint, StatusIconTurnsColor),
            };
        }

        public static List<ObjectInfoTooltipHudPresenter.Line> BuildPullLines()
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line(KnockbackCategory, StatusIconCategoryColor),
                new ObjectInfoTooltipHudPresenter.Line(PullDescription, StatusIconEffectColor),
                new ObjectInfoTooltipHudPresenter.Line(PullHint, StatusIconTurnsColor),
            };
        }

        public static List<ObjectInfoTooltipHudPresenter.Line> BuildTreasureChestLines()
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line("한 번 뽑으면 돈·유물·카드팩 중 하나가 나옵니다.", TreasureBodyColor),
                new ObjectInfoTooltipHudPresenter.Line("이미 뽑은 기계는 다시 쓸 수 없습니다.", DimColor),
            };
        }

        public static List<ObjectInfoTooltipHudPresenter.Line> BuildShopLines()
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line("재화로 카드·유물을 사거나 카드를 제거할 수 있습니다.", ShopBodyColor),
                new ObjectInfoTooltipHudPresenter.Line("떠나면 상점은 사라집니다 (1회 방문).", DimColor),
            };
        }

        public static List<ObjectInfoTooltipHudPresenter.Line> BuildCamperVanLines()
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line("체력을 회복하거나 카드를 연마할 수 있습니다.", CamperVanBodyColor),
                new ObjectInfoTooltipHudPresenter.Line("떠나면 사라집니다 (1회 방문).", DimColor),
            };
        }

        public static List<ObjectInfoTooltipHudPresenter.Line> BuildWorkshopLines()
        {
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line("카드를 제거하거나 연마할 수 있습니다.", WorkshopBodyColor),
                new ObjectInfoTooltipHudPresenter.Line("떠나면 사라집니다 (1회 방문).", DimColor),
            };
        }

        public static List<ObjectInfoTooltipHudPresenter.Line> BuildMemoryStoneLines()
        {
            // Intentionally omit the raw objectRef line ("memorystone"): it leaked an English internal id
            // into a player-facing tooltip. The Korean title/body already name the object.
            return new List<ObjectInfoTooltipHudPresenter.Line>
            {
                new ObjectInfoTooltipHudPresenter.Line("기억결을 조사할 수 있습니다.", MemoryStoneBodyColor),
                new ObjectInfoTooltipHudPresenter.Line("조사하면 목표 진행 또는 보상이 갱신될 수 있습니다.", DimColor),
            };
        }

        /// <param name="statusCardNameResolver">
        /// 상태 카드 id → 표시명. 호스트가 카드 카탈로그로 풀어 넘긴다 — 여기서 이름을 하드코딩하면
        /// cards.csv를 고칠 때 툴팁만 조용히 낡는다. null이면 id를 그대로 보여준다.
        /// </param>
        public static List<ObjectInfoTooltipHudPresenter.Line> BuildTrapLines(
            HexTrapData trap,
            System.Func<string, string> statusCardNameResolver = null)
        {
            var lines = new List<ObjectInfoTooltipHudPresenter.Line>();
            if (trap.Effects != null)
            {
                foreach (var effect in trap.Effects)
                {
                    lines.Add(new ObjectInfoTooltipHudPresenter.Line(
                        $"- {FormatTrapEffect(effect, statusCardNameResolver)}", TrapEffectColor));
                }
            }

            if (lines.Count == 0)
            {
                lines.Add(new ObjectInfoTooltipHudPresenter.Line("표시할 함정 효과가 없습니다.", DimColor));
            }

            return lines;
        }

        private static string FormatTrapEffect(
            HexTrapEffectData effect,
            System.Func<string, string> statusCardNameResolver = null)
        {
            var duration = effect.DurationTurns > 0 ? $" ({effect.DurationTurns}턴)" : string.Empty;
            switch (effect.Kind)
            {
                case HexTrapEffectKind.Damage:
                    return $"피해 {effect.Amount}";
                // HexTrapEffectKind.Burn은 D-1로 제거됐다(저작 금지). enum 멤버만 직렬화 값 유지를 위해
                // 남아 있으므로 여기서도 case를 두지 않고 default의 원시 표기로 떨어뜨린다 — 저작 사고가
                // 툴팁에서 "화상"처럼 정상으로 보이면 안 된다.
                case HexTrapEffectKind.Poison:
                    return $"중독 {effect.Amount}{duration}";
                case HexTrapEffectKind.Stun:
                    // I-29: 기절은 수치가 없는 존재형이다 — 의미 없는 Amount를 수치처럼 찍지 않는다.
                    return $"기절{duration}";
                case HexTrapEffectKind.Slow:
                    return $"둔화 {effect.Amount}{duration}";
                case HexTrapEffectKind.VisionDown:
                    return $"실명 {effect.Amount}{duration}";
                case HexTrapEffectKind.Teleport:
                    // Amount는 지속 턴이 아니라 반경이다 — duration 접미사를 붙이지 않는다.
                    return $"순간이동 (반경 {effect.Amount})";
                case HexTrapEffectKind.SpawnMonsters:
                    // Amount는 지속 턴이 아니라 스폰 수다 — duration 접미사를 붙이지 않는다.
                    return $"적 소환 {effect.Amount}";
                // I-18(WS-I): weaken_gas·disarm_ward 프리셋이 case 누락으로 "Weaken 30 (2턴)"처럼
                // raw enum을 노출하고 있었다 — 상태이상 표시명과 같은 어휘로 잇는다.
                case HexTrapEffectKind.Weaken:
                    return $"쇠약 {effect.Amount}%{duration}";
                case HexTrapEffectKind.Disarm:
                    // 무장 해제도 수치 없는 존재형이다(Amount 무의미).
                    return $"무장 해제{duration}";
                case HexTrapEffectKind.InjectStatusCard:
                    // Amount는 지속 턴이 아니라 삽입 장수다 — duration 접미사를 붙이지 않는다.
                    // 무엇이 들어오는지가 이 함정의 전부라서 카드 이름을 함께 보여준다.
                    var cardName = statusCardNameResolver?.Invoke(effect.StatusCardId);
                    var cardLabel = string.IsNullOrWhiteSpace(cardName) ? effect.StatusCardId : cardName;
                    return string.IsNullOrWhiteSpace(cardLabel)
                        ? $"상태 카드 {effect.Amount}장"
                        : $"상태 카드 '{cardLabel}' {effect.Amount}장";
                default:
                    return $"{effect.Kind} {effect.Amount}{duration}";
            }
        }
    }
}

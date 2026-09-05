namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Presentation-only classification of a <see cref="StatusEffectKind"/>. The runtime treats every
    /// status effect through one unified <c>ActiveEffect</c> pipeline (apply, tick, expire); polarity is
    /// used purely for UI grouping/coloring (buffs vs debuffs) and never gates gameplay logic.
    /// "상태이상" = Debuff, "버프" = Buff, "상태효과" = the neutral umbrella over both.
    ///
    /// Gameplay predicates live beside it, not on top of it — see
    /// <see cref="StatusEffectInfo.IsCleansable"/>, which decides 정화 scope without reading polarity so
    /// that retuning the UI grouping can never move what a cleanse removes.
    /// </summary>
    public enum StatusEffectPolarity
    {
        Debuff,
        Buff
    }

    public static class StatusEffectInfo
    {
        public static StatusEffectPolarity GetPolarity(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Agility:
                case StatusEffectKind.Reflect:
                case StatusEffectKind.Strength:
                case StatusEffectKind.TorchLight:
                case StatusEffectKind.BossAura:
                case StatusEffectKind.Might:
                case StatusEffectKind.Unknown:
                case StatusEffectKind.Guard:
                case StatusEffectKind.Stealth:
                case StatusEffectKind.Invincible:
                    return StatusEffectPolarity.Buff;
                case StatusEffectKind.Immobilize:
                case StatusEffectKind.Stun:
                case StatusEffectKind.Poison:
                case StatusEffectKind.Slow:
                case StatusEffectKind.Rupture:
                case StatusEffectKind.Blind:
                case StatusEffectKind.Disarm:
                case StatusEffectKind.Weaken:
                case StatusEffectKind.Vulnerable:
                case StatusEffectKind.Seal:
                    return StatusEffectPolarity.Debuff;
                default:
                    return StatusEffectPolarity.Debuff;
            }
        }

        public static bool IsBuff(StatusEffectKind kind) => GetPolarity(kind) == StatusEffectPolarity.Buff;

        public static bool IsDebuff(StatusEffectKind kind) => GetPolarity(kind) == StatusEffectPolarity.Debuff;

        /// <summary>
        /// Whether 정화(cleanse) can strip this status effect. Deliberately separate from
        /// <see cref="GetPolarity"/>: polarity is a UI-only contract, this is a gameplay decision.
        /// The two happen to agree today; when a non-cleansable debuff appears, change only this switch.
        /// Adding a <see cref="StatusEffectKind"/> means reviewing both.
        /// </summary>
        public static bool IsCleansable(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Immobilize:
                case StatusEffectKind.Stun:
                case StatusEffectKind.Poison:
                case StatusEffectKind.Slow:
                case StatusEffectKind.Rupture:
                case StatusEffectKind.Blind:
                case StatusEffectKind.Disarm:
                case StatusEffectKind.Weaken:
                case StatusEffectKind.Vulnerable:
                case StatusEffectKind.Seal:
                    return true;
                default:
                    return false; // Agility / Reflect / Strength / TorchLight(버프라 정화 대상 아님)
            }
        }

        /// <summary>
        /// Per-kind default magnitude. Authored in <c>status_effects.csv</c>; the switch below is the
        /// fallback for surfaces that run without a loaded catalog (EditMode fixtures, pure-C# tools).
        /// The two must agree — `StatusEffectCatalogWiringTests` fails the build if they drift, which is
        /// what keeps this from becoming a second source of truth.
        /// </summary>
        public static int DefaultAmount(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null && catalog.TryGet(kind, out var definition)
                ? definition.DefaultAmount
                : FallbackDefaultAmount(kind);
        }

        internal static int FallbackDefaultAmount(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Poison:
                    return 3;
                case StatusEffectKind.Slow:
                    return 1;
                case StatusEffectKind.Rupture:
                    return 2;
                case StatusEffectKind.Agility:
                    return 2;
                case StatusEffectKind.Reflect:
                    return 50;
                case StatusEffectKind.Strength:
                    return 100;
                case StatusEffectKind.Blind:
                    // D-7: 실명은 시야 −1이 기본. 강화판(−2)은 저작 amount로만 만든다.
                    return 1;
                case StatusEffectKind.Weaken:
                    // O-10 확정: 나가는 피해 −30%.
                    return 30;
                case StatusEffectKind.Vulnerable:
                    // O-10 확정: 받는 타격당 +2 고정(퍼센트 아님 — D-10).
                    return 2;
                case StatusEffectKind.Seal:
                    // D-16: amount = 봉인 장수. 기본 1장.
                    return 1;
                case StatusEffectKind.TorchLight:
                    // D-15: 초기 시야 +3에서 매 턴 1씩 타 들어간다.
                    return 3;
                case StatusEffectKind.Guard:
                    // T2 페이즈 C: 기본 충전 1(수호 부적 "최대 1"은 부여 측 규칙, 여기는 1회 부여량).
                    return 1;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 같은 종류를 다시 걸었을 때 기존 인스턴스에 병합할지(true) 별개 인스턴스를 더할지(false).
        /// 현행 두 <see cref="StatusEffectStackPolicy"/>가 모두 병합이므로 항상 true다 —
        /// 다중 인스턴스 정책이 생기는 날 이 술어가 갈라진다.
        ///
        /// P1.5(D-6)에서 <c>status_effects.csv</c>의 stackPolicy를 실소비로 전환했다. 그 전에는
        /// 여기 하드코딩된 switch가 정본이었고 CSV와 갈라져 있었다: 반사·민첩·강화가 CSV에는
        /// "RefreshDuration"인데 코드에서는 병합 대상이 아니었다(단일 인스턴스 보장을
        /// <c>ApplyReflectToPlayer</c> 등 전용 경로의 remove-then-add가 대신 하고 있었다).
        /// 이제는 CSV가 정본이고, 아래 switch는 카탈로그가 없는 표면(EditMode 픽스처·pure-C# 도구)의
        /// 폴백이다. 둘이 갈라지면 <c>StatusEffectCatalogWiringTests</c>가 빌드를 깬다.
        /// </summary>
        internal static bool UsesDurationOnlyStacking(StatusEffectKind kind)
        {
            // switch로 쓰는 이유: 정책을 추가하는 사람이 "이건 병합인가"를 반드시 지나가게 한다.
            switch (StackPolicy(kind))
            {
                case StatusEffectStackPolicy.RefreshDuration:
                case StatusEffectStackPolicy.Add:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 병합 시 수치를 합산할지(중독/파열) 더 큰 쪽을 남길지. <see cref="StackPolicy"/>의 파생이다.
        /// </summary>
        internal static bool UsesAdditiveAmountStacking(StatusEffectKind kind)
        {
            return StackPolicy(kind) == StatusEffectStackPolicy.Add;
        }

        /// <summary>
        /// 재적용 병합 규칙. CSV가 정본, 아래 switch는 카탈로그 미로드 폴백 —
        /// <see cref="DefaultAmount"/>와 같은 계약이다.
        /// </summary>
        public static StatusEffectStackPolicy StackPolicy(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null && catalog.TryGet(kind, out var definition)
                ? definition.StackPolicy
                : FallbackStackPolicy(kind);
        }

        internal static StatusEffectStackPolicy FallbackStackPolicy(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Poison:
                case StatusEffectKind.Rupture:
                // 수호(T2 페이즈 C): 충전은 수치 누적이 자연스럽다 — 복수 출처가 생겨도 합산된다.
                case StatusEffectKind.Guard:
                    return StatusEffectStackPolicy.Add;
                default:
                    return StatusEffectStackPolicy.RefreshDuration;
            }
        }

        /// <summary>
        /// Amount가 무엇을 뜻하는가 = <b>어느 소비 지점이 이 상태를 합산하는가</b>. 소비 지점들은
        /// 이제 종류 이름이 아니라 이 값으로 합산하므로(예: 시야는 VisionRangePenalty를 전부 더한다),
        /// 같은 축의 상태이상을 새로 추가할 때 소비 코드를 건드릴 필요가 없다.
        /// </summary>
        public static StatusEffectValueMode ValueMode(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null && catalog.TryGet(kind, out var definition)
                ? definition.ValueMode
                : FallbackValueMode(kind);
        }

        internal static StatusEffectValueMode FallbackValueMode(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Poison:
                    return StatusEffectValueMode.DamagePerTurn;
                case StatusEffectKind.Slow:
                    return StatusEffectValueMode.MoveRangePenalty;
                case StatusEffectKind.Agility:
                    return StatusEffectValueMode.MoveRangeBonus;
                case StatusEffectKind.Rupture:
                    return StatusEffectValueMode.BlockGainPenalty;
                case StatusEffectKind.Reflect:
                    return StatusEffectValueMode.ReflectGate;
                case StatusEffectKind.Strength:
                    return StatusEffectValueMode.DamageDealtBonusPercent;
                case StatusEffectKind.Blind:
                    return StatusEffectValueMode.VisionRangePenalty;
                case StatusEffectKind.Weaken:
                    return StatusEffectValueMode.DamageDealtPenaltyPercent;
                case StatusEffectKind.Vulnerable:
                    return StatusEffectValueMode.IncomingDamageBonusFlat;
                case StatusEffectKind.Seal:
                    return StatusEffectValueMode.SealedCardCount;
                case StatusEffectKind.TorchLight:
                    return StatusEffectValueMode.VisionRangeBonus;
                case StatusEffectKind.Guard:
                    return StatusEffectValueMode.StatusNegationCharges;
                // 힘(2026-09-04): 몬스터의 약오름·담력 시험이 이 축으로 피해를 싣는다. 플레이어의 힘은
                // 런 영구 스탯의 투영이라 등록부에 들어가지 않아 이 합산에 걸리지 않는다.
                case StatusEffectKind.Might:
                    return StatusEffectValueMode.DamageDealtBonusFlat;
                default:
                    return StatusEffectValueMode.None; // Immobilize / Stun / Disarm / Stealth
            }
        }

        /// <summary>
        /// 만료 규칙. <see cref="StatusEffectExpirePolicy.TurnStartAfterTick"/>만 적용 턴 유예를 받지 않는다.
        /// </summary>
        public static StatusEffectExpirePolicy ExpirePolicy(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null && catalog.TryGet(kind, out var definition)
                ? definition.ExpirePolicy
                : FallbackExpirePolicy(kind);
        }

        internal static StatusEffectExpirePolicy FallbackExpirePolicy(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Poison:
                    return StatusEffectExpirePolicy.TurnStartAfterTick;
                case StatusEffectKind.Guard:
                    // T2 페이즈 C 카운터 스택 문법: 턴이 아니라 소비로만 줄어든다.
                    return StatusEffectExpirePolicy.OnConsume;
                default:
                    return StatusEffectExpirePolicy.TurnEnd;
            }
        }

        /// <summary>
        /// 한 유닛이 동시에 가질 수 있는 인스턴스 수. 현행 정책이 전부 병합이라 항상 1이고,
        /// 클램프는 발동하지 않는 백스톱이다(<see cref="StatusEffectDefinition.MaxStacks"/> 참고).
        /// </summary>
        public static int MaxStacks(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null && catalog.TryGet(kind, out var definition)
                ? definition.MaxStacks
                : 1;
        }

        /// <summary>
        /// timing은 서술 전용이다 — 읽기 지점은 <see cref="ValueMode"/>가 결정한다. 이 접근자는
        /// 검증·문서화용이며 런타임 분기에 쓰면 안 된다(두 번째 디스패처가 생기는 순간
        /// 저작으로 규칙을 깰 수 있게 된다).
        /// </summary>
        public static StatusEffectTiming Timing(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null && catalog.TryGet(kind, out var definition)
                ? definition.Timing
                : FallbackTiming(kind);
        }

        internal static StatusEffectTiming FallbackTiming(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Stun:
                    return StatusEffectTiming.OnActionCheck;
                case StatusEffectKind.Disarm:
                case StatusEffectKind.Seal:
                    return StatusEffectTiming.OnActionCheck;
                case StatusEffectKind.Immobilize:
                    return StatusEffectTiming.BeforeMoveRangeCalc;
                case StatusEffectKind.Poison:
                    return StatusEffectTiming.TurnStart;
                case StatusEffectKind.Slow:
                case StatusEffectKind.Agility:
                    return StatusEffectTiming.BeforeMoveRangeCalc;
                case StatusEffectKind.Rupture:
                    return StatusEffectTiming.OnBlockGain;
                case StatusEffectKind.Reflect:
                    return StatusEffectTiming.OnIncomingDamage;
                case StatusEffectKind.Strength:
                case StatusEffectKind.Weaken:
                // 힘(2026-09-04)은 이제 존재형이 아니라 나가는 피해에 실리는 값이다 — 중립 지점에
                // 남겨 두면 CSV(OnOutgoingDamage)와 갈라진다.
                case StatusEffectKind.Might:
                    return StatusEffectTiming.OnOutgoingDamage;
                case StatusEffectKind.Vulnerable:
                    return StatusEffectTiming.OnIncomingDamage;
                case StatusEffectKind.BossAura:
                case StatusEffectKind.Unknown:
                // 은신(T2 페이즈 C)도 존재형이다 — 실소비는 몬스터 감지 판정이지만 timing enum은
                // 존재 검사 지점 서술만 허용하므로 중립 지점으로 못박는다.
                case StatusEffectKind.Stealth:
                // 무적(WS-I I-19)도 존재형 투영이다 — 실판정은 불리언(피격 시점)이고 timing은 서술 전용.
                case StatusEffectKind.Invincible:
                    // 존재형(valueMode None) 상태라 valueMode↔timing 검증이 존재 검사 지점만 허용한다.
                    // 둘 다 ActiveEffect로 부여되지 않아 실제로 어느 시점에도 평가되지 않으므로, 검증을
                    // 통과하는 중립 지점을 CSV와 동일하게 못박아 둔다(폴백=CSV 계약).
                    // ⚠️ default가 BeforeVisionCalc(실명/횃불)라 이 case가 없으면 조용히 시야 지점으로
                    // 떨어지고 CSV와 갈라진다.
                    return StatusEffectTiming.OnActionCheck;
                case StatusEffectKind.Guard:
                    // 수호(T2 페이즈 C): 충전은 상태이상 부여 관문에서 소비된다.
                    return StatusEffectTiming.OnStatusApply;
                default:
                    return StatusEffectTiming.BeforeVisionCalc; // Blind / TorchLight
            }
        }

        /// <summary>
        /// Canonical Korean name (원형) for a status kind — 속박/중독/기절/… — authored in
        /// <c>status_effects.csv</c> (displayNameKo), with the switch below as the fallback for surfaces that
        /// run without a loaded catalog. Same contract as <see cref="DefaultAmount"/>, and
        /// <c>StatusEffectCatalogWiringTests</c> fails the build if the two drift.
        ///
        /// This is the single naming source for every player-facing surface (HUD icon label, hover tooltip,
        /// monster info panel, floating text, Dev labs). Those used to carry private copies of the switch and
        /// had already drifted: 강화(Strength) was missing from three of them and leaked the raw enum name.
        /// The string doubles as the join key into <c>game_keywords.csv</c> 키워드, so it must stay identical
        /// to the authored keyword there.
        /// </summary>
        public static string DisplayName(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null
                   && catalog.TryGet(kind, out var definition)
                   && !string.IsNullOrWhiteSpace(definition.DisplayNameKo)
                ? definition.DisplayNameKo
                : FallbackDisplayName(kind);
        }

        internal static string FallbackDisplayName(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Immobilize:
                    return "속박";
                case StatusEffectKind.Poison:
                    return "중독";
                case StatusEffectKind.Stun:
                    return "기절";
                case StatusEffectKind.Slow:
                    return "둔화";
                case StatusEffectKind.Rupture:
                    return "파열";
                case StatusEffectKind.Reflect:
                    return "반사";
                case StatusEffectKind.Agility:
                    return "민첩";
                case StatusEffectKind.Strength:
                    return "강화";
                case StatusEffectKind.Blind:
                    return "실명";
                case StatusEffectKind.Disarm:
                    return "무장 해제";
                case StatusEffectKind.Weaken:
                    return "쇠약";
                case StatusEffectKind.Vulnerable:
                    return "허점";
                case StatusEffectKind.Seal:
                    return "봉인";
                case StatusEffectKind.Invincible:
                    return "무적";
                case StatusEffectKind.TorchLight:
                    return "등불";
                case StatusEffectKind.Might:
                    return "힘";
                case StatusEffectKind.Unknown:
                    return "미지";
                case StatusEffectKind.Guard:
                    return "수호";
                case StatusEffectKind.Stealth:
                    return "은신";
                case StatusEffectKind.BossAura:
                    // 플레이어에게 직접 노출되지 않는다(ActiveEffect로 부여되지 않아 아이콘/정화 대상이 아님).
                    // 다만 enum 전수 소비처(StatusEffectCatalogWiringTests 등)가 모든 이름을 요구하므로
                    // 이름·CSV·키워드를 함께 채워 둔다.
                    return "보스 기운";
                default:
                    return kind.ToString();
            }
        }

        // ───────────────────────────── 표현 레지스트리(1단계 구조 리팩토링) ─────────────────────────────
        // 아래 4속성은 원래 표현 소비자 4곳(네임플레이트 정렬·플로팅 문안·부여 SFX·아이콘 글리프)의 private
        // switch였다. 상태이상 하나를 추가하면 그 파일들을 함께 고쳐야 했고, 빠뜨리면 default로 조용히
        // 빠졌다(cs:846 오바인딩 · 2026-08-20 #6 글리프 12종 `!`). 이제 CSV가 정본, 아래 switch는
        // 카탈로그 없는 표면의 폴백이며 둘의 일치는 StatusEffectCatalogWiringTests가 문다 —
        // DefaultAmount·DisplayName과 같은 계약. 폴백 본문은 원 switch에서 값을 바꾸지 않고 옮긴 것이다.
        // 색(Color)은 UnityEngine 타입이라 여기 둘 수 없고, 색 3벌 존치는 사용자 결정이기도 하다.

        /// <summary>
        /// 스프라이트가 없을 때 아이콘 칸에 쓰는 한 글자. 글리프는 색이 아니라 <b>글자</b>가 식별자라
        /// 겹치면 안 된다(StatusEffectTooltipCoverageTests가 유일성을 문다).
        /// </summary>
        public static string Glyph(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null
                   && catalog.TryGet(kind, out var definition)
                   && !string.IsNullOrWhiteSpace(definition.Glyph)
                ? definition.Glyph
                : FallbackGlyph(kind);
        }

        internal static string FallbackGlyph(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Immobilize: return "속";
                case StatusEffectKind.Poison: return "독";
                case StatusEffectKind.Stun: return "기";
                case StatusEffectKind.Slow: return "둔";
                case StatusEffectKind.Rupture: return "파";
                case StatusEffectKind.Reflect: return "반";
                case StatusEffectKind.Agility: return "민";
                case StatusEffectKind.Invincible: return "무";
                // 2026-08-20 #6: 아래 12종은 스프라이트가 붙은 뒤에 append된 kind들이라 글리프가
                // 통째로 기본값 "!"였다. 원형 키워드의 첫 글자를 쓴다 — 배지 형상 규약과 같은 어휘다.
                case StatusEffectKind.Strength: return "강";
                case StatusEffectKind.Blind: return "실";
                case StatusEffectKind.Disarm: return "해";
                case StatusEffectKind.Weaken: return "쇠";
                case StatusEffectKind.Vulnerable: return "허";
                case StatusEffectKind.Seal: return "봉";
                case StatusEffectKind.TorchLight: return "등";
                // 「기」는 기절이 이미 쓴다.
                case StatusEffectKind.BossAura: return "보";
                case StatusEffectKind.Might: return "힘";
                case StatusEffectKind.Unknown: return "?";
                case StatusEffectKind.Guard: return "수";
                case StatusEffectKind.Stealth: return "은";
                default: return "!";
            }
        }

        /// <summary>몬스터 네임플레이트 배지 정렬 순위(작을수록 앞). 제어 4 · 지속피해 5 · 그 밖의 디버프 6 · 버프 7.</summary>
        public static int BadgeSortRank(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null && catalog.TryGet(kind, out var definition)
                ? definition.BadgeSortRank
                : FallbackBadgeSortRank(kind);
        }

        internal static int FallbackBadgeSortRank(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Stun:
                case StatusEffectKind.Immobilize:
                case StatusEffectKind.Seal:
                case StatusEffectKind.Disarm:
                case StatusEffectKind.Blind:
                    return 4;
                case StatusEffectKind.Poison:
                case StatusEffectKind.Rupture:
                    return 5;
                case StatusEffectKind.Weaken:
                case StatusEffectKind.Vulnerable:
                case StatusEffectKind.Slow:
                case StatusEffectKind.Unknown:
                    return 6;
                default:
                    return 7;
            }
        }

        /// <summary>플로팅 텍스트가 함정 발동으로 부여될 때 앞에 붙는 접두.</summary>
        public const string TrapFloatingTextPrefix = "함정 발동! ";

        /// <summary>
        /// 부여 플로팅 텍스트 템플릿. 토큰 <c>{name}</c>(표시명)·<c>{amount}</c>(부여 수치). 빈 값은 <c>{name}</c>과
        /// 같다. <c>|</c>로 나누면 앞이 평상시, 뒤가 함정 발동 시 본문이다(반사: 「반사 부여」 vs 「함정 발동! 반사」).
        /// 나누지 않으면 함정 발동 시에도 같은 본문에 접두만 붙는다.
        /// </summary>
        public static string FloatingTextTemplate(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null && catalog.TryGet(kind, out var definition)
                ? definition.FloatingTextTemplate
                : FallbackFloatingTextTemplate(kind);
        }

        internal static string FallbackFloatingTextTemplate(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Immobilize:
                    return "{name} {amount}턴";
                case StatusEffectKind.Poison:
                case StatusEffectKind.Rupture:
                case StatusEffectKind.Blind:
                    return "{name} {amount}";
                case StatusEffectKind.Stun:
                case StatusEffectKind.Slow:
                case StatusEffectKind.Strength:
                    return "{name}";
                case StatusEffectKind.Reflect:
                    return "{name} 부여|{name}";
                case StatusEffectKind.Agility:
                    // I-12(WS-I): 이 슬롯에 부여측(ApplyAgilityToPlayer)이 넣는 값은 턴이 아니라 <b>수치</b>다 —
                    // "민첩 2턴"이라고 찍으면 이동력 +2/1턴을 지속 2턴으로 오독하게 만든다.
                    return "{name} +{amount}";
                default:
                    // T1~T2로 kind가 늘며 케이스 없는 종류(쇠약·허점·봉인·은신 등)가 raw enum 이름으로
                    // 새고 있었다 — 표시명 단일 출처(DisplayName)만 찍는다.
                    return string.Empty;
            }
        }

        /// <summary>부여 플로팅 텍스트 완성문. 소비자는 이것만 부른다(템플릿 문법을 알 필요가 없다).</summary>
        public static string FloatingText(StatusEffectKind kind, int amount, bool isTrap)
        {
            return FormatFloatingText(FloatingTextTemplate(kind), DisplayName(kind), amount, isTrap);
        }

        /// <summary><see cref="FloatingTextTemplate"/> 문법의 단일 구현.</summary>
        public static string FormatFloatingText(string template, string displayName, int amount, bool isTrap)
        {
            var body = template ?? string.Empty;
            var split = body.IndexOf('|');
            if (split >= 0)
            {
                body = isTrap ? body.Substring(split + 1) : body.Substring(0, split);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                body = "{name}";
            }

            body = body
                .Replace("{name}", displayName ?? string.Empty)
                .Replace("{amount}", amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return isTrap ? TrapFloatingTextPrefix + body : body;
        }

        /// <summary>
        /// 부여 시 SFX 큐 id. 빈 값 = 무음 — 결함이 아니라 현행 저작이다(존재형 투영·보스 기운 등).
        /// 값은 <c>AudioCueIds</c> 상수와 같은 문자열이어야 한다(Audio 어셈블리를 참조할 수 없어 리터럴로 둔다;
        /// 존재하는 큐인지는 CombatAudioPresenterTests가 상수 표와 대조한다).
        /// </summary>
        public static string ApplyAudioCueId(StatusEffectKind kind)
        {
            var catalog = StatusEffectCatalogProvider.Active;
            return catalog != null && catalog.TryGet(kind, out var definition)
                ? definition.ApplyAudioCueId
                : FallbackApplyAudioCueId(kind);
        }

        internal static string FallbackApplyAudioCueId(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Poison:
                    return "effect.poison";
                case StatusEffectKind.Stun:
                    return "effect.stun";
                case StatusEffectKind.Slow:
                    return "effect.slow";
                case StatusEffectKind.Rupture:
                    return "effect.rupture";
                case StatusEffectKind.Immobilize:
                    return "effect.immobilize";
                case StatusEffectKind.Blind:
                    return "effect.blind";
                case StatusEffectKind.Disarm:
                    return "effect.disarm";
                case StatusEffectKind.Weaken:
                    return "effect.weaken";
                case StatusEffectKind.Vulnerable:
                    return "effect.vulnerable";
                case StatusEffectKind.Seal:
                    return "effect.seal";
                case StatusEffectKind.TorchLight:
                    return "effect.torchlight";
                case StatusEffectKind.BossAura:
                    return "effect.bossaura";
                case StatusEffectKind.Might:
                    return "effect.might";
                case StatusEffectKind.Unknown:
                    return "effect.unknown";
                case StatusEffectKind.Guard:
                    return "effect.guard";
                case StatusEffectKind.Stealth:
                    return "effect.stealth";
                case StatusEffectKind.Invincible:
                    return "effect.invincible";
                case StatusEffectKind.Reflect:
                case StatusEffectKind.Agility:
                case StatusEffectKind.Strength:
                    return "card.buff.resolve";
                default:
                    return string.Empty;
            }
        }
    }
}

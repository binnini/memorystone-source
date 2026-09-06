namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// <b>효과 종류 키</b>(effect-kind keys) — 카드가 <b>아닌</b> 발신자가 올리는 효과의 sourceRef 어휘.
    /// 카드가 자기 효과를 직접 올릴 때의 sourceRef는 언제나 <b>카드 id</b>다(트랙 ② 2026-09-06, DEC-2026-09-06-07 —
    /// 옛 behaviorId 문자열 <c>attack.damage</c>·<c>defend.block</c> 등은 은퇴). 여기 남은 키는 발신자가
    /// 카드가 떠난 뒤의 장판(<c>field.*</c>)·지연 부여(<c>*.apply</c>)·저주 턴말(<c>status.*</c>)·규칙 상태(피해 면역)처럼
    /// 「무엇이 일어났나」를 말해야 하는 것들이다. 소비자는 VFX 큐·오디오·타일 플래시·분류기·문안뿐이고 규칙 판정에는 쓰지 않는다
    /// (예외: 장판 속박의 제자리 갱신 <c>ActiveEffectRegistry.TryReplaceInPlaceFromSameSource</c>).
    /// 문자열은 세이브(<c>ActiveEffect.SourceRef</c>)와 큐 CSV에 실리므로 바꾸지 않는다.
    /// </summary>
    public static class CardEffectRefs
    {
        // ---- 지연 부여: 카드를 낸 턴이 아니라 다음 턴 시작에 규칙층이 스스로 건다 ----
        /// <summary>M05 추진력이 예약한 민첩이 다음 턴 시작에 실제로 붙는 순간.</summary>
        public const string MoveDeferredMomentumApply = "move.deferred_momentum.apply";
        // Source tag for the 속박 that D04/D06 booked and that lands at the start of the next turn.
        // Distinct from the card's own id so presentation can tell "you took this on yourself
        // last turn" apart from the moment the card was played (cf. MoveDeferredMomentumApply).
        public const string SelfDelayedImmobilizeApply = "self.delayed_immobilize.apply";
        /// <summary>D02 보호구역 안에서 도발이 예약한 「모든 적 강화」가 다음 턴 시작에 붙는 순간(옛 이름 defend.zero_then_double).</summary>
        public const string DefendProvokeStrength = "defend.provoke_strength";

        // ---- 규칙 상태에서 파생되는 알림 ----
        /// <summary>
        /// 방어 카드가 방어도 대신 <b>피해 면역</b>을 준 것을 알리는 Block(0) 발신. D02·D05가 공유하며 발신자는
        /// <c>CombatState.TryPlayerDefend</c>(규칙 상태 <c>IncomingDamageNullifiedThisMonsterAction</c>을 읽음).
        /// 분류기 <c>IsDamageImmunitySource</c>·EPC 「피해 면역」 문안·CVD02/CVD05 큐가 이것을 본다.
        /// </summary>
        public const string DefendDamageImmunity = "defend.damage_immunity";
        /// <summary>반사가 되돌린 피해(ReflectDamage). 몬스터 행동 중에 규칙층이 올린다 — D03의 Reflect 부여 자체는 카드 id다.</summary>
        public const string DefendHalfReflect = "defend.half_reflect";
        /// <summary>
        /// A12 전염병이 맞은 뒤 이웃에게 <b>옮긴</b> 상태이상 부여. 카드의 직접 발신이 아니라 파생 효과라 키를 따로 둔다 —
        /// 표현층은 이 키로 「전염! 」 접두를 단다(옛 이름 attack.plague).
        /// </summary>
        public const string PlagueContagion = "status.plague_contagion";

        // ---- 저주 카드(C-17 / D-17 → T2 2026-08-06): 사용 불가 카드라 「냈다」가 없고, 손에 있는 동안·턴말에 규칙층이 올린다 ----
        /// <summary>미세먼지: 부가 효과 없음. 턴 종료 시 손패에서 소멸(isTemporary).</summary>
        public const string StatusFineDust = "status.fine_dust";
        /// <summary>깨진 유리: 턴 종료 시 손패에 남아 있으면 피해. 버리면 아프지 않다 — 그게 선택지다.</summary>
        public const string StatusBrokenGlass = "status.broken_glass";
        /// <summary>
        /// 정전: <b>손패에 있는 동안</b> 다른 카드 1장이 봉인된다(사용자 확정 — 턴 종료 트리거가 아니다).
        /// 그래서 턴 훅이 없고, <c>GetSealedCardCount</c>가 손패를 세는 것이 곧 이 규칙이다.
        /// </summary>
        public const string StatusBlackout = "status.blackout";
        /// <summary>X04 빚 문서 — 낼 수 있는 유일한 저주(기 1로 사용 시 소멸).</summary>
        public const string StatusDebtNote = "status.debt_note";
        /// <summary>X05 악몽 — 손에 있는 동안 시야 −1.</summary>
        public const string StatusNightmare = "status.nightmare";
        /// <summary>X06 미련 — 턴말 손에 있으면 다음 턴 기 −1.</summary>
        public const string StatusLingering = "status.lingering";
        /// <summary>X07 지각 — 손에 있는 동안 이동 사거리 −1.</summary>
        public const string StatusTardiness = "status.tardiness";
        /// <summary>X08 골칫거리 — 순수 오염(효과 없음).</summary>
        public const string StatusNuisance = "status.nuisance";
        /// <summary>X09 부정 탄 부적 — 턴말 손에 있으면 쇠약 1턴.</summary>
        public const string StatusCursedCharm = "status.cursed_charm";
        /// <summary>X10 궂은 안개 — 턴말 손에 있으면 실명 1턴.</summary>
        public const string StatusMurkyFog = "status.murky_fog";
        /// <summary>X11 도깨비 장난 — 손에 있는 동안 봉인 1 + 아지랑이(턴말 소멸).</summary>
        public const string StatusGoblinPrank = "status.goblin_prank";
        /// <summary>X12 원귀 — 손에 있는 동안 받는 피해 +1.</summary>
        public const string StatusVengefulGhost = "status.vengeful_ghost";

        // ---- 장판: 카드가 떠난 뒤에도 살아 매 턴 스스로 올린다(CombatState.FieldObjectCardEffects) ----
        /// <summary>설치 순간의 발자국 알림(연출 전용, 수치 없음). 오디오·카메라는 침묵한다(분류기 <c>IsFieldPlacementAnnounce</c>).</summary>
        public const string FieldPlacement = "field.placement";
        public const string FieldDamage = "field.damage";
        public const string FieldHeal = "field.heal";
        public const string FieldImmobilizeFlashbang = "field.immobilize.flashbang";
        // F04 흡수진 — a damage field whose damage is returned to the caster as healing. Top-level ref
        // (not a `field.damage.*` flavour alias) because it maps to its own FieldObjectKind.
        public const string FieldLifesteal = "field.lifesteal";
        public const string FieldFogReveal = "field.fog-reveal";
        public const string FieldFogRevealCampfire = "field.fog_reveal.campfire";
        /// <summary>개발용 카드 연출 시나리오(<c>CardPresentationScenarioPlayer</c>)만 올린다 — 타임라인의 장판 영역 큐 판정이 같이 본다.</summary>
        public const string FieldDamageFirebomb = "field.damage.firebomb";

        /// <summary>
        /// 장판 종류 → 틱 발신 키. 장판은 카드가 떠난 뒤에도 살아 매 턴 스스로 올리므로 카드 id가 아니라 이 키를 sourceRef로 쓴다
        /// (<c>CombatState.FieldObjectCardEffects</c>가 발신자, VFX 큐·오디오·타일 플래시가 소비자). 큐 커버리지 도구가 같은 표를 읽는다.
        /// </summary>
        public static string FieldTickKey(SeoulPlayup.CardCore.CardFieldObjectKind kind)
        {
            switch (kind)
            {
                case SeoulPlayup.CardCore.CardFieldObjectKind.FieldDamage: return FieldDamage;
                case SeoulPlayup.CardCore.CardFieldObjectKind.LifestealDamage: return FieldLifesteal;
                case SeoulPlayup.CardCore.CardFieldObjectKind.ConditionalHeal: return FieldHeal;
                case SeoulPlayup.CardCore.CardFieldObjectKind.MassImmobilize: return FieldImmobilizeFlashbang;
                case SeoulPlayup.CardCore.CardFieldObjectKind.FogReveal: return FieldFogReveal;
                default: return string.Empty;
            }
        }
    }
}

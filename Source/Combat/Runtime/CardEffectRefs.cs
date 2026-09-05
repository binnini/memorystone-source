namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Central effectRef ids shared by card catalog data and the runtime effect dispatcher.
    /// These strings are intentionally stable because scene/catalog migration evidence stores them.
    /// </summary>
    public static class CardEffectRefs
    {
        public const string MoveBasic = "move.basic";
        public const string MoveDeferredMomentum = "move.deferred_momentum";
        public const string MoveDeferredMomentumApply = "move.deferred_momentum.apply";
        public const string MoveRandomRadius2 = "move.random_radius_2";
        public const string MoveFastTurtle = "move.fast_turtle";

        public const string AttackDamage = "attack.damage";
        public const string AttackAreaDamage = "attack.area_damage";
        public const string AttackMoveLinked = "attack.move_linked";
        public const string AttackHolyLight = "attack.holy_light";
        public const string AttackFinishingTouch = "attack.finishing_touch";
        public const string AttackFinalBlow = "attack.final_blow";
        public const string AttackDoubleHit = "attack.double_hit";
        public const string AttackOneStrikeEnough = "attack.one_strike_enough";
        public const string AttackMultiplyingStrike = "attack.multiplying_strike";
        public const string AttackSacrifice = "attack.sacrifice";
        public const string AttackTargetShot = "attack.target_shot";
        // A12 전염병 — extra damage against an already-afflicted target, then the affliction spreads.
        public const string AttackPlague = "attack.plague";

        /// <summary>
        /// 횃불(C-14 / D-15): 시야 +Amount를 주고 매 턴 1씩 타 들어간다. 획득 표면 둘(카드·오브젝트)이
        /// 같은 부여 지점(<c>GrantTorchLight</c>)을 쓴다 — 표면이 갈라져도 규칙은 한 곳이다.
        /// </summary>
        public const string UtilityTorch = "utility.torch";

        /// <summary>
        /// 함정 해제(C-13 / D-14): 인접 1칸의 <b>발견된</b> 함정을 소진 처리한다. 정찰 타입이지만
        /// 안개를 걷지 않는다 — 정찰의 셀 선택 문법만 빌린다.
        /// </summary>
        public const string ScoutTrapDisarm = "scout.trap_disarm";

        // 상태 카드(C-17 / D-17). 전부 사용 불가(CardEffectType.Status)이고, 차이는 부가 효과뿐이다.
        // 손패 한 칸을 먹는 것 자체가 공통 비용이라 "효과 없음"(미세먼지)도 성립한다.
        /// <summary>미세먼지: 부가 효과 없음. 턴 종료 시 손패에서 소멸(isTemporary).</summary>
        public const string StatusFineDust = "status.fine_dust";

        /// <summary>깨진 유리: 턴 종료 시 손패에 남아 있으면 피해. 버리면 아프지 않다 — 그게 선택지다.</summary>
        public const string StatusBrokenGlass = "status.broken_glass";

        /// <summary>
        /// 정전: <b>손패에 있는 동안</b> 다른 카드 1장이 봉인된다(사용자 확정 — 턴 종료 트리거가 아니다).
        /// 그래서 턴 훅이 없고, <c>GetSealedCardCount</c>가 손패를 세는 것이 곧 이 규칙이다.
        /// </summary>
        public const string StatusBlackout = "status.blackout";

        // ---- 저주 카드(T2, 2026-08-06): 상태 카드가 '저주'로 개편되며 추가된 9종 ----
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

        public const string DefendBlock = "defend.block";
        public const string DefendZeroThenDouble = "defend.zero_then_double";
        public const string DefendHalfReflect = "defend.half_reflect";
        public const string DefendCleanseBlock = "defend.cleanse_block";
        public const string DefendBlockDelayedImmobilize = "defend.block_delayed_immobilize";
        // D05 부적 방패 — exiles one random card from the hand and nullifies this turn's incoming damage.
        public const string DefendExileRandomNegate = "defend.exile_random_negate";
        // Source tag for the 속박 that D04/D06 booked and that lands at the start of the next turn.
        // Distinct from the card's own effectRef so presentation can tell "you took this on yourself
        // last turn" apart from the moment the card was played (cf. MoveDeferredMomentumApply).
        public const string SelfDelayedImmobilizeApply = "self.delayed_immobilize.apply";

        public const string ScoutReveal = "scout.reveal";
        public const string ScoutEnemyCountDamage = "scout.enemy_count_damage";
        public const string ScoutTreasureCountHeal = "scout.treasure_count_heal";
        public const string ScoutEnemyStun = "scout.enemy_stun";
        public const string ScoutEnemyCountHealThreshold = "scout.enemy_count_heal_threshold";

        /// <summary>S06 약점 간파(T1, 2026-08-06): 탐색 + 드러난 적 전원에게 허점. 기절초광(S03)의 비-CC 변형이라 인텐트 취소 경로가 없다.</summary>
        public const string ScoutEnemyVulnerable = "scout.enemy_vulnerable";

        public const string FieldFogRevealCampfire = "field.fog_reveal.campfire";
        public const string FieldDamageFirebomb = "field.damage.firebomb";
        public const string FieldHealSacredCampfire = "field.heal.sacred_campfire";
        public const string FieldImmobilizeFlashbang = "field.immobilize.flashbang";
        public const string FieldPlacement = "field.placement";
        public const string FieldFogReveal = "field.fog-reveal";
        public const string FieldDamage = "field.damage";
        public const string FieldHeal = "field.heal";
        // F04 흡수진 — a damage field whose damage is returned to the caster as healing. Top-level ref
        // (not a `field.damage.*` flavour alias) because it maps to its own FieldObjectKind.
        public const string FieldLifesteal = "field.lifesteal";

        public const string UtilityRedraw = "utility.redraw";
        public const string UtilityCleanseDraw = "utility.cleanse_draw";
        // U02 부적 끌어오기. Deliberately has no utility handler: the card is playable only through the
        // choice panel, and TryPlayerUtility rejects any utility effectRef it does not know.
        public const string UtilityDrawOrRecover = "utility.draw_or_recover";

        public const string ObjectiveInvestigate = "objective.investigate";

        public const string DebugApplyBind = "debug.bind";
        public const string DebugApplySlow = "debug.slow";
        public const string DebugApplyRupture = "debug.rupture";
        public const string DebugKnockback = "debug.knockback";
    }
}

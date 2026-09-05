namespace SeoulPlayup.Combat.Runtime
{
    public static class CombatCsvPaths
    {
        public const string CardDirectory = "Assets/Data/Combat/Cards/Source";
        public const string PlayerDirectory = "Assets/Data/Combat/Players/Source";
        public const string MonsterDirectory = "Assets/Data/Combat/Monsters/Source";
        public const string PresentationDirectory = "Assets/Data/Combat/Presentation/Source";
        public const string StatusEffectDirectory = "Assets/Data/Combat/StatusEffects/Source";
        public const string RelicDirectory = "Assets/Data/Combat/Relics/Source";
        public const string RewardDirectory = "Assets/Data/Combat/Reward/Source";
        public const string ItemDirectory = "Assets/Data/Combat/Items/Source";
        // 맵 오브젝트 저작은 Combat이 아니라 Object 트리에 산다 — 카탈로그 에셋과 같은 자리(P6).
        public const string MapObjectSourceDirectory = "Assets/Data/Object/Source";

        public const string CardsCsv = CardDirectory + "/cards.csv";
        public const string CardChoiceOptionsCsv = CardDirectory + "/card_choice_options.csv";
        public const string CardUpgradesCsv = CardDirectory + "/card_upgrades.csv";
        public const string CardVfxCuesCsv = CardDirectory + "/combat_card_vfx_cues.csv";
        public const string GameKeywordsCsv = CardDirectory + "/game_keywords.csv";
        public const string AttackShapesCsv = MonsterDirectory + "/attack_shapes.csv";

        /// <summary>몬스터 특성 배선 표(2026-09-04). 설명문 정본은 <see cref="GameKeywordsCsv"/> 한 파일이고
        /// 이 표는 배지·오버레이·정렬만 든다 — status_effects.csv와 game_keywords.csv의 관계와 같다.</summary>
        public const string MonsterTraitsCsv = MonsterDirectory + "/monster_traits.csv";
        public const string BossProfilesCsv = MonsterDirectory + "/boss_profiles.csv";
        public const string BossPhasesCsv = MonsterDirectory + "/boss_phases.csv";
        public const string PlayerCombatProfilesCsv = PlayerDirectory + "/player_combat_profiles.csv";
        public const string CombatVfxCuesCsv = PresentationDirectory + "/combat_vfx_cues.csv";
        public const string MonsterPatternVfxBindingsCsv = PresentationDirectory + "/monster_pattern_vfx_bindings.csv";
        public const string CombatSoundCuesCsv = PresentationDirectory + "/combat_sound_cues.csv";
        public const string StatusEffectsCsv = StatusEffectDirectory + "/status_effects.csv";
        public const string RelicsCsv = RelicDirectory + "/relics.csv";
        public const string CombatRewardsCsv = RewardDirectory + "/combat_rewards.csv";
        public const string GachaRewardsCsv = RewardDirectory + "/gacha_rewards.csv";
        public const string ShopPricesCsv = RewardDirectory + "/shop_prices.csv";

        /// <summary>몬스터 처치 추가 보상 확률(DEC-2026-08-31-02 — D-2 개정).</summary>
        public const string KillDropRatesCsv = RewardDirectory + "/kill_drop_rates.csv";
        public const string ConsumableItemsCsv = ItemDirectory + "/consumable_items.csv";
        public const string MapObjectsCsv = MapObjectSourceDirectory + "/map_objects.csv";
    }
}

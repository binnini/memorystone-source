using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    [CreateAssetMenu(
        fileName = "CombatCatalogTextAssetSource",
        menuName = "Seoul Playup/Combat/Combat Catalog Text Asset Source")]
    public sealed class CombatCatalogTextAssetSource : ScriptableObject
    {
        [Header("Player CSV")]
        [SerializeField] private TextAsset playerCombatProfiles;

        [Header("Monster CSV")]
        [SerializeField] private TextAsset monsterCatalog;
        [SerializeField] private TextAsset monsterAttackPatterns;
        [SerializeField] private TextAsset monsterPatternBindings;
        [SerializeField] private TextAsset monsterPatternVfxBindings;
        [Tooltip("attack_shapes.csv — 형상 저작 원본(§27). 몬스터 패턴 CSV 검증이 shapeId를 이 카탈로그에 대조하므로 반드시 함께 할당한다.")]
        [SerializeField] private TextAsset attackShapes;

        [Tooltip("monster_traits.csv — 특성 배선 표(배지 색·글리프·정렬·오버레이 종류). " +
            "설명문 정본은 game_keywords.csv이고 이 표는 keywordRef로 그 행을 가리킨다.")]
        [SerializeField] private TextAsset monsterTraits;

        [Header("Boss CSV")]
        [SerializeField] private TextAsset bossProfiles;
        [SerializeField] private TextAsset bossPhases;

        [Header("Presentation CSV")]
        [SerializeField] private TextAsset combatVfxCues;
        [SerializeField] private TextAsset combatSoundCues;

        [Header("Status Effect CSV")]
        [SerializeField] private TextAsset statusEffects;

        [Header("Relic CSV")]
        [SerializeField] private TextAsset relics;

        [Header("Reward CSV")]
        [SerializeField] private TextAsset combatRewards;
        [SerializeField] private TextAsset gachaRewards;
        [SerializeField] private TextAsset shopPrices;
        [Tooltip("kill_drop_rates.csv — 몬스터 처치 시 카드 3택 위에 얹히는 추가 보상 확률(DEC-2026-08-31-02).")]
        [SerializeField] private TextAsset killDropRates;

        [Header("Item CSV")]
        [SerializeField] private TextAsset consumableItems;

        [Header("Map Object CSV")]
        [Tooltip("map_objects.csv — 맵 오브젝트의 도감 저작(P6). 이름·설명·썸네일·노출만 담는다; " +
            "blocksMovement 같은 게임플레이 값의 정본은 MapObjectCatalogSet이다.")]
        [SerializeField] private TextAsset mapObjectsCodex;

        public bool HasPlayerCombatProfiles => playerCombatProfiles != null;

        public bool HasMonsterCatalog =>
            monsterCatalog != null &&
            monsterAttackPatterns != null &&
            monsterPatternBindings != null &&
            attackShapes != null &&
            combatVfxCues != null &&
            combatSoundCues != null;

        public bool HasBossCatalog => bossProfiles != null && bossPhases != null;

        public bool HasStatusEffectCatalog => statusEffects != null;

        public bool HasMonsterTraitCatalog => monsterTraits != null;

        public bool HasRelicCatalog => relics != null;

        public bool HasCardRewardWeights => combatRewards != null;

        public bool HasGachaRewardWeights => gachaRewards != null;

        public bool HasShopPrices => shopPrices != null;

        public bool HasKillDropRates => killDropRates != null;

        public bool HasConsumableItemCatalog => consumableItems != null;

        public bool HasMapObjectCodexCatalog => mapObjectsCodex != null;

        public PlayerCombatProfileCatalog CreatePlayerCombatProfileCatalog(
            string sourceId = "player-combat-profiles-text-asset",
            string displayName = "Player Combat Profiles TextAsset")
        {
            if (playerCombatProfiles == null)
            {
                throw new System.InvalidOperationException("Player combat profiles TextAsset is not assigned.");
            }

            return PlayerCombatProfileCsvConverter.ConvertText(
                playerCombatProfiles.text,
                playerCombatProfiles.name,
                sourceId,
                displayName);
        }

        /// <summary>
        /// Reward rarity distribution from <c>combat_rewards.csv</c> (CR-3). Falls back to
        /// <see cref="CardRewardRarityWeights.Default"/> at the call site when unassigned.
        /// </summary>
        public CardRewardRarityWeights CreateCardRewardWeights()
        {
            if (combatRewards == null)
            {
                throw new System.InvalidOperationException("Combat rewards TextAsset is not assigned.");
            }

            return CardRewardWeightsCsvConverter.ConvertText(combatRewards.text, combatRewards.name);
        }

        /// <summary>
        /// 인형뽑기 결과 분포(<c>gacha_rewards.csv</c>). 미할당이면 호출부가
        /// <see cref="GachaRewardWeights.Default"/>로 폴백한다.
        /// </summary>
        public GachaRewardWeights CreateGachaRewardWeights()
        {
            if (gachaRewards == null)
            {
                throw new System.InvalidOperationException("Gacha reward TextAsset is not assigned.");
            }

            return GachaRewardWeightsCsvConverter.ConvertText(gachaRewards.text, gachaRewards.name);
        }

        /// <summary>
        /// 상점 가격표(<c>shop_prices.csv</c>). 미할당이면 호출부가
        /// <see cref="ShopPrices.Default"/>로 폴백한다.
        /// </summary>
        public ShopPrices CreateShopPrices()
        {
            if (shopPrices == null)
            {
                throw new System.InvalidOperationException("Shop price TextAsset is not assigned.");
            }

            return ShopPricesCsvConverter.ConvertText(shopPrices.text, shopPrices.name);
        }

        /// <summary>
        /// 처치 추가 보상 확률(<c>kill_drop_rates.csv</c>). 미할당이면 호출부가
        /// <see cref="KillDropRates.Default"/>로 폴백한다.
        /// </summary>
        public KillDropRates CreateKillDropRates()
        {
            if (killDropRates == null)
            {
                throw new System.InvalidOperationException("Kill drop rate TextAsset is not assigned.");
            }

            return KillDropRatesCsvConverter.ConvertText(killDropRates.text, killDropRates.name);
        }

        /// <summary>
        /// 형상 카탈로그(<c>attack_shapes.csv</c> · §27)를 파싱해 <see cref="AttackShapeLibrary"/>에
        /// 싣는다. 빌드에서 형상이 데이터에 닿는 <b>유일한</b> 경로다(에디터/테스트에는 파일 폴백이
        /// 있지만 빌드에는 없다 — §19 선례의 조용한 갭 방지로, 미배선이면 명시적 예외가 난다).
        /// </summary>
        public void InitializeAttackShapeLibrary()
        {
            if (attackShapes == null)
            {
                throw new System.InvalidOperationException("Attack shapes TextAsset is not assigned.");
            }

            AttackShapeLibrary.Initialize(
                AttackShapeCatalogCsv.ConvertText(attackShapes.text, attackShapes.name));
        }

        public MonsterCatalogDefinition CreateMonsterCatalog(
            string sourceId = "designer-monster-csv-text-asset",
            string displayName = "Designer Monster CSV TextAsset Catalog")
        {
            if (!HasMonsterCatalog)
            {
                throw new System.InvalidOperationException("Monster catalog TextAssets are not fully assigned.");
            }

            // 순서가 계약이다: 패턴 CSV 변환이 shapeId를 AttackShapeLibrary.TryGet으로 대조한다.
            InitializeAttackShapeLibrary();

            return MonsterCatalogCsvTextAssetLoader.Convert(
                    monsterCatalog,
                    monsterAttackPatterns,
                    monsterPatternBindings,
                    combatVfxCues,
                    combatSoundCues,
                    monsterPatternVfxBindings,
                    sourceId,
                    displayName)
                .MonsterCatalog;
        }

        /// <summary>
        /// 보스 확장 데이터(<c>boss_profiles.csv</c> + <c>boss_phases.csv</c>).
        /// 미할당이면 호출부가 <see cref="BossCatalogDefinition.Empty"/>로 폴백한다(보스 없는 전투).
        /// </summary>
        public BossCatalogDefinition CreateBossCatalog(
            string sourceId = "designer-boss-csv-text-asset",
            string displayName = "Designer Boss CSV TextAsset Catalog")
        {
            if (!HasBossCatalog)
            {
                throw new System.InvalidOperationException("Boss catalog TextAssets are not fully assigned.");
            }

            return BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(
                bossProfiles.text,
                bossPhases.text,
                sourceId,
                displayName));
        }

        public StatusEffectCatalogDefinition CreateStatusEffectCatalog()
        {
            if (!HasStatusEffectCatalog)
            {
                throw new System.InvalidOperationException("Status effect TextAsset is not assigned.");
            }

            return StatusEffectCatalogCsv.ConvertText(statusEffects.text, statusEffects.name);
        }

        /// <summary>특성 배선 표. 배지·툴팁·호버 오버레이가 전부 이 한 벌을 지난다.</summary>
        public MonsterTraitCatalogDefinition CreateMonsterTraitCatalog()
        {
            if (!HasMonsterTraitCatalog)
            {
                throw new System.InvalidOperationException("Monster trait TextAsset is not assigned.");
            }

            return MonsterTraitCatalogCsv.ConvertText(monsterTraits.text, monsterTraits.name);
        }

        public RelicCatalogDefinition CreateRelicCatalog()
        {
            if (!HasRelicCatalog)
            {
                throw new System.InvalidOperationException("Relic TextAsset is not assigned.");
            }

            return RelicCatalogCsv.ConvertText(relics.text, relics.name);
        }

        public ConsumableItemCatalogDefinition CreateConsumableItemCatalog()
        {
            if (!HasConsumableItemCatalog)
            {
                throw new System.InvalidOperationException("Consumable item TextAsset is not assigned.");
            }

            return ConsumableItemCatalogCsv.ConvertText(consumableItems.text, consumableItems.name);
        }

        /// <summary>
        /// 맵 오브젝트의 도감 저작(P6). 🔴빌드에서는 <c>Assets/</c> 경로가 없으므로 이 TextAsset
        /// 경로가 <b>유일한</b> 정본이다 — 소스 CSV 직독 폴백은 에디터에서만 산다.
        /// </summary>
        public CodexObjectCatalog CreateMapObjectCodexCatalog()
        {
            if (!HasMapObjectCodexCatalog)
            {
                throw new System.InvalidOperationException("Map object codex TextAsset is not assigned.");
            }

            return CodexObjectCatalogCsv.ConvertText(mapObjectsCodex.text, mapObjectsCodex.name);
        }
    }
}

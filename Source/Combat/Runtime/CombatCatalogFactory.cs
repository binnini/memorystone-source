using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    public static class CombatCatalogFactory
    {
        public const string ThreeEyeDogMonsterId = "M001";
        public const string DefaultPlayerCombatProfileCsv = CombatCsvPaths.PlayerCombatProfilesCsv;
        public const string DefaultMonsterCsvDirectory = CombatCsvPaths.MonsterDirectory;
        public const string CampfireCardId = ApprovedCardCatalogFactory.FieldSacredCampfireId;

        public static PlayerCombatProfileCatalog CreatePlayerCombatProfileCatalog()
        {
            return PlayerCombatProfileCsvConverter.ConvertFile(
                DefaultPlayerCombatProfileCsv,
                "player-combat-profiles-csv",
                "Player Combat Profiles CSV");
        }

        public static PlayerCombatProfile GetPlayerCombatProfile(string profileId)
        {
            return CreatePlayerCombatProfileCatalog().GetProfileOrDefault(profileId);
        }

        public static MonsterCatalogDefinition CreateMonsterCatalog(CombatConfig config)
        {
            return MonsterCatalogCsvConverter.ConvertDirectories(
                    DefaultMonsterCsvDirectory,
                    CombatCsvPaths.PresentationDirectory,
                    "designer-monster-csv",
                    "Designer Monster CSV Catalog")
                .MonsterCatalog;
        }

        /// <summary>
        /// 보스 확장 데이터(<c>boss_profiles.csv</c> + <c>boss_phases.csv</c>). 몬스터 카탈로그와 달리
        /// <see cref="CombatState"/> 생성자가 자동으로 부르지 않는다 — 보스 없는 전투가 대부분이라
        /// 로딩은 호출부가 명시적으로 한다.
        /// </summary>
        public static BossCatalogDefinition CreateBossCatalog()
        {
            return BossCatalogCsvConverter.ConvertDirectory(
                DefaultMonsterCsvDirectory,
                "designer-boss-csv",
                "Designer Boss CSV Catalog");
        }

        public static CardCatalogDefinition CreateCardCatalog(CombatConfig config)
        {
            return ApprovedCardCatalogFactory.CreateApprovedCatalog(config);
        }
    }
}

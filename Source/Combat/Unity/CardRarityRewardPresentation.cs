using SeoulPlayup.CardCore;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Maps a card reward rarity to its presentation color and Korean label.
    /// Shared by the reward glow tint and the rarity badge chip so both stay in sync.
    /// Basic returns false (Basic cards never appear as rewards, so they get no styling).
    /// </summary>
    public static class CardRarityRewardPresentation
    {
        // 희귀 파랑, 영웅 보라, 전설 금.
        private static readonly Color RareColor = Hex("#3FA9F5");
        private static readonly Color EpicColor = Hex("#B45BFF");
        private static readonly Color LegendaryColor = Hex("#FFB031");

        public static bool TryGet(CardRarity rarity, out Color color, out string label)
        {
            switch (rarity)
            {
                case CardRarity.Rare:
                    color = RareColor;
                    label = "희귀"; // 희귀
                    return true;
                case CardRarity.Epic:
                    color = EpicColor;
                    label = "영웅"; // 영웅
                    return true;
                case CardRarity.Legendary:
                    color = LegendaryColor;
                    label = "전설"; // 전설
                    return true;
                default:
                    color = Color.white;
                    label = string.Empty;
                    return false;
            }
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.white;
        }
    }
}

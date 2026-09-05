namespace SeoulPlayup.CardCore
{
    /// <summary>
    /// Card reward rarity grade. Drives the weighted draw in card reward offers
    /// (treasure chests, monster-kill rewards, rerolls). Basic cards never appear
    /// as rewards; Rare/Epic/Legendary are weighted by the reward generator.
    /// </summary>
    public enum CardRarity
    {
        Basic,
        Rare,
        Epic,
        Legendary
    }
}

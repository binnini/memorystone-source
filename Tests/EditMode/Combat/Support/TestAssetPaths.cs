namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Shipping asset paths referenced by more than one test file. A rename/move is one edit here;
    /// paths used by a single file can stay as that file's own constants.
    /// </summary>
    public static class TestAssetPaths
    {
        public const string PrototypeTestScene = "Assets/Scenes/Dev/PrototypeTest.unity";
        public const string CardFrontPrefab = "Assets/Prefabs/UI/Cards/CardFront.prefab";
        public const string MoveCardFrontPrefab = "Assets/Prefabs/UI/Cards/CardFront_Move.prefab";
        public const string ActionCardFrontPrefab = "Assets/Prefabs/UI/Cards/CardFront_Action.prefab";
        public const string CardCatalogAsset = "Assets/Data/Combat/Cards/Catalogs/CardCatalog.asset";
        public const string SoundCatalogAsset = "Assets/Data/Combat/Presentation/Catalogs/SoundCatalog.asset";
        public const string MoveCardFrame = "Assets/Art/UI/Cards/card_frame_move.png";
        public const string ActionCardFrame = "Assets/Art/UI/Cards/card_frame_action.png";
    }
}

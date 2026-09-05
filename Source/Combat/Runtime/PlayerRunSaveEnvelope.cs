using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Disk-persisted single-slot wrapper around <see cref="PlayerRunSaveData"/>. Captured at the
    /// end of each overall turn; resuming re-enters the saved stage with the persisted player
    /// loadout (HP, decks, inventory) — monster/map state is intentionally not persisted.
    /// Follows the save DTO contract (public fields only, no Nullable&lt;T&gt;) for JsonUtility.
    /// </summary>
    [Serializable]
    public sealed class PlayerRunSaveEnvelope
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string StageId = string.Empty;
        public int OverallTurn;
        // 배치 랜덤화 시드(placement-randomization-plan §2-2). 런 세이브 재개는 저장된 스테이지에
        // 다시 들어가므로 같은 시드로 같은 배치를 재생성해야 한다. HasPlacementSeed=false(구세이브
        // 기본값)는 「랜덤화 미적용(저작 원본)」으로 해석한다 — 마이그레이션 불요.
        public bool HasPlacementSeed;
        public int PlacementSeed;
        public PlayerRunSaveData Player = new PlayerRunSaveData();

        public static PlayerRunSaveEnvelope Create(string stageId, int overallTurn, PlayerRunSaveData player, bool hasPlacementSeed = false, int placementSeed = 0)
        {
            if (string.IsNullOrWhiteSpace(stageId)) throw new ArgumentException("Stage id is required.", nameof(stageId));
            if (player == null) throw new ArgumentNullException(nameof(player));

            return new PlayerRunSaveEnvelope
            {
                SchemaVersion = CurrentSchemaVersion,
                StageId = stageId.Trim(),
                OverallTurn = Math.Max(1, overallTurn),
                HasPlacementSeed = hasPlacementSeed,
                PlacementSeed = placementSeed,
                Player = player
            };
        }

        /// <summary>
        /// Structural validity for a deserialized envelope. A version other than
        /// <see cref="CurrentSchemaVersion"/> is rejected (no migration paths exist yet).
        /// </summary>
        public bool IsValid(out string reason)
        {
            if (SchemaVersion != CurrentSchemaVersion)
            {
                reason = $"Unsupported schema version {SchemaVersion} (expected {CurrentSchemaVersion}).";
                return false;
            }

            if (string.IsNullOrWhiteSpace(StageId))
            {
                reason = "Stage id is missing.";
                return false;
            }

            if (Player == null)
            {
                reason = "Player payload is missing.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}

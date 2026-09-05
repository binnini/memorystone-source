using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Disk-persisted single-slot wrapper around <see cref="CombatSuspendData"/> — the ② suspend layer.
    /// Written to a slot physically separate from the ① <see cref="PlayerRunSaveEnvelope"/> whenever the
    /// player leaves an in-progress combat at a consistent turn boundary, and deleted immediately after a
    /// successful resume (anti-scum). Follows the save DTO contract (public fields only, no Nullable&lt;T&gt;)
    /// for JsonUtility.
    /// </summary>
    [Serializable]
    public sealed class CombatSuspendEnvelope
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string StageId = string.Empty;
        public int OverallTurn;
        // 배치 랜덤화 시드(placement-randomization-plan §2-2). 서스펜드 스냅샷은 몬스터·함정을 통째로
        // 복원하지만 보물상자 등 이벤트 오브젝트는 맵 재빌드에 의존한다 — 시드 없이 재개하면 상자
        // 위치가 새로 굴려져 ClaimedEventObjectIds 정합이 깨진다. HasPlacementSeed=false(구세이브
        // 기본값)는 「랜덤화 미적용(저작 원본)」으로 해석한다 — 마이그레이션 불요.
        public bool HasPlacementSeed;
        public int PlacementSeed;
        // 보상 난수(스트림 7) 커서(seed-determinism-handoff P5). 보상 난수원은 전투 상태가 아니라 컨트롤러가
        // 소유하므로 Combat.RngCursors가 아니라 봉투에 실린다. 0 = 첫 칸(구세이브 기본값).
        public int RewardCursor;
        public CombatSuspendData Combat = new CombatSuspendData();

        public static CombatSuspendEnvelope Create(string stageId, int overallTurn, CombatSuspendData combat, bool hasPlacementSeed = false, int placementSeed = 0, int rewardCursor = 0)
        {
            if (string.IsNullOrWhiteSpace(stageId)) throw new ArgumentException("Stage id is required.", nameof(stageId));
            if (combat == null) throw new ArgumentNullException(nameof(combat));

            return new CombatSuspendEnvelope
            {
                SchemaVersion = CurrentSchemaVersion,
                StageId = stageId.Trim(),
                OverallTurn = Math.Max(1, overallTurn),
                HasPlacementSeed = hasPlacementSeed,
                PlacementSeed = placementSeed,
                RewardCursor = Math.Max(0, rewardCursor),
                Combat = combat
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

            if (Combat == null)
            {
                reason = "Combat payload is missing.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// The boss catalog CSV schema, in one place. Test fixtures compose rows on top of these
    /// headers instead of each file repeating the column list — so a schema key rename is one edit
    /// here plus the converter, not a hunt across every boss test.
    /// </summary>
    public static class BossCsv
    {
        public const string ProfilesHeader = "bossId,displayName,phaseMetric,mechanicId,mechanicParams,introCinematic,deathCinematic,bgmCueBase,designerNote";
        public const string PhasesHeader = "bossId,phaseIndex,threshold,strengthBonusPercent,maxHpBonus,patternPhaseMin,visualScale,footprintRadius,auraStatusKind,designerNote";
    }
}

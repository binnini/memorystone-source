namespace SeoulPlayup.Combat.Unity
{
    // Timing profile-vs-field fallback resolution extracted from MapCombatController (refactoring
    // stage 4-3). Every method mirrors the same shape: prefer the assigned CombatTimingProfile
    // asset, else fall back to the per-field inspector value passed in by the host, so existing
    // scenes/tests behave identically when no profile is set. Pure/stateless — no host seam needed.
    public static class CombatTimingDriver
    {
        public static float ResolvePlayerMoveSeconds(CombatTimingProfile profile, float fallback) =>
            profile != null ? profile.PlayerMoveSeconds : fallback;

        public static float ResolveEnemyMoveStartDelay(CombatTimingProfile profile, float fallback) =>
            profile != null ? profile.EnemyMoveStartDelay : fallback;

        public static float ResolveEnemyMoveSeconds(CombatTimingProfile profile, float fallback) =>
            profile != null ? profile.EnemyMoveSeconds : fallback;

        public static float ResolveAttackWindupDelay(CombatTimingProfile profile, float fallback) =>
            profile != null ? profile.AttackWindupDelay : fallback;

        public static float ResolveAttackImpactDelay(CombatTimingProfile profile, float fallback) =>
            profile != null ? profile.AttackImpactDelay : fallback;

        public static float ResolveDeathDelay(CombatTimingProfile profile, float fallback) =>
            profile != null ? profile.DeathDelay : fallback;

        public static float ResolveHitStopAnimationSpeed(CombatTimingProfile profile) =>
            profile != null ? profile.HitStopAnimationSpeed : 0f;

        public static float ResolveHitStopTimeScale(CombatTimingProfile profile) =>
            profile != null ? profile.HitStopTimeScale : 1f;

        // Defaults off (no profile) so legacy timing (effects firing the instant rules resolve) is preserved.
        public static bool ResolveAlignImpactToAnimation(CombatTimingProfile profile) =>
            profile != null && profile.AlignImpactToAnimation;

        public static float ResolvePlayerAttackHitStopSeconds(CombatTimingProfile profile, bool lethal) =>
            profile != null ? profile.ResolvePlayerAttackHitStopSeconds(lethal) : 0f;

        public static float ResolveMonsterAttackHitStopSeconds(CombatTimingProfile profile, bool lethal) =>
            profile != null ? profile.ResolveMonsterAttackHitStopSeconds(lethal) : 0f;
    }
}

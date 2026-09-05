namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Per-card / per-VFX policy describing whether an effect should spawn a floating damage/heal/status
    /// number when its presentation plays. Authored in <c>combat_card_vfx_cues.csv</c> and stored on each
    /// <c>EffectVfxCatalog.Entry</c>. Pure runtime layer: no UnityEngine dependency.
    /// </summary>
    public enum EffectFloatingTextMode
    {
        /// <summary>Use the built-in default policy (show numbers, suppress movement/push text).</summary>
        Auto = 0,

        /// <summary>Always spawn floating text for this effect.</summary>
        Show = 1,

        /// <summary>Never spawn floating text for this effect (movement cards, silent VFX).</summary>
        Hide = 2
    }
}

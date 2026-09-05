using System;
using System.Collections.Generic;
using SeoulPlayup.CardCore;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    // Refactoring stage 4-5 (card/HUD presentation seam). Homologous to ICombatAudioHost (4-0) and
    // ICombatCameraHost (4-1): breaks the MapCombatController <-> Cards/HUD view bidirectional
    // concrete coupling so the Cards.Unity/UI.Hud asmdef split (Stage 3) becomes possible. Lives in
    // Combat/Unity (not Combat.Runtime) because these members need UnityEngine types (GameObject,
    // Transform, Vector2).
    //
    // ICombatCardHudHost itself moved to SeoulPlayup.Combat.Contracts (see ICombatCardHudHost.cs)
    // since Cards.Unity views need to reference it without depending on Combat.Unity.

    /// <summary>
    /// Seam interface for the card reward popup view, replacing MapCombatController's direct
    /// <c>CardRewardPopupView</c> reference at its call sites. Exposes only what those sites use.
    /// </summary>

    public interface ICardRewardPopupView
    {
        GameObject gameObject { get; }
        Transform transform { get; }
        void Hide();
        void AutoBindFromHierarchy();
        void Show(
            IReadOnlyList<CardRewardOffer> offers,
            Action<string> onSelected,
            Action onSkip,
            Action onHover,
            Action onRerollRequested,
            bool rerollAvailable);
        void ReplaceOffers(IReadOnlyList<CardRewardOffer> offers, bool rerollAvailable);
    }
}

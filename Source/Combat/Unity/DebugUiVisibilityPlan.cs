using System;
using System.Collections.Generic;

// Lives in SeoulPlayup.Combat, not SeoulPlayup.Dev, because the UI hide ladder ships: the Dev assembly is
// constrained to UNITY_EDITOR (cs:757), which excludes it from every player build — development builds
// included, and those are exactly where this toggle is meant to run. See DebugUiVisibilityController.
namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Three-step debug ladder for hiding gameplay UI while a build is being played.
    /// Full = shipping look, HandOnly = only the card rows survive (map + VFX + floating text
    /// stay because those live outside the canvas), None = nothing but the world.
    /// </summary>
    public enum DebugUiVisibilityLevel
    {
        Full = 0,
        HandOnly = 1,
        None = 2
    }

    /// <summary>
    /// What a UI root is, for the purpose of the debug ladder. The classification is by object
    /// name so runtime-created UI (tooltips, pile overlays) falls into a bucket without wiring.
    /// </summary>
    public enum DebugUiGroup
    {
        /// The card rows themselves (MoveCards / ActionCards inside CardLane).
        Hand,

        /// The rest of the bottom lane: HP, energy, piles, status icons, turn phase, end-turn.
        HandDock,

        /// Always-on chrome: sidebar, tooltips, dev panels.
        Chrome,

        /// Blocking, transient surfaces the player must answer to keep playing
        /// (card choice, fork, reward, victory/defeat, cutscene).
        Modal
    }

    /// <summary>
    /// Pure decision layer for <see cref="DebugUiVisibilityController"/>: name -> group, and
    /// (group, level) -> visible. Kept free of UnityEngine so the ladder is EditMode-testable
    /// without a scene, mirroring how CinematicUiHider keeps scene access behind a delegate.
    /// </summary>
    public static class DebugUiVisibilityPlan
    {
        public const string GameplayLayerRootName = "Gameplay UI Layers";
        public const string CardLaneName = "CardLane";
        public const string RuntimeDebugUiName = "03 Runtime Debug UI";
        public const string CardRewardPopupCanvasName = "Card Reward Popup Canvas";

        private static readonly HashSet<string> HandChildNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "MoveCards",
            "ActionCards",
            // The draw/discard flight animation belongs to the hand: without it cards teleport.
            "CardFlightOverlay"
        };

        private static readonly HashSet<string> ModalRootNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Card Selection Overlay Root",
            "Choice Overlay Root",
            "Game Victory Overlay Root",
            "Game Over Overlay Root",
            "CutSceneWithText",
            // Two separate reward surfaces exist: this runtime-created overlay inside the gameplay
            // layer root, and the scene-root popup canvas below. Both must count as modal.
            "Card Reward Overlay Root",
            ShopPopupView.RootName,
            ServiceObjectPopupView.RootName,
            CardRewardPopupCanvasName
        };

        /// UI roots that are NOT children of "Gameplay UI Layers" but still belong to the ladder.
        /// Easy to miss: the reward popup lives on its own scene-root canvas.
        public static readonly string[] ExternalRootNames =
        {
            RuntimeDebugUiName,
            CardRewardPopupCanvasName
        };

        public static bool IsCardLane(string objectName)
        {
            return string.Equals(objectName, CardLaneName, StringComparison.Ordinal);
        }

        /// Classifies a direct child of CardLane. Everything that is not a card row is dock chrome.
        public static DebugUiGroup ClassifyCardLaneChild(string objectName)
        {
            return HandChildNames.Contains(objectName ?? string.Empty)
                ? DebugUiGroup.Hand
                : DebugUiGroup.HandDock;
        }

        /// Classifies a UI root (a child of the gameplay layer root, or one of
        /// <see cref="ExternalRootNames"/>). CardLane is never classified here — the caller
        /// descends into it and classifies its children instead.
        public static DebugUiGroup ClassifyRoot(string objectName)
        {
            return ModalRootNames.Contains(objectName ?? string.Empty)
                ? DebugUiGroup.Modal
                : DebugUiGroup.Chrome;
        }

        public static bool IsVisible(DebugUiGroup group, DebugUiVisibilityLevel level, bool hideModalsInHandOnly)
        {
            switch (level)
            {
                case DebugUiVisibilityLevel.Full:
                    return true;
                case DebugUiVisibilityLevel.None:
                    return false;
                case DebugUiVisibilityLevel.HandOnly:
                    switch (group)
                    {
                        case DebugUiGroup.Hand:
                            return true;
                        // Modals block progress until answered, so they stay by default; flip the
                        // flag when capturing a clean shot matters more than continuing the run.
                        case DebugUiGroup.Modal:
                            return !hideModalsInHandOnly;
                        default:
                            return false;
                    }
                default:
                    return true;
            }
        }

        public static DebugUiVisibilityLevel Next(DebugUiVisibilityLevel level)
        {
            switch (level)
            {
                case DebugUiVisibilityLevel.Full:
                    return DebugUiVisibilityLevel.HandOnly;
                case DebugUiVisibilityLevel.HandOnly:
                    return DebugUiVisibilityLevel.None;
                default:
                    return DebugUiVisibilityLevel.Full;
            }
        }

        public static string Describe(DebugUiVisibilityLevel level)
        {
            switch (level)
            {
                case DebugUiVisibilityLevel.HandOnly:
                    return "UI 숨김: 손패만  (\\ 또는 F1 · Shift+키 = 복귀)";
                case DebugUiVisibilityLevel.None:
                    return "UI 숨김: 전체  (\\ 또는 F1 · Shift+키 = 복귀)";
                default:
                    return string.Empty;
            }
        }
    }
}

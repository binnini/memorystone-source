using System;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Tutorial
{
    public enum TutorialAdvanceMode
    {
        Click,
        CardSelected,
        TargetSelected,
        ButtonPressed,
        KeyPressed,
        HoverCompleted,
        CombatEvent,
        RewardSelected,
        Auto,
        HoverReleased
    }

    public enum TutorialButtonId
    {
        None,
        EndMovePhase,
        EndTurn,
        RewardCardSelect,
        HandCardSelectionConfirm,
        HandCardSelectionCancel
    }

    public enum TutorialKeyId
    {
        None,
        A,
        D,
        W,
        S,
        Y,
        Space,
        F,
        Q,
        E
    }

    // Where the tutorial panel sits relative to the step's focus (the Highlight target). Auto picks the side
    // with the most room; Top is the legacy fixed top-centre slot; Center is for focus-less key/narration beats.
    public enum TutorialPanelPlacement
    {
        Auto,
        Top,
        Above,
        Below,
        Left,
        Right,
        Center
    }

    // How the spotlight treats a resolved focus target.
    public enum TutorialFocusShape
    {
        Rect,      // rounded-rect hole (HUD buttons, popups)
        Ellipse,   // elliptical hole (characters)
        NoHole     // no hole: the target is lifted above the dim (cards) or already carries its own overlay (tiles)
    }

    // A focus target resolved to the screen by the host: where it is, what hole to cut, how dark the rest is.
    public readonly struct TutorialFocus
    {
        public TutorialFocus(Rect screenRect, TutorialFocusShape shape, float dimStrength = 1f, string targetKey = null, bool blocksInput = true)
        {
            ScreenRect = screenRect;
            Shape = shape;
            DimStrength = dimStrength;
            TargetKey = targetKey;
            BlocksInput = blocksInput;
        }

        // False = the dim is purely visual and swallows no pointer input (world-tile steps: the map's own input
        // ignores clicks while the pointer is over any UI, so a hole-less blocking dim would freeze the step).
        public bool BlocksInput { get; }

        public Rect ScreenRect { get; }
        public TutorialFocusShape Shape { get; }
        // 0..1 multiplier on the dim alpha (tiles keep their own overlay readable under a lighter dim).
        public float DimStrength { get; }
        // Identity of the target; the hole snaps while the key is unchanged and eases only when it changes.
        public string TargetKey { get; }
    }

    public enum TutorialHighlightTargetType
    {
        None,
        UiElement,
        Card,
        Tile,
        Monster,
        FieldObject,
        StatusIcon,
        RewardCard
    }

    [Serializable]
    public sealed class TutorialHexCoordSpec
    {
        [SerializeField] private bool enabled;
        [SerializeField] private int q;
        [SerializeField] private int r;

        public bool Enabled => enabled;
        public int Q => q;
        public int R => r;
        public HexCoord ToHexCoord() => new HexCoord(q, r);

        public bool Matches(HexCoord coord)
        {
            return !enabled || (coord.Q == q && coord.R == r);
        }
    }

    [Serializable]
    public sealed class TutorialRequirement
    {
        [SerializeField] private string requiredCardId;
        [SerializeField] private string requiredCardInstanceId;
        [SerializeField] private string requiredCardName;
        [SerializeField] private TutorialHexCoordSpec requiredTargetCoord = new TutorialHexCoordSpec();
        [SerializeField] private string requiredTargetUnitId;
        [SerializeField] private TutorialButtonId requiredButton;
        [SerializeField] private TutorialKeyId requiredKey;
        [Tooltip("Other keys that also satisfy a KeyPressed step (e.g. E next to Q). All of them are shown as keycaps.")]
        [SerializeField] private List<TutorialKeyId> requiredKeyAlternatives = new List<TutorialKeyId>();
        [SerializeField] private string requiredHoverTargetId;
        [SerializeField] private string requiredCombatEventId;
        [SerializeField] private int requiredSelectedHandCardCount;
        [Tooltip("When set, a TargetSelected step accepts the tile of any living monster instead of a fixed " +
                 "coord — so attacking a monster works even after it has moved.")]
        [SerializeField] private bool requiredTargetIsMonster;

        public string RequiredCardId => requiredCardId;
        public string RequiredCardInstanceId => requiredCardInstanceId;
        public string RequiredCardName => requiredCardName;
        public TutorialHexCoordSpec RequiredTargetCoord => requiredTargetCoord;
        public string RequiredTargetUnitId => requiredTargetUnitId;
        public TutorialButtonId RequiredButton => requiredButton;
        public TutorialKeyId RequiredKey => requiredKey;
        public IReadOnlyList<TutorialKeyId> RequiredKeyAlternatives => requiredKeyAlternatives;
        public string RequiredHoverTargetId => requiredHoverTargetId;
        public string RequiredCombatEventId => requiredCombatEventId;
        public int RequiredSelectedHandCardCount => requiredSelectedHandCardCount;
        public bool RequiredTargetIsMonster => requiredTargetIsMonster;

        public bool MatchesCard(CombatCardSnapshot card)
        {
            return MatchesOptional(requiredCardId, card.Id)
                || MatchesOptional(requiredCardId, card.CatalogSourceId)
                || MatchesOptional(requiredCardId, card.SelectionKey)
                || MatchesOptional(requiredCardInstanceId, card.InstanceId)
                || MatchesOptional(requiredCardName, card.Name)
                || (string.IsNullOrWhiteSpace(requiredCardId)
                    && string.IsNullOrWhiteSpace(requiredCardInstanceId)
                    && string.IsNullOrWhiteSpace(requiredCardName));
        }

        public bool MatchesTarget(HexCoord coord)
        {
            return requiredTargetCoord == null || requiredTargetCoord.Matches(coord);
        }

        public bool MatchesButton(TutorialButtonId button)
        {
            return MatchesButton(button, -1);
        }

        public bool MatchesButton(TutorialButtonId button, int selectedHandCardCount)
        {
            if (requiredButton != TutorialButtonId.None && requiredButton != button)
            {
                return false;
            }

            return requiredSelectedHandCardCount <= 0
                || selectedHandCardCount < 0
                || selectedHandCardCount >= requiredSelectedHandCardCount;
        }

        public bool MatchesKey(TutorialKeyId key)
        {
            if (requiredKey == TutorialKeyId.None || requiredKey == key)
            {
                return true;
            }

            return requiredKeyAlternatives != null && requiredKeyAlternatives.Contains(key);
        }

        public bool MatchesHover(string targetId)
        {
            if (string.IsNullOrWhiteSpace(requiredHoverTargetId))
            {
                return true;
            }

            var expected = requiredHoverTargetId.Trim();
            var actual = targetId?.Trim() ?? string.Empty;

            // A trailing ':' marks a category prefix (e.g. "monster:" matches any monster hover id),
            // otherwise the hover target id must match exactly.
            if (expected.EndsWith(":", StringComparison.Ordinal))
            {
                return actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesCombatEvent(string eventId)
        {
            return string.IsNullOrWhiteSpace(requiredCombatEventId)
                || string.Equals(requiredCombatEventId, eventId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesOptional(string expected, string actual)
        {
            return !string.IsNullOrWhiteSpace(expected)
                && string.Equals(expected.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    public sealed class TutorialHighlight
    {
        [SerializeField] private TutorialHighlightTargetType targetType;
        [SerializeField] private string targetId;
        [SerializeField] private TutorialHexCoordSpec coord = new TutorialHexCoordSpec();

        public TutorialHighlightTargetType TargetType => targetType;
        public string TargetId => targetId;
        public TutorialHexCoordSpec Coord => coord;
        public bool IsSet => targetType != TutorialHighlightTargetType.None;
    }

    [Serializable]
    public sealed class TutorialStep
    {
        [SerializeField] private string stepId;
        [SerializeField] private string speaker;
        [TextArea(2, 8)]
        [SerializeField] private string bodyText;
        [SerializeField] private Sprite image;
        [SerializeField] private TutorialAdvanceMode advanceMode = TutorialAdvanceMode.Click;
        [SerializeField] private TutorialRequirement requirement = new TutorialRequirement();
        [SerializeField] private TutorialHighlight highlight = new TutorialHighlight();
        [Tooltip("Secondary thing the text talks about (e.g. the HP dock) that is NOT the action target. Drawn as a thin " +
                 "ring only — the spotlight hole always follows Highlight, so putting the hole here would block the action.")]
        [SerializeField] private TutorialHighlight calloutTarget = new TutorialHighlight();
        [Tooltip("Where the text panel sits. Auto = beside the Highlight target on the side with the most room; " +
                 "Top = legacy fixed top-centre slot; Center = screen centre (focus-less key/narration beats).")]
        [SerializeField] private TutorialPanelPlacement panelPlacement = TutorialPanelPlacement.Auto;
        [Tooltip("Extra offset (canvas px) applied after placement, for hand-tuned nudges.")]
        [SerializeField] private Vector2 panelOffset;
        [Tooltip("Optional keyboard shortcut cue shown next to the panel on a ButtonPressed step (e.g. F for end phase). " +
                 "KeyPressed steps derive their cue from Requirement.RequiredKey and ignore this.")]
        [SerializeField] private TutorialKeyId shortcutHintKey = TutorialKeyId.None;
        [SerializeField] private string blockedFeedback = "튜토리얼의 지시에 따라주세요.";
        [Tooltip("When set, this step stays hidden until the matching combat event fires (e.g. a boss " +
                 "entering the player's vision). Until then the tutorial does not gate player input.")]
        [SerializeField] private string startCombatEventId;
        [Tooltip("When enabled, presenting this step briefly pans the gameplay camera to this hex (e.g. the " +
                 "memory stone) before returning to the player, so the tutorial can point out a goal.")]
        [SerializeField] private TutorialHexCoordSpec cameraFocusCoord = new TutorialHexCoordSpec();
        [Tooltip("How long (seconds) the camera dwells on the focus hex before panning back to the player.")]
        [SerializeField] private float cameraFocusHoldSeconds = 1.5f;
        [Tooltip("Card ids injected into the player's hand when this step is presented, so a scripted move " +
                 "(e.g. needing both a 3-tile and a 1-tile move) always has the required cards available.")]
        [SerializeField] private List<string> injectHandCardIds = new List<string>();
        [Tooltip("When enabled, presenting this step permanently lifts the fog around this hex (e.g. so the " +
                 "boss the tutorial is pointing the camera at is actually visible instead of hidden in fog).")]
        [SerializeField] private TutorialHexCoordSpec revealAreaCoord = new TutorialHexCoordSpec();
        [Tooltip("Radius (in hexes) of the fog lifted around Reveal Area Coord. 0 reveals only that hex.")]
        [SerializeField] private int revealAreaRadius = 2;

        public string StepId => stepId;
        public string Speaker => speaker;
        public string BodyText => bodyText;
        public Sprite Image => image;
        public TutorialAdvanceMode AdvanceMode => advanceMode;
        public TutorialRequirement Requirement => requirement;
        public TutorialHighlight Highlight => highlight;
        public TutorialHighlight CalloutTarget => calloutTarget;
        public TutorialPanelPlacement PanelPlacement => panelPlacement;
        public Vector2 PanelOffset => panelOffset;
        public TutorialKeyId ShortcutHintKey => shortcutHintKey;
        // The keycaps to show for this step: the gated key(s) on a KeyPressed step, else the optional shortcut hint.
        public List<TutorialKeyId> CueKeys
        {
            get
            {
                var keys = new List<TutorialKeyId>();
                if (advanceMode == TutorialAdvanceMode.KeyPressed && requirement != null)
                {
                    if (requirement.RequiredKey != TutorialKeyId.None)
                    {
                        keys.Add(requirement.RequiredKey);
                    }

                    foreach (var alt in requirement.RequiredKeyAlternatives)
                    {
                        if (alt != TutorialKeyId.None && !keys.Contains(alt))
                        {
                            keys.Add(alt);
                        }
                    }
                }
                else if (shortcutHintKey != TutorialKeyId.None)
                {
                    keys.Add(shortcutHintKey);
                }

                return keys;
            }
        }
        public string StartCombatEventId => startCombatEventId;
        public TutorialHexCoordSpec CameraFocusCoord => cameraFocusCoord;
        public float CameraFocusHoldSeconds => cameraFocusHoldSeconds;
        public IReadOnlyList<string> InjectHandCardIds => injectHandCardIds;
        public TutorialHexCoordSpec RevealAreaCoord => revealAreaCoord;
        public int RevealAreaRadius => revealAreaRadius;
        public string BlockedFeedback => string.IsNullOrWhiteSpace(blockedFeedback) ? "튜토리얼의 지시에 따라주세요." : blockedFeedback;
    }
}

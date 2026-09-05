using System.Collections;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// REQ7: announces turn/phase transitions inside the CardLane <c>TurnPhaseDock</c>. Driven by
    /// <see cref="CombatState.PhaseChanged"/> (forwarded by <c>MapCombatController</c>), each transition is
    /// queued and shown as a centred fading text with a fixed dwell so the player clearly registers every
    /// beat. All four combat decision/resolve phases are announced so the split monster movement/action
    /// sequence remains legible.
    /// Queued sequentially so rapid transitions never overlap. Auto-builds its TMP label + CanvasGroup so the
    /// dock GameObject can stay an empty container in the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurnPhaseDockView : MonoBehaviour
    {
        private const string AnnouncementTextName = "TurnPhase_TMP";
        private const string CurrentPhaseTextName = "CurTurnPhase";
        // 2026-07-25 user feedback: banners felt slow — per-banner time cut from 2.5+1.0=3.5s to 1.2+0.4=1.6s.
        private const float DwellSeconds = 1.2f;
        private const float OverallTurnDwellSeconds = 1.0f;
        private const float FadeSeconds = 0.4f;
        private const float CurrentPhaseFontSize = 22f;
        // 2026-07-26 user feedback: authored TurnPhase_TMP font (50) read too large and prefab edits did not
        // reliably reflect at runtime, so the transient announcement font is forced from code (the banner is
        // already fully code-driven: margins/alignment/centring). ~25% smaller than the old 50.
        private const float AnnouncementFontSize = 38f;
        private static readonly Color PlayerColor = new Color(0.72f, 0.92f, 1f, 1f);
        private static readonly Color MonsterColor = new Color(1f, 0.58f, 0.52f, 1f);
        private static readonly Color NeutralColor = new Color(1f, 0.95f, 0.78f, 1f);

        // Scene-authoring gate: when false (default) the authored CurTurnPhase font size is preserved and
        // runtime only drives the phase-state colour. Enable to force the generated font size instead.
        [Tooltip("When false (default), the authored CurTurnPhase font size is kept; runtime only sets the phase colour. Enable to force the generated font size.")]
        [SerializeField] private bool applyGeneratedStyle;

        // Hidden via CanvasGroup alpha (not SetActive) so EnsureBuilt keeps finding/binding the label and
        // re-enabling is a pure inspector toggle. The transient TurnPhase_TMP announcement is unaffected.
        [Tooltip("When false (default), the persistent CurTurnPhase readout is hidden (alpha 0). Transition announcements still play.")]
        [SerializeField] private bool showCurrentPhaseReadout;

        /// <summary>Most recently enabled dock, so the controller can reach it without a hierarchy walk
        /// (the CardLane may live under a detached HUD canvas). Single combat HUD per scene in practice.</summary>
        public static TurnPhaseDockView Active { get; private set; }

        // P5 ⑥ banner band drawn behind the transient announcement text: translucent indigo plate with a
        // thin gold line (procedural SDF skin), sized to the text each announcement. Fades with the text.
        // 2026-07-25 user feedback: the band left too much side padding — tightened to hug the text.
        // 2026-07-26 user feedback: banner read too large — trimmed ~25% (height 64→48, min-width 280→220,
        // padding 24→18; the authored TurnPhase_TMP font was cut 50→38 in CardLane.prefab to match).
        private const float BannerHeight = 48f;
        private const float BannerMinWidth = 220f;
        private const float BannerHorizontalPadding = 18f;
        private static readonly Color BannerFillColor = new Color32(0x1B, 0x24, 0x38, 0x8C);
        private static readonly Color BannerBorderColor = new Color32(0xC9, 0xA2, 0x27, 0xB3);

        private TMP_Text label;
        private TMP_Text currentPhaseLabel;
        private CanvasGroup canvasGroup;
        private CanvasGroup currentPhaseCanvasGroup;
        private RectTransform bannerRect;
        private CanvasGroup bannerGroup;
        private readonly Queue<Announcement> queue = new Queue<Announcement>();
        private Coroutine playback;

        private void Awake()
        {
            // Hide immediately so the authored "New Text" placeholder never flashes before the first phase
            // transition (the dock stays enabled so its fade coroutine can always run; "disabled by default"
            // is expressed as alpha 0, not GameObject deactivation, which would stop coroutines).
            EnsureBuilt();
            if (label != null)
            {
                label.text = string.Empty;
            }

            SetCurrentPhase(null);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }
        }

        private void OnEnable()
        {
            Active = this;
        }

        private readonly struct Announcement
        {
            public Announcement(string text, Color color, float dwellSeconds = TurnPhaseDockView.DwellSeconds, float fadeSeconds = TurnPhaseDockView.FadeSeconds)
            {
                Text = text;
                Color = color;
                DwellSeconds = dwellSeconds;
                FadeSeconds = fadeSeconds;
            }

            public string Text { get; }
            public Color Color { get; }
            public float DwellSeconds { get; }
            public float FadeSeconds { get; }
        }

        public void Announce(CombatPhase previous, CombatPhase current)
        {
            EnsureBuilt();
            foreach (var announcement in BuildAnnouncements(previous, current))
            {
                queue.Enqueue(announcement);
            }

            StartPlaybackIfReady();
        }

        public void AnnounceOverallTurnStart(int turnNumber)
        {
            EnsureBuilt();
            queue.Enqueue(new Announcement($"{turnNumber}\uD134 \uC2DC\uC791", PlayerColor, OverallTurnDwellSeconds, 0f));
            StartPlaybackIfReady();
        }

        public void AnnouncePlayerTurnStart()
        {
            EnsureBuilt();
            queue.Enqueue(new Announcement("\uD50C\uB808\uC774\uC5B4 \uC774\uB3D9 \uD398\uC774\uC988", PlayerColor));
            StartPlaybackIfReady();
        }

        public void AnnounceGameStart()
        {
            EnsureBuilt();
            queue.Enqueue(new Announcement("\uAC8C\uC784 \uC2DC\uC791", PlayerColor, 1.2f, 0.4f));
            StartPlaybackIfReady();
        }

        public void SetCurrentPhase(CombatPhase? phase)
        {
            EnsureBuilt();
            if (currentPhaseLabel == null)
            {
                return;
            }

            currentPhaseLabel.text = phase.HasValue
                ? CurrentPhaseLabel(phase.Value)
                : "\uD398\uC774\uC988 \uB300\uAE30";
            currentPhaseLabel.color = phase.HasValue ? PhaseColor(phase.Value) : NeutralColor;
            if (applyGeneratedStyle)
            {
                currentPhaseLabel.fontSize = CurrentPhaseFontSize;
            }
            currentPhaseLabel.raycastTarget = false;
            KoreanFontProvider.Apply(currentPhaseLabel);
            // CurTurnPhase keeps its authored position; only the transient TurnPhase_TMP announcement is
            // re-centred onto the screen centre (see PlayQueue).
        }

        // Overrides the label's horizontal canvas-local position to the screen centre while keeping its
        // authored vertical position. Works regardless of the dock's local origin or parent type.
        // (Previously centred over the play area \u2014 canvas minus the left sidebar \u2014 which read as
        // off-centre; user feedback 2026-07-25 asked for true screen centring.)
        private void CenterHorizontallyOnCanvas(RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            var canvas = target.GetComponentInParent<Canvas>();
            var canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            if (canvasRect == null || Mathf.Approximately(canvasRect.lossyScale.x, 0f))
            {
                return;
            }

            // Canvas RectTransform is centre-pivoted (standard for an overlay/camera canvas), so local x 0 is
            // the screen centre.
            var local = canvasRect.InverseTransformPoint(target.position);
            local.x = 0f;
            target.position = canvasRect.TransformPoint(local);
        }

        private void StartPlaybackIfReady()
        {
            if (playback == null && isActiveAndEnabled && queue.Count > 0)
            {
                playback = StartCoroutine(PlayQueue());
            }
        }

        // Announce each major phase boundary. Victory/Defeat are surfaced by the result UI, not here.
        private static IEnumerable<Announcement> BuildAnnouncements(CombatPhase previous, CombatPhase current)
        {
            switch (current)
            {
                case CombatPhase.PlayerMovement:
                    yield return new Announcement("\uD50C\uB808\uC774\uC5B4 \uC774\uB3D9 \uD398\uC774\uC988", PlayerColor);
                    break;
                case CombatPhase.MonsterMovement:
                    yield return new Announcement("\uBAAC\uC2A4\uD130 \uC774\uB3D9 \uD398\uC774\uC988", MonsterColor);
                    break;
                case CombatPhase.PlayerAction:
                    yield return new Announcement("\uD50C\uB808\uC774\uC5B4 \uC561\uC158 \uD398\uC774\uC988", PlayerColor);
                    break;
                case CombatPhase.MonsterAction:
                    yield return new Announcement("\uBAAC\uC2A4\uD130 \uC561\uC158 \uD398\uC774\uC988", MonsterColor);
                    break;
            }
        }

        private IEnumerator PlayQueue()
        {
            if (canvasGroup == null)
            {
                playback = null;
                yield break;
            }

            while (queue.Count > 0)
            {
                var announcement = queue.Dequeue();
                if (label != null)
                {
                    label.gameObject.SetActive(true);
                    label.enabled = true;
                    label.text = announcement.Text;
                    label.color = WithAlpha(announcement.Color, 1f);
                    // Force the code-driven font size before measuring: FitBannerToLabel reads preferredWidth,
                    // so the size must be set first. Disable autosizing so the value is honoured verbatim.
                    label.enableAutoSizing = false;
                    label.fontSize = AnnouncementFontSize;
                    CenterHorizontallyOnCanvas(label.rectTransform);
                    FitBannerToLabel();
                }

                // Full dock (panel + text) visible, hold for the dwell, then fade out. WaitForSecondsRealtime
                // holds on wall-clock so the dwell can't be skipped by a single long frame (activation /
                // domain-reload stall) and is honored even mid-hitstop (Time.timeScale near 0).
                SetAnnouncementAlpha(1f);
                yield return new WaitForSecondsRealtime(announcement.DwellSeconds);

                if (announcement.FadeSeconds > 0f)
                {
                    var faded = 0f;
                    while (faded < announcement.FadeSeconds)
                    {
                        faded += Time.unscaledDeltaTime;
                        SetAnnouncementAlpha(Mathf.Clamp01(1f - faded / announcement.FadeSeconds));
                        yield return null;
                    }
                }

                SetAnnouncementAlpha(0f);

            }

            if (label != null)
            {
                label.text = string.Empty;
            }

            playback = null;
        }

        private void EnsureBuilt()
        {
            if (label != null && canvasGroup != null && (currentPhaseLabel == null || currentPhaseCanvasGroup != null))
            {
                return;
            }

            currentPhaseLabel = currentPhaseLabel != null
                ? currentPhaseLabel
                : FindChildText(CurrentPhaseTextName);
            if (currentPhaseLabel != null)
            {
                currentPhaseLabel.textWrappingMode = TextWrappingModes.NoWrap;
                if (applyGeneratedStyle)
                {
                    currentPhaseLabel.fontSize = CurrentPhaseFontSize;
                }
                currentPhaseLabel.raycastTarget = false;
                KoreanFontProvider.Apply(currentPhaseLabel);

                // The transient phase announcement fades the TurnPhaseDock root with a CanvasGroup.
                // CurTurnPhase is a persistent readout and must remain visible between announcements.
                currentPhaseCanvasGroup = EnsureCanvasGroup(currentPhaseLabel.gameObject, currentPhaseCanvasGroup);
                currentPhaseCanvasGroup.alpha = showCurrentPhaseReadout ? 1f : 0f;
                currentPhaseCanvasGroup.interactable = false;
                currentPhaseCanvasGroup.blocksRaycasts = false;
                currentPhaseCanvasGroup.ignoreParentGroups = true;
            }

            label = label != null ? label : FindChildText(AnnouncementTextName);
            if (label == null)
            {
                label = FindFirstAnnouncementText();
            }

            if (label == null)
            {
                var go = new GameObject("TurnPhaseText", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rect = go.GetComponent<RectTransform>();
                // Fixed, centred box on the dock's position. Avoids depending on the parent being a
                // RectTransform (the authored TurnPhaseDock is a plain Transform container).
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localPosition = Vector3.zero;
                rect.sizeDelta = new Vector2(600f, 90f);

                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontSize = 36f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.raycastTarget = false;
                var font = KoreanFontProvider.Load();
                if (font != null)
                {
                    tmp.font = font;
                }

                label = tmp;
            }

            // The authored TurnPhase_TMP carries large asymmetric negative margins that skew its centred
            // line ~150px right of the rect centre (so "screen-centred" text landed visibly off-centre).
            // The announcement is fully code-driven, so normalise the margins. Vertical alignment is forced
            // to Middle for the same reason (authored Top made the text ride high inside the banner band).
            label.margin = Vector4.zero;
            label.verticalAlignment = VerticalAlignmentOptions.Middle;

            // Keep the dock root visible for CurTurnPhase. Fade only the transient announcement label
            // so the always-on current-phase readout does not disappear between phase changes.
            var rootCanvasGroup = GetComponent<CanvasGroup>();
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 1f;
                rootCanvasGroup.interactable = false;
                rootCanvasGroup.blocksRaycasts = false;
            }

            canvasGroup = EnsureCanvasGroup(label.gameObject, canvasGroup);
            EnsureBanner();
            HideAuthoredBackdrop();
            SetAnnouncementAlpha(0f);
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        // P5 ⑥: the authored flat TurnPhasePanel plate never faded with the announcement, so it read as a
        // permanently visible navy box next to the (centred) text. The procedural banner band replaces its
        // backdrop role; keep the object but stop it rendering (reversible authoring-side).
        private void HideAuthoredBackdrop()
        {
            var backdrop = transform.Find("TurnPhasePanel");
            var image = backdrop != null ? backdrop.GetComponent<UnityEngine.UI.Image>() : null;
            if (image != null)
            {
                image.enabled = false;
            }
        }

        // Builds the banner band as a sibling *behind* the announcement label (UI children render in
        // sibling order, so a child of the label would draw over the text). It carries its own CanvasGroup
        // because it does not live under the label's fading group.
        private void EnsureBanner()
        {
            if (bannerRect != null || label == null)
            {
                return;
            }

            var go = new GameObject("TurnPhaseBanner", typeof(RectTransform), typeof(CanvasGroup),
                typeof(UnityEngine.UI.Image));
            bannerRect = go.GetComponent<RectTransform>();
            bannerRect.SetParent(label.rectTransform.parent, false);
            bannerRect.SetSiblingIndex(Mathf.Max(0, label.rectTransform.GetSiblingIndex()));
            bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
            bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.sizeDelta = new Vector2(BannerMinWidth, BannerHeight);

            bannerGroup = go.GetComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.interactable = false;
            bannerGroup.blocksRaycasts = false;

            var image = go.GetComponent<UnityEngine.UI.Image>();
            image.raycastTarget = false;
            var skin = go.AddComponent<UiProceduralPanel>();
            skin.Configure(BannerFillColor, BannerBorderColor, 1.5f, 14f);
        }

        // Sizes the band to the current announcement text (with a minimum) and pins it onto the label's
        // (already screen-centred) rect centre, so both stay aligned regardless of the authored dock origin.
        private void FitBannerToLabel()
        {
            if (label == null)
            {
                return;
            }

            label.ForceMeshUpdate();
            // Widen the label rect to its text: the authored 300px rect overflows at the announcement font
            // size, and TMP renders the overflowing line skewed off the rect centre — which also made the
            // "screen-centred" text land visibly right of centre. Centre pivot keeps the growth symmetric.
            var labelSize = label.rectTransform.sizeDelta;
            if (labelSize.x < label.preferredWidth + 8f)
            {
                labelSize.x = label.preferredWidth + 8f;
                label.rectTransform.sizeDelta = labelSize;
                label.ForceMeshUpdate();
            }

            if (bannerRect == null)
            {
                return;
            }

            var width = Mathf.Max(BannerMinWidth, label.preferredWidth + BannerHorizontalPadding * 2f);
            bannerRect.sizeDelta = new Vector2(width, BannerHeight);
            // Pin onto the label rect's visual centre (via corners) rather than its position: the authored
            // label's pivot may be off-centre, and position is the pivot point.
            var corners = new Vector3[4];
            label.rectTransform.GetWorldCorners(corners);
            bannerRect.position = (corners[0] + corners[2]) * 0.5f;
        }

        private void SetAnnouncementAlpha(float alpha)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
            }

            if (bannerGroup != null)
            {
                bannerGroup.alpha = alpha;
            }

            if (label != null)
            {
                label.color = WithAlpha(label.color, alpha);
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static CanvasGroup EnsureCanvasGroup(GameObject target, CanvasGroup existing)
        {
            if (existing != null)
            {
                return existing;
            }

            var group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = target.AddComponent<CanvasGroup>();
            }

            return group;
        }

        private TMP_Text FindChildText(string textObjectName)
        {
            var texts = GetComponentsInChildren<TMP_Text>(true);
            foreach (var text in texts)
            {
                if (text != null && string.Equals(text.name, textObjectName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return text;
                }
            }

            return null;
        }

        private TMP_Text FindFirstAnnouncementText()
        {
            var texts = GetComponentsInChildren<TMP_Text>(true);
            foreach (var text in texts)
            {
                if (text != null && text != currentPhaseLabel)
                {
                    return text;
                }
            }

            return null;
        }

        private static string CurrentPhaseLabel(CombatPhase phase)
        {
            switch (phase)
            {
                case CombatPhase.PlayerMovement: return "\uD50C\uB808\uC774\uC5B4 \uC774\uB3D9 \uD398\uC774\uC988";
                case CombatPhase.MonsterMovement: return "\uBAAC\uC2A4\uD130 \uC774\uB3D9 \uD398\uC774\uC988";
                case CombatPhase.PlayerAction: return "\uD50C\uB808\uC774\uC5B4 \uC561\uC158 \uD398\uC774\uC988";
                case CombatPhase.MonsterAction: return "\uBAAC\uC2A4\uD130 \uC561\uC158 \uD398\uC774\uC988";
                case CombatPhase.Victory: return "\uC2B9\uB9AC";
                case CombatPhase.Defeat: return "\uD328\uBC30";
                default: return $"{phase} \uD398\uC774\uC988";
            }
        }

        private static Color PhaseColor(CombatPhase phase)
        {
            switch (phase)
            {
                case CombatPhase.PlayerMovement:
                case CombatPhase.PlayerAction:
                    return PlayerColor;
                case CombatPhase.MonsterMovement:
                case CombatPhase.MonsterAction:
                    return MonsterColor;
                default:
                    return NeutralColor;
            }
        }

        private void OnDisable()
        {
            // Reset cleanly if disabled mid-playback (e.g. scene teardown) so a later enable restarts fresh.
            playback = null;
            queue.Clear();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            if (bannerGroup != null)
            {
                bannerGroup.alpha = 0f;
            }

            if (Active == this)
            {
                Active = null;
            }
        }
    }
}

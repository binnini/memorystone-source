using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity.Tutorial
{
    /// <summary>
    /// Tutorial presentation layer: a compact text panel that sits beside the step's focus target, a full-screen
    /// spotlight dim with a hole over that target (<see cref="TutorialSpotlightView"/>), a thin ring for a
    /// secondary "callout" target the text merely mentions, and a keycap cue for key steps
    /// (<see cref="TutorialKeyCueView"/>). Everything is built at runtime under the tutorial canvas by
    /// <see cref="Build"/>; no scene or prefab authoring.
    ///
    /// Positions are driven every frame by <see cref="UpdateLayout"/> from screen-space focus rects the host
    /// resolves (world tiles and monsters move with the camera, so the hole and panel must follow). The panel
    /// placement rule: the side of the focus with the most room (above → right → left → below), clamped to the
    /// screen, unless the step pins a placement. With no focus the panel takes the top-centre slot (or the
    /// centre for focus-less key beats) and the whole screen is dimmed.
    /// </summary>
    public sealed class TutorialHudView : MonoBehaviour
    {
        private const string PanelMaterialResourcePath = "UI/Panel/UI_ProceduralPanel";
        private const string GlowMaterialResourcePath = "UI/Glow/UI_ProceduralGlow";

        private const float PanelWidth = 700f;
        private const float PanelPadX = 22f;
        private const float PanelPadY = 16f;
        private const float BodyFontSize = 26f;
        private const float SpeakerFontSize = 22f;
        private const float SpeakerGap = 4f;
        private const float PlacementGap = 26f;
        private const float ScreenMargin = 24f;
        // Left strip reserved for the gameplay sidebar (canvas units at the 2200x1238 reference).
        private const float SidebarReserve = 160f;
        private const float TopSlotInset = 60f;
        private const float CenterSlotLift = 60f;
        private const float HolePad = 14f;
        private const float CalloutPad = 16f;
        private const float TailSize = 18f;
        private const float KeyCueGap = 24f;
        private const float PanelLerpSpeed = 14f;

        private static readonly Color PanelFill = new Color(0.109804f, 0.133333f, 0.352941f, 0.96f);
        private static readonly Color PanelBorder = new Color(0.788f, 0.604f, 0.18f, 0.9f);
        private static readonly Color CalloutColor = new Color(1f, 0.77f, 0.18f, 0.9f);

        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int BorderColorId = Shader.PropertyToID("_BorderColor");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int BorderThicknessId = Shader.PropertyToID("_BorderThickness");
        private static readonly int GlowColorId = Shader.PropertyToID("_Color");
        private static readonly int GlowPadId = Shader.PropertyToID("_Pad");
        private static readonly int GlowThicknessId = Shader.PropertyToID("_Thickness");

        [SerializeField] private CanvasGroup root;
        [SerializeField] private TMP_Text speakerText;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private TMP_Text feedbackText;
        [SerializeField] private float feedbackSeconds = 1.5f;

        private RectTransform canvasRect;
        private TutorialSpotlightView spotlight;
        private RectTransform calloutRect;
        private Image calloutImage;
        private Material calloutMaterial;
        private RectTransform panelRect;
        private Image panelImage;
        private Material panelMaterial;
        private RectTransform tailRect;
        private TutorialKeyCueView keyCue;
        private Image stepImage;
        private RectTransform continueMarker;
        // The "click to continue" arrow sits outside the panel's right edge (it used to be a small 22px glyph
        // tucked inside the bottom-right corner, where it read as overlapping the text).
        private const float ContinueMarkerSize = 44f;
        private const float ContinueMarkerGap = 14f;
        private const float ContinueMarkerBob = 6f;
        private const string ArrowSpriteResourcePath = "UI/Icons/ui_arrow";
        private const float ImageMaxHeight = 260f;
        private const float ImageGap = 12f;

        private TutorialStep currentStep;
        private float feedbackHideAt;
        private bool built;
        private bool panelPlacedOnce;
        private bool warnedNotBuilt;
        private Camera canvasCamera;
        // Last resolved focus, kept for a short grace window when the resolver briefly returns null (target
        // rebuilt this frame, hex momentarily behind the camera) so the hole and panel do not pop.
        private Rect? lastFocus;
        private float lastFocusTime = float.NegativeInfinity;
        private float stepShownTime;
        private const float FocusGraceSeconds = 0.3f;

        // Everything this view draws lives under this transform; the director treats clicks on it as its own.
        public Transform HudRoot => canvasRect != null ? canvasRect : transform;

        private void Awake()
        {
            AutoBindRoot();
            HideFeedback();
        }

        private void Update()
        {
            if (feedbackText != null && feedbackText.gameObject.activeSelf && Time.unscaledTime >= feedbackHideAt)
            {
                HideFeedback();
            }
        }

        /// <summary>Creates the spotlight, panel, callout ring, feedback label and key cue under <paramref name="canvas"/>.</summary>
        public void Build(RectTransform canvas, Action<TMP_Text> fontApplier)
        {
            canvasRect = canvas;
            var canvasComponent = canvas != null ? canvas.GetComponentInParent<Canvas>() : null;
            canvasCamera = canvasComponent != null && canvasComponent.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvasComponent.rootCanvas.worldCamera
                : null;
            AutoBindRoot();

            // Draw order: spotlight (bottom) → callout ring → tail → panel (top). Later siblings render on top.
            spotlight = TutorialSpotlightView.Build(canvas);

            var calloutGo = new GameObject("TutorialCallout", typeof(RectTransform), typeof(Image));
            calloutGo.transform.SetParent(canvas, false);
            calloutRect = (RectTransform)calloutGo.transform;
            calloutRect.anchorMin = calloutRect.anchorMax = new Vector2(0.5f, 0.5f);
            calloutRect.pivot = new Vector2(0.5f, 0.5f);
            calloutImage = calloutGo.GetComponent<Image>();
            calloutImage.sprite = null;
            calloutImage.raycastTarget = false;
            calloutImage.maskable = false;
            var glowBase = Resources.Load<Material>(GlowMaterialResourcePath);
            calloutMaterial = glowBase != null ? new Material(glowBase) : null;
            if (calloutMaterial != null)
            {
                calloutMaterial.SetColor(GlowColorId, CalloutColor);
                calloutMaterial.SetFloat(GlowPadId, CalloutPad);
                calloutMaterial.SetFloat(GlowThicknessId, 4f);
                calloutImage.material = calloutMaterial;
                calloutImage.color = Color.white;
            }
            else
            {
                calloutImage.color = new Color(CalloutColor.r, CalloutColor.g, CalloutColor.b, 0.25f);
            }
            calloutGo.SetActive(false);

            var tailGo = new GameObject("TutorialPanelTail", typeof(RectTransform), typeof(Image));
            tailGo.transform.SetParent(canvas, false);
            tailRect = (RectTransform)tailGo.transform;
            tailRect.anchorMin = tailRect.anchorMax = new Vector2(0.5f, 0.5f);
            tailRect.pivot = new Vector2(0.5f, 0.5f);
            tailRect.sizeDelta = new Vector2(TailSize, TailSize);
            tailRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var tailImage = tailGo.GetComponent<Image>();
            tailImage.color = PanelFill;
            tailImage.raycastTarget = false;
            tailGo.SetActive(false);

            var panelGo = new GameObject("TutorialPanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvas, false);
            panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(PanelWidth, 80f);
            panelImage = panelGo.GetComponent<Image>();
            panelImage.sprite = null;
            panelImage.raycastTarget = true;
            var panelBase = Resources.Load<Material>(PanelMaterialResourcePath);
            panelMaterial = panelBase != null ? new Material(panelBase) : null;
            if (panelMaterial != null)
            {
                panelMaterial.SetColor(FillColorId, PanelFill);
                panelMaterial.SetColor(BorderColorId, PanelBorder);
                panelMaterial.SetFloat(RadiusId, 12f);
                panelMaterial.SetFloat(BorderThicknessId, 1.5f);
                panelImage.material = panelMaterial;
                panelImage.color = Color.white;
            }
            else
            {
                panelImage.color = PanelFill;
            }

            var imageGo = new GameObject("TutorialImage", typeof(RectTransform), typeof(Image));
            imageGo.transform.SetParent(panelRect, false);
            var imageRect = (RectTransform)imageGo.transform;
            imageRect.anchorMin = imageRect.anchorMax = new Vector2(0f, 1f);
            imageRect.pivot = new Vector2(0f, 1f);
            stepImage = imageGo.GetComponent<Image>();
            stepImage.preserveAspect = true;
            stepImage.raycastTarget = false;
            imageGo.SetActive(false);

            speakerText = CreateText(panelRect, "Speaker", SpeakerFontSize, FontStyles.Bold, new Color(1f, 0.88f, 0.45f, 1f), fontApplier);
            bodyText = CreateText(panelRect, "Body", BodyFontSize, FontStyles.Normal, Color.white, fontApplier);

            feedbackText = CreateText(panelRect, "TutorialFeedback", 24f, FontStyles.Bold, new Color(1f, 0.82f, 0.42f, 1f), fontApplier);
            var feedbackRect = (RectTransform)feedbackText.transform;
            feedbackRect.anchorMin = new Vector2(0.5f, 0f);
            feedbackRect.anchorMax = new Vector2(0.5f, 0f);
            feedbackRect.pivot = new Vector2(0.5f, 1f);
            feedbackRect.anchoredPosition = new Vector2(0f, -12f);
            feedbackRect.sizeDelta = new Vector2(PanelWidth, 40f);
            feedbackText.alignment = TextAlignmentOptions.Center;

            // "You can continue now" marker: a right-pointing arrow just outside the panel's right edge, level
            // with its bottom, that appears once the post-transition input lock releases on narration steps
            // and bobs sideways to invite the click.
            var markerGo = new GameObject("TutorialContinueMarker", typeof(RectTransform), typeof(Image));
            markerGo.transform.SetParent(panelRect, false);
            continueMarker = (RectTransform)markerGo.transform;
            continueMarker.anchorMin = continueMarker.anchorMax = new Vector2(1f, 0f);
            continueMarker.pivot = new Vector2(0f, 0f);
            continueMarker.sizeDelta = new Vector2(ContinueMarkerSize, ContinueMarkerSize);
            continueMarker.anchoredPosition = new Vector2(ContinueMarkerGap, 0f);
            var markerImage = markerGo.GetComponent<Image>();
            markerImage.sprite = Resources.Load<Sprite>(ArrowSpriteResourcePath);
            markerImage.preserveAspect = true;
            markerImage.raycastTarget = false;
            markerImage.enabled = markerImage.sprite != null;
            markerGo.SetActive(false);

            keyCue = TutorialKeyCueView.Build(panelRect, fontApplier);
            var cueRect = keyCue.RectTransform;
            cueRect.anchorMin = cueRect.anchorMax = new Vector2(1f, 0.5f);
            cueRect.pivot = new Vector2(0f, 0.5f);
            // The cue's rect includes the arrow above the cap; lift it by half that so the cap itself sits
            // level with the panel's vertical centre.
            cueRect.anchoredPosition = new Vector2(KeyCueGap, 32f);

            built = true;
            HideFeedback();
            SetVisible(false);
        }

        public void ShowStep(TutorialStep step)
        {
            AutoBindRoot();
            if (!built && !warnedNotBuilt)
            {
                warnedNotBuilt = true;
                Debug.LogWarning("TutorialHudView.ShowStep before Build(): the spotlight, panel and key cue do not exist, so the step will render nothing.", this);
            }

            currentStep = step;
            stepShownTime = Time.unscaledTime;
            lastFocus = null;
            lastFocusTime = float.NegativeInfinity;
            SetVisible(true);

            var speaker = step?.Speaker ?? string.Empty;
            var hasSpeaker = !string.IsNullOrWhiteSpace(speaker);
            if (speakerText != null)
            {
                speakerText.text = speaker;
                speakerText.gameObject.SetActive(hasSpeaker);
            }

            if (bodyText != null)
            {
                bodyText.text = FormatBody(step?.BodyText);
            }

            LayoutPanelContent(hasSpeaker, step?.Image);

            keyCue?.Show(step != null ? step.CueKeys : null);
            SetContinueReady(false);
            panelPlacedOnce = false;
        }

        public void Hide()
        {
            currentStep = null;
            SetVisible(false);
            HideFeedback();
            keyCue?.Hide();
            SetContinueReady(false);
            spotlight?.Hide();
            if (calloutRect != null)
            {
                calloutRect.gameObject.SetActive(false);
            }
            if (tailRect != null)
            {
                tailRect.gameObject.SetActive(false);
            }
        }

        public void ShowFeedback(string message)
        {
            AutoBindRoot();
            if (feedbackText == null)
            {
                return;
            }

            feedbackText.text = string.IsNullOrWhiteSpace(message) ? "튜토리얼의 지시에 따라주세요." : message;
            feedbackText.gameObject.SetActive(true);
            feedbackHideAt = Time.unscaledTime + Mathf.Max(0.1f, feedbackSeconds);
        }

        public void PlayKeyCuePressed()
        {
            keyCue?.PlayPressed();
        }

        public void SetContinueReady(bool ready)
        {
            if (continueMarker == null)
            {
                return;
            }

            if (continueMarker.gameObject.activeSelf != ready)
            {
                continueMarker.gameObject.SetActive(ready);
            }

            if (ready)
            {
                var bob = (1f - Mathf.Cos(Time.unscaledTime * 2f * Mathf.PI * 1.4f)) * 0.5f * ContinueMarkerBob;
                continueMarker.anchoredPosition = new Vector2(ContinueMarkerGap + bob, 0f);
            }
        }

        /// <summary>
        /// Per-frame placement. Rects are screen pixels (as from Camera.WorldToScreenPoint / RectTransformUtility);
        /// null focus = no hole, whole screen dimmed. <paramref name="spotlightSuppressed"/> hides the dim while the
        /// host pans the camera to a called-out hex, so the thing being pointed at is not darkened.
        /// </summary>
        public void UpdateLayout(TutorialFocus? focusTarget, TutorialFocus? calloutTarget, bool spotlightSuppressed)
        {
            if (!built || currentStep == null || canvasRect == null)
            {
                return;
            }

            var focus = ToCanvasRect(focusTarget.HasValue ? focusTarget.Value.ScreenRect : (Rect?)null);
            var callout = ToCanvasRect(calloutTarget.HasValue ? calloutTarget.Value.ScreenRect : (Rect?)null);
            var shape = focusTarget.HasValue ? focusTarget.Value.Shape : TutorialFocusShape.Rect;
            var strength = focusTarget.HasValue ? focusTarget.Value.DimStrength : 1f;
            var targetKey = focusTarget.HasValue ? focusTarget.Value.TargetKey : null;
            var blocksInput = !focusTarget.HasValue || focusTarget.Value.BlocksInput;

            var now = Time.unscaledTime;
            if (focus.HasValue)
            {
                lastFocus = focus;
                lastFocusTime = now;
            }
            else if (currentStep.Highlight != null && currentStep.Highlight.IsSet)
            {
                if (lastFocus.HasValue && now - lastFocusTime <= FocusGraceSeconds)
                {
                    focus = lastFocus; // transient miss: hold the last good rect
                }
                else if (!lastFocus.HasValue && now - stepShownTime <= FocusGraceSeconds)
                {
                    return; // target not resolved yet this step: don't place the panel at the top and fly it later
                }
            }

            var highlightSet = currentStep.Highlight != null && currentStep.Highlight.IsSet;
            if (spotlightSuppressed || (highlightSet && !focus.HasValue))
            {
                // Camera pan, or a target the host could not place on screen (popup not open yet, target
                // rebuilt): never leave a hole-less dim over a step that needs an action — that would
                // swallow the very click the step is waiting for.
                spotlight?.Hide();
            }
            else
            {
                var hole = focus.HasValue && shape != TutorialFocusShape.NoHole
                    ? Pad(focus.Value, shape == TutorialFocusShape.Ellipse ? HolePad * 1.5f : HolePad)
                    : (Rect?)null;
                spotlight?.Show(hole, shape, strength, targetKey, blocksInput);
            }

            if (calloutRect != null)
            {
                if (callout.HasValue)
                {
                    var r = Pad(callout.Value, CalloutPad);
                    calloutRect.anchoredPosition = r.center;
                    calloutRect.sizeDelta = r.size;
                    calloutMaterial?.SetVector(RectSizeId, new Vector4(r.width, r.height, 0f, 0f));
                    calloutRect.gameObject.SetActive(true);
                }
                else
                {
                    calloutRect.gameObject.SetActive(false);
                }
            }

            PlacePanel(focus, currentStep.PanelPlacement, currentStep.PanelOffset);
        }

        // Typesets the authored text: the first line is the instruction (white, full size), every following
        // line is supporting detail (smaller, muted) with a little air between them — so a two-line step reads
        // as "do this / because", not as one grey block. Authoring stays plain text with '\n'.
        private static string FormatBody(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return string.Empty;
            }

            var lines = body.Replace("\r", string.Empty).Split('\n');
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (i == 0)
                {
                    sb.Append("<line-height=120%>").Append(line);
                }
                else
                {
                    sb.Append("\n<size=88%><color=#C3CBE3>").Append(line).Append("</color></size>");
                }
            }

            return sb.ToString();
        }

        // ---- layout ---------------------------------------------------------------------------------------

        private void LayoutPanelContent(bool hasSpeaker, Sprite image)
        {
            if (panelRect == null || bodyText == null)
            {
                return;
            }

            var innerWidth = PanelWidth - PanelPadX * 2f;

            // Optional picture (e.g. the card-type chart) sits above the text, scaled to the inner width.
            var imageHeight = 0f;
            if (stepImage != null)
            {
                if (image != null)
                {
                    var aspect = image.rect.width > 0f ? image.rect.height / image.rect.width : 0.5f;
                    imageHeight = Mathf.Min(ImageMaxHeight, Mathf.Ceil(innerWidth * aspect));
                    stepImage.sprite = image;
                    var ir = (RectTransform)stepImage.transform;
                    ir.anchoredPosition = new Vector2(PanelPadX, -PanelPadY);
                    ir.sizeDelta = new Vector2(innerWidth, imageHeight);
                    stepImage.gameObject.SetActive(true);
                    imageHeight += ImageGap;
                }
                else
                {
                    stepImage.gameObject.SetActive(false);
                }
            }

            var bodyHeight = Mathf.Ceil(bodyText.GetPreferredValues(bodyText.text, innerWidth, 0f).y);
            var speakerHeight = hasSpeaker && speakerText != null ? Mathf.Ceil(speakerText.GetPreferredValues(speakerText.text, innerWidth, 0f).y) + SpeakerGap : 0f;
            var height = PanelPadY * 2f + imageHeight + speakerHeight + Mathf.Max(bodyHeight, BodyFontSize * 1.2f);
            panelRect.sizeDelta = new Vector2(PanelWidth, height);
            panelMaterial?.SetVector(RectSizeId, new Vector4(PanelWidth, height, 0f, 0f));

            if (hasSpeaker && speakerText != null)
            {
                var sr = (RectTransform)speakerText.transform;
                sr.anchoredPosition = new Vector2(PanelPadX, -PanelPadY - imageHeight);
                sr.sizeDelta = new Vector2(innerWidth, speakerHeight - SpeakerGap);
            }

            var br = (RectTransform)bodyText.transform;
            br.anchoredPosition = new Vector2(PanelPadX, -PanelPadY - imageHeight - speakerHeight);
            br.sizeDelta = new Vector2(innerWidth, bodyHeight);
        }

        private void PlacePanel(Rect? focus, TutorialPanelPlacement placement, Vector2 offset)
        {
            var bounds = canvasRect.rect;
            var size = panelRect.sizeDelta;
            var half = size * 0.5f;
            // A visible key cue or continue arrow hangs off the panel's right edge; keep it on screen too.
            var extraRight = keyCue != null && keyCue.IsShowing ? KeyCueGap + keyCue.RectTransform.sizeDelta.x : 0f;
            if (continueMarker != null && continueMarker.gameObject.activeSelf)
            {
                extraRight = Mathf.Max(extraRight, ContinueMarkerGap + ContinueMarkerBob + ContinueMarkerSize);
            }
            var minX = bounds.xMin + SidebarReserve + half.x;
            var maxX = bounds.xMax - ScreenMargin - half.x - extraRight;
            var minY = bounds.yMin + ScreenMargin + half.y;
            var maxY = bounds.yMax - ScreenMargin - half.y;

            Vector2 pos;
            var side = TutorialPanelPlacement.Top;
            if (placement == TutorialPanelPlacement.Center)
            {
                pos = new Vector2(0f, CenterSlotLift);
                side = TutorialPanelPlacement.Center;
            }
            else if (!focus.HasValue || placement == TutorialPanelPlacement.Top)
            {
                pos = new Vector2(0f, bounds.yMax - TopSlotInset - half.y);
            }
            else
            {
                var f = focus.Value;
                side = placement == TutorialPanelPlacement.Auto ? PickSide(f, half, minX, maxX, minY, maxY) : placement;
                pos = PositionFor(side, f, half);
            }

            pos += offset;
            pos.x = Mathf.Clamp(pos.x, minX, Mathf.Max(minX, maxX));
            pos.y = Mathf.Clamp(pos.y, minY, Mathf.Max(minY, maxY));

            if (!panelPlacedOnce)
            {
                panelRect.anchoredPosition = pos;
                panelPlacedOnce = true;
            }
            else
            {
                var t = 1f - Mathf.Exp(-PanelLerpSpeed * Time.unscaledDeltaTime);
                panelRect.anchoredPosition = Vector2.Lerp(panelRect.anchoredPosition, pos, t);
            }

            UpdateTail(focus, side);
        }

        private static TutorialPanelPlacement PickSide(Rect f, Vector2 half, float minX, float maxX, float minY, float maxY)
        {
            if (f.yMax + PlacementGap + half.y * 2f <= maxY + half.y) return TutorialPanelPlacement.Above;
            if (f.xMax + PlacementGap + half.x * 2f <= maxX + half.x) return TutorialPanelPlacement.Right;
            if (f.xMin - PlacementGap - half.x * 2f >= minX - half.x) return TutorialPanelPlacement.Left;
            if (f.yMin - PlacementGap - half.y * 2f >= minY - half.y) return TutorialPanelPlacement.Below;
            return TutorialPanelPlacement.Above;
        }

        private static Vector2 PositionFor(TutorialPanelPlacement side, Rect f, Vector2 half)
        {
            switch (side)
            {
                case TutorialPanelPlacement.Above: return new Vector2(f.center.x, f.yMax + PlacementGap + half.y);
                case TutorialPanelPlacement.Below: return new Vector2(f.center.x, f.yMin - PlacementGap - half.y);
                case TutorialPanelPlacement.Left: return new Vector2(f.xMin - PlacementGap - half.x, f.center.y);
                case TutorialPanelPlacement.Right: return new Vector2(f.xMax + PlacementGap + half.x, f.center.y);
                default: return new Vector2(f.center.x, f.yMax + PlacementGap + half.y);
            }
        }

        private void UpdateTail(Rect? focus, TutorialPanelPlacement side)
        {
            if (tailRect == null)
            {
                return;
            }

            var showTail = focus.HasValue
                && side != TutorialPanelPlacement.Top
                && side != TutorialPanelPlacement.Center;
            if (!showTail)
            {
                tailRect.gameObject.SetActive(false);
                return;
            }

            var f = focus.Value;
            var p = panelRect.anchoredPosition;
            var half = panelRect.sizeDelta * 0.5f;
            const float inset = 24f;
            Vector2 tail;
            switch (side)
            {
                case TutorialPanelPlacement.Below:
                    tail = new Vector2(Mathf.Clamp(f.center.x, p.x - half.x + inset, p.x + half.x - inset), p.y + half.y);
                    break;
                case TutorialPanelPlacement.Left:
                    tail = new Vector2(p.x + half.x, Mathf.Clamp(f.center.y, p.y - half.y + inset, p.y + half.y - inset));
                    break;
                case TutorialPanelPlacement.Right:
                    tail = new Vector2(p.x - half.x, Mathf.Clamp(f.center.y, p.y - half.y + inset, p.y + half.y - inset));
                    break;
                default: // Above
                    tail = new Vector2(Mathf.Clamp(f.center.x, p.x - half.x + inset, p.x + half.x - inset), p.y - half.y);
                    break;
            }

            tailRect.anchoredPosition = tail;
            tailRect.gameObject.SetActive(true);
        }

        private Rect? ToCanvasRect(Rect? screenRect)
        {
            if (!screenRect.HasValue || canvasRect == null)
            {
                return null;
            }

            var r = screenRect.Value;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, r.min, canvasCamera, out var min)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, r.max, canvasCamera, out var max))
            {
                return null;
            }

            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        private static Rect Pad(Rect r, float pad)
        {
            return Rect.MinMaxRect(r.xMin - pad, r.yMin - pad, r.xMax + pad, r.yMax + pad);
        }

        // ---- plumbing -------------------------------------------------------------------------------------

        private void HideFeedback()
        {
            if (feedbackText != null)
            {
                feedbackText.gameObject.SetActive(false);
            }
        }

        private void SetVisible(bool visible)
        {
            if (root == null)
            {
                gameObject.SetActive(visible);
                return;
            }

            root.alpha = visible ? 1f : 0f;
            root.blocksRaycasts = visible;
            root.interactable = visible;
        }

        private void AutoBindRoot()
        {
            root = root != null ? root : GetComponent<CanvasGroup>();
            if (root == null)
            {
                root = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private static TMP_Text CreateText(Transform parent, string name, float fontSize, FontStyles style, Color color, Action<TMP_Text> fontApplier)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            var label = textObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = color;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            fontApplier?.Invoke(label);
            return label;
        }

        private void OnDestroy()
        {
            if (panelMaterial != null)
            {
                Destroy(panelMaterial);
            }
            if (calloutMaterial != null)
            {
                Destroy(calloutMaterial);
            }
        }
    }
}

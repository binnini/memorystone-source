using SeoulPlayup.Combat.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Animated monster health bar on the nameplate. Two update sources converge on the same
    /// absolute value: the bulk marker refresh (<see cref="SetHealth"/> from
    /// CombatActorMarkerPresenter.ApplyHealthBar) and, crucially, the floating damage/heal number —
    /// <see cref="EffectPresentationController.FloatingTextPresented"/> fires at the exact frame a
    /// number spawns and the event carries the target's post-hit HP (EffectResultEvent.CurrentValue,
    /// stamped by CombatState.RaiseEffect), so the bar drains in sync with each hit instead of
    /// waiting for the next marker rebuild, and a lethal hit visibly reaches 0 before the death
    /// animation. The displayed fill eases toward the target while a pale "ghost" segment lingers
    /// briefly and then drains after it, MapleStory/fighting-game style.
    ///
    /// Visuals prefer the procedural "UI/MonsterHealthBar" shader (single quad on the track Image,
    /// legacy fill Image hidden); when the shader is unavailable (e.g. stripped from a build) the
    /// view falls back to animating the legacy fill RectTransform/Image so behaviour degrades to the
    /// previous flat look, still animated.
    /// </summary>
    public sealed class MonsterHealthBarView : MonoBehaviour
    {
        private const string BarShaderName = "UI/MonsterHealthBar";
        private const float FillSmoothTime = 0.09f;
        private const float GhostHoldSeconds = 0.35f;
        private const float GhostDrainPerSecond = 1.4f;
        private const float LowHpPulseThreshold = 0.25f;

        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int FillRatioId = Shader.PropertyToID("_FillRatio");
        private static readonly int GhostRatioId = Shader.PropertyToID("_GhostRatio");
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int FillColorDarkId = Shader.PropertyToID("_FillColorDark");
        private static readonly Color HighColor = new Color(0.30f, 0.97f, 0.38f, 1f);
        private static readonly Color MidColor = new Color(1f, 0.82f, 0.16f, 1f);
        private static readonly Color LowColor = new Color(0.98f, 0.23f, 0.14f, 1f);

        private Image trackImage;
        private Image legacyFillImage;
        private RectTransform legacyFillRect;
        private float barWidth = 150f;
        private float barHeight = 12f;
        private Material barMaterial;

        private string unitId = string.Empty;
        private int maxHp;
        private bool hasValue;
        private float targetRatio = 1f;
        private float displayedRatio = 1f;
        private float ghostRatio = 1f;
        private float fillVelocity;
        private float ghostHoldTimer;

        /// <summary>True when the procedural shader drives the visuals (legacy Images hidden).</summary>
        public bool UsesProceduralMaterial => barMaterial != null;

        /// <summary>
        /// Bind the bar's graphics. <paramref name="screenSpaceCanvas"/> must be true when the bar lives on a
        /// screen-space canvas (the boss HUD): Unity stamps <c>unity_GUIZTestMode</c> itself there, so the
        /// explicit override the world-space nameplate needs must be skipped rather than fought.
        /// </summary>
        public void Initialize(Image background, Image fillImage, RectTransform fillRect, float widthPx, float heightPx, bool screenSpaceCanvas = false)
        {
            trackImage = background;
            legacyFillImage = fillImage;
            legacyFillRect = fillRect;
            barWidth = Mathf.Max(1f, widthPx);
            barHeight = Mathf.Max(1f, heightPx);

            var shader = Shader.Find(BarShaderName);
            if (shader != null && trackImage != null)
            {
                barMaterial = new Material(shader)
                {
                    name = "Monster Health Bar (Instance)",
                    hideFlags = HideFlags.DontSave,
                };
                if (!screenSpaceCanvas)
                {
                    // World-space canvas: Unity only stamps unity_GUIZTestMode for screen-space overlay
                    // canvases, so set it explicitly (Always), matching the nameplate's other graphics.
                    barMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                }

                barMaterial.SetVector(RectSizeId, new Vector4(barWidth, barHeight, 0f, 0f));
                trackImage.material = barMaterial;
                trackImage.color = Color.white;
                if (legacyFillImage != null)
                {
                    // Rect/color keep updating for EditMode assertions; only the draw is disabled.
                    legacyFillImage.enabled = false;
                }
            }

            ApplyVisuals();
        }

        public void Bind(string ownerUnitId)
        {
            unitId = ownerUnitId ?? string.Empty;
        }

        /// <summary>Absolute health update from the bulk marker refresh. First call snaps, later calls animate.</summary>
        public void SetHealth(int hp, int hpMax)
        {
            maxHp = Mathf.Max(1, hpMax);
            var ratio = Mathf.Clamp01((float)Mathf.Max(0, hp) / maxHp);
            if (!hasValue)
            {
                hasValue = true;
                targetRatio = ratio;
                displayedRatio = ratio;
                ghostRatio = ratio;
                fillVelocity = 0f;
                ApplyVisuals();
                return;
            }

            SetTargetRatio(ratio);
        }

        /// <summary>Belt-and-braces from the death presentation: drain to zero even without an event.</summary>
        public void NotifyDead()
        {
            if (hasValue)
            {
                SetTargetRatio(0f);
            }
        }

        private void OnEnable()
        {
            EffectPresentationController.FloatingTextPresented += OnFloatingTextPresented;
        }

        private void OnDisable()
        {
            EffectPresentationController.FloatingTextPresented -= OnFloatingTextPresented;
        }

        private void OnDestroy()
        {
            if (barMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(barMaterial);
                }
                else
                {
                    DestroyImmediate(barMaterial);
                }

                barMaterial = null;
            }
        }

        private void OnFloatingTextPresented(EffectResultEvent resultEvent)
        {
            if (!hasValue || string.IsNullOrEmpty(unitId) ||
                !string.Equals(resultEvent.TargetUnitId, unitId, System.StringComparison.Ordinal))
            {
                return;
            }

            if (resultEvent.Kind != EffectKind.Damage && resultEvent.Kind != EffectKind.Heal)
            {
                return;
            }

            if (resultEvent.Lethal)
            {
                SetTargetRatio(0f);
                return;
            }

            // CurrentValue/PreviousValue both zero means the event wasn't stamped with HP telemetry
            // (e.g. field-center announce events) — leave the bar to the bulk refresh.
            if (resultEvent.CurrentValue <= 0 && resultEvent.PreviousValue <= 0)
            {
                return;
            }

            SetTargetRatio(Mathf.Clamp01((float)Mathf.Max(0, resultEvent.CurrentValue) / maxHp));
        }

        private void SetTargetRatio(float ratio)
        {
            if (ratio < targetRatio)
            {
                // New damage: ghost keeps covering the old value, then drains after a short hold.
                ghostRatio = Mathf.Max(ghostRatio, displayedRatio);
                ghostHoldTimer = GhostHoldSeconds;
            }

            targetRatio = ratio;
        }

        private void Update()
        {
            if (!hasValue)
            {
                return;
            }

            var deltaTime = Time.deltaTime;
            displayedRatio = Mathf.SmoothDamp(displayedRatio, targetRatio, ref fillVelocity, FillSmoothTime, Mathf.Infinity, deltaTime);
            if (Mathf.Abs(displayedRatio - targetRatio) < 0.0005f)
            {
                displayedRatio = targetRatio;
                fillVelocity = 0f;
            }

            if (ghostRatio < displayedRatio)
            {
                ghostRatio = displayedRatio;
            }
            else if (ghostRatio > displayedRatio)
            {
                if (ghostHoldTimer > 0f)
                {
                    ghostHoldTimer -= deltaTime;
                }
                else
                {
                    ghostRatio = Mathf.MoveTowards(ghostRatio, displayedRatio, GhostDrainPerSecond * deltaTime);
                }
            }

            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            var fillColor = ResolveFillColor(displayedRatio);
            if (barMaterial != null)
            {
                barMaterial.SetFloat(FillRatioId, displayedRatio);
                barMaterial.SetFloat(GhostRatioId, ghostRatio);
                barMaterial.SetColor(FillColorId, fillColor);
                barMaterial.SetColor(FillColorDarkId, Color.Lerp(fillColor * 0.45f, fillColor, 0.15f));
                return;
            }

            if (legacyFillRect != null)
            {
                legacyFillRect.sizeDelta = new Vector2(Mathf.Max(1f, barWidth * displayedRatio), barHeight);
            }

            if (legacyFillImage != null)
            {
                legacyFillImage.color = fillColor;
            }
        }

        private static Color ResolveFillColor(float ratio)
        {
            // Same palette as the legacy HealthColor thresholds, but blended smoothly, plus a low-HP
            // pulse so a nearly-dead monster's bar breathes.
            var color = ratio > 0.5f
                ? Color.Lerp(MidColor, HighColor, Mathf.InverseLerp(0.5f, 0.85f, ratio))
                : Color.Lerp(LowColor, MidColor, Mathf.InverseLerp(0.18f, 0.5f, ratio));
            if (ratio > 0f && ratio <= LowHpPulseThreshold && Application.isPlaying)
            {
                var pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 7f);
                color = Color.Lerp(color, Color.white, pulse * 0.22f);
            }

            return color;
        }
    }
}

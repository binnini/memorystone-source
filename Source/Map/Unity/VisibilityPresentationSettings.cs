using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// Single source of truth for the visibility ("암시야") presentation look shared by every scene that
    /// renders the atlas map (ArtLookdev, MainGameplay). These values previously lived as per-scene
    /// [SerializeField]s on each AtlasTilePresentationView instance and drifted whenever one scene was
    /// tuned in isolation; one shared asset removes that drift channel.
    ///
    /// A view with this asset assigned copies the values over its own serialized fields on Awake; a view
    /// with the slot empty keeps its inspector values. The asset is therefore optional and non-breaking
    /// (EditMode tests and legacy sandbox scenes are unaffected). Runtime toggles (e.g. the lookdev
    /// mode button) still win after Awake — the asset defines the starting look, not a per-frame lock.
    ///
    /// Field names intentionally mirror AtlasTilePresentationView's serialized fields 1:1.
    /// Default values mirror the shipped tuned look (2026-07-21), so a freshly created asset is a
    /// visual no-op.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Map/Visibility Presentation Settings", fileName = "VisibilityPresentationSettings")]
    public sealed class VisibilityPresentationSettings : ScriptableObject
    {
        [Header("Mode")]
        [SerializeField] private VisibilityPresentationMode visibilityPresentationMode = VisibilityPresentationMode.LightingMask;

        [Header("Overlay tint (OverlayTint mode)")]
        [SerializeField] private Color visibilityUnknownColor = new Color(0.02f, 0.025f, 0.03f, 0.78f);
        [SerializeField] private Color visibilityHintedColor = new Color(0.18f, 0.22f, 0.26f, 0.52f);
        [SerializeField] private float visibilityOverlayLift = 0.015f;
        [SerializeField] private Color fogUnknownTint = new Color(0.07f, 0.08f, 0.1f, 1f);
        [SerializeField] private Color fogHintedTint = new Color(0.34f, 0.38f, 0.42f, 1f);
        [SerializeField] [Range(0f, 1f)] private float unknownFogAmount = 0.88f;
        [SerializeField] [Range(0f, 1f)] private float hintedFogAmount = 0.58f;

        [Header("Lighting mask (LightingMask mode)")]
        [SerializeField] private Shader visibilityLightingShader;
        [SerializeField] [Range(0f, 1f)] private float visibilityUnknownLighting = 0.1f;
        [SerializeField] [Range(0f, 1f)] private float visibilityHintedLighting = 0.1f;
        [SerializeField] [Range(0f, 1f)] private float visibilityRevealedLighting = 1f;
        [SerializeField] [Range(0f, 1f)] private float visibilityEmissionFloor = 0.25f;
        [SerializeField] [Range(64, 1024)] private int visibilityLightingMaskResolution = 128;
        [SerializeField] [Range(0, 4)] private int visibilityLightingMaskBlurPasses = 1;
        [Tooltip("Snaps the sampled mask to the nearer of the Unknown/Revealed levels in the shader, so the vision edge reads as a line instead of a gradient (#12). The blur/bilinear smear still shapes WHERE the line falls; only the in-between brightness values are removed. Hinted collapses onto whichever level it is closer to (it ships equal to Unknown).")]
        [SerializeField] private bool visibilityLightingHardEdge = true;
        [Tooltip("경계 그라데이션 정도(#1 재판정 손잡이): 0 = 완전 하드, 1 = 스냅 없음. HardEdge가 켜져 있을 때만 의미. 블러 패스 수와 함께 프리셋을 이룬다(없음=0/블러0 · 약=0.4/블러1 · 중=0.7/블러2).")]
        [SerializeField] [Range(0f, 1f)] private float visibilityLightingEdgeGradation;
        [Tooltip("마스크를 육각 셀 중심에서 샘플해 경계가 타일 변을 따라가게 한다(#1 모양 수리). 끄면 옛 평면 샘플.")]
        [SerializeField] private bool visibilityLightingHexSnap = true;
        [Tooltip("물 타일의 밝기를 미지 조도 하나로 잠근다(2026-09-05 실플레이 #6 — 밤에는 물이 시야에 들어와도 밝아지지 않는다). 끄면 물도 땅 타일처럼 칸의 시야 조도(Unknown/Hinted/Revealed)를 따른다. 밤 설정은 켜고, 낮·인트로 설정은 끈다.")]
        [SerializeField] private bool visibilityWaterLockedToUnknown = true;

        [Header("Revealed edge fade")]
        [SerializeField] [Min(0)] private int visibilityRevealedFadeMaxSteps;
        [SerializeField] [Range(0f, 1f)] private float visibilityRevealedFadeFloorAlpha = 0.5f;
        [SerializeField] private Color visibilityRevealedFadeColor = new Color(0.02f, 0.025f, 0.03f, 1f);

        public VisibilityPresentationMode VisibilityPresentationMode => visibilityPresentationMode;
        public Color VisibilityUnknownColor => visibilityUnknownColor;
        public Color VisibilityHintedColor => visibilityHintedColor;
        public float VisibilityOverlayLift => visibilityOverlayLift;
        public Color FogUnknownTint => fogUnknownTint;
        public Color FogHintedTint => fogHintedTint;
        public float UnknownFogAmount => unknownFogAmount;
        public float HintedFogAmount => hintedFogAmount;
        public Shader VisibilityLightingShader => visibilityLightingShader;
        public float VisibilityUnknownLighting => visibilityUnknownLighting;
        public float VisibilityHintedLighting => visibilityHintedLighting;
        public float VisibilityRevealedLighting => visibilityRevealedLighting;
        public float VisibilityEmissionFloor => visibilityEmissionFloor;
        public int VisibilityLightingMaskResolution => visibilityLightingMaskResolution;
        public int VisibilityLightingMaskBlurPasses => visibilityLightingMaskBlurPasses;
        public bool VisibilityLightingHardEdge => visibilityLightingHardEdge;
        public float VisibilityLightingEdgeGradation => visibilityLightingEdgeGradation;
        public bool VisibilityLightingHexSnap => visibilityLightingHexSnap;
        public bool VisibilityWaterLockedToUnknown => visibilityWaterLockedToUnknown;
        public int VisibilityRevealedFadeMaxSteps => visibilityRevealedFadeMaxSteps;
        public float VisibilityRevealedFadeFloorAlpha => visibilityRevealedFadeFloorAlpha;
        public Color VisibilityRevealedFadeColor => visibilityRevealedFadeColor;
    }
}

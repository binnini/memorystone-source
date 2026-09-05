using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Scene-neutral live render-stats HUD. Shows live draw calls / batches / SetPass /
    /// triangles / frame time next to a committed steady-state budget so art and perf tuning
    /// can see cost immediately. Originally the ArtLookdev-only HUD; generalized so both the
    /// ArtLookdev lookdev scene and the canonical MainGameplay scene can host an instance.
    /// Editor-only stats come from UnityEditor.UnityStats (same source as the whole-scene perf
    /// test); in a player build only frame time is shown. Safe to leave scene-resident: attach
    /// it to a default-disabled GameObject and enable it from the inspector when profiling.
    /// Default budget = MainGameplay steady (P1, 2026-07-22 — docs/performance-testing.md).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ScenePerfHud : MonoBehaviour
    {
        [Header("Steady 예산 (MainGameplay steady, P1 2026-07-22 실측 — docs/performance-testing.md)")]
        [SerializeField] private int budgetDrawCalls = 500;
        [SerializeField] private int budgetSetPassCalls = 125;
        [SerializeField] private float budgetTrianglesMillions = 3.4f;
        [SerializeField] private float budgetFrameMs = 13.3f;

        [Header("표시")]
        [SerializeField] private string hudTitle = "Scene Perf HUD (예산 = steady)";
        [SerializeField] private bool visible = true;
        [SerializeField] private Vector2 screenOffset = new Vector2(12f, 12f);

        private float smoothedDeltaTime;

        private void Update()
        {
            if (Application.isPlaying)
            {
                smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, Time.unscaledDeltaTime, 0.06f);
            }
        }

        private void OnGUI()
        {
            // The F1 debug UI ladder hides this HUD too; it is IMGUI, so no CanvasGroup reaches it.
            if (!visible || SeoulPlayup.Combat.Unity.DebugUiVisibilityState.ImmediateModeDebugUiHidden)
            {
                return;
            }

            var lines = BuildLines();
            var style = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            var background = new Rect(screenOffset.x - 6f, screenOffset.y - 6f, 340f, 22f * lines.Length + 12f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(background, Texture2D.whiteTexture);
            GUI.color = Color.white;

            for (var i = 0; i < lines.Length; i++)
            {
                GUI.color = lines[i].OverBudget ? new Color(1f, 0.45f, 0.4f) : Color.white;
                GUI.Label(new Rect(screenOffset.x, screenOffset.y + 22f * i, 340f, 22f), lines[i].Text, style);
            }

            GUI.color = Color.white;
        }

        private readonly struct HudLine
        {
            public HudLine(string text, bool overBudget)
            {
                Text = text;
                OverBudget = overBudget;
            }

            public string Text { get; }
            public bool OverBudget { get; }
        }

        private HudLine[] BuildLines()
        {
#if UNITY_EDITOR
            var drawCalls = UnityEditor.UnityStats.drawCalls;
            var batches = UnityEditor.UnityStats.batches;
            var setPass = UnityEditor.UnityStats.setPassCalls;
            var triangles = UnityEditor.UnityStats.triangles;
            var frameMs = Application.isPlaying && smoothedDeltaTime > 0f ? smoothedDeltaTime * 1000f : 0f;
            var triangleBudget = budgetTrianglesMillions * 1_000_000f;
            return new[]
            {
                new HudLine(hudTitle, false),
                new HudLine($"DrawCalls {drawCalls}  /  예산 {budgetDrawCalls}", drawCalls > budgetDrawCalls),
                new HudLine($"Batches   {batches}", false),
                new HudLine($"SetPass   {setPass}  /  예산 {budgetSetPassCalls}", setPass > budgetSetPassCalls),
                new HudLine($"Tris      {triangles / 1_000_000f:0.00}M  /  예산 {budgetTrianglesMillions:0.0}M", triangles > triangleBudget),
                Application.isPlaying
                    ? new HudLine($"Frame     {frameMs:0.0}ms  /  예산 {budgetFrameMs:0.0}ms", frameMs > budgetFrameMs)
                    : new HudLine("Frame     (플레이 모드에서 표시)", false)
            };
#else
            var frameMs = smoothedDeltaTime > 0f ? smoothedDeltaTime * 1000f : 0f;
            return new[]
            {
                new HudLine(hudTitle, false),
                new HudLine($"Frame {frameMs:0.0}ms / 예산 {budgetFrameMs:0.0}ms (상세 스탯은 에디터 전용)", frameMs > budgetFrameMs)
            };
#endif
        }
    }
}

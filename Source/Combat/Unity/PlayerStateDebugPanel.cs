using System.Linq;
using System.Text;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    public sealed class PlayerStateDebugPanel : MonoBehaviour
    {
        [SerializeField] private MapCombatController controller;
        [SerializeField] private TMP_Text outputText;
        [SerializeField] private bool autoCreateOutputText = true;
        [SerializeField] private bool preferSceneOutputText = true;
        [SerializeField] private Vector2 panelSize = new Vector2(420f, 360f);

        public MapCombatController Controller => controller;
        public TMP_Text OutputText => outputText;

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<MapCombatController>();
            }

            if (outputText == null)
            {
                ResolveOutputText();
            }
        }

        private void Update()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<MapCombatController>();
            }

            if (outputText == null)
            {
                ResolveOutputText();
            }

            if (outputText == null)
            {
                return;
            }

            outputText.text = controller == null || controller.State == null
                ? "PlayerStateSnapshot\nState: unavailable"
                : FormatSnapshot(controller.State.CreatePlayerStateSnapshot());
        }

        public static string FormatSnapshot(PlayerStateSnapshot snapshot)
        {
            var builder = new StringBuilder(512);
            builder.AppendLine("PlayerStateSnapshot");
            builder.AppendLine($"Vitals: HP {snapshot.Hp}/{snapshot.MaxHp}  Block {snapshot.Block}  Dead {snapshot.IsDead}");
            builder.AppendLine($"Ki: {snapshot.CurrentKi}/{snapshot.MaxKi}");
            builder.AppendLine($"Position: {snapshot.Position}  Phase: {snapshot.Phase}");
            builder.AppendLine($"Move Deck: D/H/X {snapshot.MoveDeck.DrawCount}/{snapshot.MoveDeck.HandCount}/{snapshot.MoveDeck.DiscardCount}");
            builder.AppendLine($"Action Deck: D/H/X {snapshot.ActionDeck.DrawCount}/{snapshot.ActionDeck.HandCount}/{snapshot.ActionDeck.DiscardCount}");
            builder.AppendLine($"Visibility: Unknown {snapshot.Visibility.UnknownCount}  Hinted {snapshot.Visibility.HintedCount}  Revealed {snapshot.Visibility.RevealedCount}");
            builder.AppendLine($"Objective: {(snapshot.ObjectiveCompleted ? "Complete" : "Incomplete")} — {snapshot.ObjectiveStatusText}");
            var placeholderInventory = new PlayerInventoryState();
            builder.AppendLine($"Relics/Curses: {placeholderInventory.RelicCurseStatusText}");
            builder.AppendLine($"Bag: {placeholderInventory.BagStatusText}");
            builder.AppendLine($"Last Card: {(snapshot.LastDiscardedCard.HasValue ? snapshot.LastDiscardedCard.Value.ToString() : "None")}");
            builder.AppendLine($"Last Failure: {FormatEmpty(snapshot.LastFailureReason)}");
            builder.AppendLine($"Last Investigate: {FormatEmpty(snapshot.LastInvestigateResult)}");
            return builder.ToString();
        }

        private void ResolveOutputText()
        {
            if (outputText != null)
            {
                return;
            }

            if (preferSceneOutputText)
            {
                var sceneText = FindSceneOutputText();
                if (sceneText != null)
                {
                    outputText = sceneText;
                    return;
                }
            }

            if (autoCreateOutputText)
            {
                outputText = CreateRuntimeOutputText();
            }
        }

        private TMP_Text FindSceneOutputText()
        {
            var scene = gameObject.scene;
            return FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(text => text.name == GameplaySceneContract.PlayerStateDebugTextName && text.gameObject.scene == scene);
        }

        private TMP_Text CreateRuntimeOutputText()
        {
            var canvasObject = new GameObject("PlayerState Debug Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Match the gameplay HUD reference resolution (GameplaySceneContract 2200x1238).
            scaler.referenceResolution = new Vector2(2200f, 1238f);

            var panelObject = new GameObject("PlayerState Debug Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(16f, -16f);
            panelRect.sizeDelta = panelSize;
            panelObject.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 0.84f);

            var textObject = new GameObject("PlayerState Debug Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panelObject.transform, worldPositionStays: false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 12f);
            textRect.offsetMax = new Vector2(-14f, -12f);

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 18f;
            text.color = new Color(0.94f, 0.96f, 1f, 1f);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        private static string FormatEmpty(string value) => string.IsNullOrWhiteSpace(value) ? "None" : value;
    }
}

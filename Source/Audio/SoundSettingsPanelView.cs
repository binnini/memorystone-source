using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class SoundSettingsPanelView : MonoBehaviour
    {
        [SerializeField] private SoundVolumeSliderControl[] sliders;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button returnToLobbyButton;
        [SerializeField] private GameObject returnConfirmDialog;
        [SerializeField] private Button confirmReturnToLobbyButton;
        [SerializeField] private Button cancelReturnToLobbyButton;

        private void Awake()
        {
            AutoBindFromHierarchy();
            NormalizeAuthoredLayout();
            WireButtons();
            Refresh();
        }

        private void OnEnable()
        {
            AutoBindFromHierarchy();
            NormalizeAuthoredLayout();
            WireButtons();
            SoundSettingsService.SettingsChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            SoundSettingsService.SettingsChanged -= Refresh;
        }

        public void AutoBindFromHierarchy()
        {
            if (sliders == null || sliders.Length == 0)
                sliders = GetComponentsInChildren<SoundVolumeSliderControl>(true);

            closeButton = closeButton != null ? closeButton : FindButtonInBindScope("CloseSettingsButton");
            returnToLobbyButton = returnToLobbyButton != null ? returnToLobbyButton : FindButtonInBindScope("ReturnToLobbyButton");
            returnConfirmDialog = returnConfirmDialog != null ? returnConfirmDialog : FindChildInBindScope("ReturnToLobbyConfirmDialog")?.gameObject;
            confirmReturnToLobbyButton = confirmReturnToLobbyButton != null ? confirmReturnToLobbyButton : FindButtonInBindScope("ConfirmReturnToLobbyButton");
            cancelReturnToLobbyButton = cancelReturnToLobbyButton != null ? cancelReturnToLobbyButton : FindButtonInBindScope("CancelReturnToLobbyButton");
        }

        public void Refresh()
        {
            if (sliders == null)
                return;

            for (var i = 0; i < sliders.Length; i++)
            {
                if (sliders[i] != null)
                    sliders[i].Refresh();
            }
        }

        private void WireButtons()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(ClosePanel);
                closeButton.onClick.AddListener(ClosePanel);
            }

            if (returnToLobbyButton != null)
            {
                returnToLobbyButton.onClick.RemoveListener(ShowReturnConfirmDialog);
                returnToLobbyButton.onClick.AddListener(ShowReturnConfirmDialog);
            }

            if (confirmReturnToLobbyButton != null)
            {
                confirmReturnToLobbyButton.onClick.RemoveListener(ReturnToLobby);
                confirmReturnToLobbyButton.onClick.AddListener(ReturnToLobby);
            }

            if (cancelReturnToLobbyButton != null)
            {
                cancelReturnToLobbyButton.onClick.RemoveListener(HideReturnConfirmDialog);
                cancelReturnToLobbyButton.onClick.AddListener(HideReturnConfirmDialog);
            }

            if (returnConfirmDialog != null)
                returnConfirmDialog.SetActive(false);
        }

        private void NormalizeAuthoredLayout()
        {
            var rect = transform as RectTransform;
            if (rect != null && rect.rect.width <= 1f)
                rect.sizeDelta = new Vector2(388f, rect.sizeDelta.y);

            if (sliders != null)
            {
                for (var i = 0; i < sliders.Length; i++)
                {
                    NormalizeSlider(sliders[i]);
                }
            }

            EnsureVisibleButton(returnToLobbyButton, 300f, 44f, Color.black);
            EnsureVisibleButton(confirmReturnToLobbyButton, 112f, 38f, new Color(0.075f, 0.074f, 0.074f, 1f));
            EnsureVisibleButton(cancelReturnToLobbyButton, 112f, 38f, new Color(0.075f, 0.074f, 0.074f, 1f));

            if (returnConfirmDialog == null)
                return;

            var dialogRect = returnConfirmDialog.transform as RectTransform;
            if (dialogRect != null && dialogRect.rect.width <= 1f)
                dialogRect.sizeDelta = new Vector2(360f, dialogRect.sizeDelta.y <= 1f ? 136f : dialogRect.sizeDelta.y);

            var dialogImage = returnConfirmDialog.GetComponent<Image>();
            if (dialogImage != null)
                dialogImage.enabled = true;
        }

        private static void NormalizeSlider(SoundVolumeSliderControl slider)
        {
            if (slider == null)
                return;

            var rect = slider.transform as RectTransform;
            if (rect != null && rect.rect.width <= 1f)
                rect.sizeDelta = new Vector2(360f, rect.sizeDelta.y <= 1f ? 42f : rect.sizeDelta.y);

            var track = slider.transform.Find("Track") as RectTransform;
            if (track != null && track.rect.width <= 1f)
                track.sizeDelta = new Vector2(174f, track.sizeDelta.y <= 1f ? 14f : track.sizeDelta.y);
        }

        private static void EnsureVisibleButton(Button button, float width, float height, Color backgroundColor)
        {
            if (button == null)
                return;

            var rect = button.transform as RectTransform;
            if (rect != null && rect.rect.width <= 1f)
                rect.sizeDelta = new Vector2(width, rect.sizeDelta.y <= 1f ? height : rect.sizeDelta.y);

            var image = button.GetComponent<Image>();
            if (image == null)
                return;

            image.enabled = true;
            image.color = backgroundColor;
            image.raycastTarget = true;
            button.targetGraphic = image;
        }

        private void ClosePanel()
        {
            gameObject.SetActive(false);
        }

        private void ShowReturnConfirmDialog()
        {
            if (returnConfirmDialog != null)
                returnConfirmDialog.SetActive(true);
        }

        private void HideReturnConfirmDialog()
        {
            if (returnConfirmDialog != null)
                returnConfirmDialog.SetActive(false);
        }

        // SeoulPlayup.Flow references SeoulPlayup.Audio, so SceneFlowController cannot be
        // referenced directly. Resolve it reflectively so the lobby scene name stays authored in
        // one place; the literal load remains only as a last resort when the Flow assembly is absent.
        private static void ReturnToLobby()
        {
            var flowType = System.Type.GetType(
                "SeoulPlayup.Flow.Unity.SceneFlowController, SeoulPlayup.Flow");
            var controller = flowType != null
                ? flowType.GetMethod("GetOrCreate")?.Invoke(null, null) as MonoBehaviour
                : null;
            if (controller != null)
            {
                controller.SendMessage("ReturnToLobby", SendMessageOptions.DontRequireReceiver);
                return;
            }

            SceneManager.LoadScene("Lobby");
        }

        private Button FindButtonInBindScope(string objectName)
        {
            var target = FindChildInBindScope(objectName);
            return target != null ? target.GetComponent<Button>() : null;
        }

        private Transform FindChildInBindScope(string objectName)
        {
            var target = FindChild(transform, objectName);
            if (target != null)
                return target;

            return transform.parent != null ? FindChild(transform.parent, objectName) : null;
        }

        private static Transform FindChild(Transform root, string objectName)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].name == objectName)
                    return all[i];
            }

            return null;
        }
    }
}

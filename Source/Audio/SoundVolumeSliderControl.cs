using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class SoundVolumeSliderControl : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        [SerializeField] private SoundSettingsChannel channel;
        [SerializeField] private RectTransform trackRect;
        [SerializeField] private RectTransform fillRect;
        [SerializeField] private RectTransform handleRect;
        [SerializeField] private TMP_Text valueLabel;

        public SoundSettingsChannel Channel => channel;

        private void Awake()
        {
            AutoBindFromHierarchy();
        }

        private void OnEnable()
        {
            SoundSettingsService.SettingsChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            SoundSettingsService.SettingsChanged -= Refresh;
        }

        public void AutoBindFromHierarchy()
        {
            if (trackRect == null)
                trackRect = transform.Find("Track") as RectTransform;
            if (fillRect == null)
                fillRect = transform.Find("Track/Fill") as RectTransform;
            if (handleRect == null)
                handleRect = transform.Find("Track/Handle") as RectTransform;
            if (valueLabel == null)
                valueLabel = transform.Find("Value")?.GetComponent<TMP_Text>();
        }

        public void Refresh()
        {
            SetVisualValue(SoundSettingsService.GetVolume(channel));
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            SetFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            SetFromPointer(eventData);
        }

        private void SetFromPointer(PointerEventData eventData)
        {
            AutoBindFromHierarchy();
            if (trackRect == null || eventData == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(trackRect, eventData.position, eventData.pressEventCamera, out var localPoint))
                return;

            var rect = trackRect.rect;
            var normalized = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
            SoundSettingsService.SetVolume(channel, normalized);
            SetVisualValue(normalized);
        }

        private void SetVisualValue(float value)
        {
            value = Mathf.Clamp01(value);
            AutoBindFromHierarchy();

            if (fillRect != null)
                fillRect.anchorMax = new Vector2(value, fillRect.anchorMax.y);

            if (handleRect != null)
            {
                handleRect.anchorMin = new Vector2(value, handleRect.anchorMin.y);
                handleRect.anchorMax = new Vector2(value, handleRect.anchorMax.y);
                handleRect.anchoredPosition = new Vector2(0f, handleRect.anchoredPosition.y);
            }

            if (valueLabel != null)
                valueLabel.text = Mathf.RoundToInt(value * 100f).ToString();
        }
    }
}

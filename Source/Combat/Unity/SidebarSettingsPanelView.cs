using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class SidebarSettingsPanelView : MonoBehaviour
    {
        [SerializeField] private SoundSettingsPanelView soundSettingsPanel;

        private bool buttonsSkinned;

        public void AutoBindFromHierarchy()
        {
            soundSettingsPanel = soundSettingsPanel != null
                ? soundSettingsPanel
                : GetComponentInChildren<SoundSettingsPanelView>(true);
        }

        public void Refresh(CombatState state, TMP_FontAsset font)
        {
            AutoBindFromHierarchy();
            SkinButtonsOnce();
            if (soundSettingsPanel != null)
                soundSettingsPanel.Refresh();
        }

        // The settings callout's buttons ("로비로 돌아가기" and its confirm dialog) were authored as a flat
        // blue / near-black plate — the last unstyled buttons inside a sidebar callout after the P6 T2 dark
        // pass. Routed through the shared skin at runtime rather than in the prefab, because driving
        // UiButtonSkin from the editor serialises a material instance. Applied once: the skin is idempotent
        // but Refresh runs on every sidebar tick.
        private void SkinButtonsOnce()
        {
            if (buttonsSkinned || !Application.isPlaying)
            {
                return;
            }

            buttonsSkinned = true;
            foreach (var button in GetComponentsInChildren<Button>(includeInactive: true))
            {
                UiButtonSkin.Apply(button);
            }
        }
    }
}

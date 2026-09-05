using System;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class SidebarPanelButton : MonoBehaviour
    {
        [SerializeField] private string panelKey;
        [SerializeField] private SidebarCalloutPanelController controller;

        private Button button;
        private Func<string, bool> externalClickHandler;

        public string PanelKey => panelKey;
        public SidebarCalloutPanelController Controller => controller;

        private void OnEnable()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(HandleClick);
        }

        private void OnDisable()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
            }
        }

        public void Bind(string key, SidebarCalloutPanelController panelController)
        {
            panelKey = key;
            controller = panelController;
        }

        public void BindExternalClickHandler(Func<string, bool> handler)
        {
            externalClickHandler = handler;
        }

        private void HandleClick()
        {
            if (externalClickHandler != null && externalClickHandler.Invoke(panelKey))
            {
                return;
            }

            if (controller != null)
            {
                controller.TogglePanel(panelKey);
            }
        }
    }
}

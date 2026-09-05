using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Combat.Unity
{
    // Raw input-signal detection extracted from MapCombatController (refactoring stage 4-2).
    // Scope is intentionally narrow: only device-polling helpers with zero host-state dependency
    // move here. The gesture-to-gameplay-action dispatch (EndAction/CancelSelectedCard/TryMoveTo/
    // OnHexClicked, plus the shortcut-suppression gates tied to overlay/reward/hand-selection
    // visibility) stays on the host — that is combat/UI orchestration triggered by input, not
    // input routing itself, and does not separate cleanly without a logic-changing redesign.
    public static class CombatInputRouter
    {
        public static void ReadCameraRotationShortcutInput(out bool rotateLeftPressed, out bool rotateRightPressed)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            rotateLeftPressed = keyboard != null && keyboard.qKey.wasPressedThisFrame;
            rotateRightPressed = keyboard != null && keyboard.eKey.wasPressedThisFrame;
#else
            rotateLeftPressed = Input.GetKeyDown(KeyCode.Q);
            rotateRightPressed = Input.GetKeyDown(KeyCode.E);
#endif
        }

        public static bool WasEndActionShortcutPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.fKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F);
#endif
        }

        public static bool WasCardCancelShortcutPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            return mouse != null && mouse.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        public static bool WasPauseMenuShortcutPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        public static bool WasPrimaryClickPressed(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPosition = mouse.position.ReadValue();
                return true;
            }

            screenPosition = default;
            return false;
#else
            if (Input.GetMouseButtonDown(0))
            {
                screenPosition = Input.mousePosition;
                return true;
            }

            screenPosition = default;
            return false;
#endif
        }

        public static bool TryGetCurrentPointerPosition(out Vector2 pointerPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                pointerPosition = Mouse.current.position.ReadValue();
                return true;
            }
#endif
            if (Input.mousePresent)
            {
                pointerPosition = Input.mousePosition;
                return true;
            }

            pointerPosition = default;
            return false;
        }

        public static bool IsTextInputFocused()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null &&
                   (selected.GetComponentInParent<InputField>() != null ||
                    selected.GetComponentInParent<TMP_InputField>() != null);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Debug-build toggle that hides gameplay UI so effects can be watched without chrome.
    ///
    /// Deliberately NOT in the SeoulPlayup.Dev assembly despite the name. Dev is constrained to
    /// UNITY_EDITOR (cs:757), which strips it from every player build — development builds included — and
    /// <see cref="Available"/> is written to run in exactly those (UNITY_EDITOR || DEVELOPMENT_BUILD, plus
    /// the forceEnableInReleaseBuild opt-in authored per scene). Moving this back under Dev/ silently
    /// deletes the feature from builds while leaving the scene component behind as a missing script; that
    /// already happened once. <see cref="DebugUiVisibilityPlan"/> and <see cref="DebugUiVisibilityState"/>
    /// sit here for the same reason.
    /// The toggle key (Backslash by default, F1 also accepted) cycles Full -> HandOnly -> None ->
    /// Full; holding Shift with it restores Full immediately.
    ///
    /// Hides with CanvasGroup alpha, never SetActive, for two reasons:
    /// (1) CinematicUiHider already SetActive-toggles the same subtree for the stage intro and the
    ///     victory sweep, and its Restore() would resurrect anything this tool had switched off;
    /// (2) deactivating a view stops its Update/coroutines (turn-phase fade, card flight), so the
    ///     restored HUD would come back stale. Alpha keeps every view alive and correct.
    ///
    /// Nothing here touches floating text, VFX, tile overlays, status icons or unit health bars —
    /// those are world-space objects outside the UI canvas, which is exactly why the canvas-only
    /// approach satisfies "hide the UI but keep the feedback".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugUiVisibilityController : MonoBehaviour
    {
        [Header("동작")]
        [Tooltip("손패 레벨에서 모달(카드 선택/갈림길/보상/승패/컷신)까지 숨깁니다. 켜면 진행이 막힐 수 있습니다.")]
        [SerializeField] private bool hideModalsInHandOnly;

        [Tooltip("숨김 상태일 때 화면 좌상단에 현재 레벨을 표시합니다.")]
        [SerializeField] private bool showLevelLabel = true;

        [Tooltip("런타임에 새로 생성되는 UI(툴팁 등)를 다시 훑는 주기(초).")]
        [SerializeField] private float targetRefreshInterval = 0.5f;

        [Tooltip("릴리스 빌드에서도 강제로 활성화합니다. 평소에는 꺼두세요.")]
        [SerializeField] private bool forceEnableInReleaseBuild;

        [Header("입력")]
        // F1 is contended: the editor binds it to three shortcuts (ShaderGraph docs, Terrain, Search),
        // and F10-F12 are taken by macOS itself. Backslash is free in both, so it is the default and
        // F1 stays accepted as a secondary key.
        [Tooltip("토글 키. 기본 Backslash(\\). F1도 항상 함께 받습니다.")]
#if ENABLE_INPUT_SYSTEM
        [SerializeField] private Key toggleKey = Key.Backslash;
#endif

        [Tooltip("컨트롤러가 실제로 본 키 입력을 콘솔에 찍습니다. 키가 안 먹을 때만 켜세요.")]
        [SerializeField] private bool logInputDiagnostics;

        private readonly List<Target> targets = new List<Target>();
        private readonly Dictionary<CanvasGroup, HiddenState> hiddenGroups = new Dictionary<CanvasGroup, HiddenState>();
        private readonly HashSet<CanvasGroup> wantHidden = new HashSet<CanvasGroup>();
        private readonly List<CanvasGroup> restoreScratch = new List<CanvasGroup>();

        private DebugUiVisibilityLevel level = DebugUiVisibilityLevel.Full;
        private Transform gameplayLayerRoot;
        private float nextRefreshTime;

        public DebugUiVisibilityLevel Level => level;

        private bool Available => forceEnableInReleaseBuild || CombatDebugControlPanel.DebugUiAvailable;

        // The flag is static, so a domain-reload-free play session could inherit a stale true.
        private void OnEnable()
        {
            DebugUiVisibilityState.ImmediateModeDebugUiHidden = level != DebugUiVisibilityLevel.Full;
        }

        private void OnDisable()
        {
            RestoreAll();
            DebugUiVisibilityState.ImmediateModeDebugUiHidden = false;
        }

        // LateUpdate, not Update: TurnPhaseDockView and CardRewardPopupView write CanvasGroup alpha
        // from their own Update/coroutines, so re-applying after them makes this tool the last writer.
        private void LateUpdate()
        {
            if (!Available)
            {
                return;
            }

            PollInput();

            if (level == DebugUiVisibilityLevel.Full && hiddenGroups.Count == 0)
            {
                return;
            }

            Apply();
        }

        public void SetLevel(DebugUiVisibilityLevel next)
        {
            level = next;
            nextRefreshTime = 0f;
            DebugUiVisibilityState.ImmediateModeDebugUiHidden = level != DebugUiVisibilityLevel.Full;
            Apply();
        }

        /// <summary>
        /// Shows/hides this tool's own OnGUI level label. Filming takes must turn it off: it is drawn
        /// outside the canvas this controller dims, so it would be the one piece of chrome left in frame.
        /// </summary>
        public void SetLevelLabelVisible(bool visible)
        {
            showLevelLabel = visible;
        }

        private void PollInput()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                if (logInputDiagnostics)
                {
                    Debug.Log("[DebugUiToggle] Keyboard.current is null — no keyboard device is bound.");
                }

                return;
            }

            if (logInputDiagnostics)
            {
                foreach (var key in keyboard.allKeys)
                {
                    if (key.wasPressedThisFrame)
                    {
                        Debug.Log("[DebugUiToggle] saw key: " + key.keyCode);
                    }
                }
            }

            if (!keyboard[toggleKey].wasPressedThisFrame && !keyboard.f1Key.wasPressedThisFrame)
            {
                return;
            }

            var shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
#else
            if (!Input.GetKeyDown(KeyCode.F1))
            {
                return;
            }

            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            SetLevel(shift ? DebugUiVisibilityLevel.Full : DebugUiVisibilityPlan.Next(level));
        }

        private void Apply()
        {
            RefreshTargetsIfDue();

            wantHidden.Clear();
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.Root == null)
                {
                    continue;
                }

                if (DebugUiVisibilityPlan.IsVisible(target.Group, level, hideModalsInHandOnly))
                {
                    continue;
                }

                for (var h = 0; h < target.Hosts.Count; h++)
                {
                    var host = target.Hosts[h];
                    if (host != null)
                    {
                        wantHidden.Add(host);
                    }
                }
            }

            foreach (var group in wantHidden)
            {
                Hide(group);
            }

            restoreScratch.Clear();
            foreach (var pair in hiddenGroups)
            {
                if (!wantHidden.Contains(pair.Key))
                {
                    restoreScratch.Add(pair.Key);
                }
            }

            for (var i = 0; i < restoreScratch.Count; i++)
            {
                Restore(restoreScratch[i]);
            }
        }

        private void Hide(CanvasGroup group)
        {
            // Capture the authored values only on the transition into hidden; re-applying each frame
            // must not overwrite the snapshot with our own zeroes.
            if (!hiddenGroups.ContainsKey(group))
            {
                hiddenGroups[group] = new HiddenState(group.alpha, group.interactable, group.blocksRaycasts);
            }

            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        private void Restore(CanvasGroup group)
        {
            if (hiddenGroups.TryGetValue(group, out var state) && group != null)
            {
                group.alpha = state.Alpha;
                group.interactable = state.Interactable;
                group.blocksRaycasts = state.BlocksRaycasts;
            }

            hiddenGroups.Remove(group);
        }

        private void RestoreAll()
        {
            restoreScratch.Clear();
            foreach (var pair in hiddenGroups)
            {
                restoreScratch.Add(pair.Key);
            }

            for (var i = 0; i < restoreScratch.Count; i++)
            {
                Restore(restoreScratch[i]);
            }

            hiddenGroups.Clear();
            restoreScratch.Clear();
        }

        private void RefreshTargetsIfDue()
        {
            if (Time.unscaledTime < nextRefreshTime && gameplayLayerRoot != null)
            {
                return;
            }

            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, targetRefreshInterval);
            targets.Clear();

            gameplayLayerRoot = FindInScene(DebugUiVisibilityPlan.GameplayLayerRootName);
            if (gameplayLayerRoot != null)
            {
                for (var i = 0; i < gameplayLayerRoot.childCount; i++)
                {
                    var child = gameplayLayerRoot.GetChild(i);
                    if (child == null)
                    {
                        continue;
                    }

                    if (DebugUiVisibilityPlan.IsCardLane(child.name))
                    {
                        for (var c = 0; c < child.childCount; c++)
                        {
                            var laneChild = child.GetChild(c);
                            if (laneChild != null)
                            {
                                AddTarget(laneChild, DebugUiVisibilityPlan.ClassifyCardLaneChild(laneChild.name));
                            }
                        }

                        continue;
                    }

                    AddTarget(child, DebugUiVisibilityPlan.ClassifyRoot(child.name));
                }
            }

            for (var i = 0; i < DebugUiVisibilityPlan.ExternalRootNames.Length; i++)
            {
                var name = DebugUiVisibilityPlan.ExternalRootNames[i];
                var root = FindInScene(name);
                if (root != null)
                {
                    AddTarget(root, DebugUiVisibilityPlan.ClassifyRoot(name));
                }
            }
        }

        private void AddTarget(Transform root, DebugUiGroup group)
        {
            var hosts = new List<CanvasGroup>();
            CollectHosts(root, hosts);

            // Descendants that opt out of parent groups (TurnPhaseDock's persistent phase readout,
            // the reward popup's click layer) would stay visible behind a parent alpha of 0, so they
            // are hidden individually.
            var descendants = root.GetComponentsInChildren<CanvasGroup>(includeInactive: true);
            for (var i = 0; i < descendants.Length; i++)
            {
                var candidate = descendants[i];
                if (candidate != null && candidate.ignoreParentGroups && !hosts.Contains(candidate))
                {
                    hosts.Add(candidate);
                }
            }

            if (hosts.Count > 0)
            {
                targets.Add(new Target(root, group, hosts));
            }
        }

        // A CanvasGroup only carries alpha for the Graphics beneath it, and some authored dock
        // containers (TurnPhaseDock) are plain Transforms rather than RectTransforms. Anchor on the
        // node itself when it is a RectTransform, otherwise on its RectTransform descendants.
        private static void CollectHosts(Transform node, List<CanvasGroup> hosts)
        {
            if (node == null)
            {
                return;
            }

            if (node is RectTransform)
            {
                hosts.Add(EnsureCanvasGroup(node.gameObject));
                return;
            }

            for (var i = 0; i < node.childCount; i++)
            {
                CollectHosts(node.GetChild(i), hosts);
            }
        }

        private static CanvasGroup EnsureCanvasGroup(GameObject target)
        {
            var group = target.GetComponent<CanvasGroup>();
            return group != null ? group : target.AddComponent<CanvasGroup>();
        }

        private Transform FindInScene(string objectName)
        {
            var scene = gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindByName(root.transform, objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindByName(Transform node, string objectName)
        {
            if (node.name == objectName)
            {
                return node;
            }

            for (var i = 0; i < node.childCount; i++)
            {
                var found = FindByName(node.GetChild(i), objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void OnGUI()
        {
            if (!Available || !showLevelLabel || level == DebugUiVisibilityLevel.Full)
            {
                return;
            }

            var text = DebugUiVisibilityPlan.Describe(level);
            var style = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(6f, 6f, 260f, 24f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(12f, 8f, 254f, 20f), text, style);
        }

        private readonly struct HiddenState
        {
            public HiddenState(float alpha, bool interactable, bool blocksRaycasts)
            {
                Alpha = alpha;
                Interactable = interactable;
                BlocksRaycasts = blocksRaycasts;
            }

            public float Alpha { get; }
            public bool Interactable { get; }
            public bool BlocksRaycasts { get; }
        }

        private readonly struct Target
        {
            public Target(Transform root, DebugUiGroup group, List<CanvasGroup> hosts)
            {
                Root = root;
                Group = group;
                Hosts = hosts;
            }

            public Transform Root { get; }
            public DebugUiGroup Group { get; }
            public List<CanvasGroup> Hosts { get; }
        }
    }
}

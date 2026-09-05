using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.MapDesign.Editor
{
    /// <summary>
    /// Shared plumbing for the Sparse Map Editor's pop-out panels. Each panel window is a dockable view
    /// onto the one <see cref="HexSparseMapEditorWindow"/> instance — it holds no authoring state, so
    /// docking a panel, closing it, or reloading the domain never forks the editor's session.
    /// </summary>
    internal abstract class HexSparseMapPanelWindow : EditorWindow
    {
        private Vector2 scroll;

        protected abstract string MissingOwnerMessage { get; }

        protected abstract void DrawPanel(HexSparseMapEditorWindow owner);

        private void OnGUI()
        {
            var owner = HexSparseMapEditorWindow.FindOpenInstance();
            if (owner == null)
            {
                EditorGUILayout.HelpBox(MissingOwnerMessage, MessageType.Info);
                if (GUILayout.Button("Open Sparse Map Editor"))
                {
                    HexSparseMapEditorWindow.Open();
                }

                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawPanel(owner);
            EditorGUILayout.EndScrollView();
        }

        // The owning editor drives Scene View interaction, so this window must repaint alongside it or the
        // panel would show stale selection/rotation values while the designer works in the viewport.
        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }

    /// <summary>Dockable host for the Map Paint panel (tools, rotation, selection).</summary>
    internal sealed class HexSparseMapPaintPanelWindow : HexSparseMapPanelWindow
    {
        protected override string MissingOwnerMessage =>
            "The Sparse Map Editor is closed. Open it to use the Map Paint panel.";

        [MenuItem("Seoul Playup/Map/Sparse Map Paint Panel")]
        public static void Open()
        {
            GetWindow<HexSparseMapPaintPanelWindow>("Map Paint");
        }

        public static void CloseIfOpen()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<HexSparseMapPaintPanelWindow>())
            {
                window.Close();
            }
        }

        protected override void DrawPanel(HexSparseMapEditorWindow owner)
        {
            owner.DrawPaintPanelContents();
        }
    }

    /// <summary>Dockable host for the Palette / Object panel.</summary>
    internal sealed class HexSparseMapPalettePanelWindow : HexSparseMapPanelWindow
    {
        protected override string MissingOwnerMessage =>
            "The Sparse Map Editor is closed. Open it to use the Palette / Object panel.";

        [MenuItem("Seoul Playup/Map/Sparse Map Palette Panel")]
        public static void Open()
        {
            GetWindow<HexSparseMapPalettePanelWindow>("Palette / Object");
        }

        public static void CloseIfOpen()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<HexSparseMapPalettePanelWindow>())
            {
                window.Close();
            }
        }

        protected override void DrawPanel(HexSparseMapEditorWindow owner)
        {
            owner.DrawPalettePanelContents();
        }
    }
}

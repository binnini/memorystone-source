using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Editor
{
    [CustomEditor(typeof(MonsterDesignTestController))]
    public sealed class MonsterDesignTestControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var controller = (MonsterDesignTestController)target;
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Monster Design Test", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load Ranged 10"))
                {
                    Undo.RecordObject(controller, "Load Ranged Monster Patterns");
                    controller.LoadRangedRecommendations();
                    EditorUtility.SetDirty(controller);
                }

                if (GUILayout.Button("Load Melee 10"))
                {
                    Undo.RecordObject(controller, "Load Melee Monster Patterns");
                    controller.LoadMeleeRecommendations();
                    EditorUtility.SetDirty(controller);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Blank Pattern"))
                {
                    Undo.RecordObject(controller, "Add Monster Pattern");
                    controller.AddBlankPattern();
                    EditorUtility.SetDirty(controller);
                }

                if (GUILayout.Button("Reset Sandbox"))
                {
                    controller.ResetSandbox();
                    EditorUtility.SetDirty(controller);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Prev Pattern"))
                {
                    Undo.RecordObject(controller, "Select Previous Monster Pattern");
                    controller.SelectPreviousPattern();
                    EditorUtility.SetDirty(controller);
                }

                if (GUILayout.Button("Next Pattern"))
                {
                    Undo.RecordObject(controller, "Select Next Monster Pattern");
                    controller.SelectNextPattern();
                    EditorUtility.SetDirty(controller);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Put Dummy In Melee"))
                {
                    Undo.RecordObject(controller, "Move Dummy To Monster Range");
                    controller.MoveDummyToMonsterRangeAndReset();
                    EditorUtility.SetDirty(controller);
                }

                if (GUILayout.Button("Ranged Spacing"))
                {
                    Undo.RecordObject(controller, "Move Monster To Ranged Test");
                    controller.MoveMonsterToRangedTestAndReset();
                    EditorUtility.SetDirty(controller);
                }
            }

            if (GUILayout.Button("Resolve One Monster Turn / Damage Test"))
            {
                controller.ResolveMonsterTurn();
                EditorUtility.SetDirty(controller);
            }

            if (GUILayout.Button("Refresh Preview"))
            {
                controller.RefreshPreview();
                EditorUtility.SetDirty(controller);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Edit the Attack Patterns list directly. Enable 'Use Only Selected Pattern' to force a single pattern test. " +
                "Red gizmo cells show the currently previewed attack range; the yellow marker is the damage dummy.",
                MessageType.Info);
        }
    }
}

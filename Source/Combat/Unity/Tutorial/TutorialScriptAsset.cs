using System.Collections.Generic;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Tutorial
{
    [CreateAssetMenu(menuName = "Seoul Playup/Tutorial/Tutorial Script", fileName = "TutorialScript")]
    public sealed class TutorialScriptAsset : ScriptableObject
    {
        [SerializeField] private string tutorialId = "tutorial_stage_000";
        [SerializeField] private List<TutorialStep> steps = new List<TutorialStep>();

        public string TutorialId => tutorialId;
        public IReadOnlyList<TutorialStep> Steps => steps;
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SeoulPlayup.Flow.Unity
{
    [CreateAssetMenu(fileName = "StageCatalog", menuName = "Seoul Playup/Flow/Stage Catalog")]
    public sealed class StageCatalog : ScriptableObject
    {
        [SerializeField] private List<StageDefinition> stages = new();

        public IReadOnlyList<StageDefinition> Stages => stages;
        public StageDefinition DefaultStage => GetDefaultStage();

        public StageDefinition GetDefaultStage()
        {
            for (var i = 0; i < stages.Count; i++)
            {
                if (stages[i] != null)
                    return stages[i];
            }

            return null;
        }

        public StageDefinition FindById(string stageId)
        {
            if (string.IsNullOrWhiteSpace(stageId))
                return null;

            for (var i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                if (stage != null && stage.StageId == stageId)
                    return stage;
            }

            return null;
        }

        public void SetStages(IEnumerable<StageDefinition> orderedStages)
        {
            stages.Clear();
            if (orderedStages == null)
                return;

            foreach (var stage in orderedStages)
            {
                if (stage != null && !stages.Contains(stage))
                    stages.Add(stage);
            }
        }
    }
}

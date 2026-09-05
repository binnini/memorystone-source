using System;
using UnityEngine;

namespace SeoulPlayup.Flow.Unity.Story
{
    /// <summary>
    /// One advanceable line of a visual-novel cutscene. Authored in the Inspector as part of a
    /// <see cref="StoryScriptAsset"/>. Each line can optionally switch the background illustration
    /// and play a one-shot sound; when those are left empty the previous illustration stays and no
    /// extra sound plays.
    /// </summary>
    [Serializable]
    public sealed class StoryLine
    {
        [Tooltip("화자 이름. 비우면 나레이션/독백으로 표시됩니다.")]
        public string speaker;

        [TextArea(1, 5)]
        [Tooltip("이 칸에 표시할 본문 텍스트.")]
        public string text;

        [Tooltip("(선택) 이 칸부터 바뀔 배경 일러스트. 비우면 이전 일러스트를 유지합니다.")]
        public Sprite illustration;

        [Tooltip("(선택) 이 칸이 나올 때 한 번 재생할 효과음/보이스.")]
        public AudioClip sound;
    }

    /// <summary>
    /// A single stage cutscene authored as a ScriptableObject (one asset per stage intro/outro).
    /// Replaces the old markdown-parsing flow: lines, illustration and music are edited directly
    /// in the Inspector, so there is no fragile text parsing and per-line art/sound is possible.
    /// </summary>
    [CreateAssetMenu(fileName = "StoryScript", menuName = "Seoul Playup/Story/Story Script")]
    public sealed class StoryScriptAsset : ScriptableObject
    {
        [Tooltip("에디터 식별용 제목(런타임에는 사용되지 않음).")]
        [SerializeField] private string title;

        [Tooltip("컷씬 시작 시 표시할 기본 배경 일러스트. 각 줄에서 개별 지정하면 그 줄부터 교체됩니다.")]
        [SerializeField] private Sprite defaultIllustration;

        [Tooltip("컷씬 동안 반복 재생할 배경 음악(선택).")]
        [SerializeField] private AudioClip backgroundMusic;

        [SerializeField] private StoryLine[] lines = Array.Empty<StoryLine>();

        public string Title => title;
        public Sprite DefaultIllustration => defaultIllustration;
        public AudioClip BackgroundMusic => backgroundMusic;
        public StoryLine[] Lines => lines ?? Array.Empty<StoryLine>();
        public bool HasLines => lines != null && lines.Length > 0;
    }
}

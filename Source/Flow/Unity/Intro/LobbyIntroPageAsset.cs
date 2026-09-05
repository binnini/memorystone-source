using System;
using UnityEngine;

namespace SeoulPlayup.Flow.Unity.Intro
{
    /// <summary>
    /// 로비 「게임 소개」 패널이 읽는 저작 에셋. 문안·이미지가 코드 밖에 있어야 나중에 문장을 고치는 데
    /// 컴파일이 필요 없다.
    /// <para>
    /// 🔑 <b>넘김이 두 층위다.</b> 페이지는 다섯이고, <b>1페이지 안에서만</b> 비트가 셋으로 넘어간다
    /// (2026-09-01 확정 「컷씬을 조금 넣으면서」). 그래서 페이지 점은 <b>페이지만</b> 센다 —
    /// 비트까지 세면 「5장」이 거짓말이 된다.
    /// </para>
    /// <para>
    /// ⚠️ 이것은 은퇴한 스토리 컷씬(<see cref="Story.StoryCutsceneFeature"/>)의 부활이 아니다.
    /// 씬을 갈아타지도, <c>StoryScriptAsset</c>을 쓰지도 않는다 — 패널 안에서 그림과 글이 바뀔 뿐이다.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "LobbyIntroPages", menuName = "Seoul Playup/Flow/Lobby Intro Pages")]
    public sealed class LobbyIntroPageAsset : ScriptableObject
    {
        /// <summary>
        /// 컷씬 비트 한 칸. 화자는 비워도 되고(대사가 아닌 지문), 본문만 있으면 성립한다.
        /// </summary>
        [Serializable]
        public sealed class Beat
        {
            [Tooltip("말하는 이. 비우면 이름표를 그리지 않는다(지문·독백).")]
            [SerializeField] private string speaker;
            [TextArea(2, 5)]
            [SerializeField] private string body;

            [Tooltip("이 비트에서 그림의 어느 부분을 보여줄지. 이미지 폭을 1로 본 정규화 좌표이고, " +
                     "scale 1이면 가로를 꽉 채운다. 세 비트가 같은 그림 한 장을 훑는다는 것이 이 필드의 뜻이다.")]
            [SerializeField] private Vector2 focus = new Vector2(0.5f, 0.5f);
            [Range(1f, 4f)]
            [SerializeField] private float scale = 1f;

            public string Speaker => speaker;
            public string Body => body;
            public Vector2 Focus => focus;
            public float Scale => Mathf.Max(1f, scale);
        }

        /// <summary>
        /// 페이지 한 장. <see cref="Beats"/>가 비어 있으면 <see cref="Body"/>를 그대로 쓰는 정적 페이지이고,
        /// 하나 이상이면 비트를 차례로 넘기는 컷씬 페이지다 — 1페이지만 후자다.
        /// </summary>
        [Serializable]
        public sealed class Page
        {
            [SerializeField] private string title;
            [TextArea(3, 8)]
            [Tooltip("정적 페이지의 본문. 컷씬 페이지(beats가 있는 페이지)에서는 쓰이지 않는다.")]
            [SerializeField] private string body;

            [Tooltip("이 페이지의 그림. 아직 없으면 비워 둔다 — 패널이 자리를 비우고 나머지를 그린다.")]
            [SerializeField] private Sprite image;

            [Tooltip("그림이 아직 없을 때 그 자리에 적을 한 줄(발주 대기 표시). 비우면 아무것도 안 적는다.")]
            [SerializeField] private string imagePlaceholderNote;

            [Tooltip("비어 있지 않으면 이 페이지는 컷씬 페이지가 된다. 1페이지 전용.")]
            [SerializeField] private Beat[] beats = Array.Empty<Beat>();

            public string Title => title;
            public string Body => body;
            public Sprite Image => image;
            public string ImagePlaceholderNote => imagePlaceholderNote;
            public Beat[] Beats => beats ?? Array.Empty<Beat>();
            public bool IsCutscene => Beats.Length > 0;
        }

        [SerializeField] private string panelTitle = "게임 소개";
        [SerializeField] private Page[] pages = Array.Empty<Page>();

        public string PanelTitle => string.IsNullOrWhiteSpace(panelTitle) ? "게임 소개" : panelTitle;
        public Page[] Pages => pages ?? Array.Empty<Page>();
    }
}

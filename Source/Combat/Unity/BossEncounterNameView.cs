using TMPro;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 보스 조우 연출의 <b>이름 대형 표기</b>(화면 상단). 배치·크기·색·폰트의 정본은 <b>프리팹 에셋</b>
    /// (<see cref="EditorTools.UI"/>의 빌더가 저작)이고, 런타임은 이름 문자열과 알파만 채운다 — 레이아웃을
    /// 런타임에 하드코딩하면 디자이너가 프리팹에서 보는 모습과 실플레이가 갈라진다(P2 교훈, BossHudView와 같은 규약).
    ///
    /// 조우 연출이 페이드로 알파를 올렸다 내린다(<see cref="Alpha"/>). 기본 알파는 0이라 로드 직후엔 보이지 않는다.
    /// </summary>
    public sealed class BossEncounterNameView : MonoBehaviour
    {
        public const string RootObjectName = "Boss Encounter Name Plate";
        public const string NameLabelName = "BossEncounterName_Name";
        public const string TagLabelName = "BossEncounterName_Tag";

        /// <summary>Resources 상대 경로(확장자·Resources 접두어 없음). 런타임이 이 경로로 프리팹을 로드한다.</summary>
        public const string ResourcesPath = "UI/BossEncounterNamePlate";

        [SerializeField] private CanvasGroup rootGroup;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text tagLabel;

        private void Awake()
        {
            // 로드 직후엔 숨겨 둔다 — 조우 연출이 페이드로 드러낸다.
            if (rootGroup != null)
            {
                rootGroup.alpha = 0f;
            }

            // 한글 글리프 폴백 안전망(BossHudView와 같은 규약). 저작 폰트가 우선이고, 없을 때만 채운다.
            if (nameLabel != null)
            {
                KoreanFontProvider.Apply(nameLabel);
            }

            if (tagLabel != null)
            {
                KoreanFontProvider.Apply(tagLabel);
            }
        }

        /// <summary>표기할 보스 이름을 채운다. 빈 값이면 "보스"로 폴백한다(이름 미저작 보스 방어).</summary>
        public void SetBossName(string bossName)
        {
            if (nameLabel != null)
            {
                nameLabel.text = string.IsNullOrWhiteSpace(bossName) ? "보스" : bossName;
            }
        }

        /// <summary>표기 알파(0 = 숨김, 1 = 완전 표시). 조우 연출이 이징한다.</summary>
        public float Alpha
        {
            get => rootGroup != null ? rootGroup.alpha : 0f;
            set
            {
                if (rootGroup != null)
                {
                    rootGroup.alpha = Mathf.Clamp01(value);
                }
            }
        }
    }
}

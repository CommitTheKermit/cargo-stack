using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CargoStack
{
    /// <summary>
    /// 메인 메뉴. 배경에는 게임플레이 녹화 영상이 흐르고(어두운 필터를 덮어 글씨가 읽히게 한다),
    /// 그 위에 왼쪽 정렬한 미니멀한 스테이지 목록을 얹는다.
    /// 목록은 마우스를 올린 항목 앞에 "&gt;" 커서가 붙고, 클릭하면 그 스테이지로 들어간다.
    /// 배경 영상 재생은 씬 빌더가 붙이는 VideoPlayer 가 맡고, 이 스크립트는 필터와 글자만 그린다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private StageDefinition[] stages =
            Array.Empty<StageDefinition>();
        [SerializeField] private Font titleFont;
        [SerializeField] private Font bodyFont;

        [Tooltip("배경 영상을 덮는 검은 필터의 진하기(0~1). 클수록 어두워져 글씨가 잘 읽힌다.")]
        [SerializeField, Range(0f, 1f)] private float overlayOpacity = 0.55f;

        private GUIStyle titleStyle;
        private GUIStyle taglineStyle;
        private GUIStyle sectionStyle;
        private GUIStyle optionStyle;
        private GUIStyle optionHoverStyle;
        private GUIStyle noteStyle;
        private GUIStyle descriptionStyle;
        private Texture2D overlayTexture;
        private bool isLoading;

        public int StageCount => stages.Length;

        public StageDefinition GetStage(int index)
        {
            return stages[index];
        }

        public void Configure(StageDefinition[] values)
        {
            stages = values ?? throw new ArgumentNullException(nameof(values));
        }

        public void SetFonts(Font title, Font body)
        {
            titleFont = title;
            bodyFont = body;
        }

        public void LoadStage(int index)
        {
            if (isLoading)
            {
                return;
            }

            if (index < 0 || index >= stages.Length || stages[index] == null)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            isLoading = true;
            SceneManager.LoadScene(stages[index].SceneName);
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            EnsureStyles();

            // 배경 영상을 어둡게 덮어 글씨가 읽히게 한다.
            GUI.color = new Color(0f, 0f, 0f, overlayOpacity);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), overlayTexture);
            GUI.color = Color.white;

            float left = Mathf.Max(48f, Screen.width * 0.08f);
            float titleSize = Mathf.Clamp(Screen.height * 0.075f, 34f, 76f);
            float optionSize = Mathf.Clamp(Screen.height * 0.032f, 18f, 34f);

            titleStyle.fontSize = Mathf.RoundToInt(titleSize);
            taglineStyle.fontSize = Mathf.RoundToInt(optionSize * 0.62f);
            sectionStyle.fontSize = Mathf.RoundToInt(optionSize * 0.6f);
            optionStyle.fontSize = Mathf.RoundToInt(optionSize);
            optionHoverStyle.fontSize = optionStyle.fontSize;
            noteStyle.fontSize = Mathf.RoundToInt(optionSize * 0.62f);
            descriptionStyle.fontSize = Mathf.RoundToInt(optionSize * 0.6f);

            float y = Screen.height * 0.16f;

            GUI.Label(new Rect(left, y, Screen.width - left, titleSize * 1.4f),
                "CARGO STACK", titleStyle);
            y += titleSize * 1.35f;
            GUI.Label(new Rect(left, y, Screen.width - left, optionSize),
                "짐을 쌓고, 출발시킨 뒤, 끝까지 지켜보세요.", taglineStyle);

            y = Screen.height * 0.42f;
            GUI.Label(new Rect(left, y, Screen.width - left, sectionStyle.fontSize * 1.6f),
                "SELECT STAGE", sectionStyle);
            y += sectionStyle.fontSize * 2.1f;

            float rowHeight = optionSize * 1.7f;
            for (int index = 0; index < stages.Length; index++)
            {
                StageDefinition stage = stages[index];
                var row = new Rect(left, y, Screen.width - left * 1.5f, rowHeight);
                bool hover = !isLoading && row.Contains(Event.current.mousePosition);

                float cursorWidth = optionSize * 1.1f;
                if (hover)
                {
                    GUI.Label(new Rect(left, y, cursorWidth, rowHeight), ">", optionHoverStyle);
                }

                var labelRect = new Rect(left + cursorWidth, y, row.width - cursorWidth, rowHeight);
                GUI.Label(labelRect, stage.DisplayName, hover ? optionHoverStyle : optionStyle);

                // 첫 스테이지는 추천 표시를 붙인다.
                float nameWidth = (hover ? optionHoverStyle : optionStyle)
                    .CalcSize(new GUIContent(stage.DisplayName)).x;
                if (index == 0)
                {
                    GUI.Label(
                        new Rect(left + cursorWidth + nameWidth + 14f, y, 220f, rowHeight),
                        "(추천)", noteStyle);
                }

                // 마우스를 올린 스테이지의 설명을 그 아래 작게 보여 준다.
                if (hover && !string.IsNullOrEmpty(stage.MenuDescription))
                {
                    GUI.Label(
                        new Rect(left + cursorWidth, y + rowHeight * 0.92f,
                            Screen.width - left * 1.5f, rowHeight),
                        stage.MenuDescription, descriptionStyle);
                }

                if (GUI.Button(row, GUIContent.none, GUIStyle.none) && !isLoading)
                {
                    LoadStage(index);
                }

                y += rowHeight * (hover ? 1.7f : 1.25f);
            }

            if (isLoading)
            {
                GUI.Label(new Rect(left, y + rowHeight, Screen.width - left, rowHeight),
                    "불러오는 중...", noteStyle);
            }
        }

        private void EnsureStyles()
        {
            if (overlayTexture == null)
            {
                overlayTexture = new Texture2D(1, 1);
                overlayTexture.SetPixel(0, 0, Color.white);
                overlayTexture.Apply();
            }

            if (titleStyle != null)
            {
                return;
            }

            var white = new Color(0.96f, 0.96f, 0.96f);
            var dim = new Color(0.70f, 0.72f, 0.74f);
            var faint = new Color(0.55f, 0.57f, 0.60f);

            titleStyle = new GUIStyle
            {
                font = titleFont,
                fontSize = 60,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = white },
            };
            taglineStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = dim },
            };
            sectionStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = faint },
            };
            optionStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = dim },
            };
            optionHoverStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = white },
            };
            noteStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = faint },
            };
            descriptionStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                normal = { textColor = faint },
            };
        }
    }
}

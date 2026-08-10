using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CargoStack
{
    /// <summary>
    /// 메인 메뉴. 배경에는 실시간 적재·주행 데모가 흐르고,
    /// 그 위에 배송 배차표를 닮은 패널과 스테이지 목록을 얹는다.
    /// 목록은 마우스를 올린 항목에 안전색 스트랩이 붙고, 클릭하면 그 스테이지로 들어간다.
    /// 배경 데모는 MenuBackgroundDemo가 맡고, 이 스크립트는 필터와 글자만 그린다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private StageDefinition[] stages =
            Array.Empty<StageDefinition>();
        [SerializeField] private Font titleFont;
        [SerializeField] private Font bodyFont;
        [SerializeField] private RectTransform tutorialBadge;

        [Tooltip("패널 밖 배경 데모에 덮는 검은 필터의 진하기(0~1).")]
        [SerializeField, Range(0f, 1f)] private float overlayOpacity = 0.12f;

        private GUIStyle titleStyle;
        private GUIStyle sectionStyle;
        private GUIStyle optionStyle;
        private GUIStyle optionHoverStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle loadingStyle;
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

        public void SetTutorialBadge(RectTransform badge)
        {
            tutorialBadge = badge;
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

            // 배경 데모를 어둡게 덮어 글씨가 읽히게 한다.
            GUI.color = new Color(0f, 0f, 0f, overlayOpacity);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), overlayTexture);
            GUI.color = Color.white;

            float panelWidth = Mathf.Min(
                Screen.width,
                Mathf.Clamp(Screen.width * 0.34f, 320f, 500f));
            float padding = Mathf.Clamp(panelWidth * 0.09f, 32f, 54f);
            float contentWidth = panelWidth - padding * 2f;
            float titleSize = Mathf.Clamp(Screen.height * 0.07f, 34f, 64f);
            float optionSize = Mathf.Clamp(Screen.height * 0.027f, 17f, 28f);

            DrawFill(new Rect(0f, 0f, panelWidth, Screen.height),
                new Color(0.055f, 0.075f, 0.095f, 0.78f));

            titleStyle.fontSize = Mathf.RoundToInt(titleSize);
            sectionStyle.fontSize = Mathf.RoundToInt(optionSize * 0.6f);
            optionStyle.fontSize = Mathf.RoundToInt(optionSize);
            optionHoverStyle.fontSize = optionStyle.fontSize;
            descriptionStyle.fontSize = Mathf.RoundToInt(optionSize * 0.6f);
            loadingStyle.fontSize = Mathf.RoundToInt(optionSize * 0.7f);

            float y = Screen.height * 0.08f;

            GUI.Label(new Rect(padding, y, contentWidth, titleSize * 2.1f),
                "CARGO\nSTACK", titleStyle);
            y += titleSize * 2.05f;

            y = Mathf.Max(y + optionSize * 2f, Screen.height * 0.37f);
            GUI.Label(new Rect(padding, y, contentWidth, sectionStyle.fontSize * 1.6f),
                "배송 경로 선택", sectionStyle);
            y += sectionStyle.fontSize * 2.1f;

            float rowHeight = optionSize * 1.62f;
            int highlightedIndex = 0;
            for (int index = 0; index < stages.Length; index++)
            {
                StageDefinition stage = stages[index];
                var row = new Rect(padding, y, contentWidth, rowHeight);
                bool hover = !isLoading && row.Contains(Event.current.mousePosition);

                if (hover)
                {
                    highlightedIndex = index;
                    DrawFill(row, new Color(0.18f, 0.45f, 0.78f, 0.28f));
                    DrawFill(new Rect(row.x, row.y, 5f, row.height),
                        new Color(0.95f, 0.71f, 0.2f));
                }

                var labelRect = new Rect(row.x + 18f, row.y, row.width - 18f, row.height);
                GUI.Label(labelRect, stage.DisplayName, hover ? optionHoverStyle : optionStyle);

                if (index == 0)
                {
                    PositionTutorialBadge(row);
                }

                if (GUI.Button(row, GUIContent.none, GUIStyle.none) && !isLoading)
                {
                    LoadStage(index);
                }

                DrawFill(new Rect(row.x, row.yMax - 1f, row.width, 1f),
                    new Color(1f, 1f, 1f, 0.09f));
                y += rowHeight;
            }

            StageDefinition highlighted = stages[highlightedIndex];
            if (!string.IsNullOrEmpty(highlighted.MenuDescription))
            {
                GUI.Label(new Rect(padding + 18f, y + optionSize * 0.7f,
                    contentWidth - 18f, optionSize * 2.8f),
                    highlighted.MenuDescription, descriptionStyle);
            }

            if (isLoading)
            {
                DrawFill(new Rect(0f, 0f, panelWidth, Screen.height),
                    new Color(0.055f, 0.075f, 0.095f, 0.82f));
                GUI.Label(new Rect(padding, Screen.height * 0.48f, contentWidth, rowHeight),
                    "배송 준비 중...", loadingStyle);
            }
        }

        private void DrawFill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, overlayTexture);
            GUI.color = Color.white;
        }

        private void PositionTutorialBadge(Rect row)
        {
            if (tutorialBadge == null)
            {
                return;
            }

            float height = Mathf.Min(30f, row.height - 8f);
            tutorialBadge.sizeDelta = new Vector2(height * 3f, height);
            tutorialBadge.anchoredPosition = new Vector2(row.xMax - 4f, -(row.center.y));
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

            var white = new Color(0.96f, 0.98f, 0.98f);
            var dim = new Color(0.77f, 0.81f, 0.84f);
            var faint = new Color(0.57f, 0.64f, 0.68f);
            var yellow = new Color(0.95f, 0.71f, 0.2f);

            titleStyle = new GUIStyle
            {
                font = titleFont,
                fontSize = 60,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = white },
            };
            sectionStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = yellow },
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
            descriptionStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                normal = { textColor = faint },
            };
            loadingStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = white },
            };
        }
    }
}

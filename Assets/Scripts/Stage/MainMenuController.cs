using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CargoStack
{
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private StageDefinition[] stages =
            Array.Empty<StageDefinition>();

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle stageTitleStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle buttonStyle;
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

            float width = Mathf.Min(680f, Screen.width - 40f);
            float height = Mathf.Min(
                Screen.height - 40f,
                190f + stages.Length * 116f);
            var area = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);

            GUILayout.BeginArea(area, GUI.skin.window);
            GUILayout.Space(18f);
            GUILayout.Label("CARGO STACK", titleStyle);
            GUILayout.Label(
                "짐을 쌓고, 출발시킨 뒤, 끝까지 지켜보세요.",
                subtitleStyle);
            GUILayout.Space(20f);

            for (int index = 0; index < stages.Length; index++)
            {
                StageDefinition stage = stages[index];
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(stage.DisplayName, stageTitleStyle);
                GUILayout.Label(stage.MenuDescription, descriptionStyle);
                GUI.enabled = !isLoading;
                if (GUILayout.Button(
                        isLoading ? "불러오는 중..." : "이 스테이지 시작",
                        buttonStyle,
                        GUILayout.Height(34f)))
                {
                    LoadStage(index);
                }

                GUI.enabled = true;
                GUILayout.EndVertical();
                GUILayout.Space(8f);
            }

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 34,
                fontStyle = FontStyle.Bold,
            };
            subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
            };
            stageTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
            };
            descriptionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true,
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
            };
        }
    }
}

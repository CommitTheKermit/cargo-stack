using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CargoStack
{
    /// <summary>실시간 배송 데모 위의 이미지 기반 스테이지 선택 메뉴.</summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private StageDefinition[] stages =
            Array.Empty<StageDefinition>();
        [SerializeField] private Button[] stageButtons = Array.Empty<Button>();
        [SerializeField] private Text descriptionText;
        [SerializeField] private GameObject loadingPanel;

        private bool isLoading;

        public int StageCount => stages.Length;
        public int ButtonCount => stageButtons.Length;

        public StageDefinition GetStage(int index)
        {
            return stages[index];
        }

        public void Configure(StageDefinition[] values)
        {
            stages = values ?? throw new ArgumentNullException(nameof(values));
        }

        public void ConfigureUi(
            Button[] buttons,
            Text description,
            GameObject loading)
        {
            stageButtons = buttons ?? throw new ArgumentNullException(nameof(buttons));
            descriptionText = description;
            loadingPanel = loading;
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
            loadingPanel.SetActive(true);
            foreach (Button button in stageButtons)
            {
                button.interactable = false;
            }

            SceneManager.LoadScene(stages[index].SceneName);
        }

        private void Start()
        {
            if (stageButtons.Length != stages.Length)
            {
                throw new InvalidOperationException("스테이지와 메뉴 버튼 수가 다르다.");
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ShowDescription(0);

            for (int index = 0; index < stageButtons.Length; index++)
            {
                int stageIndex = index;
                stageButtons[index].onClick.AddListener(() => LoadStage(stageIndex));
            }
        }

        private void Update()
        {
            if (isLoading)
            {
                return;
            }

            for (int index = 0; index < stageButtons.Length; index++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(
                        stageButtons[index].transform as RectTransform,
                        Input.mousePosition))
                {
                    ShowDescription(index);
                    return;
                }
            }
        }

        private void ShowDescription(int index)
        {
            descriptionText.text = stages[index].MenuDescription;
        }
    }
}

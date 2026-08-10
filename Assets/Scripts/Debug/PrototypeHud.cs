using UnityEngine;

namespace CargoStack
{
    /// <summary>스테이지 공통 단축키와 이미지 기반 키 안내를 관리한다.</summary>
    public class PrototypeHud : MonoBehaviour
    {
        [SerializeField] private GameFlow flow;
        [SerializeField] private GameObject loadingGuide;
        [SerializeField] private GameObject drivingGuide;
        [SerializeField] private bool showUi = true;

        public Font UiFont => Resources.Load<Font>("Pretendard-Regular");
        public bool IsUiVisible => showUi;
        public bool IsLoadingGuideVisible => loadingGuide != null && loadingGuide.activeSelf;
        public bool IsDrivingGuideVisible => drivingGuide != null && drivingGuide.activeSelf;

        public void ConfigureUi(GameObject loading, GameObject driving)
        {
            loadingGuide = loading;
            drivingGuide = driving;
            RefreshGuide();
        }

        public void SetUiVisible(bool visible)
        {
            showUi = visible;
            RefreshGuide();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                flow.StartDriving();
            }

            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                flow.Restart();
            }

            RefreshGuide();
        }

        private void RefreshGuide()
        {
            if (loadingGuide != null)
            {
                loadingGuide.SetActive(showUi && flow.State == GameState.Loading);
            }

            if (drivingGuide != null)
            {
                drivingGuide.SetActive(showUi && flow.State == GameState.Driving);
            }
        }
    }
}

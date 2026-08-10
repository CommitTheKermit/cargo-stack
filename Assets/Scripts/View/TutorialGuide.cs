using UnityEngine;
using UnityEngine.UI;

namespace CargoStack
{
    /// <summary>Stage 01에서 조작을 막지 않고 성공한 행동만 체크하는 이미지 튜토리얼.</summary>
    [DisallowMultipleComponent]
    public sealed class TutorialGuide : MonoBehaviour
    {
        [SerializeField] private GameFlow flow;
        [SerializeField] private PlayerCargoInteractor cargoInteractor;
        [SerializeField] private PlayerRopeInteractor ropeInteractor;
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private GameObject drivingPanel;
        [SerializeField] private Image[] loadingChecks;
        [SerializeField] private Image[] loadingProgress;
        [SerializeField] private Image[] drivingChecks;
        [SerializeField] private Image[] drivingProgress;

        private bool wasHoldingCargo;

        public int LoadingCompletedCount => CountEnabled(loadingChecks);
        public int DrivingCompletedCount => CountEnabled(drivingChecks);
        public bool IsLoadingPanelVisible => loadingPanel != null && loadingPanel.activeSelf;
        public bool IsDrivingPanelVisible => drivingPanel != null && drivingPanel.activeSelf;

        public void Configure(
            GameFlow gameFlow,
            PlayerCargoInteractor cargo,
            PlayerRopeInteractor rope,
            GameObject loading,
            GameObject driving,
            Image[] loadingStepChecks,
            Image[] loadingProgressChecks,
            Image[] drivingStepChecks,
            Image[] drivingProgressChecks)
        {
            flow = gameFlow;
            cargoInteractor = cargo;
            ropeInteractor = rope;
            loadingPanel = loading;
            drivingPanel = driving;
            loadingChecks = loadingStepChecks;
            loadingProgress = loadingProgressChecks;
            drivingChecks = drivingStepChecks;
            drivingProgress = drivingProgressChecks;
        }

        private void OnEnable()
        {
            if (flow != null)
            {
                flow.StateChanged += HandleStateChanged;
            }
        }

        private void Start()
        {
            wasHoldingCargo = cargoInteractor.HasCargo;
            HandleStateChanged(flow.State);
        }

        private void OnDisable()
        {
            if (flow != null)
            {
                flow.StateChanged -= HandleStateChanged;
            }
        }

        private void Update()
        {
            if (flow.State == GameState.Loading)
            {
                TrackLoading();
            }
            else if (flow.State == GameState.Driving)
            {
                TrackDriving();
            }
        }

        private void TrackLoading()
        {
            if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f
                || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f)
            {
                Complete(loadingChecks, loadingProgress, 0);
            }

            bool isHoldingCargo = cargoInteractor.HasCargo;
            if (isHoldingCargo)
            {
                Complete(loadingChecks, loadingProgress, 1);
            }

            if (wasHoldingCargo && !isHoldingCargo)
            {
                Complete(loadingChecks, loadingProgress, 2);
            }

            if (ropeInteractor.TiedRopeCount > 0)
            {
                Complete(loadingChecks, loadingProgress, 3);
            }

            wasHoldingCargo = isHoldingCargo;
        }

        private void TrackDriving()
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                Complete(drivingChecks, drivingProgress, 0);
            }

            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            {
                Complete(drivingChecks, drivingProgress, 1);
            }

            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D)
                || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow))
            {
                Complete(drivingChecks, drivingProgress, 2);
            }

            if (Input.GetMouseButton(0))
            {
                Complete(drivingChecks, drivingProgress, 3);
            }

            if (Mathf.Abs(Input.GetAxisRaw("Mouse ScrollWheel")) > 0.0001f)
            {
                Complete(drivingChecks, drivingProgress, 4);
            }
        }

        private void HandleStateChanged(GameState state)
        {
            bool isLoading = state == GameState.Loading;
            bool isDriving = state == GameState.Driving;
            loadingPanel.SetActive(isLoading);
            drivingPanel.SetActive(isDriving);

            if (isDriving)
            {
                Complete(loadingChecks, loadingProgress, 4);
            }
        }

        private static void Complete(Image[] checks, Image[] progress, int index)
        {
            if (index < checks.Length)
            {
                checks[index].enabled = true;
            }

            int completed = CountEnabled(checks);
            for (int marker = 0; marker < progress.Length; marker++)
            {
                progress[marker].enabled = marker < completed;
            }
        }

        private static int CountEnabled(Image[] images)
        {
            int count = 0;
            foreach (Image image in images)
            {
                count += image != null && image.enabled ? 1 : 0;
            }

            return count;
        }
    }
}

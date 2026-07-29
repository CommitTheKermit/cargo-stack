using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CargoStack
{
    /// <summary>
    /// 게임의 등뼈. 적재 → 주행 → 결과 상태를 소유하고 각 시스템에 전환을 통지한다.
    /// 기획서 5장에서 합의한 팀 인터페이스 세 지점(적재 완료 / 짐 낙하 / 트럭 도착)이
    /// 모두 이 클래스를 거치므로, 시스템끼리 서로를 직접 참조하지 않는다.
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        [SerializeField] private TruckMover truck;
        [SerializeField] private CargoTracker tracker;
        [SerializeField] private CameraDirector cameraDirector;

        [Tooltip("적재를 담당하는 1인칭 플레이어. 출발하면 통째로 비활성화된다.")]
        [SerializeField] private GameObject player;

        public GameState State { get; private set; } = GameState.Loading;

        public event Action<GameState> StateChanged;

        private void OnEnable()
        {
            truck.Arrived += HandleTruckArrived;
        }

        private void OnDisable()
        {
            truck.Arrived -= HandleTruckArrived;
        }

        private void Start()
        {
            SetState(GameState.Loading);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ReturnToMainMenu();
            }
        }

        /// <summary>적재를 마치고 출발시킨다. 적재 단계에서만 유효하다.</summary>
        public void StartDriving()
        {
            if (State != GameState.Loading)
            {
                return;
            }

            SetState(GameState.Driving);
        }

        public void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void ReturnToMainMenu()
        {
            SceneManager.LoadScene("MainMenu");
        }

        private void HandleTruckArrived()
        {
            if (State != GameState.Driving)
            {
                return;
            }

            SetState(GameState.Result);
        }

        private void SetState(GameState next)
        {
            State = next;

            // 플레이어를 먼저 치운다. 화물을 들고 있었다면 여기서 그 자리에 떨어진다.
            if (next != GameState.Loading && player != null && player.activeSelf)
            {
                player.SetActive(false);
            }

            cameraDirector.Frame(next);

            if (next == GameState.Driving)
            {
                tracker.BeginWatch();
                truck.BeginDrive();
            }

            StateChanged?.Invoke(next);
        }
    }
}

using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 검증용 임시 HUD. 정식 UI 가 나오기 전까지 상태 확인과 물리 튜닝을 담당한다.
    /// 마찰 슬라이더는 PhysicsMaterial 에셋을 직접 고치므로 플레이를 멈춰도 값이 남는다(의도한 동작).
    /// 기획서 4.1: 마찰 튜닝이 이 게임 재미의 8할이라 1주차부터 조절 수단을 갖춰 둔다.
    ///
    /// 적재 중에는 1인칭 시점이라 커서가 잠겨 있다. Tab 으로 커서를 풀어야 슬라이더를 만질 수 있고,
    /// 화면을 다시 클릭하면 시점 조작으로 돌아간다.
    /// </summary>
    public class PrototypeHud : MonoBehaviour
    {
        [SerializeField] private GameFlow flow;
        [SerializeField] private CargoTracker tracker;
        [SerializeField] private PhysicsMaterial bedMaterial;
        [SerializeField] private PhysicsMaterial cargoMaterial;
        [SerializeField] private PlayerRopeInteractor ropeInteractor;

        private GUIStyle labelStyle;

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
        }

        private void OnGUI()
        {
            labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 15 };

            GUILayout.BeginArea(new Rect(16f, 16f, 340f, 330f), GUI.skin.box);

            GUILayout.Label($"상태: {DescribeState()}", labelStyle);
            GUILayout.Label($"짐칸에 남은 짐: {tracker.RemainingCount} / {tracker.TotalCount}", labelStyle);
            if (ropeInteractor != null)
            {
                GUILayout.Label($"남은 로프: {ropeInteractor.RemainingRopes}", labelStyle);
            }

            GUILayout.Space(6f);

            if (flow.State == GameState.Loading)
            {
                GUILayout.Label("WASD 이동   마우스 시점   Space 점프", labelStyle);
                GUILayout.Label("E 집기·놓기·뒷문   Q 회전   F 던지기", labelStyle);
                GUILayout.Label("R 로프 걸기   X 로프 걷기", labelStyle);
                GUILayout.Label("Enter 출발   Backspace 재시작", labelStyle);
                GUILayout.Label("Esc 스테이지 선택   Tab 커서 풀기", labelStyle);

                if (ropeInteractor != null && ropeInteractor.IsTyingKnot)
                {
                    GUILayout.Label("반대쪽 끝을 조준하고 R (X 로 취소)", labelStyle);
                }
            }
            else
            {
                GUILayout.Label("좌클릭 드래그 시점 회전   휠 확대·축소", labelStyle);
                GUILayout.Label("Backspace 재시작   Esc 스테이지 선택", labelStyle);
            }

            GUILayout.Space(10f);

            GUILayout.Label("짐칸 마찰", labelStyle);
            DrawFrictionSliders(bedMaterial);

            GUILayout.Label("짐 마찰", labelStyle);
            DrawFrictionSliders(cargoMaterial);

            // 결과 화면의 버튼은 ResultScreen 이 그린다. 여기 또 두면 같은 조작이 두 곳에 생긴다.

            GUILayout.EndArea();
        }

        private void DrawFrictionSliders(PhysicsMaterial material)
        {
            material.dynamicFriction = DrawSlider("동", material.dynamicFriction);
            material.staticFriction = DrawSlider("정", material.staticFriction);
        }

        private float DrawSlider(string caption, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{caption} {value:0.00}", labelStyle, GUILayout.Width(58f));
            float next = GUILayout.HorizontalSlider(value, 0f, 1.5f);
            GUILayout.EndHorizontal();
            return next;
        }

        private string DescribeState()
        {
            return flow.State switch
            {
                GameState.Loading => "적재 (짐을 쌓고 Enter)",
                GameState.Driving => "주행 중",
                GameState.Result => $"도착 - 별 {tracker.StarRating}/3 ({tracker.DroppedCount}개 분실)",
                _ => flow.State.ToString(),
            };
        }
    }
}

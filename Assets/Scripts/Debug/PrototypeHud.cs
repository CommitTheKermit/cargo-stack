using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 검증용 임시 HUD. 정식 UI 가 나오기 전까지 상태 확인과 물리 튜닝을 담당한다.
    /// 마찰 슬라이더는 PhysicsMaterial 에셋을 직접 고치므로 플레이를 멈춰도 값이 남는다(의도한 동작).
    /// 기획서 4.1: 마찰 튜닝이 이 게임 재미의 8할이라 1주차부터 조절 수단을 갖춰 둔다.
    /// </summary>
    public class PrototypeHud : MonoBehaviour
    {
        [SerializeField] private GameFlow flow;
        [SerializeField] private CargoTracker tracker;
        [SerializeField] private PhysicsMaterial bedMaterial;
        [SerializeField] private PhysicsMaterial cargoMaterial;

        private GUIStyle labelStyle;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
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

            GUILayout.BeginArea(new Rect(16f, 16f, 320f, 260f), GUI.skin.box);

            GUILayout.Label($"상태: {DescribeState()}", labelStyle);
            GUILayout.Label($"남은 짐: {tracker.RemainingCount} / {tracker.TotalCount}", labelStyle);
            GUILayout.Space(6f);

            GUILayout.Label("좌클릭 집기·놓기   R 회전", labelStyle);
            GUILayout.Label("Space 출발   Backspace 재시작", labelStyle);
            GUILayout.Space(10f);

            GUILayout.Label("짐칸 마찰", labelStyle);
            DrawFrictionSliders(bedMaterial);

            GUILayout.Label("짐 마찰", labelStyle);
            DrawFrictionSliders(cargoMaterial);

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
                GameState.Loading => "적재 (짐을 쌓고 Space)",
                GameState.Driving => "주행 중",
                GameState.Result => tracker.DroppedCount == 0 ? "도착 - 완주!" : $"도착 - {tracker.DroppedCount}개 분실",
                _ => flow.State.ToString(),
            };
        }
    }
}

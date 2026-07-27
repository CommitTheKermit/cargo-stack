using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 단계에 따라 어느 카메라가 화면을 그릴지 정한다.
    /// 적재는 1인칭, 주행부터는 비스듬한 디오라마 시점이다.
    ///
    /// GameObject 를 껐다 켜지 않고 컴포넌트만 토글한다.
    /// 디오라마 카메라는 꺼져 있는 동안에도 트럭을 계속 따라다녀야 전환 순간 화면이 튀지 않고,
    /// 1인칭 카메라의 AudioListener 도 계속 살아 있어야 리스너 경고가 나지 않는다.
    /// </summary>
    public sealed class CameraDirector : MonoBehaviour
    {
        [SerializeField] private Camera firstPersonCamera;
        [SerializeField] private FirstPersonCamera firstPersonLook;
        [SerializeField] private Camera dioramaCamera;

        public void Frame(GameState state)
        {
            bool loading = state == GameState.Loading;

            firstPersonCamera.enabled = loading;
            dioramaCamera.enabled = !loading;

            // 시점 조작과 조준점 그리기를 같이 멈춘다. 켜질 때 커서는 스스로 다시 잠근다.
            if (firstPersonLook != null)
            {
                firstPersonLook.enabled = loading;
            }

            if (!loading)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}

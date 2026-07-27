using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 주행 단계의 관전 카메라. 폴리브릿지의 시뮬레이션 화면과 같은 방식이다.
    /// 기본은 대각선 위에서 비스듬히 내려다보는 쿼터뷰이고, 마우스로 자유롭게 돌려
    /// 탑뷰나 사이드뷰로 옮겨 가며 짐이 무너지는 과정을 원하는 각도에서 볼 수 있다.
    ///
    /// 트럭을 축으로 도는 구면 좌표(yaw·pitch·거리)로 다룬다.
    /// 위치를 직접 보간하지 않고 각도만 다루므로 트럭이 어디에 있든 구도가 일정하다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class DioramaCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        [Tooltip("트럭 원점보다 살짝 위를 축으로 삼아 짐칸이 화면 가운데 오게 한다.")]
        [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 0.8f, 0f);

        [Header("기본 구도 (쿼터뷰)")]
        [SerializeField] private float defaultYaw = 35f;
        [SerializeField] private float defaultPitch = 38f;
        [SerializeField] private float defaultDistance = 16f;

        [Header("마우스 조작")]
        [Tooltip("좌클릭 드래그로 회전.")]
        [SerializeField] private float orbitSensitivity = 3f;

        [Tooltip("휠로 확대·축소. 거리에 비례해 움직여 멀리서는 크게, 가까이서는 곱게 조절된다.")]
        [SerializeField] private float zoomSensitivity = 4f;

        [Header("한계")]
        [Tooltip("지면 아래로 내려가지 않도록 아래를, 완전한 탑뷰까지 갈 수 있도록 위를 잡는다.")]
        [SerializeField] private float minPitch = 5f;

        [SerializeField] private float maxPitch = 85f;
        [SerializeField] private float minDistance = 6f;
        [SerializeField] private float maxDistance = 40f;
        [SerializeField] private float followSmoothing = 4f;

        private Camera view;
        private float yaw;
        private float pitch;
        private float distance;
        private Vector3 pivot;

        private void Awake()
        {
            view = GetComponent<Camera>();
            ResetFraming();
        }

        /// <summary>
        /// 기본 쿼터뷰로 되돌리고 즉시 그 자리에 놓는다.
        /// 씬 빌더도 에디터에서 구도를 확인할 수 있도록 이걸 부른다.
        /// </summary>
        public void ResetFraming()
        {
            SetFraming(defaultYaw, defaultPitch, defaultDistance);
        }

        /// <summary>
        /// 특정 각도로 즉시 옮긴다. 프리뷰 캡처가 쓰고,
        /// 나중에 "탑뷰" 같은 시점 프리셋 버튼을 붙일 때도 이 진입점을 쓴다.
        /// </summary>
        public void SetFraming(float newYaw, float newPitch, float newDistance)
        {
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
            distance = Mathf.Clamp(newDistance, minDistance, maxDistance);
            pivot = GetPivot();
            ApplyPose();
        }

        private void Update()
        {
            // 적재 중에는 화면을 그리지 않으므로 조작도 받지 않는다.
            if (view == null || !view.enabled)
            {
                return;
            }

            ReadOrbitInput();
        }

        private void LateUpdate()
        {
            float t = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
            pivot = Vector3.Lerp(pivot, GetPivot(), t);
            ApplyPose();
        }

        private void ReadOrbitInput()
        {
            // HUD 슬라이더를 끌고 있을 때는 시점이 같이 돌지 않게 한다.
            if (GUIUtility.hotControl != 0)
            {
                return;
            }

            if (Input.GetMouseButton(0))
            {
                yaw += Input.GetAxisRaw("Mouse X") * orbitSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * orbitSensitivity, minPitch, maxPitch);
            }

            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                distance = Mathf.Clamp(distance - scroll * zoomSensitivity * distance, minDistance, maxDistance);
            }
        }

        private void ApplyPose()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
        }

        private Vector3 GetPivot()
        {
            return target == null ? pivot : target.position + pivotOffset;
        }
    }
}

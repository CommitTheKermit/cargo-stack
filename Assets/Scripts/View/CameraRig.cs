using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 폴리브릿지식 카메라 전환(기획서 1장). 적재 중에는 짐칸을 확대해 보고,
    /// 출발하면 뒤로 빠지며 경로가 보이는 측면 와이드 뷰가 된다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        [SerializeField] private Transform truck;
        [SerializeField] private Transform bedAnchor;

        [Header("적재 - 짐칸 확대")]
        [SerializeField] private Vector3 loadingOffset = new Vector3(-1.5f, 0.8f, -20f);
        [SerializeField] private float loadingSize = 3.6f;

        [Header("주행 - 측면 와이드")]
        [SerializeField] private Vector3 drivingOffset = new Vector3(3f, 2.5f, -20f);
        [SerializeField] private float drivingSize = 9f;

        [SerializeField] private float smoothing = 4f;

        private Camera view;
        private GameState framing = GameState.Loading;

        private void Awake()
        {
            view = GetComponent<Camera>();
            view.orthographic = true;

            GetTargetFraming(out Vector3 position, out float size);
            transform.position = position;
            view.orthographicSize = size;
        }

        public void Frame(GameState state)
        {
            framing = state;
        }

        private void LateUpdate()
        {
            GetTargetFraming(out Vector3 position, out float size);

            // 프레임률과 무관한 지수 감쇠. Lerp 를 그대로 쓰면 프레임률에 따라 속도가 달라진다.
            float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);

            transform.position = Vector3.Lerp(transform.position, position, t);
            view.orthographicSize = Mathf.Lerp(view.orthographicSize, size, t);
        }

        private void GetTargetFraming(out Vector3 position, out float size)
        {
            if (framing == GameState.Loading)
            {
                position = bedAnchor.position + loadingOffset;
                size = loadingSize;
                return;
            }

            position = new Vector3(truck.position.x, truck.position.y, 0f) + drivingOffset;
            size = drivingSize;
        }
    }
}

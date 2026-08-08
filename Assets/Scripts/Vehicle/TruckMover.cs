using System;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 정해진 경로를 자동 주행하는 트럭. 플레이어는 운전하지 않는다.
    /// 바퀴 물리 대신 <see cref="RoutePath"/> 위를 미끄러지는 키네마틱 차체를 쓴다(기획서 4.1).
    /// 짐은 짐칸 콜라이더와의 마찰로만 실려 가므로, 여기서 만들어내는 가속·감속·경사·커브가 곧 난이도다.
    ///
    /// 경로가 곧 도로 표면이라 지면을 레이캐스트로 찾지 않는다. 도로 블록은 중심선 아래로 매달아
    /// 깔기 때문에 블록 이음매마다 몇 cm 씩 생기는 톱니를 트럭이 그대로 밟는 문제가 사라진다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TruckMover : MonoBehaviour
    {
        [Header("경로")]
        [SerializeField] private RoutePath path;

        [Tooltip("출발선의 경로상 거리.")]
        [SerializeField] private float startDistance;

        [Tooltip("이 거리를 지나면 도착으로 판정한다.")]
        [SerializeField] private float goalDistance = 60f;

        [Header("속도 프로필")]
        [SerializeField] private float maxSpeed = 10f;

        [Tooltip("주행 진행도(0~1)에 대한 속도 배율. 급제동 구간은 이 커브의 골짜기로 만든다.")]
        [SerializeField] private AnimationCurve speedOverProgress = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Tooltip("최저 속도 배율. 0 이면 트럭이 영영 멈춰 설 수 있으므로 반드시 0보다 커야 한다.")]
        [SerializeField] private float minSpeedFactor = 0.06f;

        [Header("차체")]
        [SerializeField] private float frontAxleOffset = 1.75f;
        [SerializeField] private float rearAxleOffset = -1.75f;

        [Tooltip("도로 표면에서 차체 원점까지의 높이.")]
        [SerializeField] private float rideHeight = 0.75f;

        private Rigidbody body;
        private float travelled;
        private bool isDriving;

        /// <summary>도착 지점 통과. GameFlow 가 결과 단계로 넘어가는 신호다.</summary>
        public event Action Arrived;

        public float Speed { get; private set; }

        /// <summary>최고 속도 대비 현재 속도. 시각·음향 피드백에서 사용한다.</summary>
        public float Speed01 => maxSpeed > 0f ? Mathf.Clamp01(Speed / maxSpeed) : 0f;

        /// <summary>주행 진행도 0~1. HUD 와 속도 프로필이 같은 값을 본다.</summary>
        public float Progress => Mathf.InverseLerp(startDistance, goalDistance, travelled);

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            travelled = startDistance;
        }

        public void BeginDrive()
        {
            isDriving = true;
        }

        /// <summary>씬 빌더가 출발 자세를 잡을 때 쓴다. 에디터에서도 경로 위에 정확히 올려 둔다.</summary>
        public void SnapToStart()
        {
            travelled = startDistance;
            GetPoseAt(travelled, out Vector3 position, out Quaternion rotation);
            transform.SetPositionAndRotation(position, rotation);
        }

        private void FixedUpdate()
        {
            if (!isDriving)
            {
                return;
            }

            // 바닥을 0 이 아니라 minSpeedFactor 로 잡는다. 급제동 골짜기를 좁고 깊게 파면
            // 커브를 부드럽게 이을 때 값이 음수까지 내려가는데, 그걸 0 으로 자르면
            // 트럭이 더 이상 나아가지 못해 그 자리에 영영 서 버린다.
            float factor = Mathf.Max(minSpeedFactor, speedOverProgress.Evaluate(Progress));
            Speed = maxSpeed * factor;

            travelled += Speed * Time.fixedDeltaTime;

            bool reachedGoal = travelled >= goalDistance;
            if (reachedGoal)
            {
                travelled = goalDistance;
                isDriving = false;
                Speed = 0f;
            }

            GetPoseAt(travelled, out Vector3 position, out Quaternion rotation);
            body.MovePosition(position);
            body.MoveRotation(rotation);

            if (reachedGoal)
            {
                Arrived?.Invoke();
            }
        }

        /// <summary>앞뒤 축이 짚는 두 지점을 이어 차체의 기울기와 방향을 함께 얻는다.</summary>
        private void GetPoseAt(float distance, out Vector3 position, out Quaternion rotation)
        {
            Vector3 front = path.PositionAt(distance + frontAxleOffset);
            Vector3 rear = path.PositionAt(distance + rearAxleOffset);

            Vector3 heading = front - rear;
            if (heading.sqrMagnitude < 1e-6f)
            {
                heading = transform.right;
            }

            // 차체 모델의 앞이 로컬 +X 라서, +Z 를 겨누는 LookRotation 을 90도 돌려 맞춘다.
            rotation = Quaternion.LookRotation(heading.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);

            // 차체 높이는 도로 면의 법선 방향으로 띄운다. 경사에서 차체가 파묻히지 않게 한다.
            position = (front + rear) * 0.5f + rotation * Vector3.up * rideHeight;
        }
    }
}

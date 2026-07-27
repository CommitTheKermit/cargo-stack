using System;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 정해진 경로를 자동 주행하는 트럭. 플레이어는 운전하지 않는다.
    /// 바퀴 물리 대신 지면을 레이캐스트로 훑어 따라가는 키네마틱 차체를 쓴다(기획서 4.1).
    /// 짐은 짐칸 콜라이더와의 마찰로만 실려 가므로, 여기서 만들어내는 가속·감속·경사가 곧 난이도다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TruckMover : MonoBehaviour
    {
        [Header("경로")]
        [Tooltip("이 X 좌표에 닿으면 도착으로 판정한다.")]
        [SerializeField] private float goalX = 60f;

        [Header("속도 프로필")]
        [SerializeField] private float maxSpeed = 7f;

        [Tooltip("주행 진행도(0~1)에 대한 속도 배율. 급제동 구간은 이 커브의 골짜기로 만든다.")]
        [SerializeField] private AnimationCurve speedOverProgress = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Tooltip("최저 속도 배율. 0 이면 트럭이 영영 멈춰 설 수 있으므로 반드시 0보다 커야 한다.")]
        [SerializeField] private float minSpeedFactor = 0.06f;

        [Header("지면 추종")]
        [SerializeField] private float frontAxleOffset = 1.5f;
        [SerializeField] private float rearAxleOffset = -1.5f;

        [Tooltip("지면 접점에서 차체 원점까지의 높이.")]
        [SerializeField] private float rideHeight = 0.6f;

        [SerializeField] private LayerMask groundMask;

        private Rigidbody body;
        private float startX;
        private bool isDriving;

        /// <summary>도착 지점 통과. GameFlow 가 결과 단계로 넘어가는 신호다.</summary>
        public event Action Arrived;

        public float Speed { get; private set; }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            startX = transform.position.x;
        }

        public void BeginDrive()
        {
            isDriving = true;
        }

        private void FixedUpdate()
        {
            if (!isDriving)
            {
                return;
            }

            float progress = Mathf.InverseLerp(startX, goalX, transform.position.x);

            // 바닥을 0 이 아니라 minSpeedFactor 로 잡는다. 급제동 골짜기를 좁고 깊게 파면
            // 커브를 부드럽게 이을 때 값이 음수까지 내려가는데, 그걸 0 으로 자르면
            // 진행도가 더 이상 늘지 않아 트럭이 그 자리에 영영 서 버린다.
            float factor = Mathf.Max(minSpeedFactor, speedOverProgress.Evaluate(progress));
            Speed = maxSpeed * factor;

            float nextX = transform.position.x + Speed * Time.fixedDeltaTime;
            bool reachedGoal = nextX >= goalX;
            if (reachedGoal)
            {
                nextX = goalX;
                isDriving = false;
                Speed = 0f;
            }

            ApplyGroundedPose(nextX);

            if (reachedGoal)
            {
                Arrived?.Invoke();
            }
        }

        /// <summary>앞뒤 축 아래 지면을 각각 찍어 차체 높이와 기울기를 맞춘다.</summary>
        private void ApplyGroundedPose(float nextX)
        {
            bool hasFront = TrySampleGroundHeight(nextX + frontAxleOffset, out float frontY);
            bool hasRear = TrySampleGroundHeight(nextX + rearAxleOffset, out float rearY);

            if (!hasFront || !hasRear)
            {
                body.MovePosition(new Vector3(nextX, transform.position.y, 0f));
                return;
            }

            float centerY = (frontY + rearY) * 0.5f + rideHeight;
            float slopeDegrees = Mathf.Atan2(frontY - rearY, frontAxleOffset - rearAxleOffset) * Mathf.Rad2Deg;

            body.MovePosition(new Vector3(nextX, centerY, 0f));
            body.MoveRotation(Quaternion.Euler(0f, 0f, slopeDegrees));
        }

        private bool TrySampleGroundHeight(float x, out float y)
        {
            var origin = new Vector3(x, transform.position.y + 6f, 0f);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 40f, groundMask, QueryTriggerInteraction.Ignore))
            {
                y = hit.point.y;
                return true;
            }

            y = 0f;
            return false;
        }
    }
}

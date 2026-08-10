using System;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 플레이어가 전진·후진·조향으로 직접 운전하는 트럭. 앞바퀴 조향각과 휠베이스로
    /// 차체의 회전 반경을 구하고, 실제 이동 방향은 바퀴 아래
    /// <see cref="PhysicsMaterial.dynamicFriction"/>으로 제한되는 횡접지력이 정한다.
    /// 얼음에서 코너를 돌면 기존 속도의 관성이 남아 코너 바깥으로 밀리고, 직선이나 고마찰
    /// 노면에서는 목표 진행 방향을 바로 따라간다.
    ///
    /// 차체는 화물을 안정적으로 운반하는 키네마틱 플랫폼으로 유지한다. 대신 매 물리 프레임에
    /// 속도 벡터를 적분하고, 노면 마찰로 가능한 만큼만 그 방향을 조향 방향으로 돌린다.
    /// 위치 오프셋 커브로 차체를 옆으로 이동시키는 연출은 사용하지 않는다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TruckMover : MonoBehaviour
    {
        private const float Gravity = 9.81f;

        [Header("경로")]
        [SerializeField] private RoutePath path;
        [SerializeField] private float startDistance;
        [SerializeField] private float goalDistance = 60f;

        [Header("주행 조작")]
        [SerializeField] private float maxSpeed = 10f;

        [Tooltip("엑셀을 끝까지 밟았을 때 초당 증가하는 속도.")]
        [SerializeField, Min(0f)] private float acceleration = 4.5f;

        [Tooltip("진행 반대 방향 키를 누를 때 정지할 때까지 적용되는 제동 감속도.")]
        [SerializeField, Min(0f)] private float brakeDeceleration = 8f;

        [Tooltip("후진 최고 속도.")]
        [SerializeField, Min(0f)] private float maxReverseSpeed = 4f;

        [Tooltip("후진 키를 끝까지 눌렀을 때 초당 증가하는 후진 속도.")]
        [SerializeField, Min(0f)] private float reverseAcceleration = 3.5f;

        [Tooltip("아무 페달도 밟지 않았을 때 구름 저항으로 초당 줄어드는 속도.")]
        [SerializeField, Min(0f)] private float coastingDeceleration = 0.75f;

        [Tooltip("앞바퀴가 좌우로 꺾이는 최대 각도.")]
        [SerializeField, Range(1f, 60f)] private float maxSteeringAngle = 32f;

        [Tooltip("앞바퀴가 목표 조향각까지 움직이는 초당 각도.")]
        [SerializeField, Min(0f)] private float steeringResponseDegreesPerSecond = 100f;

        [Header("테스트용 자동 주행")]

        [Tooltip("주행 진행도(0~1)에 대한 속도 배율. 급제동 구간은 이 커브의 골짜기로 만든다.")]
        [SerializeField] private AnimationCurve speedOverProgress = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Tooltip("최저 속도 배율. 0 이면 트럭이 영영 멈춰 설 수 있으므로 반드시 0보다 커야 한다.")]
        [SerializeField] private float minSpeedFactor = 0.06f;

        [Header("타이어 접지")]
        [Tooltip("바퀴 아래 도로 콜라이더를 찾는 레이어.")]
        [SerializeField] private LayerMask groundMask;

        [Tooltip("차량 진행을 막는 도로 장애물 레이어.")]
        [SerializeField] private LayerMask obstacleMask;

        [Tooltip("PhysicsMaterial 마찰을 타이어 횡접지력으로 환산하는 배율.")]
        [SerializeField, Min(0f)] private float tireFrictionMultiplier = 6.2f;

        [Tooltip("앞쪽 경로를 바라보며 자동 조향하는 거리. 짧을수록 복귀가 빠르고 조향이 거칠다.")]
        [SerializeField, Min(1f)] private float steeringLookAhead = 7f;

        [Tooltip("PhysicsMaterial이 없는 일반 도로에서 사용할 마찰 계수.")]
        [SerializeField, Min(0f)] private float defaultSurfaceFriction = 0.8f;

        [Tooltip("차체 횡가속도 1m/s²당 보이는 롤 각도.")]
        [SerializeField, Min(0f)] private float rollDegreesPerAcceleration = 1.4f;

        [Tooltip("한쪽 바퀴가 서스펜션 드룹 한계를 넘어 뜨지 않는 최대 차체 롤 각도.")]
        [SerializeField, Range(0f, 15f)] private float maxGroundedRollDegrees = 10f;

        [Header("차체")]
        [SerializeField] private float frontAxleOffset = 2.09f;
        [SerializeField] private float rearAxleOffset = -1.70f;

        [Tooltip("도로 표면에서 차체 원점까지의 높이.")]
        [SerializeField] private float rideHeight = 0.75f;

        [SerializeField] private Vector3 obstacleCollisionCenter = new(0f, 0.65f, 0f);
        [SerializeField] private Vector3 obstacleCollisionHalfExtents = new(3.1f, 1.3f, 1.25f);

        [Tooltip("장애물 충돌 직전 자세로 돌아간 뒤 바깥쪽으로 밀려나는 최대 거리.")]
        [SerializeField, Min(0f)] private float obstacleReboundDistance = 0.15f;

        private Rigidbody body;
        private Collider[] truckColliders;
        private readonly Collider[] obstacleBuffer = new Collider[32];
        private readonly RaycastHit[] groundContacts = new RaycastHit[4];
        private TruckWheelAnimator wheelAnimator;
        private float travelled;
        private bool isDriving;
        private Vector3 planarPosition;
        private Vector3 planarVelocity;
        private Vector3 steeringHeading = Vector3.right;
        private float rollVelocity;
        private bool autopilotForTesting;
        private bool hasTestControlInput;
        private float testThrottle;
        private float testReverse;
        private float testSteering;
        private float steeringAngleDegrees;
        private bool launchedByExplosion;

        public event Action Arrived;

        public float Speed { get; private set; }

        /// <summary>폭발 충격으로 차체가 키네마틱 경로 주행을 벗어났는지.</summary>
        public bool IsLaunchedByExplosion => launchedByExplosion;

        /// <summary>가장 최근 폭발 직후 차체에 남은 실제 Rigidbody 속도.</summary>
        public Vector3 ExplosionVelocity { get; private set; }

        /// <summary>현재 경로 중심에서 오른쪽을 양수로 잰 실제 횡이탈 거리.</summary>
        public float LateralDriftOffset { get; private set; }

        /// <summary>차체가 바라보는 방향과 실제 속도 벡터 사이의 슬립 각.</summary>
        public float DriftYawDegrees { get; private set; }

        public float DriftRollDegrees { get; private set; }

        /// <summary>경로 오른쪽을 양수로 잰 실제 횡방향 속도.</summary>
        public float LateralSlipSpeed { get; private set; }

        /// <summary>현재 경로를 그대로 따라가기 위해 필요한 부호 있는 횡가속도.</summary>
        public float CorneringAccelerationDemand { get; private set; }

        /// <summary>현재 바퀴 아래 콜라이더의 동마찰 계수.</summary>
        public float SurfaceFriction { get; private set; }

        public float Speed01 => maxSpeed > 0f ? Mathf.Clamp01(Mathf.Abs(Speed) / maxSpeed) : 0f;

        public bool IsDriving => isDriving;

        public float SteeringAngleDegrees => steeringAngleDegrees;

        public float Progress => Mathf.InverseLerp(startDistance, goalDistance, travelled);

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            truckColliders = GetComponentsInChildren<Collider>(true);
            wheelAnimator = GetComponent<TruckWheelAnimator>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            travelled = startDistance;
            Vector3 routePosition = path.PositionAt(travelled);
            planarPosition = new Vector3(routePosition.x, 0f, routePosition.z);
            steeringHeading = PathHeadingAt(travelled);
            SurfaceFriction = defaultSurfaceFriction;
        }

        public void BeginDrive()
        {
            if (launchedByExplosion)
            {
                return;
            }

            isDriving = true;
            if (autopilotForTesting)
            {
                Speed = EvaluateAutopilotSpeed();
                if (planarVelocity.sqrMagnitude < 1e-5f)
                {
                    planarVelocity = PathHeadingAt(travelled) * PlanarSpeedAt(travelled, Speed);
                }
            }
            else
            {
                Speed = 0f;
                planarVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 키네마틱 경로 주행 중인 차체를 실제 동적 Rigidbody로 전환해 폭발 충격을 받게 한다.
        /// 폭발은 주행 경로를 보정하는 연출이 아니라, 차체에 남는 속도와 중력으로 처리한다.
        /// </summary>
        public void ReceiveExplosionImpulse(Vector3 explosionCenter, float impulse, float radius)
        {
            if (launchedByExplosion || body == null)
            {
                return;
            }

            launchedByExplosion = true;
            isDriving = false;
            float inheritedSpeed = Mathf.Max(0f, Speed);
            Speed = 0f;
            planarVelocity = Vector3.zero;

            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = transform.right * inheritedSpeed;
            body.angularVelocity = Vector3.zero;

            float appliedImpulse = Mathf.Max(impulse, 0f);
            body.AddExplosionForce(
                appliedImpulse,
                explosionCenter,
                Mathf.Max(radius, 0.1f),
                1.8f,
                ForceMode.Impulse);

            // 폭발 중심이 차체와 겹치거나 옆에 있어도 최소한의 상승 성분은 보장한다.
            if (body.linearVelocity.y < 8f)
            {
                body.AddForce(
                    Vector3.up * (8f - body.linearVelocity.y),
                    ForceMode.VelocityChange);
            }

            body.AddTorque(
                transform.forward * appliedImpulse * 0.12f
                + transform.up * appliedImpulse * 0.08f,
                ForceMode.Impulse);
            ExplosionVelocity = body.linearVelocity;
        }

        /// <summary>기존 난이도 회귀 테스트가 같은 속도 프로필로 경로를 완주하도록 한다.</summary>
        public void EnableAutopilotForTesting()
        {
            autopilotForTesting = true;
        }

        /// <summary>키보드 대신 결정적인 입력으로 직접 주행을 검증한다.</summary>
        public void SetControlInputForTesting(float forward, float reverse, float steering)
        {
            hasTestControlInput = true;
            testThrottle = Mathf.Clamp01(forward);
            testReverse = Mathf.Clamp01(reverse);
            testSteering = Mathf.Clamp(steering, -1f, 1f);
        }

        public void ClearControlInputForTesting()
        {
            hasTestControlInput = false;
            testThrottle = 0f;
            testReverse = 0f;
            testSteering = 0f;
        }

        /// <summary>씬 빌더가 출발 자세를 잡을 때 쓴다. 에디터에서도 경로 위에 정확히 올려 둔다.</summary>
        public void SnapToStart()
        {
            travelled = startDistance;
            Vector3 routePosition = path.PositionAt(travelled);
            steeringHeading = PathHeadingAt(travelled);
            planarPosition = new Vector3(routePosition.x, 0f, routePosition.z);
            planarVelocity = Vector3.zero;
            ResetSlipFeedback();
            bool grounded = TryGetGroundPose(
                steeringHeading,
                0f,
                out Vector3 position,
                out Quaternion rotation,
                out float friction);
            if (!grounded)
            {
                GetRoutePoseAt(steeringHeading, out position, out rotation);
            }

            SurfaceFriction = grounded ? friction : defaultSurfaceFriction;
            transform.SetPositionAndRotation(position, rotation);
        }

        private void FixedUpdate()
        {
            if (!isDriving || launchedByExplosion)
            {
                return;
            }

            float deltaTime = Time.fixedDeltaTime;
            Vector3 previousPlanarPosition = planarPosition;
            float previousTravelled = travelled;
            Vector3 previousSteeringHeading = steeringHeading;
            float previousDriftRollDegrees = DriftRollDegrees;
            float previousRollVelocity = rollVelocity;
            Vector3 previousBodyPosition = body.position;
            Quaternion previousBodyRotation = body.rotation;

            Vector3 routePosition = path.PositionAt(travelled);
            Vector3 pathHeading = PathHeadingAt(travelled);
            Vector3 pathRight = Vector3.Cross(Vector3.up, pathHeading).normalized;
            Vector3 routeToTruck = new(
                planarPosition.x - routePosition.x,
                0f,
                planarPosition.z - routePosition.z);
            float lateralOffset = Vector3.Dot(routeToTruck, pathRight);
            if (autopilotForTesting)
            {
                Speed = EvaluateAutopilotSpeed();
                Vector3 steeringTarget = pathHeading * steeringLookAhead - pathRight * lateralOffset;
                steeringHeading = steeringTarget.sqrMagnitude > 1e-5f
                    ? steeringTarget.normalized
                    : pathHeading;
            }
            else
            {
                ReadDriveInput(out float forward, out float reverse, out float steering);
                UpdateManualSpeed(forward, reverse, deltaTime);
                steeringAngleDegrees = Mathf.MoveTowards(
                    steeringAngleDegrees,
                    steering * maxSteeringAngle,
                    steeringResponseDegreesPerSecond * deltaTime);
                float wheelbase = Mathf.Max(0.1f, frontAxleOffset - rearAxleOffset);
                float signedPlanarSpeed = Vector3.Dot(planarVelocity, steeringHeading);
                float yaw = signedPlanarSpeed
                    / wheelbase
                    * Mathf.Tan(steeringAngleDegrees * Mathf.Deg2Rad)
                    * Mathf.Rad2Deg
                    * deltaTime;
                steeringHeading = Quaternion.AngleAxis(yaw, Vector3.up) * steeringHeading;
                steeringHeading.y = 0f;
                steeringHeading.Normalize();
            }

            wheelAnimator?.SetFrontSteeringAngle(steeringAngleDegrees);

            float planarSpeed = PlanarSpeedAt(travelled, Speed);
            float appliedLateralAcceleration = ApplySurfaceTraction(
                deltaTime,
                steeringHeading,
                planarSpeed);
            planarPosition += planarVelocity * deltaTime;

            if (autopilotForTesting)
            {
                float directionAlignment = planarVelocity.sqrMagnitude > 1e-5f
                    ? Mathf.Max(0f, Vector3.Dot(planarVelocity.normalized, pathHeading))
                    : 0f;
                travelled += Speed * directionAlignment * deltaTime;
            }
            else
            {
                travelled += Vector3.Dot(planarVelocity, pathHeading) * deltaTime;
                travelled = Mathf.Max(0f, travelled);
            }

            routePosition = path.PositionAt(travelled);
            pathHeading = PathHeadingAt(travelled);
            UpdateSlipFeedback(routePosition, pathHeading, appliedLateralAcceleration, deltaTime);
            bool hasGroundPose = TryGetGroundPose(
                steeringHeading,
                DriftRollDegrees,
                out Vector3 position,
                out Quaternion rotation,
                out float friction);

            if (!autopilotForTesting
                && ObstacleBlocksMove(position, rotation, out Vector3 separationDirection))
            {
                Vector3 attemptedPlanarMove = planarPosition - previousPlanarPosition;
                planarPosition = previousPlanarPosition;
                travelled = previousTravelled;
                steeringHeading = previousSteeringHeading;
                DriftRollDegrees = previousDriftRollDegrees;
                rollVelocity = previousRollVelocity;
                Speed = 0f;
                planarVelocity = Vector3.zero;

                Vector3 reboundOffset = Vector3.ProjectOnPlane(separationDirection, Vector3.up);
                if (reboundOffset.sqrMagnitude < 1e-5f)
                {
                    reboundOffset = -attemptedPlanarMove;
                }

                if (reboundOffset.sqrMagnitude > 1e-5f)
                {
                    reboundOffset = reboundOffset.normalized
                        * Mathf.Min(obstacleReboundDistance, attemptedPlanarMove.magnitude);
                    planarPosition += reboundOffset;
                    travelled = Mathf.Max(
                        0f,
                        travelled + Vector3.Dot(reboundOffset, PathHeadingAt(travelled)));
                }

                hasGroundPose = TryGetGroundPose(
                    steeringHeading,
                    DriftRollDegrees,
                    out position,
                    out rotation,
                    out friction);
                if (!hasGroundPose || ObstacleBlocksMove(position, rotation, out _))
                {
                    planarPosition = previousPlanarPosition;
                    travelled = previousTravelled;
                    position = previousBodyPosition;
                    rotation = previousBodyRotation;
                    hasGroundPose = false;
                }
            }

            SurfaceFriction = hasGroundPose ? friction : SurfaceFriction;

            body.MovePosition(position);
            body.MoveRotation(rotation);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (isDriving && other.CompareTag("Finish"))
            {
                isDriving = false;
                Speed = 0f;
                planarVelocity = Vector3.zero;
                Arrived?.Invoke();
            }
        }

        private bool ObstacleBlocksMove(
            Vector3 nextPosition,
            Quaternion nextRotation,
            out Vector3 separationDirection)
        {
            separationDirection = Vector3.zero;
            int blockingMask = obstacleMask.value | groundMask.value;
            if (blockingMask == 0)
            {
                return false;
            }

            Vector3 center = nextPosition + nextRotation * obstacleCollisionCenter;
            int obstacleCount = Physics.OverlapBoxNonAlloc(
                center,
                obstacleCollisionHalfExtents,
                obstacleBuffer,
                nextRotation,
                blockingMask,
                QueryTriggerInteraction.Ignore);

            Quaternion rootRotationOffset = nextRotation * Quaternion.Inverse(transform.rotation);
            for (int obstacleIndex = 0; obstacleIndex < obstacleCount; obstacleIndex++)
            {
                Collider obstacle = obstacleBuffer[obstacleIndex];
                bool isObstacle = (obstacleMask.value & (1 << obstacle.gameObject.layer)) != 0;
                if (!isObstacle
                    && !obstacle.name.StartsWith("Boundary_", StringComparison.Ordinal))
                {
                    continue;
                }

                for (int truckIndex = 0; truckIndex < truckColliders.Length; truckIndex++)
                {
                    Collider truckCollider = truckColliders[truckIndex];
                    if (!truckCollider.enabled || truckCollider.isTrigger)
                    {
                        continue;
                    }

                    Vector3 colliderPosition = nextPosition
                        + rootRotationOffset * (truckCollider.transform.position - transform.position);
                    Quaternion colliderRotation = rootRotationOffset * truckCollider.transform.rotation;
                    if (Physics.ComputePenetration(
                        truckCollider,
                        colliderPosition,
                        colliderRotation,
                        obstacle,
                        obstacle.transform.position,
                        obstacle.transform.rotation,
                        out separationDirection,
                        out _))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private float EvaluateAutopilotSpeed()
        {
            float factor = Mathf.Max(minSpeedFactor, speedOverProgress.Evaluate(Progress));
            float targetSpeed = maxSpeed * factor;
            // 자동주행은 경로 물리 검증용이므로, 빙판에서 수동 최고속도와 같은 속도로
            // 코너를 강제로 따라가다 경로 밖으로 튀지 않도록 테스트 주행만 제한한다.
            return Mathf.Min(targetSpeed, 4f);
        }

        private void ReadDriveInput(out float forward, out float reverse, out float steering)
        {
            if (hasTestControlInput)
            {
                forward = testThrottle;
                reverse = testReverse;
                steering = testSteering;
                return;
            }

            float vertical = Input.GetAxisRaw("Vertical");
            forward = Mathf.Max(0f, vertical);
            reverse = Mathf.Max(0f, -vertical);
            steering = Input.GetAxisRaw("Horizontal");
        }

        private void UpdateManualSpeed(float forward, float reverse, float deltaTime)
        {
            if (forward > 0f)
            {
                Speed = Speed < 0f
                    ? Mathf.MoveTowards(Speed, 0f, brakeDeceleration * forward * deltaTime)
                    : Mathf.MoveTowards(Speed, maxSpeed, acceleration * forward * deltaTime);
            }
            else if (reverse > 0f)
            {
                Speed = Speed > 0f
                    ? Mathf.MoveTowards(Speed, 0f, brakeDeceleration * reverse * deltaTime)
                    : Mathf.MoveTowards(
                        Speed,
                        -maxReverseSpeed,
                        reverseAcceleration * reverse * deltaTime);
            }
            else
            {
                Speed = Mathf.MoveTowards(
                    Speed,
                    0f,
                    coastingDeceleration * deltaTime);
            }
        }

        /// <summary>
        /// 엔진은 종방향 속도를 유지하지만, 횡방향 속도는 μg를 넘는 속도로 지울 수 없다.
        /// 낮은 μ의 얼음에서는 조향 방향이 바뀌어도 이전 속도 방향이 남아 바깥으로 미끄러진다.
        /// </summary>
        private float ApplySurfaceTraction(
            float deltaTime,
            Vector3 desiredHeading,
            float desiredPlanarSpeed)
        {
            Vector3 right = Vector3.Cross(Vector3.up, desiredHeading).normalized;
            if (planarVelocity.sqrMagnitude < 1e-5f)
            {
                planarVelocity = desiredHeading * desiredPlanarSpeed;
                return 0f;
            }

            float lateralSpeed = Vector3.Dot(planarVelocity, right);
            float maxLateralSpeedChange = SurfaceFriction
                * tireFrictionMultiplier
                * Gravity
                * deltaTime;
            float lateralSpeedChange = Mathf.Clamp(
                -lateralSpeed,
                -maxLateralSpeedChange,
                maxLateralSpeedChange);
            planarVelocity += right * lateralSpeedChange;
            planarVelocity = planarVelocity.sqrMagnitude > 1e-5f
                ? planarVelocity.normalized * Mathf.Abs(desiredPlanarSpeed)
                : desiredHeading * desiredPlanarSpeed;
            return deltaTime > 0f ? lateralSpeedChange / deltaTime : 0f;
        }

        private float PlanarSpeedAt(float distance, float pathSpeed)
        {
            Vector3 slope = path.PositionAt(distance + frontAxleOffset)
                - path.PositionAt(distance + rearAxleOffset);
            float length = slope.magnitude;
            if (length < 1e-5f)
            {
                return pathSpeed;
            }

            slope.y = 0f;
            return pathSpeed * slope.magnitude / length;
        }

        private void UpdateSlipFeedback(
            Vector3 routePosition,
            Vector3 pathHeading,
            float appliedLateralAcceleration,
            float deltaTime)
        {
            Vector3 pathRight = Vector3.Cross(Vector3.up, pathHeading).normalized;
            Vector3 routeToTruck = new(
                planarPosition.x - routePosition.x,
                0f,
                planarPosition.z - routePosition.z);
            LateralDriftOffset = Vector3.Dot(routeToTruck, pathRight);
            LateralSlipSpeed = Vector3.Dot(planarVelocity, pathRight);
            CorneringAccelerationDemand = CalculateCorneringDemand(travelled, Speed);

            Vector3 velocityHeading = planarVelocity.sqrMagnitude > 1e-5f
                ? planarVelocity.normalized
                : steeringHeading;
            DriftYawDegrees = Vector3.SignedAngle(steeringHeading, velocityHeading, Vector3.up);

            float targetRoll = Mathf.Clamp(
                -appliedLateralAcceleration * rollDegreesPerAcceleration,
                -maxGroundedRollDegrees,
                maxGroundedRollDegrees);
            DriftRollDegrees = Mathf.Clamp(
                Mathf.SmoothDamp(
                    DriftRollDegrees,
                    targetRoll,
                    ref rollVelocity,
                    0.18f,
                    Mathf.Infinity,
                    deltaTime),
                -maxGroundedRollDegrees,
                maxGroundedRollDegrees);
        }

        private float CalculateCorneringDemand(float distance, float speed)
        {
            const float SampleDistance = 2f;
            Vector3 before = PathHeadingBetween(distance - SampleDistance, distance);
            Vector3 after = PathHeadingBetween(distance, distance + SampleDistance);
            float signedRadians = Vector3.SignedAngle(before, after, Vector3.up) * Mathf.Deg2Rad;
            return signedRadians / (SampleDistance * 2f) * speed * speed;
        }

        private Vector3 PathHeadingAt(float distance)
        {
            return PathHeadingBetween(distance + rearAxleOffset, distance + frontAxleOffset);
        }

        private Vector3 PathHeadingBetween(float fromDistance, float toDistance)
        {
            Vector3 heading = path.PositionAt(toDistance) - path.PositionAt(fromDistance);
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-6f)
            {
                return steeringHeading.sqrMagnitude > 1e-6f ? steeringHeading : Vector3.right;
            }

            return heading.normalized;
        }

        private void ResetSlipFeedback()
        {
            LateralDriftOffset = 0f;
            LateralSlipSpeed = 0f;
            CorneringAccelerationDemand = 0f;
            DriftYawDegrees = 0f;
            DriftRollDegrees = 0f;
            rollVelocity = 0f;
        }

        private bool TryGetGroundPose(
            Vector3 horizontalHeading,
            float roll,
            out Vector3 position,
            out Quaternion rotation,
            out float friction)
        {
            // 실제 접점을 못 얻어도 주행을 계속할 수 있도록 경로 자세를 기본값으로 남긴다.
            GetRoutePoseAt(horizontalHeading, out position, out Quaternion routeRotation);
            rotation = routeRotation;
            friction = defaultSurfaceFriction;
            if (wheelAnimator == null || wheelAnimator.WheelCount != groundContacts.Length)
            {
                return false;
            }

            friction = 0f;
            for (int index = 0; index < groundContacts.Length; index++)
            {
                if (!wheelAnimator.TryGetGroundHit(
                        position,
                        routeRotation,
                        index,
                        groundMask,
                        out groundContacts[index],
                        probeWorldDown: true))
                {
                    return false;
                }

                PhysicsMaterial material = groundContacts[index].collider.sharedMaterial;
                friction += material != null
                    ? material.dynamicFriction
                    : defaultSurfaceFriction;
            }

            friction /= groundContacts.Length;

            Vector3 front = (groundContacts[0].point + groundContacts[1].point) * 0.5f;
            Vector3 rear = (groundContacts[2].point + groundContacts[3].point) * 0.5f;
            Vector3 left = (groundContacts[0].point + groundContacts[2].point) * 0.5f;
            Vector3 right = (groundContacts[1].point + groundContacts[3].point) * 0.5f;
            Vector3 groundUp = Vector3.Cross(right - left, front - rear).normalized;
            if (Vector3.Dot(groundUp, Vector3.up) < 0f)
            {
                groundUp = -groundUp;
            }

            Vector3 bodyHeading = Vector3.ProjectOnPlane(horizontalHeading, groundUp);
            if (groundUp.sqrMagnitude < 0.5f || bodyHeading.sqrMagnitude < 1e-5f)
            {
                return false;
            }

            bodyHeading.Normalize();
            rotation = Quaternion.LookRotation(bodyHeading, groundUp)
                * Quaternion.Euler(0f, -90f, 0f)
                * Quaternion.Euler(roll, 0f, 0f);

            float axleMidpointOffset = (frontAxleOffset + rearAxleOffset) * 0.5f;
            Vector3 axleAnchor = new(axleMidpointOffset, -rideHeight, 0f);
            position += routeRotation * axleAnchor - rotation * axleAnchor;

            Vector3 suspensionUp = rotation * Vector3.up;
            float requiredCorrection = float.NegativeInfinity;
            for (int index = 0; index < groundContacts.Length; index++)
            {
                Vector3 wheel = wheelAnimator.GetRestLocalPosition(index);
                Vector3 wheelBottom = position
                    + rotation * (wheel + Vector3.down * wheelAnimator.WheelRadius);
                requiredCorrection = Mathf.Max(
                    requiredCorrection,
                    Vector3.Dot(groundContacts[index].point - wheelBottom, suspensionUp));
            }

            position += suspensionUp * requiredCorrection;
            return true;
        }

        private void GetRoutePoseAt(
            Vector3 horizontalHeading,
            out Vector3 position,
            out Quaternion rotation)
        {
            Vector3 front = path.PositionAt(travelled + frontAxleOffset);
            Vector3 rear = path.PositionAt(travelled + rearAxleOffset);
            Vector3 slopeHeading = front - rear;
            float horizontalLength = new Vector2(slopeHeading.x, slopeHeading.z).magnitude;
            float risePerMeter = horizontalLength > 1e-5f ? slopeHeading.y / horizontalLength : 0f;
            Vector3 bodyHeading = new(
                horizontalHeading.x,
                risePerMeter,
                horizontalHeading.z);
            bodyHeading.Normalize();

            Quaternion pathRotation = Quaternion.LookRotation(bodyHeading, Vector3.up)
                * Quaternion.Euler(0f, -90f, 0f);
            float axleMidpointOffset = (frontAxleOffset + rearAxleOffset) * 0.5f;
            Vector3 axleContactMidpoint = new(
                planarPosition.x,
                (front.y + rear.y) * 0.5f,
                planarPosition.z);
            axleContactMidpoint += horizontalHeading * axleMidpointOffset;
            position = axleContactMidpoint
                - pathRotation * new Vector3(axleMidpointOffset, -rideHeight, 0f);
            rotation = pathRotation;
        }
    }
}

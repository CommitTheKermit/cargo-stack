using System;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 키네마틱 트럭의 실제 프레임 간 이동량으로 원본 바퀴 메시를 굴리고,
    /// 앞바퀴 두 개는 TruckMover가 계산한 조향각만큼 좌우로 돌린다.
    /// 각 바퀴 아래 실제 지면 접점을 주행 물리에 제공하고, 같은 접점으로
    /// 시각 서스펜션을 압축·복원한다.
    /// </summary>
    public sealed class TruckWheelAnimator : MonoBehaviour
    {
        [SerializeField] private Transform[] suspensionRoots;
        [SerializeField] private Transform[] spinRoots;
        [SerializeField] private float wheelRadius = 0.515f;
        [SerializeField] private float compressionTravel = 0.20f;
        [SerializeField] private float droopTravel = 0.50f;
        [SerializeField] private float rayStartAboveCenter = 0.70f;
        [SerializeField] private float rayLength = 1.80f;
        [SerializeField] private float teleportThreshold = 1.5f;
        [SerializeField] private LayerMask groundMask;
        [SerializeField] private Collider roadCollider;

        private Vector3[] restLocalPositions;
        private Quaternion[] restLocalRotations;
        private Vector3 previousPosition;
        private Vector3 previousForward;
        private float spinAngleDegrees;
        private float totalSpinDegrees;
        private float frontSteeringAngleDegrees;
        private bool initialized;
        private readonly RaycastHit[] groundHits = new RaycastHit[16];

        public int WheelCount => suspensionRoots?.Length ?? 0;
        public float WheelRadius => wheelRadius;
        public float CompressionTravel => Mathf.Min(compressionTravel, 0.20f);
        public float DroopTravel => droopTravel;
        public float SpinAngleDegrees => spinAngleDegrees;
        public float TotalSpinDegrees => totalSpinDegrees;
        public float FrontSteeringAngleDegrees => frontSteeringAngleDegrees;

        public Transform GetSuspensionRoot(int index) => suspensionRoots[index];
        public Transform GetSpinRoot(int index) => spinRoots[index];
        public Vector3 GetRestLocalPosition(int index) =>
            restLocalPositions != null && index < restLocalPositions.Length
                ? restLocalPositions[index]
                : suspensionRoots[index].localPosition;

        public void Configure(
            Transform[] suspensions,
            Transform[] spins,
            float radius,
            LayerMask collisionMask,
            Collider roadSurface)
        {
            if (suspensions == null || spins == null || suspensions.Length != 4 || spins.Length != 4)
            {
                throw new ArgumentException("바퀴 서스펜션과 회전 루트는 각각 정확히 네 개여야 한다");
            }

            suspensionRoots = suspensions;
            spinRoots = spins;
            wheelRadius = radius;
            groundMask = collisionMask;
            roadCollider = roadSurface;
            InitializeState();
        }

        public void SetFrontSteeringAngle(float angleDegrees)
        {
            frontSteeringAngleDegrees = angleDegrees;
        }

        public bool TryGetGroundHit(
            Vector3 truckPosition,
            Quaternion truckRotation,
            int wheelIndex,
            LayerMask collisionMask,
            out RaycastHit hit)
        {
            hit = default;
            if (suspensionRoots == null || wheelIndex < 0 || wheelIndex >= suspensionRoots.Length)
            {
                return false;
            }

            Vector3 up = truckRotation * Vector3.up;
            Vector3 restWorld = truckPosition
                + truckRotation * GetRestLocalPosition(wheelIndex);
            var ray = new Ray(restWorld + up * rayStartAboveCenter, -up);
            bool foundGround = roadCollider != null
                && roadCollider.enabled
                && roadCollider.Raycast(ray, out hit, rayLength);
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                groundHits,
                rayLength,
                collisionMask,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit candidate = groundHits[index];
                if (candidate.collider == null
                    || candidate.collider.name.StartsWith("Boundary_", StringComparison.Ordinal)
                    || (foundGround && candidate.distance >= hit.distance))
                {
                    continue;
                }

                hit = candidate;
                foundGround = true;
            }

            return foundGround;
        }

        private void Awake()
        {
            InitializeState();
        }

        private void OnEnable()
        {
            previousPosition = transform.position;
            previousForward = transform.right;
            initialized = true;
        }

        private void LateUpdate()
        {
            if (!initialized || suspensionRoots == null || spinRoots == null)
            {
                InitializeState();
            }

            if (Time.deltaTime <= 0f)
            {
                previousPosition = transform.position;
                return;
            }

            UpdateSpin();
            UpdateSteering();
            UpdateSuspension();
            previousPosition = transform.position;
            previousForward = transform.right;
        }

        private void InitializeState()
        {
            if (suspensionRoots == null || spinRoots == null
                || suspensionRoots.Length != 4 || spinRoots.Length != 4)
            {
                initialized = false;
                return;
            }

            restLocalPositions = new Vector3[4];
            restLocalRotations = new Quaternion[4];
            for (int index = 0; index < 4; index++)
            {
                restLocalPositions[index] = suspensionRoots[index].localPosition;
                restLocalRotations[index] = suspensionRoots[index].localRotation;
            }

            previousPosition = transform.position;
            previousForward = transform.right;
            initialized = true;
        }

        private void UpdateSpin()
        {
            Vector3 displacement = transform.position - previousPosition;
            if (displacement.magnitude > teleportThreshold)
            {
                return;
            }

            Vector3 rollingDirection = previousForward + transform.right;
            rollingDirection = rollingDirection.sqrMagnitude > 1e-5f
                ? rollingDirection.normalized
                : transform.right;
            float forwardDistance = Vector3.Dot(displacement, rollingDirection);
            totalSpinDegrees -= forwardDistance / wheelRadius * Mathf.Rad2Deg;
            spinAngleDegrees = Mathf.Repeat(totalSpinDegrees + 180f, 360f) - 180f;

            Quaternion spin = Quaternion.AngleAxis(spinAngleDegrees, Vector3.forward);
            foreach (Transform spinRoot in spinRoots)
            {
                spinRoot.localRotation = spin;
            }
        }

        private void UpdateSteering()
        {
            Quaternion steering = Quaternion.AngleAxis(frontSteeringAngleDegrees, Vector3.up);
            suspensionRoots[0].localRotation = restLocalRotations[0] * steering;
            suspensionRoots[1].localRotation = restLocalRotations[1] * steering;
            suspensionRoots[2].localRotation = restLocalRotations[2];
            suspensionRoots[3].localRotation = restLocalRotations[3];
        }

        private void UpdateSuspension()
        {
            Vector3 up = transform.up;
            for (int index = 0; index < suspensionRoots.Length; index++)
            {
                float targetOffset = 0f;
                if (TryGetGroundHit(
                        transform.position,
                        transform.rotation,
                        index,
                        groundMask,
                        out RaycastHit hit))
                {
                    Vector3 targetCenter = hit.point + up * wheelRadius;
                    float targetLocalY = transform.InverseTransformPoint(targetCenter).y;
                    targetOffset = Mathf.Clamp(
                        targetLocalY - restLocalPositions[index].y,
                        -droopTravel,
                        CompressionTravel);
                }

                Transform suspension = suspensionRoots[index];
                Vector3 current = suspension.localPosition;
                current.x = restLocalPositions[index].x;
                current.y = restLocalPositions[index].y + targetOffset;
                current.z = restLocalPositions[index].z;
                suspension.localPosition = current;
            }
        }
    }
}

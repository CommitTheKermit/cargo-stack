using System;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 키네마틱 트럭의 실제 프레임 간 이동량으로 원본 바퀴 메시를 굴리고,
    /// 각 바퀴 아래 지면 높이를 따라 시각 서스펜션을 압축·복원한다.
    /// 주행 물리는 기존 TruckMover가 전담하며 이 컴포넌트는 시각물만 움직인다.
    /// </summary>
    public sealed class TruckWheelAnimator : MonoBehaviour
    {
        [SerializeField] private Transform[] suspensionRoots;
        [SerializeField] private Transform[] spinRoots;
        [SerializeField] private float wheelRadius = 0.515f;
        [SerializeField] private float compressionTravel = 0.18f;
        [SerializeField] private float droopTravel = 0.10f;
        [SerializeField] private float suspensionSmoothTime = 0.10f;
        [SerializeField] private float rayStartAboveCenter = 0.45f;
        [SerializeField] private float rayLength = 1.35f;
        [SerializeField] private float teleportThreshold = 1.5f;
        [SerializeField] private LayerMask groundMask;

        private Vector3[] restLocalPositions;
        private float[] suspensionVelocities;
        private Vector3 previousPosition;
        private float spinAngleDegrees;
        private bool initialized;

        public int WheelCount => suspensionRoots?.Length ?? 0;
        public float WheelRadius => wheelRadius;
        public float CompressionTravel => compressionTravel;
        public float DroopTravel => droopTravel;
        public float SpinAngleDegrees => spinAngleDegrees;

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
            LayerMask collisionMask)
        {
            if (suspensions == null || spins == null || suspensions.Length != 4 || spins.Length != 4)
            {
                throw new ArgumentException("바퀴 서스펜션과 회전 루트는 각각 정확히 네 개여야 한다");
            }

            suspensionRoots = suspensions;
            spinRoots = spins;
            wheelRadius = radius;
            groundMask = collisionMask;
            InitializeState();
        }

        private void Awake()
        {
            InitializeState();
        }

        private void OnEnable()
        {
            previousPosition = transform.position;
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
            UpdateSuspension(Time.deltaTime);
            previousPosition = transform.position;
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
            suspensionVelocities = new float[4];
            for (int index = 0; index < 4; index++)
            {
                restLocalPositions[index] = suspensionRoots[index].localPosition;
            }

            previousPosition = transform.position;
            initialized = true;
        }

        private void UpdateSpin()
        {
            Vector3 displacement = transform.position - previousPosition;
            if (displacement.magnitude > teleportThreshold)
            {
                return;
            }

            float forwardDistance = Vector3.Dot(displacement, transform.right);
            spinAngleDegrees -= forwardDistance / wheelRadius * Mathf.Rad2Deg;
            spinAngleDegrees = Mathf.Repeat(spinAngleDegrees + 180f, 360f) - 180f;

            Quaternion spin = Quaternion.AngleAxis(spinAngleDegrees, Vector3.forward);
            foreach (Transform spinRoot in spinRoots)
            {
                spinRoot.localRotation = spin;
            }
        }

        private void UpdateSuspension(float deltaTime)
        {
            Vector3 up = transform.up;
            for (int index = 0; index < suspensionRoots.Length; index++)
            {
                Vector3 restWorld = transform.TransformPoint(restLocalPositions[index]);
                Vector3 rayOrigin = restWorld + up * rayStartAboveCenter;
                float targetOffset = 0f;
                if (Physics.Raycast(
                        rayOrigin,
                        -up,
                        out RaycastHit hit,
                        rayLength,
                        groundMask,
                        QueryTriggerInteraction.Ignore))
                {
                    Vector3 targetCenter = hit.point + up * wheelRadius;
                    float targetLocalY = transform.InverseTransformPoint(targetCenter).y;
                    targetOffset = Mathf.Clamp(
                        targetLocalY - restLocalPositions[index].y,
                        -droopTravel,
                        compressionTravel);
                }

                Transform suspension = suspensionRoots[index];
                Vector3 current = suspension.localPosition;
                float smoothedY = Mathf.SmoothDamp(
                    current.y,
                    restLocalPositions[index].y + targetOffset,
                    ref suspensionVelocities[index],
                    suspensionSmoothTime,
                    Mathf.Infinity,
                    deltaTime);
                current.x = restLocalPositions[index].x;
                current.y = smoothedY;
                current.z = restLocalPositions[index].z;
                suspension.localPosition = current;
            }
        }
    }
}

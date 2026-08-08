using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 픽업트럭의 테일게이트 시각물과 충돌벽을 같은 축으로 움직인다.
    /// 적재 중에는 플레이어가 열고 닫을 수 있고, 출발 준비가 시작되면 닫힌 상태로 잠긴다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TruckTailgate : MonoBehaviour
    {
        [SerializeField, Range(0f, 120f)] private float openAngle = 90f;
        [SerializeField, Min(1f)] private float angularSpeed = 240f;

        private float currentAngle;
        private bool targetOpen;
        private bool lockedForDriving;

        public bool IsOpen => targetOpen && Mathf.Approximately(currentAngle, openAngle);
        public bool IsClosed => !targetOpen && Mathf.Approximately(currentAngle, 0f);
        public bool IsMoving => !Mathf.Approximately(currentAngle, targetOpen ? openAngle : 0f);
        public bool IsLockedForDriving => lockedForDriving;

        public void Toggle()
        {
            if (lockedForDriving)
            {
                return;
            }

            targetOpen = !targetOpen;
        }

        public void SetOpenInstantly(bool open)
        {
            if (lockedForDriving && open)
            {
                return;
            }

            targetOpen = open;
            currentAngle = open ? openAngle : 0f;
            ApplyRotation();
        }

        /// <summary>출발 전에 문을 닫고 이후 수동 입력을 막는다.</summary>
        public void CloseForDriving()
        {
            lockedForDriving = true;
            targetOpen = false;
        }

        private void Awake()
        {
            currentAngle = NormalizeDoorAngle(transform.localEulerAngles.z);
            targetOpen = currentAngle > openAngle * 0.5f;
            ApplyRotation();
        }

        private void Update()
        {
            float targetAngle = targetOpen ? openAngle : 0f;
            if (Mathf.Approximately(currentAngle, targetAngle))
            {
                return;
            }

            currentAngle = Mathf.MoveTowards(
                currentAngle,
                targetAngle,
                angularSpeed * Time.deltaTime);
            ApplyRotation();
        }

        private void ApplyRotation()
        {
            transform.localRotation = Quaternion.AngleAxis(currentAngle, Vector3.forward);
        }

        private static float NormalizeDoorAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}

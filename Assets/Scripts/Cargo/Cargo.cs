using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 짐 한 개. 그래픽은 3D지만 게임플레이는 XY 평면에서만 일어나므로(기획서 4.1)
    /// 깊이 이동과 평면 밖 회전을 Rigidbody 제약으로 잠근다. 초급 팀이 3D 물리의
    /// 자유도를 전부 상대하지 않아도 되게 하는 장치다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    public class Cargo : MonoBehaviour
    {
        private const RigidbodyConstraints PlaneLock =
            RigidbodyConstraints.FreezePositionZ |
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationY;

        /// <summary>겹침 검사에 쓰는 축소 비율. 짐칸 바닥에 살짝 닿은 정도는 겹침으로 보지 않는다.</summary>
        private const float OverlapShrink = 0.85f;

        [SerializeField] private Color invalidColor = new Color(0.9f, 0.25f, 0.2f);
        [SerializeField] private float rotationStep = 90f;

        private Rigidbody body;
        private BoxCollider box;
        private Renderer view;
        private Color baseColor;

        public bool IsHeld { get; private set; }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            box = GetComponent<BoxCollider>();
            view = GetComponentInChildren<Renderer>();

            body.constraints = PlaneLock;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            baseColor = view.material.color;
        }

        /// <summary>플레이어가 집어 든다. 드는 동안은 물리를 끄고 마우스를 따라다닌다.</summary>
        public void Hold()
        {
            IsHeld = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        /// <summary>손을 뗀다. 이 순간부터 물리에 맡겨진다.</summary>
        public void Release()
        {
            IsHeld = false;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            SetTint(baseColor);
        }

        /// <summary>드는 동안의 위치 갱신. 물리를 거치지 않아 마우스에 즉각 붙는다.</summary>
        public void MoveTo(Vector3 position)
        {
            transform.position = position;
        }

        /// <summary>화면 평면 안에서 한 단계 회전한다. 원통을 눕힐지 세울지가 전략이 된다.</summary>
        public void RotateStep()
        {
            transform.Rotate(0f, 0f, rotationStep, Space.World);
        }

        public void ShowPlacementValidity(bool valid)
        {
            SetTint(valid ? baseColor : invalidColor);
        }

        /// <summary>
        /// 자기 자신 말고 다른 콜라이더와 겹쳐 있는지 검사한다.
        /// 자기 모양을 아는 것은 자기 자신이므로 판정을 여기에 둔다.
        /// </summary>
        public bool IsOverlappingOthers()
        {
            Vector3 center = transform.TransformPoint(box.center);
            Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, transform.lossyScale) * OverlapShrink;

            Collider[] hits = Physics.OverlapBox(
                center, halfExtents, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

            foreach (Collider hit in hits)
            {
                if (hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void SetTint(Color color)
        {
            view.material.color = color;
        }
    }
}

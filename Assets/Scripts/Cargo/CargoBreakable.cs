using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 너무 강한 충격을 받으면 부서지는 화물(예: 대리석 흉상). 부서지면 보이지 않고 물리에도
    /// 관여하지 않지만, 오브젝트 자체는 남겨 CargoTracker가 손실로 집계하게 한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Cargo))]
    public sealed class CargoBreakable : MonoBehaviour
    {
        [Tooltip("이 속도(m/s) 이상의 충격을 받으면 부서진다. 실측 없이 정한 임시값이다.")]
        [SerializeField] private float breakImpactSpeed = 8f;

        private Cargo cargo;
        private Rigidbody body;

        public void Configure(float impactSpeed)
        {
            breakImpactSpeed = impactSpeed;
        }

        private void Awake()
        {
            cargo = GetComponent<Cargo>();
            body = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (cargo.IsBroken || collision.relativeVelocity.magnitude < breakImpactSpeed)
            {
                return;
            }

            Break();
        }

        private void Break()
        {
            cargo.MarkBroken();
            CargoBreakEffect.SpawnDustPuff(transform.position);

            foreach (Renderer visual in GetComponentsInChildren<Renderer>())
            {
                visual.enabled = false;
            }

            foreach (Collider hitbox in GetComponentsInChildren<Collider>())
            {
                hitbox.enabled = false;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }
    }
}

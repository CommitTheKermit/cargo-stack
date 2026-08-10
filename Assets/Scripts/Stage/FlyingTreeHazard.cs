using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 트럭보다 앞선 시점에 원거리 발사점에서 트럭의 예상 위치를 향해 포물선으로
    /// 날아가는 나무 위험물. 비행 방향을 앞쪽으로 정렬하고 지면·트럭·적재 화물에
    /// 닿으면 폭발해 주변 강체와 차량을 실제로 밀어낸다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class FlyingTreeHazard : MonoBehaviour
    {
        private enum FlightPhase
        {
            Waiting,
            Attacking,
            Exploded,
        }

        [SerializeField] private TruckMover target;
        [SerializeField, Range(0f, 1f)] private float triggerProgress = 0.2f;
        [SerializeField, Range(0f, 1f)] private float impactProgress = 0.3f;
        [SerializeField, Min(0.2f)] private float attackFlightTime = 1.5f;
        [SerializeField, Min(0f)] private float targetLeadDistance = 8f;
        [SerializeField, Min(0.1f)] private float explosionRadius = 5.5f;
        [SerializeField, Min(0f)] private float explosionForce = 1050f;
        [SerializeField, Min(0f)] private float truckLaunchImpulse = 28f;
        [SerializeField] private LayerMask groundMask;

        private Rigidbody body;
        private Collider hitbox;
        private Renderer[] renderers;
        private FlightPhase phase;

        public float TriggerProgress => triggerProgress;
        public float ImpactProgress => impactProgress;
        public bool HasLaunched => phase == FlightPhase.Attacking;
        public bool HasExploded => phase == FlightPhase.Exploded;
        public Vector3 LaunchVelocity { get; private set; }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            hitbox = GetComponent<Collider>();
            renderers = GetComponentsInChildren<Renderer>(true);
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private void Update()
        {
            if (phase == FlightPhase.Waiting
                && target != null
                && target.Progress >= triggerProgress)
            {
                Launch();
            }
        }

        private void FixedUpdate()
        {
            if (phase != FlightPhase.Attacking)
            {
                return;
            }

            Vector3 velocity = body.linearVelocity;
            if (velocity.sqrMagnitude > 0.25f)
            {
                // 나무의 로컬 앞(+Z)을 실제 비행 속도에 맞춘다. 공중에 떠서 옆으로
                // 미끄러지는 대신, 몸통이 먼저 향하는 미사일 같은 궤적을 만든다.
                body.MoveRotation(Quaternion.LookRotation(velocity.normalized, Vector3.up));
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (phase != FlightPhase.Attacking)
            {
                return;
            }

            if (IsExplosionTarget(collision.collider))
            {
                TruckMover truck = collision.collider.GetComponentInParent<TruckMover>();
                truck?.ReceiveExplosionImpulse(transform.position, truckLaunchImpulse, explosionRadius);
                Explode();
            }
        }

        public void Configure(
            TruckMover value,
            float launchProgress,
            float targetProgress,
            LayerMask ground,
            float flightTime,
            float force,
            float vehicleImpulse)
        {
            target = value;
            triggerProgress = Mathf.Clamp01(launchProgress);
            impactProgress = Mathf.Clamp01(targetProgress);
            groundMask = ground;
            attackFlightTime = Mathf.Max(0.2f, flightTime);
            explosionForce = Mathf.Max(0f, force);
            truckLaunchImpulse = Mathf.Max(0f, vehicleImpulse);
        }

        public bool IsExplosionTarget(Collider other)
        {
            if (other == null)
            {
                return false;
            }

            bool isGround = (groundMask.value & (1 << other.gameObject.layer)) != 0;
            bool isTruck = other.GetComponentInParent<TruckMover>() != null;
            bool isLoadedCargo = other.GetComponentInParent<Cargo>() != null;
            return isGround || isTruck || isLoadedCargo;
        }

        public void LaunchForTesting()
        {
            Launch();
        }

        public void ExplodeForTesting()
        {
            Explode();
        }

        private void Launch()
        {
            if (phase != FlightPhase.Waiting)
            {
                return;
            }

            phase = FlightPhase.Attacking;
            body.isKinematic = false;
            body.useGravity = true;

            Vector3 targetPosition = GetTargetPosition();
            float time = attackFlightTime;
            Vector3 displacement = targetPosition - transform.position;
            LaunchVelocity = displacement / time
                - Physics.gravity * (0.5f * time);
            body.linearVelocity = LaunchVelocity;
            body.angularVelocity = Vector3.zero;
            if (LaunchVelocity.sqrMagnitude > 0.25f)
            {
                body.rotation = Quaternion.LookRotation(LaunchVelocity.normalized, Vector3.up);
            }
        }

        private Vector3 GetTargetPosition()
        {
            if (target == null)
            {
                return transform.position + transform.forward * 24f;
            }

            Vector3 forward = target.transform.right.sqrMagnitude > 1e-5f
                ? target.transform.right.normalized
                : Vector3.right;
            float movingLead = Mathf.Max(0f, target.Speed) * attackFlightTime * 0.55f;
            return target.transform.position
                + forward * (targetLeadDistance + movingLead);
        }

        private void Explode()
        {
            if (phase == FlightPhase.Exploded)
            {
                return;
            }

            phase = FlightPhase.Exploded;
            Vector3 center = transform.position;
            TruckMover truckTarget = null;

            foreach (Collider nearby in Physics.OverlapSphere(
                center,
                explosionRadius,
                ~0,
                QueryTriggerInteraction.Ignore))
            {
                truckTarget ??= nearby.GetComponentInParent<TruckMover>();
                Rigidbody affected = nearby.attachedRigidbody;
                if (affected == null || affected == body || affected.isKinematic)
                {
                    continue;
                }

                affected.AddExplosionForce(
                    explosionForce,
                    center,
                    explosionRadius,
                    1.2f,
                    ForceMode.Impulse);
            }

            truckTarget?.ReceiveExplosionImpulse(center, truckLaunchImpulse, explosionRadius);

            FlyingTreeExplosionPulse.Create(center, explosionRadius);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            hitbox.enabled = false;
            foreach (Renderer treeRenderer in renderers)
            {
                treeRenderer.enabled = false;
            }

            Destroy(gameObject, 0.3f);
        }
    }

    internal sealed class FlyingTreeExplosionPulse : MonoBehaviour
    {
        private const float Lifetime = 0.45f;
        private float age;
        private float radius;
        private Light flash;
        private Renderer pulseRenderer;

        public static void Create(Vector3 position, float explosionRadius)
        {
            GameObject pulse = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pulse.name = "FlyingTreeExplosion";
            pulse.transform.position = position;
            Destroy(pulse.GetComponent<Collider>());

            var effect = pulse.AddComponent<FlyingTreeExplosionPulse>();
            effect.radius = explosionRadius;
            effect.pulseRenderer = pulse.GetComponent<Renderer>();
            Material material = new(Shader.Find("Standard"));
            material.color = new Color(1f, 0.2f, 0.02f, 0.9f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(4f, 0.35f, 0.02f));
            effect.pulseRenderer.material = material;

            effect.flash = pulse.AddComponent<Light>();
            effect.flash.type = LightType.Point;
            effect.flash.color = new Color(1f, 0.35f, 0.05f);
            effect.flash.range = explosionRadius * 2f;
            effect.flash.intensity = 8f;
            pulse.transform.localScale = Vector3.one * 0.2f;
        }

        private void Update()
        {
            age += Time.deltaTime;
            float progress = Mathf.Clamp01(age / Lifetime);
            transform.localScale = Vector3.one * Mathf.Lerp(0.2f, radius * 2f, progress);
            flash.intensity = Mathf.Lerp(8f, 0f, progress);
            pulseRenderer.material.color = Color.Lerp(
                new Color(1f, 0.75f, 0.08f, 0.9f),
                new Color(0.5f, 0.02f, 0f, 0f),
                progress);

            if (age >= Lifetime)
            {
                Destroy(pulseRenderer.material);
                Destroy(gameObject);
            }
        }
    }
}

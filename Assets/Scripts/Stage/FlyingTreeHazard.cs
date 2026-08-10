using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 처음에는 길가 땅에 밑동을 박고 곧게 선 나무. 트럭이 다가오면 밑동에서 로켓처럼
    /// 불을 뿜으며 솟아, 트럭의 예상 위치를 향해 천천히 날아간다. 몸통이 진행 방향으로
    /// 눕고 밑동(불꽃)이 뒤로 끌린다. 지면·트럭·적재 화물에 닿으면 폭발해 주변 강체와
    /// 차량을 실제로 밀어낸다.
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

        // 발사 직후 밑동이 심겼던 지면과 스치며 곧바로 폭발하지 않도록, 이 시간 동안은
        // 지면 충돌만 무시한다. 트럭·화물 충돌은 유예 없이 즉시 폭발시킨다.
        private const float GroundGraceSeconds = 0.25f;

        [SerializeField] private TruckMover target;
        [SerializeField, Range(0f, 1f)] private float triggerProgress = 0.2f;
        [SerializeField, Range(0f, 1f)] private float impactProgress = 0.3f;
        [SerializeField, Min(0.2f)] private float attackFlightTime = 2.4f;
        [SerializeField, Min(0f)] private float targetLeadDistance = 8f;
        [SerializeField, Min(0.1f)] private float explosionRadius = 5.5f;
        [SerializeField, Min(0f)] private float explosionForce = 1050f;
        [SerializeField, Min(0f)] private float truckLaunchImpulse = 28f;
        [SerializeField] private LayerMask groundMask;

        private Rigidbody body;
        private Collider hitbox;
        private Renderer[] renderers;
        private FlightPhase phase;
        private ParticleSystem rocketFire;
        private Light rocketGlow;
        private float launchTime;

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
            BuildRocketFire();
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
                // 곧게 선 나무의 몸통(+Y)을 실제 비행 속도에 맞춰 눕힌다. 밑동(-Y, 불꽃)이
                // 뒤로 끌리는 로켓 궤적이 된다.
                body.MoveRotation(Quaternion.FromToRotation(Vector3.up, velocity.normalized));
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (phase != FlightPhase.Attacking)
            {
                return;
            }

            if (!IsExplosionTarget(collision.collider))
            {
                return;
            }

            // 발사 직후에는 밑동이 심겼던 지면과 스칠 수 있다. 그 순간의 지면 충돌은
            // 무시하고, 트럭·화물이거나 유예가 끝난 뒤의 지면 충돌에만 폭발한다.
            bool isGround = (groundMask.value & (1 << collision.collider.gameObject.layer)) != 0;
            bool hitVehicleOrCargo =
                collision.collider.GetComponentInParent<TruckMover>() != null
                || collision.collider.GetComponentInParent<Cargo>() != null;
            if (isGround && !hitVehicleOrCargo && Time.time - launchTime < GroundGraceSeconds)
            {
                return;
            }

            TruckMover truck = collision.collider.GetComponentInParent<TruckMover>();
            truck?.ReceiveExplosionImpulse(transform.position, truckLaunchImpulse, explosionRadius);
            Explode();
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
            launchTime = Time.time;
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
                body.rotation = Quaternion.FromToRotation(Vector3.up, LaunchVelocity.normalized);
            }

            IgniteRocketFire();
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
            ExtinguishRocketFire();
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

        /// <summary>밑동(-Y)에서 뒤로 뿜는 로켓 화염을 절차적으로 만든다. 대기 중에는 꺼 둔다.</summary>
        private void BuildRocketFire()
        {
            var fireObject = new GameObject("RocketFire");
            fireObject.transform.SetParent(transform, false);
            // 나무 로컬 밑동(-Y)에서, 화염이 -Y 방향(뒤)으로 뿜어 나가도록 콘을 눕힌다.
            fireObject.transform.SetLocalPositionAndRotation(
                new Vector3(0f, 0.1f, 0f),
                Quaternion.Euler(90f, 0f, 0f));

            rocketFire = fireObject.AddComponent<ParticleSystem>();
            rocketFire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = rocketFire.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = 0.32f;
            main.startSpeed = 7.5f;
            main.startSize = 1.05f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.85f, 0.25f),
                new Color(1f, 0.35f, 0.05f));
            main.gravityModifier = -0.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;

            ParticleSystem.EmissionModule emission = rocketFire.emission;
            emission.rateOverTime = 90f;

            ParticleSystem.ShapeModule shape = rocketFire.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 11f;
            shape.radius = 0.28f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = rocketFire.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.Linear(0f, 1f, 1f, 0f));

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = rocketFire.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.9f, 0.4f), 0f),
                    new GradientColorKey(new Color(1f, 0.4f, 0.05f), 0.5f),
                    new GradientColorKey(new Color(0.25f, 0.05f, 0.02f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.4f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;

            var renderer = fireObject.GetComponent<ParticleSystemRenderer>();
            Shader fireShader = Shader.Find("Legacy Shaders/Particles/Additive")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Sprites/Default");
            if (fireShader != null)
            {
                renderer.material = new Material(fireShader);
            }

            // 밑동 글로우. 비행 중에만 켜서 로켓 분사처럼 주변을 물들인다.
            rocketGlow = fireObject.AddComponent<Light>();
            rocketGlow.type = LightType.Point;
            rocketGlow.color = new Color(1f, 0.45f, 0.12f);
            rocketGlow.range = 6f;
            rocketGlow.intensity = 0f;
        }

        private void IgniteRocketFire()
        {
            if (rocketFire != null)
            {
                rocketFire.Play(true);
            }

            if (rocketGlow != null)
            {
                rocketGlow.intensity = 4.5f;
            }
        }

        private void ExtinguishRocketFire()
        {
            if (rocketFire != null)
            {
                rocketFire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (rocketGlow != null)
            {
                rocketGlow.intensity = 0f;
            }
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

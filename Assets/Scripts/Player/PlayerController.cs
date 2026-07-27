using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 적재 단계의 1인칭 도보 이동. WASD 로 걷고 Space 로 뛰어 짐칸에 올라갈 수 있다.
    /// 원본: nan2026-cargo(NAN 2026 사전과제)의 Assets/Spike/Runtime/PlayerController.cs.
    /// 네임스페이스만 바꿔 그대로 가져왔다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private Camera movementCamera;
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float acceleration = 35f;
        [SerializeField] private float jumpHeight = 1.6f;
        [SerializeField] private float groundCheckDistance = 0.1f;
        [SerializeField] private float minGroundNormalY = 0.55f;

        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private Rigidbody playerBody;
        private CapsuleCollider playerCollider;
        private Vector2 movementInput;
        private bool jumpRequested;
        private bool controlEnabled = true;

        public Rigidbody Body
        {
            get
            {
                EnsureInitialized();
                return playerBody;
            }
        }

        public bool ControlEnabled => controlEnabled;
        public float JumpHeight => jumpHeight;

        public void Configure(Camera cameraToUse)
        {
            movementCamera = cameraToUse;
        }

        public void SetControlEnabled(bool enabled)
        {
            controlEnabled = enabled;
            if (!enabled)
            {
                movementInput = Vector2.zero;
                jumpRequested = false;
            }
        }

        public bool IsGrounded()
        {
            EnsureInitialized();
            Vector3 scale = transform.lossyScale;
            float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float radius = playerCollider.radius * radiusScale;
            float scaledHeight = playerCollider.height * Mathf.Abs(scale.y);
            float capsuleOffset = Mathf.Max(0f, scaledHeight * 0.5f - radius);
            Vector3 capsuleCenter = transform.TransformPoint(playerCollider.center);
            Vector3 capsuleUp = transform.up * capsuleOffset;
            int hitCount = Physics.CapsuleCastNonAlloc(
                capsuleCenter + capsuleUp,
                capsuleCenter - capsuleUp,
                radius,
                Vector3.down,
                groundHits,
                groundCheckDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                RaycastHit hit = groundHits[hitIndex];
                if (hit.collider != playerCollider && !hit.collider.transform.IsChildOf(transform) && hit.normal.y >= minGroundNormalY)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryJump()
        {
            EnsureInitialized();
            if (!controlEnabled || !IsGrounded())
            {
                return false;
            }

            Vector3 velocity = playerBody.linearVelocity;
            velocity.y = Mathf.Max(0f, velocity.y);
            playerBody.linearVelocity = velocity;

            float jumpSpeed = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
            playerBody.AddForce(Vector3.up * jumpSpeed, ForceMode.VelocityChange);
            return true;
        }

        public void SetWorldPose(Vector3 position, Quaternion rotation, Vector3 inheritedVelocity)
        {
            EnsureInitialized();
            transform.SetPositionAndRotation(position, rotation);
            playerBody.position = position;
            playerBody.rotation = rotation;
            playerBody.linearVelocity = inheritedVelocity;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.WakeUp();
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            movementInput = controlEnabled
                ? new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"))
                : Vector2.zero;
            movementInput = Vector2.ClampMagnitude(movementInput, 1f);

            if (controlEnabled && Input.GetKeyDown(KeyCode.Space))
            {
                jumpRequested = true;
            }
        }

        private void FixedUpdate()
        {
            EnsureInitialized();
            Vector3 movementDirection = GetMovementDirection();
            Vector3 currentPlanarVelocity = Vector3.ProjectOnPlane(playerBody.linearVelocity, Vector3.up);
            Vector3 targetPlanarVelocity = movementDirection * moveSpeed;
            Vector3 velocityChange = Vector3.MoveTowards(currentPlanarVelocity, targetPlanarVelocity, acceleration * Time.fixedDeltaTime) - currentPlanarVelocity;
            playerBody.AddForce(velocityChange, ForceMode.VelocityChange);

            if (jumpRequested)
            {
                TryJump();
                jumpRequested = false;
            }
        }

        private Vector3 GetMovementDirection()
        {
            if (!controlEnabled || movementCamera == null)
            {
                return Vector3.zero;
            }

            Vector3 cameraForward = Vector3.ProjectOnPlane(movementCamera.transform.forward, Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(movementCamera.transform.right, Vector3.up).normalized;
            return (cameraForward * movementInput.y + cameraRight * movementInput.x).normalized;
        }

        private void EnsureInitialized()
        {
            if (playerBody == null)
            {
                playerBody = GetComponent<Rigidbody>();
            }

            if (playerCollider == null)
            {
                playerCollider = GetComponent<CapsuleCollider>();
            }
        }
    }
}

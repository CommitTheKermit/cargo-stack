using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CargoStack
{
    /// <summary>
    /// 이 게임의 핵심 조작. 1인칭으로 화물을 직접 집어 짐칸에 쌓는다.
    /// E 로 집고 놓으며, 드는 동안 조준한 수평면에 반투명 미리보기가 나타나고 Q 로 90도 돌린다.
    ///
    /// 원본: nan2026-cargo(NAN 2026 사전과제)의 Assets/Spike/Runtime/PlayerCargoInteractor.cs.
    /// 네임스페이스와 화물 타입(CargoItem → Cargo)만 바꿔 가져왔다.
    /// 원본에 있던 CargoCarrierProxy 연동은 뺐다. 이 게임의 트럭은 플레이어가 몰지 않는
    /// 키네마틱 차체라 화물이 트럭을 밀 수 없고, 따라서 반작용을 분리할 프록시가 필요 없다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerCargoInteractor : MonoBehaviour
    {
        [SerializeField] private Transform carryAnchor;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private float interactionRadius = 2.1f;
        [SerializeField] private float placementRange = 4.5f;
        [SerializeField] private float minimumPlacementNormalY = 0.55f;
        [SerializeField] private float placementSurfaceGap = 0.01f;
        [SerializeField] private Color placementPreviewColor = new Color(0.25f, 1f, 0.45f, 0.38f);

        private readonly Dictionary<Collider, bool> heldColliderStates = new Dictionary<Collider, bool>();
        private Rigidbody heldBody;
        private BoxCollider heldBoxCollider;
        private Collider[] playerColliders;
        private GameObject placementPreview;
        private Material placementPreviewMaterial;
        private bool originalUseGravity;
        private CollisionDetectionMode originalCollisionMode;
        private bool hasValidPlacement;
        private float previewYaw;
        private Vector3 previewCargoPosition;
        private Quaternion previewCargoRotation;

        public bool HasCargo => heldBody != null;
        public Cargo HeldCargo => heldBody == null ? null : heldBody.GetComponent<Cargo>();
        public bool HasValidPlacement => heldBody != null && hasValidPlacement;
        public Vector3 PreviewPosition => previewCargoPosition;
        public Quaternion PreviewRotation => previewCargoRotation;

        public void Configure(Transform anchor, Camera cameraToUse)
        {
            carryAnchor = anchor;
            viewCamera = cameraToUse;
        }

        public bool TryPickUpFromView()
        {
            if (viewCamera == null)
            {
                return false;
            }

            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, interactionRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Cargo cargo = hit.collider.GetComponentInParent<Cargo>();
            return TryPickUp(cargo);
        }

        public bool TryPickUp(Cargo cargo)
        {
            EnsureInitialized();
            if (cargo == null || heldBody != null || carryAnchor == null)
            {
                return false;
            }

            Rigidbody body = cargo.GetComponent<Rigidbody>();
            BoxCollider boxCollider = cargo.GetComponent<BoxCollider>();
            if (body == null || boxCollider == null || Vector3.Distance(transform.position, body.worldCenterOfMass) > interactionRadius)
            {
                return false;
            }

            heldBody = body;
            heldBoxCollider = boxCollider;
            originalUseGravity = body.useGravity;
            originalCollisionMode = body.collisionDetectionMode;
            previewYaw = NormalizeYaw(body.rotation.eulerAngles.y);

            body.useGravity = false;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            IgnorePlayerCollisions(body, true);
            DisableHeldCargoCollisions(body);
            CreatePlacementPreview();
            RefreshPlacementPreview();
            return true;
        }

        public void RotatePlacementPreview()
        {
            if (heldBody == null)
            {
                return;
            }

            previewYaw = NormalizeYaw(previewYaw + 90f);
            RefreshPlacementPreview();
        }

        public void RefreshPlacementPreview()
        {
            hasValidPlacement = false;
            if (heldBody == null || heldBoxCollider == null || viewCamera == null || placementPreview == null)
            {
                SetPlacementPreviewVisible(false);
                return;
            }

            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, placementRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || hit.normal.y < minimumPlacementNormalY)
            {
                SetPlacementPreviewVisible(false);
                return;
            }

            previewCargoRotation = Quaternion.Euler(0f, previewYaw, 0f);
            Vector3 absoluteScale = Abs(heldBody.transform.lossyScale);
            Vector3 scaledCenter = Vector3.Scale(heldBoxCollider.center, absoluteScale);
            Vector3 halfSize = Vector3.Scale(heldBoxCollider.size * 0.5f, absoluteScale);
            float supportDistance = ProjectHalfSizeOntoNormal(halfSize, previewCargoRotation, hit.normal);
            Vector3 previewColliderCenter = hit.point + hit.normal * (supportDistance + placementSurfaceGap);

            previewCargoPosition = previewColliderCenter - previewCargoRotation * scaledCenter;
            placementPreview.transform.SetPositionAndRotation(previewColliderCenter, previewCargoRotation);
            placementPreview.transform.localScale = halfSize * 2f;
            hasValidPlacement = true;
            SetPlacementPreviewVisible(true);
        }

        public bool TryPlaceHeldCargo()
        {
            if (heldBody == null)
            {
                return false;
            }

            RefreshPlacementPreview();
            if (!hasValidPlacement)
            {
                return false;
            }

            ReleaseHeldCargo(previewCargoPosition, previewCargoRotation, Vector3.zero);
            return true;
        }

        public void DropHeldCargo()
        {
            if (heldBody == null)
            {
                return;
            }

            PlayerController playerController = GetComponent<PlayerController>();
            ReleaseHeldCargo(heldBody.position, heldBody.rotation, playerController.Body.linearVelocity);
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (heldBody == null)
                {
                    TryPickUpFromView();
                }
                else
                {
                    TryPlaceHeldCargo();
                }
            }

            if (heldBody != null && Input.GetKeyDown(KeyCode.Q))
            {
                RotatePlacementPreview();
            }
        }

        private void LateUpdate()
        {
            if (heldBody != null)
            {
                RefreshPlacementPreview();
            }
        }

        private void FixedUpdate()
        {
            if (heldBody == null || carryAnchor == null)
            {
                return;
            }

            heldBody.MovePosition(carryAnchor.position);
            heldBody.MoveRotation(carryAnchor.rotation);
        }

        private void OnDisable()
        {
            if (heldBody != null)
            {
                DropHeldCargo();
            }

            DestroyPlacementPreview();
        }

        private void ReleaseHeldCargo(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            Rigidbody body = heldBody;
            heldBody = null;
            heldBoxCollider = null;
            hasValidPlacement = false;
            DestroyPlacementPreview();
            RestoreHeldCargoCollisions();
            IgnorePlayerCollisions(body, false);
            body.position = position;
            body.rotation = rotation;
            body.isKinematic = false;
            body.useGravity = originalUseGravity;
            body.collisionDetectionMode = originalCollisionMode;
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }

        private void CreatePlacementPreview()
        {
            DestroyPlacementPreview();
            placementPreview = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placementPreview.name = "CargoPlacementPreview";
            placementPreview.layer = LayerMask.NameToLayer("Ignore Raycast");

            Collider previewCollider = placementPreview.GetComponent<Collider>();
            previewCollider.enabled = false;
            DestroyRuntimeObject(previewCollider);

            Renderer previewRenderer = placementPreview.GetComponent<Renderer>();
            previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
            previewRenderer.receiveShadows = false;
            placementPreviewMaterial = CreateTransparentPreviewMaterial();
            previewRenderer.sharedMaterial = placementPreviewMaterial;
        }

        private Material CreateTransparentPreviewMaterial()
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader)
            {
                name = "CargoPlacementPreviewMaterial",
                color = placementPreviewColor
            };

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            return material;
        }

        private void DestroyPlacementPreview()
        {
            if (placementPreview != null)
            {
                DestroyRuntimeObject(placementPreview);
                placementPreview = null;
            }

            if (placementPreviewMaterial != null)
            {
                DestroyRuntimeObject(placementPreviewMaterial);
                placementPreviewMaterial = null;
            }
        }

        private void SetPlacementPreviewVisible(bool visible)
        {
            if (placementPreview != null && placementPreview.activeSelf != visible)
            {
                placementPreview.SetActive(visible);
            }
        }

        private void IgnorePlayerCollisions(Rigidbody cargoBody, bool ignore)
        {
            Collider[] cargoColliders = cargoBody.GetComponentsInChildren<Collider>(true);

            foreach (Collider playerCollider in playerColliders)
            {
                foreach (Collider cargoCollider in cargoColliders)
                {
                    Physics.IgnoreCollision(playerCollider, cargoCollider, ignore);
                }
            }
        }

        private void DisableHeldCargoCollisions(Rigidbody cargoBody)
        {
            heldColliderStates.Clear();
            foreach (Collider cargoCollider in cargoBody.GetComponentsInChildren<Collider>(true))
            {
                heldColliderStates[cargoCollider] = cargoCollider.enabled;
                cargoCollider.enabled = false;
            }
        }

        private void RestoreHeldCargoCollisions()
        {
            foreach (KeyValuePair<Collider, bool> colliderState in heldColliderStates)
            {
                if (colliderState.Key != null)
                {
                    colliderState.Key.enabled = colliderState.Value;
                }
            }

            heldColliderStates.Clear();
        }

        private void EnsureInitialized()
        {
            if (playerColliders == null || playerColliders.Length == 0)
            {
                playerColliders = GetComponentsInChildren<Collider>();
            }
        }

        private static float ProjectHalfSizeOntoNormal(Vector3 halfSize, Quaternion rotation, Vector3 normal)
        {
            return Mathf.Abs(Vector3.Dot(rotation * Vector3.right, normal)) * halfSize.x
                + Mathf.Abs(Vector3.Dot(rotation * Vector3.up, normal)) * halfSize.y
                + Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, normal)) * halfSize.z;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static float NormalizeYaw(float yaw)
        {
            return Mathf.Repeat(yaw, 360f);
        }

        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}

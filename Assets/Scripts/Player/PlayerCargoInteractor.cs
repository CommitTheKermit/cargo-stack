using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CargoStack
{
    /// <summary>
    /// 이 게임의 핵심 조작. 1인칭으로 화물을 직접 집어 짐칸에 쌓는다.
    /// E 로 집고 놓으며, 드는 동안 조준한 수평면에 반투명 미리보기가 나타나고 Q 를 누르는 동안 반시계 방향으로 돌린다.
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
        [SerializeField, Min(0f)] private float previewRotationDegreesPerSecond = 90f;
        [SerializeField] private Color placementPreviewColor = new Color(0.25f, 1f, 0.45f, 0.38f);

        private readonly Dictionary<Collider, bool> heldColliderStates = new Dictionary<Collider, bool>();
        private Rigidbody heldBody;
        private Collider heldCollider;
        private Vector3 heldColliderCenter;
        private Vector3 heldColliderHalfSize;
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
            Collider cargoCollider = cargo.GetComponent<Collider>();
            if (body == null
                || !TryGetPlacementProxy(
                    cargoCollider,
                    out Vector3 colliderCenter,
                    out Vector3 colliderHalfSize)
                || Vector3.Distance(transform.position, body.worldCenterOfMass) > interactionRadius)
            {
                return false;
            }

            heldBody = body;
            heldCollider = cargoCollider;
            heldColliderCenter = colliderCenter;
            heldColliderHalfSize = colliderHalfSize;
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
            RotatePlacementPreview(Time.deltaTime);
        }

        public void RotatePlacementPreview(float deltaTime)
        {
            if (heldBody == null || deltaTime <= 0f)
            {
                return;
            }

            previewYaw = NormalizeYaw(previewYaw - previewRotationDegreesPerSecond * deltaTime);
            RefreshPlacementPreview();
        }

        public void RefreshPlacementPreview()
        {
            hasValidPlacement = false;
            if (heldBody == null || heldCollider == null || viewCamera == null || placementPreview == null)
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
            Vector3 scaledCenter = Vector3.Scale(heldColliderCenter, absoluteScale);
            Vector3 halfSize = Vector3.Scale(heldColliderHalfSize, absoluteScale);
            float supportDistance = ProjectHalfSizeOntoNormal(halfSize, previewCargoRotation, hit.normal);
            Vector3 previewColliderCenter = hit.point + hit.normal * (supportDistance + placementSurfaceGap);

            previewCargoPosition = previewColliderCenter - previewCargoRotation * scaledCenter;
            if (IsForbiddenPlacement(hit.collider, previewColliderCenter, halfSize, previewCargoRotation))
            {
                SetPlacementPreviewVisible(false);
                return;
            }

            // 프리뷰의 피벗은 BoxCollider 중심이 아니라 화물 루트다. 실제 시각 계층을
            // 그대로 복제했으므로, 놓일 화물과 같은 루트 자세를 써야 실루엣과 프록시가
            // 일치한다.
            placementPreview.transform.SetPositionAndRotation(previewCargoPosition, previewCargoRotation);
            placementPreview.transform.localScale = Vector3.one;
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

            if (heldBody != null && Input.GetKey(KeyCode.Q))
            {
                RotatePlacementPreview(Time.deltaTime);
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
            heldCollider = null;
            heldColliderCenter = Vector3.zero;
            heldColliderHalfSize = Vector3.zero;
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
            placementPreview = new GameObject("CargoPlacementPreview");
            placementPreview.name = "CargoPlacementPreview";
            placementPreview.layer = LayerMask.NameToLayer("Ignore Raycast");
            placementPreviewMaterial = CreateTransparentPreviewMaterial();
            CopyHeldCargoVisualsToPreview();
        }

        /// <summary>
        /// 물리 프록시를 확대해 보이던 기존 큐브 대신, 실제 화물의 렌더러
        /// 계층을 복제한다. 프리뷰에는 충돌체나 방향 마커를 추가하지 않는다.
        /// </summary>
        private void CopyHeldCargoVisualsToPreview()
        {
            bool copiedVisual = false;
            foreach (Transform child in heldBody.transform)
            {
                if (child.GetComponentInChildren<Renderer>(true) == null)
                {
                    continue;
                }

                GameObject visualCopy = Instantiate(child.gameObject, placementPreview.transform, false);
                visualCopy.name = $"PreviewVisual_{child.name}";
                visualCopy.transform.SetLocalPositionAndRotation(child.localPosition, child.localRotation);
                visualCopy.transform.localScale = child.localScale;
                PreparePreviewVisual(visualCopy);
                copiedVisual = true;
            }

            // 테스트용 단순 화물처럼 Renderer가 Cargo 루트에 직접 붙은 경우에도, 같은
            // 메시를 사용한다. 실제 씬 화물은 위 분기로 ImportedVisual 계층을 복제한다.
            if (!copiedVisual)
            {
                CopyRootRendererToPreview();
            }
        }

        private void CopyRootRendererToPreview()
        {
            MeshFilter sourceFilter = heldBody.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = heldBody.GetComponent<MeshRenderer>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null || sourceRenderer == null)
            {
                return;
            }

            var visualCopy = new GameObject("PreviewVisual_Root");
            visualCopy.transform.SetParent(placementPreview.transform, false);
            visualCopy.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            visualCopy.AddComponent<MeshRenderer>().sharedMaterial = placementPreviewMaterial;
            PreparePreviewVisual(visualCopy);
        }

        private void PreparePreviewVisual(GameObject visual)
        {
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                DestroyRuntimeObject(collider);
            }

            foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                DestroyRuntimeObject(body);
            }

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = placementPreviewMaterial;
            }

            SetLayerRecursively(visual.transform, placementPreview.layer);
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

        private static bool IsForbiddenPlacement(
            Collider supportCollider,
            Vector3 cargoCenter,
            Vector3 cargoHalfSize,
            Quaternion cargoRotation)
        {
            // 캐빈 지붕처럼 미리보기 프록시와 미세하게 떨어진 지지면도 금지한다.
            if (supportCollider.GetComponentInParent<CargoPlacementForbiddenVolume>() != null)
            {
                return true;
            }

            // 지지면은 짐칸이어도, 화물 프록시 일부가 캐빈에 침범하는 배치는 막는다.
            Collider[] overlaps = Physics.OverlapBox(
                cargoCenter,
                cargoHalfSize,
                cargoRotation,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            foreach (Collider overlap in overlaps)
            {
                if (overlap.GetComponentInParent<CargoPlacementForbiddenVolume>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static float ProjectHalfSizeOntoNormal(Vector3 halfSize, Quaternion rotation, Vector3 normal)
        {
            return Mathf.Abs(Vector3.Dot(rotation * Vector3.right, normal)) * halfSize.x
                + Mathf.Abs(Vector3.Dot(rotation * Vector3.up, normal)) * halfSize.y
                + Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, normal)) * halfSize.z;
        }

        private static bool TryGetPlacementProxy(
            Collider collider,
            out Vector3 center,
            out Vector3 halfSize)
        {
            switch (collider)
            {
                case BoxCollider box:
                    center = box.center;
                    halfSize = box.size * 0.5f;
                    return true;

                case CapsuleCollider capsule:
                    center = capsule.center;
                    halfSize = Vector3.one * capsule.radius;
                    halfSize[capsule.direction] =
                        Mathf.Max(capsule.height * 0.5f, capsule.radius);
                    return true;

                default:
                    center = Vector3.zero;
                    halfSize = Vector3.zero;
                    return false;
            }
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static float NormalizeYaw(float yaw)
        {
            return Mathf.Repeat(yaw, 360f);
        }

        private static void SetLayerRecursively(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            foreach (Transform child in target)
            {
                SetLayerRecursively(child, layer);
            }
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

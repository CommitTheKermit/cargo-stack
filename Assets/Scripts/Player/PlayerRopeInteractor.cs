using System.Collections.Generic;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 1인칭으로 짐을 로프로 묶는다. 짐을 쌓는 <see cref="PlayerCargoInteractor"/> 와 짝을 이루는 조작이다.
    ///
    /// R 로 한쪽 끝을 걸고, 걸어가서 다시 R 로 반대쪽 끝을 건다. 두 점은 어디든 좋다.
    /// 짐 위든 짐칸 벽이든 차체든 지면이든, 조준해서 맞은 곳이면 매듭이 된다.
    /// 거는 순간 로프는 팽팽해지며 사이에 있는 짐을 눌러 앉힌다.
    ///
    /// 로프는 스테이지마다 정해진 개수만 준다. 어디에 쓸지 고르는 것이 이 장비의 실력 요소다.
    /// 잘못 걸었으면 X 로 걷어 내면 그대로 되돌아온다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCargoInteractor))]
    public sealed class PlayerRopeInteractor : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;

        [Tooltip("매듭을 지을 수 있는 거리. 화물을 놓을 면을 찾는 거리와 같게 두는 것이 기본이다.")]
        [SerializeField] private float ropeReach = 6f;

        [Tooltip("이 스테이지에서 쓸 수 있는 로프 개수.")]
        [SerializeField, Min(0)] private int ropeCount = 2;

        [SerializeField] private RopeSettings ropeSettings = new RopeSettings();

        [Tooltip("걸리기 전 미리보기 색.")]
        [SerializeField] private Color previewColor = new Color(0.35f, 0.95f, 0.55f, 0.75f);

        /// <summary>걸린 로프를 조준할 때 허용하는 빗나감. 로프가 얇아 정조준을 요구하면 걷어 내기가 고역이 된다.</summary>
        private const float RopeAimRadius = 0.12f;

        private readonly List<Rope> ropes = new List<Rope>();
        private PlayerCargoInteractor cargoInteractor;
        private Collider[] playerColliders;
        private LineRenderer preview;
        private Material previewMaterial;
        private RopeAttachment pendingStart;
        private bool hasPendingStart;

        /// <summary>아직 쓰지 않은 로프 개수.</summary>
        public int RemainingRopes => Mathf.Max(0, ropeCount - ropes.Count);

        /// <summary>한쪽 끝을 걸어 두고 반대쪽을 찾는 중인가.</summary>
        public bool IsTyingKnot => hasPendingStart;

        public int TiedRopeCount => ropes.Count;

        public void Configure(Camera cameraToUse, int availableRopes)
        {
            viewCamera = cameraToUse;
            ropeCount = Mathf.Max(0, availableRopes);
        }

        /// <summary>조준한 자리에 매듭을 짓는다. 첫 호출은 시작점, 두 번째 호출이 로프를 완성한다.</summary>
        public bool TryTieFromView()
        {
            // 짐을 든 손으로는 로프를 다루지 않는다. 놓고 나서 묶는다.
            if (cargoInteractor.HasCargo || !TryAimAttachment(out RopeAttachment attachment))
            {
                return false;
            }

            if (!hasPendingStart)
            {
                if (RemainingRopes <= 0)
                {
                    return false;
                }

                pendingStart = attachment;
                hasPendingStart = true;
                return true;
            }

            Rope rope = Rope.Create(pendingStart, attachment, ropeSettings, playerColliders);
            if (rope == null)
            {
                return false;
            }

            ropes.Add(rope);
            hasPendingStart = false;
            HidePreview();
            return true;
        }

        /// <summary>
        /// 짓던 매듭을 무르거나, 조준한 로프를 걷어 낸다.
        /// 걷어 낸 로프는 다시 쓸 수 있다.
        /// </summary>
        public bool TryUntieFromView()
        {
            if (hasPendingStart)
            {
                hasPendingStart = false;
                HidePreview();
                return true;
            }

            if (viewCamera == null)
            {
                return false;
            }

            // 로프는 손가락만큼 얇다. 조준점이 정확히 얹히기를 요구하면 걷어 내기가 고역이 된다.
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.SphereCast(ray, RopeAimRadius, out RaycastHit hit, ropeReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Rope rope = hit.collider.GetComponentInParent<Rope>();
            if (rope == null || !ropes.Remove(rope))
            {
                return false;
            }

            rope.Remove();
            return true;
        }

        private void Awake()
        {
            cargoInteractor = GetComponent<PlayerCargoInteractor>();
            playerColliders = GetComponentsInChildren<Collider>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                TryTieFromView();
            }

            if (Input.GetKeyDown(KeyCode.X))
            {
                TryUntieFromView();
            }
        }

        private void LateUpdate()
        {
            RefreshPreview();
        }

        private void OnDisable()
        {
            // 출발하면 플레이어가 사라진다. 짓다 만 매듭은 없던 일이 되고, 걸린 로프는 남는다.
            hasPendingStart = false;
            HidePreview();
        }

        /// <summary>
        /// 걸기 전에 로프가 어디를 지날지 보여 준다. 실제로 걸릴 선과 같은 계산을 쓴다.
        /// </summary>
        private void RefreshPreview()
        {
            if (!hasPendingStart || cargoInteractor.HasCargo || !TryAimAttachment(out RopeAttachment attachment))
            {
                HidePreview();
                return;
            }

            List<Vector3> path = Rope.SolveWorldPath(pendingStart, attachment, ropeSettings, playerColliders);
            if (path.Count < 2)
            {
                HidePreview();
                return;
            }

            EnsurePreview();
            preview.positionCount = path.Count;
            for (int index = 0; index < path.Count; index++)
            {
                preview.SetPosition(index, path[index]);
            }

            preview.enabled = true;
        }

        /// <summary>조준선이 맞은 자리를 매듭 자리로 바꾼다. 이미 걸린 로프 위에는 묶지 못한다.</summary>
        private bool TryAimAttachment(out RopeAttachment attachment)
        {
            attachment = default;
            if (viewCamera == null)
            {
                return false;
            }

            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, ropeReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (hit.collider.GetComponentInParent<Rope>() != null)
            {
                return false;
            }

            attachment = RopeAttachment.FromHit(hit);
            return true;
        }

        private void EnsurePreview()
        {
            if (preview != null)
            {
                return;
            }

            var holder = new GameObject("RopePreview");
            holder.transform.SetParent(transform, false);
            holder.layer = LayerMask.NameToLayer("Ignore Raycast");

            preview = holder.AddComponent<LineRenderer>();
            preview.useWorldSpace = true;
            preview.startWidth = ropeSettings.Radius * 2f;
            preview.endWidth = ropeSettings.Radius * 2f;
            preview.numCapVertices = 4;
            preview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            preview.receiveShadows = false;

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            previewMaterial = new Material(shader)
            {
                name = "RopePreviewMaterial",
                color = previewColor,
            };
            preview.sharedMaterial = previewMaterial;
        }

        private void HidePreview()
        {
            if (preview != null)
            {
                preview.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (previewMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(previewMaterial);
            }
            else
            {
                DestroyImmediate(previewMaterial);
            }
        }
    }
}

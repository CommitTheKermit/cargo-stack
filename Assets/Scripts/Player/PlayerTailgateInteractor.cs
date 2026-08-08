using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 화면 중앙으로 테일게이트를 조준해 E로 여닫는다.
    /// 같은 키를 쓰는 화물 조작과 겹치지 않도록 손에 화물이 없을 때만 동작한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCargoInteractor))]
    public sealed class PlayerTailgateInteractor : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private float interactionRange = 3f;

        private PlayerCargoInteractor cargoInteractor;

        public void Configure(Camera cameraToUse)
        {
            viewCamera = cameraToUse;
        }

        public bool TryToggleFromView()
        {
            if (viewCamera == null || cargoInteractor.HasCargo)
            {
                return false;
            }

            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    interactionRange,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return TryToggle(hit.collider.GetComponentInParent<TruckTailgate>());
        }

        public bool TryToggle(TruckTailgate tailgate)
        {
            if (tailgate == null
                || cargoInteractor.HasCargo
                || tailgate.IsLockedForDriving
                || Vector3.Distance(transform.position, tailgate.transform.position) > interactionRange)
            {
                return false;
            }

            tailgate.Toggle();
            return true;
        }

        private void Awake()
        {
            cargoInteractor = GetComponent<PlayerCargoInteractor>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                TryToggleFromView();
            }
        }
    }
}

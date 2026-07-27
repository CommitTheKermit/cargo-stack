using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 이 게임의 핵심 조작(기획서 3.2). 적재 단계에서만 동작한다.
    /// 좌클릭으로 짐을 집고, 마우스로 옮기고, R 로 돌리고, 다시 좌클릭으로 놓는다.
    /// 시작 전까지는 몇 번이든 다시 집어 재배치할 수 있다.
    /// </summary>
    public class CargoPlacer : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField] private LayerMask cargoMask;

        [Tooltip("짐을 들었을 때 커서와 짐 중심 사이의 세로 간격.")]
        [SerializeField] private float holdLift = 0f;

        private Cargo held;
        private bool interactable = true;

        public bool HasHeldCargo => held != null;

        /// <summary>적재 단계가 끝나면 GameFlow 가 꺼 준다. 들고 있던 짐은 그 자리에 놓인다.</summary>
        public void SetInteractable(bool value)
        {
            interactable = value;

            if (!value)
            {
                ReleaseHeld();
            }
        }

        private void Update()
        {
            if (!interactable)
            {
                return;
            }

            if (held == null)
            {
                TryPickUp();
                return;
            }

            DragHeld();
        }

        private void TryPickUp()
        {
            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            Ray ray = view.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f, cargoMask, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Cargo cargo = hit.collider.GetComponentInParent<Cargo>();
            if (cargo == null)
            {
                return;
            }

            held = cargo;
            held.Hold();
        }

        private void DragHeld()
        {
            if (TryGetPointerOnPlane(out Vector3 point))
            {
                held.MoveTo(new Vector3(point.x, point.y + holdLift, 0f));
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                held.RotateStep();
            }

            held.ShowPlacementValidity(!held.IsOverlappingOthers());

            if (Input.GetMouseButtonDown(0))
            {
                ReleaseHeld();
            }
        }

        private void ReleaseHeld()
        {
            if (held == null)
            {
                return;
            }

            held.Release();
            held = null;
        }

        /// <summary>화면 좌표를 게임플레이 평면(z=0) 위의 한 점으로 바꾼다.</summary>
        private bool TryGetPointerOnPlane(out Vector3 point)
        {
            var plane = new Plane(Vector3.back, Vector3.zero);
            Ray ray = view.ScreenPointToRay(Input.mousePosition);

            if (plane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }

            point = Vector3.zero;
            return false;
        }
    }
}

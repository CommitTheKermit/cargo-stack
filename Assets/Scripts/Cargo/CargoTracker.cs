using System;
using System.Collections.Generic;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 짐이 짐칸 위에 남아 있는지 감시한다(기획서 4.2).
    /// 트리거 콜백 호출 순서에 의존하지 않도록, 짐칸 로컬 좌표 기준 범위 검사로 판정한다.
    /// 적재 단계에는 짐이 아직 짐칸 밖에 있으므로 주행이 시작된 뒤부터 감시한다.
    /// </summary>
    public class CargoTracker : MonoBehaviour
    {
        [SerializeField] private Transform bedAnchor;

        [Tooltip("이 범위를 벗어나면 떨어진 것으로 본다. 짐칸 로컬 좌표 기준.")]
        [SerializeField] private Vector3 keepHalfExtents = new Vector3(2.6f, 4f, 2f);

        [SerializeField] private List<Cargo> tracked = new List<Cargo>();

        private readonly HashSet<Cargo> dropped = new HashSet<Cargo>();
        private bool isWatching;

        public int TotalCount => tracked.Count;
        public int DroppedCount => dropped.Count;
        public int RemainingCount => tracked.Count - dropped.Count;

        /// <summary>짐 하나가 짐칸을 벗어났다. 기획서 5장의 팀 인터페이스 이벤트.</summary>
        public event Action<Cargo> CargoDropped;

        public void BeginWatch()
        {
            isWatching = true;
        }

        private void FixedUpdate()
        {
            if (!isWatching)
            {
                return;
            }

            foreach (Cargo cargo in tracked)
            {
                if (cargo == null || dropped.Contains(cargo))
                {
                    continue;
                }

                if (IsOnBoard(cargo))
                {
                    continue;
                }

                dropped.Add(cargo);
                CargoDropped?.Invoke(cargo);
            }
        }

        private bool IsOnBoard(Cargo cargo)
        {
            Vector3 local = bedAnchor.InverseTransformPoint(cargo.transform.position);

            return Mathf.Abs(local.x) <= keepHalfExtents.x
                && Mathf.Abs(local.y) <= keepHalfExtents.y
                && Mathf.Abs(local.z) <= keepHalfExtents.z;
        }
    }
}

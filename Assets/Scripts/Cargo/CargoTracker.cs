using System;
using System.Collections.Generic;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 짐이 짐칸 위에 남아 있는지 감시한다(기획서 4.2).
    /// 트리거 콜백 호출 순서에 의존하지 않도록, 짐칸 로컬 좌표 기준 범위 검사로 판정한다.
    /// 적재 단계에는 짐이 아직 바닥에 널려 있으므로 주행이 시작된 뒤부터 감시한다.
    ///
    /// 위아래 여유를 따로 둔다. 위로는 높이 쌓을 수 있어야 하고,
    /// 아래로는 짐칸 바닥을 조금만 벗어나도 떨어진 것으로 봐야 하기 때문이다.
    /// </summary>
    public class CargoTracker : MonoBehaviour
    {
        [SerializeField] private Transform bedAnchor;

        [Tooltip("짐칸 로컬 좌표 기준 허용 범위의 최소 모서리. y 는 바닥에 떨어진 짐과 구분될 만큼 얕아야 한다.")]
        [SerializeField] private Vector3 keepMin = new Vector3(-2.2f, -0.35f, -1.6f);

        [Tooltip("짐칸 로컬 좌표 기준 허용 범위의 최대 모서리. y 는 쌓을 수 있는 높이다.")]
        [SerializeField] private Vector3 keepMax = new Vector3(2.2f, 4f, 1.6f);

        [SerializeField] private List<Cargo> tracked = new List<Cargo>();

        private readonly HashSet<Cargo> dropped = new HashSet<Cargo>();
        private bool isWatching;

        public int TotalCount => tracked.Count;
        public int DroppedCount => dropped.Count;
        public int RemainingCount => tracked.Count - dropped.Count;

        /// <summary>모두 옮기면 3개, 하나라도 잃으면 2개, 반 이상 잃으면 1개, 전부 잃으면 0개.</summary>
        public int StarRating => CalculateStars(TotalCount, DroppedCount);

        public static int CalculateStars(int total, int dropped)
        {
            if (dropped == 0)
            {
                return 3;
            }

            if (total > 0 && dropped >= total)
            {
                return 0;
            }

            if (dropped * 2 >= total)
            {
                return 1;
            }

            return 2;
        }

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

            return local.x >= keepMin.x && local.x <= keepMax.x
                && local.y >= keepMin.y && local.y <= keepMax.y
                && local.z >= keepMin.z && local.z <= keepMax.z;
        }
    }
}

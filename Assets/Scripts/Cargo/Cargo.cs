using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 짐 한 개. PlayerCargoInteractor 가 집을 대상을 표시하고, 낙하 판정 대상이 된다.
    ///
    /// 평면 고정(Freeze Position Z)을 쓰지 않는다. 1인칭으로 짐칸 안을 돌아다니며 쌓는
    /// 이상 깊이 방향 배치가 퍼즐의 일부이므로, 짐은 온전한 3D 강체여야 한다.
    /// 대신 짐칸에 네 방향 벽을 세워 굴러떨어지는 경계를 만든다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Cargo : MonoBehaviour
    {
        private Rigidbody body;

        private void Awake()
        {
            int cargoLayer = LayerMask.NameToLayer("Cargo");
            int obstacleLayer = LayerMask.NameToLayer("Obstacle");
            if (cargoLayer >= 0 && obstacleLayer >= 0)
            {
                Physics.IgnoreLayerCollision(cargoLayer, obstacleLayer, true);
            }
        }

        public Rigidbody Body
        {
            get
            {
                if (body == null)
                {
                    body = GetComponent<Rigidbody>();
                }

                return body;
            }
        }

        /// <summary>부서졌는지. CargoTracker 는 부서진 짐을 항상 짐칸 밖으로 본다.</summary>
        public bool IsBroken { get; private set; }

        public void MarkBroken()
        {
            IsBroken = true;
        }
    }
}

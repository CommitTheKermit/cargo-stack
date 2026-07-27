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
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Cargo : MonoBehaviour
    {
        private Rigidbody body;

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
    }
}

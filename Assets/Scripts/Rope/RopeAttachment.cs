using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 로프 한쪽 끝이 묶인 자리.
    ///
    /// 짐칸 벽이든 짐 위든 차체든 지면이든, 조준해서 맞은 곳이면 어디나 부착점이 된다.
    /// 대상이 움직이면 매듭도 따라가야 하므로 월드 좌표가 아니라 <b>대상 기준 로컬 좌표</b>로 들고 있는다.
    /// 짐 위에 묶은 매듭은 그 짐이 미끄러지면 같이 끌려가야 로프가 의미를 갖는다.
    /// </summary>
    public readonly struct RopeAttachment
    {
        /// <summary>묶인 강체. 지면처럼 강체가 없는 곳에 묶었다면 null 이고, 그때는 월드에 고정된다.</summary>
        public readonly Rigidbody Body;

        /// <summary>로컬 좌표의 기준. Body 가 있으면 그 트랜스폼이고, 없으면 null 이다.</summary>
        public readonly Transform Frame;

        public readonly Vector3 LocalPoint;

        private RopeAttachment(Rigidbody body, Transform frame, Vector3 localPoint)
        {
            Body = body;
            Frame = frame;
            LocalPoint = localPoint;
        }

        public bool IsValid => Frame != null || Body == null;

        public Vector3 WorldPoint => Frame == null ? LocalPoint : Frame.TransformPoint(LocalPoint);

        /// <summary>조준이 맞은 표면을 부착점으로 삼는다.</summary>
        public static RopeAttachment FromHit(RaycastHit hit)
        {
            return At(hit.collider.attachedRigidbody, hit.point);
        }

        /// <summary>강체 위의 한 점에 묶는다. 강체가 없으면 월드에 고정된다.</summary>
        public static RopeAttachment At(Rigidbody body, Vector3 worldPoint)
        {
            if (body == null)
            {
                // 지면처럼 강체가 없는 곳. 움직이지 않으므로 월드 좌표를 그대로 쓴다.
                return new RopeAttachment(null, null, worldPoint);
            }

            return new RopeAttachment(body, body.transform, body.transform.InverseTransformPoint(worldPoint));
        }
    }
}

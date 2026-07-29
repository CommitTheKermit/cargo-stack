using System.Collections.Generic;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 두 부착점 사이에 걸린 로프 한 가닥. 짐을 실제로 눌러 고정하는 물건이다.
    ///
    /// 진짜 밧줄로 만든다. 짧은 캡슐 강체를 줄줄이 이어 조인트로 묶고, 그 사슬이 짐 위를 덮는다.
    /// "로프가 걸렸으니 짐을 고정된 것으로 친다"는 판정이 아니라, 사슬이 짐과 부딪히며 생기는
    /// 접촉력이 곧 고정력이다. 그래서 로프를 어디에 어떻게 걸었느냐가 결과를 바꾼다.
    ///
    /// 거는 순간 팽팽하다. 걸 자리를 <see cref="RopePathSolver"/> 가 미리 구해 두고,
    /// 그 선을 따라 세그먼트를 깔기 때문에 늘어진 밧줄이 물리로 안착하기를 기다릴 필요가 없다.
    ///
    /// 기획서 7장 리스크 표는 초급 팀의 학습 비용을 이유로 조인트를 MVP에서 배제했다.
    /// 로프는 확장 로드맵(6장 2번)의 첫 장비이므로 여기서 그 제약을 푼다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Rope : MonoBehaviour
    {
        /// <summary>세그먼트 수 상한. 긴 로프 하나가 물리 예산을 통째로 먹는 것을 막는다.</summary>
        private const int MaximumSegmentCount = 64;

        /// <summary>높이를 재는 광선의 출발 높이. 두 부착점 중 높은 쪽에서 이만큼 더 위에서 쏜다.</summary>
        private const float HeightProbeClearance = 6f;

        /// <summary>높이 판정용 공용 버퍼. 한 지점 위아래로 이보다 많이 겹칠 일은 없다.</summary>
        private static readonly RaycastHit[] HeightProbeBuffer = new RaycastHit[32];

        private readonly List<Rigidbody> segments = new List<Rigidbody>();
        private RopeAttachment start;
        private RopeAttachment end;
        private LineRenderer line;
        private Material lineMaterial;
        private PhysicsMaterial surfaceMaterial;

        public int SegmentCount => segments.Count;

        /// <summary>
        /// 두 부착점을 잇는 로프를 만든다. 이을 수 없는 자리면 null 을 준다.
        /// </summary>
        /// <param name="ignoredColliders">높이를 잴 때와 충돌 처리에서 무시할 것들. 플레이어 몸통이 여기 들어간다.</param>
        public static Rope Create(
            RopeAttachment start,
            RopeAttachment end,
            RopeSettings settings,
            IReadOnlyList<Collider> ignoredColliders)
        {
            List<Vector3> path = SolveWorldPath(start, end, settings, ignoredColliders);
            if (path.Count < 2)
            {
                return null;
            }

            var holder = new GameObject("Rope");
            Rope rope = holder.AddComponent<Rope>();
            rope.Build(start, end, settings, path, ignoredColliders);
            return rope;
        }

        /// <summary>
        /// 팽팽하게 걸었을 때 로프가 지날 자리를 구한다. 미리보기도 같은 선을 쓴다.
        /// 두 점이 너무 멀면 빈 목록을 준다.
        /// </summary>
        public static List<Vector3> SolveWorldPath(
            RopeAttachment start,
            RopeAttachment end,
            RopeSettings settings,
            IReadOnlyList<Collider> ignoredColliders)
        {
            var path = new List<Vector3>();
            Vector3 from = start.WorldPoint;
            Vector3 to = end.WorldPoint;
            float span = Vector3.Distance(from, to);

            // 너무 멀면 한 가닥으로 못 잇고, 너무 가까우면 매듭 두 개가 겹쳐 사슬이 터진다.
            if (span > settings.MaximumLength || span < settings.SegmentLength * 2f)
            {
                return path;
            }

            Vector3 horizontalStep = to - from;
            horizontalStep.y = 0f;
            float horizontalSpan = horizontalStep.magnitude;

            // 두 점이 수직으로 겹치면 넘어갈 봉우리라는 개념이 없다. 곧장 잇는다.
            if (horizontalSpan < settings.HeightSampleSpacing)
            {
                path.Add(from);
                path.Add(to);
                return path;
            }

            Vector3 forward = horizontalStep / horizontalSpan;
            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(horizontalSpan / settings.HeightSampleSpacing) + 1);
            float probeHeight = Mathf.Max(from.y, to.y) + HeightProbeClearance;

            // 아무것도 재지 못한 자리에 쓸 높이. 두 부착점보다 확실히 낮아야
            // 팽팽한 선 계산에서 저절로 빠진다.
            float emptyHeight = Mathf.Min(from.y, to.y) - HeightProbeClearance;

            var profile = new List<Vector2>(sampleCount);
            for (int index = 0; index < sampleCount; index++)
            {
                float ratio = index / (float)(sampleCount - 1);
                float distance = horizontalSpan * ratio;
                Vector3 groundSample = from + forward * distance;

                float height;
                if (index == 0)
                {
                    height = from.y;
                }
                else if (index == sampleCount - 1)
                {
                    height = to.y;
                }
                else
                {
                    // 부착점을 잇는 직선보다 낮은 곳은 로프가 닿지 않는다. 볼록 껍질이 알아서 걸러낸다.
                    height = MeasureSurfaceHeight(
                        new Vector3(groundSample.x, probeHeight, groundSample.z),
                        probeHeight - emptyHeight,
                        ignoredColliders,
                        settings.Radius,
                        emptyHeight);
                }

                profile.Add(new Vector2(distance, height));
            }

            foreach (Vector2 point in RopePathSolver.SolveTautLine(profile))
            {
                path.Add(from + forward * point.x + Vector3.up * (point.y - from.y));
            }

            return path;
        }

        /// <summary>
        /// 한 지점 위에서 아래로 광선을 쏴 로프가 얹힐 높이를 잰다.
        /// 아무것도 없으면 <paramref name="emptyHeight"/> 를 준다.
        /// </summary>
        private static float MeasureSurfaceHeight(
            Vector3 origin,
            float distance,
            IReadOnlyList<Collider> ignoredColliders,
            float ropeRadius,
            float emptyHeight)
        {
            // 미리보기가 매 프레임 이 선을 다시 풀기 때문에 할당 없는 판정을 쓴다.
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                HeightProbeBuffer,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            bool found = false;
            float highest = emptyHeight;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = HeightProbeBuffer[index];
                if (ShouldIgnore(hit.collider, ignoredColliders))
                {
                    continue;
                }

                highest = found ? Mathf.Max(highest, hit.point.y) : hit.point.y;
                found = true;
            }

            // 표면에 파묻히지 않도록 굵기의 절반만큼 띄운다.
            return found ? highest + ropeRadius : emptyHeight;
        }

        private static bool ShouldIgnore(Collider candidate, IReadOnlyList<Collider> ignoredColliders)
        {
            // 이미 걸린 로프 위에 또 얹지 않는다. 로프끼리 층을 이루면 사슬이 서로를 밀어낸다.
            if (candidate.GetComponentInParent<Rope>() != null)
            {
                return true;
            }

            if (ignoredColliders == null)
            {
                return false;
            }

            for (int index = 0; index < ignoredColliders.Count; index++)
            {
                if (ignoredColliders[index] == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>로프를 걷어 낸다. 회수한 로프는 다시 쓸 수 있다.</summary>
        public void Remove()
        {
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }

        private void Build(
            RopeAttachment startAttachment,
            RopeAttachment endAttachment,
            RopeSettings settings,
            IReadOnlyList<Vector3> path,
            IReadOnlyList<Collider> ignoredColliders)
        {
            start = startAttachment;
            end = endAttachment;

            // 로프가 짐 위에서 미끄러져 벗겨지면 아무것도 잡아 주지 못한다. 잘 물리는 표면으로 둔다.
            surfaceMaterial = new PhysicsMaterial("RopeSurface")
            {
                dynamicFriction = 0.9f,
                staticFriction = 0.9f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };

            Vector3[] knots = SplitIntoKnots(path, settings.SegmentLength);
            float segmentMass = settings.TotalMass / (knots.Length - 1);

            for (int index = 0; index < knots.Length - 1; index++)
            {
                segments.Add(CreateSegment(knots[index], knots[index + 1], segmentMass, settings));
            }

            ConnectSegments(knots);
            IgnoreInternalCollisions(ignoredColliders);
            CreateLine(settings);
        }

        /// <summary>꺾임점만 있는 선을 세그먼트 경계점으로 잘게 나눈다.</summary>
        private static Vector3[] SplitIntoKnots(IReadOnlyList<Vector3> path, float segmentLength)
        {
            float total = 0f;
            for (int index = 0; index < path.Count - 1; index++)
            {
                total += Vector3.Distance(path[index], path[index + 1]);
            }

            int segmentCount = Mathf.Clamp(
                Mathf.RoundToInt(total / segmentLength),
                2,
                MaximumSegmentCount);

            var knots = new Vector3[segmentCount + 1];
            float step = total / segmentCount;
            for (int index = 0; index <= segmentCount; index++)
            {
                knots[index] = PointAtDistance(path, step * index);
            }

            return knots;
        }

        private static Vector3 PointAtDistance(IReadOnlyList<Vector3> path, float distance)
        {
            float travelled = 0f;
            for (int index = 0; index < path.Count - 1; index++)
            {
                float span = Vector3.Distance(path[index], path[index + 1]);
                if (travelled + span >= distance || index == path.Count - 2)
                {
                    float ratio = span <= Mathf.Epsilon ? 0f : Mathf.Clamp01((distance - travelled) / span);
                    return Vector3.Lerp(path[index], path[index + 1], ratio);
                }

                travelled += span;
            }

            return path[path.Count - 1];
        }

        private Rigidbody CreateSegment(Vector3 from, Vector3 to, float mass, RopeSettings settings)
        {
            Vector3 axis = to - from;
            float length = axis.magnitude;

            var segment = new GameObject($"Segment_{segments.Count:000}");
            segment.transform.SetParent(transform, false);
            segment.transform.position = (from + to) * 0.5f;
            if (length > Mathf.Epsilon)
            {
                segment.transform.rotation = Quaternion.LookRotation(axis / length, Vector3.up);
            }

            CapsuleCollider capsule = segment.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.radius = settings.Radius;
            capsule.height = Mathf.Max(length, settings.Radius * 2f);
            capsule.sharedMaterial = surfaceMaterial;

            Rigidbody body = segment.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = 0.6f;
            body.angularDamping = 0.6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            // 트럭 위에서 빠르게 움직이는 얇은 물체라, 기본 판정으로는 짐을 뚫고 지나간다.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // 사슬은 기본 반복 횟수로는 늘어난다. 로프에만 더 준다.
            body.solverIterations = 24;
            body.solverVelocityIterations = 8;
            return body;
        }

        private void ConnectSegments(IReadOnlyList<Vector3> knots)
        {
            for (int index = 0; index < segments.Count; index++)
            {
                Rigidbody previous = index == 0 ? start.Body : segments[index - 1];
                LockTogether(segments[index], knots[index], previous);
            }

            LockTogether(segments[segments.Count - 1], knots[knots.Count - 1], end.Body);
        }

        /// <summary>
        /// 한 점에서 두 강체를 붙인다. 위치는 잠그고 회전만 열어 둔 조인트라,
        /// 로프는 자유롭게 꺾이되 늘어나지는 않는다.
        /// </summary>
        private static void LockTogether(Rigidbody body, Vector3 worldAnchor, Rigidbody connectedBody)
        {
            ConfigurableJoint joint = body.gameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = body.transform.InverseTransformPoint(worldAnchor);
            joint.connectedBody = connectedBody;
            joint.connectedAnchor = connectedBody == null
                ? worldAnchor
                : connectedBody.transform.InverseTransformPoint(worldAnchor);

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Free;

            // 사슬이 한 번 벌어지면 스스로 돌아오지 못한다. 벌어진 만큼 매 스텝 되돌린다.
            // 마디마다 조금씩 늘어난 것이 누적되면 로프 전체가 늘어나므로 허용치를 짧게 잡는다.
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.002f;
            joint.projectionAngle = 3f;

            joint.enablePreprocessing = false;
            joint.enableCollision = false;
        }

        /// <summary>
        /// 로프가 자기 자신과 부딪히지 않게 한다. 사슬 마디끼리 밀어내면 로프가 튀어 오른다.
        /// 짐이나 짐칸과는 부딪혀야 하므로 그쪽은 건드리지 않는다.
        /// </summary>
        private void IgnoreInternalCollisions(IReadOnlyList<Collider> ignoredColliders)
        {
            var colliders = new List<Collider>(segments.Count);
            foreach (Rigidbody segment in segments)
            {
                colliders.Add(segment.GetComponent<Collider>());
            }

            for (int first = 0; first < colliders.Count; first++)
            {
                for (int second = first + 1; second < colliders.Count; second++)
                {
                    Physics.IgnoreCollision(colliders[first], colliders[second], true);
                }

                if (ignoredColliders == null)
                {
                    continue;
                }

                for (int index = 0; index < ignoredColliders.Count; index++)
                {
                    if (ignoredColliders[index] != null)
                    {
                        Physics.IgnoreCollision(colliders[first], ignoredColliders[index], true);
                    }
                }
            }
        }

        private void CreateLine(RopeSettings settings)
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = segments.Count + 1;
            line.startWidth = settings.Radius * 2f;
            line.endWidth = settings.Radius * 2f;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            lineMaterial = new Material(shader)
            {
                name = "RopeMaterial",
                color = settings.Color,
            };
            line.sharedMaterial = lineMaterial;
            RedrawLine();
        }

        private void LateUpdate()
        {
            RedrawLine();
        }

        private void RedrawLine()
        {
            if (line == null || segments.Count == 0)
            {
                return;
            }

            line.SetPosition(0, start.WorldPoint);
            for (int index = 0; index < segments.Count - 1; index++)
            {
                // 이웃한 두 마디가 만나는 자리가 곧 매듭이다.
                line.SetPosition(index + 1, (segments[index].position + segments[index + 1].position) * 0.5f);
            }

            line.SetPosition(segments.Count, end.WorldPoint);
        }

        private void OnDestroy()
        {
            DestroyRuntimeAsset(lineMaterial);
            DestroyRuntimeAsset(surfaceMaterial);
        }

        private static void DestroyRuntimeAsset(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(asset);
            }
            else
            {
                DestroyImmediate(asset);
            }
        }
    }
}

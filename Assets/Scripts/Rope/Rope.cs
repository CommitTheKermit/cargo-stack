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
        private MeshFilter visualMeshFilter;
        private MeshRenderer visualRenderer;
        private Mesh visualMesh;
        private Material visualMaterial;
        private PhysicsMaterial surfaceMaterial;
        private float visualRadius;

        // HE_Rope_Tool.hda's "Rope" style rolls a small circular profile around
        // the input curve. The runtime player cannot cook the HDA without
        // Houdini Engine, so the visual fallback below uses three circular
        // strands braided around that same curve. The 0.05 m curve resolution
        // is important: rolling a sparse physics chain by 500 degrees per metre
        // creates the same pinched, ribbon-like triangles that the asset avoids
        // by resampling its input curve first.
        private const int VisualCrossSectionRows = 8;
        private const int VisualStrandCount = 3;
        private const float VisualStrandOrbitRatio = 0.52f;
        private const float VisualStrandRadiusRatio = 0.46f;
        private const float VisualSampleSpacing = 0.05f;
        private const int MaximumVisualPointCount = 512;
        private const float VisualTwistDegreesPerUnit = 500f;

        private Vector3[] visualControlPoints;
        private float[] visualControlDistances;
        private Vector3[] visualPathPoints;
        private Vector3[] visualVertices;
        private Vector2[] visualUvs;
        private int[] visualTriangles;
        private int visualControlPointCount;
        private int visualPointCount;

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
                        settings.Radius - settings.GripDepth,
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
        /// <param name="surfaceOffset">
        /// 표면에서 로프 중심선까지 띄울 거리. 굵기의 절반보다 짧게 주면 로프가 그만큼
        /// 표면을 물고 들어간 상태로 만들어지고, 물리가 그 파묻힘을 밀어내려는 힘이
        /// 곧 짐을 누르는 힘이 된다.
        /// </param>
        private static float MeasureSurfaceHeight(
            Vector3 origin,
            float distance,
            IReadOnlyList<Collider> ignoredColliders,
            float surfaceOffset,
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

            return found ? highest + surfaceOffset : emptyHeight;
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
            CreateRopeToolVisual(settings);
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

            // 주행 중 트럭 앵커가 커브·고저차로 급하게 움직이면 그 힘이 사슬을 타고 채찍처럼
            // 전달된다(실측: 마디 최고 속도가 트럭의 1.7배). 감쇠를 올려 그 진동이 잦아드는
            // 속도를 높인다. 다만 이 값 자체가 순간 최고 속도(조인트 보정)를 낮추지는 않았다
            // (실측 17.0 → 17.8m/s, 오차 범위). gripDepth 와 별개로 남은 문제로 본다.
            body.linearDamping = 1.6f;
            body.angularDamping = 1.6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            // 트럭 위에서 빠르게 움직이는 얇은 물체라, 기본 판정으로는 짐을 뚫고 지나간다.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // 사슬은 기본 반복 횟수로는 늘어난다. 로프에만 더 준다.
            body.solverIterations = 24;
            body.solverVelocityIterations = 8;

            // 얇은 마디가 짐과 짐칸 벽 사이에 끼면 물리 엔진이 기본 10m/s 까지 속도를 줘서
            // 밀어낸다. 그 튕겨나감이 곧바로 짐에 실리므로, 밀어내는 속도를 걷는 정도로 묶는다.
            body.maxDepenetrationVelocity = 1.5f;
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
        ///
        /// 매듭 자리를 어긋나게 잡아 장력을 주는 방법은 쓰지 않는다. 위치를 잠근 조인트에
        /// 만족할 수 없는 목표를 주면 솔버가 매 스텝 최대 힘을 내며 진동하고, 그 진동이
        /// 짐을 누르기는커녕 밀어낸다(실측: 위로 튄 높이가 0.057m 에서 0.329m 로 나빠졌다).
        /// 누르는 힘은 <see cref="RopeSettings.GripDepth"/> 의 접촉으로 만든다.
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

            // projection 을 쓰지 않는다. 벌어진 조인트를 순간이동으로 되돌리는 기능인데,
            // 트럭이 달리면 매듭은 한 스텝에 20cm 를 따라가고 사슬 마디는 관성으로 뒤처지므로
            // 매 스텝 허용치를 크게 넘는다. 그때마다 전 마디가 순간이동하면 속도가 불연속이 되고,
            // 그 충격이 짐을 그대로 날려 버린다(실측 마디 속도 73.7m/s, 트럭의 7배).
            // 정지 상태에서는 도움이 되지만 이 게임의 로프는 달리는 트럭 위에 있어야 한다.
            joint.projectionMode = JointProjectionMode.None;

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

        private void CreateRopeToolVisual(RopeSettings settings)
        {
            visualRadius = settings.Radius;
            visualControlPointCount = segments.Count + 2;
            visualControlPoints = new Vector3[visualControlPointCount];
            visualControlDistances = new float[visualControlPointCount];
            float initialPathLength = RefreshVisualControlPoints();
            visualPointCount = Mathf.Clamp(
                Mathf.CeilToInt(initialPathLength / VisualSampleSpacing) + 1,
                2,
                MaximumVisualPointCount);
            visualPathPoints = new Vector3[visualPointCount];

            int ringVertexCount = visualPointCount * VisualCrossSectionRows * VisualStrandCount;
            visualVertices = new Vector3[ringVertexCount + VisualStrandCount * 2];
            visualUvs = new Vector2[visualVertices.Length];
            visualTriangles = BuildVisualTriangles(visualPointCount);

            for (int pointIndex = 0; pointIndex < visualPointCount; pointIndex++)
            {
                float u = visualPointCount <= 1
                    ? 0f
                    : pointIndex / (float)(visualPointCount - 1);
                for (int strand = 0; strand < VisualStrandCount; strand++)
                {
                    for (int row = 0; row < VisualCrossSectionRows; row++)
                    {
                        int vertexIndex = GetVisualVertexIndex(pointIndex, strand, row);
                        visualUvs[vertexIndex] = new Vector2(
                            u,
                            row / (float)VisualCrossSectionRows);
                    }
                }
            }

            for (int strand = 0; strand < VisualStrandCount; strand++)
            {
                visualUvs[GetVisualStartCapIndex(visualPointCount, strand)] = new Vector2(0f, 0.5f);
                visualUvs[GetVisualEndCapIndex(visualPointCount, strand)] = new Vector2(1f, 0.5f);
            }

            visualMeshFilter = gameObject.AddComponent<MeshFilter>();
            visualRenderer = gameObject.AddComponent<MeshRenderer>();
            visualMesh = new Mesh
            {
                name = "RopeToolVisualMesh",
            };
            visualMesh.MarkDynamic();
            visualMesh.vertices = visualVertices;
            visualMesh.uv = visualUvs;
            visualMesh.triangles = visualTriangles;
            visualMeshFilter.sharedMesh = visualMesh;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            visualMaterial = new Material(shader)
            {
                name = "RopeToolVisualMaterial",
                color = settings.Color,
            };
            if (visualMaterial.HasProperty("_BaseColor"))
            {
                visualMaterial.SetColor("_BaseColor", settings.Color);
            }

            if (visualMaterial.HasProperty("_Color"))
            {
                visualMaterial.SetColor("_Color", settings.Color);
            }

            if (visualMaterial.HasProperty("_Metallic"))
            {
                visualMaterial.SetFloat("_Metallic", 0f);
            }

            if (visualMaterial.HasProperty("_Smoothness"))
            {
                visualMaterial.SetFloat("_Smoothness", 0.45f);
            }

            visualRenderer.sharedMaterial = visualMaterial;
            visualRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            visualRenderer.receiveShadows = true;
            RedrawVisual();
        }

        private void LateUpdate()
        {
            RedrawVisual();
        }

        private int[] BuildVisualTriangles(int pointCount)
        {
            int quadTriangleCount = (pointCount - 1) * VisualCrossSectionRows * VisualStrandCount * 2;
            int capTriangleCount = VisualCrossSectionRows * VisualStrandCount * 2;
            var triangles = new int[(quadTriangleCount + capTriangleCount) * 3];
            int writeIndex = 0;

            for (int strand = 0; strand < VisualStrandCount; strand++)
            {
                for (int pointIndex = 0; pointIndex < pointCount - 1; pointIndex++)
                {
                    int currentRing = GetVisualVertexIndex(pointIndex, strand, 0);
                    int nextRing = GetVisualVertexIndex(pointIndex + 1, strand, 0);
                    for (int row = 0; row < VisualCrossSectionRows; row++)
                    {
                        int nextRow = (row + 1) % VisualCrossSectionRows;
                        AddTriangle(
                            triangles,
                            ref writeIndex,
                            currentRing + row,
                            nextRing + row,
                            nextRing + nextRow);
                        AddTriangle(
                            triangles,
                            ref writeIndex,
                            currentRing + row,
                            nextRing + nextRow,
                            currentRing + nextRow);
                    }
                }

                int startCap = GetVisualStartCapIndex(pointCount, strand);
                int endCap = GetVisualEndCapIndex(pointCount, strand);
                int lastRing = GetVisualVertexIndex(pointCount - 1, strand, 0);
                for (int row = 0; row < VisualCrossSectionRows; row++)
                {
                    int nextRow = (row + 1) % VisualCrossSectionRows;
                    AddTriangle(
                        triangles,
                        ref writeIndex,
                        startCap,
                        nextRow + GetVisualVertexIndex(0, strand, 0),
                        row + GetVisualVertexIndex(0, strand, 0));
                    AddTriangle(
                        triangles,
                        ref writeIndex,
                        endCap,
                        lastRing + row,
                        lastRing + nextRow);
                }
            }

            return triangles;
        }

        private static int GetVisualVertexIndex(int pointIndex, int strand, int row)
        {
            return ((pointIndex * VisualStrandCount) + strand) * VisualCrossSectionRows + row;
        }

        private static int GetVisualStartCapIndex(int pointCount, int strand)
        {
            return pointCount * VisualStrandCount * VisualCrossSectionRows + strand * 2;
        }

        private static int GetVisualEndCapIndex(int pointCount, int strand)
        {
            return GetVisualStartCapIndex(pointCount, strand) + 1;
        }

        private static void AddTriangle(int[] triangles, ref int writeIndex, int first, int second, int third)
        {
            triangles[writeIndex++] = first;
            triangles[writeIndex++] = second;
            triangles[writeIndex++] = third;
        }

        private void RedrawVisual()
        {
            if (visualMesh == null || segments.Count == 0)
            {
                return;
            }

            float totalDistance = RefreshVisualControlPoints();
            if (totalDistance < Mathf.Epsilon)
            {
                return;
            }

            for (int pointIndex = 0; pointIndex < visualPointCount; pointIndex++)
            {
                float ratio = pointIndex / (float)(visualPointCount - 1);
                visualPathPoints[pointIndex] = SampleVisualPath(totalDistance * ratio);
            }

            Vector3 previousPoint = visualPathPoints[0];
            Vector3 tangent = GetVisualTangent(0);
            Vector3 normal = ChooseVisualNormal(tangent);
            float distance = 0f;

            for (int pointIndex = 0; pointIndex < visualPointCount; pointIndex++)
            {
                Vector3 point = visualPathPoints[pointIndex];
                if (pointIndex > 0)
                {
                    Vector3 nextTangent = GetVisualTangent(pointIndex);
                    Quaternion transport = Quaternion.FromToRotation(tangent, nextTangent);
                    normal = transport * normal;
                    normal = Vector3.ProjectOnPlane(normal, nextTangent);
                    if (normal.sqrMagnitude < 0.000001f)
                    {
                        normal = ChooseVisualNormal(nextTangent);
                    }
                    else
                    {
                        normal.Normalize();
                    }

                    tangent = nextTangent;
                    distance += Vector3.Distance(previousPoint, point);
                    previousPoint = point;
                }

                Quaternion twist = Quaternion.AngleAxis(
                    distance * VisualTwistDegreesPerUnit,
                    tangent);
                Vector3 twistedNormal = twist * normal;
                Vector3 binormal = Vector3.Cross(tangent, twistedNormal).normalized;
                twistedNormal = Vector3.Cross(binormal, tangent).normalized;

                float strandOrbit = visualRadius * VisualStrandOrbitRatio;
                float strandRadius = visualRadius * VisualStrandRadiusRatio;
                for (int strand = 0; strand < VisualStrandCount; strand++)
                {
                    float strandAngle = strand * Mathf.PI * 2f / VisualStrandCount;
                    Vector3 strandDirection =
                        twistedNormal * Mathf.Cos(strandAngle) + binormal * Mathf.Sin(strandAngle);
                    Vector3 strandBinormal = Vector3.Cross(tangent, strandDirection).normalized;
                    Vector3 strandCenter = point + strandDirection * strandOrbit;

                    for (int row = 0; row < VisualCrossSectionRows; row++)
                    {
                        float angle = row * Mathf.PI * 2f / VisualCrossSectionRows;
                        Vector3 sectionDirection =
                            strandDirection * Mathf.Cos(angle) + strandBinormal * Mathf.Sin(angle);
                        Vector3 offset = strandDirection * strandOrbit + sectionDirection * strandRadius;
                        int vertexIndex = GetVisualVertexIndex(pointIndex, strand, row);
                        visualVertices[vertexIndex] = transform.InverseTransformPoint(point + offset);
                    }

                    if (pointIndex == 0)
                    {
                        visualVertices[GetVisualStartCapIndex(visualPointCount, strand)] =
                            transform.InverseTransformPoint(strandCenter);
                    }
                    else if (pointIndex == visualPointCount - 1)
                    {
                        visualVertices[GetVisualEndCapIndex(visualPointCount, strand)] =
                            transform.InverseTransformPoint(strandCenter);
                    }
                }
            }

            visualMesh.vertices = visualVertices;
            visualMesh.RecalculateNormals();
            visualMesh.RecalculateBounds();
        }

        private float RefreshVisualControlPoints()
        {
            visualControlPoints[0] = start.WorldPoint;
            for (int index = 0; index < segments.Count; index++)
            {
                visualControlPoints[index + 1] = segments[index].transform.position;
            }
            visualControlPoints[visualControlPointCount - 1] = end.WorldPoint;

            visualControlDistances[0] = 0f;
            float totalDistance = 0f;
            for (int index = 1; index < visualControlPointCount; index++)
            {
                totalDistance += Vector3.Distance(
                    visualControlPoints[index - 1], visualControlPoints[index]);
                visualControlDistances[index] = totalDistance;
            }

            return totalDistance;
        }

        private Vector3 SampleVisualPath(float distance)
        {
            distance = Mathf.Clamp(distance, 0f, visualControlDistances[visualControlPointCount - 1]);
            for (int index = 1; index < visualControlPointCount; index++)
            {
                if (visualControlDistances[index] < distance && index < visualControlPointCount - 1)
                {
                    continue;
                }

                float span = visualControlDistances[index] - visualControlDistances[index - 1];
                float ratio = span < Mathf.Epsilon
                    ? 0f
                    : (distance - visualControlDistances[index - 1]) / span;
                return Vector3.Lerp(
                    visualControlPoints[index - 1],
                    visualControlPoints[index],
                    Mathf.Clamp01(ratio));
            }

            return visualControlPoints[visualControlPointCount - 1];
        }

        private void OnDestroy()
        {
            DestroyRuntimeAsset(visualMesh);
            DestroyRuntimeAsset(visualMaterial);
            DestroyRuntimeAsset(surfaceMaterial);
        }

        private Vector3 GetVisualTangent(int pointIndex)
        {
            Vector3 point = visualPathPoints[pointIndex];
            if (pointIndex == 0)
            {
                return SafeVisualDirection(visualPathPoints[1] - point);
            }

            if (pointIndex == visualPointCount - 1)
            {
                return SafeVisualDirection(point - visualPathPoints[pointIndex - 1]);
            }

            Vector3 previousStep = (point - visualPathPoints[pointIndex - 1]).normalized;
            Vector3 nextStep = (visualPathPoints[pointIndex + 1] - point).normalized;
            Vector3 tangent = previousStep + nextStep;
            if (tangent.sqrMagnitude < 0.000001f)
            {
                tangent = nextStep;
            }

            return SafeVisualDirection(tangent);
        }

        private static Vector3 SafeVisualDirection(Vector3 direction)
        {
            return direction.sqrMagnitude < 0.000001f ? Vector3.forward : direction.normalized;
        }

        private static Vector3 ChooseVisualNormal(Vector3 tangent)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f
                ? Vector3.right
                : Vector3.up;
            return Vector3.ProjectOnPlane(reference, tangent).normalized;
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

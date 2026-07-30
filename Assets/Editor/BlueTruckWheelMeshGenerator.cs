using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CargoStack.EditorTools
{
    /// <summary>
    /// 단일 BlueTruck FBX를 런타임 분석 없이 차체, 테일게이트, 실제 바퀴 네 개로 결정적으로 분리한다.
    /// 테일게이트의 계측 영역과 바퀴 축 주변의 원통형 영역으로 모든 삼각형을 정확히 한 파트에만 배정한다.
    /// </summary>
    public static class BlueTruckWheelMeshGenerator
    {
        public const string GeneratedFolder = "Assets/Art/Vehicles/BlueTruck/Generated";
        public const string BodyMeshPath = GeneratedFolder + "/BlueTruckBody.asset";
        public const string TailgateMeshPath = GeneratedFolder + "/BlueTruckTailgate.asset";
        public const float WheelRadius = 0.515f;
        public const int SourceTriangleCount = 501510;
        public static readonly Vector3 TailgatePivot = new(-3.04f, 0.20f, 0f);

        private const string SourceModelPath = "Assets/Art/Vehicles/BlueTruck/BlueTruck.fbx";
        private const float SourceScale = 6.2002f;
        // BlueTruck v2의 후면 판넬을 Truck 로컬 좌표에서 계측한 영역이다. 긴 삼각형의
        // 일부가 차체에서 뜯겨 나오지 않도록 세 꼭짓점이 전부 영역 안에 들어올 때만 문으로 분리한다.
        private const float TailgateMaximumX = -2.90f;
        private const float TailgateMinimumY = 0.14f;
        private const float TailgateMaximumY = 0.94f;
        private const float TailgateHalfWidth = 1.16f;
        // A centroid-only test previously put long body triangles into the wheel meshes.
        // Assign a triangle to a wheel only when its complete geometry fits this envelope.
        public const float WheelRetentionRadius = 0.535f;
        public const float WheelRetentionHalfWidth = 0.190f;
        private const float PivotProbeHalfWidth = 0.21f;
        private const float PivotFitMinimumRadius = 0.47f;
        private const float PivotFitMaximumRadius = 0.58f;
        private const float PivotFitTolerance = 0.0045f;
        private const float PivotRefineTolerance = 0.007f;

        // These only locate the four tyres. Generate() measures their actual circular axes.
        private static readonly Vector3[] WheelPivotSeeds =
        {
            new Vector3(2.09f, -0.235f, -1.13f),
            new Vector3(2.09f, -0.235f, 1.13f),
            new Vector3(-1.70f, -0.235f, -1.13f),
            new Vector3(-1.70f, -0.235f, 1.13f),
        };

        public static Vector3[] WheelPivots { get; private set; } =
            (Vector3[])WheelPivotSeeds.Clone();

        public static readonly string[] WheelNames =
        {
            "FrontLeft",
            "FrontRight",
            "RearLeft",
            "RearRight",
        };

        public static string WheelMeshPath(int index) =>
            $"{GeneratedFolder}/BlueTruckWheel_{WheelNames[index]}.asset";

        public static void Generate()
        {
            Directory.CreateDirectory(GeneratedFolder);
            AssetDatabase.Refresh();

            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModelPath)
                ?? throw new InvalidOperationException($"BlueTruck FBX를 찾지 못했다: {SourceModelPath}");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            instance.transform.SetPositionAndRotation(
                new Vector3(0f, -0.75f, 0f),
                Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(-90f, 0f, 0f));
            instance.transform.localScale = Vector3.one * SourceScale;

            try
            {
                MeshFilter sourceFilter = instance.GetComponentInChildren<MeshFilter>(true)
                    ?? throw new InvalidOperationException("BlueTruck FBX에 MeshFilter가 없다");
                Mesh source = sourceFilter.sharedMesh;
                if (source.subMeshCount < 1)
                {
                    throw new InvalidOperationException("BlueTruck FBX에 submesh가 없다");
                }

                var sourceData = new SourceMeshData(source, sourceFilter.transform.localToWorldMatrix);
                WheelPivots = MeasureWheelPivots(sourceData.Positions);
                var parts = new PartMeshData[6];
                parts[0] = new PartMeshData("BlueTruckBody", Vector3.zero, sourceData);
                parts[1] = new PartMeshData("BlueTruckTailgate", TailgatePivot, sourceData);
                for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++)
                {
                    parts[wheelIndex + 2] = new PartMeshData(
                        $"BlueTruckWheel_{WheelNames[wheelIndex]}",
                        WheelPivots[wheelIndex],
                        sourceData);
                }

                int assignedTriangles = 0;
                for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
                {
                    int[] indices = source.GetIndices(subMesh);
                    for (int index = 0; index < indices.Length; index += 3)
                    {
                        int a = indices[index];
                        int b = indices[index + 1];
                        int c = indices[index + 2];
                        int partIndex = FindPart(
                            sourceData.Positions[a],
                            sourceData.Positions[b],
                            sourceData.Positions[c]);
                        parts[partIndex].AddTriangle(subMesh, a, b, c);
                        assignedTriangles++;
                    }
                }

                if (assignedTriangles != SourceTriangleCount)
                {
                    throw new InvalidOperationException(
                        $"BlueTruck v2 삼각형 수가 계측값과 다르다: {assignedTriangles}/{SourceTriangleCount}");
                }

                SaveMesh(BodyMeshPath, parts[0].Build());
                SaveMesh(TailgateMeshPath, parts[1].Build());
                for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++)
                {
                    SaveMesh(WheelMeshPath(wheelIndex), parts[wheelIndex + 2].Build());
                }

                int wheelTriangles = 0;
                for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++)
                {
                    wheelTriangles += parts[wheelIndex + 2].TriangleCount;
                    Debug.Log(
                        $"[CargoStack] BlueTruck {WheelNames[wheelIndex]} wheel: " +
                        $"{parts[wheelIndex + 2].TriangleCount} triangles");
                }

                Debug.Log(
                    $"[CargoStack] BlueTruck 파트 메시 생성 완료: body={parts[0].TriangleCount}, " +
                    $"tailgate={parts[1].TriangleCount}, wheels={wheelTriangles}, total={assignedTriangles}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            AssetDatabase.SaveAssets();
        }

        private static int FindPart(Vector3 a, Vector3 b, Vector3 c)
        {
            if (IsInsideTailgateEnvelope(a)
                && IsInsideTailgateEnvelope(b)
                && IsInsideTailgateEnvelope(c))
            {
                return 1;
            }

            int wheelIndex = FindWheelZone(a, b, c);
            return wheelIndex < 0 ? 0 : wheelIndex + 2;
        }

        private static bool IsInsideTailgateEnvelope(Vector3 point)
        {
            return point.x <= TailgateMaximumX
                && point.y >= TailgateMinimumY
                && point.y <= TailgateMaximumY
                && Mathf.Abs(point.z) <= TailgateHalfWidth;
        }

        private static int FindWheelZone(Vector3 a, Vector3 b, Vector3 c)
        {
            for (int index = 0; index < WheelPivots.Length; index++)
            {
                Vector3 pivot = WheelPivots[index];
                if (IsInsideWheelEnvelope(a, pivot)
                    && IsInsideWheelEnvelope(b, pivot)
                    && IsInsideWheelEnvelope(c, pivot))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool IsInsideWheelEnvelope(Vector3 point, Vector3 pivot)
        {
            if (Mathf.Abs(point.z - pivot.z) > WheelRetentionHalfWidth)
            {
                return false;
            }

            Vector2 radial = new Vector2(point.x - pivot.x, point.y - pivot.y);
            return radial.sqrMagnitude <= WheelRetentionRadius * WheelRetentionRadius;
        }

        private static Vector3[] MeasureWheelPivots(Vector3[] positions)
        {
            var measured = new Vector3[WheelPivotSeeds.Length];
            for (int index = 0; index < WheelPivotSeeds.Length; index++)
            {
                measured[index] = MeasureWheelPivot(positions, WheelPivotSeeds[index], index);
            }

            // The left/right fits measure the same physical axle. Average them to remove
            // sub-millimetre tessellation bias without hand-tuning a rotation centre.
            float frontX = (measured[0].x + measured[1].x) * 0.5f;
            float rearX = (measured[2].x + measured[3].x) * 0.5f;
            float axleY = (measured[0].y + measured[1].y + measured[2].y + measured[3].y) * 0.25f;
            return new[]
            {
                new Vector3(frontX, axleY, WheelPivotSeeds[0].z),
                new Vector3(frontX, axleY, WheelPivotSeeds[1].z),
                new Vector3(rearX, axleY, WheelPivotSeeds[2].z),
                new Vector3(rearX, axleY, WheelPivotSeeds[3].z),
            };
        }

        private static Vector3 MeasureWheelPivot(Vector3[] positions, Vector3 seed, int wheelIndex)
        {
            var candidates = new List<Vector2>();
            float minimumRadiusSquared = PivotFitMinimumRadius * PivotFitMinimumRadius;
            float maximumRadiusSquared = PivotFitMaximumRadius * PivotFitMaximumRadius;
            for (int index = 0; index < positions.Length; index++)
            {
                Vector3 point = positions[index];
                if (Mathf.Abs(point.z - seed.z) > PivotProbeHalfWidth)
                {
                    continue;
                }

                Vector2 radial = new Vector2(point.x - seed.x, point.y - seed.y);
                float radiusSquared = radial.sqrMagnitude;
                if (radiusSquared > minimumRadiusSquared && radiusSquared < maximumRadiusSquared)
                {
                    candidates.Add(new Vector2(point.x, point.y));
                }
            }

            if (candidates.Count < 64)
            {
                throw new InvalidOperationException(
                    $"{WheelNames[wheelIndex]} 바퀴 축을 계측할 원형 타이어 정점이 부족하다: {candidates.Count}");
            }

            Vector3 bestCircle = FindDominantCircle(candidates, seed, wheelIndex);
            var inliers = new List<Vector2>();
            for (int index = 0; index < candidates.Count; index++)
            {
                if (Mathf.Abs(Vector2.Distance(
                        candidates[index], new Vector2(bestCircle.x, bestCircle.y)) - bestCircle.z)
                    <= PivotRefineTolerance)
                {
                    inliers.Add(candidates[index]);
                }
            }

            if (inliers.Count < 64)
            {
                throw new InvalidOperationException(
                    $"{WheelNames[wheelIndex]} 바퀴 축 원형 피팅이 충분히 수렴하지 않았다: {inliers.Count}");
            }

            return FitCircle(inliers);
        }

        private static Vector3 FindDominantCircle(List<Vector2> candidates, Vector3 seed, int wheelIndex)
        {
            var random = new System.Random(12017 + wheelIndex);
            Vector3 bestCircle = Vector3.zero;
            int bestInlierCount = -1;
            for (int iteration = 0; iteration < 800; iteration++)
            {
                Vector2 a = candidates[random.Next(candidates.Count)];
                Vector2 b = candidates[random.Next(candidates.Count)];
                Vector2 c = candidates[random.Next(candidates.Count)];
                if (!TryGetCircle(a, b, c, out Vector3 circle)
                    || circle.z < WheelRadius * 0.90f
                    || circle.z > WheelRadius * 1.08f
                    || Vector2.Distance(new Vector2(circle.x, circle.y), new Vector2(seed.x, seed.y)) > 0.08f)
                {
                    continue;
                }

                int inlierCount = 0;
                for (int pointIndex = 0; pointIndex < candidates.Count; pointIndex++)
                {
                    if (Mathf.Abs(Vector2.Distance(
                            candidates[pointIndex], new Vector2(circle.x, circle.y)) - circle.z)
                        <= PivotFitTolerance)
                    {
                        inlierCount++;
                    }
                }

                if (inlierCount > bestInlierCount)
                {
                    bestInlierCount = inlierCount;
                    bestCircle = circle;
                }
            }

            if (bestInlierCount < 64)
            {
                throw new InvalidOperationException(
                    $"{WheelNames[wheelIndex]} 바퀴 축 원형을 찾지 못했다: {bestInlierCount}");
            }

            return bestCircle;
        }

        private static bool TryGetCircle(Vector2 a, Vector2 b, Vector2 c, out Vector3 circle)
        {
            double determinant = 2d * (
                a.x * (b.y - c.y)
                + b.x * (c.y - a.y)
                + c.x * (a.y - b.y));
            if (Math.Abs(determinant) < 0.000001d)
            {
                circle = default;
                return false;
            }

            double aLength = a.x * a.x + a.y * a.y;
            double bLength = b.x * b.x + b.y * b.y;
            double cLength = c.x * c.x + c.y * c.y;
            float x = (float)((
                aLength * (b.y - c.y)
                + bLength * (c.y - a.y)
                + cLength * (a.y - b.y)) / determinant);
            float y = (float)((
                aLength * (c.x - b.x)
                + bLength * (a.x - c.x)
                + cLength * (b.x - a.x)) / determinant);
            circle = new Vector3(x, y, Vector2.Distance(new Vector2(x, y), a));
            return true;
        }

        private static Vector3 FitCircle(List<Vector2> points)
        {
            double sumX = 0d;
            double sumY = 0d;
            double sumXX = 0d;
            double sumYY = 0d;
            double sumXY = 0d;
            double sumXXX = 0d;
            double sumYYY = 0d;
            double sumXYY = 0d;
            double sumXXY = 0d;
            foreach (Vector2 point in points)
            {
                double x = point.x;
                double y = point.y;
                double xx = x * x;
                double yy = y * y;
                sumX += x;
                sumY += y;
                sumXX += xx;
                sumYY += yy;
                sumXY += x * y;
                sumXXX += xx * x;
                sumYYY += yy * y;
                sumXYY += x * yy;
                sumXXY += xx * y;
            }

            // x² + y² + D*x + E*y + F = 0의 최소제곱 해.
            double count = points.Count;
            double determinant =
                sumXX * (sumYY * count - sumY * sumY)
                - sumXY * (sumXY * count - sumY * sumX)
                + sumX * (sumXY * sumY - sumYY * sumX);
            if (Math.Abs(determinant) < 0.000000001d)
            {
                throw new InvalidOperationException("바퀴 축 원형 피팅 행렬이 특이하다");
            }

            double rightX = -(sumXXX + sumXYY);
            double rightY = -(sumXXY + sumYYY);
            double rightConstant = -(sumXX + sumYY);
            double d = (
                rightX * (sumYY * count - sumY * sumY)
                - sumXY * (rightY * count - sumY * rightConstant)
                + sumX * (rightY * sumY - sumYY * rightConstant)) / determinant;
            double e = (
                sumXX * (rightY * count - sumY * rightConstant)
                - rightX * (sumXY * count - sumY * sumX)
                + sumX * (sumXY * rightConstant - rightY * sumX)) / determinant;
            double f = (
                sumXX * (sumYY * rightConstant - rightY * sumY)
                - sumXY * (sumXY * rightConstant - rightY * sumX)
                + rightX * (sumXY * sumY - sumYY * sumX)) / determinant;
            float xCenter = (float)(-d * 0.5d);
            float yCenter = (float)(-e * 0.5d);
            float radius = (float)Math.Sqrt(xCenter * xCenter + yCenter * yCenter - f);
            return new Vector3(xCenter, yCenter, radius);
        }

        private static void SaveMesh(string path, Mesh generated)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return;
            }

            EditorUtility.CopySerialized(generated, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(generated);
        }

        private sealed class SourceMeshData
        {
            public Vector3[] Positions { get; }
            public Vector3[] Normals { get; }
            public Vector4[] Tangents { get; }
            public Color32[] Colors { get; }
            public List<Vector4>[] UvChannels { get; } = new List<Vector4>[8];
            public int SubMeshCount { get; }

            public SourceMeshData(Mesh source, Matrix4x4 sourceToTruck)
            {
                Vector3[] sourcePositions = source.vertices;
                Vector3[] sourceNormals = source.normals;
                Vector4[] sourceTangents = source.tangents;
                Positions = new Vector3[sourcePositions.Length];
                Normals = sourceNormals.Length == sourcePositions.Length
                    ? new Vector3[sourcePositions.Length]
                    : Array.Empty<Vector3>();
                Tangents = sourceTangents.Length == sourcePositions.Length
                    ? new Vector4[sourcePositions.Length]
                    : Array.Empty<Vector4>();
                Colors = source.colors32;
                SubMeshCount = source.subMeshCount;

                for (int index = 0; index < sourcePositions.Length; index++)
                {
                    Positions[index] = sourceToTruck.MultiplyPoint3x4(sourcePositions[index]);
                    if (Normals.Length > 0)
                    {
                        Normals[index] = sourceToTruck.MultiplyVector(sourceNormals[index]).normalized;
                    }

                    if (Tangents.Length > 0)
                    {
                        Vector3 tangent = sourceToTruck.MultiplyVector(
                            new Vector3(
                                sourceTangents[index].x,
                                sourceTangents[index].y,
                                sourceTangents[index].z)).normalized;
                        Tangents[index] = new Vector4(
                            tangent.x,
                            tangent.y,
                            tangent.z,
                            sourceTangents[index].w);
                    }
                }

                for (int channel = 0; channel < UvChannels.Length; channel++)
                {
                    var values = new List<Vector4>();
                    source.GetUVs(channel, values);
                    UvChannels[channel] = values.Count == sourcePositions.Length ? values : null;
                }
            }
        }

        private sealed class PartMeshData
        {
            private readonly string name;
            private readonly Vector3 pivot;
            private readonly SourceMeshData source;
            private readonly Dictionary<int, int> vertexMap = new();
            private readonly List<Vector3> positions = new();
            private readonly List<Vector3> normals = new();
            private readonly List<Vector4> tangents = new();
            private readonly List<Color32> colors = new();
            private readonly List<Vector4>[] uvChannels = new List<Vector4>[8];
            private readonly List<int>[] subMeshIndices;

            public int TriangleCount { get; private set; }

            public PartMeshData(string name, Vector3 pivot, SourceMeshData source)
            {
                this.name = name;
                this.pivot = pivot;
                this.source = source;
                subMeshIndices = new List<int>[source.SubMeshCount];
                for (int subMesh = 0; subMesh < subMeshIndices.Length; subMesh++)
                {
                    subMeshIndices[subMesh] = new List<int>();
                }

                for (int channel = 0; channel < uvChannels.Length; channel++)
                {
                    if (source.UvChannels[channel] != null)
                    {
                        uvChannels[channel] = new List<Vector4>();
                    }
                }
            }

            public void AddTriangle(int subMesh, int a, int b, int c)
            {
                subMeshIndices[subMesh].Add(AddVertex(a));
                subMeshIndices[subMesh].Add(AddVertex(b));
                subMeshIndices[subMesh].Add(AddVertex(c));
                TriangleCount++;
            }

            public Mesh Build()
            {
                var mesh = new Mesh
                {
                    name = name,
                    indexFormat = positions.Count > ushort.MaxValue
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16,
                };
                mesh.SetVertices(positions);
                if (normals.Count == positions.Count)
                {
                    mesh.SetNormals(normals);
                }

                if (tangents.Count == positions.Count)
                {
                    mesh.SetTangents(tangents);
                }

                if (colors.Count == positions.Count)
                {
                    mesh.SetColors(colors);
                }

                for (int channel = 0; channel < uvChannels.Length; channel++)
                {
                    if (uvChannels[channel] != null)
                    {
                        mesh.SetUVs(channel, uvChannels[channel]);
                    }
                }

                mesh.subMeshCount = subMeshIndices.Length;
                for (int subMesh = 0; subMesh < subMeshIndices.Length; subMesh++)
                {
                    mesh.SetTriangles(subMeshIndices[subMesh], subMesh, false);
                }

                mesh.RecalculateBounds();
                // 파생 메시가 원본 FBX보다 훨씬 큰 YAML 자산이 되지 않게 버텍스 데이터를 압축한다.
                // High는 가까운 측면 샷에서 타이어/휠 표면 오차가 보여 Medium을 사용한다.
                MeshUtility.Optimize(mesh);
                MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
                return mesh;
            }

            private int AddVertex(int sourceIndex)
            {
                if (vertexMap.TryGetValue(sourceIndex, out int existing))
                {
                    return existing;
                }

                int index = positions.Count;
                vertexMap.Add(sourceIndex, index);
                positions.Add(source.Positions[sourceIndex] - pivot);
                if (source.Normals.Length > 0)
                {
                    normals.Add(source.Normals[sourceIndex]);
                }

                if (source.Tangents.Length > 0)
                {
                    tangents.Add(source.Tangents[sourceIndex]);
                }

                if (source.Colors.Length == source.Positions.Length)
                {
                    colors.Add(source.Colors[sourceIndex]);
                }

                for (int channel = 0; channel < uvChannels.Length; channel++)
                {
                    if (uvChannels[channel] != null)
                    {
                        uvChannels[channel].Add(source.UvChannels[channel][sourceIndex]);
                    }
                }

                return index;
            }
        }
    }
}

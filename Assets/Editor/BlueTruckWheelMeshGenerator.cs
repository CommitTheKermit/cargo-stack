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
    /// 단일 BlueTruck FBX를 런타임 분석 없이 차체와 실제 바퀴 네 개로 결정적으로 분리한다.
    /// 바퀴 축 주변의 원통형 영역으로 모든 삼각형을 정확히 한 파트에만 배정한다.
    /// </summary>
    public static class BlueTruckWheelMeshGenerator
    {
        public const string GeneratedFolder = "Assets/Art/Vehicles/BlueTruck/Generated";
        public const string BodyMeshPath = GeneratedFolder + "/BlueTruckBody.asset";
        public const float WheelRadius = 0.515f;
        public const int SourceTriangleCount = 501510;

        private const string SourceModelPath = "Assets/Art/Vehicles/BlueTruck/BlueTruck.fbx";
        private const float SourceScale = 6.2002f;
        private const float WheelSelectionRadius = 0.56f;
        private const float WheelSelectionHalfWidth = 0.24f;

        public static readonly Vector3[] WheelPivots =
        {
            new Vector3(2.09f, -0.235f, -1.13f),
            new Vector3(2.09f, -0.235f, 1.13f),
            new Vector3(-1.70f, -0.235f, -1.13f),
            new Vector3(-1.70f, -0.235f, 1.13f),
        };

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
                var parts = new PartMeshData[5];
                parts[0] = new PartMeshData("BlueTruckBody", Vector3.zero, sourceData);
                for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++)
                {
                    parts[wheelIndex + 1] = new PartMeshData(
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
                        Vector3 centroid =
                            (sourceData.Positions[a] + sourceData.Positions[b] + sourceData.Positions[c]) / 3f;
                        int wheelIndex = FindWheelZone(centroid);
                        parts[wheelIndex + 1].AddTriangle(subMesh, a, b, c);
                        assignedTriangles++;
                    }
                }

                if (assignedTriangles != SourceTriangleCount)
                {
                    throw new InvalidOperationException(
                        $"BlueTruck v2 삼각형 수가 계측값과 다르다: {assignedTriangles}/{SourceTriangleCount}");
                }

                SaveMesh(BodyMeshPath, parts[0].Build());
                for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++)
                {
                    SaveMesh(WheelMeshPath(wheelIndex), parts[wheelIndex + 1].Build());
                }

                int wheelTriangles = 0;
                for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++)
                {
                    wheelTriangles += parts[wheelIndex + 1].TriangleCount;
                    Debug.Log(
                        $"[CargoStack] BlueTruck {WheelNames[wheelIndex]} wheel: " +
                        $"{parts[wheelIndex + 1].TriangleCount} triangles");
                }

                Debug.Log(
                    $"[CargoStack] BlueTruck wheel mesh 생성 완료: body={parts[0].TriangleCount}, " +
                    $"wheels={wheelTriangles}, total={assignedTriangles}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            AssetDatabase.SaveAssets();
        }

        private static int FindWheelZone(Vector3 point)
        {
            for (int index = 0; index < WheelPivots.Length; index++)
            {
                Vector3 pivot = WheelPivots[index];
                if (Mathf.Abs(point.z - pivot.z) > WheelSelectionHalfWidth)
                {
                    continue;
                }

                Vector2 radial = new Vector2(point.x - pivot.x, point.y - pivot.y);
                if (radial.sqrMagnitude <= WheelSelectionRadius * WheelSelectionRadius)
                {
                    return index;
                }
            }

            return -1;
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

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CargoStack.EditorTools
{
    /// <summary>
    /// 검증용 프로토타입 씬을 코드로 만든다.
    /// 씬 파일은 git 자동 병합이 안 되므로(기획서 5장 협업 규칙), 손으로 고치는 대신
    /// 이 스크립트를 고쳐 다시 생성하는 것을 기본 절차로 삼는다.
    ///
    /// 트럭은 +X 방향으로 달린다. 짐칸 치수와 플레이어 규격은
    /// nan2026-cargo 스파이크에서 손맛이 검증된 값을 축 방향만 바꿔 가져왔다.
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePath = SceneFolder + "/Prototype.unity";
        private const string MaterialFolder = "Assets/Materials";
        private const string MeshFolder = "Assets/Meshes";

        /// <summary>도로 표면에서 차체 원점까지의 높이. GroundSupport 콜라이더 바닥과 맞춘다.</summary>
        private const float RideHeight = 0.75f;

        /// <summary>출발선의 경로상 거리. 이 앞 구간은 직선이라 적재장으로 쓴다.</summary>
        private const float TruckStartDistance = 8f;

        /// <summary>최고 속도(m/s). 약 36km/h. 급제동 골짜기의 깊이도 이 값에 비례해 세진다.</summary>
        private const float MaxSpeed = 10f;

        private const float RoadWidth = 13f;
        private const float RoadThickness = 1.2f;

        /// <summary>도로 조각을 이웃과 겹치게 늘리는 배율. 커브 바깥쪽이 벌어지는 것을 막는다.</summary>
        private const float RoadBlockOverlap = 1.5f;

        public const int CargoCount = 6;

        /// <summary>
        /// 도로 중심선 제어점. 위에서 보면 일직선이고, 옆에서 보면 오르막과 내리막이 굽이친다.
        /// 곡선이 이 점들을 모두 지나가므로 마루와 골짜기가 각지지 않고 둥글게 이어진다.
        ///
        /// 두 구간은 일부러 평평하게 남겨 뒀다. 앞머리 직선은 짐을 쌓는 적재장이고,
        /// 능선은 급제동 구간이다. 급제동을 굴곡 위에 걸면 마루가 짐을 띄우는 효과와
        /// 감속이 겹쳐 버려서, 플레이어가 무엇 때문에 실패했는지 배울 수 없다.
        /// </summary>
        private static readonly Vector3[] RouteControlPoints =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(16f, 0f, 0f),      // 적재·출발 평지
            new Vector3(28f, 0f, 0f),
            new Vector3(40f, 3.2f, 0f),    // 첫 오르막
            new Vector3(52f, 5f, 0f),      // 마루로 올라붙는다
            new Vector3(64f, 4.6f, 0f),    // 능선 평지 = 급제동 구간
            new Vector3(76f, 1f, 0f),      // 긴 내리막
            new Vector3(86f, -1.2f, 0f),   // 골짜기 바닥
            new Vector3(94f, 1.6f, 0f),    // 짧고 가파른 오르막
            new Vector3(104f, 2.6f, 0f),   // 두 번째 마루
            new Vector3(112f, 0.2f, 0f),   // 내려온다
            new Vector3(126f, 0f, 0f),     // 마무리 평지
        };

        [MenuItem("CargoStack/프로토타입 씬 다시 만들기")]
        public static void Build()
        {
            Directory.CreateDirectory(SceneFolder);
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();

            int groundLayer = EnsureLayer("Ground");

            Material groundMaterial = EnsureColorMaterial("Ground", new Color(0.45f, 0.62f, 0.36f));
            Material truckMaterial = EnsureColorMaterial("Truck", new Color(0.26f, 0.48f, 0.62f));
            Material bedMaterial = EnsureColorMaterial("Bed", new Color(0.47f, 0.5f, 0.52f));
            Material wheelMaterial = EnsureColorMaterial("Wheel", new Color(0.09f, 0.09f, 0.1f));
            Material cargoMaterialA = EnsureColorMaterial("CargoA", new Color(0.78f, 0.5f, 0.21f));
            Material cargoMaterialB = EnsureColorMaterial("CargoB", new Color(0.63f, 0.37f, 0.24f));
            Material playerMaterial = EnsureColorMaterial("Player", new Color(0.32f, 0.7f, 0.42f));

            PhysicsMaterial bedPhysics = EnsurePhysicsMaterial("BedSurface", 0.55f, 0.65f);
            PhysicsMaterial cargoPhysics = EnsurePhysicsMaterial("CargoSurface", 0.45f, 0.55f);

            ApplyPhysicsProjectSettings();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RoutePath route = BuildRoute(groundLayer, groundMaterial);
            float goalDistance = route.TotalLength - 6f;

            CreateLighting();

            GameObject truck = BuildTruck(truckMaterial, bedMaterial, wheelMaterial, bedPhysics, out Transform bedAnchor);
            var mover = truck.GetComponent<TruckMover>();

            using (var wiring = new Wiring(mover))
            {
                wiring.Ref("path", route)
                    .Num("startDistance", TruckStartDistance)
                    .Num("goalDistance", goalDistance)
                    .Num("maxSpeed", MaxSpeed)
                    .Curve("speedOverProgress", BuildSpeedProfile(route, TruckStartDistance, goalDistance))
                    .Num("minSpeedFactor", 0.06f)
                    .Num("rideHeight", RideHeight);
            }

            // 트럭을 경로 위 출발선에 정확히 올린다. 배선이 끝난 뒤여야 경로를 읽을 수 있다.
            // 아래 짐·플레이어·카메라가 모두 이 자세를 기준으로 자리를 잡으므로 순서를 지켜야 한다.
            mover.SnapToStart();

            List<Cargo> cargo = BuildCargo(truck.transform, cargoMaterialA, cargoMaterialB, cargoPhysics);

            Camera firstPersonCamera = BuildFirstPersonCamera(out Transform carryAnchor);
            GameObject player = BuildPlayer(truck.transform, firstPersonCamera, carryAnchor, playerMaterial);
            Camera dioramaCamera = BuildDioramaCamera(truck.transform);

            var systems = new GameObject("Systems");
            var flow = systems.AddComponent<GameFlow>();
            var tracker = systems.AddComponent<CargoTracker>();
            var director = systems.AddComponent<CameraDirector>();
            var hud = systems.AddComponent<PrototypeHud>();

            using (var wiring = new Wiring(director))
            {
                wiring.Ref("firstPersonCamera", firstPersonCamera)
                    .Ref("firstPersonLook", firstPersonCamera.GetComponent<FirstPersonCamera>())
                    .Ref("dioramaCamera", dioramaCamera);
            }

            using (var wiring = new Wiring(tracker))
            {
                wiring.Ref("bedAnchor", bedAnchor).Refs("tracked", cargo.ConvertAll(item => (Object)item));
            }

            using (var wiring = new Wiring(flow))
            {
                wiring.Ref("truck", mover)
                    .Ref("tracker", tracker)
                    .Ref("cameraDirector", director)
                    .Ref("player", player);
            }

            using (var wiring = new Wiring(hud))
            {
                wiring.Ref("flow", flow)
                    .Ref("tracker", tracker)
                    .Ref("bedMaterial", bedPhysics)
                    .Ref("cargoMaterial", cargoPhysics);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"[CargoStack] 프로토타입 씬 생성 완료: {ScenePath} " +
                $"(경로 {route.TotalLength:0.0}m, 도착 {goalDistance:0.0}m, 도로 조각 {route.SampleCount - 1}개, 화물 {cargo.Count}개)");
        }

        /// <summary>
        /// 주행 속도 프로필. 골짜기 구간이 급제동이고, 짐을 앞으로 쏟아지게 만드는 순간이다.
        ///
        /// 감속도가 마찰 한계 μg 를 넘어야 짐이 미끄러진다. 마찰 0.5 기준 4.9m/s² 가 문턱이라
        /// 완만한 감속으로는 아무 일도 일어나지 않는다. 그래서 골짜기를 좁고 깊게 판다.
        /// 반대로 출발 가속과 도착 감속은 일부러 문턱 아래(3m/s² 안팎)로 눕혀 놓았다.
        /// 출발하자마자, 혹은 결승선에서 짐이 쏟아지면 배치 실력과 결과가 이어지지 않기 때문이다.
        ///
        /// 구간을 진행도(0~1)가 아니라 미터로 잡고 경로에서 환산한다.
        /// 경로 모양을 바꿔 길이가 달라져도 급제동이 능선 위에 그대로 남아 있어야 하기 때문이다.
        /// </summary>
        private static AnimationCurve BuildSpeedProfile(RoutePath route, float startDistance, float goalDistance)
        {
            // 능선(제어점 4~5) 한복판. 여기는 평평해서 감속 효과만 따로 시험할 수 있다.
            float brakeBottom = Mathf.Lerp(route.DistanceAtControlPoint(4), route.DistanceAtControlPoint(5), 0.6f);

            float ToProgress(float distance) => Mathf.InverseLerp(startDistance, goalDistance, distance);

            var curve = new AnimationCurve(
                new Keyframe(0f, 0.4f),
                new Keyframe(ToProgress(startDistance + 13f), 1f),
                new Keyframe(ToProgress(brakeBottom - 4.2f), 1f),
                new Keyframe(ToProgress(brakeBottom), 0.08f),
                new Keyframe(ToProgress(brakeBottom + 15f), 1f),
                new Keyframe(ToProgress(goalDistance - 12f), 1f),
                new Keyframe(1f, 0.3f));

            for (int i = 0; i < curve.length; i++)
            {
                curve.SmoothTangents(i, 0f);
            }

            return curve;
        }

        /// <summary>
        /// 도로를 깐다. 보이는 면과 부딪히는 면을 분리한다.
        ///
        /// 보이는 면은 중심선을 따라 뜬 리본 메시 하나다. 처음에는 겹쳐 깐 상자를 그대로 보여
        /// 줬는데, 겹친 윗면이 같은 평면이라 커브마다 z-파이팅 얼룩이 생겼다.
        /// 부딪히는 면은 여전히 상자다. 커브 바깥쪽 호가 안쪽보다 길어서 상자 길이를 딱 맞추면
        /// 바깥 차선에 구멍이 뚫리므로, 이웃과 겹치게 늘려 깐다. 겹쳐도 이제는 안 보인다.
        /// </summary>
        private static RoutePath BuildRoute(int layer, Material material)
        {
            var holder = new GameObject("Route");
            RoutePath route = holder.AddComponent<RoutePath>();
            route.SetControlPoints(RouteControlPoints);

            var surface = new GameObject("RoadSurface");
            surface.transform.SetParent(holder.transform, false);
            surface.AddComponent<MeshFilter>().sharedMesh = EnsureRoadMesh(route);
            surface.AddComponent<MeshRenderer>().sharedMaterial = material;

            var blocks = new GameObject("RoadColliders").transform;
            blocks.SetParent(holder.transform, false);

            for (int i = 0; i < route.SampleCount - 1; i++)
            {
                Vector3 from = route.SampleAt(i);
                Vector3 to = route.SampleAt(i + 1);
                Vector3 step = to - from;
                float length = step.magnitude;
                if (length < 1e-4f)
                {
                    continue;
                }

                // 조각의 로컬 +X 가 진행 방향이 되도록 LookRotation 을 90도 돌린다.
                Quaternion rotation = Quaternion.LookRotation(step / length, Vector3.up)
                    * Quaternion.Euler(0f, -90f, 0f);

                var block = new GameObject($"Road_{i:000}") { layer = layer };
                block.transform.SetParent(blocks, false);
                block.transform.SetPositionAndRotation(
                    (from + to) * 0.5f - rotation * Vector3.up * (RoadThickness * 0.5f),
                    rotation);

                BoxCollider box = block.AddComponent<BoxCollider>();
                box.size = new Vector3(length * RoadBlockOverlap, RoadThickness, RoadWidth);
            }

            return route;
        }

        /// <summary>
        /// 도로 리본 메시. 중심선 샘플마다 좌우 모서리와 그 아래 치맛단을 만들어 이어 붙인다.
        /// 도로에 뱅킹을 주지 않으므로 좌우 폭은 항상 수평이다.
        /// 메시는 씬에 끼워 넣을 수 없어 별도 에셋으로 저장한다.
        /// </summary>
        private static Mesh EnsureRoadMesh(RoutePath route)
        {
            int count = route.SampleCount;
            var vertices = new Vector3[count * 4];

            for (int i = 0; i < count; i++)
            {
                Vector3 point = route.SampleAt(i);
                Vector3 heading = route.SampleAt(Mathf.Min(i + 1, count - 1))
                    - route.SampleAt(Mathf.Max(i - 1, 0));
                heading.y = 0f;

                Vector3 side = Vector3.Cross(Vector3.up, heading.normalized) * (RoadWidth * 0.5f);
                Vector3 skirt = Vector3.down * RoadThickness;

                vertices[i * 4 + 0] = point - side;          // 왼쪽 모서리
                vertices[i * 4 + 1] = point + side;          // 오른쪽 모서리
                vertices[i * 4 + 2] = point - side + skirt;
                vertices[i * 4 + 3] = point + side + skirt;
            }

            var triangles = new List<int>((count - 1) * 18);
            for (int i = 0; i < count - 1; i++)
            {
                int a = i * 4;
                int b = (i + 1) * 4;

                // 윗면
                triangles.Add(a + 0); triangles.Add(b + 0); triangles.Add(a + 1);
                triangles.Add(b + 0); triangles.Add(b + 1); triangles.Add(a + 1);

                // 왼쪽 치맛단
                triangles.Add(a + 0); triangles.Add(a + 2); triangles.Add(b + 0);
                triangles.Add(a + 2); triangles.Add(b + 2); triangles.Add(b + 0);

                // 오른쪽 치맛단
                triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(a + 3);
                triangles.Add(b + 1); triangles.Add(b + 3); triangles.Add(a + 3);
            }

            var mesh = new Mesh { name = "RoadSurface" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Directory.CreateDirectory(MeshFolder);
            AssetDatabase.Refresh();

            string path = $"{MeshFolder}/RoadSurface.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            // 이미 있는 에셋을 덮어써야 씬이 참조하던 메시가 그대로 갱신된다.
            existing.Clear();
            existing.SetVertices(vertices);
            existing.SetTriangles(triangles, 0);
            existing.RecalculateNormals();
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static GameObject BuildTruck(
            Material truckMaterial, Material bedMaterial, Material wheelMaterial, PhysicsMaterial bedPhysics,
            out Transform bedAnchor)
        {
            // 자리는 배선이 끝난 뒤 TruckMover.SnapToStart 가 경로에서 잡아 준다.
            var truck = new GameObject("Truck");

            Rigidbody body = truck.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            truck.AddComponent<TruckMover>();

            // 바닥 지지대. 바닥면이 정확히 -RideHeight 라서 지면 추종 높이의 기준이 된다.
            AddPart(truck.transform, "GroundSupport", new Vector3(0f, -0.6f, 0f), new Vector3(4.6f, 0.3f, 1.9f), truckMaterial, bedPhysics, false);
            AddPart(truck.transform, "Chassis", new Vector3(0f, -0.05f, 0f), new Vector3(5.8f, 0.55f, 2.4f), truckMaterial, bedPhysics, true);
            AddPart(truck.transform, "Cab", new Vector3(1.9f, 0.7f, 0f), new Vector3(1.6f, 1.55f, 2.25f), truckMaterial, bedPhysics, true);

            // 짐칸 바닥은 지면에서 0.975m, 벽 상단은 1.425m 다. 플레이어 눈높이가 1.6m 이므로
            // 옆에 서서 짐칸 안을 내려다볼 수 있다. 이 높이 관계가 깨지면 1인칭 적재가 불가능해진다.
            AddPart(truck.transform, "BedFloor", new Vector3(-0.95f, 0.15f, 0f), new Vector3(3.45f, 0.15f, 2.25f), bedMaterial, bedPhysics, true);
            AddPart(truck.transform, "BedWall_Left", new Vector3(-0.95f, 0.45f, -1.08f), new Vector3(3.45f, 0.45f, 0.12f), bedMaterial, bedPhysics, true);
            AddPart(truck.transform, "BedWall_Right", new Vector3(-0.95f, 0.45f, 1.08f), new Vector3(3.45f, 0.45f, 0.12f), bedMaterial, bedPhysics, true);
            AddPart(truck.transform, "BedWall_Rear", new Vector3(-2.62f, 0.45f, 0f), new Vector3(0.12f, 0.45f, 2.25f), bedMaterial, bedPhysics, true);
            AddPart(truck.transform, "BedWall_Front", new Vector3(0.72f, 0.45f, 0f), new Vector3(0.12f, 0.45f, 2.25f), bedMaterial, bedPhysics, true);

            AddWheel(truck.transform, new Vector3(1.75f, -0.18f, -1.22f), wheelMaterial);
            AddWheel(truck.transform, new Vector3(1.75f, -0.18f, 1.22f), wheelMaterial);
            AddWheel(truck.transform, new Vector3(-1.75f, -0.18f, -1.22f), wheelMaterial);
            AddWheel(truck.transform, new Vector3(-1.75f, -0.18f, 1.22f), wheelMaterial);

            bedAnchor = CreatePoint("BedAnchor", truck.transform, new Vector3(-0.95f, 0.225f, 0f));
            return truck;
        }

        private static void AddPart(
            Transform parent, string name, Vector3 localPosition, Vector3 localScale,
            Material material, PhysicsMaterial physics, bool visible)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = localScale;
            part.GetComponent<BoxCollider>().sharedMaterial = physics;

            Renderer renderer = part.GetComponent<Renderer>();
            if (visible)
            {
                renderer.sharedMaterial = material;
            }
            else
            {
                Object.DestroyImmediate(renderer);
            }
        }

        /// <summary>바퀴는 보기용이다. 지면 추종은 TruckMover 의 레이캐스트가 담당한다.</summary>
        private static void AddWheel(Transform parent, Vector3 localPosition, Material material)
        {
            GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = "Wheel";
            wheel.transform.SetParent(parent, false);
            wheel.transform.localPosition = localPosition;

            // 실린더 축은 기본이 Y 다. 트럭이 +X 로 달리므로 차축은 Z 를 향해야 한다.
            wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            wheel.transform.localScale = new Vector3(0.55f, 0.28f, 0.55f);
            wheel.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(wheel.GetComponent<Collider>());
        }

        /// <summary>
        /// 짐은 트럭 옆 바닥에 널어 둔다. 플레이어가 걸어가 하나씩 실어야 한다.
        /// 자리는 트럭 기준 상대 좌표로 잡는다. 출발선이 경로를 따라 움직여도 같이 따라오게 하기 위함이다.
        /// </summary>
        private static List<Cargo> BuildCargo(
            Transform truck, Material materialA, Material materialB, PhysicsMaterial physics)
        {
            var cargoRoot = new GameObject("Cargo").transform;
            // 크기를 조금씩 다르게 해 쌓는 재미를 만들되, 가로·세로는 1.0 을 넘기지 않는다.
            // 짐칸 내부가 3.22 x 2.04 라서 그래야 3x2 한 층이 딱 들어간다.
            var sizes = new[]
            {
                new Vector3(0.9f, 0.9f, 0.9f),
                new Vector3(1f, 0.7f, 0.85f),
                new Vector3(0.8f, 1.1f, 0.8f),
                new Vector3(0.95f, 0.6f, 0.95f),
                new Vector3(0.75f, 0.75f, 0.75f),
                new Vector3(0.9f, 0.9f, 0.9f),
            };

            var cargo = new List<Cargo>(CargoCount);

            for (int index = 0; index < CargoCount; index++)
            {
                Vector3 size = sizes[index % sizes.Length];

                // 트럭 옆 인도. 폭 13m 도로를 벗어나지 않게 z 를 -4.7m 안쪽으로 묶는다.
                var localSpot = new Vector3(
                    -2.6f + index % 3 * 1.5f,
                    size.y * 0.5f + 0.02f - RideHeight,
                    -3f - index / 3 * 1.2f);

                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = $"Cargo_{index + 1:00}";
                box.transform.SetParent(cargoRoot, false);
                box.transform.position = truck.TransformPoint(localSpot);
                box.transform.localScale = size;
                box.GetComponent<Renderer>().sharedMaterial = index % 2 == 0 ? materialA : materialB;
                box.GetComponent<BoxCollider>().sharedMaterial = physics;

                Rigidbody body = box.AddComponent<Rigidbody>();
                body.mass = 18f + index * 2f;
                body.linearDamping = 0.05f;
                body.angularDamping = 0.15f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                cargo.Add(box.AddComponent<Cargo>());
            }

            return cargo;
        }

        private static Camera BuildFirstPersonCamera(out Transform carryAnchor)
        {
            var holder = new GameObject("First Person Camera") { tag = "MainCamera" };
            Camera camera = holder.AddComponent<Camera>();
            camera.fieldOfView = 58f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 250f;
            camera.backgroundColor = new Color(0.63f, 0.76f, 0.85f);
            camera.clearFlags = CameraClearFlags.SolidColor;

            holder.AddComponent<AudioListener>();
            holder.AddComponent<FirstPersonCamera>();

            // 든 화물이 시야 한가운데를 가리지 않도록 살짝 오른쪽 아래에 붙인다.
            carryAnchor = CreatePoint("CarryAnchor", holder.transform, new Vector3(0.65f, -0.9f, 2f));
            return camera;
        }

        private static GameObject BuildPlayer(
            Transform truck, Camera firstPersonCamera, Transform carryAnchor, Material material)
        {
            // 짐 뒤에 서서 +Z(트럭 쪽)를 바라보게 둔다. 첫 화면에 짐과 트럭이 함께 들어온다.
            var player = new GameObject("Player");
            player.transform.position = truck.TransformPoint(new Vector3(-2f, 0.05f - RideHeight, -5.4f));

            Rigidbody body = player.AddComponent<Rigidbody>();
            body.mass = 80f;
            body.linearDamping = 0f;
            body.angularDamping = 8f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 회전을 세 축 모두 잠근다. Y 를 풀어 두면 상자나 트럭에 부딪힐 때 생긴 토크가
            // 몸통을 돌려 버리고, 1인칭 카메라가 이 몸통에 매달려 있어 시점이 통째로 홱 돌아간다.
            // 시점 yaw 는 FirstPersonCamera 의 마우스 입력만으로 정해져야 한다.
            body.constraints = RigidbodyConstraints.FreezeRotation;

            CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
            capsule.radius = 0.4f;
            capsule.height = 1.8f;
            capsule.center = new Vector3(0f, 0.9f, 0f);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Body";
            visual.transform.SetParent(player.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.75f, 0.9f, 0.75f);
            visual.GetComponent<Renderer>().sharedMaterial = material;

            // 1인칭 시야를 자기 몸이 가리지 않게 그림자만 남긴다.
            visual.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            Transform eye = CreatePoint("PlayerView", player.transform, new Vector3(0f, 1.6f, 0f));

            PlayerController controller = player.AddComponent<PlayerController>();
            controller.Configure(firstPersonCamera);

            PlayerCargoInteractor interactor = player.AddComponent<PlayerCargoInteractor>();
            interactor.Configure(carryAnchor, firstPersonCamera);

            firstPersonCamera.GetComponent<FirstPersonCamera>().Configure(eye, true);
            return player;
        }

        private static Camera BuildDioramaCamera(Transform truck)
        {
            var holder = new GameObject("Diorama Camera");
            Camera camera = holder.AddComponent<Camera>();

            // 좁은 화각 + 먼 거리 조합이 폴리브릿지 같은 미니어처 느낌을 만든다.
            camera.fieldOfView = 35f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250f;
            camera.backgroundColor = new Color(0.63f, 0.76f, 0.85f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.enabled = false;

            DioramaCamera rig = holder.AddComponent<DioramaCamera>();
            using (var wiring = new Wiring(rig))
            {
                wiring.Ref("target", truck);
            }

            // 에디터에서도 제자리를 잡아 둬야 씬 뷰와 프리뷰 캡처에서 구도를 확인할 수 있다.
            rig.ResetFraming();
            return camera;
        }

        private static void CreateLighting()
        {
            var holder = new GameObject("Sun");
            Light light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            holder.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.45f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.26f, 0.22f);
        }

        private static Transform CreatePoint(string name, Transform parent, Vector3 localPosition)
        {
            var point = new GameObject(name);
            point.transform.SetParent(parent, false);
            point.transform.localPosition = localPosition;
            point.transform.localRotation = Quaternion.identity;
            return point.transform;
        }

        /// <summary>스택 안정성을 위한 물리 설정(기획서 4.1).</summary>
        private static void ApplyPhysicsProjectSettings()
        {
            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 2;
        }

        private static int EnsureLayer(string layerName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                {
                    return i;
                }
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue))
                {
                    continue;
                }

                slot.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                return i;
            }

            throw new InvalidOperationException($"빈 레이어 슬롯이 없어 '{layerName}' 레이어를 만들지 못했다.");
        }

        private static Material EnsureColorMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.color = color;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var material = new Material(Shader.Find("Standard")) { color = color };
            material.SetFloat("_Glossiness", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static PhysicsMaterial EnsurePhysicsMaterial(string name, float dynamicFriction, float staticFriction)
        {
            string path = $"{MaterialFolder}/{name}.physicsMaterial";
            var existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            PhysicsMaterial material = existing != null ? existing : new PhysicsMaterial(name);

            material.dynamicFriction = dynamicFriction;
            material.staticFriction = staticFriction;
            material.bounciness = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Average;

            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>private [SerializeField] 필드를 코드에서 연결하기 위한 얇은 래퍼.</summary>
        private sealed class Wiring : IDisposable
        {
            private readonly SerializedObject serialized;

            public Wiring(Object target)
            {
                serialized = new SerializedObject(target);
            }

            public Wiring Ref(string field, Object value)
            {
                Find(field).objectReferenceValue = value;
                return this;
            }

            public Wiring Num(string field, float value)
            {
                Find(field).floatValue = value;
                return this;
            }

            public Wiring Mask(string field, int value)
            {
                Find(field).intValue = value;
                return this;
            }

            public Wiring Curve(string field, AnimationCurve value)
            {
                Find(field).animationCurveValue = value;
                return this;
            }

            public Wiring Refs(string field, IReadOnlyList<Object> values)
            {
                SerializedProperty property = Find(field);
                property.arraySize = values.Count;
                for (int i = 0; i < values.Count; i++)
                {
                    property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
                }

                return this;
            }

            public void Dispose()
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            private SerializedProperty Find(string field)
            {
                SerializedProperty property = serialized.FindProperty(field)
                    ?? throw new InvalidOperationException($"필드를 찾지 못했다: {serialized.targetObject.GetType().Name}.{field}");
                return property;
            }
        }
    }
}

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

        /// <summary>지면 접점에서 차체 원점까지의 높이. GroundSupport 콜라이더 바닥과 맞춘다.</summary>
        private const float RideHeight = 0.75f;

        private const float TruckStartX = 8f;

        public const int CargoCount = 6;

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

            var route = new GameObject("Route").transform;
            var routeEnd = new Vector2(0f, 0f);
            routeEnd = AppendGround(route, "Flat_A", routeEnd, 0f, 30f, groundLayer, groundMaterial);
            routeEnd = AppendGround(route, "Hill", routeEnd, 11f, 16f, groundLayer, groundMaterial);
            routeEnd = AppendGround(route, "Flat_B", routeEnd, 0f, 34f, groundLayer, groundMaterial);
            float goalX = routeEnd.x - 6f;

            CreateLighting();

            GameObject truck = BuildTruck(truckMaterial, bedMaterial, wheelMaterial, bedPhysics, out Transform bedAnchor);
            List<Cargo> cargo = BuildCargo(cargoMaterialA, cargoMaterialB, cargoPhysics);

            Camera firstPersonCamera = BuildFirstPersonCamera(out Transform carryAnchor);
            GameObject player = BuildPlayer(firstPersonCamera, carryAnchor, playerMaterial);
            Camera dioramaCamera = BuildDioramaCamera(truck.transform);

            var systems = new GameObject("Systems");
            var flow = systems.AddComponent<GameFlow>();
            var tracker = systems.AddComponent<CargoTracker>();
            var director = systems.AddComponent<CameraDirector>();
            var hud = systems.AddComponent<PrototypeHud>();

            var mover = truck.GetComponent<TruckMover>();

            using (var wiring = new Wiring(mover))
            {
                wiring.Num("goalX", goalX)
                    .Mask("groundMask", 1 << groundLayer)
                    .Curve("speedOverProgress", BuildSpeedProfile())
                    .Num("minSpeedFactor", 0.06f)
                    .Num("rideHeight", RideHeight);
            }

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

            Debug.Log($"[CargoStack] 프로토타입 씬 생성 완료: {ScenePath} (도착 지점 x={goalX:0.0}, 화물 {cargo.Count}개)");
        }

        /// <summary>
        /// 주행 속도 프로필. 골짜기 구간이 급제동이고, 이 게임에서 짐이 무너지는 유일한 순간이다.
        ///
        /// 감속도가 마찰 한계 μg 를 넘어야 짐이 미끄러진다. 마찰 0.5 기준 4.9m/s² 가 문턱이라
        /// 완만한 감속으로는 아무 일도 일어나지 않는다. 그래서 골짜기를 좁고 깊게 판다.
        /// 경사도 같은 이유로 26도는 되어야 짐이 흘러내리므로, 11도 언덕은 흔들기만 한다.
        /// 첫 키는 0보다 커야 출발한다.
        /// </summary>
        private static AnimationCurve BuildSpeedProfile()
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 0.15f),
                new Keyframe(0.12f, 1f),
                new Keyframe(0.47f, 1f),
                new Keyframe(0.5f, 0.04f),
                new Keyframe(0.58f, 1f),
                new Keyframe(0.92f, 1f),
                new Keyframe(1f, 0.2f));

            for (int i = 0; i < curve.length; i++)
            {
                curve.SmoothTangents(i, 0f);
            }

            return curve;
        }

        /// <summary>경사면을 이어 붙이며 끝점을 돌려준다. 다음 조각의 시작점이 된다.</summary>
        private static Vector2 AppendGround(
            Transform parent, string name, Vector2 startTop, float angleDegrees, float length,
            int layer, Material material)
        {
            const float thickness = 1.5f;
            const float depth = 18f;

            Quaternion rotation = Quaternion.Euler(0f, 0f, angleDegrees);
            Vector3 forward = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            var start = new Vector3(startTop.x, startTop.y, 0f);
            Vector3 finish = start + forward * length;
            Vector3 center = (start + finish) * 0.5f - up * (thickness * 0.5f);

            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.layer = layer;
            block.transform.SetParent(parent);
            block.transform.SetPositionAndRotation(center, rotation);
            block.transform.localScale = new Vector3(length, thickness, depth);
            block.GetComponent<Renderer>().sharedMaterial = material;

            return new Vector2(finish.x, finish.y);
        }

        private static GameObject BuildTruck(
            Material truckMaterial, Material bedMaterial, Material wheelMaterial, PhysicsMaterial bedPhysics,
            out Transform bedAnchor)
        {
            var truck = new GameObject("Truck");
            truck.transform.position = new Vector3(TruckStartX, RideHeight, 0f);

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

        /// <summary>짐은 트럭 옆 바닥에 널어 둔다. 플레이어가 걸어가 하나씩 실어야 한다.</summary>
        private static List<Cargo> BuildCargo(Material materialA, Material materialB, PhysicsMaterial physics)
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
                var position = new Vector3(
                    TruckStartX - 2.6f + index % 3 * 1.5f,
                    size.y * 0.5f + 0.02f,
                    -3.6f - index / 3 * 1.5f);

                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = $"Cargo_{index + 1:00}";
                box.transform.SetParent(cargoRoot, false);
                box.transform.localPosition = position;
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

        private static GameObject BuildPlayer(Camera firstPersonCamera, Transform carryAnchor, Material material)
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(TruckStartX - 2f, 0.05f, -6.5f);

            Rigidbody body = player.AddComponent<Rigidbody>();
            body.mass = 80f;
            body.linearDamping = 0f;
            body.angularDamping = 8f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

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

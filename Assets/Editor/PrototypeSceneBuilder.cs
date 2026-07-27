using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CargoStack.EditorTools
{
    /// <summary>
    /// 검증용 프로토타입 씬을 코드로 만든다.
    /// 씬 파일은 git 자동 병합이 안 되므로(기획서 5장 협업 규칙), 손으로 고치는 대신
    /// 이 스크립트를 고쳐 다시 생성하는 것을 기본 절차로 삼는다.
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePath = SceneFolder + "/Prototype.unity";
        private const string MaterialFolder = "Assets/Materials";

        private const float RideHeight = 0.6f;
        private const float CargoSize = 0.8f;

        [MenuItem("CargoStack/프로토타입 씬 다시 만들기")]
        public static void Build()
        {
            Directory.CreateDirectory(SceneFolder);
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();

            int groundLayer = EnsureLayer("Ground");
            int cargoLayer = EnsureLayer("Cargo");

            Material groundMaterial = EnsureColorMaterial("Ground", new Color(0.45f, 0.62f, 0.36f));
            Material truckMaterial = EnsureColorMaterial("Truck", new Color(0.27f, 0.45f, 0.72f));
            Material wheelMaterial = EnsureColorMaterial("Wheel", new Color(0.16f, 0.16f, 0.18f));
            Material cargoMaterial = EnsureColorMaterial("Cargo", new Color(0.82f, 0.55f, 0.25f));

            PhysicsMaterial bedPhysics = EnsurePhysicsMaterial("BedSurface", 0.55f, 0.65f);
            PhysicsMaterial cargoPhysics = EnsurePhysicsMaterial("CargoSurface", 0.45f, 0.55f);

            ApplyPhysicsProjectSettings();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var route = new GameObject("Route").transform;
            Vector2 end = new Vector2(0f, 0f);
            end = AppendGround(route, "Flat_A", end, 0f, 30f, groundLayer, groundMaterial);
            end = AppendGround(route, "Hill", end, 11f, 16f, groundLayer, groundMaterial);
            end = AppendGround(route, "Flat_B", end, 0f, 34f, groundLayer, groundMaterial);
            float goalX = end.x - 6f;

            const float truckStartX = 8f;
            GameObject truck = BuildTruck(
                new Vector3(truckStartX, RideHeight, 0f), truckMaterial, wheelMaterial, bedPhysics,
                out Transform bedAnchor);

            var cargo = new List<Cargo>
            {
                BuildCargo("Cargo_1", new Vector3(2.4f, CargoSize * 0.5f, 0f), cargoLayer, cargoMaterial, cargoPhysics),
                BuildCargo("Cargo_2", new Vector3(3.5f, CargoSize * 0.5f, 0f), cargoLayer, cargoMaterial, cargoPhysics),
                BuildCargo("Cargo_3", new Vector3(4.6f, CargoSize * 0.5f, 0f), cargoLayer, cargoMaterial, cargoPhysics),
            };

            Camera camera = BuildCamera();
            BuildSunLight();

            var systems = new GameObject("Systems");
            var flow = systems.AddComponent<GameFlow>();
            var placer = systems.AddComponent<CargoPlacer>();
            var tracker = systems.AddComponent<CargoTracker>();
            var hud = systems.AddComponent<PrototypeHud>();

            var mover = truck.GetComponent<TruckMover>();
            var rig = camera.GetComponent<CameraRig>();

            using (var wiring = new Wiring(mover))
            {
                wiring.Num("goalX", goalX)
                    .Mask("groundMask", 1 << groundLayer)
                    .Curve("speedOverProgress", BuildSpeedProfile())
                    .Num("rideHeight", RideHeight);
            }

            using (var wiring = new Wiring(rig))
            {
                wiring.Ref("truck", truck.transform).Ref("bedAnchor", bedAnchor);
            }

            using (var wiring = new Wiring(placer))
            {
                wiring.Ref("view", camera).Mask("cargoMask", 1 << cargoLayer);
            }

            using (var wiring = new Wiring(tracker))
            {
                wiring.Ref("bedAnchor", bedAnchor).Refs("tracked", cargo.ConvertAll(item => (Object)item));
            }

            using (var wiring = new Wiring(flow))
            {
                wiring.Ref("truck", mover).Ref("placer", placer).Ref("tracker", tracker).Ref("cameraRig", rig);
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

            Debug.Log($"[CargoStack] 프로토타입 씬 생성 완료: {ScenePath} (도착 지점 x={goalX:0.0})");
        }

        /// <summary>주행 속도 프로필. 골짜기 구간이 급제동이다. 첫 키는 0보다 커야 출발한다.</summary>
        private static AnimationCurve BuildSpeedProfile()
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 0.15f),
                new Keyframe(0.12f, 1f),
                new Keyframe(0.45f, 1f),
                new Keyframe(0.55f, 0.12f),
                new Keyframe(0.68f, 1f),
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
            const float depth = 8f;

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
            Vector3 position, Material bodyMaterial, Material wheelMaterial, PhysicsMaterial bedPhysics,
            out Transform bedAnchor)
        {
            var truck = new GameObject("Truck");
            truck.transform.position = position;

            Rigidbody body = truck.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            truck.AddComponent<TruckMover>();

            AddTruckPart(truck.transform, "Cab", new Vector3(1.7f, 0.65f, 0f), new Vector3(1.6f, 1.4f, 2.2f), bodyMaterial, bedPhysics);
            AddTruckPart(truck.transform, "BedFloor", new Vector3(-0.85f, -0.05f, 0f), new Vector3(3.5f, 0.2f, 2.2f), bodyMaterial, bedPhysics);
            AddTruckPart(truck.transform, "RearWall", new Vector3(-2.6f, 0.35f, 0f), new Vector3(0.2f, 0.7f, 2.2f), bodyMaterial, bedPhysics);

            AddWheel(truck.transform, "Wheel_Front", new Vector3(1.5f, -0.25f, 0f), wheelMaterial);
            AddWheel(truck.transform, "Wheel_Rear", new Vector3(-1.5f, -0.25f, 0f), wheelMaterial);

            var anchor = new GameObject("BedAnchor");
            anchor.transform.SetParent(truck.transform);
            anchor.transform.localPosition = new Vector3(-0.85f, 0.05f, 0f);
            anchor.transform.localRotation = Quaternion.identity;
            bedAnchor = anchor.transform;

            return truck;
        }

        private static void AddTruckPart(
            Transform parent, string name, Vector3 localPosition, Vector3 localScale,
            Material material, PhysicsMaterial physics)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            part.GetComponent<BoxCollider>().sharedMaterial = physics;
        }

        /// <summary>바퀴는 보기용이다. 지면 추종은 TruckMover 의 레이캐스트가 담당한다.</summary>
        private static void AddWheel(Transform parent, string name, Vector3 localPosition, Material material)
        {
            GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = name;
            wheel.transform.SetParent(parent);
            wheel.transform.localPosition = localPosition;
            wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            wheel.transform.localScale = new Vector3(0.7f, 0.15f, 0.7f);
            wheel.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(wheel.GetComponent<Collider>());
        }

        private static Cargo BuildCargo(
            string name, Vector3 position, int layer, Material material, PhysicsMaterial physics)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.layer = layer;
            box.transform.position = position;
            box.transform.localScale = Vector3.one * CargoSize;
            box.GetComponent<Renderer>().sharedMaterial = material;
            box.GetComponent<BoxCollider>().sharedMaterial = physics;

            Rigidbody body = box.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            return box.AddComponent<Cargo>();
        }

        private static Camera BuildCamera()
        {
            var holder = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = holder.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3.6f;
            camera.backgroundColor = new Color(0.63f, 0.76f, 0.85f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            holder.AddComponent<CameraRig>();
            return camera;
        }

        private static void BuildSunLight()
        {
            var holder = new GameObject("Sun");
            Light light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            holder.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
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

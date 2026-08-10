using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    public class StageDefinitionTests
    {
        [Test]
        public void 얼음큐브_화물은_저마찰_표면으로_직렬화할_수_있다()
        {
            StageCargoDefinition definition = JsonUtility.FromJson<StageCargoDefinition>(
                "{\"assetName\":\"IceCube\",\"maximumSize\":{\"x\":1,\"y\":1,\"z\":1},"
                + "\"mass\":18,\"surfaceType\":1}");

            Assert.AreEqual("IceCube", definition.AssetName);
            Assert.AreEqual(StageCargoSurfaceType.Slippery, definition.SurfaceType);
        }

        [UnityTest]
        public IEnumerator 모든_스테이지의_충돌_대상_월드_애셋에는_메시_콜라이더가_있다()
        {
            string[] sceneNames =
            {
                "Prototype",
                "Stage01_Tutorial",
                "Stage02_SpeedBumps",
                "Stage03_HillsAndPits",
                "Stage04_ComplexRoute",
                "Stage05_Winter",
                "Stage06_FrozenCargo",
                "Stage07_HardBumps",
            };

            foreach (string sceneName in sceneNames)
            {
                yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

                GameObject environment = GameObject.Find("Environment");
                Assert.NotNull(environment, $"{sceneName}: Environment가 없다");

                int obstacleCount = 0;
                int colliderCount = 0;
                foreach (string containerName in new[] { "Trees", "Rocks" })
                {
                    Transform container = environment.transform.Find(containerName);
                    Assert.NotNull(container, $"{sceneName}: {containerName} 컨테이너가 없다");
                    Assert.That(container.childCount, Is.GreaterThan(0),
                        $"{sceneName}: {containerName}가 비어 있다");

                    foreach (Transform obstacle in container)
                    {
                        MeshCollider[] colliders = obstacle.GetComponentsInChildren<MeshCollider>(true);
                        Assert.That(colliders.Length, Is.GreaterThan(0),
                            $"{sceneName}: {obstacle.name}에 MeshCollider가 없다");
                        foreach (MeshCollider collider in colliders)
                        {
                            Assert.IsFalse(collider.isTrigger,
                                $"{sceneName}: {obstacle.name}의 콜라이더가 Trigger다");
                            Assert.NotNull(collider.sharedMesh,
                                $"{sceneName}: {obstacle.name}의 콜라이더 메시가 비어 있다");
                        }

                        obstacleCount++;
                        colliderCount += colliders.Length;
                    }
                }

                if (sceneName == "Stage05_Winter" || sceneName == "Stage06_FrozenCargo")
                {
                    foreach (string containerName in new[] { "Snowmen", "IcePlatforms" })
                    {
                        Transform container = environment.transform.Find(containerName);
                        Assert.NotNull(container, $"{sceneName}: {containerName} 컨테이너가 없다");
                        foreach (Transform obstacle in container)
                        {
                            MeshCollider[] colliders = obstacle.GetComponentsInChildren<MeshCollider>(true);
                            Assert.That(colliders.Length, Is.GreaterThan(0),
                                $"{sceneName}: {obstacle.name}에 MeshCollider가 없다");
                            obstacleCount++;
                            colliderCount += colliders.Length;
                        }
                    }

                    Transform landmarks = environment.transform.Find("IceLandmarks");
                    Assert.NotNull(landmarks, $"{sceneName}: IceLandmarks 컨테이너가 없다");
                    foreach (Transform obstacle in landmarks)
                    {
                        if (!obstacle.name.Contains("IceMountain"))
                        {
                            continue;
                        }

                        MeshCollider[] colliders = obstacle.GetComponentsInChildren<MeshCollider>(true);
                        Assert.That(colliders.Length, Is.GreaterThan(0),
                            $"{sceneName}: {obstacle.name}에 MeshCollider가 없다");
                        obstacleCount++;
                        colliderCount += colliders.Length;
                    }
                }

                Debug.Log(
                    $"[CargoStack] {sceneName} 월드 충돌체: "
                    + $"충돌 애셋 {obstacleCount}개, MeshCollider {colliderCount}개");
            }
        }

        [UnityTest]
        public IEnumerator 튜토리얼_스테이지는_정의한_구성으로_생성된다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage01_Tutorial",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();

            Assert.NotNull(context, "StageContext가 없다");
            Assert.NotNull(context.Definition, "스테이지 정의가 연결되지 않았다");
            Assert.AreEqual("stage-01", context.Definition.StageId);
            Assert.AreEqual(
                SceneManager.GetActiveScene().name,
                context.Definition.SceneName);
            Assert.NotNull(route, "경로가 생성되지 않았다");
            Assert.That(route.TotalLength, Is.GreaterThan(125f));
            Assert.NotNull(truck, "트럭이 생성되지 않았다");
            Assert.AreEqual(context.Definition.CargoCount, cargo.Length);
            Assert.AreEqual(6, cargo.Length, "튜토리얼 화물 구성이 달라졌다");

            int hills = 0;
            bool wasOnHill = false;
            float highestPoint = float.NegativeInfinity;
            for (int index = 0; index < route.SampleCount; index++)
            {
                float height = route.SampleAt(index).y;
                highestPoint = Mathf.Max(highestPoint, height);
                bool isOnHill = height > 2f;
                if (isOnHill && !wasOnHill)
                {
                    hills++;
                }

                wasOnHill = isOnHill;
            }

            Debug.Log(
                $"[CargoStack] Stage01 경로: 길이 {route.TotalLength:0.0}m, "
                + $"언덕 {hills}개, 최고점 {highestPoint:0.0}m");
            Assert.AreEqual(2, hills, "Stage 01 경로에는 분리된 언덕이 두 개여야 한다");

            int boxes = 0;
            int capsules = 0;
            foreach (Cargo item in cargo)
            {
                boxes += item.GetComponent<BoxCollider>() != null ? 1 : 0;
                capsules += item.GetComponent<CapsuleCollider>() != null ? 1 : 0;
            }

            Assert.AreEqual(4, boxes, "상자·조각상·전등 화물은 박스 프록시 네 개여야 한다");
            Assert.AreEqual(2, capsules, "원통 화물은 두 개여야 한다");
        }

        [UnityTest]
        public IEnumerator 두번째_스테이지는_방지턱_두개와_구르는_원통을_만든다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage02_SpeedBumps",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();

            Assert.NotNull(context, "StageContext가 없다");
            Assert.NotNull(context.Definition, "스테이지 정의가 연결되지 않았다");
            Assert.AreEqual("stage-02", context.Definition.StageId);
            Assert.AreEqual(
                SceneManager.GetActiveScene().name,
                context.Definition.SceneName);
            Assert.NotNull(route, "경로가 생성되지 않았다");
            Assert.That(route.TotalLength, Is.GreaterThan(100f));
            Assert.AreEqual(5, cargo.Length, "Stage 02 화물 구성이 달라졌다");

            int raisedRegions = 0;
            bool wasRaised = false;
            for (int index = 0; index < route.SampleCount; index++)
            {
                bool isRaised = route.SampleAt(index).y > 0.3f;
                if (isRaised && !wasRaised)
                {
                    raisedRegions++;
                }

                wasRaised = isRaised;
            }

            Assert.AreEqual(2, raisedRegions, "방지턱 두 구간이 경로 높이에 반영되지 않았다");

            int boxes = 0;
            int capsules = 0;
            foreach (Cargo item in cargo)
            {
                boxes += item.GetComponent<BoxCollider>() != null ? 1 : 0;
                capsules += item.GetComponent<CapsuleCollider>() != null ? 1 : 0;
            }

            Assert.AreEqual(4, boxes, "상자 화물은 네 개여야 한다");
            Assert.AreEqual(1, capsules, "구르는 원통 화물은 하나여야 한다");
        }

        [UnityTest]
        public IEnumerator 세번째_스테이지는_대형_박스_세개와_일반_화물_네개를_만든다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage03_HillsAndPits",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();

            Assert.NotNull(context, "StageContext가 없다");
            Assert.NotNull(context.Definition, "스테이지 정의가 연결되지 않았다");
            Assert.AreEqual("stage-03", context.Definition.StageId);
            Assert.AreEqual(
                SceneManager.GetActiveScene().name,
                context.Definition.SceneName);
            Assert.NotNull(route, "경로가 생성되지 않았다");
            Assert.That(route.TotalLength, Is.GreaterThan(180f));
            Assert.AreEqual(7, cargo.Length, "Stage 03 화물은 일곱 개여야 한다");

            int hills = CountHeightRegions(route, pointHeight => pointHeight > 2f);
            int pits = CountHeightRegions(route, pointHeight => pointHeight < -1f);
            int firstPit = FindHeightSample(route, pointHeight => pointHeight < -1f);
            int lastPit = FindHeightSample(route, pointHeight => pointHeight < -1f, searchBackward: true);
            int firstHill = FindHeightSample(route, pointHeight => pointHeight > 2f);
            int lastHill = FindHeightSample(route, pointHeight => pointHeight > 2f, searchBackward: true);
            float lowestPoint = float.PositiveInfinity;
            float highestPoint = float.NegativeInfinity;
            for (int index = 0; index < route.SampleCount; index++)
            {
                float height = route.SampleAt(index).y;
                lowestPoint = Mathf.Min(lowestPoint, height);
                highestPoint = Mathf.Max(highestPoint, height);
            }

            Debug.Log(
                $"[CargoStack] Stage03 경로: 길이 {route.TotalLength:0.0}m, "
                + $"언덕 {hills}개, 구덩이 {pits}개, "
                + $"최저점 {lowestPoint:0.0}m, 최고점 {highestPoint:0.0}m");
            Assert.AreEqual(1, hills, "언덕이 한 구간으로 이어지지 않았다");
            Assert.AreEqual(1, pits, "구덩이가 한 구간으로 이어지지 않았다");
            Assert.That(firstPit, Is.GreaterThan(route.SampleCount * 0.2f),
                "구덩이 전에 충분한 평지가 없다");
            Assert.That(firstHill, Is.GreaterThan(lastPit),
                "구덩이보다 언덕이 먼저 나온다");
            Assert.That(firstHill - lastPit, Is.GreaterThan(route.SampleCount * 0.1f),
                "구덩이와 언덕 사이에 충분한 평지가 없다");
            Assert.That(route.SampleCount - 1 - lastHill, Is.GreaterThan(route.SampleCount * 0.2f),
                "언덕 뒤에 충분한 평지가 없다");
            Assert.That(Mathf.Abs(route.SampleAt((lastPit + firstHill) / 2).y), Is.LessThan(0.25f),
                "구덩이와 언덕 사이가 평지가 아니다");

            int boxes = 0;
            int barrels = 0;
            int largeBoxes = 0;
            foreach (Cargo item in cargo)
            {
                BoxCollider box = item.GetComponent<BoxCollider>();
                boxes += box != null ? 1 : 0;
                barrels += item.GetComponent<CapsuleCollider>() != null ? 1 : 0;
                largeBoxes += box != null && box.size.y > 1.9f ? 1 : 0;
            }

            Assert.AreEqual(5, boxes, "박스 화물은 다섯 개여야 한다");
            Assert.AreEqual(2, barrels, "드럼통 화물은 두 개여야 한다");
            Assert.AreEqual(3, largeBoxes, "Stage 04 규격의 대형 박스가 세 개여야 한다");
        }

        [UnityTest]
        public IEnumerator 네번째_스테이지는_복합_코스에_대형_박스를_포함한_다양한_화물_여덟개를_만든다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage04_ComplexRoute",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();

            Assert.NotNull(context, "StageContext가 없다");
            Assert.NotNull(context.Definition, "스테이지 정의가 연결되지 않았다");
            Assert.AreEqual("stage-04", context.Definition.StageId);
            Assert.AreEqual(SceneManager.GetActiveScene().name, context.Definition.SceneName);
            Assert.NotNull(route, "경로가 생성되지 않았다");
            Assert.That(route.TotalLength, Is.GreaterThan(180f));
            Assert.AreEqual(8, cargo.Length, "Stage 04 화물은 여덟 개여야 한다");
            Assert.IsTrue(
                context.Definition.Cargo[0].StretchToMaximumSize,
                "첫 화물의 대형 박스 비균등 크기 조절이 꺼져 있다");

            int hills = CountHeightRegions(route, pointHeight => pointHeight > 2f);
            int pits = CountHeightRegions(route, pointHeight => pointHeight < -1f);
            float lowestZ = float.PositiveInfinity;
            float highestZ = float.NegativeInfinity;
            for (int index = 0; index < route.SampleCount; index++)
            {
                float lateral = route.SampleAt(index).z;
                lowestZ = Mathf.Min(lowestZ, lateral);
                highestZ = Mathf.Max(highestZ, lateral);
            }

            Debug.Log(
                $"[CargoStack] Stage04 경로: 길이 {route.TotalLength:0.0}m, "
                + $"언덕 {hills}개, 구덩이 {pits}개, 좌우 변화 {highestZ - lowestZ:0.0}m");
            Assert.That(hills, Is.GreaterThanOrEqualTo(2), "복합 코스에 언덕이 충분하지 않다");
            Assert.That(pits, Is.GreaterThanOrEqualTo(2), "복합 코스에 구덩이가 충분하지 않다");
            Assert.That(highestZ - lowestZ, Is.GreaterThan(12f), "S자 좌우 굴곡이 충분하지 않다");

            int cardboardBoxes = 0;
            int barrels = 0;
            int busts = 0;
            int lamps = 0;
            BoxCollider largeBox = null;
            foreach (Cargo item in cargo)
            {
                foreach (Transform child in item.transform)
                {
                    switch (child.name)
                    {
                        case "ImportedVisual_CardboardBox":
                            cardboardBoxes++;
                            BoxCollider box = item.GetComponent<BoxCollider>();
                            if (largeBox == null || box.size.y > largeBox.size.y)
                            {
                                largeBox = box;
                            }
                            break;
                        case "ImportedVisual_BlueBarrel":
                            barrels++;
                            break;
                        case "ImportedVisual_MarbleBust":
                            busts++;
                            break;
                        case "ImportedVisual_FloorLamp":
                            lamps++;
                            break;
                    }
                }
            }

            Assert.AreEqual(2, cardboardBoxes, "상자 화물은 두 개여야 한다");
            Assert.AreEqual(2, barrels, "드럼통 화물은 두 개여야 한다");
            Assert.AreEqual(2, busts, "대리석 흉상 화물은 두 개여야 한다");
            Assert.AreEqual(2, lamps, "스탠드 조명 화물은 두 개여야 한다");
            Assert.NotNull(largeBox, "대형 박스가 없다");
            Rigidbody largeBoxBody = largeBox.GetComponent<Rigidbody>();
            Debug.Log(
                $"[CargoStack] Stage04 대형 박스: 크기 "
                + $"{largeBox.size.x:0.00}×{largeBox.size.y:0.00}×{largeBox.size.z:0.00}m, "
                + $"질량 {largeBoxBody.mass:0.0}kg");
            Assert.That(largeBox.size.x, Is.GreaterThan(1f), "대형 박스 폭이 충분하지 않다");
            Assert.That(largeBox.size.y, Is.GreaterThan(1.9f), "대형 박스가 냉장고처럼 길쭉하지 않다");

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            PlayerCargoInteractor interactor =
                Object.FindAnyObjectByType<PlayerCargoInteractor>();
            player.SetWorldPose(
                largeBox.transform.position + Vector3.back,
                Quaternion.identity,
                Vector3.zero);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(interactor.TryPickUp(largeBox.GetComponent<Cargo>()),
                "대형 박스를 집을 수 없다");
            interactor.DropHeldCargo();
        }

        [UnityTest]
        public IEnumerator 다섯번째_스테이지는_언덕과_빙판_드리프트가_있는_설원_S자길이다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage05_Winter",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();
            GameObject roadSurface = GameObject.Find("RoadSurface");
            GameObject groundSurface = GameObject.Find("GroundSurface");
            BoxCollider roadCollider = GameObject.Find("Road_000")?.GetComponent<BoxCollider>();
            GameObject environment = GameObject.Find("Environment");

            Assert.NotNull(context, "겨울 스테이지의 StageContext가 없다");
            Assert.NotNull(context.Definition, "겨울 스테이지 정의가 연결되지 않았다");
            Assert.AreEqual("stage-05", context.Definition.StageId);
            Assert.AreEqual(StageTheme.Winter, context.Definition.Theme);
            Assert.AreEqual("Stage05_Winter", SceneManager.GetActiveScene().name);
            Assert.NotNull(route, "겨울 경로가 생성되지 않았다");
            Assert.That(route.TotalLength, Is.GreaterThan(190f));
            Assert.AreEqual(7, cargo.Length, "설원 스테이지는 화물 일곱 개를 운송해야 한다");
            Assert.AreEqual(2, context.Definition.RopeCount, "로프는 두 개만 제공해야 한다");
            Assert.AreEqual(16.6667f, context.Definition.MaxSpeed, 0.01f);

            int boxes = 0;
            int capsules = 0;
            int fragile = 0;
            foreach (Cargo item in cargo)
            {
                capsules += item.GetComponent<CapsuleCollider>() != null ? 1 : 0;
                fragile += item.GetComponent<CargoBreakable>() != null ? 1 : 0;
                foreach (Transform child in item.transform)
                {
                    boxes += child.name == "ImportedVisual_CardboardBox" ? 1 : 0;
                }
            }

            Assert.AreEqual(4, boxes, "상자 화물은 네 개여야 한다");
            Assert.AreEqual(2, capsules, "굴러가는 원통 화물은 두 개여야 한다");
            Assert.AreEqual(1, fragile, "빙판 주행의 충격을 관리할 파손 화물이 하나 있어야 한다");

            float lowest = float.PositiveInfinity;
            float highest = float.NegativeInfinity;
            float leftmost = float.PositiveInfinity;
            float rightmost = float.NegativeInfinity;
            for (int index = 0; index < route.SampleCount; index++)
            {
                Vector3 point = route.SampleAt(index);
                lowest = Mathf.Min(lowest, point.y);
                highest = Mathf.Max(highest, point.y);
                leftmost = Mathf.Min(leftmost, point.z);
                rightmost = Mathf.Max(rightmost, point.z);
            }

            Assert.That(highest, Is.GreaterThan(3.5f), "첫 번째 설원 언덕이 충분히 높지 않다");
            Assert.That(lowest, Is.LessThan(-1f), "내리막 뒤의 얕은 골짜기가 없다");
            Assert.That(highest - lowest, Is.GreaterThan(5f), "오르막·내리막 고저차가 충분하지 않다");
            Assert.That(leftmost, Is.LessThan(-9f), "S자 코스의 한쪽 커브가 너무 약하다");
            Assert.That(rightmost, Is.GreaterThan(9f), "S자 코스의 반대쪽 커브가 너무 약하다");

            Assert.IsNull(GameObject.Find("IceDriftZones"),
                "얼음 도로 위에 별도 얼음 오버레이를 다시 올리면 안 된다");

            AnimationCurve speedCurve = context.Definition.CopySpeedOverProgress();
            for (int sample = 2; sample <= 8; sample++)
            {
                Assert.That(
                    speedCurve.Evaluate(sample / 10f),
                    Is.GreaterThan(0.95f),
                    "주행 중간에 급제동 골짜기가 생겼다");
            }

            Assert.NotNull(roadSurface, "얼음 도로 표면이 없다");
            Material roadMaterial = roadSurface.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.NotNull(roadMaterial, "얼음 도로 재질이 없다");
            Assert.AreEqual("IceRoad", roadMaterial.name);
            Assert.That(roadMaterial.GetFloat("_Glossiness"), Is.GreaterThan(0.8f));
            Assert.NotNull(roadMaterial.mainTexture, "4K 얼음 텍스처가 얼음 도로 재질에 연결되지 않았다");
            Assert.AreEqual(
                "ice_toon_smooth_1",
                roadMaterial.mainTexture.name,
                "Asset Store 4K 얼음 텍스처가 아닌 재질이 연결되었다");
            Vector2[] roadUvs = roadSurface.GetComponent<MeshFilter>().sharedMesh.uv;
            Assert.That(roadUvs.Length, Is.GreaterThan(0), "얼음 도로 메시의 UV가 없다");
            Assert.That(
                MaxUvCoordinate(roadUvs, true),
                Is.GreaterThan(20f),
                "얼음 텍스처가 도로 길이 방향으로 타일링되지 않는다");
            Assert.That(
                MaxUvCoordinate(roadUvs, false),
                Is.GreaterThan(1f),
                "얼음 텍스처가 도로 폭 방향으로 매핑되지 않는다");
            Assert.NotNull(roadCollider, "얼음 도로 물리 표면이 없다");
            Assert.NotNull(roadCollider.sharedMaterial, "얼음 도로 저마찰 재질이 연결되지 않았다");
            Assert.That(roadCollider.sharedMaterial.dynamicFriction, Is.LessThan(0.1f));
            Assert.That(roadCollider.sharedMaterial.staticFriction, Is.LessThan(0.1f));
            Assert.AreEqual(
                PhysicsMaterialCombine.Minimum,
                roadCollider.sharedMaterial.frictionCombine,
                "빙판 도로는 다른 재질과 접촉해도 최소 마찰을 사용해야 한다");
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            Assert.NotNull(truck, "빙판 접지력을 계산할 TruckMover가 없다");
            Assert.AreEqual(
                roadCollider.sharedMaterial.dynamicFriction,
                truck.SurfaceFriction,
                0.001f,
                "트럭이 바퀴 아래 얼음 PhysicsMaterial의 마찰을 읽지 않는다");

            Assert.NotNull(groundSurface, "눈 지면 표면이 없다");
            Material groundMaterial = groundSurface.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.NotNull(groundMaterial, "눈 지면 재질이 없다");
            Assert.AreEqual("SnowGround", groundMaterial.name);
            Assert.NotNull(groundMaterial.mainTexture, "4K 눈 텍스처가 눈 지면 재질에 연결되지 않았다");
            Assert.AreEqual(
                "snow_solid_1",
                groundMaterial.mainTexture.name,
                "Asset Store 4K 눈 텍스처가 아닌 재질이 연결되었다");
            Vector2[] groundUvs = groundSurface.GetComponent<MeshFilter>().sharedMesh.uv;
            Assert.That(groundUvs.Length, Is.GreaterThan(0), "눈 지면 메시의 UV가 없다");
            Assert.That(
                MaxUvCoordinate(groundUvs, true),
                Is.GreaterThan(20f),
                "눈 텍스처가 지면 길이 방향으로 타일링되지 않는다");
            Assert.That(
                MaxUvCoordinate(groundUvs, false),
                Is.GreaterThan(5f),
                "눈 텍스처가 지면 폭 방향으로 타일링되지 않는다");
            Assert.NotNull(environment, "겨울 환경 배치가 없다");
            Assert.That(
                environment.GetComponentsInChildren<Renderer>(true).Length,
                Is.GreaterThan(0),
                "겨울 월드에 눈·얼음 시각물이 없다");

            Transform trees = environment.transform.Find("Trees");
            Assert.NotNull(trees, "겨울 나무 컨테이너가 없다");
            Transform landmarks = environment.transform.Find("IceLandmarks");
            Transform snowmen = environment.transform.Find("Snowmen");
            Transform platforms = environment.transform.Find("IcePlatforms");
            Assert.NotNull(landmarks, "MochiModels 얼음 지형 컨테이너가 없다");
            Assert.NotNull(snowmen, "MochiModels 눈사람 컨테이너가 없다");
            Assert.NotNull(platforms, "MochiModels 얼음 플랫폼 컨테이너가 없다");

            bool hasMochiTree = false;
            bool hasMochiMountain = false;
            bool hasMochiCave = false;
            bool hasMochiRock = false;
            bool hasMochiSnowman = false;
            bool hasMochiPlatform = false;
            foreach (Transform item in environment.GetComponentsInChildren<Transform>(true))
            {
                if (!item.name.StartsWith("MochiModels_"))
                {
                    continue;
                }

                hasMochiTree |= item.name.Contains("IceTree");
                hasMochiMountain |= item.name.Contains("IceMountain");
                hasMochiCave |= item.name.Contains("IceCave");
                hasMochiRock |= item.name.Contains("IceRock");
                hasMochiSnowman |= item.name.Contains("Snowman");
                hasMochiPlatform |= item.name.Contains("IcePlatform");
            }

            Assert.IsTrue(
                hasMochiTree,
                "MochiModels 3D Low Poly Environment Assets의 IceTree가 겨울 나무에 연결되지 않았다");
            Assert.IsTrue(
                hasMochiMountain,
                "MochiModels 3D Low Poly Environment Assets의 IceMountain이 겨울 지형에 연결되지 않았다");
            Assert.IsTrue(
                hasMochiCave,
                "MochiModels 3D Low Poly Environment Assets의 IceCave가 겨울 지형에 연결되지 않았다");
            Assert.IsTrue(
                hasMochiRock,
                "MochiModels 3D Low Poly Environment Assets의 IceRock이 겨울 지형에 연결되지 않았다");
            Assert.IsTrue(
                hasMochiSnowman,
                "MochiModels 3D Low Poly Environment Assets의 Snowman이 겨울 지형에 연결되지 않았다");
            Assert.IsTrue(
                hasMochiPlatform,
                "MochiModels 3D Low Poly Environment Assets의 IcePlatform이 겨울 지형에 연결되지 않았다");

            Assert.That(
                landmarks.GetComponentsInChildren<Renderer>(true).Length,
                Is.GreaterThan(0),
                "겨울 얼음 랜드마크에 렌더러가 없다");
            Assert.That(
                trees.GetComponentsInChildren<Renderer>(true).Length,
                Is.GreaterThan(0),
                "겨울 IceTree에 렌더러가 없다");
            Assert.That(
                snowmen.GetComponentsInChildren<Renderer>(true).Length,
                Is.GreaterThan(0),
                "겨울 Snowman에 렌더러가 없다");
            Assert.That(
                platforms.GetComponentsInChildren<Renderer>(true).Length,
                Is.GreaterThan(0),
                "겨울 IcePlatform에 렌더러가 없다");

            AssertWinterEnvironmentStaysOutsideRoad(route, environment);

            foreach (Renderer renderer in trees.GetComponentsInChildren<Renderer>(true))
            {
                Assert.That(renderer.sharedMaterials.Length, Is.GreaterThan(0));
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.NotNull(
                        material,
                        "MochiModels IceTree의 원본 재질이 제거되었다");
                }
            }

            Debug.Log(
                $"[CargoStack] Stage05 겨울 경로: 길이 {route.TotalLength:0.0}m, "
                + $"화물 {cargo.Length}개, 얼음 마찰 {roadCollider.sharedMaterial.dynamicFriction:0.00}");
        }

        [UnityTest]
        public IEnumerator 여섯번째_스테이지는_얼음_화물을_싣고_급경사_설원길을_달린다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage06_FrozenCargo",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();
            BoxCollider roadCollider = GameObject.Find("Road_000")?.GetComponent<BoxCollider>();

            Assert.NotNull(context, "새 설원 스테이지의 StageContext가 없다");
            Assert.AreEqual("stage-06", context.Definition.StageId);
            Assert.AreEqual(StageTheme.Winter, context.Definition.Theme);
            Assert.AreEqual(7, cargo.Length, "Stage06 화물 수가 기획과 다르다");
            Assert.AreEqual(2, context.Definition.RopeCount, "Stage06 로프 수가 기획과 다르다");
            Assert.AreEqual(16.6667f, context.Definition.MaxSpeed, 0.01f);
            Assert.NotNull(route, "Stage06 경로가 없다");
            Assert.That(route.TotalLength, Is.GreaterThan(215f));
            Assert.NotNull(roadCollider, "Stage06 얼음 도로 콜라이더가 없다");
            Assert.That(roadCollider.sharedMaterial.dynamicFriction, Is.LessThan(0.1f));

            float lowest = float.PositiveInfinity;
            float highest = float.NegativeInfinity;
            float leftmost = float.PositiveInfinity;
            float rightmost = float.NegativeInfinity;
            for (int index = 0; index < route.SampleCount; index++)
            {
                Vector3 point = route.SampleAt(index);
                lowest = Mathf.Min(lowest, point.y);
                highest = Mathf.Max(highest, point.y);
                leftmost = Mathf.Min(leftmost, point.z);
                rightmost = Mathf.Max(rightmost, point.z);
            }

            Assert.That(lowest, Is.LessThan(-3.5f), "Stage06에 깊은 내리막이 없다");
            Assert.That(highest, Is.GreaterThan(7.5f), "Stage06에 높은 오르막이 없다");
            Assert.That(rightmost - leftmost, Is.GreaterThan(25f), "Stage06 코너 변화가 부족하다");

            int iceCubes = 0;
            foreach (Cargo item in cargo)
            {
                Transform iceVisual = item.transform.Find("ImportedVisual_IceCube");
                if (iceVisual == null)
                {
                    continue;
                }

                iceCubes++;
                Collider iceCollider = item.GetComponent<Collider>();
                Assert.NotNull(iceCollider.sharedMaterial, "얼음 화물에 물리 재질이 없다");
                Assert.That(
                    iceCollider.sharedMaterial.dynamicFriction,
                    Is.EqualTo(0.015f).Within(0.001f));
                Assert.AreEqual(
                    PhysicsMaterialCombine.Minimum,
                    iceCollider.sharedMaterial.frictionCombine);

                Renderer renderer = iceVisual.GetComponentInChildren<Renderer>();
                Assert.NotNull(renderer, "얼음 큐브가 화면에 보일 Renderer를 갖지 않는다");
                Assert.That(renderer.sharedMaterial.GetFloat("_Glossiness"), Is.GreaterThan(0.7f));
            }

            Assert.AreEqual(2, iceCubes, "새 설원 스테이지에는 얼음 큐브 두 개가 있어야 한다");
            Debug.Log(
                $"[CargoStack] Stage06 얼음 화물: 경로 {route.TotalLength:0.0}m, "
                + $"높이 {lowest:0.0}~{highest:0.0}m, 좌우 폭 {rightmost - leftmost:0.0}m, "
                + $"얼음 큐브 {iceCubes}개");
        }

        [UnityTest]
        public IEnumerator 일곱번째_스테이지는_대형_방지턱_다섯개가_이어지는_고난도_맵이다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage07_HardBumps",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();

            Assert.NotNull(context, "Stage07 StageContext가 없다");
            Assert.AreEqual("stage-07", context.Definition.StageId);
            Assert.AreEqual(StageTheme.Default, context.Definition.Theme);
            Assert.AreEqual(8, cargo.Length, "Stage07 화물 수가 기획과 다르다");
            Assert.AreEqual(3, context.Definition.RopeCount, "Stage07 로프 수가 기획과 다르다");
            Assert.AreEqual(16.6667f, context.Definition.MaxSpeed, 0.01f);
            Assert.NotNull(route, "Stage07 경로가 없다");
            Assert.That(route.TotalLength, Is.GreaterThan(160f));

            int raisedRegions = 0;
            bool wasRaised = false;
            float highest = float.NegativeInfinity;
            float steepestAngle = 0f;
            Vector3 previous = route.SampleAt(0);
            for (int index = 0; index < route.SampleCount; index++)
            {
                Vector3 point = route.SampleAt(index);
                bool isRaised = point.y > 0.35f;
                if (isRaised && !wasRaised)
                {
                    raisedRegions++;
                }

                if (index > 0)
                {
                    float horizontal = Vector2.Distance(
                        new Vector2(previous.x, previous.z),
                        new Vector2(point.x, point.z));
                    steepestAngle = Mathf.Max(
                        steepestAngle,
                        Mathf.Atan2(Mathf.Abs(point.y - previous.y), horizontal)
                            * Mathf.Rad2Deg);
                }

                highest = Mathf.Max(highest, point.y);
                previous = point;
                wasRaised = isRaised;
            }

            Assert.AreEqual(5, raisedRegions, "독립된 대형 방지턱이 다섯 구간이어야 한다");
            Assert.That(highest, Is.GreaterThan(1.25f), "마지막 방지턱 높이가 충분하지 않다");
            Assert.That(steepestAngle, Is.GreaterThan(18f), "방지턱 경사가 빡세지 않다");

            int capsules = 0;
            int fragile = 0;
            foreach (Cargo item in cargo)
            {
                capsules += item.GetComponent<CapsuleCollider>() != null ? 1 : 0;
                fragile += item.GetComponent<CargoBreakable>() != null ? 1 : 0;
            }

            Assert.AreEqual(2, capsules, "방지턱에서 구르는 원통 화물은 두 개여야 한다");
            Assert.AreEqual(2, fragile, "충격 관리가 필요한 파손 화물은 두 개여야 한다");
            Debug.Log(
                $"[CargoStack] Stage07 대형 방지턱: 경로 {route.TotalLength:0.0}m, "
                + $"방지턱 {raisedRegions}개, 최고 {highest:0.0}m, "
                + $"최대 경사 {steepestAngle:0.0}°, 화물 {cargo.Length}개");
        }

        [UnityTest]
        public IEnumerator 두번째부터_도로_장애물이_스테이지마다_하나씩_늘어난다()
        {
            string[] scenes =
            {
                "Stage01_Tutorial",
                "Stage02_SpeedBumps",
                "Stage03_HillsAndPits",
                "Stage04_ComplexRoute",
                "Stage05_Winter",
                "Stage06_FrozenCargo",
                "Stage07_HardBumps",
            };

            for (int index = 0; index < scenes.Length; index++)
            {
                yield return SceneManager.LoadSceneAsync(scenes[index], LoadSceneMode.Single);
                GameObject obstacles = GameObject.Find("RoadObstacles");
                if (index == 0)
                {
                    Assert.IsNull(obstacles, "Stage01에는 도로 장애물이 없어야 한다");
                    continue;
                }

                int expected = index + 1;
                Assert.NotNull(obstacles, $"{scenes[index]}에 도로 장애물이 없다");
                Assert.AreEqual(expected, obstacles.transform.childCount,
                    $"{scenes[index]} 장애물 수가 난이도 단계와 다르다");
                foreach (Transform obstacle in obstacles.transform)
                {
                    BoxCollider collider = obstacle.GetComponent<BoxCollider>();
                    Assert.NotNull(collider, $"{obstacle.name}에 차량 충돌체가 없다");
                    Assert.IsTrue(collider.isTrigger,
                        $"{obstacle.name}가 화물 물리에 직접 간섭한다");
                    Assert.AreEqual(LayerMask.NameToLayer("Obstacle"), obstacle.gameObject.layer);
                }
            }

            Debug.Log("[CargoStack] 도로 장애물: Stage01=0, Stage02~07=2/3/4/5/6/7개");
        }

        private static void AssertWinterEnvironmentStaysOutsideRoad(
            RoutePath route,
            GameObject environment)
        {
            const float roadHalfWidth = 6.5f;
            const float visualClearance = 1.5f;
            Renderer[] renderers = environment.GetComponentsInChildren<Renderer>(true);

            for (int sample = 0; sample < route.SampleCount; sample++)
            {
                Vector3 routePoint = route.SampleAt(sample);
                foreach (Renderer renderer in renderers)
                {
                    Bounds bounds = renderer.bounds;
                    float closestX = Mathf.Clamp(
                        routePoint.x,
                        bounds.min.x,
                        bounds.max.x);
                    float closestZ = Mathf.Clamp(
                        routePoint.z,
                        bounds.min.z,
                        bounds.max.z);
                    float distance = Vector2.Distance(
                        new Vector2(routePoint.x, routePoint.z),
                        new Vector2(closestX, closestZ));

                    Assert.That(
                        distance,
                        Is.GreaterThanOrEqualTo(roadHalfWidth + visualClearance),
                        $"겨울 환경 렌더러가 도로를 막는다: {renderer.name}, "
                        + $"경로 샘플 {sample}, 중심선 거리 {distance:0.00}m");
                }
            }
        }

        private static float MaxUvCoordinate(Vector2[] uvs, bool horizontal)
        {
            float maximum = float.MinValue;
            foreach (Vector2 uv in uvs)
            {
                maximum = Mathf.Max(maximum, horizontal ? uv.x : uv.y);
            }

            return maximum;
        }

        [UnityTest]
        public IEnumerator 원통_화물도_집을_수_있다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage02_SpeedBumps",
                LoadSceneMode.Single);

            Cargo cylinder = null;
            foreach (Cargo item in Object.FindObjectsByType<Cargo>())
            {
                if (item.GetComponent<CapsuleCollider>() != null)
                {
                    cylinder = item;
                    break;
                }
            }

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            PlayerCargoInteractor interactor =
                Object.FindAnyObjectByType<PlayerCargoInteractor>();

            Assert.NotNull(cylinder, "원통 화물이 없다");
            Assert.NotNull(player, "플레이어가 없다");
            Assert.NotNull(interactor, "화물 상호작용기가 없다");

            player.SetWorldPose(
                cylinder.transform.position + Vector3.back,
                Quaternion.identity,
                Vector3.zero);
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(interactor.TryPickUp(cylinder), "CapsuleCollider 원통을 집지 못했다");
            Assert.AreSame(cylinder, interactor.HeldCargo);
            interactor.DropHeldCargo();
        }

        private static int CountHeightRegions(
            RoutePath route,
            System.Func<float, bool> belongsToRegion)
        {
            int regions = 0;
            bool wasInside = false;
            for (int index = 0; index < route.SampleCount; index++)
            {
                bool isInside = belongsToRegion(route.SampleAt(index).y);
                if (isInside && !wasInside)
                {
                    regions++;
                }

                wasInside = isInside;
            }

            return regions;
        }

        private static int FindHeightSample(
            RoutePath route,
            System.Func<float, bool> matches,
            bool searchBackward = false)
        {
            int index = searchBackward ? route.SampleCount - 1 : 0;
            int end = searchBackward ? -1 : route.SampleCount;
            int step = searchBackward ? -1 : 1;
            for (; index != end; index += step)
            {
                if (matches(route.SampleAt(index).y))
                {
                    return index;
                }
            }

            return -1;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    /// <summary>
    /// MVP 검증 대상 두 축(1인칭 짐 쌓기·자동 주행)이 실제로 성립하는지 확인한다.
    /// 특히 "마찰만으로 짐이 실려 간다"는 전제가 깨지면 게임 자체가 성립하지 않으므로
    /// 그 전제를 회귀 테스트로 고정해 둔다.
    /// </summary>
    public class CoreLoopTests
    {
        private const float DriveTimeoutSeconds = 40f;
        private const int BlueTruckV2TriangleCount = 504658;
        private const float WheelRetentionRadius = 0.535f;
        private const float WheelRetentionHalfWidth = 0.190f;
        private const float WheelMeshTolerance = 0.007f;
        private const float BedCenterX = -1.835f;
        private const float BedFloorTop = 0.20f;
        private const float BedInsideLength = 2.29f;
        private const float BedInsideWidth = 2.26f;
        private const float BedWallHeight = 0.70f;
        private const float BedFrontBarrierX = -0.64f;
        private const float BedFrontBarrierHeight = 0.68f;
        private const float BedFrontBarrierWidth = 2.26f;
        private const float BedFloorThickness = 0.12f;
        private const float BedWallThickness = 0.12f;
        private const float BedMinX = BedCenterX - BedInsideLength * 0.5f;
        private const float BedMaxX = BedCenterX + BedInsideLength * 0.5f;
        private const float BedMinZ = -BedInsideWidth * 0.5f;
        private const float BedMaxZ = BedInsideWidth * 0.5f;
        private const float FrontCargoLimit = BedFrontBarrierX - BedWallThickness * 0.5f;
        private const float CabInteriorForbiddenMinX = BedFrontBarrierX + BedWallThickness * 0.5f;
        private static readonly Vector3 TailgatePivotPosition =
            new(BedMinX - BedWallThickness * 0.5f, BedFloorTop, 0f);
        private GameFlow flow;
        private CargoTracker tracker;
        private TruckMover truck;
        private PlayerController player;
        private PlayerCargoInteractor interactor;
        private Transform bedAnchor;
        private TruckWheelAnimator wheelAnimator;
        private TruckTailgate tailgate;
        private PlayerTailgateInteractor tailgateInteractor;
        private StageContext stageContext;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);

            flow = Object.FindFirstObjectByType<GameFlow>();
            tracker = Object.FindFirstObjectByType<CargoTracker>();
            truck = Object.FindFirstObjectByType<TruckMover>();
            player = Object.FindFirstObjectByType<PlayerController>();
            interactor = Object.FindFirstObjectByType<PlayerCargoInteractor>();
            bedAnchor = GameObject.Find("BedAnchor").transform;
            wheelAnimator = truck.GetComponent<TruckWheelAnimator>();
            tailgate = truck.GetComponentInChildren<TruckTailgate>();
            tailgateInteractor = player.GetComponent<PlayerTailgateInteractor>();
            stageContext = Object.FindFirstObjectByType<StageContext>();

            Assert.NotNull(flow, "씬에 GameFlow 가 없다");
            Assert.NotNull(tracker, "씬에 CargoTracker 가 없다");
            Assert.NotNull(truck, "씬에 TruckMover 가 없다");
            Assert.NotNull(player, "씬에 PlayerController 가 없다");
            Assert.NotNull(interactor, "씬에 PlayerCargoInteractor 가 없다");
            Assert.NotNull(bedAnchor, "씬에 BedAnchor 가 없다");
            Assert.NotNull(wheelAnimator, "Truck 루트에 바퀴 시각 애니메이터가 없다");
            Assert.NotNull(tailgate, "Truck에 테일게이트가 없다");
            Assert.NotNull(tailgateInteractor, "플레이어에 테일게이트 조작이 배선되지 않았다");
            Assert.NotNull(stageContext, "씬에 StageContext 가 없다");
            Assert.NotNull(stageContext.Definition, "StageContext에 스테이지 정의가 없다");

            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator 적재_단계에서는_트럭이_움직이지_않는다()
        {
            Vector3 start = truck.transform.position;

            for (int i = 0; i < 30; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(GameState.Loading, flow.State);
            Assert.That(Vector3.Distance(truck.transform.position, start), Is.LessThan(0.01f),
                "출발 전인데 트럭이 움직였다");
        }

        [UnityTest]
        public IEnumerator 씬은_스테이지_정의와_같은_수의_화물을_생성한다()
        {
            Assert.AreEqual("prototype", stageContext.Definition.StageId);
            Assert.AreEqual(SceneManager.GetActiveScene().name, stageContext.Definition.SceneName);
            Assert.AreEqual(stageContext.Definition.CargoCount, GetCargo().Length);
            yield break;
        }

        [UnityTest]
        public IEnumerator 트럭과_모든_화물에_프로토타입_소리가_배선되어_있다()
        {
            AudioSource backgroundMusic = GameObject.Find("BackgroundMusic")?.GetComponent<AudioSource>();
            Assert.NotNull(backgroundMusic, "씬에 배경음악이 없다");
            Assert.NotNull(backgroundMusic.clip, "배경음악 클립이 배선되지 않았다");
            Assert.IsTrue(backgroundMusic.loop, "배경음악이 반복 재생되지 않는다");
            Assert.NotNull(backgroundMusic.GetComponent<BackgroundMusic>(), "배경음악 재생기가 없다");

            TruckEngineAudio engineAudio = truck.GetComponent<TruckEngineAudio>();
            Assert.NotNull(engineAudio, "Truck에 엔진 오디오가 없다");
            Assert.IsTrue(engineAudio.HasClip, "엔진 루프 클립이 배선되지 않았다");

            foreach (Cargo item in GetCargo())
            {
                CargoImpactAudio impactAudio = item.GetComponent<CargoImpactAudio>();
                Assert.NotNull(impactAudio, $"{item.name}에 충돌 오디오가 없다");
                Assert.AreEqual(3, impactAudio.ClipCount, $"{item.name}의 충돌음 변형 수가 다르다");
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator 플레이어가_손_닿는_화물을_집고_다시_놓을_수_있다()
        {
            Cargo target = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID)[0];

            // 화물 바로 옆으로 순간이동시켜 집기 사거리 안에 둔다.
            player.SetWorldPose(
                target.transform.position + new Vector3(0f, 0f, -1f),
                Quaternion.identity,
                Vector3.zero);

            yield return new WaitForFixedUpdate();

            Assert.IsTrue(interactor.TryPickUp(target), "손 닿는 거리인데 화물을 집지 못했다");
            Assert.IsTrue(interactor.HasCargo, "집었는데 든 화물이 없다고 나온다");

            interactor.DropHeldCargo();

            Assert.IsFalse(interactor.HasCargo, "놓았는데 여전히 들고 있다고 나온다");
        }

        [UnityTest]
        public IEnumerator 운전석_실내_바닥과_캐빈에는_짐_미리보기나_배치가_허용되지_않고_짐칸에는_허용된다()
        {
            Cargo cargo = FindCargoWithVisual("CardboardBox");
            Rigidbody body = cargo.Body;
            Vector3 cargoPosition = truck.transform.TransformPoint(new Vector3(0f, 1.4f, -3f));
            body.position = cargoPosition;
            body.rotation = truck.transform.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            cargo.transform.SetPositionAndRotation(body.position, body.rotation);

            GameObject cameraObject = new GameObject("Cab Placement Test Camera");
            Camera placementCamera = cameraObject.AddComponent<Camera>();
            GameObject anchorObject = new GameObject("Cab Placement Test Anchor");
            anchorObject.transform.position = cargoPosition;
            player.SetWorldPose(cargoPosition + truck.transform.forward, truck.transform.rotation, Vector3.zero);

            GameObject cabInteriorVolume = GameObject.Find("CabInteriorPlacementForbiddenVolume");
            Assert.NotNull(cabInteriorVolume, "캐빈 실내 배치 금지 볼륨이 없다");
            BoxCollider cabInteriorCollider = cabInteriorVolume.GetComponent<BoxCollider>();
            Assert.NotNull(cabInteriorCollider, "캐빈 실내 배치 금지 볼륨에 Collider가 없다");
            Assert.IsTrue(cabInteriorCollider.isTrigger, "캐빈 실내 배치 금지 볼륨이 물리 차체가 됐다");

            // 실제 실패 경로: 보이는 캐빈 실내 바닥에서는 Cab 프록시가 아니라 Chassis를
            // 지지면으로 맞는다. 기존 테스트처럼 Cab 윗면만 겨냥하면 이 틈을 검증하지 못한다.
            AimPlacementCameraAtTruckLocal(
                placementCamera,
                new Vector3(CabInteriorForbiddenMinX + 0.2f, BedFloorTop, 0f));
            interactor.Configure(anchorObject.transform, placementCamera);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(
                Physics.Raycast(
                    placementCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)),
                    out RaycastHit cabInteriorSupport,
                    6f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore),
                "운전석 실내 배치 경로가 지지면을 맞추지 못했다");
            Assert.AreEqual("Chassis", cabInteriorSupport.collider.name,
                "실제 운전석 실내 배치 경로가 Cab 프록시가 아니라 Chassis를 지지면으로 맞지 않았다");

            Assert.IsTrue(interactor.TryPickUp(cargo), "캐빈 배치 검사용 화물을 집지 못했다");
            interactor.RefreshPlacementPreview();
            Assert.IsFalse(interactor.HasValidPlacement, "운전석 실내 바닥에 유효한 화물 미리보기가 생겼다");
            Assert.IsFalse(interactor.TryPlaceHeldCargo(), "운전석 실내 바닥에 화물이 배치됐다");
            Assert.IsTrue(interactor.HasCargo, "막힌 운전석 실내 배치 뒤 화물을 놓아 버렸다");

            AimPlacementCameraAtTruckLocal(placementCamera, new Vector3(BedCenterX, BedFloorTop, 0f));
            interactor.RefreshPlacementPreview();
            Assert.IsTrue(interactor.HasValidPlacement, "보이는 짐칸에 유효한 화물 미리보기가 생기지 않았다");
            Assert.IsTrue(interactor.TryPlaceHeldCargo(), "보이는 짐칸에 화물을 배치하지 못했다");

            Object.Destroy(cameraObject);
            Object.Destroy(anchorObject);
        }

        [UnityTest]
        public IEnumerator 모든_화물은_루트_큐브가_아닌_가져온_모델을_보인다()
        {
            Cargo[] cargo = GetCargo();

            foreach (Cargo item in cargo)
            {
                Assert.IsNull(item.GetComponent<Renderer>(), $"{item.name} 루트에 원시 도형 Renderer 가 남아 있다");

                Transform visual = null;
                foreach (Transform child in item.transform)
                {
                    if (child.name.StartsWith("ImportedVisual_"))
                    {
                        visual = child;
                        break;
                    }
                }

                Assert.NotNull(visual, $"{item.name} 에 가져온 모델 시각물이 없다");
                Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
                Assert.IsNotEmpty(renderers, $"{item.name} 가져온 모델에 Renderer 가 없다");

                foreach (Renderer renderer in renderers)
                {
                    Assert.NotNull(renderer.sharedMaterial, $"{item.name} 모델에 재질이 할당되지 않았다");
                    Assert.That(renderer.sharedMaterial.name, Does.EndWith("Material"),
                        $"{item.name} 모델이 cargo art 재질을 사용하지 않는다");
                }
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator 초기_화물은_Zup_원본을_Yup으로_바로_세운다()
        {
            foreach (Cargo item in GetCargo())
            {
                Transform visual = FindImportedVisual(item);
                Assert.NotNull(visual, $"{item.name} 에 가져온 모델 시각물이 없다");
                Assert.That(Vector3.Dot(visual.forward, Vector3.up), Is.GreaterThan(0.99f),
                    $"{item.name} 의 Z-up 원본이 Unity에서 거꾸로 서 있다");
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator 최종_블루트럭은_단일_게임플레이_루트와_물리만_사용한다()
        {
            Transform visual = truck.transform.Find("BlueTruckVisual");
            Assert.NotNull(visual, "최종 BlueTruck 시각물이 없다");
            Assert.AreSame(truck.transform, visual.parent, "BlueTruck 시각물이 단일 Truck 루트 밖에 있다");
            Assert.IsNotEmpty(visual.GetComponentsInChildren<Renderer>(true), "BlueTruck에 Renderer가 없다");
            Assert.IsEmpty(visual.GetComponentsInChildren<Collider>(true),
                "BlueTruck 시각물에 공유 물리와 겹치는 Collider가 남아 있다");
            Assert.IsEmpty(visual.GetComponentsInChildren<Rigidbody>(true),
                "BlueTruck 시각물에 공유 물리와 겹치는 Rigidbody가 남아 있다");
            Assert.IsEmpty(visual.GetComponentsInChildren<MonoBehaviour>(true),
                "BlueTruck 시각물에 외부 차량 제어 스크립트가 남아 있다");
            var meshFilters = new List<MeshFilter>(
                visual.GetComponentsInChildren<MeshFilter>(true));
            meshFilters.Add(
                tailgate.transform.Find("BlueTruckTailgate").GetComponent<MeshFilter>());
            Assert.AreEqual(6, meshFilters.Count, "차체, 테일게이트와 실제 바퀴 네 개 이외의 메시가 남아 있다");
            Assert.AreEqual(4, wheelAnimator.WheelCount, "실제 바퀴 리그가 정확히 네 개가 아니다");

            int totalTriangles = 0;
            foreach (MeshFilter meshFilter in meshFilters)
            {
                totalTriangles += meshFilter.sharedMesh.triangles.Length / 3;
                Assert.AreEqual(1, meshFilter.sharedMesh.subMeshCount,
                    $"{meshFilter.name}의 원본 submesh 구성이 보존되지 않았다");
                Assert.AreEqual(
                    meshFilter.sharedMesh.vertexCount,
                    meshFilter.sharedMesh.normals.Length,
                    $"{meshFilter.name}의 원본 normal이 보존되지 않았다");
                Assert.AreEqual(
                    meshFilter.sharedMesh.vertexCount,
                    meshFilter.sharedMesh.uv.Length,
                    $"{meshFilter.name}의 원본 UV가 보존되지 않았다");
            }

            Assert.AreEqual(BlueTruckV2TriangleCount, totalTriangles,
                "원본 BlueTruck 삼각형이 누락되거나 중복 배정됐다");

            Assert.AreEqual(1, Object.FindObjectsByType<TruckMover>(FindObjectsSortMode.None).Length,
                "씬에 게임플레이 TruckMover가 여러 개다");
            Assert.AreEqual(1, truck.GetComponents<Rigidbody>().Length,
                "Truck 루트의 Rigidbody가 하나가 아니다");
            Assert.AreSame(truck.transform, bedAnchor.parent, "BedAnchor가 단일 Truck 루트에서 분리됐다");
            Assert.IsNull(truck.transform.Find("CartoonTruckVisual"), "이전 카툰 후보가 씬에 남아 있다");
            Assert.IsNull(truck.transform.Find("LowPolyPickupVisual"), "이전 로우폴리 후보가 씬에 남아 있다");
            Assert.IsNull(truck.transform.Find("FreePickupVisual"), "이전 무료 픽업 후보가 씬에 남아 있다");

            string[] physicsParts =
            {
                "GroundSupport", "Chassis", "Cab", "BedFloor", "BedWall_Left",
                "BedWall_Right", "BedWall_Front",
            };
            foreach (string partName in physicsParts)
            {
                Transform part = truck.transform.Find(partName);
                Assert.NotNull(part, $"공유 물리 파트가 없다: {partName}");
                Assert.NotNull(part.GetComponent<BoxCollider>(), $"공유 BoxCollider가 없다: {partName}");
                Assert.IsNull(part.GetComponent<Renderer>(), $"보이지 않는 물리 프록시가 렌더링된다: {partName}");
            }

            Transform rearWall = tailgate.transform.Find("BedWall_Rear");
            Assert.NotNull(rearWall, "테일게이트 아래에 후면 충돌벽이 없다");
            Assert.NotNull(rearWall.GetComponent<BoxCollider>(), "후면 충돌벽에 BoxCollider가 없다");
            Assert.IsNull(rearWall.GetComponent<Renderer>(), "후면 충돌 프록시가 렌더링된다");
            yield break;
        }

        [UnityTest]
        public IEnumerator 블루트럭_계측값과_공유_짐칸_물리가_일치한다()
        {
            AssertVector3(
                bedAnchor.localPosition,
                new Vector3(BedCenterX, BedFloorTop, 0f),
                "BedAnchor");
            AssertBedPartMatches(
                "BedFloor",
                new Vector3(BedCenterX, BedFloorTop - BedFloorThickness * 0.5f, 0f),
                new Vector3(BedInsideLength + BedWallThickness * 2f, BedFloorThickness, BedInsideWidth));
            AssertBedPartMatches(
                "BedWall_Left",
                new Vector3(BedCenterX, BedFloorTop + BedWallHeight * 0.5f, BedMinZ - BedWallThickness * 0.5f),
                new Vector3(BedInsideLength + BedWallThickness * 2f, BedWallHeight, BedWallThickness));
            AssertBedPartMatches(
                "BedWall_Right",
                new Vector3(BedCenterX, BedFloorTop + BedWallHeight * 0.5f, BedMaxZ + BedWallThickness * 0.5f),
                new Vector3(BedInsideLength + BedWallThickness * 2f, BedWallHeight, BedWallThickness));
            AssertVector3(tailgate.transform.localPosition, TailgatePivotPosition, "TailgatePivot");
            AssertVector3(tailgate.transform.localScale, Vector3.one, "TailgatePivot 크기");
            AssertBedPartMatches(
                "TailgatePivot/BedWall_Rear",
                new Vector3(0f, BedWallHeight * 0.5f, 0f),
                new Vector3(BedWallThickness, BedWallHeight, BedInsideWidth));
            AssertBedPartMatches(
                "BedWall_Front",
                new Vector3(BedFrontBarrierX, BedFloorTop + BedFrontBarrierHeight * 0.5f, 0f),
                new Vector3(BedWallThickness, BedFrontBarrierHeight, BedFrontBarrierWidth));

            Transform visual = truck.transform.Find("BlueTruckVisual");
            AssertVector3(visual.localPosition, Vector3.zero, "BlueTruck 파생 시각물 위치");
            AssertVector3(visual.localScale, Vector3.one, "BlueTruck 파생 시각물 크기");

            Bounds rendererBounds = GetRendererBounds(visual);
            Bounds localRendererBounds = GetTruckLocalBounds(rendererBounds);
            Assert.That(localRendererBounds.min.y, Is.EqualTo(-0.75f).Within(0.03f),
                "BlueTruck 바퀴가 도로 높이에 닿지 않는다");
            Assert.That(localRendererBounds.max.x, Is.GreaterThan(3f), "BlueTruck 앞부분이 +X를 향하지 않는다");
            Assert.That(localRendererBounds.min.x, Is.LessThan(-3f), "BlueTruck 전체 길이가 계측값과 다르다");
            yield break;
        }

        [UnityTest]
        public IEnumerator 테일게이트는_빈손으로_열고_닫을_수_있다()
        {
            Assert.IsTrue(tailgate.IsClosed, "씬 시작 시 테일게이트가 닫혀 있지 않다");
            Assert.IsFalse(tailgate.IsLockedForDriving, "적재 단계인데 테일게이트가 잠겨 있다");

            player.SetWorldPose(
                tailgate.transform.position - truck.transform.right,
                truck.transform.rotation,
                Vector3.zero);
            Assert.IsTrue(tailgateInteractor.TryToggle(tailgate), "빈손으로 테일게이트를 열지 못했다");

            yield return WaitForTailgate(open: true);

            Assert.IsTrue(tailgate.IsOpen, "테일게이트 열림 애니메이션이 끝나지 않았다");
            Assert.That(
                Mathf.DeltaAngle(tailgate.transform.localEulerAngles.z, 90f),
                Is.EqualTo(0f).Within(0.5f),
                "테일게이트가 바깥쪽 수평까지 열리지 않았다");

            BoxCollider rearWall = tailgate.transform.Find("BedWall_Rear").GetComponent<BoxCollider>();
            Assert.That(rearWall.bounds.size.x, Is.GreaterThan(0.6f),
                "열린 뒷문의 충돌체가 시각물과 함께 눕지 않았다");
            Assert.That(rearWall.bounds.size.y, Is.LessThan(0.2f),
                "열린 뒷문의 충돌체가 여전히 세워져 있다");

            Assert.IsTrue(tailgateInteractor.TryToggle(tailgate), "열린 테일게이트를 다시 닫지 못했다");
            yield return WaitForTailgate(open: false);
            Assert.IsTrue(tailgate.IsClosed, "테일게이트 닫힘 애니메이션이 끝나지 않았다");
        }

        [UnityTest]
        public IEnumerator 출발하면_열린_테일게이트를_닫은_뒤_주행한다()
        {
            tailgate.SetOpenInstantly(true);
            Assert.IsTrue(tailgate.IsOpen, "출발 전 테일게이트 열림 준비에 실패했다");

            flow.StartDriving();

            Assert.AreEqual(GameState.Loading, flow.State,
                "테일게이트가 열린 채로 주행을 시작했다");
            yield return WaitForTailgate(open: false);
            yield return null;

            Assert.AreEqual(GameState.Driving, flow.State,
                "테일게이트를 닫은 뒤 주행 상태로 넘어가지 않았다");
            Assert.IsTrue(tailgate.IsClosed, "주행 시작 시 테일게이트가 닫혀 있지 않다");
            Assert.IsTrue(tailgate.IsLockedForDriving, "주행 중 테일게이트가 다시 열릴 수 있다");
        }

        [UnityTest]
        public IEnumerator 블루트럭_원본_바퀴는_차체에서_빠져_네_리그에_한번씩만_배정된다()
        {
            Transform visual = truck.transform.Find("BlueTruckVisual");
            MeshFilter bodyFilter = visual.Find("BlueTruckBody").GetComponent<MeshFilter>();
            Mesh bodyMesh = bodyFilter.sharedMesh;
            Mesh tailgateMesh =
                tailgate.transform.Find("BlueTruckTailgate").GetComponent<MeshFilter>().sharedMesh;
            Assert.That(bodyMesh.triangles.Length / 3, Is.GreaterThan(400000),
                "차체 메시가 원본 삼각형 대부분을 보존하지 못했다");
            Assert.That(tailgateMesh.triangles.Length / 3, Is.GreaterThan(0),
                "원본 차체에서 테일게이트 메시를 분리하지 못했다");
            Assert.That(tailgateMesh.bounds.size.x, Is.LessThan(0.3f),
                "테일게이트에 후면 판넬보다 깊은 차체 조각이 포함됐다");
            Assert.That(tailgateMesh.bounds.size.y, Is.InRange(0.5f, 0.9f),
                "테일게이트 높이가 계측 영역과 다르다");
            Assert.That(tailgateMesh.bounds.size.z, Is.GreaterThan(1.8f),
                "테일게이트 폭 대부분이 메시로 분리되지 않았다");

            var wheelPivots = new Vector3[wheelAnimator.WheelCount];
            for (int index = 0; index < wheelPivots.Length; index++)
            {
                wheelPivots[index] = wheelAnimator.GetRestLocalPosition(index);
            }

            Vector3[] bodyVertices = bodyMesh.vertices;
            int[] bodyTriangles = bodyMesh.triangles;
            for (int triangle = 0; triangle < bodyTriangles.Length; triangle += 3)
            {
                Assert.IsFalse(
                    IsInsideWheelEnvelope(bodyVertices[bodyTriangles[triangle]], wheelPivots)
                    && IsInsideWheelEnvelope(bodyVertices[bodyTriangles[triangle + 1]], wheelPivots)
                    && IsInsideWheelEnvelope(bodyVertices[bodyTriangles[triangle + 2]], wheelPivots),
                    $"차체에 정지한 바퀴 삼각형이 남았다: triangle {triangle / 3}");
            }

            int wheelTriangles = 0;
            for (int index = 0; index < wheelAnimator.WheelCount; index++)
            {
                Transform suspension = wheelAnimator.GetSuspensionRoot(index);
                Transform spin = wheelAnimator.GetSpinRoot(index);
                Assert.AreSame(suspension, spin.parent, $"{index}번 회전 루트가 서스펜션 아래에 없다");
                AssertVector3(suspension.localPosition, wheelPivots[index], $"{index}번 바퀴 축");

                MeshFilter[] wheelFilters = spin.GetComponentsInChildren<MeshFilter>(true);
                Assert.AreEqual(1, wheelFilters.Length, $"{index}번 실제 바퀴 메시가 하나가 아니다");
                Mesh wheelMesh = wheelFilters[0].sharedMesh;
                int triangles = wheelMesh.triangles.Length / 3;
                Assert.That(triangles, Is.GreaterThan(10000), $"{index}번 바퀴 메시가 비어 있다");
                Assert.That(wheelMesh.bounds.size.z, Is.LessThanOrEqualTo(0.40f),
                    $"{index}번 바퀴 메시가 축 바깥 차체까지 포함한다");
                Assert.That(wheelMesh.bounds.extents.x, Is.InRange(0.48f, 0.54f),
                    $"{index}번 바퀴 반지름이 계측값과 다르다");
                Assert.That(wheelMesh.bounds.extents.y, Is.InRange(0.48f, 0.54f),
                    $"{index}번 바퀴 반지름이 계측값과 다르다");
                Assert.That(Mathf.Abs(wheelMesh.bounds.center.x), Is.LessThan(WheelMeshTolerance),
                    $"{index}번 바퀴 회전축이 X 중심에서 벗어났다");
                Assert.That(Mathf.Abs(wheelMesh.bounds.center.y), Is.LessThan(WheelMeshTolerance),
                    $"{index}번 바퀴 회전축이 Y 중심에서 벗어났다");
                AssertWheelHasNoDistantFragments(wheelMesh, index);
                wheelTriangles += triangles;
            }

            Assert.AreEqual(
                BlueTruckV2TriangleCount,
                bodyMesh.triangles.Length / 3
                    + tailgateMesh.triangles.Length / 3
                    + wheelTriangles,
                "원본 삼각형이 차체/테일게이트/네 바퀴 사이에서 누락되거나 중복됐다");
            yield break;
        }

        [UnityTest]
        public IEnumerator 블루트럭_바퀴_회전축은_시각_중심을_공전시키지_않는다()
        {
            for (int index = 0; index < wheelAnimator.WheelCount; index++)
            {
                Transform spin = wheelAnimator.GetSpinRoot(index);
                MeshFilter wheelFilter = spin.GetComponentInChildren<MeshFilter>();
                Vector3 visualCenter = wheelFilter.sharedMesh.bounds.center;
                Quaternion originalRotation = spin.localRotation;
                Vector3 before = wheelFilter.transform.TransformPoint(visualCenter);
                spin.localRotation = Quaternion.AngleAxis(90f, Vector3.forward);
                Vector3 after = wheelFilter.transform.TransformPoint(visualCenter);
                spin.localRotation = originalRotation;

                Assert.That(Vector3.Distance(before, after), Is.LessThan(WheelMeshTolerance),
                    $"{index}번 바퀴의 시각 중심이 축 주위를 공전한다");
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator 트럭의_실제_이동량만큼_원본_바퀴가_전진과_후진으로_회전한다()
        {
            Transform spin = wheelAnimator.GetSpinRoot(0);
            MeshFilter wheelFilter = spin.GetComponentInChildren<MeshFilter>();
            Vector3[] vertices = wheelFilter.sharedMesh.vertices;
            int sampleIndex = 0;
            float farthest = 0f;
            for (int index = 0; index < vertices.Length; index++)
            {
                float radial = new Vector2(vertices[index].x, vertices[index].y).sqrMagnitude;
                if (radial > farthest)
                {
                    farthest = radial;
                    sampleIndex = index;
                }
            }

            Quaternion initialSpin = spin.localRotation;
            Vector3 initialVertexInTruck = truck.transform.InverseTransformPoint(
                wheelFilter.transform.TransformPoint(vertices[sampleIndex]));
            Vector3 startPosition = truck.transform.position;
            Vector3 displacement = truck.transform.right * 0.50f;

            truck.transform.position = startPosition + displacement;
            Physics.SyncTransforms();
            yield return null;

            float expectedDegrees = displacement.magnitude / wheelAnimator.WheelRadius * Mathf.Rad2Deg;
            float forwardDegrees = Vector3.SignedAngle(
                initialSpin * Vector3.up,
                spin.localRotation * Vector3.up,
                Vector3.forward);
            Vector3 forwardVertexInTruck = truck.transform.InverseTransformPoint(
                wheelFilter.transform.TransformPoint(vertices[sampleIndex]));
            Assert.That(forwardDegrees, Is.EqualTo(-expectedDegrees).Within(2f),
                "전진한 실제 거리와 바퀴 회전량이 맞지 않는다");
            Assert.That(Vector3.Distance(initialVertexInTruck, forwardVertexInTruck), Is.GreaterThan(0.25f),
                "회전 루트가 아니라 정지한 복제 바퀴를 보고 있다");

            truck.transform.position = startPosition;
            Physics.SyncTransforms();
            yield return null;

            Vector3 restoredVertexInTruck = truck.transform.InverseTransformPoint(
                wheelFilter.transform.TransformPoint(vertices[sampleIndex]));
            Assert.That(Quaternion.Angle(initialSpin, spin.localRotation), Is.LessThan(2f),
                "후진 거리만큼 바퀴 회전이 되감기지 않았다");
            Assert.That(Vector3.Distance(initialVertexInTruck, restoredVertexInTruck), Is.LessThan(0.03f),
                "후진 뒤 실제 바퀴의 정점이 원래 위치로 돌아오지 않았다");
        }

        [UnityTest]
        public IEnumerator 한쪽_바퀴만_단차를_밟으면_서스펜션이_제한내에서_압축되고_부드럽게_복원된다()
        {
            yield return new WaitForSeconds(0.30f);

            Transform steppedWheel = wheelAnimator.GetSuspensionRoot(0);
            Transform oppositeWheel = wheelAnimator.GetSuspensionRoot(1);
            Vector3 steppedRest = wheelAnimator.GetRestLocalPosition(0);
            Vector3 oppositeRest = wheelAnimator.GetRestLocalPosition(1);
            Assert.That(Mathf.Abs(steppedWheel.localPosition.y - steppedRest.y), Is.LessThan(0.015f),
                "평지에서 바퀴가 기본 축 위치에 있지 않다");
            Assert.That(Mathf.Abs(oppositeWheel.localPosition.y - oppositeRest.y), Is.LessThan(0.015f),
                "평지에서 반대편 바퀴가 기본 축 위치에 있지 않다");

            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = "Wheel Suspension Test Step";
            step.layer = LayerMask.NameToLayer("Ground");
            step.transform.SetPositionAndRotation(
                truck.transform.TransformPoint(steppedRest - Vector3.up * wheelAnimator.WheelRadius)
                    + truck.transform.up * 0.18f,
                truck.transform.rotation);
            step.transform.localScale = new Vector3(0.72f, 0.36f, 0.72f);
            Physics.SyncTransforms();

            yield return new WaitForSeconds(0.35f);

            float compressedOffset = steppedWheel.localPosition.y - steppedRest.y;
            float oppositeOffset = oppositeWheel.localPosition.y - oppositeRest.y;
            Assert.That(compressedOffset, Is.GreaterThan(0.12f),
                "단차를 밟은 바퀴의 서스펜션이 충분히 압축되지 않았다");
            Assert.That(compressedOffset, Is.LessThanOrEqualTo(wheelAnimator.CompressionTravel + 0.002f),
                "서스펜션이 설정된 압축 한계를 넘었다");
            Assert.That(Mathf.Abs(oppositeOffset), Is.LessThan(0.025f),
                "단차를 밟지 않은 반대편 바퀴까지 같이 움직였다");

            Object.Destroy(step);
            yield return null;
            float firstRecoveryOffset = steppedWheel.localPosition.y - steppedRest.y;
            Assert.That(firstRecoveryOffset, Is.GreaterThan(0.03f),
                "단차가 사라진 한 프레임 만에 서스펜션이 순간이동했다");
            Assert.That(firstRecoveryOffset, Is.LessThanOrEqualTo(compressedOffset + 0.002f),
                "단차 제거 뒤 서스펜션이 반대 방향으로 더 압축됐다");

            yield return new WaitForSeconds(0.65f);

            Assert.That(Mathf.Abs(steppedWheel.localPosition.y - steppedRest.y), Is.LessThan(0.015f),
                "서스펜션이 평지의 기본 축 위치로 복원되지 않았다");
        }

        [UnityTest]
        public IEnumerator 블루트럭_짐칸은_상자_드럼통_흉상을_관통없이_지지한다()
        {
            Cargo[] representativeCargo =
            {
                FindCargoWithVisual("CardboardBox"),
                FindCargoWithVisual("BlueBarrel"),
                FindCargoWithVisual("MarbleBust"),
            };
            Vector2[] offsets =
            {
                new Vector2(-0.48f, -0.48f),
                new Vector2(-0.48f, 0.48f),
                new Vector2(0.48f, 0f),
            };

            for (int index = 0; index < representativeCargo.Length; index++)
            {
                PlaceCargoOnBed(representativeCargo[index], offsets[index]);
            }

            yield return Settle(60);

            float worldFloorTop = truck.transform.TransformPoint(
                new Vector3(BedCenterX, BedFloorTop, 0f)).y;
            foreach (Cargo cargo in representativeCargo)
            {
                BoxCollider proxy = cargo.GetComponent<BoxCollider>();
                Bounds rendererBounds = GetRendererBounds(FindImportedVisual(cargo));
                Bounds localProxyBounds = GetTruckLocalBounds(proxy.bounds);

                Assert.That(proxy.bounds.min.y, Is.GreaterThanOrEqualTo(worldFloorTop - 0.035f),
                    $"{cargo.name} 콜라이더가 보이는 바닥을 관통했다");
                Assert.That(rendererBounds.min.y, Is.GreaterThanOrEqualTo(worldFloorTop - 0.035f),
                    $"{cargo.name} 메시가 보이는 바닥을 관통했다");
                Assert.That(localProxyBounds.min.x, Is.GreaterThanOrEqualTo(BedMinX - 0.04f),
                    $"{cargo.name}이 짐칸 뒤 경계를 벗어났다");
                Assert.That(localProxyBounds.max.x, Is.LessThanOrEqualTo(BedMaxX + 0.04f),
                    $"{cargo.name}이 짐칸 앞 경계를 벗어났다");
                Assert.That(localProxyBounds.min.z, Is.GreaterThanOrEqualTo(BedMinZ - 0.04f),
                    $"{cargo.name}이 짐칸 왼쪽 경계를 벗어났다");
                Assert.That(localProxyBounds.max.z, Is.LessThanOrEqualTo(BedMaxZ + 0.04f),
                    $"{cargo.name}이 짐칸 오른쪽 경계를 벗어났다");
            }
        }

        [UnityTest]
        public IEnumerator 블루트럭_앞_격벽은_고속_화물이_캐빈으로_침범하지_못하게_한다()
        {
            Cargo impactCargo = FindCargoWithVisual("CardboardBox");
            foreach (Cargo cargo in GetCargo())
            {
                if (cargo != impactCargo)
                {
                    cargo.gameObject.SetActive(false);
                }
            }

            BoxCollider proxy = impactCargo.GetComponent<BoxCollider>();
            float startCenterX = FrontCargoLimit - proxy.size.x * 0.5f - 0.40f;
            Rigidbody body = impactCargo.Body;
            body.position = truck.transform.TransformPoint(new Vector3(
                startCenterX,
                BedFloorTop + proxy.size.y * 0.5f + 0.04f,
                0f));
            body.rotation = truck.transform.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            impactCargo.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();

            yield return Settle(10);

            body.linearVelocity = truck.transform.right * 10f;
            float furthestColliderX = float.NegativeInfinity;
            float furthestRendererX = float.NegativeInfinity;
            for (int fixedStep = 0; fixedStep < 60; fixedStep++)
            {
                yield return new WaitForFixedUpdate();
                furthestColliderX = Mathf.Max(furthestColliderX, GetTruckLocalBounds(proxy.bounds).max.x);
                furthestRendererX = Mathf.Max(
                    furthestRendererX,
                    GetTruckLocalBounds(GetRendererBounds(FindImportedVisual(impactCargo))).max.x);
            }

            float worldFloorTop = truck.transform.TransformPoint(new Vector3(BedCenterX, BedFloorTop, 0f)).y;
            Assert.That(furthestColliderX, Is.GreaterThan(FrontCargoLimit - 0.12f),
                "시험 화물이 앞 격벽까지 도달하지 않았다");
            Assert.That(furthestColliderX, Is.LessThanOrEqualTo(FrontCargoLimit + 0.04f),
                "화물 콜라이더가 앞 격벽을 뚫고 캐빈에 들어갔다");
            Assert.That(furthestRendererX, Is.LessThanOrEqualTo(FrontCargoLimit + 0.04f),
                "화물 메시가 앞 격벽을 뚫고 캐빈에 들어갔다");
            Assert.That(proxy.bounds.min.y, Is.GreaterThanOrEqualTo(worldFloorTop - 0.04f),
                "앞 격벽 충돌 뒤 화물이 바닥 아래로 빠졌다");
        }

        [UnityTest]
        public IEnumerator 블루트럭_측면과_후면_벽은_고속_화물의_탈출을_막는다()
        {
            Cargo impactCargo = FindCargoWithVisual("CardboardBox");
            foreach (Cargo cargo in GetCargo())
            {
                if (cargo != impactCargo)
                {
                    cargo.gameObject.SetActive(false);
                }
            }

            BoxCollider proxy = impactCargo.GetComponent<BoxCollider>();
            Rigidbody body = impactCargo.Body;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            body.position = truck.transform.TransformPoint(new Vector3(
                BedMinX + proxy.size.x * 0.5f + 0.40f,
                BedFloorTop + proxy.size.y * 0.5f + 0.04f,
                0f));
            body.rotation = truck.transform.rotation;
            body.linearVelocity = -truck.transform.right * 10f;
            body.angularVelocity = Vector3.zero;
            impactCargo.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();

            float furthestRearX = float.PositiveInfinity;
            for (int fixedStep = 0; fixedStep < 60; fixedStep++)
            {
                yield return new WaitForFixedUpdate();
                furthestRearX = Mathf.Min(furthestRearX, GetTruckLocalBounds(proxy.bounds).min.x);
            }

            Assert.That(furthestRearX, Is.LessThan(BedMinX + 0.12f),
                "시험 화물이 후면 벽까지 도달하지 않았다");
            Assert.That(furthestRearX, Is.GreaterThanOrEqualTo(BedMinX - 0.04f),
                "화물이 후면 벽을 뚫고 짐칸 밖으로 나갔다");

            body.position = truck.transform.TransformPoint(new Vector3(
                BedCenterX,
                BedFloorTop + proxy.size.y * 0.5f + 0.04f,
                BedMaxZ - proxy.size.z * 0.5f - 0.40f));
            body.rotation = truck.transform.rotation;
            body.linearVelocity = truck.transform.forward * 10f;
            body.angularVelocity = Vector3.zero;
            impactCargo.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();

            float furthestSideZ = float.NegativeInfinity;
            for (int fixedStep = 0; fixedStep < 60; fixedStep++)
            {
                yield return new WaitForFixedUpdate();
                furthestSideZ = Mathf.Max(furthestSideZ, GetTruckLocalBounds(proxy.bounds).max.z);
            }

            Assert.That(furthestSideZ, Is.GreaterThan(BedMaxZ - 0.12f),
                "시험 화물이 측면 벽까지 도달하지 않았다");
            Assert.That(furthestSideZ, Is.LessThanOrEqualTo(BedMaxZ + 0.04f),
                "화물이 측면 벽을 뚫고 짐칸 밖으로 나갔다");
        }

        [UnityTest]
        public IEnumerator 가져온_화물의_미리보기는_실제_메시_실루엣을_복제한다()
        {
            Vector3 testOrigin = new Vector3(1200f, 0f, 1200f);
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.transform.SetPositionAndRotation(testOrigin, Quaternion.identity);
            surface.transform.localScale = new Vector3(8f, 0.1f, 8f);

            GameObject cameraObject = new GameObject("Preview Shape Test Camera");
            Camera previewCamera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.SetPositionAndRotation(
                testOrigin + new Vector3(0f, 3f, -3f),
                Quaternion.LookRotation(new Vector3(0f, -3f, 3f).normalized, Vector3.up));

            GameObject anchorObject = new GameObject("Preview Shape Test Anchor");
            anchorObject.transform.position = testOrigin + new Vector3(0f, 1f, -1f);

            Cargo cargo = FindCargoWithVisual("FloorLamp");
            Rigidbody body = cargo.Body;
            body.position = testOrigin + new Vector3(0f, 1f, -1f);
            body.rotation = Quaternion.identity;
            cargo.transform.SetPositionAndRotation(body.position, body.rotation);
            player.SetWorldPose(testOrigin + new Vector3(-1.5f, 1f, -1f), Quaternion.identity, Vector3.zero);
            interactor.Configure(anchorObject.transform, previewCamera);

            yield return new WaitForFixedUpdate();

            Assert.IsTrue(interactor.TryPickUp(cargo), "가져온 화물을 집지 못했다");
            Assert.IsTrue(interactor.HasValidPlacement, "가져온 화물의 유효한 미리보기가 생성되지 않았다");

            Transform sourceVisual = FindImportedVisual(cargo);
            MeshFilter sourceMesh = sourceVisual.GetComponentInChildren<MeshFilter>(true);
            Assert.NotNull(sourceMesh, "원본 가져온 화물에 MeshFilter 가 없다");

            GameObject preview = GameObject.Find("CargoPlacementPreview");
            Assert.NotNull(preview, "화물 미리보기가 생성되지 않았다");
            Assert.IsNull(preview.GetComponent<Renderer>(), "미리보기 루트가 직육면체 Renderer 를 사용한다");
            Assert.IsEmpty(preview.GetComponentsInChildren<Collider>(true), "미리보기에 불필요한 충돌체가 남아 있다");

            MeshFilter[] previewMeshes = preview.GetComponentsInChildren<MeshFilter>(true);
            Assert.IsNotEmpty(previewMeshes, "미리보기에 실제 메시가 없다");
            bool containsSourceMesh = false;
            foreach (MeshFilter previewMesh in previewMeshes)
            {
                containsSourceMesh |= previewMesh.sharedMesh == sourceMesh.sharedMesh;
            }

            Assert.IsTrue(containsSourceMesh, "미리보기가 집은 화물의 실제 메시를 복제하지 않았다");

            interactor.DropHeldCargo();
            Object.Destroy(surface);
            Object.Destroy(cameraObject);
            Object.Destroy(anchorObject);
        }

        [UnityTest]
        public IEnumerator 미리보기는_Q를_누른_시간만큼_반시계로_돌고_그_방향으로_놓인다()
        {
            // 씬과 플레이어 콜라이더가 카메라 광선을 가로채지 않도록, 별도 공간에서 검증한다.
            Vector3 testOrigin = new Vector3(1000f, 0f, 1000f);
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.transform.SetPositionAndRotation(testOrigin, Quaternion.identity);
            surface.transform.localScale = new Vector3(8f, 0.1f, 8f);

            GameObject cameraObject = new GameObject("Preview Rotation Test Camera");
            Camera previewCamera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.SetPositionAndRotation(
                testOrigin + new Vector3(0f, 3f, -3f),
                Quaternion.LookRotation(new Vector3(0f, -3f, 3f).normalized, Vector3.up));

            GameObject anchorObject = new GameObject("Preview Rotation Test Anchor");
            anchorObject.transform.position = testOrigin + new Vector3(0f, 1f, -1f);

            GameObject cargoObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cargoObject.transform.position = testOrigin + new Vector3(0f, 1f, -1f);
            Rigidbody body = cargoObject.AddComponent<Rigidbody>();
            Cargo cargo = cargoObject.AddComponent<Cargo>();
            player.SetWorldPose(testOrigin + new Vector3(-1.5f, 1f, -1f), Quaternion.identity, Vector3.zero);
            interactor.Configure(anchorObject.transform, previewCamera);

            yield return null;

            Assert.IsTrue(interactor.TryPickUp(cargo), "사거리 안의 정사각 화물을 집지 못했다");
            Assert.IsTrue(interactor.HasValidPlacement, "유효한 바닥에서 미리보기가 생성되지 않았다");

            Quaternion initialPreviewRotation = interactor.PreviewRotation;
            interactor.RotatePlacementPreview(0.2f);

            float firstSignedYawDelta = Vector3.SignedAngle(
                initialPreviewRotation * Vector3.forward,
                interactor.PreviewRotation * Vector3.forward,
                Vector3.up);
            Assert.That(firstSignedYawDelta, Is.EqualTo(-18f).Within(0.01f),
                "Q를 0.2초 누른 미리보기가 초당 90도 반시계로 돌지 않았다");

            interactor.RotatePlacementPreview(0.3f);
            float totalSignedYawDelta = Vector3.SignedAngle(
                initialPreviewRotation * Vector3.forward,
                interactor.PreviewRotation * Vector3.forward,
                Vector3.up);
            Assert.That(totalSignedYawDelta, Is.EqualTo(-45f).Within(0.01f),
                "프레임 분할과 무관하게 Q를 누른 시간만큼 회전하지 않았다");

            Quaternion rotatedPreviewRotation = interactor.PreviewRotation;
            interactor.RefreshPlacementPreview();
            Assert.That(Quaternion.Angle(rotatedPreviewRotation, interactor.PreviewRotation), Is.LessThan(0.01f),
                "다음 미리보기 갱신에서 Q로 고른 방향이 사라졌다");

            Assert.IsTrue(interactor.TryPlaceHeldCargo(), "유효한 미리보기에 화물을 놓지 못했다");
            Assert.That(Quaternion.Angle(rotatedPreviewRotation, body.rotation), Is.LessThan(0.01f),
                "놓인 화물의 자세가 미리보기에서 고른 방향과 다르다");

            Object.Destroy(surface);
            Object.Destroy(cameraObject);
            Object.Destroy(anchorObject);
            Object.Destroy(cargoObject);
        }

        [UnityTest]
        public IEnumerator 출발하면_1인칭에서_디오라마_시점으로_바뀐다()
        {
            Camera firstPerson = GameObject.Find("First Person Camera").GetComponent<Camera>();
            Camera diorama = GameObject.Find("Diorama Camera").GetComponent<Camera>();
            AudioListener firstPersonListener = firstPerson.GetComponent<AudioListener>();
            AudioListener dioramaListener = diorama.GetComponent<AudioListener>();
            GameObject playerObject = player.gameObject;

            Assert.IsTrue(firstPerson.enabled, "적재 중인데 1인칭 카메라가 꺼져 있다");
            Assert.IsFalse(diorama.enabled, "적재 중인데 디오라마 카메라가 켜져 있다");
            Assert.IsTrue(firstPersonListener.enabled, "적재 중인데 1인칭 AudioListener가 꺼져 있다");
            Assert.IsFalse(dioramaListener.enabled, "적재 중인데 디오라마 AudioListener가 켜져 있다");

            flow.StartDriving();
            yield return null;

            Assert.IsFalse(firstPerson.enabled, "출발했는데 1인칭 카메라가 켜져 있다");
            Assert.IsTrue(diorama.enabled, "디오라마 시점으로 전환되지 않았다");
            Assert.IsFalse(firstPersonListener.enabled, "출발했는데 1인칭 AudioListener가 켜져 있다");
            Assert.IsTrue(dioramaListener.enabled, "디오라마 AudioListener로 전환되지 않았다");
            Assert.IsFalse(playerObject.activeSelf, "출발했는데 플레이어가 화면에 남아 있다");
        }

        [UnityTest]
        public IEnumerator 출발하면_도착_지점까지_주행하고_결과_상태가_된다()
        {
            Time.timeScale = 3f;
            flow.StartDriving();

            Assert.AreEqual(GameState.Driving, flow.State, "출발 신호를 줘도 주행 상태로 넘어가지 않았다");

            yield return WaitForResult();

            Assert.AreEqual(GameState.Result, flow.State, "제한 시간 안에 도착하지 못했다");
        }

        /// <summary>
        /// 굴곡은 급제동 다음가는 위협이다. 마루에서는 짐이 가벼워져 마찰이 풀리고
        /// 골짜기에서는 눌린다. 경로를 평지로 되돌려 놓는 퇴행은 다른 테스트에 걸리지 않는다.
        /// </summary>
        [UnityTest]
        public IEnumerator 트럭은_오르막과_내리막을_오르내린다()
        {
            float steepestClimb = 0f;
            float steepestDrop = 0f;
            float lowest = truck.transform.position.y;
            float highest = lowest;

            Time.timeScale = 3f;
            flow.StartDriving();

            float remaining = DriveTimeoutSeconds;
            while (flow.State != GameState.Result && remaining > 0f)
            {
                // 차체의 앞은 로컬 +X 다. 그 방향의 y 성분이 곧 지금 밟고 있는 경사다.
                float slope = Mathf.Asin(Mathf.Clamp(truck.transform.right.y, -1f, 1f)) * Mathf.Rad2Deg;
                steepestClimb = Mathf.Max(steepestClimb, slope);
                steepestDrop = Mathf.Min(steepestDrop, slope);
                lowest = Mathf.Min(lowest, truck.transform.position.y);
                highest = Mathf.Max(highest, truck.transform.position.y);

                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            Debug.Log($"[CargoStack] 고저차 {highest - lowest:0.0}m, " +
                $"최대 오르막 {steepestClimb:0.0}도, 최대 내리막 {steepestDrop:0.0}도");

            Assert.That(highest - lowest, Is.GreaterThan(4f), "경로가 거의 평평하다");
            Assert.That(steepestClimb, Is.GreaterThan(8f), "제대로 된 오르막이 없다");
            Assert.That(steepestDrop, Is.LessThan(-8f), "제대로 된 내리막이 없다");
        }

        /// <summary>
        /// 1인칭 카메라가 플레이어 몸통에 매달려 있어서, 몸통이 물리로 돌면 시점이 통째로 홱 돈다.
        /// 상자나 트럭에 부딪힐 때마다 화면이 돌아가는 문제가 실제로 있었다.
        /// </summary>
        [UnityTest]
        public IEnumerator 몸통에_충격이_와도_1인칭_시점이_돌아가지_않는다()
        {
            Transform view = GameObject.Find("First Person Camera").transform;
            Quaternion before = view.rotation;

            // 상자 모서리를 들이받은 것과 같은, 무게중심을 벗어난 충격.
            player.Body.AddTorque(Vector3.up * 400f, ForceMode.Impulse);

            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Assert.That(Quaternion.Angle(before, view.rotation), Is.LessThan(1f),
                "부딪힌 충격이 시점을 돌렸다. 플레이어 Rigidbody 의 회전이 잠겨 있어야 한다");
        }

        /// <summary>
        /// 사거리가 너무 짧으면 상자 하나 옮길 때마다 트럭을 빙 돌아야 한다. 그게 적재의 재미가 아니다.
        /// 최소한 트럭 옆에 선 채로 짐칸 한복판까지는 닿아야 한다.
        ///
        /// 건너편 구석까지는 현재 사거리 3m 로 닿지 않는다. 이건 의도한 선택이라,
        /// 얼마나 모자라는지 로그로 남겨 둔다. 사거리를 다시 만질 때 이 숫자를 보고 판단하면 된다.
        /// </summary>
        [UnityTest]
        public IEnumerator 트럭_옆에_선_채로_짐칸_한복판까지_손이_닿는다()
        {
            Cargo[] cargo = GetCargo();

            // 플레이어는 짐칸 한쪽(-z) 바퀴 바깥 땅에 선다.
            Vector3 standing = bedAnchor.TransformPoint(new Vector3(0f, -0.975f, -2.1f));
            player.SetWorldPose(standing, Quaternion.identity, Vector3.zero);

            MoveCargo(cargo[0], new Vector3(0f, 0.7f, 0f));       // 짐칸 한복판
            MoveCargo(cargo[1], new Vector3(0f, 0.7f, 0.85f));    // 건너편 구석
            yield return new WaitForFixedUpdate();

            float toMiddle = Vector3.Distance(player.transform.position, cargo[0].Body.worldCenterOfMass);
            float toFarCorner = Vector3.Distance(player.transform.position, cargo[1].Body.worldCenterOfMass);
            Debug.Log($"[CargoStack] 서 있는 자리에서 짐칸 한복판 {toMiddle:0.00}m, 건너편 구석 {toFarCorner:0.00}m");

            Assert.IsTrue(interactor.TryPickUp(cargo[0]),
                $"트럭 옆에 섰는데 짐칸 한복판 상자({toMiddle:0.00}m)에 손이 닿지 않는다");
        }

        [UnityTest]
        public IEnumerator 짐칸에_실은_상자는_마찰만으로_목적지까지_실려_간다()
        {
            PlaceCargoInSingleLayer();
            yield return Settle(150);

            Assert.AreEqual(
                stageContext.Definition.CargoCount,
                tracker.RemainingCount,
                "출발 전인데 짐이 이미 짐칸을 벗어났다");

            Time.timeScale = 3f;
            flow.StartDriving();
            yield return WaitForResult();

            // 난이도 튜닝의 기준값. 전부 남으면 너무 쉽고, 0개면 너무 어렵다.
            Debug.Log($"[CargoStack] 한 층으로 깔기: {tracker.RemainingCount}/{tracker.TotalCount} 생존");

            Assert.That(tracker.RemainingCount, Is.GreaterThan(0),
                "마찰만으로는 짐이 하나도 실려 가지 못했다. 마찰계수나 속도 프로필이 잘못됐다");
        }

        [UnityTest]
        public IEnumerator 높이_쌓아도_주행이_끝까지_진행된다()
        {
            PlaceCargoInTwoLayers();
            yield return Settle(180);

            Time.timeScale = 3f;
            flow.StartDriving();
            yield return WaitForResult();

            Debug.Log($"[CargoStack] 두 층으로 쌓기: {tracker.RemainingCount}/{tracker.TotalCount} 생존");

            Assert.AreEqual(GameState.Result, flow.State, "높이 쌓은 상태에서 주행이 끝나지 않았다");
        }

        [UnityTest]
        public IEnumerator 바닥에_둔_상자는_전부_낙하로_집계된다()
        {
            int dropped = 0;
            tracker.CargoDropped += _ => dropped++;

            Time.timeScale = 3f;
            flow.StartDriving();
            yield return WaitForResult();

            Assert.AreEqual(
                stageContext.Definition.CargoCount,
                dropped,
                "바닥에 놔둔 짐이 낙하로 집계되지 않았다");
            Assert.AreEqual(0, tracker.RemainingCount);
        }

        [UnityTest]
        public IEnumerator 너무_강한_충격을_받은_흉상은_부서져_손실로_집계된다()
        {
            Cargo bust = FindCargoWithVisual("MarbleBust");
            CargoBreakable breakable = bust.GetComponent<CargoBreakable>();
            Assert.NotNull(breakable, "흉상에 파손 컴포넌트가 배선되지 않았다");

            foreach (Cargo cargo in GetCargo())
            {
                if (cargo != bust)
                {
                    cargo.gameObject.SetActive(false);
                }
            }

            BoxCollider proxy = bust.GetComponent<BoxCollider>();
            Rigidbody body = bust.Body;
            body.position = truck.transform.TransformPoint(new Vector3(
                BedCenterX,
                BedFloorTop + proxy.size.y * 0.5f + 0.04f,
                BedMinZ + proxy.size.z * 0.5f + 0.40f));
            body.rotation = truck.transform.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            bust.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();

            yield return Settle(10);

            // 측면 벽을 향해 세게 던진다. 기본 파손 속도(8m/s)보다 확실히 위인 값이다.
            body.linearVelocity = truck.transform.forward * 12f;

            for (int fixedStep = 0; fixedStep < 60 && !bust.IsBroken; fixedStep++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.IsTrue(bust.IsBroken, "강한 충격을 받고도 흉상이 부서지지 않았다");
            Renderer visual = FindImportedVisual(bust).GetComponentInChildren<Renderer>();
            Assert.IsFalse(visual.enabled, "부서진 흉상의 모델이 여전히 보인다");

            Time.timeScale = 3f;
            flow.StartDriving();
            yield return WaitForResult();

            Assert.AreEqual(0, tracker.RemainingCount, "부서진 흉상이 별점 손실로 집계되지 않았다");
        }

        /// <summary>작아진 픽업 짐칸에 네 개를 바닥, 두 개를 위층으로 안정되게 쌓는다.</summary>
        private void PlaceCargoInSingleLayer()
        {
            Cargo[] cargo = GetCargo();
            Vector3[] offsets =
            {
                new Vector3(-0.50f, 0.58f, -0.50f),
                new Vector3(-0.50f, 0.58f, 0.50f),
                new Vector3(0.50f, 0.58f, -0.50f),
                new Vector3(0.50f, 0.58f, 0.50f),
                new Vector3(-0.45f, 1.52f, 0f),
                new Vector3(0.45f, 1.52f, 0f),
            };

            for (int i = 0; i < cargo.Length; i++)
            {
                MoveCargo(cargo[i], offsets[i]);
            }
        }

        /// <summary>같은 짐을 두 층으로 쌓는다. 무게중심이 높아지는 대조군이다.</summary>
        private void PlaceCargoInTwoLayers()
        {
            Cargo[] cargo = GetCargo();
            Vector3[] offsets =
            {
                new Vector3(-0.50f, 0.58f, -0.50f),
                new Vector3(-0.50f, 0.58f, 0.50f),
                new Vector3(0.50f, 0.58f, -0.50f),
                new Vector3(0.50f, 0.58f, 0.50f),
                new Vector3(-0.40f, 1.65f, 0f),
                new Vector3(0.40f, 1.65f, 0f),
            };

            for (int i = 0; i < cargo.Length; i++)
            {
                MoveCargo(cargo[i], offsets[i]);
            }
        }

        private Cargo[] GetCargo()
        {
            Cargo[] cargo = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID);
            Assert.AreEqual(
                stageContext.Definition.CargoCount,
                cargo.Length,
                $"짐 {stageContext.Definition.CargoCount}개를 기대했다");
            return cargo;
        }

        private static Transform FindImportedVisual(Cargo cargo)
        {
            foreach (Transform child in cargo.transform)
            {
                if (child.name.StartsWith("ImportedVisual_"))
                {
                    return child;
                }
            }

            return null;
        }

        private Cargo FindCargoWithVisual(string visualName)
        {
            foreach (Cargo cargo in GetCargo())
            {
                Transform visual = FindImportedVisual(cargo);
                if (visual != null && visual.name.Contains(visualName))
                {
                    return cargo;
                }
            }

            Assert.Fail($"{visualName} 가져온 화물을 찾지 못했다");
            return null;
        }

        private void AimPlacementCameraAtTruckLocal(Camera cameraToAim, Vector3 truckLocalTarget)
        {
            Vector3 target = truck.transform.TransformPoint(truckLocalTarget);
            cameraToAim.transform.SetPositionAndRotation(
                target + truck.transform.up * 3f,
                Quaternion.LookRotation(-truck.transform.up, truck.transform.forward));
        }

        private void MoveCargo(Cargo cargo, Vector3 bedLocalOffset)
        {
            Rigidbody body = cargo.Body;
            body.position = bedAnchor.TransformPoint(bedLocalOffset);
            body.rotation = bedAnchor.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            cargo.transform.SetPositionAndRotation(body.position, body.rotation);
        }

        private void PlaceCargoOnBed(Cargo cargo, Vector2 offset)
        {
            BoxCollider proxy = cargo.GetComponent<BoxCollider>();
            Rigidbody body = cargo.Body;
            Vector3 localPosition = new Vector3(
                BedCenterX + offset.x,
                BedFloorTop + proxy.size.y * 0.5f + 0.08f,
                offset.y);
            body.position = truck.transform.TransformPoint(localPosition);
            body.rotation = truck.transform.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            cargo.transform.SetPositionAndRotation(body.position, body.rotation);
        }

        private void AssertBedPartMatches(string partName, Vector3 expectedPosition, Vector3 expectedScale)
        {
            Transform part = truck.transform.Find(partName);
            Assert.NotNull(part, $"{partName}이 없다");
            AssertVector3(part.localPosition, expectedPosition, $"{partName} 위치");
            AssertVector3(part.localScale, expectedScale, $"{partName} 크기");
            Assert.NotNull(part.GetComponent<BoxCollider>(), $"{partName}의 공유 BoxCollider가 없다");
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), $"{label} X");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), $"{label} Y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f), $"{label} Z");
        }

        private static void AssertWheelHasNoDistantFragments(Mesh wheelMesh, int wheelIndex)
        {
            foreach (Vector3 vertex in wheelMesh.vertices)
            {
                Assert.That(Mathf.Abs(vertex.z), Is.LessThanOrEqualTo(
                        WheelRetentionHalfWidth + WheelMeshTolerance),
                    $"{wheelIndex}번 바퀴에 축 방향으로 멀리 떨어진 차체 조각이 포함됐다");
                Assert.That(new Vector2(vertex.x, vertex.y).magnitude, Is.LessThanOrEqualTo(
                        WheelRetentionRadius + WheelMeshTolerance),
                    $"{wheelIndex}번 바퀴에 회전 시 공전할 먼 차체 조각이 포함됐다");
            }
        }

        private static bool IsInsideWheelEnvelope(Vector3 point, Vector3[] pivots)
        {
            foreach (Vector3 pivot in pivots)
            {
                if (Mathf.Abs(point.z - pivot.z) > WheelRetentionHalfWidth)
                {
                    continue;
                }

                Vector2 radial = new Vector2(point.x - pivot.x, point.y - pivot.y);
                if (radial.sqrMagnitude <= WheelRetentionRadius * WheelRetentionRadius)
                {
                    return true;
                }
            }

            return false;
        }

        private Bounds GetTruckLocalBounds(Bounds worldBounds)
        {
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            Bounds localBounds = new Bounds(
                truck.transform.InverseTransformPoint(min),
                Vector3.zero);

            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        localBounds.Encapsulate(truck.transform.InverseTransformPoint(new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z)));
                    }
                }
            }

            return localBounds;
        }

        private static Bounds GetRendererBounds(Transform visual)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            Assert.IsNotEmpty(renderers, $"{visual.name}에 렌더러가 없다");

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private IEnumerator Settle(int fixedSteps)
        {
            for (int i = 0; i < fixedSteps; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private IEnumerator WaitForTailgate(bool open)
        {
            float remaining = 2f;
            while ((open ? !tailgate.IsOpen : !tailgate.IsClosed) && remaining > 0f)
            {
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.Greater(remaining, 0f,
                open ? "테일게이트 열림 시간 초과" : "테일게이트 닫힘 시간 초과");
        }

        private IEnumerator WaitForResult()
        {
            float remaining = DriveTimeoutSeconds;

            while (flow.State != GameState.Result && remaining > 0f)
            {
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}

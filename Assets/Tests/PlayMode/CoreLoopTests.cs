using System.Collections;
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
        private const int ExpectedCargoCount = 6;

        private GameFlow flow;
        private CargoTracker tracker;
        private TruckMover truck;
        private PlayerController player;
        private PlayerCargoInteractor interactor;
        private Transform bedAnchor;
        private TruckVisualSelector visualSelector;

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
            visualSelector = truck.GetComponent<TruckVisualSelector>();

            Assert.NotNull(flow, "씬에 GameFlow 가 없다");
            Assert.NotNull(tracker, "씬에 CargoTracker 가 없다");
            Assert.NotNull(truck, "씬에 TruckMover 가 없다");
            Assert.NotNull(player, "씬에 PlayerController 가 없다");
            Assert.NotNull(interactor, "씬에 PlayerCargoInteractor 가 없다");
            Assert.NotNull(bedAnchor, "씬에 BedAnchor 가 없다");
            Assert.NotNull(visualSelector, "Truck 루트에 TruckVisualSelector 가 없다");

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
        public IEnumerator 트럭_시각물_후보_셋은_카툰을_기본으로_하나만_보인다()
        {
            string[] expectedObjects =
            {
                "CartoonTruckVisual",
                "LowPolyPickupVisual",
                "FreePickupVisual",
            };
            string[] expectedNames =
            {
                "카툰 트럭",
                "로우폴리 픽업",
                "무료 픽업",
            };

            Assert.AreEqual(3, visualSelector.CandidateCount, "트럭 비교 후보가 세 개가 아니다");
            Assert.AreEqual(0, visualSelector.ActiveIndex, "기본 후보가 카툰 트럭이 아니다");
            Assert.AreEqual("카툰 트럭", visualSelector.ActiveCandidateName);

            for (int index = 0; index < visualSelector.CandidateCount; index++)
            {
                GameObject candidate = visualSelector.GetCandidate(index);
                Assert.NotNull(candidate, $"{index + 1}번 트럭 후보가 없다");
                Assert.AreEqual(expectedObjects[index], candidate.name);
                Assert.AreEqual(expectedNames[index], visualSelector.GetCandidateName(index));
                Assert.AreSame(truck.transform, candidate.transform.parent,
                    $"{candidate.name}이 단일 Truck 루트 밖에 있다");
                Assert.IsNotEmpty(candidate.GetComponentsInChildren<Renderer>(true),
                    $"{candidate.name}에 렌더러가 없다");
            }

            Assert.AreEqual(1, CountActiveCandidates(), "초기 상태에서 여러 트럭 후보가 동시에 보인다");
            yield break;
        }

        [UnityTest]
        public IEnumerator 숫자키와_화면_버튼은_같은_트럭_후보를_하나씩_고른다()
        {
            visualSelector.SelectFromButton(1);
            Assert.AreEqual(1, visualSelector.ActiveIndex, "두 번째 화면 버튼이 로우폴리 픽업을 고르지 않았다");
            Assert.AreEqual("로우폴리 픽업", visualSelector.ActiveCandidateName);
            Assert.AreEqual(1, CountActiveCandidates(), "화면 버튼 선택 뒤 여러 후보가 동시에 보인다");

            Assert.IsTrue(visualSelector.SelectFromShortcut(KeyCode.Alpha3), "3 키가 후보 선택 키로 연결되지 않았다");
            Assert.AreEqual(2, visualSelector.ActiveIndex, "3 키가 무료 픽업을 고르지 않았다");
            Assert.AreEqual("무료 픽업", visualSelector.ActiveCandidateName);
            Assert.AreEqual(1, CountActiveCandidates(), "숫자키 선택 뒤 여러 후보가 동시에 보인다");

            Assert.IsTrue(visualSelector.SelectFromShortcut(KeyCode.Keypad1), "키패드 1이 후보 선택 키로 연결되지 않았다");
            Assert.AreEqual(0, visualSelector.ActiveIndex, "키패드 1이 카툰 트럭을 고르지 않았다");
            Assert.IsFalse(visualSelector.SelectFromShortcut(KeyCode.Q), "무관한 키가 트럭 후보 선택으로 처리됐다");
            Assert.AreEqual(0, visualSelector.ActiveIndex, "무관한 키가 현재 후보를 바꿨다");
            Assert.AreEqual(1, CountActiveCandidates(), "후보 전환 뒤 활성 시각물이 하나가 아니다");
            yield break;
        }

        [UnityTest]
        public IEnumerator 후보_전환은_단일_트럭의_주행과_적재_물리를_바꾸지_않는다()
        {
            Assert.AreEqual(1, Object.FindObjectsByType<TruckMover>(FindObjectsSortMode.None).Length,
                "씬에 게임플레이 TruckMover가 여러 개다");
            Assert.AreEqual(1, truck.GetComponents<Rigidbody>().Length,
                "Truck 루트의 Rigidbody가 하나가 아니다");
            Assert.AreEqual(1, truck.GetComponents<TruckVisualSelector>().Length,
                "Truck 루트의 후보 선택기가 하나가 아니다");
            Assert.AreSame(truck.transform, bedAnchor.parent, "BedAnchor가 단일 Truck 루트에서 분리됐다");

            Assert.NotNull(truck.transform.Find("BedFloor").GetComponent<BoxCollider>(),
                "짐칸 바닥 Collider가 사라졌다");
            Assert.NotNull(truck.transform.Find("BedWall_Left").GetComponent<BoxCollider>(),
                "짐칸 왼쪽 벽 Collider가 사라졌다");
            Assert.NotNull(truck.transform.Find("BedWall_Right").GetComponent<BoxCollider>(),
                "짐칸 오른쪽 벽 Collider가 사라졌다");
            Assert.NotNull(truck.transform.Find("BedWall_Rear").GetComponent<BoxCollider>(),
                "짐칸 뒤쪽 벽 Collider가 사라졌다");
            Assert.NotNull(truck.transform.Find("BedWall_Front").GetComponent<BoxCollider>(),
                "짐칸 앞쪽 벽 Collider가 사라졌다");

            for (int index = 0; index < visualSelector.CandidateCount; index++)
            {
                GameObject candidate = visualSelector.GetCandidate(index);
                Assert.IsEmpty(candidate.GetComponentsInChildren<Collider>(true),
                    $"{candidate.name}에 게임플레이와 겹치는 Collider가 남아 있다");
                Assert.IsEmpty(candidate.GetComponentsInChildren<Rigidbody>(true),
                    $"{candidate.name}에 게임플레이와 겹치는 Rigidbody가 남아 있다");
                Assert.IsEmpty(candidate.GetComponentsInChildren<MonoBehaviour>(true),
                    $"{candidate.name}에 외부 차량 제어 스크립트가 남아 있다");
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator 후보마다_계측한_짐칸_프로필을_공유_콜라이더에_적용한다()
        {
            TruckBedProfile[] expectedProfiles =
            {
                new TruckBedProfile(
                    -2.50f, 0.01f, 0.390f, 4.20f, 2.30f, 0.26f,
                    -0.380f, 1.803f, 2.37f),
                new TruckBedProfile(
                    -2.16f, 0f, 0.460f, 3.34f, 2.30f, 0.47f,
                    -0.445f, 1.208f, 2.50f),
                new TruckBedProfile(
                    -1.625f, 0f, 0.680f, 2.15f, 2.10f, 0.44f,
                    -0.545f, 1.195f, 2.10f),
            };

            for (int index = 0; index < expectedProfiles.Length; index++)
            {
                visualSelector.Select(index);
                TruckBedProfile expected = expectedProfiles[index];
                TruckBedProfile actual = visualSelector.ActiveProfile;

                AssertProfileMatches(expected, actual, $"{index + 1}번 후보");
                AssertVector3(
                    bedAnchor.localPosition,
                    new Vector3(actual.CenterX, actual.FloorTop, actual.CenterZ),
                    $"{index + 1}번 후보 BedAnchor");
                AssertBedPartMatches(
                    "BedFloor",
                    new Vector3(
                        actual.CenterX,
                        actual.FloorTop - actual.FloorThickness * 0.5f,
                        actual.CenterZ),
                    new Vector3(
                        actual.InsideLength + actual.WallThickness * 2f,
                        actual.FloorThickness,
                        actual.InsideWidth));
                AssertBedPartMatches(
                    "BedWall_Left",
                    new Vector3(
                        actual.CenterX,
                        actual.FloorTop + actual.WallHeight * 0.5f,
                        actual.MinZ - actual.WallThickness * 0.5f),
                    new Vector3(
                        actual.InsideLength + actual.WallThickness * 2f,
                        actual.WallHeight,
                        actual.WallThickness));
                AssertBedPartMatches(
                    "BedWall_Right",
                    new Vector3(
                        actual.CenterX,
                        actual.FloorTop + actual.WallHeight * 0.5f,
                        actual.MaxZ + actual.WallThickness * 0.5f),
                    new Vector3(
                        actual.InsideLength + actual.WallThickness * 2f,
                        actual.WallHeight,
                        actual.WallThickness));
                AssertBedPartMatches(
                    "BedWall_Rear",
                    new Vector3(
                        actual.MinX - actual.WallThickness * 0.5f,
                        actual.FloorTop + actual.WallHeight * 0.5f,
                        actual.CenterZ),
                    new Vector3(
                        actual.WallThickness,
                        actual.WallHeight,
                        actual.InsideWidth));
                AssertBedPartMatches(
                    "BedWall_Front",
                    new Vector3(
                        actual.FrontBarrierX,
                        actual.FloorTop + actual.FrontBarrierHeight * 0.5f,
                        actual.CenterZ),
                    new Vector3(
                        actual.WallThickness,
                        actual.FrontBarrierHeight,
                        actual.FrontBarrierWidth));
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator 후보를_바꿔도_짐칸_위_화물만_BedAnchor_상대_위치를_보존한다()
        {
            Cargo loadedCargo = FindCargoWithVisual("CardboardBox");
            Cargo groundCargo = FindCargoWithVisual("FloorLamp");
            TruckBedProfile cartoonProfile = visualSelector.ActiveProfile;
            PlaceCargoOnActiveBed(loadedCargo, cartoonProfile, new Vector2(-0.4f, 0.25f));
            Physics.SyncTransforms();

            Vector3 relativeBefore = truck.transform.InverseTransformPoint(loadedCargo.Body.position) -
                                     bedAnchor.localPosition;
            Vector3 groundPositionBefore = groundCargo.Body.position;

            visualSelector.Select(2);
            yield return null;

            Vector3 relativeAfter = truck.transform.InverseTransformPoint(loadedCargo.Body.position) -
                                    bedAnchor.localPosition;
            AssertVector3(relativeAfter, relativeBefore, "짐칸 위 화물의 BedAnchor 상대 위치");
            Assert.That(Vector3.Distance(groundCargo.Body.position, groundPositionBefore), Is.LessThan(0.001f),
                "짐칸 밖 화물까지 후보 전환에 따라 이동했다");
        }

        [UnityTest]
        public IEnumerator 세_후보의_보이는_짐칸에_상자_드럼통_흉상이_관통하지_않고_지지된다()
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

            for (int index = 0; index < visualSelector.CandidateCount; index++)
            {
                visualSelector.Select(index);
                TruckBedProfile profile = visualSelector.ActiveProfile;

                for (int cargoIndex = 0; cargoIndex < representativeCargo.Length; cargoIndex++)
                {
                    PlaceCargoOnActiveBed(representativeCargo[cargoIndex], profile, offsets[cargoIndex]);
                }

                yield return Settle(60);

                float worldFloorTop = truck.transform.TransformPoint(
                    new Vector3(profile.CenterX, profile.FloorTop, profile.CenterZ)).y;
                foreach (Cargo cargo in representativeCargo)
                {
                    BoxCollider proxy = cargo.GetComponent<BoxCollider>();
                    Bounds rendererBounds = GetRendererBounds(FindImportedVisual(cargo));
                    Bounds localProxyBounds = GetTruckLocalBounds(proxy.bounds);

                    Assert.That(proxy.bounds.min.y, Is.GreaterThanOrEqualTo(worldFloorTop - 0.035f),
                        $"{visualSelector.ActiveCandidateName}에서 {cargo.name} 콜라이더가 보이는 바닥을 관통했다");
                    Assert.That(rendererBounds.min.y, Is.GreaterThanOrEqualTo(worldFloorTop - 0.035f),
                        $"{visualSelector.ActiveCandidateName}에서 {cargo.name} 메시가 보이는 바닥을 관통했다");
                    Assert.That(localProxyBounds.min.x, Is.GreaterThanOrEqualTo(profile.MinX - 0.04f),
                        $"{visualSelector.ActiveCandidateName}에서 {cargo.name}이 짐칸 뒤 경계를 벗어났다");
                    Assert.That(localProxyBounds.max.x, Is.LessThanOrEqualTo(profile.MaxX + 0.04f),
                        $"{visualSelector.ActiveCandidateName}에서 {cargo.name}이 짐칸 앞 경계를 벗어났다");
                    Assert.That(localProxyBounds.min.z, Is.GreaterThanOrEqualTo(profile.MinZ - 0.04f),
                        $"{visualSelector.ActiveCandidateName}에서 {cargo.name}이 짐칸 왼쪽 경계를 벗어났다");
                    Assert.That(localProxyBounds.max.z, Is.LessThanOrEqualTo(profile.MaxZ + 0.04f),
                        $"{visualSelector.ActiveCandidateName}에서 {cargo.name}이 짐칸 오른쪽 경계를 벗어났다");
                }
            }
        }

        [UnityTest]
        public IEnumerator 세_후보의_앞_격벽은_가속된_화물이_캐빈으로_침범하는_것을_막는다()
        {
            Cargo impactCargo = FindCargoWithVisual("CardboardBox");
            foreach (Cargo cargo in GetCargo())
            {
                if (cargo != impactCargo)
                {
                    cargo.gameObject.SetActive(false);
                }
            }

            for (int index = 0; index < visualSelector.CandidateCount; index++)
            {
                visualSelector.Select(index);
                TruckBedProfile profile = visualSelector.ActiveProfile;
                BoxCollider proxy = impactCargo.GetComponent<BoxCollider>();
                float startCenterX = profile.FrontCargoLimit - proxy.size.x * 0.5f - 0.40f;
                Rigidbody body = impactCargo.Body;
                body.position = truck.transform.TransformPoint(new Vector3(
                    startCenterX,
                    profile.FloorTop + proxy.size.y * 0.5f + 0.04f,
                    profile.CenterZ));
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
                    furthestColliderX = Mathf.Max(
                        furthestColliderX,
                        GetTruckLocalBounds(proxy.bounds).max.x);
                    furthestRendererX = Mathf.Max(
                        furthestRendererX,
                        GetTruckLocalBounds(GetRendererBounds(FindImportedVisual(impactCargo))).max.x);
                }

                float worldFloorTop = truck.transform.TransformPoint(
                    new Vector3(profile.CenterX, profile.FloorTop, profile.CenterZ)).y;
                Assert.That(furthestColliderX, Is.GreaterThan(profile.FrontCargoLimit - 0.12f),
                    $"{visualSelector.ActiveCandidateName}에서 시험 화물이 앞 격벽까지 도달하지 않았다");
                Assert.That(furthestColliderX, Is.LessThanOrEqualTo(profile.FrontCargoLimit + 0.04f),
                    $"{visualSelector.ActiveCandidateName}에서 화물 콜라이더가 앞 격벽을 뚫고 캐빈에 들어갔다");
                Assert.That(furthestRendererX, Is.LessThanOrEqualTo(profile.FrontCargoLimit + 0.04f),
                    $"{visualSelector.ActiveCandidateName}에서 화물 메시가 앞 격벽을 뚫고 캐빈에 들어갔다");
                Assert.That(proxy.bounds.min.y, Is.GreaterThanOrEqualTo(worldFloorTop - 0.04f),
                    $"{visualSelector.ActiveCandidateName}의 앞 격벽 충돌 뒤 화물이 바닥 아래로 빠졌다");
            }
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
            GameObject playerObject = player.gameObject;

            Assert.IsTrue(firstPerson.enabled, "적재 중인데 1인칭 카메라가 꺼져 있다");
            Assert.IsFalse(diorama.enabled, "적재 중인데 디오라마 카메라가 켜져 있다");

            flow.StartDriving();
            yield return null;

            Assert.IsFalse(firstPerson.enabled, "출발했는데 1인칭 카메라가 켜져 있다");
            Assert.IsTrue(diorama.enabled, "디오라마 시점으로 전환되지 않았다");
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

            Assert.AreEqual(ExpectedCargoCount, tracker.RemainingCount, "출발 전인데 짐이 이미 짐칸을 벗어났다");

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

            Assert.AreEqual(ExpectedCargoCount, dropped, "바닥에 놔둔 짐이 낙하로 집계되지 않았다");
            Assert.AreEqual(0, tracker.RemainingCount);
        }

        /// <summary>짐칸 바닥에 3x2 한 층으로 깐다. 무게중심이 낮은 안전한 배치다.</summary>
        private void PlaceCargoInSingleLayer()
        {
            Cargo[] cargo = GetCargo();

            for (int i = 0; i < cargo.Length; i++)
            {
                var offset = new Vector3(-1.05f + i % 3 * 1.05f, 0.7f, -0.5f + i / 3 * 1f);
                MoveCargo(cargo[i], offset);
            }
        }

        /// <summary>같은 짐을 두 층으로 쌓는다. 무게중심이 높아지는 대조군이다.</summary>
        private void PlaceCargoInTwoLayers()
        {
            Cargo[] cargo = GetCargo();

            for (int i = 0; i < cargo.Length; i++)
            {
                var offset = new Vector3(-1.05f + i % 3 * 1.05f, 0.7f + i / 3 * 1.2f, 0f);
                MoveCargo(cargo[i], offset);
            }
        }

        private Cargo[] GetCargo()
        {
            Cargo[] cargo = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID);
            Assert.AreEqual(ExpectedCargoCount, cargo.Length, $"짐 {ExpectedCargoCount}개를 기대했다");
            return cargo;
        }

        private int CountActiveCandidates()
        {
            int activeCount = 0;
            for (int index = 0; index < visualSelector.CandidateCount; index++)
            {
                activeCount += visualSelector.GetCandidate(index).activeSelf ? 1 : 0;
            }

            return activeCount;
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

        private void MoveCargo(Cargo cargo, Vector3 bedLocalOffset)
        {
            Rigidbody body = cargo.Body;
            body.position = bedAnchor.TransformPoint(bedLocalOffset);
            body.rotation = bedAnchor.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            cargo.transform.SetPositionAndRotation(body.position, body.rotation);
        }

        private void PlaceCargoOnActiveBed(Cargo cargo, TruckBedProfile profile, Vector2 offset)
        {
            BoxCollider proxy = cargo.GetComponent<BoxCollider>();
            Rigidbody body = cargo.Body;
            Vector3 localPosition = new Vector3(
                profile.CenterX + offset.x,
                profile.FloorTop + proxy.size.y * 0.5f + 0.08f,
                profile.CenterZ + offset.y);
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

        private static void AssertProfileMatches(
            TruckBedProfile expected,
            TruckBedProfile actual,
            string label)
        {
            Assert.That(actual.CenterX, Is.EqualTo(expected.CenterX).Within(0.001f), $"{label} 중심 X");
            Assert.That(actual.CenterZ, Is.EqualTo(expected.CenterZ).Within(0.001f), $"{label} 중심 Z");
            Assert.That(actual.FloorTop, Is.EqualTo(expected.FloorTop).Within(0.001f), $"{label} 바닥 높이");
            Assert.That(actual.InsideLength, Is.EqualTo(expected.InsideLength).Within(0.001f), $"{label} 안쪽 길이");
            Assert.That(actual.InsideWidth, Is.EqualTo(expected.InsideWidth).Within(0.001f), $"{label} 안쪽 너비");
            Assert.That(actual.WallHeight, Is.EqualTo(expected.WallHeight).Within(0.001f), $"{label} 벽 높이");
            Assert.That(
                actual.FrontBarrierX,
                Is.EqualTo(expected.FrontBarrierX).Within(0.001f),
                $"{label} 앞 격벽 X");
            Assert.That(
                actual.FrontBarrierHeight,
                Is.EqualTo(expected.FrontBarrierHeight).Within(0.001f),
                $"{label} 앞 격벽 높이");
            Assert.That(
                actual.FrontBarrierWidth,
                Is.EqualTo(expected.FrontBarrierWidth).Within(0.001f),
                $"{label} 앞 격벽 너비");
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), $"{label} X");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), $"{label} Y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f), $"{label} Z");
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

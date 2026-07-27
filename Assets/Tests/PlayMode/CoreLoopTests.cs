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

            Assert.NotNull(flow, "씬에 GameFlow 가 없다");
            Assert.NotNull(tracker, "씬에 CargoTracker 가 없다");
            Assert.NotNull(truck, "씬에 TruckMover 가 없다");
            Assert.NotNull(player, "씬에 PlayerController 가 없다");
            Assert.NotNull(interactor, "씬에 PlayerCargoInteractor 가 없다");
            Assert.NotNull(bedAnchor, "씬에 BedAnchor 가 없다");

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

        private void MoveCargo(Cargo cargo, Vector3 bedLocalOffset)
        {
            Rigidbody body = cargo.Body;
            body.position = bedAnchor.TransformPoint(bedLocalOffset);
            body.rotation = bedAnchor.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            cargo.transform.SetPositionAndRotation(body.position, body.rotation);
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

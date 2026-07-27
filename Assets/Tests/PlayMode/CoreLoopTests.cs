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
        /// 사거리가 짧으면 반대편에 놓으려고 트럭을 빙 돌아가야 한다. 그게 적재의 재미가 아니다.
        /// 트럭 한쪽에 선 채로 짐칸 건너편 구석까지는 닿아야 한다.
        /// </summary>
        [UnityTest]
        public IEnumerator 트럭_옆에_선_채로_짐칸_건너편까지_손이_닿는다()
        {
            Cargo target = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID)[0];

            // 짐칸 건너편(+z) 구석에 상자를 둔다.
            MoveCargo(target, new Vector3(0f, 0.7f, 0.85f));

            // 플레이어는 반대쪽(-z) 바퀴 바깥 땅에 선다.
            player.SetWorldPose(
                bedAnchor.TransformPoint(new Vector3(0f, -0.975f, -2.1f)),
                Quaternion.identity,
                Vector3.zero);

            yield return new WaitForFixedUpdate();

            float reach = Vector3.Distance(player.transform.position, target.Body.worldCenterOfMass);
            Debug.Log($"[CargoStack] 짐칸 건너편까지 거리: {reach:0.00}m");

            Assert.IsTrue(interactor.TryPickUp(target),
                $"트럭 옆에 섰는데 짐칸 건너편 상자({reach:0.00}m)에 손이 닿지 않는다");
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

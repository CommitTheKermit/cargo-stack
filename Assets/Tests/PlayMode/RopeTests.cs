using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    /// <summary>
    /// 로프가 고정 장비 구실을 하는지 본다.
    ///
    /// "로프를 걸었다"는 표시가 아니라 실제 사슬이 짐을 눌러야 한다는 것이 이 장비의 전제다.
    /// 그 전제가 깨지면 로프는 화면에만 있고 결과는 배치가 전부 정하게 되므로,
    /// 짐이 실제로 덜 뜨는지를 회귀 테스트로 고정한다.
    /// </summary>
    public class RopeTests
    {
        private const float BedCenterX = -1.835f;
        private const float BedFloorTop = 0.20f;
        private const float BedInsideWidth = 2.26f;
        private const float BedWallHeight = 0.70f;
        private const float BedInsideLength = 2.29f;
        private const float BedFrontBarrierX = -0.64f;
        private const float BedFrontBarrierHeight = 0.68f;
        private const float BedMinZ = -BedInsideWidth * 0.5f;
        private const float BedMaxZ = BedInsideWidth * 0.5f;
        private const float BedMinX = BedCenterX - BedInsideLength * 0.5f;

        private const float DriveTimeoutSeconds = 40f;

        private TruckMover truck;
        private Rigidbody truckBody;
        private PlayerController player;
        private PlayerRopeInteractor ropeInteractor;
        private StageContext stageContext;
        private GameFlow flow;
        private CargoTracker tracker;
        private Transform bedAnchor;
        private readonly RopeSettings settings = new RopeSettings();
        private float measuredRise;
        private float measuredSlide;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);

            truck = Object.FindFirstObjectByType<TruckMover>();
            truckBody = truck.GetComponent<Rigidbody>();
            player = Object.FindFirstObjectByType<PlayerController>();
            ropeInteractor = Object.FindFirstObjectByType<PlayerRopeInteractor>();
            stageContext = Object.FindFirstObjectByType<StageContext>();
            flow = Object.FindFirstObjectByType<GameFlow>();
            tracker = Object.FindFirstObjectByType<CargoTracker>();
            bedAnchor = GameObject.Find("BedAnchor").transform;

            Assert.NotNull(truck, "씬에 TruckMover 가 없다");
            Assert.NotNull(ropeInteractor, "플레이어에 로프 조작이 배선되지 않았다");
            Assert.NotNull(stageContext, "씬에 StageContext 가 없다");
            Assert.NotNull(flow, "씬에 GameFlow 가 없다");
            Assert.NotNull(tracker, "씬에 CargoTracker 가 없다");

            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator 스테이지가_정한_개수만큼_로프를_준다()
        {
            Assert.AreEqual(
                stageContext.Definition.RopeCount,
                ropeInteractor.RemainingRopes,
                "스테이지 정의와 플레이어가 들고 시작하는 로프 개수가 다르다");
            yield break;
        }

        [UnityTest]
        public IEnumerator 짐칸을_가로지르는_로프는_짐_윗면_위로_지난다()
        {
            Cargo cargo = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID)[0];
            BoxCollider proxy = cargo.GetComponent<BoxCollider>();
            PlaceOnBedCenter(cargo);
            yield return Settle(40);

            float cargoTop = proxy.bounds.max.y;
            List<Vector3> path = Rope.SolveWorldPath(
                WallTop(BedMinZ),
                WallTop(BedMaxZ),
                settings,
                null);

            Assert.That(path.Count, Is.GreaterThan(2),
                "짐을 사이에 두고도 로프가 직선으로 지났다");

            float highest = float.NegativeInfinity;
            foreach (Vector3 point in path)
            {
                highest = Mathf.Max(highest, point.y);
            }

            Assert.That(highest, Is.GreaterThanOrEqualTo(cargoTop - 0.02f),
                "로프가 짐을 관통해 지나간다");
        }

        [UnityTest]
        public IEnumerator 로프는_위로_뜨려는_짐을_눌러_앉힌다()
        {
            Cargo cargo = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID)[0];
            Rigidbody body = cargo.Body;

            PlaceOnBedCenter(cargo);
            yield return Settle(60);
            float restingHeight = body.worldCenterOfMass.y;

            // 대조군: 로프 없이 같은 충격을 준다. 마루에서 짐이 뜨는 상황을 흉내 낸 것이다.
            yield return LaunchAndMeasureRise(body, restingHeight);
            float freeRise = measuredRise;

            PlaceOnBedCenter(cargo);
            yield return Settle(60);

            Rope rope = Rope.Create(WallTop(BedMinZ), WallTop(BedMaxZ), settings, null);
            Assert.NotNull(rope, "짐칸을 가로지르는 로프를 걸지 못했다");
            yield return Settle(60);

            Assert.That(body.worldCenterOfMass.y, Is.EqualTo(restingHeight).Within(0.06f),
                "로프를 거는 것만으로 짐이 튀어 올랐다");

            yield return LaunchAndMeasureRise(body, restingHeight);
            float tiedRise = measuredRise;

            Debug.Log($"[CargoStack] 위로 튄 높이 - 로프 없음 {freeRise:0.000}m, 로프 있음 {tiedRise:0.000}m");

            Assert.That(freeRise, Is.GreaterThan(0.05f),
                "대조군이 뜨지도 않았다. 이 시험으로는 로프 효과를 잴 수 없다");
            Assert.That(tiedRise, Is.LessThan(freeRise * 0.5f),
                "로프를 걸어도 짐이 그대로 떴다. 사슬이 짐을 누르지 못하고 있다");

            rope.Remove();
        }

        [UnityTest]
        public IEnumerator 걷어_낸_로프는_다시_쓸_수_있다()
        {
            int available = ropeInteractor.RemainingRopes;
            Assert.That(available, Is.GreaterThan(0), "이 스테이지는 로프를 주지 않는다");

            Rope rope = Rope.Create(WallTop(BedMinZ), WallTop(BedMaxZ), settings, null);
            Assert.NotNull(rope, "로프를 걸지 못했다");
            Assert.That(rope.SegmentCount, Is.GreaterThan(1), "로프가 사슬로 만들어지지 않았다");

            GameObject holder = rope.gameObject;
            rope.Remove();
            yield return null;

            Assert.IsTrue(holder == null, "걷어 낸 로프가 씬에 남아 있다");
        }

        [UnityTest]
        public IEnumerator 너무_먼_두_점은_한_가닥으로_잇지_못한다()
        {
            RopeAttachment near = RopeAttachment.At(truckBody, truck.transform.TransformPoint(Vector3.zero));
            RopeAttachment far = RopeAttachment.At(
                null,
                truck.transform.TransformPoint(new Vector3(0f, 0f, settings.MaximumLength + 5f)));

            Assert.IsEmpty(
                Rope.SolveWorldPath(near, far, settings, null),
                "한 가닥 길이를 넘는 두 점이 이어졌다");
            yield break;
        }

        /// <summary>
        /// 급제동에 짐이 앞으로 쏟아지는 것을 <b>진행 방향으로 건</b> 로프가 막는지 본다.
        ///
        /// 가로로 건 로프와 세로로 건 로프의 구실이 다르다. 이것을 혼동해 처음에는
        /// 가로 로프가 앞뒤 미끄러짐도 막을 것으로 기대했지만, 가로 로프는 짐 윗면을 거의
        /// 수평으로 지나므로 장력의 수직 성분이 모서리 두 곳에만 걸린다. 앞뒤로는 마찰만
        /// 조금 보탤 뿐이다(실측 3% 감소).
        ///
        /// 앞뒤를 막는 것은 진행 방향 로프다. 짐이 앞으로 가려면 뒤쪽 구간이 늘어나야 하는데
        /// 양 끝이 차체에 묶여 있어 늘어날 수 없다. 마찰이 아니라 기하학이 막는 것이라
        /// 훨씬 강하게 걸린다.
        /// </summary>
        [UnityTest]
        public IEnumerator 진행_방향으로_건_로프는_앞으로_쏟아지는_짐을_막는다()
        {
            Cargo cargo = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID)[0];
            Rigidbody body = cargo.Body;

            PlaceOnBedCenter(cargo);
            yield return Settle(60);
            Vector3 restingPosition = body.position;

            // 대조군: 로프 없이 짐을 앞으로 밀어 본다.
            yield return PushForwardAndMeasure(body, restingPosition);
            float freeSlide = measuredSlide;

            PlaceOnBedCenter(cargo);
            yield return Settle(60);

            Rope rope = Rope.Create(RearWallTop(), FrontBarrierTop(), settings, null);
            Assert.NotNull(rope, "짐칸을 앞뒤로 가로지르는 로프를 걸지 못했다");
            yield return Settle(60);

            yield return PushForwardAndMeasure(body, restingPosition);
            float tiedSlide = measuredSlide;

            Debug.Log($"[CargoStack] 앞으로 밀린 거리 - 로프 없음 {freeSlide:0.000}m, " +
                $"진행 방향 로프 {tiedSlide:0.000}m");

            Assert.That(freeSlide, Is.GreaterThan(0.05f),
                "대조군이 밀리지도 않았다. 이 시험으로는 로프 효과를 잴 수 없다");
            Assert.That(tiedSlide, Is.LessThan(freeSlide * 0.7f),
                "진행 방향으로 걸었는데도 짐이 그대로 쏟아졌다");

            rope.Remove();
        }

        /// <summary>
        /// 로프를 걸어 둔 채 실제로 주행시킨다.
        ///
        /// 정지 상태에서만 재던 것이 이 장비의 사각지대였다. 트럭이 달리면 매듭은 차체를 따라
        /// 한 스텝에 수십 cm 를 움직이는데 사슬 마디는 관성으로 뒤처진다. 이 어긋남을
        /// 물리 법칙을 무시하고 되돌리면 마디가 순간이동하고, 그 속도 불연속이 짐을 날려 버린다.
        /// 그래서 결과가 아니라 <b>마디의 속도</b>를 직접 본다.
        /// </summary>
        [UnityTest]
        public IEnumerator 주행_중에도_로프_사슬이_튀지_않는다()
        {
            PlaceCargoInSingleLayer();
            yield return Settle(150);

            Rope rope = Rope.Create(WallTop(BedMinZ), WallTop(BedMaxZ), settings, null);
            Assert.NotNull(rope, "짐칸을 가로지르는 로프를 걸지 못했다");
            yield return Settle(60);

            Rigidbody[] segments = rope.GetComponentsInChildren<Rigidbody>();
            Assert.IsNotEmpty(segments, "로프에 사슬 마디가 없다");

            Time.timeScale = 3f;
            flow.StartDriving();

            float fastestSegment = 0f;
            float remaining = DriveTimeoutSeconds;
            while (flow.State != GameState.Result && remaining > 0f)
            {
                yield return new WaitForFixedUpdate();
                foreach (Rigidbody segment in segments)
                {
                    if (segment != null)
                    {
                        fastestSegment = Mathf.Max(fastestSegment, segment.linearVelocity.magnitude);
                    }
                }

                remaining -= Time.unscaledDeltaTime;
            }

            Debug.Log($"[CargoStack] 주행 중 로프 마디 최고 속도 {fastestSegment:0.0}m/s " +
                $"(트럭 최고 {stageContext.Definition.MaxSpeed:0.0}m/s), 짐 {tracker.RemainingCount}/{tracker.TotalCount} 생존");

            Assert.AreEqual(GameState.Result, flow.State, "로프를 건 채로 주행이 끝나지 않았다");
            Assert.That(fastestSegment, Is.LessThan(stageContext.Definition.MaxSpeed * 2.5f),
                "로프 사슬이 트럭보다 훨씬 빠르게 튀었다. 조인트가 마디를 순간이동시키고 있다");
        }

        /// <summary>
        /// 지터의 실제 피해는 여기서 드러난다. 고정 장비가 짐을 지키기는커녕 날려 버리면
        /// 로프는 없느니만 못하다. 한 층 배치는 로프 없이도 대부분 살아남는 구성이므로,
        /// 로프를 걸고 크게 밑돌면 로프가 가해자라는 뜻이다.
        /// </summary>
        [UnityTest]
        public IEnumerator 로프를_건_짐이_주행_뒤에도_짐칸에_남는다()
        {
            PlaceCargoInSingleLayer();
            yield return Settle(150);

            Rope rope = Rope.Create(WallTop(BedMinZ), WallTop(BedMaxZ), settings, null);
            Assert.NotNull(rope, "짐칸을 가로지르는 로프를 걸지 못했다");
            yield return Settle(60);

            Assert.AreEqual(tracker.TotalCount, tracker.RemainingCount,
                "출발 전인데 로프를 걸었다는 이유로 짐이 이미 짐칸을 벗어났다");

            Time.timeScale = 3f;
            flow.StartDriving();
            yield return WaitForResult();

            Debug.Log($"[CargoStack] 로프 건 한 층: {tracker.RemainingCount}/{tracker.TotalCount} 생존");

            Assert.That(tracker.RemainingCount, Is.GreaterThanOrEqualTo(3),
                "로프를 걸었더니 짐이 오히려 쏟아졌다");
        }

        /// <summary>짐칸 좌우 벽 윗면 한 점. 짐을 가로질러 누르는 로프를 묶는 자리다.</summary>
        private RopeAttachment WallTop(float localZ)
        {
            return RopeAttachment.At(
                truckBody,
                truck.transform.TransformPoint(new Vector3(BedCenterX, BedFloorTop + BedWallHeight, localZ)));
        }

        /// <summary>짐칸 뒷벽 윗면. 진행 방향으로 거는 로프의 뒤쪽 매듭이다.</summary>
        private RopeAttachment RearWallTop()
        {
            return RopeAttachment.At(
                truckBody,
                truck.transform.TransformPoint(new Vector3(BedMinX, BedFloorTop + BedWallHeight, 0f)));
        }

        /// <summary>앞 격벽 윗면. 진행 방향으로 거는 로프의 앞쪽 매듭이다.</summary>
        private RopeAttachment FrontBarrierTop()
        {
            return RopeAttachment.At(
                truckBody,
                truck.transform.TransformPoint(
                    new Vector3(BedFrontBarrierX, BedFloorTop + BedFrontBarrierHeight, 0f)));
        }

        private void PlaceOnBedCenter(Cargo cargo)
        {
            BoxCollider proxy = cargo.GetComponent<BoxCollider>();
            Rigidbody body = cargo.Body;
            body.position = truck.transform.TransformPoint(new Vector3(
                BedCenterX,
                BedFloorTop + proxy.size.y * 0.5f + 0.02f,
                0f));
            body.rotation = truck.transform.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            cargo.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 짐을 트럭 앞쪽으로 밀어 보고 얼마나 밀려났는지 <see cref="measuredSlide"/> 에 남긴다.
        /// 급제동으로 짐이 앞으로 쏟아지는 상황을 흉내 낸 것이다.
        /// </summary>
        private IEnumerator PushForwardAndMeasure(Rigidbody body, Vector3 restingPosition)
        {
            // 트럭의 앞은 로컬 +X 다. 마찰 한계를 넘길 만큼만 민다.
            body.linearVelocity = truck.transform.right * 2.5f;
            body.angularVelocity = Vector3.zero;

            float furthest = 0f;
            for (int step = 0; step < 60; step++)
            {
                yield return new WaitForFixedUpdate();

                // 앞으로 간 거리만 잰다. 위아래로 튄 것은 여기서 관심이 아니다.
                Vector3 travel = body.position - restingPosition;
                furthest = Mathf.Max(furthest, Vector3.Dot(travel, truck.transform.right));
            }

            measuredSlide = furthest;
        }

        /// <summary>짐을 위로 튀겨 보고 가장 높이 올라간 지점을 <see cref="measuredRise"/> 에 남긴다.</summary>
        private IEnumerator LaunchAndMeasureRise(Rigidbody body, float restingHeight)
        {
            body.linearVelocity = Vector3.up * 3f;
            body.angularVelocity = Vector3.zero;

            float highest = body.worldCenterOfMass.y;
            for (int step = 0; step < 40; step++)
            {
                yield return new WaitForFixedUpdate();
                highest = Mathf.Max(highest, body.worldCenterOfMass.y);
            }

            measuredRise = highest - restingHeight;
        }

        /// <summary>짐을 짐칸에 한 층으로 깐다. CoreLoopTests 의 기준 배치와 같은 자리다.</summary>
        private void PlaceCargoInSingleLayer()
        {
            Cargo[] cargo = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID);
            Vector3[] offsets =
            {
                new Vector3(-0.50f, 0.58f, -0.50f),
                new Vector3(-0.50f, 0.58f, 0.50f),
                new Vector3(0.50f, 0.58f, -0.50f),
                new Vector3(0.50f, 0.58f, 0.50f),
                new Vector3(-0.45f, 1.52f, 0f),
                new Vector3(0.45f, 1.52f, 0f),
            };

            for (int index = 0; index < cargo.Length && index < offsets.Length; index++)
            {
                Rigidbody body = cargo[index].Body;
                body.position = bedAnchor.TransformPoint(offsets[index]);
                body.rotation = bedAnchor.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                cargo[index].transform.SetPositionAndRotation(body.position, body.rotation);
            }

            Physics.SyncTransforms();
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

        private IEnumerator Settle(int fixedSteps)
        {
            for (int step = 0; step < fixedSteps; step++)
            {
                yield return new WaitForFixedUpdate();
            }
        }
    }
}

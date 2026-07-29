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
        private const float BedMinZ = -BedInsideWidth * 0.5f;
        private const float BedMaxZ = BedInsideWidth * 0.5f;

        private TruckMover truck;
        private Rigidbody truckBody;
        private PlayerController player;
        private PlayerRopeInteractor ropeInteractor;
        private StageContext stageContext;
        private readonly RopeSettings settings = new RopeSettings();
        private float measuredRise;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);

            truck = Object.FindFirstObjectByType<TruckMover>();
            truckBody = truck.GetComponent<Rigidbody>();
            player = Object.FindFirstObjectByType<PlayerController>();
            ropeInteractor = Object.FindFirstObjectByType<PlayerRopeInteractor>();
            stageContext = Object.FindFirstObjectByType<StageContext>();

            Assert.NotNull(truck, "씬에 TruckMover 가 없다");
            Assert.NotNull(ropeInteractor, "플레이어에 로프 조작이 배선되지 않았다");
            Assert.NotNull(stageContext, "씬에 StageContext 가 없다");

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

        /// <summary>짐칸 벽 윗면 한 점. 로프를 트럭에 묶는 자리다.</summary>
        private RopeAttachment WallTop(float localZ)
        {
            return RopeAttachment.At(
                truckBody,
                truck.transform.TransformPoint(new Vector3(BedCenterX, BedFloorTop + BedWallHeight, localZ)));
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

        private IEnumerator Settle(int fixedSteps)
        {
            for (int step = 0; step < fixedSteps; step++)
            {
                yield return new WaitForFixedUpdate();
            }
        }
    }
}

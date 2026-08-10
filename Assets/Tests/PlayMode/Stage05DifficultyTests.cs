using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    /// <summary>
    /// Stage 05는 오르막·내리막으로 수직 적재를 흔든 뒤, 얼음 노면의 낮은 접지력 때문에
    /// 코너 바깥으로 밀린다. 좋은 배치는 완주할 수 있어야 하지만 한쪽 고층 적재는
    /// Stage 04보다 명확한 손실을 내야 한다.
    /// </summary>
    public class Stage05DifficultyTests
    {
        private const float DriveTimeoutSeconds = 45f;
        private GameFlow flow;
        private CargoTracker tracker;
        private Transform bedAnchor;
        private TruckMover truck;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Stage05_Winter", LoadSceneMode.Single);

            flow = Object.FindAnyObjectByType<GameFlow>();
            tracker = Object.FindAnyObjectByType<CargoTracker>();
            truck = Object.FindAnyObjectByType<TruckMover>();
            bedAnchor = GameObject.Find("BedAnchor")?.transform;

            Assert.NotNull(flow);
            Assert.NotNull(tracker);
            Assert.NotNull(truck);
            Assert.NotNull(bedAnchor);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator 좌우_균형을_맞춰_실으면_최소_다섯_개가_완주한다()
        {
            PlaceCargo(new[]
            {
                new Vector3(-0.50f, 0.58f, -0.50f),
                new Vector3(-0.50f, 0.58f, 0.50f),
                new Vector3(0.50f, 0.58f, -0.50f),
                new Vector3(0.50f, 0.58f, 0.50f),
                new Vector3(-0.45f, 1.52f, 0f),
                new Vector3(0.45f, 1.52f, 0f),
                new Vector3(0f, 2.46f, 0f),
            });
            yield return Settle(180);

            Assert.AreEqual(7, tracker.RemainingCount, "출발 전에 균형 배치가 무너졌다");
            yield return DriveToResultAndMeasureDrift();

            Debug.Log($"[CargoStack] Stage05 균형 배치: {tracker.RemainingCount}/{tracker.TotalCount} 생존");
            Assert.That(tracker.RemainingCount, Is.GreaterThanOrEqualTo(5),
                "균형 배치도 절반 가까이 잃어 적재 전략의 보상이 부족하다");
        }

        [UnityTest]
        public IEnumerator 한쪽에_높게_쌓으면_최소_하나는_잃는다()
        {
            PlaceCargo(new[]
            {
                new Vector3(-0.50f, 0.58f, -0.85f),
                new Vector3(0.50f, 0.58f, -0.85f),
                new Vector3(-0.50f, 1.52f, -0.85f),
                new Vector3(0.50f, 1.52f, -0.85f),
                new Vector3(-0.45f, 2.46f, -0.85f),
                new Vector3(0.45f, 2.46f, -0.85f),
                new Vector3(0f, 3.40f, -0.85f),
            });
            yield return Settle(180);

            Assert.AreEqual(7, tracker.RemainingCount, "비교 배치가 출발 전에 무너졌다");
            yield return DriveToResultAndMeasureDrift();

            Debug.Log($"[CargoStack] Stage05 한쪽 높은 배치: {tracker.RemainingCount}/{tracker.TotalCount} 생존");
            Assert.That(tracker.RemainingCount, Is.LessThanOrEqualTo(4),
                "좌우 균형을 무시해도 결과가 같아 배치 학습이 성립하지 않는다");
        }

        [UnityTest]
        public IEnumerator 같은_코스도_노면_마찰을_높이면_코너에서_거의_미끄러지지_않는다()
        {
            var highGrip = new PhysicsMaterial("Stage05_TestHighGrip")
            {
                dynamicFriction = 1f,
                staticFriction = 1f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
            };

            foreach (BoxCollider road in Object.FindObjectsByType<BoxCollider>())
            {
                if (road.name.StartsWith("Road_"))
                {
                    road.sharedMaterial = highGrip;
                }
            }

            DriveMetrics metrics = default;
            yield return DriveToResult(value => metrics = value);

            Debug.Log(
                $"[CargoStack] Stage05 고마찰 대조군: 횡이탈 {metrics.MaxAbsOffset:0.00}m, "
                + $"슬립 {metrics.MaxCornerSlipSpeed:0.00}m/s, 마찰 {metrics.SurfaceFriction:0.00}");
            Assert.That(metrics.SurfaceFriction, Is.GreaterThan(0.9f));
            Assert.That(metrics.MaxAbsOffset, Is.LessThan(0.18f),
                "노면 마찰을 높여도 같은 드리프트가 남는다. 이동 경로가 미끄러짐을 강제하고 있다");
            Assert.That(metrics.MaxCornerSlipSpeed, Is.LessThan(0.18f),
                "고마찰 노면에서도 횡속도가 생겨 실제 접지력 기반 동작이 아니다");

            Object.Destroy(highGrip);
        }

        private void PlaceCargo(Vector3[] offsets)
        {
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();
            System.Array.Sort(cargo, (left, right) =>
                string.CompareOrdinal(left.name, right.name));
            Assert.AreEqual(offsets.Length, cargo.Length);
            for (int index = 0; index < cargo.Length; index++)
            {
                Rigidbody body = cargo[index].Body;
                body.position = bedAnchor.TransformPoint(offsets[index]);
                body.rotation = bedAnchor.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                cargo[index].transform.SetPositionAndRotation(body.position, body.rotation);
            }
        }

        private static IEnumerator Settle(int fixedSteps)
        {
            for (int step = 0; step < fixedSteps; step++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private IEnumerator DriveToResultAndMeasureDrift()
        {
            DriveMetrics metrics = default;
            yield return DriveToResult(value => metrics = value);

            Debug.Log(
                $"[CargoStack] Stage05 코너 접지 슬립: 이탈 {metrics.StrongestLeft:0.00}/"
                + $"{metrics.StrongestRight:0.00}m, 횡속도 {metrics.MaxCornerSlipSpeed:0.00}m/s, "
                + $"요 {metrics.MaxSlipAngle:0.0}°, 롤 {metrics.MaxRoll:0.0}°, "
                + $"요구 횡가속 {metrics.MaxCorneringDemand:0.00}m/s², "
                + $"마찰 {metrics.SurfaceFriction:0.00}");
            Assert.That(metrics.MaxPreCornerOffset, Is.LessThan(0.08f),
                "첫 코너 전 직선에서부터 옆으로 밀려 고정 드리프트처럼 보인다");
            Assert.That(metrics.MaxCorneringDemand, Is.GreaterThan(0.8f),
                "S자 코너가 횡가속도를 요구하지 않는다");
            Assert.That(metrics.MaxCornerSlipSpeed, Is.GreaterThan(0.22f),
                "얼음 접지 한계를 넘는 코너에서도 횡미끄러짐이 생기지 않는다");
            Assert.That(metrics.MaxAbsOffset, Is.GreaterThan(2.3f),
                "조정한 빙판 접지력에 비해 코너 바깥으로 밀리는 거리가 약하다");
            Assert.That(metrics.MaxAbsOffset, Is.LessThan(2.8f),
                "빙판 코너에서 도로를 가로지를 만큼 밀려 조향 회복이 불가능하다");
            Assert.That(metrics.OutwardSlipSamples, Is.GreaterThan(10),
                "코너 방향과 관계없이 정해진 쪽으로 움직여 관성 드리프트가 아니다");
            Assert.That(metrics.MaxSlipAngle, Is.GreaterThan(2.5f),
                "차체 진행 방향과 실제 이동 방향이 같아 미끄러지는 자세가 보이지 않는다");
            Assert.That(metrics.SurfaceFriction, Is.LessThan(0.1f),
                "빙판 PhysicsMaterial의 낮은 마찰이 차량 접지 계산에 반영되지 않았다");
        }

        private IEnumerator DriveToResult(System.Action<DriveMetrics> completed)
        {
            Time.timeScale = 3f;
            truck.EnableAutopilotForTesting();
            flow.StartDriving();

            float remaining = DriveTimeoutSeconds;
            DriveMetrics metrics = default;
            while (flow.State != GameState.Result && remaining > 0f)
            {
                metrics.StrongestLeft = Mathf.Min(metrics.StrongestLeft, truck.LateralDriftOffset);
                metrics.StrongestRight = Mathf.Max(metrics.StrongestRight, truck.LateralDriftOffset);
                metrics.MaxCorneringDemand = Mathf.Max(
                    metrics.MaxCorneringDemand,
                    Mathf.Abs(truck.CorneringAccelerationDemand));
                metrics.MaxSlipAngle = Mathf.Max(metrics.MaxSlipAngle, Mathf.Abs(truck.DriftYawDegrees));
                metrics.MaxRoll = Mathf.Max(metrics.MaxRoll, Mathf.Abs(truck.DriftRollDegrees));
                metrics.SurfaceFriction = truck.SurfaceFriction;

                if (truck.Progress < 0.12f
                    && Mathf.Abs(truck.CorneringAccelerationDemand) < 0.15f)
                {
                    metrics.MaxPreCornerOffset = Mathf.Max(
                        metrics.MaxPreCornerOffset,
                        Mathf.Abs(truck.LateralDriftOffset));
                }

                if (Mathf.Abs(truck.CorneringAccelerationDemand) > 0.55f)
                {
                    metrics.MaxCornerSlipSpeed = Mathf.Max(
                        metrics.MaxCornerSlipSpeed,
                        Mathf.Abs(truck.LateralSlipSpeed));
                    if (Mathf.Abs(truck.LateralSlipSpeed) > 0.08f
                        && Mathf.Sign(truck.LateralSlipSpeed)
                            == -Mathf.Sign(truck.CorneringAccelerationDemand))
                    {
                        metrics.OutwardSlipSamples++;
                    }
                }

                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(GameState.Result, flow.State, "Stage 05 주행이 제한 시간 안에 끝나지 않았다");
            completed(metrics);
        }

        private struct DriveMetrics
        {
            public float StrongestLeft;
            public float StrongestRight;
            public float MaxPreCornerOffset;
            public float MaxCorneringDemand;
            public float MaxCornerSlipSpeed;
            public float MaxSlipAngle;
            public float MaxRoll;
            public float SurfaceFriction;
            public int OutwardSlipSamples;

            public float MaxAbsOffset => Mathf.Max(Mathf.Abs(StrongestLeft), StrongestRight);
        }
    }
}

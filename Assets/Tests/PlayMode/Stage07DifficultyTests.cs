using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    public class Stage07DifficultyTests
    {
        [UnityTest]
        public IEnumerator 대형_방지턱을_달리는_동안_네_바퀴가_도로를_관통하지_않는다()
        {
            yield return SceneManager.LoadSceneAsync("Stage07_HardBumps", LoadSceneMode.Single);

            GameFlow flow = Object.FindAnyObjectByType<GameFlow>();
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            TruckWheelAnimator wheels = truck.GetComponent<TruckWheelAnimator>();
            MeshCollider road = GameObject.Find("RoadSurface").GetComponent<MeshCollider>();
            float minimumClearance = float.PositiveInfinity;
            float maximumClearance = float.NegativeInfinity;
            float maximumSuspensionTravel = 0f;

            yield return null;
            wheels.SendMessage("LateUpdate");
            MeasureWheelClearance(
                wheels,
                road,
                ref minimumClearance,
                ref maximumClearance,
                ref maximumSuspensionTravel);

            Time.timeScale = 3f;
            truck.EnableAutopilotForTesting();
            flow.StartDriving();
            float remaining = 25f;
            while (flow.State != GameState.Result && remaining > 0f)
            {
                yield return null;
                wheels.SendMessage("LateUpdate");
                remaining -= Time.unscaledDeltaTime;
                MeasureWheelClearance(
                    wheels,
                    road,
                    ref minimumClearance,
                    ref maximumClearance,
                    ref maximumSuspensionTravel);
            }

            Time.timeScale = 1f;
            Assert.AreEqual(GameState.Result, flow.State, "Stage07 접지 주행이 제한 시간 안에 끝나지 않았다");
            Debug.Log(
                $"[CargoStack] Stage07 바퀴 접지: "
                + $"최소 {minimumClearance:0.000}m, 최대 {maximumClearance:0.000}m, "
                + $"최대 서스펜션 이동 {maximumSuspensionTravel:0.000}m");
            Assert.That(minimumClearance, Is.GreaterThanOrEqualTo(-0.015f),
                "주행 중 바퀴가 도로를 관통했다");
            Assert.That(maximumClearance, Is.LessThanOrEqualTo(0.025f),
                "주행 중 바퀴가 도로에서 떨어졌다");
        }

        [UnityTest]
        public IEnumerator 로프_세개로_두층_화물을_눌러도_대형_방지턱에서_강하게_튀지만_전멸하지_않는다()
        {
            yield return SceneManager.LoadSceneAsync("Stage07_HardBumps", LoadSceneMode.Single);

            GameFlow flow = Object.FindAnyObjectByType<GameFlow>();
            CargoTracker tracker = Object.FindAnyObjectByType<CargoTracker>();
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            Transform bedAnchor = GameObject.Find("BedAnchor")?.transform;
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();
            System.Array.Sort(cargo, (left, right) =>
                string.CompareOrdinal(left.name, right.name));

            Assert.NotNull(flow);
            Assert.NotNull(tracker);
            Assert.NotNull(truck);
            Assert.NotNull(bedAnchor);
            Assert.AreEqual(8, cargo.Length);

            Vector3[] offsets =
            {
                new(-0.55f, 0.90f, -0.55f),
                new(0.55f, 0.58f, -0.55f),
                new(-0.55f, 0.53f, 0.55f),
                new(0.55f, 0.54f, 0.55f),
                new(-0.55f, 2.18f, -0.55f),
                new(0.55f, 1.47f, -0.55f),
                new(-0.55f, 1.48f, 0.55f),
                new(0.55f, 1.65f, 0.55f),
            };
            for (int index = 0; index < cargo.Length; index++)
            {
                Rigidbody body = cargo[index].Body;
                body.position = bedAnchor.TransformPoint(offsets[index]);
                body.rotation = bedAnchor.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                cargo[index].transform.SetPositionAndRotation(body.position, body.rotation);
            }

            for (int step = 0; step < 220; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(8, tracker.RemainingCount, "Stage07 두 층 적재가 출발 전에 무너졌다");
            Rigidbody truckBody = truck.GetComponent<Rigidbody>();
            var ropeSettings = new RopeSettings();
            Rope frontRope = Rope.Create(
                RopeAttachment.At(truckBody, truck.transform.TransformPoint(new Vector3(-1.35f, 0.9f, -1.13f))),
                RopeAttachment.At(truckBody, truck.transform.TransformPoint(new Vector3(-1.35f, 0.9f, 1.13f))),
                ropeSettings,
                null);
            Rope rearRope = Rope.Create(
                RopeAttachment.At(truckBody, truck.transform.TransformPoint(new Vector3(-2.35f, 0.9f, -1.13f))),
                RopeAttachment.At(truckBody, truck.transform.TransformPoint(new Vector3(-2.35f, 0.9f, 1.13f))),
                ropeSettings,
                null);
            Rope lengthwiseRope = Rope.Create(
                RopeAttachment.At(truckBody, truck.transform.TransformPoint(new Vector3(-2.98f, 0.9f, 0f))),
                RopeAttachment.At(truckBody, truck.transform.TransformPoint(new Vector3(-0.64f, 0.9f, 0f))),
                ropeSettings,
                null);
            Assert.NotNull(frontRope, "앞쪽 가로 로프를 만들지 못했다");
            Assert.NotNull(rearRope, "뒤쪽 가로 로프를 만들지 못했다");
            Assert.NotNull(lengthwiseRope, "진행 방향 로프를 만들지 못했다");
            for (int step = 0; step < 100; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(8, tracker.RemainingCount, "로프를 거는 동안 화물이 무너졌다");
            var settledLocalHeights = new float[cargo.Length];
            for (int index = 0; index < cargo.Length; index++)
            {
                settledLocalHeights[index] =
                    bedAnchor.InverseTransformPoint(cargo[index].transform.position).y;
            }

            Time.timeScale = 3f;
            truck.EnableAutopilotForTesting();
            flow.StartDriving();
            float remaining = 50f;
            float peakUpwardVelocity = 0f;
            float peakRelativeRise = 0f;
            while (flow.State != GameState.Result && remaining > 0f)
            {
                if (truck.Progress < 0.78f)
                {
                    for (int index = 0; index < cargo.Length; index++)
                    {
                        peakUpwardVelocity = Mathf.Max(
                            peakUpwardVelocity,
                            cargo[index].Body.linearVelocity.y);
                        float currentHeight =
                            bedAnchor.InverseTransformPoint(cargo[index].transform.position).y;
                        float relativeRise = currentHeight - settledLocalHeights[index];
                        if (relativeRise < 3f)
                        {
                            peakRelativeRise = Mathf.Max(peakRelativeRise, relativeRise);
                        }
                    }
                }

                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            Time.timeScale = 1f;
            Assert.AreEqual(GameState.Result, flow.State, "Stage07 주행이 제한 시간 안에 끝나지 않았다");
            Debug.Log(
                $"[CargoStack] Stage07 방지턱 로프 3개 2층: "
                + $"{tracker.RemainingCount}/{tracker.TotalCount} 생존, "
                + $"최대 상승속도 {peakUpwardVelocity:0.00}m/s, "
                + $"짐칸 대비 최고 들림 {peakRelativeRise:0.00}m");

            Assert.That(peakUpwardVelocity, Is.GreaterThan(2f),
                "대형 방지턱에서도 화물이 위로 튀지 않는다");
            Assert.That(peakRelativeRise, Is.GreaterThan(0.35f),
                "화물이 짐칸에서 들릴 만큼 강한 방지턱이 아니다");
            Assert.That(tracker.RemainingCount, Is.LessThanOrEqualTo(6),
                "로프 세 개로 고정해도 손실이 전혀 없어 고난도 방지턱이 아니다");
            Assert.That(tracker.RemainingCount, Is.GreaterThanOrEqualTo(2),
                "로프를 모두 사용해도 전멸해 배치 판단 전에 결과가 결정된다");
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        private static void MeasureWheelClearance(
            TruckWheelAnimator wheels,
            MeshCollider road,
            ref float minimum,
            ref float maximum,
            ref float maximumSuspensionTravel)
        {
            const float RayStart = 1f;
            for (int index = 0; index < wheels.WheelCount; index++)
            {
                Vector3 up = wheels.transform.up;
                Ray ray = new(wheels.GetSuspensionRoot(index).position + up * RayStart, -up);
                Assert.IsTrue(road.Raycast(ray, out RaycastHit hit, 3f),
                    $"{index}번 바퀴 아래에 도로가 없다");
                float clearance = hit.distance - RayStart - wheels.WheelRadius;
                minimum = Mathf.Min(minimum, clearance);
                maximum = Mathf.Max(maximum, clearance);
                float travel = Mathf.Abs(
                    wheels.GetSuspensionRoot(index).localPosition.y
                    - wheels.GetRestLocalPosition(index).y);
                maximumSuspensionTravel = Mathf.Max(maximumSuspensionTravel, travel);
            }
        }
    }
}

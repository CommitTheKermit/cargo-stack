using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    public class Stage06DifficultyTests
    {
        [UnityTest]
        public IEnumerator 균형_배치라면_새_설원길에서도_절반_이상이_완주한다()
        {
            yield return SceneManager.LoadSceneAsync("Stage06_FrozenCargo", LoadSceneMode.Single);

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
            Assert.AreEqual(7, cargo.Length);

            Vector3[] offsets =
            {
                new(-0.50f, 0.58f, -0.50f),
                new(0.50f, 0.58f, -0.50f),
                new(-0.50f, 0.58f, 0.50f),
                new(0.50f, 0.58f, 0.50f),
                new(-0.45f, 1.52f, 0f),
                new(0.45f, 1.58f, 0f),
                new(0f, 2.70f, 0f),
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

            for (int step = 0; step < 180; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(7, tracker.RemainingCount, "Stage06 균형 배치가 출발 전에 무너졌다");
            var settledLocalPositions = new Vector3[cargo.Length];
            var isIce = new bool[cargo.Length];
            for (int index = 0; index < cargo.Length; index++)
            {
                settledLocalPositions[index] = bedAnchor.InverseTransformPoint(cargo[index].transform.position);
                isIce[index] = cargo[index].transform.Find("ImportedVisual_IceCube") != null;
            }

            Time.timeScale = 3f;
            truck.EnableAutopilotForTesting();
            flow.StartDriving();
            float remaining = 55f;
            float maxIceDrift = 0f;
            float maxStandardDrift = 0f;
            while (flow.State != GameState.Result && remaining > 0f)
            {
                if (truck.Progress < 0.65f)
                {
                    for (int index = 0; index < cargo.Length; index++)
                    {
                        Vector3 current = bedAnchor.InverseTransformPoint(cargo[index].transform.position);
                        float drift = Vector2.Distance(
                            new Vector2(current.x, current.z),
                            new Vector2(settledLocalPositions[index].x, settledLocalPositions[index].z));
                        if (isIce[index])
                        {
                            maxIceDrift = Mathf.Max(maxIceDrift, drift);
                        }
                        else
                        {
                            maxStandardDrift = Mathf.Max(maxStandardDrift, drift);
                        }
                    }
                }

                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            Time.timeScale = 1f;
            Assert.AreEqual(GameState.Result, flow.State, "Stage06 주행이 제한 시간 안에 끝나지 않았다");
            Debug.Log(
                $"[CargoStack] Stage06 균형 배치: "
                + $"{tracker.RemainingCount}/{tracker.TotalCount} 생존, "
                + $"중반까지 얼음 최대 이동 {maxIceDrift:0.00}m, "
                + $"일반 최대 이동 {maxStandardDrift:0.00}m");
            Assert.That(tracker.RemainingCount, Is.GreaterThanOrEqualTo(4),
                "얼음 화물에 대비해 균형을 맞춰도 절반 이상을 잃는다");
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

    }
}

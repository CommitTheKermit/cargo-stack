using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    public class TruckControlTests
    {
        [UnityTest]
        public IEnumerator 장애물을_피하지_않고_직진하면_트럭이_멈춘다()
        {
            yield return SceneManager.LoadSceneAsync("Stage02_SpeedBumps", LoadSceneMode.Single);

            GameFlow flow = Object.FindAnyObjectByType<GameFlow>();
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            GameObject obstacles = GameObject.Find("RoadObstacles");
            Assert.NotNull(flow);
            Assert.NotNull(truck);
            Assert.NotNull(obstacles);

            truck.SetControlInputForTesting(1f, 0f, 0f);
            flow.StartDriving();
            float timeout = 15f;
            bool accelerated = false;
            while (timeout > 0f)
            {
                yield return new WaitForFixedUpdate();
                timeout -= Time.fixedDeltaTime;
                accelerated |= truck.Speed > 1f;
                if (accelerated && truck.Speed < 0.01f)
                {
                    break;
                }
            }

            Assert.IsTrue(accelerated, "장애물에 닿기 전에 트럭이 출발하지 못했다");
            Assert.That(truck.Speed, Is.LessThan(0.01f), "직진한 트럭이 장애물을 통과했다");
            Assert.That(truck.Progress, Is.LessThan(0.9f), "도착 직전까지 장애물을 만나지 못했다");
            Debug.Log($"[CargoStack] 직진 장애물 충돌: 진행도 {truck.Progress:0.00}, 속도 {truck.Speed:0.00}m/s");
            truck.ClearControlInputForTesting();
        }

        [UnityTest]
        public IEnumerator 앞바퀴를_조향해_전진하고_S를_누르면_제동후_후진한다()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);

            GameFlow flow = Object.FindAnyObjectByType<GameFlow>();
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            TruckWheelAnimator wheels = truck != null
                ? truck.GetComponent<TruckWheelAnimator>()
                : null;
            Assert.NotNull(flow);
            Assert.NotNull(truck);
            Assert.NotNull(wheels);
            yield return null;

            Vector3 start = truck.transform.position;
            truck.SetControlInputForTesting(0f, 0f, 0f);
            flow.StartDriving();

            float startTimeout = 3f;
            while (flow.State == GameState.Loading && startTimeout > 0f)
            {
                startTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            for (int step = 0; step < 30; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(GameState.Driving, flow.State);
            Assert.That(truck.Speed, Is.LessThan(0.05f), "엑셀 없이 트럭이 스스로 가속했다");
            Assert.That(Vector3.Distance(truck.transform.position, start), Is.LessThan(0.05f),
                "직접 조작 모드인데 입력 없이 트럭이 움직였다");

            Vector3 stationaryHeading = truck.transform.right;
            truck.SetControlInputForTesting(0f, 0f, 1f);
            for (int step = 0; step < 30; step++)
            {
                yield return new WaitForFixedUpdate();
            }
            yield return null;

            Assert.That(truck.SteeringAngleDegrees, Is.GreaterThan(25f),
                "정지 상태에서 조향해도 앞바퀴 조향각이 생기지 않았다");
            Assert.That(wheels.FrontSteeringAngleDegrees, Is.EqualTo(truck.SteeringAngleDegrees).Within(0.1f),
                "앞바퀴 시각 조향각이 주행 물리의 조향각과 다르다");
            float observedSteeringAngle = wheels.FrontSteeringAngleDegrees;
            Assert.That(Quaternion.Angle(Quaternion.identity, wheels.GetSuspensionRoot(0).localRotation),
                Is.GreaterThan(20f), "앞바퀴 메시가 좌우로 꺾이지 않았다");
            Assert.That(Quaternion.Angle(Quaternion.identity, wheels.GetSuspensionRoot(2).localRotation),
                Is.LessThan(1f), "뒷바퀴까지 조향되고 있다");
            Assert.That(Vector3.Angle(stationaryHeading, truck.transform.right), Is.LessThan(0.5f),
                "차가 움직이지 않는데 차체만 제자리 회전했다");

            truck.SetControlInputForTesting(0f, 0f, 0f);
            for (int step = 0; step < 25; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            float spinBeforeForward = wheels.TotalSpinDegrees;
            truck.SetControlInputForTesting(1f, 0f, 0f);
            for (int step = 0; step < 90; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            float acceleratedSpeed = truck.Speed;
            float progressBeforeSteering = truck.Progress;
            Assert.That(acceleratedSpeed, Is.GreaterThan(3f), "엑셀 입력으로 충분히 가속하지 못했다");
            Assert.That(progressBeforeSteering, Is.GreaterThan(0.01f), "엑셀을 밟아도 전진하지 않았다");
            Assert.That(wheels.TotalSpinDegrees, Is.LessThan(spinBeforeForward - 90f),
                "전진 거리만큼 바퀴가 굴러가지 않았다");

            Vector3 headingBeforeTurn = truck.transform.right;
            truck.SetControlInputForTesting(1f, 0f, 1f);
            for (int step = 0; step < 45; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(Mathf.Abs(truck.LateralDriftOffset), Is.GreaterThan(0.12f),
                "조향 입력이 트럭의 실제 이동 경로를 바꾸지 못했다");
            Assert.That(Vector3.Angle(headingBeforeTurn, truck.transform.right), Is.GreaterThan(5f),
                "앞바퀴 조향각이 차체의 회전 반경에 반영되지 않았다");

            truck.SetControlInputForTesting(0f, 1f, 0f);
            for (int step = 0; step < 130; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(truck.Speed, Is.LessThan(-1f),
                "S 입력이 전진 속도를 줄인 뒤 후진으로 이어지지 않았다");
            float reverseSpinStart = wheels.TotalSpinDegrees;
            Vector3 reverseStart = truck.transform.position;
            Vector3 reverseHeading = truck.transform.right;
            for (int step = 0; step < 30; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(wheels.TotalSpinDegrees, Is.GreaterThan(reverseSpinStart + 45f),
                "후진할 때 바퀴가 반대 방향으로 구르지 않았다");
            Assert.That(Vector3.Dot(truck.transform.position - reverseStart, reverseHeading),
                Is.LessThan(-0.2f), "S를 누른 상태에서 차가 뒤로 움직이지 않았다");
            Debug.Log(
                $"[CargoStack] 직접 조작: 가속 {acceleratedSpeed:0.00}m/s, "
                + $"조향 횡이탈 {truck.LateralDriftOffset:0.00}m, "
                + $"앞바퀴 {observedSteeringAngle:0.0}°, "
                + $"S 후진 {truck.Speed:0.00}m/s");
            truck.ClearControlInputForTesting();
        }
    }
}

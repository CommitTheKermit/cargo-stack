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
        public IEnumerator 월드_바위에_직진하면_트럭이_멈춘다()
        {
            yield return SceneManager.LoadSceneAsync("Stage01_Tutorial", LoadSceneMode.Single);

            GameFlow flow = Object.FindAnyObjectByType<GameFlow>();
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            Transform rocks = GameObject.Find("Environment")?.transform.Find("Rocks");
            Assert.NotNull(flow);
            Assert.NotNull(truck);
            Assert.NotNull(rocks);
            Assert.That(rocks.childCount, Is.GreaterThan(0));

            Transform rock = rocks.GetChild(0);
            Collider rockCollider = rock.GetComponentInChildren<Collider>();
            Assert.NotNull(rockCollider);
            Vector3 targetCenter = truck.transform.position + truck.transform.right * 8f;
            rock.position += targetCenter - rockCollider.bounds.center;
            Physics.SyncTransforms();

            truck.SetControlInputForTesting(1f, 0f, 0f);
            flow.StartDriving();
            float timeout = 6f;
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

            Assert.IsTrue(accelerated, "바위에 닿기 전에 트럭이 출발하지 못했다");
            Assert.That(truck.Speed, Is.LessThan(0.01f), "트럭이 월드 바위를 통과했다");
            AssertTruckDoesNotPenetrate(truck, new[] { rockCollider });
            Debug.Log($"[CargoStack] 월드 바위 충돌: 진행도 {truck.Progress:0.00}, 속도 {truck.Speed:0.00}m/s");
            truck.ClearControlInputForTesting();
        }

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
            AssertTruckDoesNotPenetrate(
                truck,
                obstacles.GetComponentsInChildren<MeshCollider>(true));
            Debug.Log($"[CargoStack] 직진 장애물 충돌: 진행도 {truck.Progress:0.00}, 속도 {truck.Speed:0.00}m/s");
            truck.ClearControlInputForTesting();
        }

        private static void AssertTruckDoesNotPenetrate(
            TruckMover truck,
            Collider[] obstacles)
        {
            foreach (Collider truckCollider in truck.GetComponentsInChildren<Collider>(true))
            {
                if (!truckCollider.enabled || truckCollider.isTrigger)
                {
                    continue;
                }

                foreach (Collider obstacle in obstacles)
                {
                    bool overlaps = Physics.ComputePenetration(
                        truckCollider,
                        truckCollider.transform.position,
                        truckCollider.transform.rotation,
                        obstacle,
                        obstacle.transform.position,
                        obstacle.transform.rotation,
                        out _,
                        out _);
                    Assert.IsFalse(overlaps,
                        $"{truckCollider.name}가 {obstacle.name} 안에 들어갔다");
                }
            }
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
            int groundMask = LayerMask.GetMask("Ground");
            Assert.NotNull(flow);
            Assert.NotNull(truck);
            Assert.NotNull(wheels);
            Assert.AreNotEqual(0, groundMask);
            GameObject.Find("Environment")?.SetActive(false);
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
            Assert.That(Mathf.Abs(truck.DriftRollDegrees), Is.LessThanOrEqualTo(10.05f),
                "고속 조향 롤이 서스펜션 접지 한계를 넘었다");

            yield return null;
            wheels.SendMessage("LateUpdate");
            Vector3 up = truck.transform.up;
            float maximumWheelClearance = 0f;
            for (int index = 0; index < wheels.WheelCount; index++)
            {
                const float RayStart = 1f;
                var ray = new Ray(wheels.GetSuspensionRoot(index).position + up * RayStart, -up);
                Assert.IsTrue(Physics.Raycast(
                        ray,
                        out RaycastHit hit,
                        3f,
                        groundMask,
                        QueryTriggerInteraction.Ignore),
                    $"고속 조향 중 {index}번 바퀴 아래에 지면이 없다");
                float clearance = hit.distance - RayStart - wheels.WheelRadius;
                maximumWheelClearance = Mathf.Max(maximumWheelClearance, Mathf.Abs(clearance));
                Debug.Log(
                    $"[CargoStack] 고속 조향 접지 {index}: 롤 {truck.DriftRollDegrees:0.00}°, "
                    + $"간격 {clearance:0.000}m, 서스펜션 "
                    + $"{wheels.GetSuspensionRoot(index).localPosition.y - wheels.GetRestLocalPosition(index).y:0.000}m");
            }
            Assert.That(maximumWheelClearance, Is.LessThanOrEqualTo(0.025f),
                $"고속 조향 중 바퀴가 지면에서 떨어졌다: {maximumWheelClearance:0.000}m");

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

        [UnityTest]
        public IEnumerator 실제_접지를_따르되_탐지_실패가_아닌_경계벽에서_멈춘다()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);

            GameFlow flow = Object.FindAnyObjectByType<GameFlow>();
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            TruckWheelAnimator wheels = truck != null
                ? truck.GetComponent<TruckWheelAnimator>()
                : null;
            MeshCollider road = GameObject.Find("RoadSurface")?.GetComponent<MeshCollider>();
            MeshCollider ground = GameObject.Find("GroundSurface")?.GetComponent<MeshCollider>();
            Transform boundary = GameObject.Find("GroundBoundary")?.transform.Find("Boundary_End");
            Assert.NotNull(flow);
            Assert.NotNull(truck);
            Assert.NotNull(wheels);
            Assert.NotNull(road);
            Assert.NotNull(ground);
            Assert.NotNull(boundary);
            GameObject.Find("Environment")?.SetActive(false);

            truck.SetControlInputForTesting(0f, 0f, 0f);
            flow.StartDriving();
            while (flow.State == GameState.Loading)
            {
                yield return null;
            }

            yield return new WaitForFixedUpdate();
            Vector3 originalUp = truck.transform.up;
            Vector3 rollAxis = truck.transform.right;
            road.transform.RotateAround(truck.transform.position, rollAxis, 8f);
            ground.transform.RotateAround(truck.transform.position, rollAxis, 8f);
            Physics.SyncTransforms();
            for (int step = 0; step < 5; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            float bodyRoll = Vector3.SignedAngle(originalUp, truck.transform.up, rollAxis);
            Assert.That(Mathf.Abs(bodyRoll), Is.GreaterThan(6f),
                "실제 지면을 기울여도 차체가 경로의 고정 평면만 따랐다");
            float maximumCompression = 0f;
            for (int index = 0; index < wheels.WheelCount; index++)
            {
                maximumCompression = Mathf.Max(
                    maximumCompression,
                    wheels.GetSuspensionRoot(index).localPosition.y
                        - wheels.GetRestLocalPosition(index).y);
            }

            Assert.That(maximumCompression, Is.LessThan(0.03f),
                "차체가 접점을 따르지 못해 바퀴가 차체 쪽으로 밀려 들어갔다");

            truck.SetControlInputForTesting(1f, 0f, 0f);
            for (int step = 0; step < 40; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(truck.Speed, Is.GreaterThan(2f), "지면 제거 전에 트럭이 출발하지 못했다");
            road.enabled = false;
            ground.enabled = false;
            Physics.SyncTransforms();
            Vector3 fallbackStart = truck.transform.position;
            for (int step = 0; step < 10; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            float fallbackMovement = Vector3.Distance(fallbackStart, truck.transform.position);
            Assert.That(truck.Speed, Is.GreaterThan(2f),
                "접지 탐지 실패가 트럭을 정지시켰다");
            Assert.That(fallbackMovement, Is.GreaterThan(0.2f),
                "접지 탐지 실패 후 경로 자세로 계속 주행하지 못했다");

            Vector3 wallHeading = Vector3.ProjectOnPlane(
                truck.transform.right,
                Vector3.up).normalized;
            boundary.SetPositionAndRotation(
                truck.transform.position + wallHeading * 3f + Vector3.up,
                Quaternion.LookRotation(wallHeading, Vector3.up));
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Vector3 stoppedPosition = truck.transform.position;
            for (int step = 0; step < 5; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            float boundaryMovement = Vector3.Distance(stoppedPosition, truck.transform.position);
            Assert.That(truck.Speed, Is.LessThan(0.01f),
                "트럭이 투명한 맵 경계벽에서 멈추지 않았다");
            Assert.That(boundaryMovement, Is.LessThan(0.03f),
                "트럭이 투명한 맵 경계벽을 통과했다");
            Debug.Log(
                $"[CargoStack] 실제 접지/경계: 차체 롤 {bodyRoll:0.00}°, "
                + $"최대 바퀴 압축 {maximumCompression:0.000}m, "
                + $"접지 실패 후 주행 {fallbackMovement:0.000}m, "
                + $"경계 후 이동 {boundaryMovement:0.000}m");
            truck.ClearControlInputForTesting();
        }

        [UnityTest]
        public IEnumerator 언덕을_대각선으로_주행해도_접지가_끊기지_않는다()
        {
            yield return SceneManager.LoadSceneAsync("Stage03_HillsAndPits", LoadSceneMode.Single);

            GameFlow flow = Object.FindAnyObjectByType<GameFlow>();
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            Assert.NotNull(flow);
            Assert.NotNull(truck);
            GameObject.Find("Environment")?.SetActive(false);
            GameObject.Find("RoadObstacles")?.SetActive(false);

            truck.SetControlInputForTesting(1f, 0f, 0f);
            flow.StartDriving();
            float hillTimeout = 15f;
            while (truck.transform.position.x < 101f && hillTimeout > 0f)
            {
                yield return new WaitForFixedUpdate();
                hillTimeout -= Time.fixedDeltaTime;
            }

            Assert.That(truck.transform.position.x, Is.GreaterThanOrEqualTo(101f),
                "언덕의 조향 검증 지점까지 도달하지 못했다");
            truck.SetControlInputForTesting(1f, 0f, 1f);

            float diagonalAngle = 0f;
            float turnTimeout = 1f;
            while (diagonalAngle < 45f && turnTimeout > 0f && truck.Speed > 0.01f)
            {
                yield return new WaitForFixedUpdate();
                Vector3 heading = Vector3.ProjectOnPlane(truck.transform.right, Vector3.up).normalized;
                diagonalAngle = Vector3.Angle(Vector3.right, heading);
                turnTimeout -= Time.fixedDeltaTime;
            }

            Assert.That(diagonalAngle, Is.GreaterThanOrEqualTo(45f),
                "언덕에서 대각선 자세를 만들기 전에 접지가 끊겼다");
            Assert.That(truck.Speed, Is.GreaterThan(5f),
                "장애물이 없는 언덕에서 접지 탐색 실패로 트럭이 멈췄다");
            Debug.Log(
                $"[CargoStack] 언덕 대각선 접지: 각도 {diagonalAngle:0.0}°, "
                + $"속도 {truck.Speed:0.00}m/s, 진행도 {truck.Progress:0.00}");
            truck.ClearControlInputForTesting();
        }

        [UnityTest]
        public IEnumerator 최고속도_60kmh가_실제_적용된다()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);

            GameFlow flow = Object.FindAnyObjectByType<GameFlow>();
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            StageContext context = Object.FindAnyObjectByType<StageContext>();
            Assert.NotNull(flow);
            Assert.NotNull(truck);
            Assert.NotNull(context);

            truck.SetControlInputForTesting(1f, 0f, 0f);
            flow.StartDriving();
            for (int step = 0; step < 200; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(context.Definition.MaxSpeed, Is.EqualTo(16.6667f).Within(0.01f));
            Assert.That(truck.Speed, Is.EqualTo(16.6667f).Within(0.15f),
                "전진 입력을 충분히 유지해도 60km/h 최고속도에 도달하지 못했다");
            Assert.That(truck.Speed01, Is.GreaterThan(0.99f));
            Debug.Log($"[CargoStack] 최고속도 검증: {truck.Speed:0.00}m/s ({truck.Speed * 3.6f:0.0}km/h)");
            truck.ClearControlInputForTesting();
        }
    }
}

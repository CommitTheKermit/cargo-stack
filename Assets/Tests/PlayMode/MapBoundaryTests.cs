using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    public class MapBoundaryTests
    {
        [UnityTest]
        public IEnumerator 네_맵에서_플레이어와_화물은_네_방향_외곽을_통과하지_못한다()
        {
            string[] sceneNames =
            {
                "Prototype",
                "Stage01_Tutorial",
                "Stage02_SpeedBumps",
                "Stage03_HillsAndPits",
            };

            foreach (string sceneName in sceneNames)
            {
                yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                yield return null;
                yield return VerifyLoadedSceneBoundary(sceneName);
            }
        }

        private static IEnumerator VerifyLoadedSceneBoundary(string sceneName)
        {
            GameObject boundary = GameObject.Find("GroundBoundary");
            RoutePath route = Object.FindFirstObjectByType<RoutePath>();
            PlayerController player = Object.FindFirstObjectByType<PlayerController>();
            Cargo cargo = FindBoxCargo();

            Assert.NotNull(boundary, $"{sceneName}: 맵 외곽 충돌벽 루트가 없다");
            Assert.NotNull(route, $"{sceneName}: 경로가 없다");
            Assert.NotNull(player, $"{sceneName}: 플레이어가 없다");
            Assert.NotNull(cargo, $"{sceneName}: 시험할 박스 화물이 없다");
            Assert.AreEqual(
                (route.SampleCount - 1) * 2 + 2,
                boundary.GetComponentsInChildren<BoxCollider>().Length,
                $"{sceneName}: 경로 좌우와 시작·끝을 모두 막는 충돌벽이 아니다");
            Assert.IsEmpty(
                boundary.GetComponentsInChildren<Renderer>(),
                $"{sceneName}: 가상 벽은 플레이 중 보이면 안 된다");

            int middle = route.SampleCount / 2;
            int last = route.SampleCount - 1;
            Vector3 middlePoint = route.SampleAt(middle);
            Vector3 startPoint = route.SampleAt(0);
            Vector3 endPoint = route.SampleAt(last);
            Transform leftWall = boundary.transform.Find($"Boundary_Left_{middle:000}");
            Transform rightWall = boundary.transform.Find($"Boundary_Right_{middle:000}");
            Transform startWall = boundary.transform.Find("Boundary_Start");
            Transform endWall = boundary.transform.Find("Boundary_End");

            Assert.NotNull(leftWall, $"{sceneName}: 왼쪽 외곽 벽이 없다");
            Assert.NotNull(rightWall, $"{sceneName}: 오른쪽 외곽 벽이 없다");
            Assert.NotNull(startWall, $"{sceneName}: 시작 외곽 벽이 없다");
            Assert.NotNull(endWall, $"{sceneName}: 끝 외곽 벽이 없다");

            Vector3 startHeading = PlanarDirection(startPoint, route.SampleAt(1));
            Vector3 endHeading = PlanarDirection(route.SampleAt(last - 1), endPoint);
            var cases = new[]
            {
                new BoundaryPushCase(
                    "왼쪽",
                    leftWall,
                    middlePoint,
                    PlanarDirection(middlePoint, leftWall.position)),
                new BoundaryPushCase(
                    "오른쪽",
                    rightWall,
                    middlePoint,
                    PlanarDirection(middlePoint, rightWall.position)),
                new BoundaryPushCase("시작", startWall, startPoint, -startHeading),
                new BoundaryPushCase("끝", endWall, endPoint, endHeading),
            };

            Collider cargoCollider = cargo.GetComponent<Collider>();
            float cargoHalfHeight = cargoCollider.bounds.extents.y;
            cargo.gameObject.SetActive(false);

            foreach (BoundaryPushCase testCase in cases)
            {
                yield return PushPlayerAgainstBoundary(player, testCase);

                player.SetWorldPose(
                    testCase.SurfacePoint + Vector3.up * 0.02f,
                    Quaternion.identity,
                    Vector3.zero);
                cargo.gameObject.SetActive(true);
                yield return PushCargoAgainstBoundary(cargo, cargoHalfHeight, testCase);
                cargo.gameObject.SetActive(false);
            }
        }

        private static Cargo FindBoxCargo()
        {
            foreach (Cargo candidate in Object.FindObjectsByType<Cargo>(FindObjectsSortMode.None))
            {
                if (candidate.GetComponent<BoxCollider>() != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static IEnumerator PushPlayerAgainstBoundary(
            PlayerController player,
            BoundaryPushCase testCase)
        {
            const float PushSpeed = 18f;
            Collider playerCollider = player.GetComponent<Collider>();
            Collider wallCollider = testCase.Wall.GetComponent<Collider>();
            List<Collider> ignored = IgnoreUnrelatedColliders(playerCollider, wallCollider);
            Vector3 startPosition = testCase.Wall.position - testCase.Outward * 2f;
            startPosition.y = testCase.SurfacePoint.y + 0.02f;
            player.SetWorldPose(startPosition, Quaternion.identity, Vector3.zero);
            Physics.SyncTransforms();

            for (int frame = 0; frame < 30; frame++)
            {
                Vector3 velocity = player.Body.linearVelocity;
                velocity.x = testCase.Outward.x * PushSpeed;
                velocity.z = testCase.Outward.z * PushSpeed;
                player.Body.linearVelocity = velocity;
                yield return new WaitForFixedUpdate();
            }

            RestoreCollisions(playerCollider, ignored);
            AssertInsideBoundary("플레이어", player.transform.position, testCase);
        }

        private static IEnumerator PushCargoAgainstBoundary(
            Cargo cargo,
            float halfHeight,
            BoundaryPushCase testCase)
        {
            const float PushSpeed = 18f;
            Rigidbody body = cargo.Body;
            Collider cargoCollider = cargo.GetComponent<Collider>();
            Collider wallCollider = testCase.Wall.GetComponent<Collider>();
            List<Collider> ignored = IgnoreUnrelatedColliders(cargoCollider, wallCollider);
            Vector3 startPosition = testCase.Wall.position - testCase.Outward * 2f;
            startPosition.y = testCase.SurfacePoint.y + halfHeight + 0.02f;
            body.position = startPosition;
            body.rotation = Quaternion.identity;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            cargo.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();

            for (int frame = 0; frame < 30; frame++)
            {
                Vector3 velocity = body.linearVelocity;
                velocity.x = testCase.Outward.x * PushSpeed;
                velocity.z = testCase.Outward.z * PushSpeed;
                body.linearVelocity = velocity;
                yield return new WaitForFixedUpdate();
            }

            RestoreCollisions(cargoCollider, ignored);
            AssertInsideBoundary("화물", cargo.transform.position, testCase);
        }

        private static List<Collider> IgnoreUnrelatedColliders(
            Collider target,
            Collider targetWall)
        {
            var ignored = new List<Collider>();
            foreach (Collider other in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (other == target
                    || other == targetWall
                    || other.gameObject.name == "GroundSurface")
                {
                    continue;
                }

                Physics.IgnoreCollision(target, other, true);
                ignored.Add(other);
            }

            return ignored;
        }

        private static void RestoreCollisions(Collider target, List<Collider> ignored)
        {
            foreach (Collider other in ignored)
            {
                if (other != null)
                {
                    Physics.IgnoreCollision(target, other, false);
                }
            }
        }

        private static void AssertInsideBoundary(
            string targetName,
            Vector3 position,
            BoundaryPushCase testCase)
        {
            float insideMargin = -Vector3.Dot(
                position - testCase.Wall.position,
                testCase.Outward);
            float heightFromSurface = position.y - testCase.SurfacePoint.y;
            Debug.Log(
                $"[CargoStack] 맵 외곽 {SceneManager.GetActiveScene().name}/"
                + $"{testCase.Name}/{targetName}: 내부 여유 {insideMargin:0.000}m, "
                + $"지면 높이차 {heightFromSurface:0.000}m (18m/s 연속 밀기)");

            Assert.That(
                insideMargin,
                Is.InRange(0.2f, 1.2f),
                $"{targetName}이 맵 {testCase.Name} 외곽 벽에 닿아 멈추지 않았다");
            Assert.That(
                position.y,
                Is.GreaterThan(testCase.SurfacePoint.y - 1f),
                $"{targetName}이 맵 {testCase.Name} 외곽에서 아래로 떨어졌다");
        }

        private static Vector3 PlanarDirection(Vector3 from, Vector3 to)
        {
            return Vector3.ProjectOnPlane(to - from, Vector3.up).normalized;
        }

        private readonly struct BoundaryPushCase
        {
            public BoundaryPushCase(
                string name,
                Transform wall,
                Vector3 surfacePoint,
                Vector3 outward)
            {
                Name = name;
                Wall = wall;
                SurfacePoint = surfacePoint;
                Outward = outward;
            }

            public string Name { get; }
            public Transform Wall { get; }
            public Vector3 SurfacePoint { get; }
            public Vector3 Outward { get; }
        }
    }
}

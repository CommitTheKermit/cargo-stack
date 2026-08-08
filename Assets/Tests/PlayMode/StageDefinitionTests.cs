using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    public class StageDefinitionTests
    {
        [UnityTest]
        public IEnumerator 튜토리얼_스테이지는_정의한_구성으로_생성된다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage01_Tutorial",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            TruckMover truck = Object.FindAnyObjectByType<TruckMover>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();

            Assert.NotNull(context, "StageContext가 없다");
            Assert.NotNull(context.Definition, "스테이지 정의가 연결되지 않았다");
            Assert.AreEqual("stage-01", context.Definition.StageId);
            Assert.AreEqual(
                SceneManager.GetActiveScene().name,
                context.Definition.SceneName);
            Assert.NotNull(route, "경로가 생성되지 않았다");
            Assert.That(route.TotalLength, Is.GreaterThan(125f));
            Assert.NotNull(truck, "트럭이 생성되지 않았다");
            Assert.AreEqual(context.Definition.CargoCount, cargo.Length);
            Assert.AreEqual(6, cargo.Length, "튜토리얼 화물 구성이 달라졌다");

            int hills = 0;
            bool wasOnHill = false;
            float highestPoint = float.NegativeInfinity;
            for (int index = 0; index < route.SampleCount; index++)
            {
                float height = route.SampleAt(index).y;
                highestPoint = Mathf.Max(highestPoint, height);
                bool isOnHill = height > 2f;
                if (isOnHill && !wasOnHill)
                {
                    hills++;
                }

                wasOnHill = isOnHill;
            }

            Debug.Log(
                $"[CargoStack] Stage01 경로: 길이 {route.TotalLength:0.0}m, "
                + $"언덕 {hills}개, 최고점 {highestPoint:0.0}m");
            Assert.AreEqual(2, hills, "Stage 01 경로에는 분리된 언덕이 두 개여야 한다");

            int boxes = 0;
            int capsules = 0;
            foreach (Cargo item in cargo)
            {
                boxes += item.GetComponent<BoxCollider>() != null ? 1 : 0;
                capsules += item.GetComponent<CapsuleCollider>() != null ? 1 : 0;
            }

            Assert.AreEqual(4, boxes, "상자·조각상·전등 화물은 박스 프록시 네 개여야 한다");
            Assert.AreEqual(2, capsules, "원통 화물은 두 개여야 한다");
        }

        [UnityTest]
        public IEnumerator 두번째_스테이지는_방지턱_두개와_구르는_원통을_만든다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage02_SpeedBumps",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();

            Assert.NotNull(context, "StageContext가 없다");
            Assert.NotNull(context.Definition, "스테이지 정의가 연결되지 않았다");
            Assert.AreEqual("stage-02", context.Definition.StageId);
            Assert.AreEqual(
                SceneManager.GetActiveScene().name,
                context.Definition.SceneName);
            Assert.NotNull(route, "경로가 생성되지 않았다");
            Assert.That(route.TotalLength, Is.GreaterThan(100f));
            Assert.AreEqual(5, cargo.Length, "Stage 02 화물 구성이 달라졌다");

            int raisedRegions = 0;
            bool wasRaised = false;
            for (int index = 0; index < route.SampleCount; index++)
            {
                bool isRaised = route.SampleAt(index).y > 0.3f;
                if (isRaised && !wasRaised)
                {
                    raisedRegions++;
                }

                wasRaised = isRaised;
            }

            Assert.AreEqual(2, raisedRegions, "방지턱 두 구간이 경로 높이에 반영되지 않았다");

            int boxes = 0;
            int capsules = 0;
            foreach (Cargo item in cargo)
            {
                boxes += item.GetComponent<BoxCollider>() != null ? 1 : 0;
                capsules += item.GetComponent<CapsuleCollider>() != null ? 1 : 0;
            }

            Assert.AreEqual(4, boxes, "상자 화물은 네 개여야 한다");
            Assert.AreEqual(1, capsules, "구르는 원통 화물은 하나여야 한다");
        }

        [UnityTest]
        public IEnumerator 세번째_스테이지는_평지_구덩이_평지_언덕_평지_순서로_박스_여섯개와_드럼통_세개를_만든다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage03_HillsAndPits",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();

            Assert.NotNull(context, "StageContext가 없다");
            Assert.NotNull(context.Definition, "스테이지 정의가 연결되지 않았다");
            Assert.AreEqual("stage-03", context.Definition.StageId);
            Assert.AreEqual(
                SceneManager.GetActiveScene().name,
                context.Definition.SceneName);
            Assert.NotNull(route, "경로가 생성되지 않았다");
            Assert.That(route.TotalLength, Is.GreaterThan(180f));
            Assert.AreEqual(9, cargo.Length, "Stage 03 화물은 아홉 개여야 한다");

            int hills = CountHeightRegions(route, pointHeight => pointHeight > 2f);
            int pits = CountHeightRegions(route, pointHeight => pointHeight < -1f);
            int firstPit = FindHeightSample(route, pointHeight => pointHeight < -1f);
            int lastPit = FindHeightSample(route, pointHeight => pointHeight < -1f, searchBackward: true);
            int firstHill = FindHeightSample(route, pointHeight => pointHeight > 2f);
            int lastHill = FindHeightSample(route, pointHeight => pointHeight > 2f, searchBackward: true);
            float lowestPoint = float.PositiveInfinity;
            float highestPoint = float.NegativeInfinity;
            for (int index = 0; index < route.SampleCount; index++)
            {
                float height = route.SampleAt(index).y;
                lowestPoint = Mathf.Min(lowestPoint, height);
                highestPoint = Mathf.Max(highestPoint, height);
            }

            Debug.Log(
                $"[CargoStack] Stage03 경로: 길이 {route.TotalLength:0.0}m, "
                + $"언덕 {hills}개, 구덩이 {pits}개, "
                + $"최저점 {lowestPoint:0.0}m, 최고점 {highestPoint:0.0}m");
            Assert.AreEqual(1, hills, "언덕이 한 구간으로 이어지지 않았다");
            Assert.AreEqual(1, pits, "구덩이가 한 구간으로 이어지지 않았다");
            Assert.That(firstPit, Is.GreaterThan(route.SampleCount * 0.2f),
                "구덩이 전에 충분한 평지가 없다");
            Assert.That(firstHill, Is.GreaterThan(lastPit),
                "구덩이보다 언덕이 먼저 나온다");
            Assert.That(firstHill - lastPit, Is.GreaterThan(route.SampleCount * 0.1f),
                "구덩이와 언덕 사이에 충분한 평지가 없다");
            Assert.That(route.SampleCount - 1 - lastHill, Is.GreaterThan(route.SampleCount * 0.2f),
                "언덕 뒤에 충분한 평지가 없다");
            Assert.That(Mathf.Abs(route.SampleAt((lastPit + firstHill) / 2).y), Is.LessThan(0.25f),
                "구덩이와 언덕 사이가 평지가 아니다");

            int boxes = 0;
            int barrels = 0;
            foreach (Cargo item in cargo)
            {
                boxes += item.GetComponent<BoxCollider>() != null ? 1 : 0;
                barrels += item.GetComponent<CapsuleCollider>() != null ? 1 : 0;
            }

            Assert.AreEqual(6, boxes, "박스 화물은 여섯 개여야 한다");
            Assert.AreEqual(3, barrels, "드럼통 화물은 세 개여야 한다");
        }

        [UnityTest]
        public IEnumerator 네번째_스테이지는_복합_코스에_대형_박스를_포함한_다양한_화물_여덟개를_만든다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage04_ComplexRoute",
                LoadSceneMode.Single);

            StageContext context = Object.FindAnyObjectByType<StageContext>();
            RoutePath route = Object.FindAnyObjectByType<RoutePath>();
            Cargo[] cargo = Object.FindObjectsByType<Cargo>();

            Assert.NotNull(context, "StageContext가 없다");
            Assert.NotNull(context.Definition, "스테이지 정의가 연결되지 않았다");
            Assert.AreEqual("stage-04", context.Definition.StageId);
            Assert.AreEqual(SceneManager.GetActiveScene().name, context.Definition.SceneName);
            Assert.NotNull(route, "경로가 생성되지 않았다");
            Assert.That(route.TotalLength, Is.GreaterThan(180f));
            Assert.AreEqual(8, cargo.Length, "Stage 04 화물은 여덟 개여야 한다");
            Assert.IsTrue(
                context.Definition.Cargo[0].StretchToMaximumSize,
                "첫 화물의 대형 박스 비균등 크기 조절이 꺼져 있다");

            int hills = CountHeightRegions(route, pointHeight => pointHeight > 2f);
            int pits = CountHeightRegions(route, pointHeight => pointHeight < -1f);
            float lowestZ = float.PositiveInfinity;
            float highestZ = float.NegativeInfinity;
            for (int index = 0; index < route.SampleCount; index++)
            {
                float lateral = route.SampleAt(index).z;
                lowestZ = Mathf.Min(lowestZ, lateral);
                highestZ = Mathf.Max(highestZ, lateral);
            }

            Debug.Log(
                $"[CargoStack] Stage04 경로: 길이 {route.TotalLength:0.0}m, "
                + $"언덕 {hills}개, 구덩이 {pits}개, 좌우 변화 {highestZ - lowestZ:0.0}m");
            Assert.That(hills, Is.GreaterThanOrEqualTo(2), "복합 코스에 언덕이 충분하지 않다");
            Assert.That(pits, Is.GreaterThanOrEqualTo(2), "복합 코스에 구덩이가 충분하지 않다");
            Assert.That(highestZ - lowestZ, Is.GreaterThan(12f), "S자 좌우 굴곡이 충분하지 않다");

            int cardboardBoxes = 0;
            int barrels = 0;
            int busts = 0;
            int lamps = 0;
            BoxCollider largeBox = null;
            foreach (Cargo item in cargo)
            {
                foreach (Transform child in item.transform)
                {
                    switch (child.name)
                    {
                        case "ImportedVisual_CardboardBox":
                            cardboardBoxes++;
                            BoxCollider box = item.GetComponent<BoxCollider>();
                            if (largeBox == null || box.size.y > largeBox.size.y)
                            {
                                largeBox = box;
                            }
                            break;
                        case "ImportedVisual_BlueBarrel":
                            barrels++;
                            break;
                        case "ImportedVisual_MarbleBust":
                            busts++;
                            break;
                        case "ImportedVisual_FloorLamp":
                            lamps++;
                            break;
                    }
                }
            }

            Assert.AreEqual(2, cardboardBoxes, "상자 화물은 두 개여야 한다");
            Assert.AreEqual(2, barrels, "드럼통 화물은 두 개여야 한다");
            Assert.AreEqual(2, busts, "대리석 흉상 화물은 두 개여야 한다");
            Assert.AreEqual(2, lamps, "스탠드 조명 화물은 두 개여야 한다");
            Assert.NotNull(largeBox, "대형 박스가 없다");
            Rigidbody largeBoxBody = largeBox.GetComponent<Rigidbody>();
            Debug.Log(
                $"[CargoStack] Stage04 대형 박스: 크기 "
                + $"{largeBox.size.x:0.00}×{largeBox.size.y:0.00}×{largeBox.size.z:0.00}m, "
                + $"질량 {largeBoxBody.mass:0.0}kg");
            Assert.That(largeBox.size.x, Is.GreaterThan(1f), "대형 박스 폭이 충분하지 않다");
            Assert.That(largeBox.size.y, Is.GreaterThan(1.9f), "대형 박스가 냉장고처럼 길쭉하지 않다");

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            PlayerCargoInteractor interactor =
                Object.FindAnyObjectByType<PlayerCargoInteractor>();
            player.SetWorldPose(
                largeBox.transform.position + Vector3.back,
                Quaternion.identity,
                Vector3.zero);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(interactor.TryPickUp(largeBox.GetComponent<Cargo>()),
                "대형 박스를 집을 수 없다");
            interactor.DropHeldCargo();
        }

        [UnityTest]
        public IEnumerator 원통_화물도_집을_수_있다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Stage02_SpeedBumps",
                LoadSceneMode.Single);

            Cargo cylinder = null;
            foreach (Cargo item in Object.FindObjectsByType<Cargo>())
            {
                if (item.GetComponent<CapsuleCollider>() != null)
                {
                    cylinder = item;
                    break;
                }
            }

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            PlayerCargoInteractor interactor =
                Object.FindAnyObjectByType<PlayerCargoInteractor>();

            Assert.NotNull(cylinder, "원통 화물이 없다");
            Assert.NotNull(player, "플레이어가 없다");
            Assert.NotNull(interactor, "화물 상호작용기가 없다");

            player.SetWorldPose(
                cylinder.transform.position + Vector3.back,
                Quaternion.identity,
                Vector3.zero);
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(interactor.TryPickUp(cylinder), "CapsuleCollider 원통을 집지 못했다");
            Assert.AreSame(cylinder, interactor.HeldCargo);
            interactor.DropHeldCargo();
        }

        private static int CountHeightRegions(
            RoutePath route,
            System.Func<float, bool> belongsToRegion)
        {
            int regions = 0;
            bool wasInside = false;
            for (int index = 0; index < route.SampleCount; index++)
            {
                bool isInside = belongsToRegion(route.SampleAt(index).y);
                if (isInside && !wasInside)
                {
                    regions++;
                }

                wasInside = isInside;
            }

            return regions;
        }

        private static int FindHeightSample(
            RoutePath route,
            System.Func<float, bool> matches,
            bool searchBackward = false)
        {
            int index = searchBackward ? route.SampleCount - 1 : 0;
            int end = searchBackward ? -1 : route.SampleCount;
            int step = searchBackward ? -1 : 1;
            for (; index != end; index += step)
            {
                if (matches(route.SampleAt(index).y))
                {
                    return index;
                }
            }

            return -1;
        }
    }
}

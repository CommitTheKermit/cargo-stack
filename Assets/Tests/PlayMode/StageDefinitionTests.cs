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
            Assert.That(route.TotalLength, Is.GreaterThan(80f));
            Assert.NotNull(truck, "트럭이 생성되지 않았다");
            Assert.AreEqual(context.Definition.CargoCount, cargo.Length);
            Assert.AreEqual(6, cargo.Length, "튜토리얼 화물 구성이 달라졌다");

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
    }
}

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    public class MainMenuTests
    {
        [UnityTest]
        public IEnumerator 메인_메뉴는_플레이할_스테이지만_순서대로_보여준다()
        {
            yield return SceneManager.LoadSceneAsync(
                "MainMenu",
                LoadSceneMode.Single);

            MainMenuController menu =
                Object.FindAnyObjectByType<MainMenuController>();

            Assert.NotNull(menu, "MainMenuController가 없다");
            Assert.AreEqual(7, menu.StageCount);
            Assert.AreEqual("stage-01", menu.GetStage(0).StageId);
            Assert.AreEqual("stage-02", menu.GetStage(1).StageId);
            Assert.AreEqual("stage-03", menu.GetStage(2).StageId);
            Assert.AreEqual("stage-04", menu.GetStage(3).StageId);
            Assert.AreEqual("stage-05", menu.GetStage(4).StageId);
            Assert.AreEqual("stage-06", menu.GetStage(5).StageId);
            Assert.AreEqual("stage-07", menu.GetStage(6).StageId);
            Assert.IsFalse(menu.GetStage(0).StageId == "prototype");
        }

        [UnityTest]
        public IEnumerator 메인_메뉴는_화물을_쌓고_자동으로_주행한다()
        {
            yield return SceneManager.LoadSceneAsync(
                "MainMenu",
                LoadSceneMode.Single);

            MenuBackgroundDemo demo =
                Object.FindAnyObjectByType<MenuBackgroundDemo>();
            Assert.NotNull(demo, "메뉴에 실시간 배경 데모가 없다");
            Assert.AreEqual(4, demo.DisplayedCargoCount);

            float timeout = 6f;
            while (!demo.IsDriving && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(demo.IsDriving, "화물 적재 뒤 자동 주행을 시작하지 않았다");
            Assert.IsTrue(
                Object.FindAnyObjectByType<DioramaCamera>().GetComponent<Camera>().enabled,
                "메뉴 배경용 디오라마 카메라가 꺼져 있다");
        }

        [UnityTest]
        public IEnumerator 메인_메뉴에서_첫_스테이지를_선택하면_해당_씬을_연다()
        {
            yield return SceneManager.LoadSceneAsync(
                "MainMenu",
                LoadSceneMode.Single);

            MainMenuController menu =
                Object.FindAnyObjectByType<MainMenuController>();
            Assert.NotNull(menu, "MainMenuController가 없다");
            menu.LoadStage(0);
            yield return null;

            Assert.AreEqual(
                "Stage01_Tutorial",
                SceneManager.GetActiveScene().name);
            StageContext context = Object.FindAnyObjectByType<StageContext>();
            Assert.NotNull(context);
            Assert.AreEqual("stage-01", context.Definition.StageId);
            Assert.NotNull(
                Object.FindAnyObjectByType<PrototypeHud>().UiFont,
                "HUD에 Pretendard가 연결되지 않았다");
            Assert.NotNull(
                Object.FindAnyObjectByType<ResultScreen>().UiFont,
                "결과 화면에 Pretendard가 연결되지 않았다");
        }

        [UnityTest]
        public IEnumerator 게임_흐름에서_메인_메뉴로_돌아가면_스테이지_선택_씬을_연다()
        {
            yield return SceneManager.LoadSceneAsync(
                "Prototype",
                LoadSceneMode.Single);

            GameFlow flow = Object.FindAnyObjectByType<GameFlow>();
            Assert.NotNull(flow);

            flow.ReturnToMainMenu();
            yield return null;

            Assert.AreEqual(
                "MainMenu",
                SceneManager.GetActiveScene().name);
            Assert.NotNull(
                Object.FindAnyObjectByType<MainMenuController>());
        }
    }
}

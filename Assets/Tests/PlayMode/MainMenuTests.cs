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

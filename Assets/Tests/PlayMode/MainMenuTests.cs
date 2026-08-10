using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
            Assert.AreEqual(7, menu.ButtonCount, "이미지 스테이지 버튼 수가 다르다");
            Assert.NotNull(GameObject.Find("Main Menu UI"), "이미지 기반 메인 메뉴 Canvas가 없다");
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
            yield return null;
            GameObject.Find("Stage Button 01").GetComponent<Button>().onClick.Invoke();
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

            TutorialGuide guide = Object.FindAnyObjectByType<TutorialGuide>();
            Assert.NotNull(guide, "Stage 01에 이미지 튜토리얼이 없다");
            Assert.IsTrue(guide.IsLoadingPanelVisible, "적재 튜토리얼 패널이 보이지 않는다");
            Assert.IsFalse(guide.IsDrivingPanelVisible, "출발 전에 주행 튜토리얼 패널이 보인다");
            Assert.AreEqual(6, guide.LoadingStepCount, "적재 튜토리얼 단계 수가 다르다");
            Assert.AreEqual("(0 / 6)", guide.CargoCountText, "화물 적재 카운트가 실제 수량과 다르다");
            PrototypeHud hud = Object.FindAnyObjectByType<PrototypeHud>();
            Assert.IsTrue(hud.enabled, "Stage 01에서 Enter 출발 입력이 꺼져 있다");
            Assert.IsFalse(hud.IsUiVisible,
                "Stage 01에서 기존 IMGUI HUD가 튜토리얼 위젯과 겹친다");

            TruckTailgate tailgate = Object.FindAnyObjectByType<TruckTailgate>();
            tailgate.SetOpenInstantly(true);
            yield return null;
            Assert.AreEqual(1, guide.LoadingCompletedCount,
                "화물칸 문을 열어도 튜토리얼 단계가 완료되지 않는다");

            GameFlow flow = Object.FindAnyObjectByType<GameFlow>();
            flow.StartDriving();
            float timeout = 1f;
            while (flow.State == GameState.Loading && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(GameState.Driving, flow.State,
                "체크리스트를 완료하지 않으면 출발할 수 없다");
            Assert.IsFalse(guide.IsLoadingPanelVisible, "출발 뒤에도 적재 패널이 남아 있다");
            Assert.IsTrue(guide.IsDrivingPanelVisible, "출발 뒤 주행 패널로 전환되지 않았다");
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

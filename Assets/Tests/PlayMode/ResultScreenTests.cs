using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CargoStack.Tests
{
    /// <summary>
    /// 결과 화면의 별 등장 연출을 고정한다.
    ///
    /// 이 곡선이 깨지면 별이 안 보이거나(0 에서 안 자람) 커진 채로 남는(1 로 안 돌아옴)
    /// 눈에 띄는 고장이 나는데, 둘 다 실행해 보기 전에는 모르는 종류라 수치로 잡아 둔다.
    /// </summary>
    public class ResultScreenTests
    {
        private const float OvershootScale = 1.3f;

        [Test]
        public void 시작할_때는_크기가_0이다()
        {
            Assert.That(ResultScreen.PopScale(0f), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void 끝나면_제_크기로_돌아온다()
        {
            Assert.That(ResultScreen.PopScale(1f), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void 중간에_제_크기를_넘어섰다_돌아온다()
        {
            float peak = 0f;
            for (int step = 0; step <= 100; step++)
            {
                peak = Mathf.Max(peak, ResultScreen.PopScale(step / 100f));
            }

            Assert.That(peak, Is.EqualTo(OvershootScale).Within(0.01f),
                "오버슛이 사라졌다. 별이 통통 튀지 않고 밋밋하게 커진다");
        }

        [Test]
        public void 커지는_구간에서는_줄어들지_않는다()
        {
            float previous = 0f;
            for (int step = 0; step <= 60; step++)
            {
                float current = ResultScreen.PopScale(step / 100f);
                Assert.That(current, Is.GreaterThanOrEqualTo(previous - 0.001f),
                    $"진행도 {step / 100f:0.00} 에서 별이 도로 작아졌다");
                previous = current;
            }
        }

        [Test]
        public void 범위를_벗어난_진행도도_제_크기로_묶인다()
        {
            Assert.That(ResultScreen.PopScale(-1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(ResultScreen.PopScale(4f), Is.EqualTo(1f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator 씬에_결과_화면이_배선되어_있다()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);

            ResultScreen screen = Object.FindFirstObjectByType<ResultScreen>();
            Assert.NotNull(screen, "씬에 ResultScreen 이 없다. 결과가 디버그 HUD 텍스트로만 보인다");
            Assert.NotNull(screen.GetComponent<AudioSource>(),
                "결과 화면에 AudioSource 가 없어 별 소리를 낼 수 없다");
        }
    }
}

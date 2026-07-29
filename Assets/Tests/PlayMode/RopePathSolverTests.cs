using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CargoStack.Tests
{
    /// <summary>
    /// 팽팽한 로프가 어디를 지나는지는 물리 없이 정해진다. 그 계산만 따로 고정한다.
    /// 씬도 물리 엔진도 필요 없으므로 실패하면 원인이 계산 자체에 있다.
    /// </summary>
    public class RopePathSolverTests
    {
        [Test]
        public void 사이에_아무것도_없으면_두_끝만_남는다()
        {
            List<Vector2> line = RopePathSolver.SolveTautLine(new[]
            {
                new Vector2(0f, 1f),
                new Vector2(1f, 0f),
                new Vector2(2f, 0f),
                new Vector2(3f, 1f),
            });

            Assert.AreEqual(2, line.Count, "가운데가 비었는데 로프가 처졌다");
            Assert.AreEqual(new Vector2(0f, 1f), line[0]);
            Assert.AreEqual(new Vector2(3f, 1f), line[1]);
        }

        [Test]
        public void 사이에_솟은_짐이_있으면_그_위를_타고_넘는다()
        {
            List<Vector2> line = RopePathSolver.SolveTautLine(new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 2f),
                new Vector2(2f, 0f),
            });

            Assert.AreEqual(3, line.Count, "솟은 짐 위를 지나지 않았다");
            Assert.AreEqual(new Vector2(1f, 2f), line[1], "봉우리를 짚지 않았다");
        }

        [Test]
        public void 짐_윗면처럼_평평한_봉우리는_양_모서리만_짚는다()
        {
            List<Vector2> line = RopePathSolver.SolveTautLine(new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 1f),
                new Vector2(3f, 1f),
                new Vector2(4f, 0f),
            });

            // 윗면 가운데 점은 로프가 지나되 꺾이지 않는 자리다. 꺾임점만 남아야 한다.
            Assert.AreEqual(4, line.Count, "평평한 윗면에서 불필요한 꺾임점이 남았다");
            Assert.AreEqual(new Vector2(1f, 1f), line[1]);
            Assert.AreEqual(new Vector2(3f, 1f), line[2]);
        }

        [Test]
        public void 두_짐_사이의_골은_로프가_닿지_않아_건너뛴다()
        {
            List<Vector2> line = RopePathSolver.SolveTautLine(new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 2f),
                new Vector2(2f, 0.5f),
                new Vector2(3f, 2f),
                new Vector2(4f, 0f),
            });

            Assert.AreEqual(4, line.Count, "두 짐 사이 골에 로프가 내려앉았다");
            Assert.AreEqual(new Vector2(1f, 2f), line[1]);
            Assert.AreEqual(new Vector2(3f, 2f), line[2]);
        }

        [Test]
        public void 두_부착점은_어떤_경우에도_남는다()
        {
            // 양쪽 부착점보다 사이가 훨씬 높은, 로프가 거의 다 들리는 배치.
            List<Vector2> line = RopePathSolver.SolveTautLine(new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 5f),
                new Vector2(2f, 0f),
            });

            Assert.AreEqual(new Vector2(0f, 0f), line[0], "시작 매듭이 사라졌다");
            Assert.AreEqual(new Vector2(2f, 0f), line[line.Count - 1], "끝 매듭이 사라졌다");
        }

        [Test]
        public void 표본이_없으면_빈_선을_준다()
        {
            Assert.IsEmpty(RopePathSolver.SolveTautLine(null));
            Assert.IsEmpty(RopePathSolver.SolveTautLine(new Vector2[0]));
        }
    }
}

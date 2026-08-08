using System.Collections.Generic;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 팽팽하게 걸린 로프가 그리는 선을 구한다.
    ///
    /// 두 점 사이에 로프를 걸고 당기면, 로프는 사이에 놓인 짐 위를 타고 넘는 최단선이 된다.
    /// 그 선은 두 점을 지나는 수직 평면에서 잰 장애물 윗면 높이의 <b>위쪽 볼록 껍질</b>과 같다.
    /// 오목하게 파인 자리는 로프가 닿지 않고 뜨는 자리라, 당기면 그 구간이 직선으로 펴지기 때문이다.
    ///
    /// 높이를 어떻게 재는지(레이캐스트)는 여기서 모른다. 표본만 받는다.
    /// 그래서 물리 엔진도 씬도 없이 단위 테스트할 수 있다.
    /// </summary>
    public static class RopePathSolver
    {
        /// <summary>
        /// 진행거리-높이 표본에서 팽팽한 선의 꺾임점만 남긴다.
        ///
        /// 표본은 진행거리(x) 오름차순이어야 하며, 첫 표본과 마지막 표본이 두 부착점이다.
        /// 두 부착점은 어떤 경우에도 결과에 남는다. 로프가 거기 묶여 있기 때문이다.
        /// </summary>
        public static List<Vector2> SolveTautLine(IReadOnlyList<Vector2> heightProfile)
        {
            var tautLine = new List<Vector2>();
            if (heightProfile == null)
            {
                return tautLine;
            }

            for (int index = 0; index < heightProfile.Count; index++)
            {
                Vector2 sample = heightProfile[index];

                // 직전 두 점과 새 점이 위로 꺾이면(오목하면) 가운데 점은 로프가 닿지 않는 자리다.
                // 마지막 표본은 부착점이므로 그 앞의 점들만 걷어낸다.
                while (tautLine.Count >= 2
                    && !TurnsDownward(tautLine[tautLine.Count - 2], tautLine[tautLine.Count - 1], sample))
                {
                    tautLine.RemoveAt(tautLine.Count - 1);
                }

                tautLine.Add(sample);
            }

            return tautLine;
        }

        /// <summary>
        /// previous → middle → next 로 갈 때 진행 방향이 아래로 꺾이는가.
        /// 꺾인다면 middle 은 봉우리이고, 팽팽한 로프가 그 위에 얹힌다.
        /// </summary>
        private static bool TurnsDownward(Vector2 previous, Vector2 middle, Vector2 next)
        {
            float cross = (middle.x - previous.x) * (next.y - previous.y)
                - (middle.y - previous.y) * (next.x - previous.x);
            return cross < 0f;
        }
    }
}

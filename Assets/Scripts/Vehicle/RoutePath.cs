using System;
using System.Collections.Generic;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 트럭이 따라 달리는 도로 중심선. 제어점을 Catmull-Rom 으로 이어 촘촘한 폴리라인으로 펴 둔다.
    /// 이 선이 곧 도로 표면이다. 도로 블록은 이 선 아래로 매달아 깔고, 트럭은 이 선 위를 달린다.
    ///
    /// 경사를 직선 램프로 이어 붙이지 않고 곡선으로 만드는 이유는 두 가지다.
    /// 하나는 눈에 보이는 것으로, 각진 이음매가 없어야 길처럼 보인다.
    /// 다른 하나는 물리로, 마루와 골짜기의 곡률이 그 자체로 난이도를 만든다.
    /// 마루를 반경 R 로 넘으면 짐에 걸리는 유효 중력이 g - v²/R 로 줄어 마찰이 그만큼 약해지고,
    /// 골짜기에서는 반대로 g + v²/R 로 늘어 짐이 눌린다. 램프를 직선으로 이으면 이 효과가
    /// 이음매 한 점에 충격으로 뭉치지만, 곡선으로 이으면 구간 전체에 고르게 퍼진다.
    /// </summary>
    public class RoutePath : MonoBehaviour
    {
        [Tooltip("도로 중심선 제어점. 곡선이 이 점들을 모두 지나간다.")]
        [SerializeField] private Vector3[] controlPoints = Array.Empty<Vector3>();

        [Tooltip("제어점 사이를 몇 조각으로 나눠 펼지. 클수록 도로 메시가 매끄럽다.")]
        [SerializeField] private int samplesPerSegment = 64;

        private Vector3[] points;
        private float[] travel;
        private int[] controlSample;

        /// <summary>경로 전체 길이(m).</summary>
        public float TotalLength
        {
            get
            {
                EnsureBuilt();
                return travel[travel.Length - 1];
            }
        }

        public int SampleCount
        {
            get
            {
                EnsureBuilt();
                return points.Length;
            }
        }

        public void SetControlPoints(Vector3[] value)
        {
            controlPoints = value ?? Array.Empty<Vector3>();
            points = null;
        }

        public Vector3 SampleAt(int index)
        {
            EnsureBuilt();
            return points[index];
        }

        /// <summary>제어점까지의 누적 거리. 속도 프로필의 구간을 잡을 때 기준으로 쓴다.</summary>
        public float DistanceAtControlPoint(int index)
        {
            EnsureBuilt();
            return travel[controlSample[Mathf.Clamp(index, 0, controlSample.Length - 1)]];
        }

        /// <summary>
        /// 경로를 따라 <paramref name="distance"/> 만큼 간 지점. 트럭 앞뒤 축이 경로 밖을 짚는 일이
        /// 있으므로 양 끝은 자르지 않고 직선으로 늘려 준다.
        /// </summary>
        public Vector3 PositionAt(float distance)
        {
            EnsureBuilt();

            int low = 0;
            int high = travel.Length - 1;
            while (low + 1 < high)
            {
                int mid = (low + high) / 2;
                if (travel[mid] <= distance)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            float span = travel[high] - travel[low];
            float t = span > 1e-5f ? (distance - travel[low]) / span : 0f;
            return Vector3.LerpUnclamped(points[low], points[high], t);
        }

        private void EnsureBuilt()
        {
            if (points != null)
            {
                return;
            }

            if (controlPoints.Length < 2)
            {
                throw new InvalidOperationException("RoutePath 에 제어점이 2개 미만이다. 경로를 만들 수 없다.");
            }

            var built = new List<Vector3>(controlPoints.Length * samplesPerSegment + 1);
            var anchors = new int[controlPoints.Length];

            for (int segment = 0; segment < controlPoints.Length - 1; segment++)
            {
                // 양 끝 제어점은 자기 자신을 이웃으로 삼아 곡선이 시작·끝에서 튀지 않게 한다.
                Vector3 previous = controlPoints[Mathf.Max(segment - 1, 0)];
                Vector3 start = controlPoints[segment];
                Vector3 end = controlPoints[segment + 1];
                Vector3 next = controlPoints[Mathf.Min(segment + 2, controlPoints.Length - 1)];

                anchors[segment] = built.Count;
                for (int step = 0; step < samplesPerSegment; step++)
                {
                    built.Add(CatmullRom(previous, start, end, next, (float)step / samplesPerSegment));
                }
            }

            anchors[controlPoints.Length - 1] = built.Count;
            built.Add(controlPoints[controlPoints.Length - 1]);

            points = built.ToArray();
            controlSample = anchors;

            travel = new float[points.Length];
            for (int i = 1; i < points.Length; i++)
            {
                travel[i] = travel[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            }
        }

        private static Vector3 CatmullRom(Vector3 previous, Vector3 start, Vector3 end, Vector3 next, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (2f * start
                + (end - previous) * t
                + (2f * previous - 5f * start + 4f * end - next) * t2
                + (-previous + 3f * start - 3f * end + next) * t3);
        }
    }
}

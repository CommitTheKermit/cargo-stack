using System;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 로프 한 가닥의 물리 규격.
    ///
    /// 세그먼트 질량은 기획서 4.1 의 "질량 비율 10:1 이내"를 짐(기본 20kg)과의 사이에서 지켜야 한다.
    /// 너무 가벼우면 무거운 짐이 밀 때 로프가 늘어나 고정 장비 구실을 못 한다.
    /// </summary>
    [Serializable]
    public sealed class RopeSettings
    {
        [Tooltip("세그먼트 하나의 목표 길이. 짧을수록 로프가 부드럽지만, 마디마다 생기는 늘어남이 누적된다.")]
        [SerializeField, Min(0.02f)] private float segmentLength = 0.26f;

        [Tooltip("로프 굵기(반지름). 짐 위에 얹혔을 때 파묻히지 않을 만큼은 되어야 한다.")]
        [SerializeField, Min(0.005f)] private float radius = 0.035f;

        [Tooltip("로프 한 가닥 전체의 질량. 세그먼트들이 나눠 갖는다. 짐보다 지나치게 가벼우면 그냥 끌려간다.")]
        [SerializeField, Min(0.1f)] private float totalMass = 10f;

        [Tooltip("팽팽한 선을 찾을 때 높이를 재는 간격. 촘촘할수록 짐 모서리를 정확히 탄다.")]
        [SerializeField, Min(0.02f)] private float heightSampleSpacing = 0.08f;

        [Tooltip("한 가닥의 최대 길이. 이보다 먼 두 점은 이을 수 없다.")]
        [SerializeField, Min(0.5f)] private float maximumLength = 8f;

        [Tooltip("로프 색.")]
        [SerializeField] private Color color = new Color(0.86f, 0.68f, 0.30f);

        public float SegmentLength => segmentLength;
        public float Radius => radius;
        public float TotalMass => totalMass;
        public float HeightSampleSpacing => heightSampleSpacing;
        public float MaximumLength => maximumLength;
        public Color Color => color;
    }
}

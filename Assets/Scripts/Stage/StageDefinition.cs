using System;
using System.Collections.Generic;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 씬을 구성할 때 달라지는 값만 담는다.
    /// 차량 규격, 물리 재질, 카메라처럼 모든 스테이지가 공유하는 규칙은 씬 빌더에 남긴다.
    /// </summary>
    [CreateAssetMenu(fileName = "StageDefinition", menuName = "CargoStack/Stage Definition")]
    public sealed class StageDefinition : ScriptableObject
    {
        [SerializeField] private string stageId = "stage";
        [SerializeField] private string sceneName = "Stage";
        [SerializeField] private StageTheme theme = StageTheme.Default;
        [SerializeField] private bool showInMenu = true;
        [SerializeField] private string displayName = "새 스테이지";
        [SerializeField, TextArea] private string menuDescription = "";
        [SerializeField] private Vector3[] routeControlPoints = Array.Empty<Vector3>();
        [SerializeField] private float truckStartDistance = 8f;
        [SerializeField] private float goalOffsetFromEnd = 6f;
        [SerializeField] private float maxSpeed = 10f;
        [SerializeField] private AnimationCurve speedOverProgress =
            AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [SerializeField] private StageCargoDefinition[] cargo = Array.Empty<StageCargoDefinition>();

        [Tooltip("이 스테이지에서 쓸 수 있는 로프 개수. 0이면 배치만으로 풀어야 한다.")]
        [SerializeField, Min(0)] private int ropeCount = 2;

        public string StageId => stageId;
        public string SceneName => sceneName;
        public StageTheme Theme => theme;
        public bool ShowInMenu => showInMenu;
        public string DisplayName => displayName;
        public string MenuDescription => menuDescription;
        public float TruckStartDistance => truckStartDistance;
        public float GoalOffsetFromEnd => goalOffsetFromEnd;
        public float MaxSpeed => maxSpeed;
        public IReadOnlyList<StageCargoDefinition> Cargo => cargo;
        public int CargoCount => cargo.Length;
        public int RopeCount => ropeCount;

        public Vector3[] CopyRouteControlPoints()
        {
            return (Vector3[])routeControlPoints.Clone();
        }

        public AnimationCurve CopySpeedOverProgress()
        {
            var copy = new AnimationCurve(speedOverProgress.keys)
            {
                preWrapMode = speedOverProgress.preWrapMode,
                postWrapMode = speedOverProgress.postWrapMode,
            };
            return copy;
        }

        public void ValidateOrThrow()
        {
            if (string.IsNullOrWhiteSpace(stageId))
            {
                throw new InvalidOperationException($"{name}: stageId가 비어 있다.");
            }

            if (string.IsNullOrWhiteSpace(sceneName)
                || sceneName.Contains("/")
                || sceneName.Contains("\\"))
            {
                throw new InvalidOperationException($"{name}: sceneName은 폴더가 아닌 파일 이름이어야 한다.");
            }

            if (showInMenu && string.IsNullOrWhiteSpace(displayName))
            {
                throw new InvalidOperationException($"{name}: 메뉴 표시 이름이 비어 있다.");
            }

            if (routeControlPoints == null || routeControlPoints.Length < 2)
            {
                throw new InvalidOperationException($"{name}: 경로 제어점이 2개 미만이다.");
            }

            if (truckStartDistance < 0f || goalOffsetFromEnd < 0f)
            {
                throw new InvalidOperationException($"{name}: 시작 거리와 도착 여백은 음수일 수 없다.");
            }

            if (maxSpeed <= 0f)
            {
                throw new InvalidOperationException($"{name}: 최고 속도는 0보다 커야 한다.");
            }

            if (speedOverProgress == null || speedOverProgress.length < 2)
            {
                throw new InvalidOperationException($"{name}: 속도 곡선에는 키가 2개 이상 필요하다.");
            }

            if (cargo == null || cargo.Length == 0)
            {
                throw new InvalidOperationException($"{name}: 화물이 하나 이상 필요하다.");
            }

            if (ropeCount < 0)
            {
                throw new InvalidOperationException($"{name}: 로프 개수는 음수일 수 없다.");
            }

            for (int index = 0; index < cargo.Length; index++)
            {
                cargo[index]?.ValidateOrThrow(name, index);
                if (cargo[index] == null)
                {
                    throw new InvalidOperationException($"{name}: {index}번 화물 정의가 비어 있다.");
                }
            }
        }
    }

    public enum StageTheme
    {
        Default,
        Winter,
    }

    /// <summary>
    /// 가져온 화물 모델과 그 모델을 감쌀 물리 프록시의 최대 크기·질량.
    /// 스폰 배치는 현재 공통 적재장 격자를 사용하고, 실제로 다른 배치가 필요해질 때만 데이터화한다.
    /// </summary>
    [Serializable]
    public sealed class StageCargoDefinition
    {
        [SerializeField] private string assetName;
        [SerializeField] private Vector3 maximumSize = Vector3.one;
        [SerializeField] private float mass = 20f;
        [SerializeField] private StageCargoColliderShape colliderShape =
            StageCargoColliderShape.Box;

        [Tooltip("화물 표면의 접지 특성. Slippery는 얼음 화물처럼 짐칸에서 쉽게 미끄러진다.")]
        [SerializeField] private StageCargoSurfaceType surfaceType = StageCargoSurfaceType.Standard;

        [Tooltip("모델 원래 비율을 무시하고 maximumSize에 정확히 맞춘다. 대형 가전 박스처럼 길쭉한 화물에만 사용한다.")]
        [SerializeField] private bool stretchToMaximumSize;

        [Tooltip("깨질 수 있는 화물인지. 충격 속도가 breakImpactSpeed 이상이면 부서진다.")]
        [SerializeField] private bool isFragile;

        [Tooltip("이 속도(m/s) 이상의 충격을 받으면 부서진다. isFragile일 때만 쓰인다.")]
        [SerializeField] private float breakImpactSpeed = 8f;

        public string AssetName => assetName;
        public Vector3 MaximumSize => maximumSize;
        public float Mass => mass;
        public StageCargoColliderShape ColliderShape => colliderShape;
        public StageCargoSurfaceType SurfaceType => surfaceType;
        public bool StretchToMaximumSize => stretchToMaximumSize;
        public bool IsFragile => isFragile;
        public float BreakImpactSpeed => breakImpactSpeed;

        internal void ValidateOrThrow(string stageName, int index)
        {
            if (string.IsNullOrWhiteSpace(assetName)
                || assetName.Contains("/")
                || assetName.Contains("\\"))
            {
                throw new InvalidOperationException(
                    $"{stageName}: {index}번 화물의 assetName이 올바르지 않다.");
            }

            if (maximumSize.x <= 0f || maximumSize.y <= 0f || maximumSize.z <= 0f)
            {
                throw new InvalidOperationException(
                    $"{stageName}: {index}번 화물의 최대 크기는 모두 0보다 커야 한다.");
            }

            if (mass <= 0f)
            {
                throw new InvalidOperationException(
                    $"{stageName}: {index}번 화물의 질량은 0보다 커야 한다.");
            }

            if (!Enum.IsDefined(typeof(StageCargoColliderShape), colliderShape))
            {
                throw new InvalidOperationException(
                    $"{stageName}: {index}번 화물의 충돌 형태가 올바르지 않다.");
            }

            if (!Enum.IsDefined(typeof(StageCargoSurfaceType), surfaceType))
            {
                throw new InvalidOperationException(
                    $"{stageName}: {index}번 화물의 표면 형태가 올바르지 않다.");
            }

            if (isFragile && breakImpactSpeed <= 0f)
            {
                throw new InvalidOperationException(
                    $"{stageName}: {index}번 화물의 파손 충격 속도는 0보다 커야 한다.");
            }
        }
    }

    public enum StageCargoColliderShape
    {
        Box,
        Capsule,
    }

    public enum StageCargoSurfaceType
    {
        Standard,
        Slippery,
    }
}

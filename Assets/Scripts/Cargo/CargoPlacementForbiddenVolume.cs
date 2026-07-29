using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 화물을 놓을 수 없는 차량 내부 공간을 표시한다.
    /// 이 컴포넌트가 붙은 Collider는 배치 레이의 지지면이 되거나 미리보기 프록시와
    /// 겹칠 경우 모두 배치 대상에서 제외된다. 물리 충돌은 그대로 유지한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class CargoPlacementForbiddenVolume : MonoBehaviour
    {
    }
}

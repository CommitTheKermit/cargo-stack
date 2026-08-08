namespace CargoStack
{
    /// <summary>
    /// 핵심 루프의 세 단계(기획서 2장). 이 순서로만 진행하며 되돌아가지 않는다.
    /// </summary>
    public enum GameState
    {
        /// <summary>짐칸을 확대해 보여주고 플레이어가 짐을 쌓는 단계.</summary>
        Loading,

        /// <summary>적재가 잠기고 트럭이 정해진 경로를 자동 주행하는 관전 단계.</summary>
        Driving,

        /// <summary>도착 후 남은 짐을 집계해 보여주는 단계.</summary>
        Result,
    }
}

using System;
using UnityEngine;

namespace CargoStack
{
    /// <summary>생성된 씬이 어떤 스테이지 정의에서 왔는지 런타임에 남긴다.</summary>
    [DisallowMultipleComponent]
    public sealed class StageContext : MonoBehaviour
    {
        [SerializeField] private StageDefinition definition;

        public StageDefinition Definition => definition;

        public void Configure(StageDefinition value)
        {
            definition = value != null
                ? value
                : throw new ArgumentNullException(nameof(value));
        }
    }
}

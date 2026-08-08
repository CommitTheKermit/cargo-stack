using UnityEngine;

namespace CargoStack
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class BackgroundMusic : MonoBehaviour
    {
        private void Start()
        {
            // 배치모드 오디오 디코딩은 3배속 물리 테스트의 프레임 타이밍만 흔든다.
            if (!Application.isBatchMode)
            {
                GetComponent<AudioSource>().Play();
            }
        }
    }
}

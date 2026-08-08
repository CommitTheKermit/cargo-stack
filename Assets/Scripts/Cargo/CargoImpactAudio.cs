using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 화물이 다른 화물이나 짐칸에 부딪힐 때 충격 세기에 맞춰 짧은 소리를 낸다.
    /// 작은 접촉과 너무 빠른 연타를 걸러 물리 접점의 잡음을 그대로 재생하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(AudioSource))]
    public sealed class CargoImpactAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip[] impactClips;
        [SerializeField] private float minimumImpactSpeed = 0.65f;
        [SerializeField] private float fullVolumeImpactSpeed = 5f;
        [SerializeField] private float maximumVolume = 0.72f;
        [SerializeField] private float minimumInterval = 0.09f;

        private AudioSource source;
        private float lastPlayedAt = float.NegativeInfinity;

        public int ClipCount => impactClips?.Length ?? 0;

        public void Configure(AudioClip[] clips)
        {
            impactClips = clips;
        }

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0.1f;
            source.minDistance = 1.5f;
            source.maxDistance = 24f;
        }

        private void OnCollisionEnter(Collision collision)
        {
            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactClips == null
                || impactClips.Length == 0
                || impactSpeed < minimumImpactSpeed
                || Time.time - lastPlayedAt < minimumInterval)
            {
                return;
            }

            AudioClip clip = impactClips[Random.Range(0, impactClips.Length)];
            if (clip == null)
            {
                return;
            }

            float strength = Mathf.InverseLerp(
                minimumImpactSpeed,
                fullVolumeImpactSpeed,
                impactSpeed);
            float volume = Mathf.Lerp(0.12f, maximumVolume, strength);

            source.pitch = Random.Range(0.92f, 1.08f);
            source.PlayOneShot(clip, volume);
            lastPlayedAt = Time.time;
        }
    }
}

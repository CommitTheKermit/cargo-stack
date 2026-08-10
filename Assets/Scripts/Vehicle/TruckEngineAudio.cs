using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 트럭 속도를 엔진 루프의 피치와 볼륨으로 들려준다.
    /// 주행 물리는 건드리지 않고 <see cref="TruckMover.Speed01"/>만 읽는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TruckMover), typeof(AudioSource))]
    public sealed class TruckEngineAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip engineLoop;
        [SerializeField] private float idlePitch = 0.78f;
        [SerializeField] private float drivingPitch = 1.35f;
        [SerializeField] private float idleVolume = 0.24f;
        [SerializeField] private float drivingVolume = 0.58f;
        [SerializeField] private float responseSpeed = 5f;

        private TruckMover mover;
        private AudioSource source;
        private AudioSource hornSource;
        private AudioClip hornClip;

        public bool HasClip => engineLoop != null;
        public bool HasHornClip => hornClip != null;

        public void Configure(AudioClip clip)
        {
            engineLoop = clip;
        }

        private void Awake()
        {
            mover = GetComponent<TruckMover>();
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0.15f;
            source.minDistance = 3f;
            source.maxDistance = 45f;

            hornClip = Resources.Load<AudioClip>("Audio/truck_horn");
            hornSource = gameObject.AddComponent<AudioSource>();
            hornSource.playOnAwake = false;
            hornSource.spatialBlend = 1f;
            hornSource.dopplerLevel = 0.15f;
            hornSource.minDistance = 4f;
            hornSource.maxDistance = 50f;
        }

        private void Start()
        {
            if (engineLoop == null)
            {
                return;
            }

            source.clip = engineLoop;
            source.pitch = idlePitch;
            source.volume = idleVolume;
            source.Play();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TryHonk();
            }

            if (source == null || !source.isPlaying)
            {
                return;
            }

            float speed = mover.Speed01;
            float response = 1f - Mathf.Exp(-responseSpeed * Time.deltaTime);
            source.pitch = Mathf.Lerp(source.pitch, Mathf.Lerp(idlePitch, drivingPitch, speed), response);
            source.volume = Mathf.Lerp(source.volume, Mathf.Lerp(idleVolume, drivingVolume, speed), response);
        }

        public bool TryHonk()
        {
            if (!mover.IsDriving || hornClip == null)
            {
                return false;
            }

            hornSource.PlayOneShot(hornClip, 0.8f);
            return true;
        }
    }
}

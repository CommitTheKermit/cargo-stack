using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 화물이 부서졌음을 보여주는 작은 먼지구름. 전용 아트 에셋이 없어 파티클을 코드로 구성한다.
    /// </summary>
    internal static class CargoBreakEffect
    {
        private const int ParticleCount = 10;
        private const float Duration = 0.6f;
        private const float StartLifetime = 0.8f;

        public static void SpawnDustPuff(Vector3 position)
        {
            var puff = new GameObject("CargoBreakDustPuff");
            puff.transform.position = position;

            ParticleSystem system = puff.AddComponent<ParticleSystem>();
            // AddComponent 직후에는 playOnAwake로 이미 재생 중이라, duration 등을 바로 못 바꾼다.
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.duration = Duration;
            main.loop = false;
            main.startLifetime = StartLifetime;
            main.startSpeed = 1.5f;
            main.startSize = 0.35f;
            main.startColor = new Color(0.85f, 0.85f, 0.85f, 0.9f);
            main.gravityModifier = -0.05f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)ParticleCount) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            Renderer renderer = system.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));

            system.Play();
            Object.Destroy(puff, Duration + StartLifetime);
        }
    }
}

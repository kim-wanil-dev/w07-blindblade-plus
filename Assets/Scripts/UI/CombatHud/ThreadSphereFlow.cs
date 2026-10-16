using UnityEngine;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// 실 구체의 두 실이 일정한 속도로 돌지 않도록 각자의 공전 진행 각도를 불규칙하게 적분한다.
    /// 잡음 표류에 지수 분포 간격의 무작위 당김을 더하고, 실마다 평균 속도가 따로 천천히 오르내린다.
    /// </summary>
    public sealed class ThreadSphereFlow
    {
        private const float TWO_PI = Mathf.PI * 2f;

        private readonly float[] _heads = new float[ThreadSphere.THREAD_COUNT];
        private readonly float[] _velocities = new float[ThreadSphere.THREAD_COUNT];
        private readonly float[] _nextTugs = new float[ThreadSphere.THREAD_COUNT];
        private readonly float[] _dampings = new float[ThreadSphere.THREAD_COUNT];
        private readonly float _noiseSeed;

        /// <summary>
        /// noiseSeed로 표류 잡음 줄을 정한다. 구체마다 다른 값을 주면 서로 다르게 흐른다.
        /// </summary>
        public ThreadSphereFlow(float noiseSeed)
        {
            _noiseSeed = noiseSeed;
        }

        /// <summary>thread 실의 머리 진행 각도(라디안, 감지 않은 누적값)를 반환한다.</summary>
        public float GetHead(int thread)
        {
            return _heads[thread];
        }

        /// <summary>
        /// 두 실의 시작 각도를 무작위로 정하고(두 실은 반 바퀴 떨어짐) 속도를 0으로, 다음 당김을 time 뒤 style의 평균 간격 안에서 정한다.
        /// </summary>
        public void Reset(CombatTokenOrbitStyle style, float time)
        {
            float start = Random.value * TWO_PI;
            for (int thread = 0; thread < ThreadSphere.THREAD_COUNT; thread++)
            {
                _heads[thread] = start + thread * Mathf.PI;
                _velocities[thread] = 0f;
                _nextTugs[thread] = time + Random.Range(0.1f, style.TugMeanInterval);
                _dampings[thread] = 3f;
            }
        }

        /// <summary>
        /// style의 평균 속도·당김 설정으로 두 실의 진행 각도를 time 시각 기준 deltaTime만큼 적분한다. 두 번째 실은 CounterSpeedRatio 배 속도로 돈다.
        /// 궤도면 회전이 진행 각도에 비례하므로 2π로 감지 않고 누적한다.
        /// </summary>
        public void Step(CombatTokenOrbitStyle style, float time, float deltaTime)
        {
            for (int thread = 0; thread < ThreadSphere.THREAD_COUNT; thread++)
            {
                float seed = _noiseSeed + thread;
                float speedScale = thread == 0 ? 1f : style.CounterSpeedRatio;
                speedScale *= 1f + style.SpeedWander * (Mathf.PerlinNoise(time * 0.12f, 61.7f + seed * 19.3f) - 0.5f) * 2f;
                float baseSpeed = style.OrbitSpeed * TWO_PI * speedScale;
                float drift = baseSpeed * (0.4f + 1.2f * Mathf.PerlinNoise(time * 0.31f, 4.2f + seed * 37.1f));
                _velocities[thread] = Mathf.Lerp(_velocities[thread], drift, 1f - Mathf.Exp(-_dampings[thread] * deltaTime));
                if (time >= _nextTugs[thread])
                {
                    _velocities[thread] += baseSpeed * style.TugStrength * Random.Range(0.3f, 1.2f);
                    _dampings[thread] = Random.Range(1.5f, 5f);
                    // 지수 분포 간격이라 당김이 몰려서 오거나 오래 쉬기도 한다.
                    float interval = -Mathf.Log(1f - Random.value * 0.999f) * style.TugMeanInterval;
                    _nextTugs[thread] = time + Mathf.Clamp(interval, 0.15f, 4f);
                }

                _heads[thread] += _velocities[thread] * deltaTime;
            }
        }
    }
}

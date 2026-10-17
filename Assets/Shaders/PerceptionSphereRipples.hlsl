#ifndef BLINDBLADE_PERCEPTION_SPHERE_RIPPLES_INCLUDED
#define BLINDBLADE_PERCEPTION_SPHERE_RIPPLES_INCLUDED

// PerceptionRevealMap.MAX_SPHERE_RIPPLES와 같아야 한다.
#define MAX_SPHERE_RIPPLES 32
int _OutlineSphereRippleCount;
// xyz: 중심, w: 최대 반경
float4 _OutlineSphereRipples[MAX_SPHERE_RIPPLES];
// x: 시작 시각, y: 확산 시간, z: 링 간격, w: 잔상 시간
float4 _OutlineSphereRippleParams[MAX_SPHERE_RIPPLES];
// x: 링 수
float4 _OutlineSphereRippleRings[MAX_SPHERE_RIPPLES];

// 구형 걸음 파동의 각 링 전선이 3D 거리로 지나간 뒤 잔상 동안 남는 공개 정도 중 가장 큰 값을 반환한다.
// 시야 맵 걸음 파동과 같은 감속 확산식과 잔상 규칙을 쓰며, now는 인지 시각, edgeFade는 바깥 경계에서 잔상이 짧아지는 거리다.
float SphereRippleVisibility(float3 position, float now, float edgeFade)
{
    float visibility = 0.0;
    [loop]
    for (int i = 0; i < _OutlineSphereRippleCount; i++)
    {
        float4 ripple = _OutlineSphereRipples[i];
        float4 rippleParams = _OutlineSphereRippleParams[i];
        float distanceFromCenter = distance(position, ripple.xyz);
        if (distanceFromCenter > ripple.w)
            continue;
        float hitTime = rippleParams.y * (1.0 - sqrt(saturate(1.0 - distanceFromCenter / ripple.w)));
        float innerRadius = max(0.0, ripple.w - max(edgeFade, 0.0001));
        float edgeAfterimage = rippleParams.w * (1.0 - smoothstep(innerRadius, ripple.w, distanceFromCenter));
        int ringCount = (int)_OutlineSphereRippleRings[i].x;
        [loop]
        for (int ring = 0; ring < ringCount; ring++)
        {
            float passedAt = rippleParams.x + ring * rippleParams.z + hitTime;
            if (now < passedAt)
                break;
            float remaining = passedAt + edgeAfterimage - now;
            visibility = max(visibility,
                smoothstep(0.0, 1.0, saturate(remaining / max(rippleParams.w, 0.0001))));
        }
    }
    return visibility;
}

#endif

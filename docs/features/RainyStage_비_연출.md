### RainyStage 비 연출

- 기능 설명: `RainyStage`에서만 카메라 주변에 비가 내린다. 불릿 타임에는 물방울이 거의 멈추고 둥글게 보인다. 실제 칼날과 검기의 베기 면에 닿은 방울은 그 자리에서 파열하고, 베기가 지난 면에는 잠시 방울이 없는 자국이 남는다.
- 동작 조건 및 처리 흐름: `RainyStage` 씬의 `RainStageWeather`가 플레이어 카메라 주위 방울 2,048개를 GPU 버퍼에 생성한다. 일반 상태는 게임 시간으로 낙하하고, `PlayerEmpower.IsBulletTimeActive` 동안에는 실시간의 0.012배로 낙하한다. 일시정지는 갱신하지 않는다. 빗방울의 종횡비는 실시간 0.12초에 걸쳐 비 줄기와 둥근 방울 사이를 바꾸며, 둥근 방울 크기는 기본 크기의 0.33배까지 줄어든다. 불릿 타임에는 방울별 위상 차이를 둔 셰이더 외곽선 변형이 실시간으로 흔들려 위쪽이 뾰족한 불규칙한 물방울 형태를 만든다. 일시정지 중에는 흔들림 시간을 멈춘다. 방울은 투명 블렌딩과 깊이 쓰기 비활성화로 내부를 비우고 은은한 회백색 외곽선만 표시한다. GPU에서 베기 면에 닿으면 방울 위치를 유지한 채 흰색 파열 상태로 바꾸고, 파열은 불릿 타임 중 실시간의 0.65배로 진행한다. 화면 아래로 나가거나 파열이 끝난 방울은 카메라 위쪽 한 점에 몰아넣지 않고 비 영역의 높이 범위에 고르게 재생성해 불릿 타임 해제 뒤 동시 낙하를 줄인다. 공격이 지나간 면은 게임 시간 0.75초 동안 남겨 새로 태어나거나 지나가는 방울을 제거한다.
- 구현 상태: GPU Compute Shader와 URP 인스턴싱 셰이더, 씬 연결, 검격 경로 전달을 구현했다. GPU 방울 수를 2,048개로 줄이고, 불릿 타임 크기·외곽선 색·불투명도·외곽선 흔들림 크기, 흰 파열 색·불투명도를 Inspector에서 조절하게 했다. 실제 Unity Play에서 최신 변경의 보이는 모습과 성능은 미확인이다.

#### 관련 코드

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| `Assets/Scenes/Defense/RainyStage.unity` | `RainStageWeather` 오브젝트 | 이 씬에서만 비를 시작하고 씬 해제 때 GPU 자원을 정리 |
| `Assets/Scripts/Defense/RainStageWeather.cs` | `RainStageWeather`, `RecordBladeSweep`, `RecordWaveSweep` | 시간 배율, GPU 버퍼, 베기 면 및 잔류 영역 관리, Inspector 연출값 전달 |
| `Assets/Effects/RainStage/RainStage.compute` | `Initialize`, `UpdateRain`, `SpawnPosition` | 방울 생성 높이 분산·이동·파열·빈 자국 판정 |
| `Assets/Effects/RainStage/RainStage.shader`, `RainStage.mat` | `Vert`, `Frag` | 투명한 방울 내부와 회백색 외곽선, 불릿 타임 크기 축소, 비 줄기·둥근 방울·흰 위치별 물 파열 표시 |
| `Assets/Scripts/Player/PlayerBladeBambooCutter.cs` | `LateUpdate` | 기존 실제 칼날 베기 사각형을 비 시스템에 전달 |
| `Assets/Scripts/Defense/SwordWaveBambooCutter.cs` | `LateUpdate` | 기존 검기 이동 구간과 반폭을 비 시스템에 전달 |

#### 연결 및 협업

- 연결된 기능: 대숲 스테이지 1, 대나무 베기, 퍼펙트 패링 토큰, 게임 씬 공통 구성.
- 공유하는 코드 및 상태: `PlayerEmpower.IsBulletTimeActive`, `PlayerController.IsPaused`, 기존 칼날·검기 베기 경로를 읽는다. `Time.timeScale`이나 공격 판정을 변경하지 않는다.
- 중복 또는 충돌 사항: 공유 커터 두 곳에 비 시스템 호출만 추가한다. RainyStage 외 씬에서는 활성 비 시스템이 없어 호출이 아무 효과를 내지 않는다. VFX Graph의 GPU Event 대신 Compute Shader의 방울 상태를 직접 바꿔 파열을 방울 위치에 고정한다.
- 승인 필요 사항 및 결과: 사용자가 RainyStage 한정 비 연출 구현을 요청했다. 기존 기능의 판단과 피해 동작은 그대로다.

#### Inspector 설정

- `RainyStage` 씬의 `RainStageWeather` 오브젝트에 있는 `RainStageWeather` 컴포넌트에서 Droplet Count, Near Radius, Height Above Camera, Depth Below Camera, Fall Speed, Droplet Size, Streak Length를 조정한다. Droplet Count 기본값은 2,048개다.
- Droplet Outline Color, Droplet Outline Width, Droplet Outline Opacity로 방울 외곽선의 색·두께·불투명도를 조절한다. 기본 색은 어두운 배경에서 덜 가리도록 낮춘 회백색이며, 방울 내부는 픽셀을 그리지 않아 뒤의 장면을 그대로 통과시킨다.
- Bullet Rain Speed, Shape Transition Time, Bullet Droplet Scale, Bullet Wiggle Amplitude, Bullet Splash Speed로 불릿 타임의 낙하·형태 전환·둥근 방울 크기·외곽선 흔들림·파열 시간을 조정한다. Bullet Droplet Scale 기본값은 0.33이며, Bullet Wiggle Amplitude 기본값은 방울 반경 기준 0.25다. 위쪽 뾰족한 변형과 작은 불규칙성 위에 이 진폭이 더해지며, 방울별 위상을 분산하고 일시정지 동안 애니메이션 시간을 멈춘다.
- Splash Color에서 파열 색과 알파를 조절한다. 기본 RGB는 흰색이며 알파는 0.48로 설정해 흰 파열이 화면을 덮지 않도록 했다.
- Blade Thickness, Wave Thickness, Trail Lifetime, Maximum Trails, Maximum Hits Per Frame, Splash Duration, Splash Size로 공격 반응과 예산을 조정한다.
- 방울마다 GameObject·Collider·Rigidbody를 만들지 않는다. 카메라 주변 방울과 베기 면만 GPU에 보낸다. 최대 잔류 영역 32개, 프레임당 신규 공격 면 48개다.

#### 확인 결과

- 최초 RainStage 구현본의 RainStageWeather와 두 검격 커터를 Unity 6 참조로 Roslyn 컴파일했다. 오류 0건이며 Inspector 참조 필드 경고 2건과 기존 칼날 참조 경고 2건은 에셋 직렬화 필드 특성이다. 이후 추가한 외곽선·재생성 높이 분산·Inspector 값·불릿 타임 흔들림 변경은 별도 컴파일하지 않았다.
- 이전 Unity Editor 로그에서 최초 셰이더·컴퓨트 파일 가져오기를 확인했다. 첫 셰이더 가져오기에서 발견된 인스턴스 ID와 HLSL 예약어 오류를 수정했으며, 그 당시 최종 가져오기 이후 새 셰이더 오류 기록은 없었다. 그 뒤 변경한 셰이더는 에디터에서 다시 가져왔는지 확인하지 못했다.
- RainStage 씬 참조와 GUID 연결은 정적 확인했다. 컴퓨터에서 Unity Editor가 프로젝트 잠금 파일을 보유 중이라 별도 배치 Editor 실행은 종료 코드 1로 끝났고 Play 모드 시각 확인은 수행하지 못했다.
- GPU/CPU 프레임 시간 측정은 미확인이다.
- 화면 캡처나 Play 상태에서 외곽선 색·알파, 방울 수, 불릿 타임 크기·흔들림, 흰 파열색과 재생성 분산 효과를 검증하지 않았다. 불릿 타임 중 방울이 영역 아래를 벗어나면 높이 범위에 분산해 재생성되므로 같은 높이에서 일제히 낙하하는 원인을 제거하는 방향으로 수정했다.

### RainyStage 비 연출

- 기능 설명: `RainyStage`에서만 카메라 주변에 비가 내린다. 불릿 타임에는 물방울이 거의 멈추고 둥글게 보인다. 실제 칼날과 검기의 베기 면에 닿은 방울은 그 자리에서 파열하고, 베기가 지난 면에는 잠시 방울이 없는 자국이 남는다.
- 동작 조건 및 처리 흐름: `RainyStage` 씬의 `RainStageWeather`가 플레이어 카메라 주위 방울 3,072개를 GPU 버퍼에 생성한다. 일반 상태는 게임 시간으로 낙하하고, `PlayerEmpower.IsBulletTimeActive` 동안에는 실시간의 0.012배로 낙하한다. 일시정지는 갱신하지 않는다. 빗방울의 종횡비는 실시간 0.12초에 걸쳐 비 줄기와 둥근 방울 사이를 바꾼다. GPU에서 베기 면에 닿으면 방울 위치를 유지한 채 파열 상태로 바꾸고, 파열은 불릿 타임 중 실시간의 0.65배로 진행한다. 기존 물방울은 파열 종료 후 화면 밖 위쪽에서 재생성한다. 공격이 지나간 면은 게임 시간 0.75초 동안 남겨 새로 태어나거나 지나가는 방울을 제거한다.
- 구현 상태: GPU Compute Shader와 URP 인스턴싱 셰이더, 씬 연결, 검격 경로 전달을 구현했다. 실제 Unity Play에서 보이는 모습과 성능은 미확인이다.

#### 관련 코드

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| `Assets/Scenes/Defense/RainyStage.unity` | `RainStageWeather` 오브젝트 | 이 씬에서만 비를 시작하고 씬 해제 때 GPU 자원을 정리 |
| `Assets/Scripts/Defense/RainStageWeather.cs` | `RainStageWeather`, `RecordBladeSweep`, `RecordWaveSweep` | 시간 배율, GPU 버퍼, 베기 면 및 잔류 영역 관리 |
| `Assets/Effects/RainStage/RainStage.compute` | `Initialize`, `UpdateRain` | 방울 생성·이동·파열·빈 자국 판정 |
| `Assets/Effects/RainStage/RainStage.shader`, `RainStage.mat` | `Vert`, `Frag` | 비 줄기·둥근 방울·위치별 물 파열 표시 |
| `Assets/Scripts/Player/PlayerBladeBambooCutter.cs` | `LateUpdate` | 기존 실제 칼날 베기 사각형을 비 시스템에 전달 |
| `Assets/Scripts/Defense/SwordWaveBambooCutter.cs` | `LateUpdate` | 기존 검기 이동 구간과 반폭을 비 시스템에 전달 |

#### 연결 및 협업

- 연결된 기능: 대숲 스테이지 1, 대나무 베기, 퍼펙트 패링 토큰, 게임 씬 공통 구성.
- 공유하는 코드 및 상태: `PlayerEmpower.IsBulletTimeActive`, `PlayerController.IsPaused`, 기존 칼날·검기 베기 경로를 읽는다. `Time.timeScale`이나 공격 판정을 변경하지 않는다.
- 중복 또는 충돌 사항: 공유 커터 두 곳에 비 시스템 호출만 추가한다. RainyStage 외 씬에서는 활성 비 시스템이 없어 호출이 아무 효과를 내지 않는다. VFX Graph의 GPU Event 대신 Compute Shader의 방울 상태를 직접 바꿔 파열을 방울 위치에 고정한다.
- 승인 필요 사항 및 결과: 사용자가 RainyStage 한정 비 연출 구현을 요청했다. 기존 기능의 판단과 피해 동작은 그대로다.

#### Inspector 설정

- `RainStageWeather`에서 Droplet Count, Near Radius, Height Above Camera, Depth Below Camera, Fall Speed, Droplet Size, Streak Length, Rain Color를 조정한다.
- Bullet Rain Speed, Shape Transition Time, Bullet Splash Speed로 불릿 타임의 낙하·형태 전환·파열 시간을 조정한다.
- Blade Thickness, Wave Thickness, Trail Lifetime, Maximum Trails, Maximum Hits Per Frame, Splash Duration, Splash Size로 공격 반응과 예산을 조정한다.
- 방울마다 GameObject·Collider·Rigidbody를 만들지 않는다. 카메라 주변 방울과 베기 면만 GPU에 보낸다. 최대 잔류 영역 32개, 프레임당 신규 공격 면 48개다.

#### 확인 결과

- RainStageWeather와 두 검격 커터를 Unity 6 참조로 Roslyn 컴파일했다. 오류 0건이며 Inspector 참조 필드 경고 2건과 기존 칼날 참조 경고 2건은 에셋 직렬화 필드 특성이다.
- Unity Editor 로그에서 C# 재컴파일 성공, RainStage 셰이더·컴퓨트 파일의 최종 가져오기를 확인했다. 첫 셰이더 가져오기에서 발견된 인스턴스 ID와 HLSL 예약어 오류를 수정했다. 최종 가져오기 이후 새 셰이더 오류 기록은 없다.
- RainStage 씬 참조와 GUID 연결은 정적 확인했다. 컴퓨터에서 Unity Editor가 프로젝트 잠금 파일을 보유 중이라 별도 배치 Editor 실행은 종료 코드 1로 끝났고 Play 모드 시각 확인은 수행하지 못했다.
- GPU/CPU 프레임 시간 측정은 미확인이다.

# DefenseBeast 일반 적 구현 명세

- 작성일: 2026-10-17
- 상태: **일반 Beast 전용 시각·도약 전조·Spine 표면 연기 방출 적용. 도약 높이·방어 반동 변경 반영, Unity 컴파일·실제 전투 확인 대기**

## 기능과 행동 규칙

Assets/Resources/Defense/DefenseBeast.prefab은 Enemy_SingleMelee.prefab에서 직접 상속한 Prefab Variant다. TwinBoss.prefab의 Beast/Wolf 시각 계층을 사용하고 `EnemySmokeVisual` 파티클이 결합된 스킨 메시 표면에서 연기를 방출한다. 일반 적의 이동·체력·공격 컴포넌트를 유지하며 TwinBoss의 Beast 공격·컨트롤러 동작은 변경하지 않았다.

DefenseBeast는 플레이어를 추적하고, 수평 거리 6m 이내에서 완전한 NavMesh 경로와 장애물 없는 도약선이 있으면 0.55초 동안 최고 1.5m 높이의 포물선 도약을 한다. 플레이어의 CharacterController 높이는 2m다. 막기 또는 쳐내기에 막히면 접촉 위치에서 플레이어 시야 전방 4m 지점으로 0.25초 동안 반동한다. 그 지점에 유효한 NavMesh가 없거나 경로가 막혀 있으면 도약 시작 위치로 돌아온다. 착지 반경은 2m이고 피해는 10이다. 전조가 시작되면 플레이어가 감지 거리 밖으로 이동하거나 시야가 가려져도 도약을 취소하지 않는다. 전조 시작 시 고정한 수평 도약 축에 현재 플레이어 위치를 투영해 도약 중 앞뒤 위치만 따라가며, 좌우 위치 변화는 착지점에 반영하지 않는다. 기본 착지 목표는 현재 플레이어 위치에서 도약 축을 따라 10m 뒤이며, NavMesh가 그 지점까지 이어지지 않으면 플레이어 위치를 후보로 사용한다. 도약 중 경로와 플레이어 충돌체를 검사해 뒤로 물러나도 경로에 닿으면 피해를 판정한다. 좌우 회피는 도약 경로 밖으로 벗어나야 피할 수 있다. 도약 후 행동은 다음과 같다.

| 착지 결과 | 이동 목표 |
| --- | --- |
| Hit이며 실제 피해 적용 | 플레이어 반대편 최소 6m, 이미 10m 추월했다면 착지점 유지 |
| DodgeAvoided | 플레이어 반대편 최소 6m, 이미 10m 추월했다면 착지점 유지 |
| 플레이어와 접촉하지 않은 빗나감 | 플레이어 반대편 최소 6m, 이미 10m 추월했다면 착지점 유지 |
| Guard, NormalParry, PerfectParry | 플레이어 시야 전방 4m 지점으로 반동 |
| Invulnerable | 도약 후 기존 이동으로 도약 시작 위치 |

적중·회피·빗나감 후 이동은 NavMesh 완전 경로와 장애물 검사를 거친다. 방어 반동은 플레이어 전방 목적지의 NavMesh 샘플과 직선 장애물 검사를 거치며, 목적지가 유효하지 않으면 시작 위치를 사용한다. 제한 시간 안에 목적지에 닿지 못하면 현재 위치에서 후딜로 넘어간다. 벽을 관통하는 순간이동은 사용하지 않는다. 피격 취소·사망·비활성화가 발생하면 도약 이동을 중단하고 위치 및 NavMeshAgent 설정을 복원한다. 비활성화 상태에서는 Warp를 시도하지 않지만 저장한 Agent 설정은 복원한다.

## 프리팹 구성

- 직접 부모: Assets/Resources/Enemies/Enemy_SingleMelee.prefab
- TwinBoss의 Beast/Wolf 시각 계층을 복제하되, 일반 적 Wolf에는 전용 `DefenseBeastWolfVisual`을 연결한다. 이 스크립트는 `DefenseBeastLeapAttack`의 전조·도약 상태를 표시하며 TwinBoss 전용 `BeastBossAttack`이나 `TwinBeastWolfVisual`을 사용하지 않는다.
- 일반 적 루트는 NavMeshAgent, EnemyMotor, EnemyHealth를 하나씩 사용한다. 보스 공격·보스 오디오·TwinBossController·RiderSeat는 복제하지 않았다.
- 원본의 베기·돌진 공격과 더 이상 쓰지 않는 공격 시각 계층을 Variant에서 제거했다. 공격 선택기에는 DefenseBeastLeapAttack 하나만 지정하고 Melee Only는 끈다.
- Wolf 피격 판정은 기존 일반 적 체력과 연결했다. 프리팹에는 누락된 스크립트가 없고 중복 NavMeshAgent 또는 EnemyHealth가 없다.
- `DefenseEnemySpawner`의 일반 적/Beast 가중치 선택에 연결되어 있다. `BambooStage1`, `RainyStage_JH`, `RainyTwinBoss`, `TestStage` 씬의 스포너가 `DefenseBeast.prefab`을 참조한다.

## Inspector 초깃값

| 컴포넌트 / 설정 | 값 |
| --- | ---: |
| EnemyMotor 이동 속도 / 가속도 | 7.2m/s / 12m/s² |
| EnemySensor 감지 거리 | 8m |
| NavMeshAgent 반경 / 높이 / 속도 | 0.6m / 1.6m / 7.2m/s |
| DefenseBeastLeapAttack 사거리 | 6m |
| 도약 시간 / 최고 높이 / 경로·착지 접촉 반경 | 0.55초 / 1.5m / 2m |
| 착지 피해 / 착지 추월 거리 / 최소 반대편 이동 거리 | 10 / 10m / 6m |
| 방어 반동 복귀 시간 / 플레이어 전방 거리 | 0.25초 / 4m |
| 결과 이동 속도 / 제한 시간 / NavMesh 샘플 반경 | 7.2m/s / 2초 / 1m |
| 장애물 마스크 / 착지 마스크 | 51 / 모든 레이어 |
| 공통 전조 / 후딜 / 쿨타임 | 1초 / 0.75초 / 2초 |
| 박동·도약 소리 | EnemyHeartbeat / DefenseBeastLeap (보스 공격음보다 +2반음) |
| EnemyHealth 최대 체력 | 10 |

도약 수치와 공통 전조·쿨타임 설정은 프리팹 Inspector에서 조정할 수 있다. FootstepRipple은 DefenseMelee의 리플 설정을 사용하고 DefenseBeast 프리팹의 걸음 간격 설정은 2다.

## 전투·감각 연결

DefenseBeastLeapAttack은 EnemyAttackBehaviour를 상속하고 공격 로그 종류 및 DamageContext.Kind를 Melee로 분류한다. 도약 시작과 접촉에는 같은 AttackId와 PatternId를 사용한다. KatanaDamageReceiver는 저장된 진입 방향으로 도약 공격의 전방 방어를 판정한다. Guard는 기존 플레이어 밀림을 적용하고 패링은 DefenseBeast의 일반 적 자동 경직을 생략해 패턴이 시작점 복귀를 마칠 수 있게 한다.

`DefenseBeastWolfVisual`은 일반 적의 이동 속도와 `DefenseBeastLeapAttack` 상태를 사용해 걸음·웅크림·도약 자세를 표시한다. `TwinBeastWolfVisual`은 TwinBoss의 `BeastBossAttack` 상태와 잠수·물기 연출만 담당한다. FootstepRipple은 첫 박동과 공격 확정 사건에 기존 리플을 내며, 새 공격의 박동음과 도약음은 Inspector SoundId를 사용한다.

## 2026-10-17 도약 전조 붉은 실루엣

- `DefenseBeast.prefab`의 루트에 `TwinBossWarningVisual`을 연결했다. 현재 `_smokeRoot`는 Wolf를 가리키며 이 컴포넌트의 전조 강도를 `DefenseBeastWolfVisual`이 몸체 메시로 전달한다. 일반 근접 적 전조와 TwinBoss가 공유하는 `_MeleeWarningIntensity` 및 `ScreenSpaceOutlineFeature` 근접 경고 마스크를 사용한다.
- `DefenseBeastLeapAttack.PrepareTelegraph`가 실제 공격 문맥의 `TelegraphDuration` 동안 붉은 강도를 올린다. 착지 또는 공격 취소·종료 시 경고를 끝내고 0.15초 동안 기본 실루엣으로 돌아온다.
- TwinBoss 프리팹과 `TwinBeastWolfVisual`은 수정하지 않았다.

## 2026-10-17 Spine 형상에 밀착한 연기와 몸체 선 제거

- `DefenseBeast.prefab`의 Wolf 아래에 `SmokeShape` 스킨 메시 방출원을 추가했다. 기존 36개 Spine 몸체 메시의 정점 18,293개를 36개 본에 연결한 단일 메시이므로 다리·몸통·머리의 애니메이션과 함께 방출 형태가 움직인다.
- `EnemySmokeVisual.prefab`의 기존 `BodySmoke`와 `TrailingWisps` 두 시스템을 Wolf에 하나씩 배치하고 Shape 모듈을 `SkinnedMeshRenderer`로 설정했다. 몸통 연기는 입자 크기 0.68~0.86m, 수명 0.70~0.95초, 방출량 120개/s로 겹침을 늘리고, 속도 0.01~0.04m/s와 노이즈 0.04로 실루엣에서 크게 흩어지지 않게 했다. 보조 wisps는 크기 0.50~0.68m, 방출량 10개/s, 로컬 시뮬레이션으로 설정해 Beast 주변에 머물게 하고, 0.25m/s로 완만하게 상승시킨다. 색상 변화와 기존 연기 재질은 유지한다. Beast 한 마리당 ParticleSystem은 2개이며 메시 조각마다 이미터를 만들지 않는다.
- `DefenseBeastSmokeShapeSource`는 Shape 모듈이 읽는 스킨 메시를 계속 갱신하고 렌더링은 끈다. 화면 공간 연기 합성, `OutlineBeastSmokeMask.shader`, Beast 전용 화면 연기 수치는 사용하지 않는다.
- `ScreenSpaceOutlineFeature`가 보이는 Beast 몸체를 깊이 검사하는 R8 제외 마스크에 그린다. 최종 셰이더는 마스크에 닿는 Beast의 깊이·노멀 경계선을 숨기고, `EnemySmokeVisual` 파티클의 투명 윤곽선은 계속 그린다. 다른 적의 선과 전조 마스크는 기존 방식을 유지한다.
- 도약 전조의 붉은 연기 표시는 기존 근접 경고 R8 마스크를 사용한다. `DefenseBeastWolfVisual.WarningIntensity`가 전조 강도를 노출하며 경고 중인 몸체 렌더러만 경고 마스크에 포함한다.
- 확인: Unity에서 두 파티클 시스템의 스킨 메시 Shape, 눈의 본 연결·공유 머티리얼, 셰이더 메시지 0건을 확인했다. 실제 게임 카메라의 최신 외형과 교전 중 성능은 별도 확인이 필요하다.

## 2026-10-18 늑대형 연기 상승과 발광 눈

- `BodySmoke`의 위쪽 속도를 0.20m/s로 올려 몸체 실루엣에서 많이 벗어나지 않으면서 연기가 천천히 피어오르게 했다. 기존 `TrailingWisps`는 0.25m/s로 상승하며 Beast의 로컬 좌표에 남는다.
- `Head` 본 앞에 `EyeGlow_Left`와 `EyeGlow_Right` Quad를 배치해 머리 자세를 따라가게 했다. `DefenseBeastEyeGlow` 셰이더가 청록색 코어와 푸른 additive 후광을 그리고 Screen Space Outline 합성 뒤에 렌더한다. 두 눈은 `DefenseBeastEyeGlow.mat` 하나를 공유하며 별도 조명이나 충돌체는 사용하지 않는다.
- 확인: Unity에서 눈의 머리 본 연결·머티리얼과 셰이더 메시지 0건을 확인했다. 현재 게임 화면 캡처에는 Beast가 나오지 않아 실제 전투 중 외형과 성능은 확인 대기다.

## 2026-10-18 도약 준비 확정과 축 방향 추적

- `DefenseBeast.prefab`의 `DefenseBeastLeapAttack._cancelWhenHidden`을 끄고, 전조가 시작된 뒤 플레이어가 감지 거리 밖으로 이동하거나 가려져도 준비된 도약을 실행하게 했다. 대상이 비활성화되거나 다른 대상으로 바뀌는 경우의 취소는 유지한다.
- `DefenseBeastLeapAttack.CanAttack`은 공격 시작 시 `_attackRange`를 확인하고, 도약 목표 계산은 전조 시작 방향의 수평 축을 기준으로 한다. 전조 중과 도약 중 뒤로 물러난 거리만 추적하며 옆 방향 위치 변화는 따라가지 않는다.
- 도약 중 현재 플레이어 위치보다 기본 10m 더 나아간 지점을 착지 목표로 삼는다. 뒤로 계속 이동해도 축을 따라 목표를 갱신하고, 유효한 NavMesh 목표가 없을 때는 플레이어 위치의 축 투영점을 후보로 사용한다.
- 도약 매 프레임 비행 경로를 캡슐 검사한다. 플레이어와 접촉하면 그 시점에 방어 판정을 한 번 처리하고, 착지 시 중복 피해를 막는다. Guard·NormalParry·PerfectParry는 접촉 위치에서 0.25초 반동해 시작 위치로 돌아가고, 적중·회피·빗나감은 최소 6m 반대편으로 이동한다. 이미 10m 추월해 착지했다면 다시 플레이어 쪽으로 되돌아가지 않는다.
- 도약 포물선의 최고 높이를 3m에서 1.5m로 낮췄다. `Assets/Resources/Prefabs/Player.prefab`의 CharacterController 높이 2m보다 낮게 설정했다.
- Guard·NormalParry·PerfectParry 접촉 시 전진 도약을 즉시 멈추고, 플레이어의 `PlayerMotor.ViewForward` 방향 4m 지점으로 0.25초 동안 반동 이동한다. 목적지 NavMesh 샘플 또는 직선 경로 검사가 실패하면 도약 시작 위치로 복귀한다. Invulnerable과 DodgeAvoided는 기존 결과 동작을 유지한다.
- 확인: 스크립트·프리팹·명세 변경을 반영했다. Unity 컴파일 및 실제 전투 Play 검증은 미확인이다.

## 2026-10-18 RainyTwinBoss 일반 Beast 사망 으르렁거림

- 기존 깨갱 세 파일을 `123090__stephensaldanha__monster_growl.wav`와 `416862__tbsounddesigns__beargrowl_05.wav`로 교체했다. 각 원본의 전체 2.117초·1.645초를 사용하고 양끝에 0.01초 페이드를 적용한 모노 24비트 WAV를 `FMODProject/week06-blindblade/Assets/DefenseBeast/Death/`에 저장했다.
- 두 파일의 A 가중 평균 음량을 각각 -32dBFS로 맞췄다. 원본 대비 Monster는 -2.43dB, Bear는 +12.42dB이며 출력 피크는 각각 -2.63dBFS, -1.90dBFS로 클리핑하지 않는다. FMOD `event:/Enemy/DefenseBeastDie`의 Multi Instrument가 두 파일 중 하나를 셔플 재생한다.
- 으르렁거림 원본이 이미 낮은 음역이므로 깨갱용 FMOD -6반음 피치를 해제해 0반음으로 돌렸다. 이벤트 길이는 2.2초이며, `SoundLibrary.asset`의 `SoundId.DefenseBeastDie=47` 음량 배율은 1이다. 이전 0.1990536 배율은 깨갱 원본에 맞춘 설정이므로 사용하지 않는다.
- 사망음은 `EnemyDie`와 같은 FMOD 3D Spatializer 기본 최소·최대 거리 1~20m를 사용하며, 별도 2D 거리 보정은 꺼져 있다. SFX 버스·뱅크와 RainyTwinBoss의 비 속 적 소리 필터를 따른다.
- `RainyTwinBoss`의 `DefenseEnemySpawner._beastDeathSound`만 47로 지정했다. 스폰 시 `EnemyDamageAudio.ConfigureDeathSound`가 해당 Beast에 전용 사망음을 설정한다. 다른 씬의 일반 적, 일반 Beast 및 TwinBoss의 사망음은 기존 연결을 사용한다.
- FMOD Desktop 뱅크 빌드·프로젝트 진단과 이벤트 GUID·씬·Library 연결을 확인했다. Unity 배치 실행은 로컬 라이선스 서비스 연결 단계에서 종료되어 실제 전투의 재생 선택·음량·거리감은 미확인이다.

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| `Assets/Scripts/Defense/DefenseEnemySpawner.cs` | `DefenseEnemySpawner.CreateMelee` | 이 씬의 Beast 인스턴스에 사망음 ID를 설정 |
| `Assets/Scripts/SoundSystem/EnemyDamageAudio.cs` | `ConfigureDeathSound`, `HandleDeath` | 전용 사망음 저장 및 사망 위치 재생 |
| `Assets/Scripts/Managers/SoundManager.cs` | `IsEnemyOrProjectileSound` | 새 사망음을 비 속 적 사운드 필터 대상으로 분류 |
| `Assets/Resources/Sound/SoundLibrary.asset` | `SoundId.DefenseBeastDie` | FMOD 이벤트·거리 감쇠 설정 |
| `Assets/Scenes/Defense/RainyTwinBoss.unity` | `DefenseEnemySpawner` | 이 씬에만 ID 47 지정 |

## 2026-10-18 일반 Beast 도약음 분리

- 일반 Beast의 `DefenseBeastLeapAttack` 실행음은 기존 보스 `BeastBossAttack=29` 이벤트를 공유하되, 별도 `SoundId.DefenseBeastLeap=48`로 재생한다. `SoundLibrary.asset`에서 도약음의 피치 배율을 1.122462(+2반음)로 지정해 보스보다 조금 높인다. 보스의 29번 ID·피치·이벤트는 변경하지 않는다.
- `DefenseBeast.prefab`의 `_leapSound`만 29에서 48로 바꿨다. 기존 `EnemyAttackBehaviour.CommitAttack`이 도약 실행 직전에 `EnemySoundStage.Action`을 재생하므로 공격 시점과 취소·자연 종료 처리는 그대로다. 이 프리팹을 사용하는 씬의 일반 Beast에 적용된다.
- 48번의 보스 공격 이벤트 참조·피치 배율과 프리팹 연결을 확인했고 FMOD 프로젝트 진단이 통과했다. Unity 라이선스 서비스 연결 실패로 실제 전투에서의 도약음은 확인하지 못했다.
- 관련 코드와 설정: `Assets/Scripts/Enemies/Attack/DefenseBeastLeapAttack.cs` / `ResolveActionSound`, `Assets/Scripts/Enemies/Attack/EnemyAttackBehaviour.cs` / `CommitAttack`, `Assets/Scripts/Enums/SoundId.cs`, `Assets/Resources/Sound/SoundLibrary.asset`, `Assets/Resources/Defense/DefenseBeast.prefab`.

## 구현 및 확인 상태

- 구현 코드: Assets/Scripts/Enemies/Attack/DefenseBeastLeapAttack.cs
- 전투 연결: Assets/Scripts/Enemies/Attack/EnemyAttackBehaviour.cs, Assets/Scripts/Enemies/Attack/EnemyAttackContext.cs, Assets/Scripts/Player/KatanaV2/KatanaDamageReceiver.cs, Assets/Scripts/Player/KatanaV2/KatanaCombatCoordinator.cs
- 시각·감각 연결: Assets/Scripts/Enemies/DefenseBeastWolfVisual.cs, Assets/Scripts/Enemies/TwinBossWarningVisual.cs, Assets/Scripts/Footsteps/FootstepRipple.cs
- 연기·눈·몸체 선 처리: Assets/Scripts/Enemies/DefenseBeastSmokeShapeSource.cs, Assets/Scripts/Rendering/ScreenSpaceOutlineFeature.cs, Assets/Shaders/ScreenSpaceOutline.shader, Assets/Shaders/DefenseBeastEyeGlow.shader, Assets/Resources/Defense/DefenseBeastEyeGlow.mat, Assets/Resources/Defense/DefenseBeastSmokeShape.asset, Assets/Resources/Defense/DefenseBeast.prefab, Assets/Resources/Enemies/Visuals/EnemySmokeVisual.prefab
- `DefenseBeast.prefab`은 `DefenseBeastWolfVisual`과 `TwinBossWarningVisual`을 GUID로 연결했고 기존 Wolf 리그 참조와 Inspector 수치는 YAML 직렬화 데이터에서 보존했다.
- Unity Editor 재컴파일 후 스크립트 오류 0건과 ParticleSystem 두 개의 스킨 메시 방출 설정을 확인했다. 실제 게임 카메라의 최신 외형과 교전 중 프레임 비용은 확인하지 않았다.
- TwinBoss 프리팹은 계속 `TwinBeastWolfVisual`을 사용하며 일반 Beast 프리팹은 해당 컴포넌트를 사용하지 않는다.
- 실제 전투의 모든 도약 상태와 다양한 카메라 거리·해상도·성능은 미확인이다.
- 테스트 코드는 작성하지 않았다.

## 관련 명세

- 적 추적: 적 이동과 공격 접근 거리
- 적 공격 패턴: DefenseBeast 도약 공격의 준비·실행·복귀
- 전투 판정 계약: 근접 전방 방어와 도약 결과 처리
- 감각 표현: 도약 박동·확정 리플과 Wolf 시각 상태
- 쌍둥이 보스: 기존 Beast 공격 및 보스 동작은 범위 밖이며 변경하지 않음

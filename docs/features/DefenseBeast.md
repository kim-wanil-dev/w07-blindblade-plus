# DefenseBeast 일반 적 구현 명세

- 작성일: 2026-10-17
- 상태: **일반 Beast 전용 시각·도약 전조·Spine 표면 연기 방출 적용. Unity 에셋·컴파일 확인 완료, 실제 전투 화면 확인 대기**

## 기능과 행동 규칙

Assets/Resources/Defense/DefenseBeast.prefab은 Enemy_SingleMelee.prefab에서 직접 상속한 Prefab Variant다. TwinBoss.prefab의 Beast/Wolf 시각 계층을 사용하고 `EnemySmokeVisual` 파티클이 결합된 스킨 메시 표면에서 연기를 방출한다. 일반 적의 이동·체력·공격 컴포넌트를 유지하며 TwinBoss의 Beast 공격·컨트롤러 동작은 변경하지 않았다.

DefenseBeast는 플레이어를 추적하고, 수평 거리 6m 이내에서 완전한 NavMesh 경로와 장애물 없는 도약선이 있으면 0.55초 동안 높이 3m의 포물선 도약을 한다. 착지 반경은 2m이고 피해는 10이다. 도약 후 행동은 다음과 같다.

| 착지 결과 | 이동 목표 |
| --- | --- |
| Hit이며 실제 피해 적용 | 플레이어의 반대편 6m |
| DodgeAvoided | 플레이어의 반대편 6m |
| 플레이어와 접촉하지 않은 빗나감 | 플레이어의 반대편 6m |
| Guard, NormalParry, PerfectParry, Invulnerable | 도약 시작 위치 |

반대편 이동과 시작 위치 복귀는 NavMesh 위치 샘플·완전한 경로·장애물 검사를 거친다. 유효한 경로가 없거나 제한 시간 안에 목적지에 닿지 못하면 현재 위치에서 후딜로 넘어간다. 벽을 관통하는 순간이동은 사용하지 않는다. 피격 취소·사망·비활성화가 발생하면 도약 코루틴과 이동을 중단하고 위치 및 NavMeshAgent 설정을 복원한다. 비활성화 상태에서는 Warp를 시도하지 않지만 저장한 Agent 설정은 복원한다.

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
| 도약 시간 / 높이 / 착지 반경 | 0.55초 / 3m / 2m |
| 착지 피해 / 반대편 이동 거리 | 10 / 6m |
| 결과 이동 속도 / 제한 시간 / NavMesh 샘플 반경 | 7.2m/s / 2초 / 1m |
| 장애물 마스크 / 착지 마스크 | 51 / 모든 레이어 |
| 공통 전조 / 후딜 / 쿨타임 | 1초 / 0.75초 / 2초 |
| 박동·도약 소리 | EnemyHeartbeat / BeastBossAttack |
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

# DefenseBeast 일반 적 구현 명세

- 작성일: 2026-10-17
- 상태: **코드·프리팹 구현 및 Unity 컴파일 확인 완료, Play 검증 미확인**

## 기능과 행동 규칙

Assets/Resources/Defense/DefenseBeast.prefab은 Enemy_SingleMelee.prefab에서 직접 상속한 Prefab Variant다. TwinBoss.prefab의 Beast/Wolf 시각 계층과 연기를 사용하지만 일반 적의 이동·체력·공격 컴포넌트를 유지한다. TwinBoss의 Beast 공격·컨트롤러 동작은 변경하지 않았다.

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
- TwinBoss의 Beast/Wolf 시각 계층만 복제했다. 복제된 Wolf의 TwinBeastWolfVisual은 DefenseBeastLeapAttack을 참조하고 기존 BeastBossAttack 참조는 비워 둔다.
- 일반 적 루트는 NavMeshAgent, EnemyMotor, EnemyHealth를 하나씩 사용한다. 보스 공격·보스 오디오·TwinBossController·RiderSeat는 복제하지 않았다.
- 원본의 베기·돌진 공격과 더 이상 쓰지 않는 공격 시각 계층을 Variant에서 제거했다. 공격 선택기에는 DefenseBeastLeapAttack 하나만 지정하고 Melee Only는 끈다.
- Wolf 피격 판정은 기존 일반 적 체력과 연결했다. 프리팹에는 누락된 스크립트가 없고 중복 NavMeshAgent 또는 EnemyHealth가 없다.
- DefenseEnemySpawner와 BambooStage1에는 연결하지 않았다. 기존 스폰 코드가 EnemyChargeAttack 컴포넌트를 전제로 하므로 스테이지 연결은 별도 작업이다.

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

TwinBeastWolfVisual은 기존 TwinBoss 공격 참조와 별도로 DefenseBeastLeapAttack 참조도 사용할 수 있다. 새 프리팹은 이 참조로 웅크림·도약 자세를 표시한다. FootstepRipple은 첫 박동과 공격 확정 사건에 기존 리플을 내며, 새 공격의 박동음과 도약음은 Inspector SoundId를 사용한다.

## 구현 및 확인 상태

- 구현 코드: Assets/Scripts/Enemies/Attack/DefenseBeastLeapAttack.cs
- 전투 연결: Assets/Scripts/Enemies/Attack/EnemyAttackBehaviour.cs, Assets/Scripts/Enemies/Attack/EnemyAttackContext.cs, Assets/Scripts/Player/KatanaV2/KatanaDamageReceiver.cs, Assets/Scripts/Player/KatanaV2/KatanaCombatCoordinator.cs
- 시각·감각 연결: Assets/Scripts/Enemies/TwinBeastWolfVisual.cs, Assets/Scripts/Footsteps/FootstepRipple.cs
- Unity 6000.3.22f1에서 최신 재컴파일 완료, 현재 컴파일 오류 없음.
- Unity Prefab API로 Variant 부모, 패턴 선택, 주요 직렬화 설정, Wolf 참조, 누락 스크립트 및 중복 Agent/Health를 확인했다. IntroScene은 변경되지 않았고 현재 dirty 상태가 아니다.
- Play 동작 검증은 미확인이다. 코드 재컴파일 중 기존 IntroScene Play 세션이 자동 종료되어 수동 Play 검증을 진행하지 않았다. 자동 종료 뒤 Play를 재시작하지 않았다.
- 테스트 코드는 작성하지 않았다.

## 관련 명세

- 적 추적: 적 이동과 공격 접근 거리
- 적 공격 패턴: DefenseBeast 도약 공격의 준비·실행·복귀
- 전투 판정 계약: 근접 전방 방어와 도약 결과 처리
- 감각 표현: 도약 박동·확정 리플과 Wolf 시각 상태
- 쌍둥이 보스: 기존 Beast 공격 및 보스 동작은 범위 밖이며 변경하지 않음
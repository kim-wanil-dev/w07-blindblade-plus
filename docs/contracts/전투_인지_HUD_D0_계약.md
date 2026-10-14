# 전투·인지·HUD 공통 계약

- 갱신일: 2026-10-14
- 계약 버전: `0.2.0`
- 텔레메트리 봉투 스키마: `0.1.0` (저장기 미구현)
- 상태: 현재 공유 저장소의 정식 BambooDefense 수리검 흐름에 적용했다. 외부 팀원 검토·합의는 기록되지 않았다.
- 코드 경로: `Assets/Scripts/Combat/Contracts/`
- namespace: `BlindBlade.Combat.Contracts`
- 구현 계획과 진행: [D PRD](../plans/전투_인지_HUD_D_구현_PRD.md), [D 진행 기록](../plans/전투_인지_HUD_D_progress.md)

## 1. 목적과 소유권

이 계약은 전투 판정이나 인지 결과를 새로 만들지 않는다. 각 시스템의 소유자가 사실을 확정하고, 다른 시스템은 같은 세션·대상·사건 식별자를 사용한다.

| 담당 | 확정하고 소유하는 상태 | 현재 연결 |
| --- | --- | --- |
| A | 플레이어 피해·방어 결과·행동·자원 | `KatanaDamageReceiver`가 공통 `DefenseResolution`을 발행한다. 실제 스태미너 공급자는 아직 없다. |
| B | 적·투사체 수명·공격 원본 ID·충돌·피해 호출 | BambooDefense의 `DefenseShuriken`이 공격/투사체 ID를 발급하고 resolver를 한 번 호출한다. |
| C | 인지 가시성·화면 위치·최종 표시 자격/색/투명도 | `CombatThreatPresentationProvider`가 감각 마스크·카메라·발소리 사건을 스냅샷으로 제공한다. |
| D | 세션 연결·시간 요청 합성·HUD 표시·로그 | `CombatSessionRuntime`과 `ShurikenDirectionIndicator`가 구현돼 있다. 텔레메트리 저장은 미구현이다. |

공통 값 타입은 한 곳에만 둔다. `DamageContext.cs`와 `PlayerContracts.cs`에 전역 `IncomingAttackKind`, `DefenseOutcome`, `DefenseResolution`을 다시 정의하지 않는다. 기존 A 공개 계약은 공유 타입을 import해서 사용한다.

## 2. 세션·시각·식별

| 타입 | 권위 있는 필드 | 의미 |
| --- | --- | --- |
| `CombatSessionKey` | `SessionId:string`, `Generation:int` | 새 게임/재시작마다 달라지는 세션 키 |
| `CombatTimestamp` | `RealtimeSeconds`, `GameplaySeconds`, `ActiveUnscaledSeconds:double` | 세션 시작부터 측정한 실시간, 배율 적용 시간, 명시적 정지를 제외한 실시간 |
| `CombatEventIdentity` | `Session`, `SourceId:string`, `SourceSequence:long` | 원본 발행자가 사건 생성 시 부여하는 키 |
| `CombatEntityKey` | `EntityId:string`, `SpawnGeneration:int` | 재사용되는 같은 게임 오브젝트의 활성화 세대 구분 |
| `CombatCorrelationIds` | `InputId`, `ActionId`, `AttackId`, `HitId`, `ProjectileId`, `DodgeId:int` | 기존 `ActionId:int`와 맞춘 원인 연결 값 |

- 미부여 ID는 0이고 실제 발급 값은 양수다. 사건 순번, 표시 Revision, 시간 요청 ID는 `long`이다.
- 공격·투사체 ID는 `CombatSessionRuntime`이 세션마다 1부터 발급한다. D는 사건을 수집할 때 ID를 새로 만들지 않는다.
- `EnemyHealth.TargetId`는 기존 플레이어 전투 검색과 호환해 유지한다. `SpawnGeneration`을 `OnEnable` 때 증가시켜 풀 재사용을 구분한다.
- `DamageContext`의 기존 5개 인자 생성자는 유지한다. 새 값을 제공하는 경로는 공격자가 `AttackId`, `HitId`, `IncomingAttackKind`를 지정한다.
- `CombatSessionRuntime.CaptureTimestamp()`는 세션 시작 기준으로 세 시계를 반환한다. `ActiveUnscaledSeconds`는 explicit freeze 동안 멈추고 슬로모션 중에는 진행한다.

## 3. A — 공격 수신과 확정 결과

유일한 공통 타입은 `DefenseResolution.cs`의 `IncomingAttackKind`, `DefenseOutcome`, `DefenseResolution`이다.

| 결과 | 의미 |
| --- | --- |
| `Hit` | 방어되지 않아 실제 피해가 적용됨 |
| `Guard` | 일반 가드로 차단 |
| `NormalParry` | 일반 패링으로 차단 |
| `PerfectParry` | 퍼펙트 패링으로 차단 |
| `DodgeAvoided` | A가 확정한 회피 무적 회피 |
| `Invulnerable` | 다른 무적 상태로 차단 |
| `Ignored` | 정지·사망·비활성 등으로 판정에서 제외 |

- `IncomingAttackKind`는 `Unknown`, `Melee`, `Charge`, `Projectile`, `PlayerOwned`다. `Unknown`은 기존 공격자/패턴 검사를 보존한다.
- `IPlayerDefenseResolver.ResolveIncomingAttack(context)`가 한 접촉을 판정한다. 구현체는 `Hit`이면 HP 적용까지 완료한다. 호출자는 반환 결과와 관계없이 `TakeDamage`를 다시 부르지 않는다.
- `DefenseResolution`은 `Outcome`, `AttackId`, `HitId`, `GuardInputId`, `AppliedDamage`, `ContactPoint`, `GameTime`, `RealTime`을 보존한다.
- A의 `KatanaDamageReceiver`는 기존 `TryDefendProjectile`·`TakeDamage` 호환 경로를 유지하고 `DefenseResolved`를 발행한다. 이번 수리검은 새 resolver 경로를 사용해 중복 피해를 막는다.
- 강화 공격 사건, 극한 회피·스태미너 공급자, 실제 일반 패링의 새 동작은 이 연결 범위에서 구현하지 않았다.

## 4. B — BambooDefense 수리검 원본

- `DefenseShuriken.Launch`는 세션 런타임에서 `AttackId`와 `ProjectileId`를 각각 발급받는다. 현재 수리검은 한 번 접촉 후 종료하므로 `HitId = 1`을 쓴다.
- `ProcessHit`은 `Kind = Projectile`을 담은 `DamageContext`를 만들어 `IPlayerDefenseResolver`를 최대 한 번 호출한다. resolver가 없을 때만 `PlayerHealth.TakeDamage` 호환 경로를 쓴다.
- 반환이 `PerfectParry`이면 B가 기존 검기 반격을 생성한다. 일반 가드/패링의 피해·보상 판단을 B가 다시 계산하지 않는다.
- `CombatThreatPresentationProvider`는 활성 수리검 위치를 매 LateUpdate에 읽는다. 발사체가 사라지면 마지막 좌표와 같은 Session/Target으로 `IsActive=false`를 발행한다.
- 현재 세션 발급 ID를 쓰는 공격은 BambooDefense의 `DefenseShuriken`이다. 레거시 보스/일반 적 투사체의 기존 호출부는 이번 요청에서 바꾸지 않았으며 `DamageContext` 5인자 경로에 남아 있다.

## 5. C — 인지 결과와 발소리

`ThreatPresentationSnapshot`은 `Session`, `Target(EntityId, SpawnGeneration)`, `Revision`, `IsActive`, `IsEligible`, `IsInCamera`, `WorldVisible`, `UiVisible`, `UiDirection`, `WorldPosition`, `Proximity`, `Color`, `Alpha`를 보관한다.

- C는 `SensoryPerceptionMask.GetVisibility`와 플레이어 카메라 투영으로 판정한다. 현재 규칙은 `WorldVisible = 인지 가시성 기준 통과 && 카메라 안`, `UiVisible = 활성 && 자격 있음 && !WorldVisible`다.
- 월드 수리검의 `ShurikenPerceptionEffect`도 공급자의 같은 `VisibilityThreshold`를 사용한다. 기본값 0은 기존의 양수 가시성 기준을 보존하며 HUD와 월드 모델의 인지 경계가 갈라지지 않게 한다.
- `UiDirection`은 화면 중심 기준 `(x=오른쪽, y=위)` 단위 벡터다. 뒤쪽 투영은 방향을 뒤집고 방향이 모호하면 위쪽을 사용한다.
- `Proximity`는 공급자 Inspector의 `Near Distance`/`Far Distance`로 계산한다. 색의 alpha는 1, 불투명도는 별도의 `Alpha` 값이다.
- 현재 공급자는 활성 수리검과 `EnemyFootsteps`에서 발소리 사건을 제공한다. 발소리 `Origin`은 발생 위치로 고정하고, 카메라 회전에 따른 표시 갱신은 같은 EventIdentity와 증가하는 Presentation Revision을 사용한다.
- 발소리 표시 사건은 Inspector의 `_footstepLifetime` 동안 읽을 수 있다. 이벤트 순번은 적 세대별로 증가하고 표시 Target은 발소리마다 고유하다.
- 마지막 `_footstepFadeDuration` 동안 C가 Alpha를 감쇠한다. 만료·적 사망·세대 변경·공급자 종료·세션 변경에서는 같은 사건의 `Presentation.IsActive=false`를 발행한다. D는 이를 받아 표시 슬롯을 반환한다.
- 일반 적 전체의 Threat 수명/표시 우선순위와 다른 투사체 종류는 아직 연결되지 않았다.

## 6. D — 표시와 시간

### HUD

- `ShurikenDirectionIndicator`는 C `IThreatPresentationProvider`의 초기 목록과 `ThreatChanged`를 구독한다.
- 마커 키는 Session + Target이며 같은 세대의 이전 Revision을 버린다. C의 방향·색·근접도·투명도를 적용하고 플레이어/투사체 Transform을 저장하지 않는다.
- 발사 전 텔레그래프는 기존 `BambooDefenseDirector.ThrowTelegraphed`를 유지한다. `CombatFootstepHud`가 발소리 초기 목록과 변경 사건을 받아 24개 Image 풀에 적용한다.
- `CombatPlayerHud`가 PlayerHealth의 현재 값·HealthChanged와 선택적인 IPlayerStaminaReadModel을 읽는다. 스태미너 공급자가 없으면 빈 게이지·미연결 텍스트로 표시한다. 임시 Sprite 교체 위치는 [전투 HUD](../features/전투_HUD.md)를 따른다. 실제 피격 전용 비네팅 전환은 남아 있다.

### 시간

- `CombatSessionRuntime`이 공식 BambooDefense 경로의 단일 `Time.timeScale` writer다.
- `RequestSlowMotion`은 현재 Session·OwnerId·Reason·양수 Scale을 확인하고 해제 토큰을 준다. 여러 요청은 최소 Scale, `SetFrozen(true)`는 0으로 합성하며 해제 후 남은 요청을 다시 적용한다.
- `GameStateManager`는 게임오버 정지를 위임하고 `PlayerMotor`는 대시 예고 토큰을 등록/해제한다. 과거 배율을 각자 저장해 덮어쓰지 않는다.
- `TutorialGuide`의 직접 시간 쓰기는 정식 씬에서 참조되지 않는 레거시 코드로 남아 있다. 향후 이 씬을 되살릴 때는 이 계약으로 이관한다.

## 7. 미구현 및 다음 범위

- `IPlayerStaminaReadModel`은 데이터 계약만 있고 공급자가 없다. HUD가 추정값을 만들지 않는다.
- `CombatTelemetryEnvelope`와 `combat-telemetry-v0.1.schema.json`은 저장 명세일 뿐 저장기·payload 검증·전처리기는 없다.
- A의 강화 준비/시작/종료, 스태미너, 회피 보상, 퍼펙트 파동 사건은 형식 일부만 있다.
- D의 HP·발소리 HUD는 실제 공급자와 연결했고 스태미너 HUD는 공급자를 기다린다. 실제 피격 비네팅 전환, 로그 저장, 전체 Play 통합 인수가 남아 있다.
- 현재 저장소 변경은 외부 팀원의 검토 완료를 뜻하지 않는다. 병합하거나 추가 변경을 적용하기 전에 각 담당자의 작업공간과 미저장 Editor 상태를 대조한다.

## 8. 적용 기록과 검증

- `2026-10-14 / 0.2.0`: A의 중복 전역 방어 타입을 공통 namespace 한 곳으로 통합했다. B의 BambooDefense 수리검을 ID 부여와 resolver 호출로 연결했다. C의 위협/발소리 공급자와 D의 HUD consumer를 연결했다. 세션 시계와 게임오버/대시 시간 요청을 연결했다.
- BambooDefense 씬에는 기존 `GameSceneInitializer` 오브젝트의 `CombatSessionRuntime`과 기존 `DefenseDirector` 오브젝트의 `CombatThreatPresentationProvider`를 Unity Editor로 추가하고 저장했다. 기존 12개 HUD 마커와 `_director` 참조는 유지했다.
- Unity Editor 6000.3.22f1 재컴파일 성공, 컴파일 오류 0. Console 최신 표본의 C# 경고 1건은 기존 경고다.
- Play Mode 검증과 테스트 코드는 요청에 포함되지 않아 실행·작성하지 않았다. 수동 검증 시나리오는 D PRD 8절을 따른다.

## 9. 다른 담당자를 위한 안전한 변경 규칙

1. 공유 타입 수정은 이 폴더와 이 문서에서만 기준을 정하고, 같은 이름의 전역 복사본을 만들지 않는다.
2. A가 판정 규칙/HP 적용을, B가 원본 ID·투사체 수명을, C가 표시 자격·색·방향을, D가 HUD 표시와 시간 합성을 맡는다.
3. 기존 공개 API를 바꿀 때는 이 문서와 모든 호출부·직렬화 참조를 함께 갱신한다. `ResolveIncomingAttack` 뒤 `TakeDamage`를 중복 호출하지 않는다.
4. 씬/프리팹 편집은 Unity Editor에서 하고 현재 Inspector 배열·참조를 보존한다. `BambooDefense.unity`를 재생성하지 않는다.
5. 미구현 항목은 실제 공급자와 소비자가 연결되기 전 완료로 표시하지 않는다. 외부 팀원 확인이 필요하면 이 버전/파일을 전달하고 응답을 기록한다.

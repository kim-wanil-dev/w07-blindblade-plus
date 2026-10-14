### 전투·인지·HUD 공통 계약

- 기능 설명: A의 방어 판정, B의 수리검 수명, C의 인지·표시 계산, D의 HUD 적용이 세션·대상·사건 ID를 공유한다.
- 동작 조건 및 처리 흐름: `GameSceneInitializer`가 `CombatSessionRuntime`을 초기화한다. B 수리검이 공격·투사체 ID를 만들고 resolver를 한 번 호출한다. A가 피해와 DefenseResolution을 확정한다. C는 수리검과 발소리 발행자의 현재 위치를 인지·카메라에 대조해 표시 결과를 발행하고 D가 적용한다. 발소리는 적당 하나로 합치고 비네팅은 실제 DamageReceived의 짧은 펄스로 표시한다.
- 상태 소유자: 세션·시간 배율은 `CombatSessionRuntime`, 방어·피해는 A의 `KatanaDamageReceiver`, 투사체 수명과 공격 ID 요청은 B의 `DefenseShuriken`, 인지·표시 결과는 C의 `CombatThreatPresentationProvider`, 마커 풀과 전조 표현은 D의 `ShurikenDirectionIndicator`가 소유한다.
- 구현 상태: 계약 `0.2.0`을 현재 저장소의 정식 BambooDefense 경로에 연결했다. 공통 방어 타입, 세션 런타임과 B/C 공급자, HP·적별 방향 HUD·피격 펄스를 연결했다. 스태미너 게이지는 읽기 계약 슬롯을 구현했으며 실제 공급자를 기다린다. 로그 저장, 강화 공격·극한 회피 연결은 미구현이다. 팀원의 별도 검토·승인으로 간주하지 않는다.

#### 관련 코드

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| Assets/Scripts/Combat/Contracts/CombatSessionRuntime.cs | CombatSessionRuntime | 세션 키·시계·ID 발급·시간 요청 합성 |
| Assets/Scripts/Combat/Contracts/DefenseResolution.cs | DefenseOutcome, IncomingAttackKind, DefenseResolution | 유일한 공통 방어 결과·공격 종류 |
| Assets/Scripts/Combat/Contracts/CombatEventContracts.cs | CombatCorrelationIds, CombatEntityKey, CombatEventIdentity | 사건·대상·원인 식별 |
| Assets/Scripts/Combat/Contracts/ThreatPresentationContracts.cs | ThreatPresentationSnapshot, FootstepPresentationSnapshot, IThreatPresentationProvider | C 표시 결과와 초기 조회/갱신 사건 |
| Assets/Scripts/GameManagers/GameSceneInitializer.cs | Initialize, OnDisable | 세션 런타임 시작과 정리 |
| Assets/Scripts/Player/KatanaV2/KatanaDamageReceiver.cs | ResolveIncomingAttack | B 공격 ID를 보존해 A가 방어·피해 결과를 확정 |
| Assets/Scripts/Defense/DefenseShuriken.cs | Launch, ProcessHit | 공격/투사체 ID 부여와 단일 방어 resolver 호출 |
| Assets/Scripts/Defense/CombatThreatPresentationProvider.cs | PublishThreatSnapshots, HandleFootstep | 인지·카메라 표시 결과와 발소리 사건 발행 |
| Assets/Scripts/Rendering/ThreatPresentationContractAdapter.cs | PublishNow, GetCurrentThreats | C 거리·외형 판정을 공통 결과로 변환하고 기존 공급자에 연결 |
| Assets/Scripts/Enemies/ShurikenPerceptionEffect.cs | LateUpdate | 월드 수리검에 같은 인지 가시성 기준 적용 |
| Assets/Scripts/Defense/ShurikenDirectionIndicator.cs | HandleThreatChanged, UpdateMarker | 세션·대상·Revision 기준 HUD 반영 |
| Assets/Scripts/UI/CombatHud/ | CombatPlayerHud, CombatResourceGauge, CombatFootstepHud, CombatFootstepMarker, CombatHudReferences | HP·스태미너 연결, 발소리 Image 풀, 명시적 HUD 참조 |
| Assets/Scripts/GameManagers/GameStateManager.cs, Assets/Scripts/Player/PlayerMotor.cs | GameOver 정지, 대시 슬로모션 요청 | 공식 경로의 시간 배율 변경을 세션 런타임에 위임 |

#### 연결 및 소유권

- 공유 타입의 단일 원본은 `Assets/Scripts/Combat/Contracts/`의 `BlindBlade.Combat.Contracts`다. `PlayerContracts.cs`나 `DamageContext.cs`에 같은 이름의 전역 타입을 다시 만들지 않는다.
- `DamageContext`와 `PlayerCombatEvents`는 A의 기존 공개 계약을 유지한다. `DefenseResolution`은 공유 네임스페이스의 타입이며 A 구현체가 발행한다.
- `DefenseShuriken`은 한 번의 타격에 `AttackId`, `HitId = 1`, `Kind = Projectile`을 기록한다. 수신자가 `IPlayerDefenseResolver`이면 resolver가 피해까지 적용하므로 호출자는 다시 `TakeDamage`를 부르지 않는다. Resolver가 없는 기존 `PlayerHealth`에는 `TakeDamage` 호환 경로를 쓴다.
- `CombatSessionRuntime`은 `GameSceneInitializer`와 같은 오브젝트에 직렬화되어 있다. 초기화 코드는 컴포넌트가 빠진 씬에서만 추가한다. `BambooDefense.unity`의 `DefenseDirector`에는 `CombatThreatPresentationProvider`가 있고 기존 `_director` 및 12개 마커 참조는 유지한다.
- C 연결을 추가한 공급자는 Threat Presentation Source의 공통 결과를 사용한다. Resolver -20 → Adapter -10 → Provider -5 순으로 LateUpdate를 실행하고, 표시 값은 재판정 없이 보존하며 공급자 Revision만 다시 부여한다. 슬롯이 비어 있는 기존 정식 씬은 아래 감각 마스크 경로를 유지한다. 실제 프리팹·씬 연결은 [위협 표시 판정](위협_표시_판정.md)의 세 번째 단위 인계를 따른다.
- C는 `SensoryPerceptionMask.GetVisibility`, 플레이어 카메라 투영과 Inspector 거리·색상 설정을 이용해 최종 `WorldVisible`, `UiVisible`, `UiDirection`, `Proximity`, `Color`, `Alpha`를 만든다. 월드 `ShurikenPerceptionEffect`도 같은 가시성 임계값을 읽는다. 월드와 UI 표시 플래그는 동시에 켜지지 않는다. D는 거리·차폐·색 판정을 다시 하지 않는다.
- 공식 씬에서는 `CombatSessionRuntime`이 유일한 `Time.timeScale` 쓰기 소유자다. `GameStateManager`는 정지를 요청하고 `PlayerMotor`는 대시 토큰을 등록·해제한다. `TutorialGuide`의 쓰기는 기존 비활성 레거시 코드에 남아 있으며 정식 BambooDefense 씬은 사용하지 않는다.
- 스태미너 인터페이스는 형태만 있으며 실제 공급자가 없다. 미구현 타입을 플레이 가능한 가짜 값으로 채우지 않는다.
- HP·스태미너·발소리 HUD의 Inspector 연결·이미지 교체·공급 전 상태는 [전투 HUD](전투_HUD.md)를 따른다. C는 발소리 감쇠와 만료·사망·세대 변경·종료에서 비활성 스냅샷도 발행해 D 마커를 반환한다.
- 발소리 Origin은 고정하고 표시 방향은 C가 현재 적 위치로 갱신한다. D는 Emitter ID/세대별로 최신 걸음 하나만 표시한다. 실제 피해 비네팅은 DamageReceived에서만 시작하고 unscaledTime으로 종료한다.
- 사용자는 A/B/C 실제 시스템 연결을 명시적으로 요청했다. 변경은 현재 공유 저장소에 반영했지만 외부 팀원에게 메시지를 보내거나 검토·합의가 완료됐다고 표시하지 않았다.

#### Inspector와 씬 연결

- 세션 시간 기본 배율: `BambooDefense / GameSceneInitializer / CombatSessionRuntime / Default Time Scale`.
- C의 위협 표시 기준: `BambooDefense / DefenseDirector / CombatThreatPresentationProvider`의 Visibility Threshold, Near/Far Distance, 색상, Alpha.
- D의 전조 색·깜빡임·페이드·마커 배열: 기존 `ShurikenDirectionIndicator` 및 `BambooDefenseHud` 설정을 유지한다.
- C 공급자와 기존 `BambooDefenseDirector`, HUD 마커 12개의 직렬화 연결을 Editor에서 확인하고 씬을 저장했다.

#### 확인 결과

- Unity Editor 6000.3.22f1에서 스크립트 재컴파일 오류·경고 0건을 확인했다.
- `BambooDefense.unity`는 Unity Editor를 통해 저장했다. `CombatSessionRuntime`과 `CombatThreatPresentationProvider`가 각 기존 루트에 연결되어 있고 기존 HUD 배열 참조가 보존된 것을 확인했다.
- HUD 추가 후 Play에서 초기 HP·실제 사망 게이지·발소리 Image·공급 전 스태미너·비활성/재활성을 확인했다. 실제 입력 전투·패링 전체 인수는 미확인이다. 상세 근거와 제한은 [전투 HUD](전투_HUD.md)를 따른다. 테스트 코드는 작성하거나 실행하지 않았다.
- 남은 범위와 이어서 할 일은 [D 진행 기록](../plans/전투_인지_HUD_D_progress.md)과 [D PRD](../plans/전투_인지_HUD_D_구현_PRD.md)를 따른다.

#### C 공통 결과 연결 후속 기록 (2026-10-14)

- 사용자가 C 표시 규칙을 기준으로 연결하도록 승인했다. 공통 계약 타입·버전 0.2.0은 유지한다.
- C의 거리 색상·외형 Bounds·화면 경계 여유·반사/통과 제외를 공통 결과로 변환한다. HUD 근접도는 공통 3~12m 수평 거리다. Color.a는 1로 만들고 C Color.a × Alpha를 공통 Alpha에 담는다.
- 기존 D 공급자·HUD 연결을 유지하면서 선택 슬롯으로 C 결과를 받는다. WorldVisible 전환 시 방향 마커를 즉시 숨겨 월드와 UI의 페이드 중첩을 막는다. 발소리 정책·HP·스태미너·시간 제어는 변경하지 않는다.
- C 전용 씬에서 사용자 확인: C/D 목록·전달 값 일치, 대표 대상 자격·색·근접도, 생명 세대 변경, 새 세션 ID, 어댑터 중단 시 목록 정리·재활성화, 새 Console 오류 없음.
- 정식 BambooDefense의 B 대상과 C 컴포넌트 연결은 남아 있다. 기존 ShurikenPerceptionEffect의 Renderer.enabled 제어도 실제 월드 통합 때 표시 소유권을 정리해야 한다. 상세 매핑과 Inspector는 위협 표시 판정 명세를 따른다.

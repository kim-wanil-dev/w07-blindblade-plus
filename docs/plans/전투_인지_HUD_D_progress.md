# 전투·인지·HUD D 진행 기록

- 갱신일: 2026-10-14
- 현재 상태: **공통 계약 v0.2.0. HP·적당 하나의 발소리 방향 마커/원호·실제 피격 펄스 구현 및 Play 확인 완료. 스태미너 HUD는 실제 공급자 대기. D3/D4의 나머지 범위는 진행 중.**
- 요청: A/B/C 통합에 이어 사용자가 HP·스태미너·발소리 HUD를 임시 이미지로 만들고 나중에 이미지만 교체 가능하게 요청했다. 프로젝트 내부 수정은 기존 상시 지침에 따라 별도 승인 없이 진행한다.
- 최신 요청 반영: 걸음마다 표시하던 발소리를 적 ID/세대당 하나로 합쳐 현재 적 방향을 추적한다. 저체력 지속 비네팅은 피해 때만 잠깐 나타나는 방식으로 변경했다. 발소리 아이콘 밑에는 수리검과 같은 원호를 추가했다.
- 작업 경로: `C:\Users\JUNGLE\Documents\w07-blindblade-plus`
- 브랜치: `feature/ui`. 이번 작업에서 Git CLI와 커밋은 사용하지 않았다.
- 계획: [D PRD](전투_인지_HUD_D_구현_PRD.md)
- 계약: [공통 계약 및 안전한 변경 규칙](../contracts/전투_인지_HUD_D0_계약.md), 버전 `0.2.0`
- 기능 명세: [전투 HUD](../features/전투_HUD.md), [공통 계약](../features/전투_인지_HUD_공통_계약.md), [전투 판정](../features/전투_판정_계약.md)

## 단계 상태

| 단계 | 현재 상태 | 남은 조건 |
| --- | --- | --- |
| D0 | v0.2.0 공유 저장소 적용 완료 | 별도 담당자 작업공간 대조와 검토 기록은 미완료 |
| D1 | 런타임 코드 적용, 부분 완료 | 재도전/재활성화에서 세션·반복 종료 Play 확인 |
| D2 | 공식 경로 시간 소유권 적용, 부분 완료 | 게임오버·재도전·중복 토큰 해제와 TutorialGuide 활성 여부 확인 |
| D3 | HP 게이지·피격 펄스·스태미너 HUD 연결 슬롯 구현, 부분 완료 | A의 실제 스태미너 공급자, 완성 아트와 전체 인수 |
| D4 | 수리검·발소리 HUD 연결, 부분 완료 | 다른 투사체 유형, 화면 안팎 전체 인수와 일반 패링 Play 확인 |
| D5 | 미착수 | 확정 사건 recorder·저장·전처리 |
| D6 | 미착수 | 강화 공격·극한 회피·보상·파동 연결 |
| D7 | 미착수 | 공용 에셋 값과 전체 Intro→BambooDefense→게임오버→재도전 인수 확인 |
| D8 | 진행 중 | 모든 단계 완료 후 최종 인수 문서 갱신 |

## 이번 통합에서 정한 단일 계약

- `DefenseOutcome`, `IncomingAttackKind`, `DefenseResolution`의 유일한 정의를 `Assets/Scripts/Combat/Contracts/DefenseResolution.cs`의 `BlindBlade.Combat.Contracts`로 통합했다. A의 `DamageContext`와 `PlayerContracts`가 이를 import한다. 전역 중복 타입을 제거해 같은 이름의 API가 둘로 갈라지지 않게 했다.
- `CombatCorrelationIds`의 원인 연결 ID는 기존 `ActionId:int`와 같은 `int`로 정리했다. 미부여 값은 0이며 원본 사건 순번·표시 Revision·시간 요청 ID만 `long`을 쓴다.
- 전투 판정과 실제 피해는 A, 원본 공격/투사체 수명은 B, 가시성과 최종 표시 결과는 C, HUD와 시간 합성은 D가 소유한다. 세부 타입 의미와 비구현 항목은 계약 문서가 기준이다.
- 사용자는 구현을 요청했고 현재 공유 저장소의 연결을 진행했다. 다른 팀원에게 메시지를 보내거나 별도 검토가 끝났다고 기록하지 않았다.

## 코드 통합

### A — 판정 결과

- `KatanaDamageReceiver.ResolveIncomingAttack`가 이제 공통 namespace의 `DefenseResolution`을 반환·발행한다.
- 기존 5인자 `DamageContext` 생성자와 `TryDefendProjectile` 호환 경로는 유지했다. 이번 통합에서 새 공통 타입을 참조하도록 했지만 레거시 호출 전부를 바꾸지는 않았다.
- 일반 패링·회피 무적·스태미너·강화 공격 사건은 기존 계획대로 후속 구현이다.

### B — BambooDefense 수리검

- `DefenseShuriken.Launch`에서 현재 세션의 `AttackId`와 `ProjectileId`를 각각 발급받는다. 단일 접촉 뒤 종료하는 이 수리검은 `HitId = 1`, `Kind = Projectile`로 피해 문맥을 만든다.
- `ProcessHit`은 `IPlayerDefenseResolver`가 있으면 한 번 호출한다. 그 결과가 `Hit`이면 A가 이미 피해를 적용했으므로 B는 `TakeDamage`를 중복 호출하지 않는다. Resolver가 없는 기존 PlayerHealth 호환 경로만 `TakeDamage`를 쓴다.
- 퍼펙트 패링 결과는 기존 B 검기 반격으로 연결한다. 게임플레이 규칙과 수리검 수치 변경은 없다.
- `EnemyHealth.SpawnGeneration`을 활성화마다 증가시켜 재사용된 적을 대상 세대로 구분한다. 기존 `TargetId`는 유지했다.

### C — 인지·발소리 공급자

- 신규 `CombatThreatPresentationProvider`는 활성 수리검 위치를 읽고 `SensoryPerceptionMask.GetVisibility`와 플레이어 카메라 투영으로 `ThreatPresentationSnapshot`을 만든다.
- 스냅샷에는 Session/Target/Revision, 인지·카메라·UI 표시 상태, UI 방향, 월드 위치, 근접도, 색과 Alpha가 포함된다. WorldVisible과 UiVisible은 동시에 true가 되지 않는다.
- `EnemyFootsteps.Footstep`을 구독하고 EventIdentity·세대·고정 발생 원점이 있는 `FootstepPresentationSnapshot`을 D의 발소리 Image HUD에 제공한다.
- 위협이 사라지면 마지막 위치를 가진 비활성 스냅샷을 발행해 D가 이전 표시를 정리할 수 있다.
- 발소리 수명 2초·마지막 0.5초 Alpha 감쇠는 C가 소유한다. 만료·사망·발행자 세대 변경·세션/공급자 종료에서 비활성 스냅샷을 발행한다.
- Origin은 실제 걸음 발생 위치로 고정하지만 Presentation.WorldPosition·UiDirection은 적의 현재 위치로 갱신한다. D는 Emitter ID/세대별 슬롯 하나를 유지하고 SourceSequence를 먼저 비교해 이전 걸음의 만료가 새 마커를 지우지 않게 한다.

### D — 세션·시간·HUD 소비

- 신규 `CombatSessionRuntime`이 세션 키·세션 기준 시계·공격/투사체 ID 발급과 시간 요청을 관리한다. `GameSceneInitializer.Initialize/OnDisable`이 시작·종료한다.
- `GameStateManager`의 이전 `timeScale` 저장/복원을 없애고 게임오버 정지를 런타임에 위임했다. `PlayerMotor` 대시 예고 직접 시간 쓰기를 요청 토큰으로 대체했다.
- 정식 경로의 직접 `Time.timeScale` 대입은 `CombatSessionRuntime` 한 곳으로 모았다. `TutorialGuide`에는 레거시 대입이 남아 있으나 S7에서 제거한 정식 씬에서 참조되는지는 별도 확인이 필요하다.
- `ShurikenDirectionIndicator`는 C 공급자의 세션·대상·Revision 기준 스냅샷을 소비한다. 비행 투사체 Transform을 보관하지 않고, 기존 발사 전조는 BambooDefenseDirector 이벤트를 유지한다.
- `CombatPlayerHud`는 PlayerHealth 초기값·HealthChanged와 선택적인 IPlayerStaminaReadModel을 연결한다. 공급자 없는 스태미너는 fill=0·alpha=0.35·`-- / --`다. 실제 스태미너 규칙을 만들지 않았다.
- `CombatFootstepHud`는 C의 사건별 방향·가시성·색·Alpha를 HUD에 미리 직렬화한 Image 24개에 적용한다. 런타임 Instantiate가 없고 풀이 가득 차면 새 표시를 생략한다. 재활성화 시 초기 스냅샷을 다시 읽고 풀은 유지한다.
- `CombatHudReferences`로 Record·12개 방향 마커·새 HUD 참조를 직렬화했다. BambooDefenseHudAuthoring의 첫 TMP/전체 마커 검색을 교체해 새 값 텍스트가 Record 연결을 바꾸지 않게 했다.
- 발소리 마커마다 DirectionArc 중첩 자식이 있다. 기존 DefenseDirectionMarker를 Show Icon=false로 재사용하고 원호는 아이콘 안쪽의 화면 중심 원 위에 그린다. 기존 수리검 12개는 Show Icon=true를 유지한다.
- PlayerLowHealthVignette는 DamageReceived만 구독하며 Duration 0.3초·Peak Opacity 0.45·Width 0.22로 실제 피격 펄스를 표시한다. HP 초기 조회·회복에서 켜지지 않고 사망 정지에서도 unscaledTime으로 끝난다.

## Unity 씬 연결

열려 있는 `BambooDefense` 씬을 Unity Editor로 수정했다.

| 오브젝트 | 적용한 컴포넌트 / 보존한 연결 |
| --- | --- |
| `/GameSceneInitializer` | `CombatSessionRuntime` 추가, 기본 배율 1 |
| `/DefenseDirector` | `CombatThreatPresentationProvider` 추가. 기존 `BambooDefenseDirector`, `_director` 연결 유지 |
| `BambooDefenseHud` | 기존 Canvas와 `DefenseDirectionMarker` 12개, Graphics 배열 유지 |
| `BambooDefenseHud/CombatStatusCanvas` | HP·스태미너 게이지 및 CombatPlayerHud. PlayerHealth 씬 참조 연결 |
| `CombatStatusCanvas/FootstepMarkers` | CombatFootstepHud. C 공급자와 CombatFootstepMarker.prefab 연결 |

Unity CLI의 component property 조회로 컴포넌트와 기본 Inspector 값 및 HUD 배열 연결을 확인했다. `BambooDefense.unity`를 Editor에서 저장했다. 씬을 재생성하지 않았다.

## 검증 결과와 제한

- Unity Editor 6000.3.22f1이 연결된 상태에서 스크립트 재컴파일 완료(`compilationFailed=false`, 컴파일 오류 0)를 확인했다.
- HUD 코드 재컴파일은 completed, compilationFailed=false, 오류·경고 0건이다.
- Play에서 HP 초기 100/100·fill=1, 실제 전투 사망 후 0/100·fill=0, 공급 전 스태미너 상태를 확인했다.
- 실제 발소리 스냅샷 14개·활성 마커 14개를 확인했다. HUD 비활성/재활성 뒤 풀 24개 유지와 초기 스냅샷 재표시를 확인했다. 사망 후 발소리 목록/표시는 0개였다.
- `UserSettings/CombatHud/Hud-Alive.png`, `Hud-Footsteps-Final.png`, `Hud-GameOver.png`의 GameView 화면을 직접 확인했다. 임시 최대 HP 캡처 Hud-Footsteps.png와 Overlay UI가 빠진 Camera 렌더 캡처 Hud-1920x1080.png는 최종 HUD 화면 근거에서 제외한다.
- 발소리 장시간 관찰을 위한 Play 인스턴스의 임시 최대 HP 10000은 저장하지 않았다. 종료 후 저장된 씬을 다시 열어 최대 HP 100 복원을 확인했다.
- 실제 스태미너 소모·회복, 키보드/패드 전투 전체, 풀 초과·적 재사용·전체 화면비 인수는 미확인이다.
- 테스트 코드는 작성하거나 실행하지 않았다.
- 텔레메트리 JSON Schema `0.1.0`은 유지했으며 recorder·저장 DTO·전처리기는 아직 구현하지 않았다.
- 이전 D0 batch compile 종료 시 TMP 동적 글꼴 에셋 자동 저장이 기록돼 있다. 사본은 `UserSettings/CombatHud/D0-20261014/ChosunCentennial_after_compile.asset`에 남아 있다. 이번에는 live Editor 재컴파일을 썼고 해당 글꼴을 의도적으로 수정하지 않았다.

## 다음 작업자가 이어갈 순서

1. `AGENTS.md` → `docs/FEATURE_SPEC.md` → [D PRD](전투_인지_HUD_D_구현_PRD.md) → 이 문서 → [계약 v0.2.0](../contracts/전투_인지_HUD_D0_계약.md)를 읽는다.
2. 다른 담당자 작업공간과 이 공유 계약의 변경 여부를 대조한다. `BlindBlade.Combat.Contracts` 아래 타입을 다시 전역 복사하지 않는다.
3. A/B/C 소유권 표와 연결 지점을 기준으로 코드 리뷰한다. 특히 resolver 반환 뒤 `TakeDamage` 중복 호출이 없는지 확인한다.
4. Unity Play에서 준비→투척→패링/피해→GameOver→재도전을 수동 확인하고 Console·마커 종료·세션 배율을 기록한다.
5. D3는 A의 실제 스태미너 원본이 준비되면 IPlayerStaminaReadModel을 구현·연결하고 소모·회복을 확인한다. UI 규칙을 복제하지 않는다. 실제 피격 펄스는 구현됐으며 전체 가드/연속 피격 인수를 이어간다.
6. D4에서 다른 위협 유형과 발소리 풀 초과·사망/재사용·화면 경계 전체 인수를 진행한다. C의 표시 규칙을 D에 중복 구현하지 않는다.
7. D5 로그 저장, D6 강화/회피 통합, D7 전체 인수, D8 최종 문서를 계획 순서대로 진행한다.

## 이번 작업에서 수정한 범위

- 코드: `Combat/Contracts` 공통 타입·세션 런타임, `DamageContext`, `PlayerContracts`, `KatanaDamageReceiver`, `PlayerMotor`, `GameSceneInitializer`, `GameStateManager`, `EnemyHealth`, `DefenseShuriken`, `CombatThreatPresentationProvider`, `ShurikenDirectionIndicator`.
- Unity 에셋: `Assets/Scenes/Defense/BambooDefense.unity`의 기존 GameSceneInitializer/DefenseDirector 루트 컴포넌트 연결.
- 문서: 공통 계약, 전투 판정, BambooDefense·게임 씬 공통 구성·게임오버·적 전투 명세, D PRD, 이 progress.
### 2026-10-14 이전 공통 계약 통합 기록

- 월드 수리검의 ShurikenPerceptionEffect가 C 공급자의 VisibilityThreshold를 읽도록 통일했다. BambooDefense Inspector의 _visibilityThreshold를 0으로 맞추고 씬을 Unity Editor에서 저장했다.
- DefenseShuriken.Launch의 XML summary에 공격 ID·투사체 ID 발급과 비행 상태 변경을 명시했다.
- 최신 Unity Editor 재컴파일은 up_to_date, failed=false, 오류 0건, 경고 0건이다.
- 이 이전 계약 통합 시점에는 Play Mode를 실행하지 않았다. 그 뒤의 HUD 추가 검증은 위 현재 상태와 전투 HUD 명세를 따른다.

### 2026-10-14 HP·스태미너·발소리 HUD 인수인계

- 추가 코드: `Assets/Scripts/UI/CombatHud/`의 CombatResourceGauge, CombatPlayerHud, CombatFootstepMarker, CombatFootstepHud, CombatHudReferences. Editor 생성/이관은 CombatHudAuthoring, 연결은 BambooDefenseHudAuthoring이다.
- 수정 에셋: 기존 BambooDefenseHud.prefab·BambooDefense.unity. 새 에셋: CombatFootstepMarker.prefab, UI/Placeholders의 PNG 4개와 Unity가 생성한 meta. 기존 프리팹·씬 GUID와 기록/수리검 연결을 유지했다.
- 이미지 교체: HUD 프리팹의 HealthGauge·StaminaGauge 아래 Icon/Background/Fill → Image Source Image. 발소리 전체 교체는 CombatFootstepMarker.prefab 루트의 Source Image 한 곳이다. PNG 내용을 같은 경로에서 교체할 때 meta를 보존한다.
- 스태미너 원본 확인: 저장소 전체에서 실제 구현이 없고 계약만 있다. 과거 TASK_0084~0087 명세의 PlayerStamina·PlayerStaminaBar는 현재 구현으로 간주하지 않는다. A가 같은 Player에 공급자를 붙이면 바인딩 때 자동 조회하며, 다른 위치면 Stamina Source를 명시하거나 BindSources를 호출한다.
- HUD 도입 당시 요청 범위 밖이었던 피격 전용 비네팅은 후속 요청으로 완료했다. 실제 스태미너 규칙, 다른 투사체, 로그 저장, 강화/극한 회피는 남아 있으므로 D3/D4 전체 완료로 표시하지 않는다.
- 최초 런타임 생성 발소리 풀에서 Play 종료 native `t.GetParent() == nullptr` assertion을 발견했다. 발소리 HUD 비활성 비교에서는 발생하지 않았다. HUD에 중첩 마커 24개를 미리 직렬화한 뒤 사망 후 종료·활성 마커 상태 종료 두 번 모두 새 오류 0건이었다. 효과가 없었던 종료 가드는 제거했다.
- 최종 직렬화 풀 Play 표본: HP 70, 발소리 스냅샷 9, 활성 마커 9, 전체 마커 24. `Hud-Footsteps-Final.png`가 최종 화면 근거다.
- FMOD 종료 경고 `Cannot close System while suspended` 1종은 남는다. HUD 기능 외 FMOD 구현은 수정하지 않았다. 과거 Console의 assertion 이력과 최신 실행의 오류 0을 구분한다.
- 다음 시작점은 A의 IPlayerStaminaReadModel 공급자 연결 또는 D4 다른 위협 유형/전체 인수다. 피격 펄스는 아래 후속 요청에서 완료했다. 이미지 교체 방법·기본값은 전투 HUD 명세에 있다.
- 최종 Editor 상태: BambooDefense 편집 모드, scene.isDirty=false, CombatStatusCanvas 활성, 최대 HP 100. Missing Script 0, Record 참조 유지, 방향 마커 12/누락 0, 중첩 발소리 마커 24, 새 Image 30/Source Image 누락 0/Raycast Target 0. 재설치 후에도 동일 수량으로 유지됐다.

### 2026-10-14 후속 요청: 적별 방향·원호·실제 피격 펄스

- 수정 코드: CombatFootstepHud, CombatThreatPresentationProvider, CombatFootstepMarker, DefenseDirectionMarker, PlayerLowHealthVignette, CombatHudAuthoring. 공개 스냅샷의 필드·원본 EventIdentity·고정 Origin은 유지한다.
- 발소리: raw 걸음 9개/적 1명/마커 1개, raw 13개/적 2명/마커 2개. C 표시 위치와 현재 적 위치의 차이는 0, Origin과 현재 표시 위치는 0.22~0.56m 차이를 확인했다. 이전 걸음의 갱신·종료는 SourceSequence로 거부한다.
- 원호: 프리팹에 미리 직렬화한 24개 마커에 DirectionArc 자식을 추가했다. 아이콘 1개가 활성일 때 원호도 1개였고 만료 뒤 둘 다 0개였다. 기존 수리검 배열은 12개, Show Icon=true 유지. 새 원호는 false·Raycast Target=false다.
- 비네팅: HealthChanged를 제거하고 DamageReceived에 구독했다. 최대 불투명도/두께의 이전 필드는 FormerlySerializedAs로 이관했고 Player.prefab·씬에는 새 0.3초/0.45/0.22 값을 저장했다. 최소값/HP 임계값은 제거했다. 기존 클래스명·meta·셰이더는 보존했다.
- 실제 피해 API 관찰: HP 99와 HP 25에서 peak 0.45/width 0.22로 시작해 opacity 0/비활성으로 종료, 회복에서는 비활성 유지. HP 0/timeScale 0 치명타에서도 같은 펄스 후 종료했다.
- 화면 근거: UserSettings/CombatHud/Hud-EnemyDirection-Arc.png(HP 60·방향 아이콘/원호·지속 비네팅 없음), Hud-DamagePulse-Expired.png(사망 후 붉은 가장자리 없음). 이전 걸음별 정책 화면과 구분한다.
- 컴파일 오류 0. 테스트 코드는 작성하지 않았으며 CLI의 실제 Play API·읽기 전용 상태 조회·GameView 화면으로 확인했다. GameView가 비활성일 때 프레임이 멈춰 검증 중 runInBackground만 임시 true로 사용했고 false로 복원했다. 임시 HP/피해/freeze는 Play에만 적용하고 저장하지 않았다.
- 실제 키보드/패드 전투 전체·24명 초과·연속 피해 정밀 시간 측정은 미확인이다. 원호 튜닝은 CombatFootstepMarker.prefab의 Arc Thickness/Angle/Gap/Tint, 피격 펄스 튜닝은 Player의 PlayerLowHealthVignette에서 한다.
- 최종 컴파일 completed/compilationFailed=false/오류 0. 기존 KatanaDebugOverlay CS0414는 남는다. 현재 종료 검증에서 native `t.GetParent() == nullptr` assertion이 재현됐다. 이번 수정 전 동일 Editor 로그(10:25 UTC, seq 158~166)에 이미 9건 있었으며 발소리 HUD를 비활성화한 비교에서도 2건 발생했다. UI 표시/펄스 검증과 종료 문제를 구분한다. 원인은 미확인이고 이번 범위에서 기존 종료 문제를 해결했다고 표시하지 않는다.
- 비교 중 비활성화한 FootstepMarkers는 저장하지 않고 BambooDefense를 다시 열어 복원했다. Player.prefab과 씬의 Duration/Opacity/Width는 0.3/0.45/0.22로 일치한다. 최신 원호 프리팹과 씬은 저장 상태다.

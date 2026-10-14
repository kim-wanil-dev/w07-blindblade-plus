### 전투 HUD

- 기능 설명: BambooDefense의 HP·스태미너 게이지와 실제 적 발소리 방향을 표시한다. 임시 Sprite를 프리팹의 Image Source Image에서 교체할 수 있도록 시각 요소와 데이터 연결을 분리한다.
- 동작 조건 및 처리 흐름: HP는 PlayerHealth의 초기 값과 HealthChanged를 읽는다. 스태미너는 IPlayerStaminaReadModel의 Current·Changed를 읽으며 공급자가 없으면 빈 게이지와 미연결 값을 표시한다. 발소리는 C의 FootstepPresentationSnapshot만 소비한다.
- 구현 상태: 2026-10-14 HP 게이지·발소리 HUD의 실제 공급자 연결과 스태미너 게이지·읽기 계약 연결 슬롯 구현 완료. 스태미너 게임 규칙은 A 소유이며 현재 공급자가 없어 빈 게이지·`-- / --`·불투명도 0.35로 표시한다.

#### 관련 코드

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| Assets/Scripts/UI/CombatHud/CombatResourceGauge.cs | SetValue, SetUnavailable | 게이지 값·보간·공급 전 표시 |
| Assets/Scripts/UI/CombatHud/CombatPlayerHud.cs | BindSources | HP·스태미너 초기 조회와 구독 수명 |
| Assets/Scripts/UI/CombatHud/CombatFootstepHud.cs | HandleFootstep | 세션·사건별 발소리 표시 풀 |
| Assets/Scripts/UI/CombatHud/CombatFootstepMarker.cs | Show, Hide | 교체 가능한 Image에 C 표시 결과 적용 |
| Assets/Scripts/UI/CombatHud/CombatHudReferences.cs | RecordText, DirectionMarkers, PlayerHud, FootstepHud | HUD 내부의 명시적 직렬화 참조 |
| Assets/Scripts/Defense/CombatThreatPresentationProvider.cs | ExpireFootsteps, PublishEndedFootsteps | C의 감쇠·만료·발행자 사망/세대 변경·종료 사건 |
| Assets/Editor/CombatHudAuthoring.cs | Install | 임시 Sprite·HUD 프리팹 생성 및 기존 씬 연결 |
| Assets/Editor/BambooDefenseHudAuthoring.cs | Connect | Record·수리검 배열·HP·스태미너·C 공급자 연결 |

#### 연결 및 협업

- 공통 계약 0.2.0을 사용한다. HP·스태미너 규칙은 A, 발소리의 자격·가시성·원점·색·수명은 C, 표시 풀·배치·이미지는 D가 소유한다.
- 기존 기록 텍스트·수리검 방향 마커 12개·게임오버 UI 연결을 유지한다. 기존 HUD 연결 도구는 CombatHudReferences의 명시적 참조를 사용한다. 값 텍스트가 추가돼도 첫 TMP 텍스트를 잘못 연결하지 않는다.
- 사용자가 HP·스태미너·발소리 HUD 구현과 나중에 이미지만 교체 가능한 구성을 요청했다. 다른 작업자의 새 구현과 충돌하는 공급자는 현재 발견되지 않았다.
- 스태미너 담당자는 `BlindBlade.Combat.Contracts.IPlayerStaminaReadModel`의 `Current`와 `Changed`를 구현한다. 같은 Player에 붙이면 HUD가 바인딩할 때 한 번 찾는다. 다른 오브젝트라면 씬의 CombatPlayerHud / Stamina Source에 연결한다. 실행 중 공급자를 추가·교체할 때는 `BindSources(health, source)`로 즉시 다시 연결한다.
- 발소리 HUD는 `Origin`이나 적 Transform을 추적하지 않는다. C의 UiDirection·Proximity·Color·Alpha를 적용하고, WorldVisible이면 UiVisible=false인 기존 정책을 따른다. 세션·Target·Revision으로 이전 표시 갱신을 거부한다.
- 풀은 HUD 프리팹에 미리 직렬화한 마커 24개를 재사용한다. 런타임 Instantiate는 없다. 빈 슬롯이 없으면 추가 표시는 생략하고 종료·숨김으로 슬롯을 반환한다. 비활성/재활성 시 구독과 초기 스냅샷을 다시 연결하며 풀을 재생성하지 않는다.

#### Inspector 설정과 이미지 교체

HUD 프리팹: `Assets/Scenes/Defense/UI/BambooDefenseHud.prefab`.

| 교체 대상 | 프리팹 내부 경로 / Inspector |
| --- | --- |
| HP 아이콘 | CombatStatusCanvas/HealthGauge/Icon → Image / Source Image |
| HP 게이지 배경·채움 | CombatStatusCanvas/HealthGauge/Background·Fill → Image / Source Image |
| 스태미너 아이콘 | CombatStatusCanvas/StaminaGauge/Icon → Image / Source Image |
| 스태미너 게이지 배경·채움 | CombatStatusCanvas/StaminaGauge/Background·Fill → Image / Source Image |
| 발소리 아이콘 전체 | `Assets/Scenes/Defense/UI/CombatFootstepMarker.prefab` 루트 → Image / Source Image. HUD의 중첩 프리팹 마커 전체에 적용 |

- 새 이미지는 Texture Type을 Sprite (2D and UI)로 가져온 뒤 Source Image에 연결한다. 같은 임시 PNG 파일 내용만 바꿀 수도 있다. 이때 `.meta`를 유지하면 기존 GUID 연결이 유지된다. 표시 코드 변경은 필요 없다.
- 임시 PNG는 `Assets/Scenes/Defense/UI/Placeholders/`의 HealthPlaceholder·StaminaPlaceholder·FootstepPlaceholder·BarPlaceholder다. 흰색 알파 이미지에 HP 붉은색·스태미너 녹색·발소리 C 정책 색을 곱한다. 완성 이미지도 투명 배경 단색 Sprite를 사용하면 현재 색 정책이 유지된다.
- HP·스태미너의 Icon/Background/Fill/Value RectTransform에서 배치·크기를, Image Color와 Value TMP에서 색·글꼴·글자 크기를 수정한다. Fill의 Image Type=Filled, Fill Method=Horizontal은 유지한다.
- CombatStatusCanvas는 Overlay, Sorting Order 3, 기준 1920×1080, Match 0.5다. HP는 좌하단 (48,98), 360×34, 스태미너는 (48,56), 300×28이다. 새 표시용 Graphic은 모두 Raycast Target=false이며 게이지 CanvasGroup도 입력을 가로채지 않는다.
- CombatResourceGauge: Transition Duration 0.12초, Unavailable Alpha 0.35. 표시 보간만 unscaledDeltaTime을 사용한다.
- FootstepMarkers / CombatFootstepHud: Markers 배열 24개, Radius 310, Far Size 24, Near Size 36. 화면 가용 반경 안으로 제한한다. 풀을 늘릴 때는 HUD 프리팹에 마커 중첩 인스턴스를 추가하고 Markers 배열에도 연결한다. 발소리 Icon의 Sprite는 위 전용 프리팹에서 교체한다.
- DefenseDirector / CombatThreatPresentationProvider: Footstep Lifetime 2초, Fade Duration 0.5초. C가 실시간 수명·감쇠·종료 상태를 발행한다.
- 재설치 메뉴는 `BlindBlade/대숲 방어/HUD/임시 전투 HUD 설치·연결`이다. 현재 BambooDefense 편집 모드에서 실행한다. 기존 이미지·새 Canvas·프리팹이 있으면 재생성하지 않으며 원본 연결만 갱신한다.

#### 검증

- Unity Editor 6000.3.22f1 재컴파일 completed, compilationFailed=false, 오류·경고 0건.
- 최종 저장 씬 조회: Missing Script 0, 기존 방향 마커 12/누락 0, 중첩 발소리 마커 24, 새 Image 30/이미지 누락 0/Raycast Target 0. HUD 설치를 다시 실행해도 중복되지 않았다. 편집 모드·씬 저장 상태·HP 기본값 100으로 종료했다.
- BambooDefense Play에서 HP 100/100과 fill=1, 실제 전투 사망 뒤 HP 0/100과 fill=0, 공급자 없는 스태미너 fill=0·alpha=0.35를 확인했다.
- 실제 적 발걸음에서 공급자 스냅샷 14개와 활성 Image 마커 14개를 확인했다. HUD 두 컴포넌트를 껐다 켜도 마커 풀은 24개였고 실제 스냅샷이 다시 표시됐다. 사망 뒤 공급자 목록과 활성 발소리 마커는 0개였다.
- 최종 직렬화 풀에서는 실제 HP 70·발소리 스냅샷 9개·활성 마커 9개·총 슬롯 24개를 다시 확인했다. 활성 발소리가 있는 상태의 Play 종료에서도 새 오류가 없었다.
- 장시간 발소리 화면 관찰 중 Play 인스턴스의 최대 HP만 임시로 10000으로 올렸다. 종료 후 저장된 씬을 다시 열어 기본 최대 HP 100을 복원했다. 이 값은 에셋에 저장하지 않았다.
- UI가 포함된 GameView 화면을 직접 확인했다. 근거는 `UserSettings/CombatHud/Hud-Alive.png`, `Hud-Footsteps-Final.png`, `Hud-GameOver.png`다. 임시 최대 HP를 사용한 이전 Hud-Footsteps.png와 Overlay UI가 없는 CLI Camera 렌더 screenshot은 최종 HUD 화면 근거에서 제외했다.
- 최초 런타임 생성 풀에서 Play 종료 시 Unity native `t.GetParent() == nullptr` assertion을 발견했다. HUD 프리팹의 중첩 마커 24개를 직렬화하는 방식으로 교체한 뒤 사망 후 종료와 활성 발소리 중 종료에서 새 오류 0건을 확인했다. 종료 중 그래픽 갱신을 막는 임시 가드는 효과가 없어 제거했다.
- FMOD의 Play 종료 경고 `Cannot close System while suspended` 1종이 남는다. HUD 코드의 예외는 아니며 FMOD 구현은 수정하지 않았다. 기존 KatanaDebugOverlay CS0414도 과거 Console 이력에 있다.
- 실제 스태미너 소모·회복은 공급자 미구현으로 미확인이다. 완성 아트, 전체 해상도 인수, 발소리 풀 초과, 적 재사용의 전체 시나리오는 미확인이다. 테스트 코드는 작성하지 않았다.

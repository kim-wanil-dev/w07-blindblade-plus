### 전투 HUD

- 기능 설명: BambooDefense의 HP·스태미너 게이지, 적 한 명당 하나의 발소리 방향 마커와 실제 피격 때만 나타나는 짧은 비네팅을 표시한다. 임시 Sprite는 프리팹의 Image Source Image에서 교체한다.
- 동작 조건 및 처리 흐름: HP는 PlayerHealth의 초기 값과 HealthChanged를 읽는다. 스태미너는 IPlayerStaminaReadModel의 Current·Changed를 읽으며 공급자가 없으면 빈 게이지와 미연결 값을 표시한다. 발소리는 C의 FootstepPresentationSnapshot만 소비한다.
- 구현 상태: 2026-10-14 HP·적별 방향 마커·실제 피격 펄스 구현/연결 완료. 스태미너 게이지와 읽기 계약 슬롯은 구현했으며 실제 공급자가 없어 빈 게이지·`-- / --`·불투명도 0.35로 표시한다.

#### 관련 코드

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| Assets/Scripts/UI/CombatHud/CombatResourceGauge.cs | SetValue, SetUnavailable | 게이지 값·보간·공급 전 표시 |
| Assets/Scripts/UI/CombatHud/CombatPlayerHud.cs | BindSources | HP·스태미너 초기 조회와 구독 수명 |
| Assets/Scripts/UI/CombatHud/CombatFootstepHud.cs | HandleFootstep | 세션·발행자 ID/세대별 표시 풀, 최신 걸음 순번/Revision 비교 |
| Assets/Scripts/UI/CombatHud/CombatFootstepMarker.cs | Show, Hide | 교체 가능한 Image와 그 아래 방향 원호에 C 표시 결과 적용 |
| Assets/Scripts/Defense/DefenseDirectionMarker.cs | Configure, OnPopulateMesh | 기존 수리검/원호 도형 재사용, 발소리에서는 아이콘 없이 원호만 그리기 |
| Assets/Scripts/UI/CombatHud/CombatHudReferences.cs | RecordText, DirectionMarkers, PlayerHud, FootstepHud | HUD 내부의 명시적 직렬화 참조 |
| Assets/Scripts/Defense/CombatThreatPresentationProvider.cs | ExpireFootsteps, PublishEndedFootsteps | C의 감쇠·만료·발행자 사망/세대 변경·종료 사건 |
| Assets/Scripts/UI/PlayerLowHealthVignette.cs | HandleDamageReceived, ApplyVignette, ResetPulse | 실제 피해 구독, 고정 두께·강도의 짧은 피격 펄스 |
| Assets/Scripts/UI/CombatHud/CombatPerfectParryTokenHud.cs | ApplyTokens, Update, CreateOrbitThreads, UpdateOrbitThreads | 퍼펙트 패링 토큰 3칸 표시(2026-10-15). 2026-10-16 찬 토큰을 실 두 가닥이 감싸는 구체로 표시, 토큰 크기 2배, 빈 토큰 이미지 추가. 규칙과 이미지 교체는 [퍼펙트 패링 토큰](퍼펙트_패링_토큰.md) |
| Assets/Scripts/UI/CombatHud/CombatTokenOrbitThread.cs, CombatTokenOrbitStyle.cs, ThreadSphere.cs, ThreadSphereFlow.cs | OnPopulateMesh / 설정값 / 경로·흐름 | 토큰 실 구체 UI 메시와 설정, 칼 구슬과 공유하는 실 구체 계산. 상세는 [퍼펙트 패링 토큰](퍼펙트_패링_토큰.md) |
| Assets/Editor/CombatHudAuthoring.cs | Install | 임시 Sprite·HUD 프리팹 생성 및 기존 씬 연결 |
| Assets/Editor/BambooDefenseHudAuthoring.cs | Connect | Record·수리검 배열·HP·스태미너·C 공급자 연결 |

#### 연결 및 협업

- 공통 계약 0.2.0을 사용한다. HP·스태미너 규칙은 A, 발소리의 자격·가시성·원점·색·수명은 C, 표시 풀·배치·이미지는 D가 소유한다.
- 기존 기록 텍스트·수리검 방향 마커 12개·게임오버 UI 연결을 유지한다. 기존 HUD 연결 도구는 CombatHudReferences의 명시적 참조를 사용한다. 값 텍스트가 추가돼도 첫 TMP 텍스트를 잘못 연결하지 않는다.
- 사용자가 HP·스태미너·발소리 HUD 구현과 나중에 이미지만 교체 가능한 구성을 요청했다. 다른 작업자의 새 구현과 충돌하는 공급자는 현재 발견되지 않았다.
- 스태미너 담당자는 `BlindBlade.Combat.Contracts.IPlayerStaminaReadModel`의 `Current`와 `Changed`를 구현한다. 같은 Player에 붙이면 HUD가 바인딩할 때 한 번 찾는다. 다른 오브젝트라면 씬의 CombatPlayerHud / Stamina Source에 연결한다. 실행 중 공급자를 추가·교체할 때는 `BindSources(health, source)`로 즉시 다시 연결한다.
- 발소리 HUD는 `Origin`이나 적 Transform을 추적하지 않는다. 공급자의 UiDirection·Proximity·Color·Alpha를 적용하며, 적의 외형이 화면에 들어오면 UiVisible=false로 마커를 즉시 숨긴다. 세션·Target·Revision으로 이전 표시 갱신을 거부한다.
- 방향 HUD의 공급자에 C 공통 결과 어댑터를 연결하면 C의 표시 규칙을 사용한다. WorldVisible로 전환할 때 방향 마커를 즉시 숨기며 다른 종료의 기존 페이드는 유지한다. 발소리 HUD·마커 풀은 이번 연결에서 변경하지 않았다. C 전용 씬의 공통 값 전달은 사용자 확인했고, 정식 HUD의 실제 화면 전환은 B/월드 연결 이후 확인한다.
- 풀은 HUD 프리팹에 미리 직렬화한 마커 24개를 재사용한다. 런타임 Instantiate는 없다. 빈 슬롯이 없으면 추가 표시는 생략하고 종료·숨김으로 슬롯을 반환한다. 비활성/재활성 시 구독과 초기 스냅샷을 다시 연결하며 풀을 재생성하지 않는다.
- 발소리 HUD는 `Emitter(EntityId, SpawnGeneration)`별로 슬롯을 하나만 쓴다. 같은 적의 다음 걸음은 기존 슬롯을 갱신한다. 걸음의 SourceSequence를 먼저, 같은 걸음에서는 Revision을 비교해 이전 걸음의 갱신·만료가 최신 마커를 지우지 않게 한다.
- 실제 `Origin`은 고정 발생 위치로 보존한다. 공급자가 `Presentation.WorldPosition`과 방향·근접도·가시성을 **발행자 적의 현재 위치**로 갱신하고 D는 그 결과만 적용한다. 방향은 카메라의 수평 회전 기준으로 앞=위, 뒤=아래, 오른쪽=오른쪽이다. D는 적 Transform을 저장하거나 인지를 재계산하지 않는다. 월드 발자국 효과는 유지한다.
- 마지막 걸음의 인지가 살아 있는 동안 적의 현재 방향을 따라간다. 기존 공급자 경로는 적 외형 Renderer Bounds와 카메라 프러스텀의 교차를 확인하고 화면 안에서는 숨긴다. 마지막 사건의 수명 2초 뒤 사라진다. C 최신 후보 슬롯을 연결한 경로는 별도 청각 범위 기본 20m, 수명 0.8 게임 초·마지막 0.3 게임 초 페이드를 사용한다. 두 경로 모두 화면 안 적의 발소리 UI를 숨기며 사망·세대 변경·세션 종료에서도 정리한다.
- 발소리 아이콘 아래에는 수리검과 같은 화면 중심 원의 호가 있다. 아이콘·원호는 같은 방향·Alpha를 사용하고 함께 숨긴다. 기존 DefenseDirectionMarker의 도형을 재사용하며 발소리용 DirectionArc는 Show Icon=false, 기존 수리검 12개는 true다. 명시적 참조 덕분에 새 원호가 수리검 배열에 섞이지 않는다.
- 비네팅은 `PlayerHealth.DamageReceived`에만 구독한다. HP 초기값·회복·최대치 변경에는 켜지지 않는다. 피해마다 같은 강도·두께로 펄스를 다시 시작하며 연속 피해를 누적해서 화면을 키우지 않는다. 치명타 후 시간 정지에서도 실시간으로 끝난다.
- 풀은 HUD 프리팹에 미리 직렬화한 마커 24개를 재사용한다. 최대 24명의 적을 동시에 표시하며 런타임 Instantiate는 없다. 빈 슬롯이 없으면 새 적의 표시는 생략한다. 비활성/재활성 시 구독과 초기 스냅샷을 다시 연결하며 풀을 재생성하지 않는다.

#### Inspector 설정과 이미지 교체

HUD 프리팹: `Assets/Scenes/Defense/UI/BambooDefenseHud.prefab`.

| 교체 대상 | 프리팹 내부 경로 / Inspector |
| --- | --- |
| HP 아이콘 | CombatStatusCanvas/HealthGauge/Icon → Image / Source Image |
| HP 게이지 배경·채움 | CombatStatusCanvas/HealthGauge/Background·Fill → Image / Source Image |
| 스태미너 아이콘 | CombatStatusCanvas/StaminaGauge/Icon → Image / Source Image |
| 스태미너 게이지 배경·채움 | CombatStatusCanvas/StaminaGauge/Background·Fill → Image / Source Image |
| 발소리 아이콘 전체 | `Assets/Scenes/Defense/UI/CombatFootstepMarker.prefab` 루트 → Image / Source Image. HUD의 중첩 프리팹 마커 전체에 적용 |
| 퍼펙트 패링 토큰 | CombatStatusCanvas/PerfectParryTokens → Combat Perfect Parry Token Hud / Filled Sprite·Empty Sprite(Image Source Image는 실행 중 이 값으로 바뀜). 좌하단 기준 (48,845), 184×64, 스태미너 게이지 아래(2026-10-16 토큰 56×56·간격 64px로 키움, 이전 116×32). 2026-10-16 Empty Sprite를 `Placeholders/TokenEmptySlot.png`, Empty Color를 (0.62,0.62,0.64,0.85)로 바꿨고, 실 공전 설정은 같은 컴포넌트의 "실 공전" 묶음이다. 찬 슬롯은 Image를 끄고, 실행 중 각 Token_i 자식 `Token_i_Orbit`이 실 구체를 그린다 |

- 새 이미지는 Texture Type을 Sprite (2D and UI)로 가져온 뒤 Source Image에 연결한다. 같은 임시 PNG 파일 내용만 바꿀 수도 있다. 이때 `.meta`를 유지하면 기존 GUID 연결이 유지된다. 표시 코드 변경은 필요 없다.
- 임시 PNG는 `Assets/Scenes/Defense/UI/Placeholders/`의 HealthPlaceholder·StaminaPlaceholder·FootstepPlaceholder·BarPlaceholder다. 흰색 알파 이미지에 HP 붉은색·스태미너 녹색·발소리 C 정책 색을 곱한다. 완성 이미지도 투명 배경 단색 Sprite를 사용하면 현재 색 정책이 유지된다.
- HP·스태미너의 Icon/Background/Fill/Value RectTransform에서 배치·크기를, Image Color와 Value TMP에서 색·글꼴·글자 크기를 수정한다. Fill의 Image Type=Filled, Fill Method=Horizontal은 유지한다.
- CombatStatusCanvas는 Overlay, Sorting Order 3, 기준 1920×1080, Match 0.5다. HP는 좌하단 (48,98), 360×34, 스태미너는 (48,56), 300×28이다. 새 표시용 Graphic은 모두 Raycast Target=false이며 게이지 CanvasGroup도 입력을 가로채지 않는다.
- CombatResourceGauge: Transition Duration 0.12초, Unavailable Alpha 0.35. 표시 보간만 unscaledDeltaTime을 사용한다.
- FootstepMarkers / CombatFootstepHud: Markers 배열 24개, Radius 310, Far Size 24, Near Size 36. 화면 가용 반경 안으로 제한한다. 풀을 늘릴 때는 HUD 프리팹에 마커 중첩 인스턴스를 추가하고 Markers 배열에도 연결한다. 발소리 Icon의 Sprite는 위 전용 프리팹에서 교체한다.
- `CombatFootstepMarker.prefab`의 CombatFootstepMarker / 방향 호: Arc Thickness 4, Arc Angle 18도, Icon Arc Gap 6, Arc Tint 흰색. Tint는 C 색상에 곱한다. DirectionArc 자식의 Show Icon=false는 유지한다. 원호는 아이콘보다 화면 중심 쪽에 배치되고 원의 중심은 화면 중심으로 맞춘다. 새 원호도 Raycast Target=false다.
- DefenseDirector / CombatThreatPresentationProvider: 미연결 기존 발소리는 Footstep Lifetime 2초, Fade Duration 0.5초다. Footstep Candidate Source에 C EnemyFootstepCandidates를 연결하면 Assets/Settings/Perception/ThreatPresentationSettings.asset의 Footstep Hearing Radius 20m, Lifetime 0.8 게임 초, Fade Duration 0.3 게임 초를 사용한다. 새 정책의 범위·연결·검증은 [발소리 후보](발소리_후보.md)를 따른다.
- Player 프리팹/씬의 `PlayerLowHealthVignette`: Duration 0.3초, Peak Opacity 0.45, Width 0.22. 색상과 일렁임도 해당 Inspector에서 조절한다. 이름·스크립트 GUID·셰이더 연결은 유지했다. `_maxVignetteOpacity`→`_peakOpacity`, `_maxVignetteWidth`→`_width`는 FormerlySerializedAs로 이관하고 정식 프리팹·씬에는 새 기본값을 저장했다. 과거 최소값 필드와 HP 임계값은 제거했다.
- 재설치 메뉴는 `BlindBlade/대숲 방어/HUD/임시 전투 HUD 설치·연결`이다. 현재 BambooDefense 편집 모드에서 실행한다. 기존 이미지·새 Canvas·프리팹이 있으면 재생성하지 않으며 원본 연결만 갱신한다.

#### 적별 방향·피격 펄스 변경 검증

- 재컴파일 up_to_date, failed=false, 오류·경고 0건. 열린 BambooDefense의 연결을 유지하고 Player 프리팹과 씬의 펄스 설정을 Unity Editor로 저장했다.
- 자연 전투 표본 1: 유효 걸음 9개·발행자 1명·표시 마커 1개. 표본 2: 유효 걸음 13개·발행자 2명·표시 마커 2개. 전체 슬롯은 24개로 유지됐다.
- 두 표본에서 C 표시 위치와 적의 현재 위치 차이는 0이었다. 고정 Origin과 표시 위치는 0.22~0.56m 달라져 현재 적을 따라가는 것을 확인했다.
- 원호 추가 후 걸음 9개·발행자 1명·마커 1개·활성 원호 1개를 확인했다. 인지 만료 후 아이콘·원호는 모두 0개였다. 저장 씬에는 발소리 원호 24개, 수리검 12개가 있고 새 원호의 Raycast Target은 모두 false다. 기존 수리검 Show Icon은 12개 모두 true다.
- 실제 피해 경로를 호출해 HP 99와 HP 25 모두 peak=0.45·width=0.22를 확인했다. 뒤이어 opacity=0·Image 비활성으로 바뀌었다. 회복 뒤에는 비활성을 유지했다.
- 치명타 HP 0·timeScale 0에서도 즉시 peak=0.45로 켜지고 이후 opacity=0·비활성으로 끝났다. 체력 부족으로 지속되는 비네팅은 없다.
- GameView의 사망 후 화면에서 가장자리 붉은 효과가 남지 않는 것을 확인했다. `UserSettings/CombatHud/Hud-DamagePulse-Expired.png`가 근거다. 실제 키보드/패드 가드·연속 피격 전체 인수는 미확인이다. 테스트 코드는 작성하지 않았다.
- `UserSettings/CombatHud/Hud-EnemyDirection-Arc.png`에서 적별 발소리 아이콘과 그 아래 원호, HP 60에서 지속 비네팅이 없는 화면을 확인했다. 검증 중 임시 freeze·runInBackground는 저장하지 않았고 종료 시 복원했다.
- 최종 재컴파일 completed, compilationFailed=false, 오류 0건. 기존 KatanaDebugOverlay CS0414 경고가 Console에 있다. Play 종료 시 Unity native `t.GetParent() == nullptr` assertion도 재현됐다. 이번 수정 전 `2026-10-14 10:25 UTC` 로그에 같은 assertion 9건이 있었고, 발소리 HUD를 비활성화한 자연 사망/종료 비교에서도 2건이었다. 원인은 미확인으로 남기며 새 원호만의 문제로 판단하지 않는다. 이전 도입 단계의 통과 기록을 전체 종료 문제 해결 근거로 재사용하지 않는다.

#### 이전 HUD 도입 검증 (걸음별 마커 정책 당시 기록)

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

#### 2026-10-15 발자국 화면 진입·방향 보정

- 정식 BambooDefense의 공급자 C 연결 슬롯 두 개가 비어 있어 기존 발소리·수리검 경로를 수정했다. 기존 발소리 사건은 적 Renderer 배열을 보관하고 매 갱신마다 카메라 화면 교차를 확인한다. 화면 안 적의 발자국 아이콘·원호는 숨기고, 화면 밖에서만 최신 걸음을 표시한다. C 후보 경로도 동일한 화면 진입 규칙을 사용한다.
- 기존 공급자, C 발소리 후보, C 수리검 어댑터의 `UiDirection`은 현재 카메라의 수평 회전과 대상의 현재 위치로 계산한다. 뒤 정중앙=아래, 앞=위, 오른쪽=오른쪽이다. 기존 수리검 발사 전조는 PlayerMotor의 수평 시선으로 이미 같은 기준을 사용한다.
- Unity Editor 재컴파일 완료, 오류·경고 0건. 현재 열린 씬은 IntroScene이며 실제 BambooDefense Play에서 적이 화면 경계를 넘는 순간의 시각 확인은 아직 수행하지 않았다.

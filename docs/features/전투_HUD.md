> 2026-10-23 정식 전투 변경: 일섬·거합 자원과 Shift/Q 입력을 분리한다. 패링당 거합 1/3·보스 일반 공격 유효 적중당 3%p를 적용하며, 현재 규칙과 검증은 [특수 스킬 게이지](특수스킬_게이지.md)를 따른다. 온보딩 개편은 이번 범위에서 제외한다.

> 2026-10-23 일섬 토큰 표시 변경: PerfectParryTokens의 칸 표시는 실행 중 각 Token_i 자식 `Token_i_LiquidBody`·`Token_i_Liquid`가 그리는 실 액체다. 아래 `Token_i_Orbit`·"실 공전" 묶음 기록은 이전 표시이며 현재 설정은 [실 액체 게이지](실_액체_게이지.md)를 따른다.

### 전투 HUD
#### 수리검 방향 인디케이터 Sprite 설정 (2026-10-26)

- 흰색 네 날 수리검 Sprite를 `Assets/Scenes/Defense/UI/Indicators/AttackIndicator-shuriken-white.png`에 저장했다. 투명 배경이며, 기존 마커의 도형을 Sprite로 옮겼다.
- `Assets/Scenes/Defense/UI/BambooDefenseHud.prefab`의 `BambooDefenseHud > CombatHudReferences > 수리검 방향 인디케이터 > 수리검 인디케이터 이미지` 한 필드가 모든 방향 마커의 Sprite를 지정한다. 변경은 `OnValidate`에서 프리팹 편집 화면에 반영되고, `Awake`에서 실행 시에도 각 마커에 적용된다.
- 각 `DirectionMarkers/Marker_n`에는 저장된 `ShurikenIcon` 자식 `Image`가 있다. 런타임 생성 없이 방향 원호와 아이콘을 별도 그래픽으로 그리며, 기존 거리별 배율·알파·흰색/노란색/빨간색 경고 색을 아이콘에도 적용한다. `_showIcon`은 기존처럼 아이콘 표시 여부를 조절한다.
- 아이콘 크기·원호 배치는 각 `DefenseDirectionMarker`의 `_iconSize`, `_radius`, `_arcThickness`로 유지한다. HUD 공용 프리팹이 적용되는 씬에서 공유 설정을 사용한다.
- 관련 코드: `Assets/Scripts/Defense/DefenseDirectionMarker.cs` / `SetShurikenIconSprite`, `SetIconScale`, `Configure`, `ApplyIconLayout`; `Assets/Scripts/UI/CombatHud/CombatHudReferences.cs` / `ApplyDirectionMarkerSprite`.
- 검증: Unity 재컴파일 오류 0개·경고 0개. Editor에서 Sprite 로드, Inspector 필드 연결, 12개 방향 마커 모두의 자식 Image 참조를 확인했다. 실제 Play 화면은 미확인이다.
#### 수리검 도달 예상 시간 경고색 (2026-10-26)

- 비행 중 수리검은 도달 예상 시간이 0.6초 초과이면 흰색, 0.2초 초과~0.6초 이하이면 노란색, 0.2초 이하이면 빨간색으로 방향 인디케이터를 표시한다. 발사 전 흰색 점멸은 유지한다.
- 연속 위협 스냅샷의 위치 변화와 게임 시간으로 실제 비행 속도를 구한다. 현재 플레이어 CharacterController의 월드 경계 상자에서 가장 가까운 지점까지의 거리와 접근 속도로 남은 시간을 추정하고, 플레이어 이동에 따라 매 프레임 갱신한다. 정지한 수리검은 임박 경고를 표시하지 않는다. 속도와 현재 플레이어 중심 방향의 내적이 0 이하인 수리검은 지나쳐 멀어지는 것으로 보고 인디케이터를 즉시 숨긴다. 위치 추적은 유지하여 플레이어가 이동해 다시 접근 관계가 되면 표시를 재평가한다.
- 정확한 충돌 예측은 아니며 플레이어의 미래 이동을 예측하지 않는다. 첫 위치 표본만 있으면 속도를 알 수 없어 흰색을 사용한다. 게임 시간 기준이므로 불릿타임을 반영한다.
- 일반 수리검·1막 보스·2막 보스 및 C 위협 스냅샷 경로에 공통 적용한다. 기존 화면 안/밖·소리 인지 반경·표시 토글·알파 정책을 유지하며, 비활성·표시 자격 상실·플레이어 통과로 공급자가 위협을 종료하면 페이드 없이 즉시 마커를 반환한다. 그 외 비표시 전환의 페이드는 마지막 경고색을 유지한다.
- Inspector: 씬의 `DefenseDirector > ShurikenDirectionIndicator > 비행 적중 예상 경고`에서 `Warning Lead Time`(0.6), `Danger Lead Time`(0.2), 세 비행 색상을 설정한다. 위험 시간은 경고 시간 이하로 제한하여 계산한다.
- 관련 코드: `Assets/Scripts/Defense/ShurikenDirectionIndicator.cs`의 `UpdateFlightMotion`(비행 속도 표본), `IsMovingAway`(지나쳐 멀어지는 비행 표시 제외), `EstimateImpactTime`(현재 몸체까지 접근 시간), `GetFlightColor`·`UpdateMarker`(경고색 갱신), `Hide`(표본 초기화).
- 검증: Unity Editor 재컴파일 오류 0개·경고 0개. 실제 Play에서 경고색 전환과 경계 시간 확인은 미확인이다. 테스트 코드는 추가하지 않았다.


#### 1·2스테이지 공격 전조 방향 인디케이터 (2026-10-26, 최신 규칙)

- `BambooStage1`과 `RainyTwinBoss`의 `DefenseDirector/CombatThreatPresentationProvider`는 `적 방향 표시 모드=AttackTelegraph`를 저장한다. 이 모드에서 기존 화면 밖 발걸음 방향 UI는 그리지 않고, `DefenseMelee`의 근접 공격 시도에는 카타나 아이콘, `DefenseBeast`의 물기 돌진 시도에는 이빨을 표시한다. 실제 적 발소리와 월드 발자국 효과는 유지한다. 다른 씬은 기본 `Footstep` 모드다.
- 공급자는 `EnemyAttackBehaviour.TelegraphStarted`의 `AttackId`와 `EnemyHealth`의 대상 ID·생성 세대를 묶는다. 현재 적 위치로 방향과 화면 진입을 매 프레임 갱신하고, 취소·공격 종료·사망·재사용·세션 종료 시 마커를 반환한다. 기존 `CombatFootstepHud`의 24개 슬롯과 방향 원호를 재사용한다. 화면 안 적은 기존 몸체 경고를 보며 방향 UI는 숨긴다.
- 실제 전조 0.8초 중 처음 0.6초는 흰색에서 노란색으로 변하고, 남은 0.2초는 붉은색·`!` 배지·1회 크기 펄스로 강조한다. 공격 확정 때 0.1초 이내의 붉은 확인 펄스 후 표시를 정리한다. 지속 점멸은 쓰지 않는다. 카타나·Beast·수리검 표시 모두 배지를 방향 원호의 중앙에 놓으며, 배지 자체는 화면에서 회전하지 않는다. 수리검은 도달 예상 시간이 `Danger Lead Time` 이하가 되어 빨간색일 때 배지를 표시한다.
- Inspector: 씬 `DefenseDirector > CombatThreatPresentationProvider`의 기존 `위협 표시 정책`에서 가시성 임계값·거리·투명도를 조절한다. `공격 전조 표시 정책`에서 시작/준비/임박 색과 강조 크기를 조절한다. 공용 `Assets/Scenes/Defense/UI/CombatFootstepMarker.prefab`의 `공격 전조 이미지`에서 카타나·이빨 Sprite와 위험 배지 Image를 바꾼다. 수리검 위험 배지는 `BambooDefenseHud.prefab`의 `DirectionMarkers/Marker_n/AttackWarningBadge` Image에 같은 Sprite를 연결한다. PNG 원본은 `Assets/Scenes/Defense/UI/Indicators/`에 있다.
- 근접 공격 전조 아이콘은 `Assets/Scenes/Defense/UI/Indicators/AttackIndicator-katana-white-256.png`를 사용한다. 256×256 투명 캔버스에서 기존 흰색 카타나 실루엣 전체를 1.2배 확대해 최대 길이를 캔버스의 약 58%에서 70%로 높였다. 투명 여백은 좌우 43~44px, 상하 39px이며, `KATANA_VISIBLE_FRACTION`도 0.70으로 맞춰 방향 원호와 위험 배지 간격을 계산한다. 기존 흰색·노랑·빨강 경고색 전환은 유지한다.
- Beast 물기 공격 전조는 `Assets/Scenes/Defense/UI/Indicators/AttackIndicator-fangs.png`를 사용한다. 256×256 투명 Sprite에 위쪽 4개·아래쪽 4개의 분리된 흰색 이빨을 배치했다. `_fangsSprite` 연결을 유지해 Beast 공격 전조에 적용한다.
- 크기 조정: 공용 `BambooDefenseHud.prefab > FootstepMarkers > CombatFootstepHud > 공격 전조 크기`의 `먼 거리 크기`와 `가까운 거리 크기`를 조절한다. 기본값은 44/160이며 기존 발걸음 크기 24/36과 분리돼 있다. PNG 내부 여백을 제외한 카타나 실루엣의 최대 길이는 먼 거리 약 31, 가까운 거리 약 112이며 이빨 아이콘도 같은 0.70 비율로 계산한다. 거리에 따른 변화는 수리검과 같이 근접도의 제곱을 사용한다. 위험 배지는 각도에 관계없이 방향 원호 중앙점에 정렬하고 화면 기준으로 똑바로 유지한다.
- 관련 코드: `Assets/Scripts/Combat/Contracts/ThreatPresentationContracts.cs` / `AttackTelegraphPresentationSnapshot`; `Assets/Scripts/Defense/CombatThreatPresentationProvider.cs` / `HandleAttackStarted`, `PublishAttackTelegraphs`; `Assets/Scripts/Defense/DefenseDirectionMarker.cs` / `SetWarningBadge`; `Assets/Scripts/Defense/ShurikenDirectionIndicator.cs` / `GetFlightColor`; `Assets/Scripts/UI/CombatHud/CombatFootstepHud.cs` / `HandleAttackTelegraph`; `Assets/Scripts/UI/CombatHud/CombatFootstepMarker.cs` / `ShowAttack`.
- 검증: Unity가 저장한 Roslyn 응답 파일로 전체 `Assembly-CSharp` 컴파일 성공, 새 오류 0건. 기존 파일 경고 16건이 출력됐다. Unity 배치 실행은 LicensingClient/WMI 오류로 프로젝트 로드 전에 종료되어 실제 1·2스테이지 Play 화면·청취는 미확인이다. 테스트 코드는 추가하지 않았다.

#### 2026-10-26 위험 배지 위치·수리검 표시 통일

- 카타나·Beast 공격 배지를 화면 오른쪽 위 대신 방향 원호 중앙점에 배치한다. 배지 이미지는 공격 방향에 따라 기울지 않게 화면 기준 회전을 상쇄한다.
- `BambooDefenseHud.prefab`의 수리검 방향 마커 12개에도 같은 배지 Sprite를 자식 Image로 연결한다. 비행체가 `Danger Lead Time` 이내로 접근해 빨간색이 되면 배지를 표시하고, 경고색 단계·발사 전조·비위험 페이드에서는 숨긴다. 빨간색 페이드 중에는 배지도 같은 알파로 함께 사라진다.
- Unity Play 화면은 확인하지 않았다. 프리팹 직렬화 참조와 코드 경로만 정적으로 확인했다. 테스트 코드는 추가하지 않았다.

#### Beast 발소리 인디케이터 화면 판정 보정 (2026-10-25)

- 원인: 실제 Beast 발걸음과 HUD 연결은 정상이었지만, 기존 공급자는 적 아래 모든 Renderer의 Bounds를 화면 진입 판단에 포함했다. `WolfEyeGlint` 파티클의 약 5m 범위가 카메라에 걸리면서 뒤에 있는 Beast도 `IsInCamera=true`가 되고 방향 UI가 숨겨졌다. 연기·궤적도 같은 오판을 일으킬 수 있었다.
- 수정: 공통 `EnemyFootstepVisibility.IsInCamera`가 활성 `EnemyHitCapsule`이 있으면 그 몸체 경계를 사용하고, 없으면 메시·스킨 메시와 발행자 위치로 판정한다. ParticleSystemRenderer·LineRenderer·TrailRenderer는 제외한다. 기본 공급자와 실제 적의 C 발소리 후보가 이 판정을 공유하며 몸체 참조는 사건 생성 시 캐시한다. C 전용 미리보기 대상은 기존 대상 분류를 유지한다.
- 1스테이지 점검: BambooStage1의 HUD는 같은 씬 DefenseDirector 공급자와 연결돼 있고 표시 옵션이 켜져 있으며 마커는 24개다. 일반·Beast 프리팹 모두 기존 발걸음 컴포넌트를 유지하며, 메시 fallback도 지원한다. 1스테이지 역시 공통 판정을 사용해 이펙트로 인한 표시 누락을 방지한다. 1스테이지 실제 Play는 이번 확인 범위에서 수행하지 않았다.
- 검증: 수정 전 2-1에서 Beast의 발 접촉 구독과 발소리 사건은 정상인데, 화면 밖 적의 눈 섬광 Bounds가 화면에 걸려 UI가 꺼지는 것을 재현했다. 수정 후 실제 2-1 Play에서 Beast 발걸음 39개 중 화면 밖 19개가 마커 19개로 표시됐다. 이 19개는 파티클 범위가 화면에 걸린 상태였으므로 기존 방식에서는 숨겨지는 대상이었다. 최종 Unity 재컴파일 오류 0건·경고 0건. 테스트 코드는 저장소에 추가하지 않았다.
- 연결 및 범위: 스폰 가중치 문제는 사용자 요청으로 이번 수정에서 제외했다. 스폰 설정·스폰 선택 코드는 변경하지 않았고, 열려 있던 RainyTwinBoss의 미저장 변경을 저장하거나 되돌리지 않았다. Play 확인용 진행도·실행 ID·백그라운드 실행 설정은 복원했다.
- 관련 코드: `Assets/Scripts/Rendering/EnemyFootstepVisibility.cs`, `Assets/Scripts/Defense/CombatThreatPresentationProvider.cs`의 `ActiveFootstep`·`HandleFootstep`·`CreateSnapshot`, `Assets/Scripts/Rendering/EnemyFootstepCandidates.cs`의 `Candidate`·`IsInCamera`.

#### 2-1 적 발소리 인디케이터 연결 복구 (2026-10-25)

- 원인: 공용 `BambooDefenseHud.prefab`의 `CombatFootstepHud._provider`는 씬 공급자를 직접 저장할 수 없어 비어 있다. `BambooStage1`은 씬 인스턴스에 `DefenseDirector/CombatThreatPresentationProvider` 참조를 저장했지만 `RainyTwinBoss`의 2-1 HUD에는 해당 참조가 누락됐다. 공급자의 `Show Enemy Indicators=true`와 24개 마커가 있어도 HUD가 발걸음 사건을 구독하지 않아 표시되지 않았다.
- 수정: `Assets/Scenes/Defense/RainyTwinBoss.unity`의 `FootstepMarkers/CombatFootstepHud.Provider`를 같은 씬 `DefenseDirector`의 공급자에 연결하고 저장했다.
- 재발 방지: `CombatFootstepHud`가 켜질 때 참조가 없거나 다른 씬을 가리키면 같은 씬의 활성 공급자를 찾아 구독한다. 뒤늦게 생성된 공급자는 0.5초 간격으로 재시도하고, 연결되면 검색을 멈춘다. 실제 구독한 공급자를 별도로 보관해 참조 변경·재활성화 시 이전 구독과 마커를 정리하고 현재 스냅샷을 적용한다. 전역 검색 대신 현재 씬 루트와 재사용 목록을 사용한다.
- Inspector: 씬의 `DefenseDirector > CombatThreatPresentationProvider > 적 방향 인디케이터 표시`에서 표시를 켜고 끈다. HUD 쪽 참조는 `BambooDefenseHud > FootstepMarkers > CombatFootstepHud > Provider`다. 지면 발자국의 스포너 설정과는 별개의 화면 밖 적 방향 UI 설정이다.
- 검증: Unity 재컴파일 오류 0건·경고 0건. 실제 2-1 Play에서 프레임 진행과 생존 상태를 확인하고, 자연 발걸음 스냅샷 6개·표시 대상 2개·마커 2개를 관찰했다. HUD를 끄고 Provider 참조를 비운 뒤 다시 켜도 같은 씬 공급자에 자동 연결됐다. 표시 옵션을 끄면 스냅샷·마커 0개, 다시 켜면 발걸음 59개·표시 대상 25개 중 기존 마커 풀 한도인 24개를 표시했다. 테스트 코드는 저장소에 추가하지 않았다. 종료 후 진행도 PlayerPrefs와 백그라운드 실행 설정을 복원하고 원래 2-1 씬의 Edit Mode로 돌아왔다.
- 관련 코드: `Assets/Scripts/UI/CombatHud/CombatFootstepHud.cs` / `TrySubscribeProvider`, `FindSceneProvider`, `OnEnable`, `LateUpdate`, `OnDisable`; 관련 계약은 [발소리 후보](발소리_후보.md)를 따른다.

#### 일섬 게이지 비활성화 (2026-10-24)

- 공용 `Assets/Scenes/Defense/UI/BambooDefenseHud.prefab`의 `PerfectParryTokens` 오브젝트를 비활성화했다. 실행 중 토글이 아니라 프리팹의 기본 활성 상태를 껐으므로 세 칸 일섬 게이지와 자식 액체 표현이 생성·표시되지 않는다. 거합 원형 게이지는 유지한다.
- 패링 집중 3초 동안 좌클릭 무료 일섬, 극한 회피 집중 1초 동안 대상 돌진·일섬 금지, Shift 일섬 비활성화 규칙은 [특수 스킬 게이지](특수스킬_게이지.md)를 따른다. 온보딩 안내 문구와 단계는 이번 작업에서 수정하지 않았다.

#### 멀티킬 처치 표시 (2026-10-24)

- 온보딩을 제외한 BambooStage1, BambooDefense, RainyStage_JH, RainyTwinBoss, TestStage, (Legacy) RainyStage의 처치 목표 아래에 일반 공격·일섬·거합 실행별 `+N`을 최신·직전 두 줄로 표시한다. HUD 프리팹에 TMP 슬롯을 저장한다.
- 공용 설정은 `BambooDefenseHud.prefab` 루트의 `DefenseMultiKillHud` Inspector에서 한 번 조절한다. 등급별 기준 처치 수·글자 색·크기, 유지·퇴장 시간, 갱신 간격, 줄 위치가 모든 연결 씬에 적용된다. 씬에는 해당 HUD 인스턴스의 Director 참조만 저장한다.
- 두 TMP 슬롯의 검은 바깥 테두리는 전용 `ChosunCentennial_otf SDF - MultiKill Black Outline.mat`의 Underlay를 사용한다. 글자 색은 등급 설정에서, 테두리 색·두께는 이 머티리얼의 Underlay Color·Dilate에서 조절한다. 둘 다 글자 알파와 함께 페이드한다.
- 상세 동작, 공격 출처 보존과 미확인 Play 항목은 [멀티킬 처치 표시](멀티킬_처치_표시.md)를 따른다.

#### 거합 게이지 프리팹 구성 (2026-10-23)

- `BambooDefenseHud.prefab` 루트의 `PlayerUltimateHint`가 자식 `UltimateGaugeCanvas/UltimateGauge/GaugeCircle`의 `Background`, `ThreadLiquidBody`, `ThreadLiquid`, `Percentage`, `InputLabel`을 제어한다. UI 계층과 `CanvasGroup`·RawImage·실 액체 Graphic·TMP 참조는 프리팹에 직렬화되어 있으며, 플레이 중 새 HUD 오브젝트를 만들지 않는다.
- 게이지 값은 실 액체로 아래에서 위로 차오르고 중앙에 내림 정수 퍼센트를 표시한다. 입력 안내는 키보드에서 `Q:거합`, 게임패드에서 `Y:거합`으로 바뀌며, 홀드 차징·시전 중 HUD 전체가 페이드 아웃된다. 홀드 진행도는 화면 중앙 수리검 인디케이터 원 안쪽의 `UltimateHoldProgress` 원형 바가 표시한다(2026-10-24, [특수 스킬 게이지](특수스킬_게이지.md)). 컴포넌트는 플레이어의 `PlayerSkillGauge.Changed` 이벤트를 구독하고, 플레이어가 준비되기 전까지 찾기를 반복한다.
- 관련 파일: `Assets/Scenes/Defense/UI/BambooDefenseHud.prefab`, `Assets/Scripts/Player/PlayerUltimateHint.cs`. 프리팹 직렬화 참조와 Unity 재컴파일은 확인했고, Play 화면 렌더링은 아직 확인하지 않았다.

#### 수리검·적 방향 인디케이터 Inspector 토글 (2026-10-22)

- 두 토글은 `CombatThreatPresentationProvider` 한 컴포넌트에서 관리한다. 기본값은 켜짐이며, 끄면 해당 방향 UI만 숨기고 수리검 투척·위협 판정과 적 발소리·인지 처리는 유지한다.
- 정식 플레이 씬(`Assets/Scenes/Onboarding/Onboarding.unity`, `Assets/Scenes/Defense/BambooStage1.unity`, `Assets/Scenes/Defense/RainyTwinBoss.unity`)의 Hierarchy에서 `DefenseDirector`를 선택하고 `CombatThreatPresentationProvider` 컴포넌트의 `방향 인디케이터 표시` 그룹에서 두 값을 설정한다. 이 씬의 기본값을 바꾸면 해당 씬에 저장된다.
- 같은 Inspector 설정은 `BambooDefense`, `RainyStage_JH`, `TestStage`, `(Legacy) RainyStage` 씬의 `DefenseDirector` 오브젝트에도 있다.
- 플레이 중 토글도 지원한다. 수리검은 숨긴 동안 전조·비행 상태와 수명을 계속 갱신하며, 적 마커는 다시 켠 뒤 공급자가 갱신하는 발소리 스냅샷부터 표시한다.
- 관련 코드: `Assets/Scripts/Defense/CombatThreatPresentationProvider.cs` / `ShowShurikenIndicators`, `ShowEnemyIndicators`; `Assets/Scripts/Defense/ShurikenDirectionIndicator.cs` / `HandleTelegraph`, `HandleThreatChanged`, `UpdateMarker`; `Assets/Scripts/UI/CombatHud/CombatFootstepHud.cs` / `LateUpdate`, `HandleFootstep`, `ClearMarkers`.
- Unity Inspector 및 Play 화면에서 개별 토글 동작은 미확인이다.

#### 회피 기력 부족 안내 (2026-10-21)

- `InputManager.RequestDodge`가 실제 회피 입력을 `NotEnoughStamina`로 거부하면 `PlayerDodgeStaminaHint.Show`가 화면 하단 중앙에 "기력이 부족합니다."를 표시한다. 경고는 실시간 1.2초 동안 유지하고 0.15초에 걸쳐 나타나고 사라진다. 연속 입력은 표시 시간을 다시 시작한다.
- Player 프리팹에 붙인 전용 Canvas는 온보딩 안내와 같은 공용 조선100년체 글꼴, 어두운 패널·밝은 글자, 760×104·하단 62px(좁은 화면 640×104·42px)을 사용한다. 정렬 순서 103으로 온보딩 안내와 거합 안내 위에 나타나며 입력을 막지 않는다. 관련 코드: `Assets/Scripts/Player/PlayerDodgeStaminaHint.cs` / `BuildCanvas`, `ApplyResponsiveLayout`, `Show`, `Update`, `Hide`; `Assets/Scripts/GameManagers/InputManager.cs` / `RequestDodge`; `Assets/Resources/Prefabs/Player.prefab`. Roslyn 런타임 컴파일 오류 0개·기존 경고 4개, Unity Play에서 위치·전환 확인은 미확인이다.

#### 1·2막 보스 수리검 방향 표시 (2026-10-19)

- 1막 보스의 `EnemyProjectile`과 2막 보스의 `TwinBossProjectile`도 일반 `DefenseShuriken`과 같은 방향 인디케이터 판정을 사용한다. 보스 수리검이 적대 비행 중이고, 현재 씬에 속하며, 세션 투사체 ID가 있고, 플레이어를 지나지 않았을 때 기존 `CombatThreatPresentationProvider.CreateSnapshot` 경로에 등록한다.
- 화면 안에서 인지되는 수리검은 방향 UI를 숨기고, 화면 밖 또는 인지되지 않은 수리검은 플레이어 카메라 기준 방향과 기존 색·근접도 정책으로 표시한다. 일반 스테이지의 소리 인지 거리 제한도 기존 HUD 판정이 그대로 적용한다.
- 플레이어를 지나간 수리검, 반사된 수리검, 비행 종료·소멸 수리검은 활성 목록에서 빠져 마커가 종료된다. 발사 전 전조 표시가 아니라 실제 발사된 보스 수리검의 비행 표시다.
- 관련 코드: `EnemyProjectile.Initialize`, `EnemyProjectile.UpdatePassedPlayer`, `TwinBossProjectile.Initialize`, `TwinBossProjectile.UpdatePassedPlayer`, `CombatThreatPresentationProvider.AddProjectileThreat`.
- 구현 확인: 일반 수리검과 같은 공통 스냅샷·HUD 구독 경로에 연결했다. Unity Play에서 1막·2막 보스 전투를 진행하는 화면 확인은 아직 하지 않았다.

#### 일반 스테이지 소리 인지 거리 연동 (2026-10-18)

- 씬 `BambooDefenseDirector`에서 `소리 인지 거리 제한 사용`을 켜면 수리검 비행·투척 전조와 적 발소리 인디케이터는 같은 XZ 수평 반경 안에서만 표시한다. 기본 꺼짐이며 보스전·온보딩은 제한하지 않는다. 설정 위치와 검증은 [대숲 방어](대숲_방어.md)의 같은 절을 따른다.
- `ShurikenDirectionIndicator.HandleThreatChanged`는 범위 밖 비행 마커를 즉시 반환하고, `UpdateMarker`는 전조·페이드 마커에도 거리 제한을 적용한다. 전조의 수명은 범위 밖에서도 계속 진행하며 아직 유효한 전조가 범위에 들어오면 다시 표시할 수 있다.
- `CombatFootstepHud.HandleFootstep`는 현재 `Presentation.WorldPosition`으로 범위를 검사하여 범위 밖 적의 슬롯을 반환한다. C 후보 경로는 `EnemyFootstepCandidates`에서 스테이지 반경으로 자격도 판정한다. 적이 화면 안이면 발소리 마커를 숨기는 기존 규칙과 수리검의 반사·통과 제외 규칙은 함께 적용된다.

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
| Assets/Scripts/Defense/DefenseDirectionMarker.cs | Configure, SetIconScale, SetShurikenIconSprite, OnPopulateMesh | 원호 메시와 자식 수리검 Sprite Image의 배치·크기·색·알파 갱신 |
| Assets/Scripts/UI/CombatHud/CombatHudReferences.cs | RecordText, DirectionMarkers, PlayerHud, FootstepHud, ApplyDirectionMarkerSprite | HUD 내부 참조와 공용 수리검 Sprite의 Inspector 설정·마커 적용 |
| Assets/Scripts/Defense/CombatThreatPresentationProvider.cs | ShowShurikenIndicators, ShowEnemyIndicators, PublishThreatSnapshots, AddProjectileThreat, ExpireFootsteps, PublishEndedFootsteps | 수리검·적 방향 표시의 공통 Inspector 설정, 일반·보스 수리검 공통 위협 스냅샷, C의 감쇠·만료·발행자 종료 사건 |
| Assets/Scripts/Defense/ShurikenDirectionIndicator.cs | HandleTelegraph, HandleThreatChanged, UpdateMarker, Hide | 위협 진행에 맞춘 수리검 Sprite 방향·색·크기·알파·표시 상태 갱신 |
| Assets/Scripts/Enemies/EnemyProjectile.cs, TwinBossProjectile.cs | IsHostileFlying, HasPassedPlayer, ProjectileId | 보스 수리검의 적대 비행·통과 상태와 세션 고유 식별자 제공 |
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
| 거합 원형 게이지 | `BambooDefenseHud.prefab` 루트 → PlayerUltimateHint. 자식 `UltimateGaugeCanvas`의 Canvas·CanvasScaler·CanvasGroup과 `UltimateGauge/GaugeCircle`의 Background/Image, ThreadLiquidBody/RawImage·CombatThreadLiquidBody, ThreadLiquid/CombatThreadLiquidGauge, Percentage·InputLabel/TMP로 표시한다. 캔버스는 Overlay, 정렬 순서 101이며 기본 원 크기 104px·하단 오프셋 170px이다. 런타임에는 프리팹 오브젝트를 생성하지 않는다 |

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

#### 위험 배지 지름 확대 (2026-10-26)

- 근접·Beast 공격과 수리검 경고에 표시되는 느낌표 배지 지름을 기존 계산값의 1.5배로 확대했다. 최소 지름도 포함해 기존 최종 크기에 1.5를 곱한다.
- `CombatFootstepMarker`와 `DefenseDirectionMarker`의 런타임 크기 계산을 갱신하고, `CombatFootstepMarker.prefab` 및 `BambooDefenseHud.prefab`에 저장된 기본 크기도 15×15에서 22.5×22.5로 맞췄다.
# 전투 HUD

## 현재 구성

정식 전투 씬 `BambooStage1`과 `RainyTwinBoss`는 공용 `BambooDefenseHud` 프리팹을 사용한다. 웨이브 기록, 플레이어 체력·기력, 수리검·적 위협 표시, 거합 게이지, 공격별 멀티킬 표시가 연결되어 있다.

### 웨이브 기록

`DefenseRecordHud`는 웨이브 시작 전 준비 카운트다운을 표시한다. 웨이브 시작 후 처치 목표가 있으면 `처치 현재/목표`를 표시하고, 목표가 없으면 생존 시간과 실제 처치 수를 표시한다. 처치 목표가 있는 경우 화면에 표시하는 수치만 0부터 목표까지 제한하며 Director의 실제 처치 기록은 바꾸지 않는다. 보스 연출 중 기록 표시가 숨겨진 씬에서는 이 UI도 숨겨진다. 두 정식 전투 씬의 씬 저장 목표는 각각 100명이다. 웨이브와 처치 목표 상세는 [대숲 방어](대숲_방어.md)를 따른다.

### 체력과 기력

`CombatPlayerHud`는 현재 플레이어의 `PlayerHealth`를 찾고 같은 플레이어 오브젝트에서 `IPlayerStaminaReadModel` 구현체도 찾는다. HUD 프리팹의 원본 참조 필드는 비어 있지만 런타임 자동 연결을 사용한다. 플레이어 프리팹에는 `KatanaDamageReceiver`(`PlayerHealth` 파생 클래스)와 `PlayerStamina`가 연결되어 있다.

체력과 기력은 현재값/최대값과 게이지 채움으로 표시한다. 양수 체력이 1 이하일 때 숫자는 1로 표시하지만 게이지는 실제 체력 비율을 따른다. 기력 공급자를 찾지 못하면 `-- / --`와 0.35 알파로 표시한다. 플레이어 프리팹의 기력 설정은 최대 100, 회피 소모 20, 가드 소모 0, 초당 회복 20, 회복 지연 0.5초다.

### 수리검 방향과 도달 예상 경고

`CombatHudReferences`가 공용 흰색 수리검 Sprite를 각 `DefenseDirectionMarker`의 자식 Image에 적용한다. HUD 프리팹에는 12개 방향 마커와 수리검 이미지·위험 배지 연결이 저장되어 있다. 투척 전조는 흰색으로 점멸하고, 비행 중 표시에는 실제 수리검의 이동 표본과 현재 플레이어 `CharacterController` 경계를 사용한다.

비행 중 도달 예상 시간은 현재 수리검 위치에서 플레이어 몸체의 가장 가까운 점까지의 거리와 접근 속도로 계산한다. 0.6초 초과는 흰색, 0.2초 초과부터 0.6초까지는 노란색, 0.2초 이하는 빨간색이며 빨간 구간에는 위험 배지가 함께 표시된다. 이 시간은 게임 시간 기준이고 플레이어의 미래 이동이나 실제 충돌 경로를 예측하지 않는다. 첫 이동 표본 전이나 정지·비접근 상태는 임박 경고를 만들지 않는다.

수리검이 플레이어에게서 멀어지는 속도 방향이면 비행 아이콘을 숨기되 위치를 계속 추적해 표시 관계를 갱신한다. 투사체가 자체 `HasPassedPlayer` 상태가 되면 위협 스냅샷 대상에서 빠지고 마커가 종료된다. 이 판정은 일반 `DefenseShuriken`, 1막 `EnemyProjectile`, 2막 `TwinBossProjectile`에 공통 적용된다. 시간 기준과 색은 `ShurikenDirectionIndicator`의 `Warning Lead Time` 0.6초, `Danger Lead Time` 0.2초 및 세 비행 색상으로 조정한다.

### 적 공격 전조 방향

`BambooStage1`과 `RainyTwinBoss`는 수리검·적 방향 표시를 켜고 적 표시 모드를 `AttackTelegraph`로 저장한다. 따라서 화면 밖 일반 발소리 방향 대신 근접 적의 베기 시도에는 흰색 카타나 아이콘, Beast 물기 돌진에는 이빨 아이콘을 표시한다. 실제 적 발소리와 월드 발자국 효과, 화면 안 적의 몸체 전조는 별도로 유지된다.

공격 전조 마커는 공격 ID와 적 생성 세대를 추적하며 공격 취소·종료·사망·재사용·세션 종료 시 정리된다. 전조 진행에 따라 흰색에서 노란색, 빨간색으로 바뀌고 임박 단계에는 위험 배지를 표시한다. 방향 원호는 아이콘 크기와 함께 배치되며 위험 배지는 화면에서 회전하지 않는다. 수리검 방향 아이콘은 `AttackIndicator-shuriken-white.png`, 근접 공격 아이콘은 `AttackIndicator-katana-white-256.png`, Beast 아이콘은 `AttackIndicator-fangs.png`를 사용한다.

표시 자격·화면 안 판정·인지 범위 상세는 [위협 표시 판정](위협_표시_판정.md), [발소리 후보](발소리_후보.md), [전투 인지 HUD 공통 계약](전투_인지_HUD_공통_계약.md)을 따른다.

### 거합 게이지와 일섬 토큰

`PlayerUltimateHint`는 `PlayerSkillGauge`의 거합 준비도를 원형 실 액체 게이지와 퍼센트로 표시한다. 키보드는 `Q:거합`, 게임패드는 `Y:거합`을 안내한다. 자동 충전 또는 시전 중에는 게이지를 숨기고, `UltimateHoldProgress`는 홀드 진행도를 별도로 표시한다. 홀드 차징 중에는 암전 0.5초 전에 퍼펙트 패링 파동을 내며, 정식 플레이어 설정에서 반경은 60m이고 거합 종료 뒤 1.6초에 걸쳐 흐려진다. 차징이 시전 전에 취소되면 이 파동을 즉시 지운다. 입력·충전 규칙은 [특수 스킬 게이지](특수스킬_게이지.md), 액체 표현은 [실 액체 게이지](실_액체_게이지.md)를 따른다.

공용 HUD 프리팹의 `PerfectParryTokens` 오브젝트는 비활성 상태라 세 칸 일섬 토큰 게이지는 표시되지 않는다.

### 멀티킬 표시와 기력 부족 안내

`DefenseMultiKillHud`는 한 공격 실행으로 표시 기준 이상 처치했을 때 처치 기록 아래에 최신·직전 결과를 두 줄로 표시한다. 공격 ID별로 처치를 집계하고 일반 공격, 일섬, 거합 및 검기 공격 패턴을 처리한다. 두 TMP 슬롯은 처음에는 비활성이고 표시 대상이 생기면 켜진다. 등급과 표시 시간은 [멀티킬 처치 표시](멀티킬_처치_표시.md)를 따른다.

회피 입력이 기력 부족으로 거부되면 `PlayerDodgeStaminaHint`가 화면 하단에 안내를 표시한다. 기본 유지 시간은 1.2초이며 0.15초에 걸쳐 나타나고 사라진다.

## 구현 연결

| 파일 | 역할 |
| --- | --- |
| `Assets/Scenes/Defense/UI/BambooDefenseHud.prefab` | 공용 전투 HUD, 12개 수리검 방향 마커와 직렬화 UI 연결 |
| `Assets/Scenes/Defense/UI/CombatFootstepMarker.prefab` | 발소리·적 공격 전조 아이콘과 위험 배지 |
| `Assets/Scenes/Defense/UI/Indicators/AttackIndicator-shuriken-white.png` | 수리검 방향 마커 Sprite |
| `Assets/Scenes/Defense/UI/Indicators/AttackIndicator-katana-white-256.png` | 근접 공격 전조 Sprite |
| `Assets/Scenes/Defense/UI/Indicators/AttackIndicator-fangs.png` | Beast 공격 전조 Sprite |
| `Assets/Scripts/Defense/DefenseRecordHud.cs` | 준비 카운트다운과 처치·생존 기록 표시 |
| `Assets/Scripts/UI/CombatHud/CombatPlayerHud.cs` | 플레이어 체력·기력 원본 연결 |
| `Assets/Scripts/UI/CombatHud/CombatResourceGauge.cs` | 자원 숫자와 게이지 표시 |
| `Assets/Scripts/UI/CombatHud/CombatHudReferences.cs` | 공용 수리검 Sprite를 각 방향 마커에 연결 |
| `Assets/Scripts/Defense/CombatThreatPresentationProvider.cs` | 수리검·적 위협 및 공격 전조 데이터 제공 |
| `Assets/Scripts/Defense/ShurikenDirectionIndicator.cs` | 수리검 방향·도달 예상 경고색·위험 배지 갱신 |
| `Assets/Scripts/Defense/DefenseDirectionMarker.cs` | 수리검 아이콘·원호·위험 배지 표시 |
| `Assets/Scripts/UI/CombatHud/CombatFootstepHud.cs` | 화면 밖 적 방향·공격 전조 마커 표시 |
| `Assets/Scripts/UI/CombatHud/CombatFootstepMarker.cs` | 발소리·공격 전조 이미지와 원호·위험 배지 표시 |
| `Assets/Scripts/Player/PlayerUltimateHint.cs` | 거합 준비도와 입력 안내 표시 |
| `Assets/Scripts/Defense/DefenseMultiKillHud.cs` | 공격 실행별 추가 처치 표시 |
| `Assets/Scripts/Player/PlayerDodgeStaminaHint.cs` | 기력 부족 회피 안내 표시 |
| `Assets/Scripts/Player/PlayerEmpower.cs` | 거합 홀드 중 차징 파동 시작과 취소 처리 |
| `Assets/Scripts/Player/PlayerPerfectPulse.cs` | 파동 반경·공개 유지·페이드 및 파동별 취소 관리 |

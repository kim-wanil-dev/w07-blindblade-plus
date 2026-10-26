# 전투 HUD

## 현재 구성

정식 전투 씬 `BambooStage1`과 `RainyTwinBoss`는 공용 `BambooDefenseHud` 프리팹을 사용한다. 이 HUD에는 웨이브 기록, 플레이어 체력·기력, 수리검·적 위협 표시, 거합 게이지, 공격별 멀티킬 표시가 연결되어 있다.

### 웨이브 기록

`DefenseRecordHud`는 웨이브 시작 전 준비 카운트다운을 표시한다. 웨이브 시작 후 처치 목표가 있으면 `처치 현재/목표`를 표시하고, 목표가 없으면 생존 시간과 실제 처치 수를 표시한다. 처치 목표가 있는 경우 화면에 표시하는 수치만 0부터 목표까지 제한하며 Director의 실제 처치 기록은 바꾸지 않는다. 보스 연출 중 기록 표시가 숨겨진 씬에서는 이 UI도 숨겨진다. 두 정식 전투 씬의 씬 저장 목표는 각각 100명이다. 자세한 웨이브 설정은 [대숲 방어](대숲_방어.md)를 따른다.

### 체력과 기력

`CombatPlayerHud`는 현재 플레이어의 `PlayerHealth`를 찾고, 같은 플레이어 오브젝트에서 `IPlayerStaminaReadModel` 구현체도 찾는다. 공용 HUD 프리팹의 원본 참조 필드는 비어 있지만 런타임 자동 연결을 사용한다. 플레이어 프리팹에는 `KatanaDamageReceiver`( `PlayerHealth` 파생 클래스)와 `PlayerStamina`가 연결되어 있다.

체력과 기력은 각각 현재값/최대값과 게이지 채움으로 표시한다. 양수 체력이 1 이하일 때 숫자는 1로 표시하지만 게이지는 실제 체력 비율을 따른다. 기력 공급자를 찾지 못하면 `-- / --`와 0.35 알파로 표시한다. 현재 플레이어 프리팹의 기력 설정은 최대 100, 회피 소모 20, 가드 소모 0, 초당 회복 20, 회복 지연 0.5초다.

### 위협 방향과 공격 전조

`ShurikenDirectionIndicator`는 표시 대상인 수리검의 방향을 화면 가장자리 인디케이터로 보여준다. `CombatFootstepHud`는 적의 발소리 후보와 공격 전조 표시를 같은 마커 풀로 표시한다. `BambooStage1`과 `RainyTwinBoss`는 두 표시 옵션을 켜고 적 표시 모드를 `AttackTelegraph`로 저장한다. 이 두 씬에서는 화면 밖 일반 발소리 방향 대신 근접 적의 베기 시도와 Beast의 물기 돌진 시도를 공격별 아이콘과 임박 경고로 표시한다. 화면 안 적의 몸체 전조와 실제 월드 발소리는 별도로 유지된다.

표시 대상 선정과 인디케이터 규칙은 [위협 표시 판정](위협_표시_판정.md), [발소리 후보](발소리_후보.md), [전투 인지 HUD 공통 계약](전투_인지_HUD_공통_계약.md)을 따른다.

### 거합 게이지와 일섬 토큰

`PlayerUltimateHint`는 `PlayerSkillGauge`의 거합 준비도를 원형 실 액체 게이지와 퍼센트로 보여준다. 키보드에서는 `Q:거합`, 게임패드에서는 `Y:거합`을 표시한다. 자동 충전 또는 시전 중에는 게이지를 숨기며, `UltimateHoldProgress`는 거합 홀드 진행도를 별도로 나타낸다. 액체 표현 규칙은 [실 액체 게이지](실_액체_게이지.md), 거합·일섬 입력 및 사용 조건은 [특수 스킬 게이지](특수스킬_게이지.md)를 따른다.

공용 HUD 프리팹의 `PerfectParryTokens` 오브젝트는 비활성 상태라 현재 세 칸 일섬 토큰 UI는 표시되지 않는다.

### 멀티킬 표시와 기력 부족 안내

`DefenseMultiKillHud`는 한 공격 실행으로 표시 기준 이상 처치했을 때 처치 기록 아래에 최신·직전 결과를 두 줄로 표시한다. 공격 ID별로 처치를 집계하며 일반 공격, 일섬, 거합 및 검기 공격 패턴을 처리한다. 공용 HUD의 두 TMP 슬롯은 시작 시 비활성이고 표시 대상이 생기면 켜진다. 등급과 유지 시간은 [멀티킬 처치 표시](멀티킬_처치_표시.md)를 따른다.

회피 입력이 기력 부족으로 거부되면 플레이어 프리팹의 `PlayerDodgeStaminaHint`가 화면 하단에 안내를 표시한다. 안내는 기본 1.2초 유지되고 0.15초에 걸쳐 나타나고 사라진다.

## 구현 연결

| 파일 | 역할 |
| --- | --- |
| `Assets/Scenes/Defense/UI/BambooDefenseHud.prefab` | 공용 전투 HUD와 직렬화된 UI 구성 |
| `Assets/Scripts/Defense/DefenseRecordHud.cs` | 준비 카운트다운과 처치·생존 기록 표시 |
| `Assets/Scripts/UI/CombatHud/CombatPlayerHud.cs` | 플레이어 체력·기력 원본 연결 |
| `Assets/Scripts/UI/CombatHud/CombatResourceGauge.cs` | 자원 숫자와 게이지 표시 |
| `Assets/Scripts/Defense/CombatThreatPresentationProvider.cs` | 수리검 및 적 위협 데이터 제공 |
| `Assets/Scripts/Defense/ShurikenDirectionIndicator.cs` | 화면 가장자리 수리검 방향 표시 |
| `Assets/Scripts/UI/CombatHud/CombatFootstepHud.cs` | 적 방향·공격 전조 마커 표시 |
| `Assets/Scripts/Player/PlayerUltimateHint.cs` | 거합 준비도와 입력 안내 표시 |
| `Assets/Scripts/Defense/DefenseMultiKillHud.cs` | 공격 실행별 추가 처치 표시 |
| `Assets/Scripts/Player/PlayerDodgeStaminaHint.cs` | 기력 부족 회피 안내 표시 |

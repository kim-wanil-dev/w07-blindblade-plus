# 텔레메트리 CSV 변수 사전과 분석 파일 만들기

이 문서는 `tools/combat_telemetry/Export-Telemetry.ps1`가 생성하는 CSV의 열 의미와 기본 분석 절차를 설명한다. 현재 변환기는 신규 스키마 `0.5.0`과 기존 `0.3.0`·`0.4.0`, 공통 계약 `0.2.0` JSONL을 처리한다.

> 최신 스프레드시트 열 전체 사전과 웹 대시보드 지표 해석은 [텔레메트리 README](../tools/combat_telemetry/README.md)를 기준으로 한다. CSV 가져오기와 전처리 절차는 이 문서를 참고한다.

## 분석용 CSV 생성

게임 로그는 기본적으로 아래 경로에 JSONL 형식으로 저장된다. 한 줄이 이벤트 하나다.

```text
%USERPROFILE%\AppData\LocalLow\DefaultCompany\w07-blindblade+\CombatTelemetry
```

프로젝트 루트에서 PowerShell을 열고 다음 명령을 실행한다. Node.js가 설치되어 있어야 한다. 출력 폴더는 로그 폴더와 다른 위치를 사용한다.

```powershell
$logDirectory = Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\w07-blindblade+\CombatTelemetry'
$outputDirectory = Join-Path $env:USERPROFILE 'Desktop\Stage1Telemetry'

powershell -ExecutionPolicy Bypass -File .\tools\combat_telemetry\Export-Telemetry.ps1 `
  -LogDirectory $logDirectory `
  -OutputDirectory $outputDirectory
```

성공하면 `Desktop\Stage1Telemetry`에 다음 파일이 만들어진다.

| 파일 | 용도 |
|---|---|
| `Events.csv` | 사건별 원본 로그 |
| `Sessions.csv` | 구간 도전별 최신 누적 요약 |
| `OnboardingSteps.csv` | 온보딩 목표별 시간·시도 요약 |
| `Patterns.csv` | 적 공격 패턴별 결과와 비율 |
| `Heatmaps.csv` | 위치 격자별 이동 시간·공격·피격 수 |
| `Diagnostics.csv` | 지원하지 않는 로그나 읽을 수 없는 줄과 원인 |

변환기는 지원 스키마를 검증하고, `sessionId:eventSequence`가 같은 중복 이벤트를 한 번만 남긴다. Sessions는 각 `stage_attempt_id`에서 가장 큰 이벤트 순번의 요약을 내보낸다. OnboardingSteps도 단계·목표 키가 같은 행을 중복하지 않는다. 변환이 불가능한 줄은 버리지 않고 `Diagnostics.csv`에 원본 파일명·줄 번호·오류와 함께 기록한다.

직접 Node.js로 실행할 수도 있다.

```powershell
node .\tools\combat_telemetry\Export-Telemetry.cjs `
  "$logDirectory" `
  "$outputDirectory"
```

## Google Sheets에서 분석하기

1. Google Sheets에서 **파일 → 가져오기 → 업로드**를 선택하고 CSV를 하나씩 가져온다. 각 파일은 별도 시트로 넣는다.
2. 가져오기 설정에서 **텍스트를 숫자·날짜·수식으로 변환** 옵션을 끈다. 특히 `stage_id`의 `1-1`, `1-2`가 날짜로 바뀌지 않게 한다.
3. 참가자 데이터만 볼 때는 `config_version` 값이 `-editor`로 끝나는 Editor 실행 행을 제외한다.
4. 구간 비교는 `stage_id`로 나눈다. 현재 온보딩은 `0-1`~`0-5`, `1-1`은 일반 적 구간, `1-2`는 보스전이다. 이전 `onboarding-2026-10-16-stage-ids-7`의 `0-1`~`0-7`과 더 오래된 `onboarding-2026-10-16` 설정의 `0-1`·`0-2`는 현재 기록과 합쳐 비교하지 않는다.
5. 전체 비율을 여러 세션에서 합칠 때 세션별 비율의 평균을 내지 않는다. 예를 들어 적중률은 `적중 횟수 합 ÷ 판정된 공격 수 합`으로 계산한다.
6. Heatmaps는 `stage_id`, `stage_attempt_id`, `layer`로 필터링하고 `grid_z`를 행, `grid_x`를 열, `weight` 합계를 값으로 하는 피벗 테이블을 만든 뒤 색상 척도를 적용한다.
7. `Diagnostics.csv`에 행이 있으면 해당 로그 줄은 다른 CSV 집계에 포함되지 않았을 수 있으므로 `problem`을 확인한다.

Sessions는 도전별 최신 누적값이므로 같은 `stage_attempt_id`의 행을 합산하지 않는다. 패턴 종류별 적중률도 Sessions의 비율 열을 평균 내기보다 `Patterns.csv` 또는 Events의 분자·분모를 합산해 다시 계산한다. `ranged`·`melee`와 보스 개별 패턴 분류는 서로 겹칠 수 있으므로 그룹 간 수치를 더하지 않는다.

## 공통 해석 규칙

- 빈칸은 값이 0이라는 뜻이 아니다. 해당 이벤트에 적용되지 않거나, 표본 또는 분모가 없거나, 측정할 수 없었다는 뜻이다.
- 현재 `stage_id`는 온보딩 `0-1`~`0-5`, 일반 전투 `1-1`, 보스전 `1-2` 또는 미지정 구간이다. 예전 7단계 로그의 `0-6`·`0-7` 값은 보존된다. `stage_attempt_id`는 한 구간의 한 도전을 식별한다.
- `session_id`는 씬 실행을 식별한다. `run_id`는 온보딩 완료와 이어지는 전투 실행을 연결한다. 온보딩 없이 Stage1을 직접 실행하면 `run_id`가 비어 있을 수 있다.
- 시간은 초, 위치는 월드 XZ 좌표(미터)다. `realtime_seconds`는 실제 경과, `gameplay_seconds`는 게임 시간, `active_play_seconds`는 실제 플레이 활성 시간이다.
- CSV 값은 원시 카운트와 비율을 함께 제공한다. 비율 열은 `0~1` 범위이며 분모가 0이면 빈칸이다. 퍼센트로 표시할 때는 100을 곱한다.
- `received_kst`는 서버가 받은 한국시간이다. 로컬 CSV 변환에서는 서버 수신 시각이 없으므로 비어 있다. `occurred_at_kst`는 게임에서 이벤트가 발생한 한국시간이다.
- Editor 실행은 `config_version` 끝에 `-editor`가 붙는다. 본 플레이 테스트 분석에서 제외한다.

## Events.csv

한 행이 이벤트 하나다. `event_type`과 함께 읽어야 하는 이벤트별 필드는 해당 이벤트에서만 값이 채워진다.

| 변수 | 의미 |
|---|---|
| `event_key` | `session_id:event_sequence` 형태의 이벤트 중복 방지 키 |
| `received_kst` | 서버 수신 한국시간. 로컬 CSV에서는 빈칸 |
| `schema_version` | 로그 데이터 스키마 버전 |
| `build_version` | 게임 빌드 버전 |
| `config_version` | 텔레메트리 설정 버전. Editor 실행은 `-editor` 접미사 포함 |
| `session_id` | 씬 실행 ID |
| `stage_id` | 현재 온보딩 구간 `0-1`~`0-5`; 이전 7단계 행은 `0-6`·`0-7`도 포함 가능, 일반 전투 `1-1`, 보스전 `1-2` 등 |
| `stage_attempt_id` | 구간 도전 ID |
| `anonymous_player_id` | 익명 PC 식별자. 개인 계정 ID가 아님 |
| `event_sequence` | 세션 내 이벤트 순서 번호 |
| `event_type` | 이벤트 종류. 예: `player_input`, `player_died`, `enemy_attack_resolved`, `onboarding_step` |
| `occurred_at_kst` | 이벤트 발생 한국시간 |
| `realtime_seconds` | 실제 경과 시간(초) |
| `gameplay_seconds` | 게임 시간 경과(초) |
| `input_id` | 플레이어 입력 식별자 |
| `action_id` | 입력으로 시작한 액션 식별자 |
| `attack_id` | 적 공격의 시작·실행·결과를 연결하는 식별자 |
| `dodge_id` | 회피 동작 식별자 |
| `kind` | 문맥에 따른 입력·공격 종류 |
| `pattern_id` | 구체적인 적 공격 패턴 ID |
| `outcome` | 공격 또는 방어 판정 결과 |
| `is_attempt` | 해당 입력 행을 시도 1회로 집계하는지 표시 |
| `input_status` | 입력 처리 상태. 예: 시작·거부·버퍼됨·버퍼에서 삭제됨 |
| `applied_damage` | 실제 적용된 피해량 |
| `x`, `z` | 이벤트 위치. 이동·피격 위치 분석에 사용 |
| `origin_x`, `origin_z` | 적 공격의 발사·시작 위치 |
| `aim_x`, `aim_z` | 공격이 향한 목표 위치 |
| `player_x`, `player_z` | 공격 판정 시점 플레이어 위치 |
| `duration_seconds` | 이동 표본 등 이벤트가 지속된 시간 |
| `reaction_seconds` | 공격별 반응 기준 신호부터 연결 입력까지의 시간. 일반 패턴은 전조 시작, 2막 짐승 물기(`TwinBeastBite`)는 돌진 중 눈 섬광 시점부터 계산 |
| `input_to_contact_seconds` | 연결된 입력부터 공격 접촉까지의 시간 |
| `reaction_association` | 반응 시간을 공격에 연결한 근거. 예: `resolved_contact`, `held_guard_intent` |
| `payload_json` | 구조화된 전체 이벤트 데이터. 위 열과 값이 중복될 수 있음 |
| `run_id` | 온보딩부터 이어지는 전투 실행 연결 ID |
| `step_index` | 온보딩 단계 번호 |
| `objective_index` | 단계 안의 세부 목표 번호 |
| `retry_count` | 해당 온보딩 단계의 사망 후 재도전 횟수 |
| `first_input_seconds` | 목표 활성화부터 첫 입력까지의 활성 시간 |
| `expected` | 입력이 해당 온보딩 목표에서 기대한 행동인지 표시 |
| `defense_outcome` | 입력에 연결된 방어 결과 |

## Sessions.csv

한 행이 `stage_attempt_id` 하나의 구간 요약이다. 이벤트가 누적될 때 같은 도전의 최신 요약으로 갱신되므로 행들을 합산하지 않는다.

### 식별·시간 열

| 변수 | 의미 |
|---|---|
| `stage_attempt_id` | 구간 도전 ID |
| `session_id` | 씬 실행 ID |
| `stage_id` | 요약 구간 |
| `anonymous_player_id` | 익명 PC 식별자 |
| `build_version`, `config_version` | 빌드·설정 버전 |
| `started_kst` | 구간 시작 한국시간 |
| `last_event_sequence` | 요약에 반영된 마지막 이벤트 순번 |
| `status` | 상태: `started`, `in_progress`, `cleared`, `death`, `final_clear`, `application_quit`, `scene_unloaded`, `unclean_exit` 등. 강제 종료 상태는 다음 실행에서 `unclean_exit`로 복구될 수 있음 |
| `realtime_seconds` | 구간의 실제 경과 시간 |
| `gameplay_seconds` | 구간의 게임 시간 경과 |
| `active_play_seconds` | 구간의 활성 플레이 시간 |
| `health` | 요약 시점 또는 종료 시점의 현재 체력 |

### 행동·피격 지표

| 변수 | 의미 |
|---|---|
| `deaths` | 구간 사망 수. 보통 도전당 0 또는 1 |
| `kills` | 처치 수 |
| `damageTaken` | 받은 총 피해량 |
| `damageHitCount` | 피해가 실제 적용된 접촉 수 |
| `dodgeAttempts` | 회피 입력 시도 수 |
| `dodgeRejected` | 거부된 회피 입력 수 |
| `dodgeStarted` | 실제 시작된 회피 동작 수 |
| `guardAttempts` | 방어 입력 시도 수 |
| `guardRejected` | 거부된 방어 입력 수 |
| `guardAttemptsWithContact` | 공격 접촉이 연결된 고유 방어 입력 수 |
| `guardAttemptsWithoutContact` | 공격 접촉이 연결되지 않은 고유 방어 입력 수 |
| `guardHitCount` | 방어 의도 중 실제 피해를 받은 접촉 수 |
| `guardGuardCount` | 일반 방어로 막은 접촉 수 |
| `guardNormalParryCount` | 일반 패링 성공 접촉 수 |
| `guardPerfectParryCount` | 퍼펙트 패링 성공 접촉 수 |
| `attackAttempts` | 플레이어 공격 입력 시도 수 |
| `attackRejected` | 거부된 플레이어 공격 입력 수 |
| `enemyAttackCount` | 실제 commit된 적 공격 수 |
| `enemyResolvedCount` | commit된 최종 결과 수. 기존 의미대로 Cancelled·Interrupted는 제외 |
| `enemyHitCount` | 플레이어에게 적중한 적 공격 수 |
| `enemyDodgeCount` | 0.5.0에서는 일반 회피 수. 극한 회피는 별도 `enemyPerfectDodgeCount`; 0.4.0 이하에서는 일반/극한 회피가 분리되지 않음 |
| `enemyNormalParryCount` | 일반 패링으로 막은 적 공격 수 |
| `enemyPerfectParryCount` | 퍼펙트 패링으로 막은 적 공격 수 |
| `enemyGuardCount` | 일반 방어로 막은 적 공격 수 |
| `enemyMissCount` | 접촉하지 않고 빗나간 적 공격 수 |
| `enemyInvulnerableCount` | 회피 이외의 무적 상태로 피해가 적용되지 않은 공격 수 |
| `enemyIgnoredCount` | 집계 대상에서 제외된 공격 수 |
| `enemyDestroyedCount` | 투사체 파괴 등으로 종료된 공격 수 |
| `enemyCancelledCount` | 취소된 공격 수 |
| `enemyInterruptedCount` | 중단된 공격 수 |
| `reactionCount` | 평균 반응 시간 산출에 포함된 공격 판정 수 |
| `reactionMeanSeconds` | 연결된 반응 시간의 평균(초) |
| `enemyHitRate` | `enemyHitCount ÷ enemyResolvedCount` |
| `serviceDroppedEventsTotal` | 로그 저장·전송 서비스가 기록하지 못한 이벤트 누계 |

입력 시도 수와 접촉 판정 수는 단위가 다르다. 한 번의 방어 입력으로 여러 공격을 막을 수 있으므로 방어 성공 접촉 수를 방어 입력 수로 나눈 값은 입력 성공률이 아니다.

### 공격 유형별 지표

아래 아홉 접미사가 각각 `ranged`, `melee`, `bossRanged`, `bossCharge` 접두사에 붙는다. 예를 들어 `bossChargeHitRate`는 보스 돌진 공격의 적중률이다.

| 접미사 | 의미 |
|---|---|
| `Attempts` | 공격 실행 시도 수 |
| `Resolved` | 결과 판정 완료 수 |
| `Hits` | 플레이어 적중 수 |
| `Dodges` | 실제 회피 판정 수 |
| `Parries` | 일반 패링 수 |
| `PerfectParries` | 퍼펙트 패링 수 |
| `Damage` | 해당 유형이 준 총 피해 |
| `HitRate` | `Hits ÷ Resolved` |
| `DamageShare` | 해당 유형 피해 ÷ 구간 전체 피해 |

`ranged`는 투사체, `melee`는 비투사체 공격 집계다. `bossRanged`와 `bossCharge`는 보스 패턴별 집계이므로 상위 유형과 서로 겹칠 수 있다.

### 패턴·온보딩 확장 열

| 변수 | 의미 |
|---|---|
| `patterns_json` | 패턴별 상세 집계 JSON. `Patterns.csv`에 펼쳐진 데이터와 중복 |
| `run_id` | 온보딩과 이후 스테이지를 연결하는 실행 ID |
| `exit_step_index` | 온보딩 종료·이탈 시점의 단계 번호 |
| `exit_objective_index` | 이탈 단계의 세부 목표 번호 |
| `steps_completed` | 완료한 온보딩 목표 수 |
| `attempts` | 해당 온보딩 구간의 전체 입력 시도 수 |
| `retry_count` | 해당 온보딩 구간의 사망 재도전 횟수 |

온보딩 전투 카운터는 적용 대상이 아니면 빈칸일 수 있다. `attempts`는 구간 전체 시도 수이고, 아래 OnboardingSteps의 `attempts`는 목표가 요구한 행동의 시도 수다.

## OnboardingSteps.csv

한 행이 온보딩 단계 하나의 세부 목표 하나다.

| 변수 | 의미 |
|---|---|
| `step_key` | 세션·단계·목표 조합의 중복 방지 키 |
| `received_kst` | 서버 수신 한국시간. 로컬 CSV에서는 빈칸 |
| `run_id` | 온보딩과 이후 스테이지를 연결하는 ID |
| `session_id` | 씬 실행 ID |
| `stage_id` | 현재 온보딩 단계 `0-1`~`0-5`; 이전 7단계 행은 `0-6`·`0-7`, 초기 2구간 데이터는 README의 버전 분리 지침을 따른다. |
| `stage_attempt_id` | 구간 도전 ID |
| `step_index` | 온보딩 단계 번호(현재 0~4) |
| `objective_index` | 한 단계의 세부 목표 번호 |
| `retry_count` | 해당 단계에서 사망 후 재진입한 횟수 |
| `active_seconds` | 목표의 입력이 활성화되어 있던 시간 |
| `first_input_seconds` | 목표 활성화부터 첫 입력까지 걸린 활성 시간. 입력이 없으면 빈칸 |
| `attempts` | 목표가 요구한 전투 행동의 고유 입력 시도 수. 이동 목표는 정지 후 다시 움직인 횟수를 포함 |
| `wrong_attempts` | 목표가 요구하지 않은 전투 입력 수 |
| `movement_attempts` | 이동 입력 시도 수 |
| `started_inputs` | 실제 시작된 입력 수 |
| `rejected_inputs` | 거부된 입력 수 |
| `buffered_inputs` | 입력 버퍼에 들어간 입력 수 |
| `dropped_inputs` | 버퍼에서 삭제된 입력 수 |
| `reason` | 기록 종료 사유: 완료·사망·씬 이탈·앱 종료 등 |

`started_inputs`, `rejected_inputs`, `buffered_inputs`, `dropped_inputs`는 입력 처리 상태별 수치이며, 오입력도 포함할 수 있다. 따라서 `attempts`나 `wrong_attempts`와 단순 합산하지 않는다.

## Patterns.csv

한 행이 한 구간의 적 공격 패턴 하나다.

| 변수 | 의미 |
|---|---|
| `stage_attempt_id`, `stage_id` | 구간 도전 ID와 구간 이름 |
| `kind` | 공격 유형 |
| `pattern_id` | 공격 패턴 ID |
| `attempts` | 패턴 공격 시도 수 |
| `resolved` | 결과 판정 완료 수 |
| `hit` | 적중 수 |
| `dodge` | 회피 판정 수 |
| `parry` | 일반 패링 수 |
| `perfect_parry` | 퍼펙트 패링 수 |
| `guard` | 일반 방어 수 |
| `miss` | 빗나감 수 |
| `damage` | 이 패턴이 준 총 피해 |
| `damage_hits` | 피해가 적용된 접촉 수 |
| `hit_rate` | `hit ÷ resolved` |
| `dodge_rate` | `dodge ÷ resolved` |
| `parry_rate` | `parry ÷ resolved` |
| `perfect_parry_rate` | `perfect_parry ÷ resolved` |
| `damage_share` | 패턴 피해 ÷ 구간 전체 피해 |
| `damage_hit_share` | 패턴 피해 접촉 수 ÷ 구간 전체 피해 접촉 수 |

## Heatmaps.csv

한 행이 구간·레이어·격자 한 칸의 누적값이다. 격자는 월드 XZ 위치를 1미터 단위로 나눈다.

| 변수 | 의미 |
|---|---|
| `session_id` | 씬 실행 ID |
| `stage_id` | 구간 ID |
| `stage_attempt_id` | 구간 도전 ID |
| `layer` | 위치 데이터 종류 |
| `grid_x`, `grid_z` | 격자 좌표 |
| `weight` | 격자에 누적된 값 |

`layer`는 `movement`(머문 시간, 초), `attack_attempt`(공격 시도 횟수), `damage_hit`(피해 접촉 횟수)다. 원거리 공격의 선은 이 격자 데이터가 아니라 Events의 `origin_x`, `origin_z`에서 `x`, `z`까지를 이용해 분석한다. 이는 유도 투사체의 실제 곡선 궤적을 나타내지 않는다.

## Diagnostics.csv

| 변수 | 의미 |
|---|---|
| `file` | 오류가 발견된 원본 JSONL 파일명 |
| `line` | 오류가 있는 줄 번호 |
| `problem` | JSON 파싱 또는 스키마 검증 오류 |

이 문서의 스키마는 `tools/combat_telemetry/google_apps_script/Code.gs`와 `tools/combat_telemetry/Export-Telemetry.cjs`의 현재 열 정의를 기준으로 한다. 변환기나 Apps Script의 열이 바뀌면 이 사전도 함께 갱신한다.

## 스키마 0.5.0 추가 필드

| 열 또는 필드 | 의미 |
| --- | --- |
| `attemptSessionId` / `attempt_session_id` | 구간 도전별 분석 ID. 0.5.0의 `stageAttemptId`와 동일 |
| `retryIndex` / `retry_index` | 같은 run과 stage의 0부터 시작하는 도전 횟수 |
| `previousAttemptSessionId` / `previous_attempt_session_id` | 같은 구간의 바로 전 도전 ID. 첫 도전이면 빈 값 |
| `exitStageId`, `exitStageAttemptId`, `exitLocation`, `exitOccurredAtKst` | `session_ended(application_quit)`에서 남기는 종료 위치·도전·시각. death 상태와 deaths를 변경하지 않음 |
| `gameCompleted`, `gameCompletedOccurredAtKst` | 최종 게임 클리어 여부와 발생 시각. Sheets Sessions에는 `game_completed`, `game_completed_occurred_at_kst`로 제공 |
| `enemyFinalOutcomeCount` | 등록된 AttackId 중 최종 결과가 확정된 수. commit 전 Cancelled/Interrupted도 포함하며 각 결과 열 합계와 같음 |
| `enemyNormalDodgeCount`, `enemyPerfectDodgeCount` | 서로 배타적인 일반·극한 회피 최종 결과 수. 0.5.0 `enemyDodgeCount`는 일반 회피와 같음 |
| `enemyValidOutcomeCount` | commit된 공격 중 Hit·Dodge·PerfectDodge·Guard·NormalParry·PerfectParry·Miss 수 |
| `enemyResolvedCount` | 기존 의미 유지: commit된 최종 결과 중 Cancelled·Interrupted를 제외한 수 |
| `guardRepresentativeGuardCount`, `guardRepresentativePerfectParryCount`, `guardRepresentativeNoResultCount` | 고유 방어 입력당 우선순위가 적용된 대표 결과 |
| `dodgePerfectCount` | 극한 회피가 한 번 이상 발생한 고유 DodgeId 수 |
| `skillEmpowerActivationCount`, `skillSwordWaveActivationCount` | 수동 거합 시작(`skillSource=manual`)과 실제 일섬 발사 횟수. 자동 반격 거합은 `skillSource=counter` 사건으로 남고 수동 횟수에는 포함되지 않음 |
| `awarenessHitCount`, `awarenessRadiusMetersMean`, `normalEnemiesNearHitMean`, `bossesNearHitMean`, `hostileShurikensNearHitMean`, `hostileTotalNearHitMean` | 양수 피해 접촉에서 수집된 실제 인지 반경과 반경 안 개체 수의 세션별 건수·평균. 적대 투사체는 DefenseShuriken·EnemyProjectile·TwinBossProjectile을 포함하며 반사·비행 종료·파괴 및 GameObject 중복은 제외 |
| `exit_occurred_at_kst`, `game_completed_occurred_at_kst` | Apps Script core `sheets.Sessions` 응답의 종료·클리어 사건 시각. Events 페이지를 가져오기 전에도 플레이어 이탈을 분석할 수 있음 |

`enemyFinalOutcomeCount`에는 commit 전 공격 취소도 들어가므로 `enemyAttackCount`보다 커질 수 있다. `enemyValidOutcomeCount`와 `enemyResolvedCount`의 분모는 commit된 공격 기준으로 유지한다. 누락되거나 미수집된 값은 0으로 대체하지 않는다.


## Session event sequence fields

`Sessions.csv` ends with four ordering fields: `last_summary_sequence`, `last_exit_sequence`, `last_completion_sequence`, and `status_sequence`. The first three track the newest stage snapshot, session exit fact, and game completion fact independently. `status_sequence` tracks the event that supplied the selected status. Status precedence is death/final_clear/cleared, then scene_unloaded/application_quit/unclean_exit, then in_progress/started; event sequence breaks ties within a level. `player_died` can set death status before a stage summary arrives, and `game_completed` can set final_clear independently. The existing `last_event_sequence` remains a monotonic maximum for compatibility. An out-of-order quit cannot cause a later-arriving death summary to be discarded, and terminal facts do not change the summary event's own sequence.

# 온보딩·BambooStage1 분석 로그 v0.3

## 바로 사용하기

1. 새 빌드 또는 갱신된 Unity 프로젝트에서 BambooStage1을 플레이한다. Intro 시작 버튼은 Onboarding을 거쳐 이 씬을 연다.
2. 로컬 경로: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\w07-blindblade+\CombatTelemetry`.
3. `*.jsonl` 한 줄이 Events의 한 행이다. 현재 온보딩은 `stageId=0-1~0-7`, 일반 구간은 `1-1`, 보스 구간은 `1-2`다. 보스 재도전은 `1-2`만 기록한다.
4. 기본 전송 설정은 Resources에 포함된다. 기존 URL·token은 유지했으므로 새 PC의 별도 CONFIG는 필요 없다. 로컬 `upload-config.json`이 있으면 그 설정이 우선한다.
5. 전송은 기본 20초마다 시작하고, 밀린 배치는 ACK 후 이어서 보낸다. 큐가 저장·전송될 시간을 두고 종료한다. 시트가 비어 있으면 Console의 전송 오류와 아래 배포 상태를 확인한다.

## Events와 Sessions

- **Events**: 사건별 원본. stage_id, stage_attempt_id, input_id, attack_id와 함께 종류·패턴·결과·피해량·좌표·반응 시간을 열로 제공한다. 기타 정보는 payload_json에 남긴다.
- **Sessions**: 세션 전체가 아니라 **스테이지 도전당 1행**. 한 플레이에서 웨이브와 보스를 하면 2행, 보스 재도전은 1행이다. status=started/in_progress/cleared/death/application_quit/scene_unloaded.
- 같은 사건 재전송은 event_key로 중복 제거한다. Sessions는 최신 event_sequence의 누적값으로 갱신한다.
- 정상 종료 신호 없이 프로세스가 강제 종료되면 in_progress 행을 완료/사망으로 바꾸지 않는다.

## 요청한 분석과 필터

| 분석 | 사용할 데이터 |
| --- | --- |
| 구간별 사망 수 | Sessions를 stage_id로 묶어 deaths 합계 |
| 구간별 평균 처치·사망 횟수 | Sessions를 stage_id로 묶어 수치가 기록된 kills와 deaths의 평균을 계산한다. 정상 로그는 구간별 Sessions 행(도전)마다 두 값이 기록된다. |
| 피해량 원인 비중 | rangedDamage/meleeDamage, bossRangedDamage/bossEscapeBarrageDamage/bossChargeDamage 및 DamageShare 열 |
| 피격 횟수 원인 비중 | Patterns.csv의 damage_hits / 구간의 damageHitCount |
| 회피 시도 | dodgeAttempts, 거부=dodgeRejected, 실행=dodgeStarted |
| 방어 시도 | guardAttempts, guardRejected, guardAttemptsWithContact/WithoutContact |
| 방어 중 접촉 결과 | guardNormalParryCount, guardPerfectParryCount, guardGuardCount, guardHitCount |
| 반응 시간 | Events의 enemy_attack_resolved에서 reaction_seconds / input_to_contact_seconds |
| 이동 히트맵 | movement_sample의 x,z에 duration_seconds를 가중치로 사용 |
| 공격 시도 히트맵 | player_input, kind=Attack, is_attempt=true의 x,z |
| 적 공격 결과·적중률 | enemy_attack_resolved의 outcome; Sessions의 ranged/melee/bossRanged/bossEscapeBarrage/bossCharge 열 |
| 공격 적중 히트맵 | enemy_attack_resolved, applied_damage>0의 x,z |
| 원거리 공격 직선 | 같은 결과 행의 origin_x,origin_z → x,z; kind=Projectile |

비율을 여러 도전에 걸쳐 계산할 때는 비율의 평균 대신 **분자 합/분모 합**을 쓴다. 전체 적중률 분모는 실행 후 결과가 확정된 공격이며 취소·중단은 제외한다. Miss는 빗나감이고 Dodge는 실제 회피 무적/극한 회피 판정이다. Guard·다른 무적·공격 제거도 별도 결과라서 네 결과만 합하면 100%가 아닐 수 있다.

방어 결과 열은 접촉 횟수다. 한 번 누른 가드로 여러 공격을 막을 수 있어 입력 횟수와 같지 않다. guardHitCount는 누르고 있던 가드 의도와 연결된 피격이며 거부된 입력도 포함한다.

## 반응 시간 해석

- 전조 시작→입력 처리 프레임: 엔진 realtime을 사용한다. 원시 deviceTime은 따로 저장하며 엔진 시간과 직접 빼지 않는다.
- 입력→접촉: 같은 AttackId의 확정 접촉과 입력 ID를 연결한다.
- reaction_association=resolved_contact가 실제 방어/회피 연결, held_guard_intent는 가드 의도와 피격의 시간상 연결이다.
- 반응 후보 사건은 중복 전송을 줄이기 위해 더 이상 만들지 않는다. 실제 접촉에 연결된 반응 시간만 분석한다.
- 전조 전에 누른 입력이나 연결 불가 입력은 반응 시간이 빈 값이다. 0초로 채우지 않는다.
- 원거리 직선은 발사점→접촉/종료점이다. 유도 궤적을 재현하는 선은 아니다.

## CSV와 히트맵 전처리

Node.js가 설치된 PC에서 실행한다.

```powershell
powershell -ExecutionPolicy Bypass -File tools/combat_telemetry/Export-Telemetry.ps1 `
  -LogDirectory "$env:USERPROFILE\AppData\LocalLow\DefaultCompany\w07-blindblade+\CombatTelemetry" `
  -OutputDirectory "$env:USERPROFILE\Desktop\Stage1Telemetry"
```

생성 파일:

- Events.csv / Sessions.csv / OnboardingSteps.csv: 시트와 같은 구조.
- Patterns.csv: 패턴별 분모·적중/회피/패리/퍼펙트 패리 비율과 피해량·피격 횟수 비중.
- Heatmaps.csv: 1m 월드 XZ 격자별 movement(초), attack_attempt(횟수), damage_hit(횟수).
- Diagnostics.csv: 잘린 JSONL, 지원하지 않는 스키마 등 제외된 줄. 파일이 정상이어도 강제 종료 마지막 줄은 제외될 수 있다.

Google Sheets에 Heatmaps.csv를 가져와 stage_id와 layer로 필터링하고 grid_z를 행, grid_x를 열, weight 합계를 값으로 피벗 테이블을 만든 뒤 색상 척도를 적용한다. 원거리 직선은 Events.csv의 시작·끝 좌표를 이용해 별도 선분 그래프로 그린다.

## Apps Script 재배포

1. 기존 시트의 확장 프로그램 → Apps Script에서 `google_apps_script/Code.gs`를 반영하고 저장한다.
2. Script Properties의 SPREADSHEET_ID와 UPLOAD_TOKEN은 기존 값을 유지한다. 코드나 로그에 token을 출력하지 않는다.
3. 스프레드시트를 새로고침하면 상단 메뉴에 **전투 로그 → 현재 데이터 초기화…**가 나타난다. 메뉴를 선택하고 확인창에서 다시 승인하면 Events·Sessions·OnboardingSteps의 데이터 행을 삭제하고 헤더만 복원한다. 이 작업은 되돌릴 수 없으며 다른 탭, Script Properties, 로컬 JSONL은 건드리지 않는다. 미전송 로컬 로그가 남아 있으면 나중에 시트에 다시 올라올 수 있다.
4. 메뉴/초기화 기능만 바꾼 경우 웹 앱 재배포는 필요 없다. `doPost` 등 웹 앱 요청 처리를 바꾼 경우에는 배포 → 배포 관리 → 기존 웹 앱 편집 → **새 버전** → 배포한다. 새 배포를 만들지 않으면 기존 URL이 유지된다.
5. 이전 0.2 빌드는 새 수신기로 보낼 수 없다. 새 게임 빌드를 배포한다. 다른 PC에 남은 0.2 JSONL은 업로더가 건너뛴다.

Events/Sessions 헤더를 수동으로 바꾸면 header_mismatch로 저장을 거부한다. 전송 실패에서도 로컬 원본은 남고 재시도하며, ACK되지 않은 데이터를 성공으로 표시하지 않는다. 저장 한도 기본100MB와 Apps Script 처리량 때문에 장기 서비스 전체 수집에 대한 규모 검증은 별도 필요하다.

## 이번 변경의 확인 범위

- C# 빌드·Unity Editor 컴파일, 씬 연결 및 빌드 순서 확인.
- 기존 로컬 57세션과 커서 삭제. 익명 PC 식별자는 유지.
- 보스 재도전 Play에서 보스 수리검 3회와 돌진 1회의 시작·실행·적중, 피해 70, KST 시각, 자동 전송 64건 ACK를 확인했다. Google Events·Sessions 행과 로컬 CSV 전처리(65건, 오류 0건)를 확인했다. 확인용 로컬·시트 데이터는 삭제했다.
- 실제 회피·가드 입력과 1-1 웨이브의 플레이 검증은 아직 수행하지 않았다.
- 같은 PC에서 여러 참가자가 플레이하면 anonymous_player_id가 같으므로 참가자 구분은 별도로 관리한다.

## 분석 대시보드

- 비공개 대시보드: https://blindblade-combat-analysis.wan1k.chatgpt.site
- 사이트 프로젝트 소스는 `tools/combat_telemetry/dashboard_site`에 있다. 사이트는 소유자 전용 Sites Worker 버전 4로 배포했다. 기존 Google Apps Script 웹 앱 URL을 유지한 채 `dashboard_snapshot`과 온보딩 7단계 허용 목록을 웹 앱 버전 7에 배포했다. Apps Script 실행 계정은 소유자, 액세스 권한은 기존 `Anyone` 설정 그대로다. 자동 연결을 확인했으며, 2026-10-16 확인 당시 Events·Sessions·OnboardingSteps는 모두 0행이었다.
- Google Sheets에서 Events, Sessions, OnboardingSteps 탭을 각각 CSV로 다운로드하고, 사이트에서 파일을 한 번에 선택한다. Patterns, Heatmaps, Diagnostics 탭도 선택 입력할 수 있다.
- CSV 파일은 브라우저 안에서만 처리되며 사이트 서버로 업로드되지 않는다. 한 파일만 가져와도 보이지만 전체 분석에는 Events·Sessions·OnboardingSteps를 함께 불러온다.
- 로컬 JSONL을 분석하려면 위의 `Export-Telemetry.ps1` 명령으로 동일한 CSV 묶음을 생성한다.
- 자동 연결 설정: 같은 `DASHBOARD_READ_TOKEN`을 Apps Script Script Properties와 Sites 런타임 secret 양쪽에 저장한다. 현재 둘 다 설정되어 있고 자동 연결을 확인했다. 토큰을 바꿀 때는 두 값을 함께 갱신하고 Apps Script 웹 앱과 사이트를 다시 배포한다. 게임 업로드 `UPLOAD_TOKEN`은 재사용하지 않는다.
- Apps Script 웹 앱은 소유자 계정으로 실행되고 액세스 권한은 기존 `Anyone` 설정 그대로다. 별도 토큰 검증 전에는 시트 데이터를 반환하지 않는다. `/exec`를 브라우저에서 직접 열면 doGet 없음 메시지가 나오는 것은 정상이며, Sites Worker가 POST 요청을 보낸다. 스프레드시트 공유는 제한됨/소유자만으로 유지한다.
- 상세 데이터 흐름과 보안 경계는 [대시보드 기능 명세](../../docs/features/전투_텔레메트리_분석_대시보드.md)를 따른다.

## 온보딩 0-1~0-7 단계 로그

- **Events**: 온보딩 입력 한 번의 최종 처리 결과는 `onboarding_attempt`, 목표 종료 결과는 `onboarding_step`이다. 두 목표가 있는 방어 단계는 `objective_index=0/1`로 구분한다.
- **stageId 대응**: 0→`0-1` 이동, 1→`0-2` 돌진, 2→`0-3` 근접 적 처치, 3→`0-4` 수리검 극한 회피, 4→`0-5` 근접 가드·패링, 5→`0-6` 원거리 가드·패링·검기, 6→`0-7` 수리검 패링·궁극기.
- **Sessions**: 온보딩 단계마다 도전 행 하나다. `run_id`로 7단계와 이후 Stage1 전투를 묶고, `exit_step_index`와 `exit_objective_index`로 마지막 진행 지점을 본다. 같은 PC에서 같은 온보딩을 재시작해도 `run_id`가 달라지므로 별도 실행이다.
- **OnboardingSteps / OnboardingSteps.csv**: 목표당 1행. `active_seconds`는 목표 입력이 열린 시간, `first_input_seconds`는 처음 추적된 이동/전투 입력까지 그 활성 시간이다. 이 입력은 기대 행동이거나 성공 판정일 필요가 없다. `attempts`는 필요한 행동 시도, `wrong_attempts`는 다른 전투 입력이다. `retry_count`는 해당 단계의 사망 후 재진입 횟수이고 `reason`은 completed/death/scene_unloaded/application_quit이다.
- 이동 목표는 이동을 멈춘 뒤 다시 시작할 때 새 시도로 센다. 공격·방어·회피 입력은 고유 InputId로 세고 누르고 있기·놓기·버퍼 후 Started는 중복 계산하지 않는다. 입력 결과는 Started/Rejected/BufferDropped/Buffered이며 실제 목표 성공은 step의 `reason=completed`로 확인한다.
- 사망 후 다시 진입하면 새 `session_id`와 `stage_attempt_id` 행이 생기지만 `run_id`는 유지된다. Intro에서 새로 시작하면 `run_id`와 단계별 재시도 횟수가 초기화된다.
- 이전 설정(`config_version=onboarding-2026-10-16`)의 `0-1`·`0-2`는 각각 여러 단계가 합쳐진 과거 구간이다. 행은 보존하며 새 설정(`onboarding-2026-10-16-stage-ids-7`)의 7단계 데이터와 합쳐 비율을 계산하지 않는다. 대시보드는 기본으로 과거 행을 제외하고, `이전 2구간 온보딩 로그 포함`을 켜면 이전 기록임을 표시한다. OnboardingSteps CSV만 있으면 구버전 설정을 판별할 수 없으므로 Sessions도 함께 불러온다.

## 전체 플레이에서 확인할 수 있는 것

| 구간 | 핵심 지표 | 데이터 위치 |
| --- | --- | --- |
| 0-1~0-7 온보딩 각 단계 | 해당 단계의 목표별 활성 시간, 첫 추적 입력까지 시간, 요구 행동 시도·오입력·결과, 사망 재시도, 이탈 목표 | OnboardingSteps, Events의 onboarding_attempt, Sessions |
| 1-1 일반 전투 | 사망·처치, 수리검/근접 피해 비중, 회피·방어 시도와 실제 판정, 반응 시간, 이동/공격/피격 히트맵 | Sessions, Events, Patterns.csv, Heatmaps.csv |
| 1-2 보스전 | 보스 수리검/도주 난사/돌진의 적중률·피해 비중, 방어·회피 결과, 보스전 시간과 사망 | Sessions, Events, Patterns.csv, Heatmaps.csv |

온보딩을 완료하고 BambooStage1에 진입하면 전투 Sessions에도 같은 `run_id`를 남긴다. 보스 재도전은 새 `session_id`지만 같은 `run_id`로 묶인다. Stage1을 직접 실행하면 `run_id`는 비어 있다. 같은 PC에서 여러 참가자가 플레이하면 익명 PC ID도 같으므로 참가자 구분은 별도로 관리한다. Editor 확인 행은 `config_version`이 `-editor`로 끝나며 본 분석에서 제외한다.

## 스프레드시트 열 사전

Apps Script `Code.gs`의 헤더 배열 기준이다. 비어 있는 기본 탭 `시트1`은 로그 수신에 사용하지 않는다. 모든 위치는 Unity 월드의 XZ 평면이고 거리 단위는 m다. 값이 없는 열은 해당 사건에 적용되지 않았거나 제공되지 않은 값이며, 0과 빈 값을 구별한다.

### Events

한 행은 한 사건이다. 자주 쓰는 값을 열로 펼치고, 그 밖의 원본 세부 사항은 `payload_json`에 둔다.

| 열 | 의미 |
| --- | --- |
| `event_key` | `session_id:event_sequence` 형식의 중복 방지 키 |
| `received_kst` | Apps Script가 행을 받은 한국 시각 |
| `schema_version` | JSON 로그 자료 구조 버전(현재 `0.3.0`) |
| `build_version` | Unity `Application.version` |
| `config_version` | 기록 설정 버전. Editor 기록은 끝에 `-editor`가 붙음 |
| `session_id` | 씬 세션을 식별하는 임의 ID |
| `stage_id` | 현재 구간 ID: 온보딩 `0-1`~`0-7`, 일반 전투 `1-1`, 보스전 `1-2` |
| `stage_attempt_id` | 도전 요약 키. `session_id:stage_id` |
| `anonymous_player_id` | PC별 익명 ID. 참가자 고유 ID는 아니므로 같은 PC의 참가자를 분리하지 못함 |
| `event_sequence` | 해당 세션에서 증가하는 사건 순번 |
| `event_type` | 사건 종류: 예를 들어 `movement_sample`, `player_input`, `enemy_attack_resolved`, `stage_started`, `onboarding_attempt`, `onboarding_step` |
| `occurred_at_kst` | 게임에서 사건이 발생한 한국 시각 |
| `realtime_seconds` | 해당 씬 세션 시작 뒤 실제 시간(초) |
| `gameplay_seconds` | 해당 씬 세션 시작 뒤 Unity 시간 배율이 적용된 시간(초) |
| `input_id` | 사건과 연결된 플레이어 입력 ID |
| `action_id` | 사건과 연결된 행동 ID |
| `attack_id` | 같은 적 공격의 시작·확정·결과를 잇는 공격 ID |
| `dodge_id` | 회피 행동의 연결 ID |
| `kind` | 사건 종류에 따른 입력·공격 방식. 예: `Attack`, `Projectile` |
| `pattern_id` | 적 공격 패턴 식별자. `BossEscapeBarrage`는 4초 동안 누적 피해가 30 이상일 때 발동하는 보스 도주 난사이며, 각 투사체를 별도 `attack_id`로 집계함 |
| `outcome` | 공격 판정 결과. 예: Hit, Dodge, NormalParry, PerfectParry, Guard, Miss |
| `is_attempt` | 플레이어 입력이 행동 시도로 집계되는지 여부 |
| `input_status` | 입력 처리 결과: Started, Rejected, BufferDropped, Buffered 등 |
| `applied_damage` | 해당 접촉에서 실제 적용된 피해량 |
| `x`, `z` | `position`의 월드 X/Z 좌표. 히트맵 셀을 만드는 기준 위치 |
| `origin_x`, `origin_z` | 원거리 공격 발사 원점의 월드 X/Z 좌표 |
| `aim_x`, `aim_z` | 공격이 조준한 위치의 월드 X/Z 좌표 |
| `player_x`, `player_z` | 사건 시점 플레이어 위치의 월드 X/Z 좌표 |
| `duration_seconds` | 해당 이동 표본 등이 대표하는 시간(초) |
| `reaction_seconds` | 공격 전조 시작부터 연결된 플레이어 입력까지의 반응 시간(초) |
| `input_to_contact_seconds` | 연결된 입력부터 실제 공격 접촉까지의 시간(초) |
| `reaction_association` | 반응 시간 연결 근거. 예: `resolved_contact`, `held_guard_intent` |
| `payload_json` | 위 열로 펼치지 않은 원본 사건 데이터 전체 |
| `run_id` | 온보딩 단계들과 이후 전투를 묶는 실행 ID |
| `step_index` | 온보딩 단계의 0부터 시작하는 인덱스(현재 0~6) |
| `objective_index` | 한 단계 안의 세부 목표 인덱스(0부터 시작) |
| `retry_count` | 해당 온보딩 단계의 사망 후 재진입 횟수 |
| `first_input_seconds` | 목표 입력 활성화 후 첫 추적 입력까지 걸린 시간(초) |
| `expected` | 온보딩 목표가 요구한 입력 종류인지 여부 |
| `defense_outcome` | 입력과 연결된 실제 방어 접촉 결과 |

### Sessions

한 행은 전체 게임 세션이 아니라 **하나의 스테이지 도전**이다. Events의 `stage_started`, `stage_summary`, `stage_ended` 중 가장 최신 누적 스냅샷으로 같은 `stage_attempt_id` 행을 갱신한다.

| 열 | 의미 |
| --- | --- |
| `stage_attempt_id`, `session_id`, `stage_id`, `anonymous_player_id`, `build_version`, `config_version` | Events와 같은 도전·세션·구간·PC·빌드·설정 식별 값 |
| `started_kst` | 해당 구간 도전이 시작된 한국 시각 |
| `last_event_sequence` | 행에 적용된 가장 최신 요약 사건 순번 |
| `status` | 최신 요약의 상태: started, in_progress, cleared, death, application_quit, scene_unloaded 등 |
| `realtime_seconds` | 세션 시작 뒤 실제 경과 초 |
| `gameplay_seconds` | 세션 시작 뒤 Unity 시간 배율이 적용된 경과 초 |
| `active_play_seconds` | 명시적 정지 상태를 뺀 플레이 누적 시간(초) |
| `health` | 최신 구간 요약 때의 플레이어 체력 |
| `deaths` | 해당 구간 도전 중 발생한 사망 수. 재시도는 새 도전 행으로 구분됨 |
| `kills` | 해당 구간에서 기록한 적 처치 수 |
| `damageTaken` | 받은 피해량 합계 |
| `damageHitCount` | 피해가 실제 적용된 접촉 횟수 |
| `dodgeAttempts` | 회피 입력 시도 수 |
| `dodgeRejected` | 거부된 회피 입력 수 |
| `dodgeStarted` | 실행 단계까지 시작된 회피 수 |
| `guardAttempts` | 방어 입력 시도 수 |
| `guardRejected` | 거부된 방어 입력 수 |
| `guardAttemptsWithContact` | 가드 입력 의도와 적 공격 접촉이 연결된 횟수 |
| `guardAttemptsWithoutContact` | 적 공격 접촉 없이 끝난 방어 입력 수 |
| `guardHitCount` | 방어 입력 중 실제 피해를 받은 접촉 수 |
| `guardGuardCount` | 방어로 막은 접촉 수 |
| `guardNormalParryCount` | 일반 패링 성공 접촉 수 |
| `guardPerfectParryCount` | 퍼펙트 패링 성공 접촉 수 |
| `attackAttempts` | 플레이어 공격 입력 시도 수 |
| `attackRejected` | 거부된 플레이어 공격 입력 수 |
| `enemyAttackCount` | 시작 기록이 생성된 적 공격 수 |
| `enemyResolvedCount` | 취소·중단되지 않고 결과가 확정된 적 공격 수 |
| `enemyHitCount` | 플레이어에게 피해를 적용한 적 공격 수 |
| `enemyDodgeCount` | 회피 판정으로 피한 적 공격 수 |
| `enemyNormalParryCount` | 일반 패링된 적 공격 수 |
| `enemyPerfectParryCount` | 퍼펙트 패링된 적 공격 수 |
| `enemyGuardCount` | 가드로 막은 적 공격 수 |
| `enemyMissCount` | 플레이어 접촉 없이 빗나간 적 공격 수 |
| `enemyInvulnerableCount` | 무적 상태에 막힌 적 공격 수 |
| `enemyIgnoredCount` | 게임 판정에서 무시된 적 공격 수 |
| `enemyDestroyedCount` | 접촉 전에 제거되어 종료된 적 공격 수 |
| `enemyCancelledCount` | 취소된 적 공격 수 |
| `enemyInterruptedCount` | 경직 등으로 중단된 적 공격 수 |
| `reactionCount` | 반응 시간이 연결된 적 공격 접촉 수 |
| `reactionMeanSeconds` | 연결된 반응 시간 평균(초) |
| `enemyHitRate` | 결과 확정 공격 중 적중 비율(0~1) |
| `serviceDroppedEventsTotal` | 저장·전송 서비스가 버퍼 한도 등으로 누락한 사건 누계 |
| `ranged*`, `melee*`, `bossRanged*`, `bossEscapeBarrage*`, `bossCharge*` | 공격 종류별 집계. 아래 접미어 정의 참조 |
| `patterns_json` | 패턴별 판정 수·피해량 등 세부 누적 JSON |
| `run_id` | 온보딩과 전투를 함께 묶는 실행 ID |
| `exit_step_index` | 마지막으로 진행 중이던 온보딩 단계 인덱스(0부터) |
| `exit_objective_index` | 마지막으로 진행 중이던 목표 인덱스(0부터) |
| `steps_completed` | 이 구간 도전에서 완료한 온보딩 목표 수 |
| `attempts` | 구간 요약이 집계한 입력 시도 수 |
| `retry_count` | 해당 온보딩 단계의 이전 사망 후 재진입 횟수 |

공격 종류 접두어 다섯 가지(`ranged`, `melee`, `bossRanged`, `bossEscapeBarrage`, `bossCharge`)마다 다음 열이 있다.

| 접미어 | 의미 |
| --- | --- |
| `Attempts` | 해당 종류의 공격 시작 시도 수 |
| `Resolved` | 결과가 확정된 공격 수 |
| `Hits` | 피해가 적용된 공격 수 |
| `Dodges` | 회피된 공격 수 |
| `Parries` | 일반 패링된 공격 수 |
| `PerfectParries` | 퍼펙트 패링된 공격 수 |
| `Damage` | 해당 종류가 적용한 피해량 합계 |
| `HitRate` | `Hits / Resolved`, 분모가 0이면 값 없음 |
| `DamageShare` | 해당 종류 피해량 / 전체 피해량 |

### OnboardingSteps

한 행은 HUD에 표시된 세부 목표 하나의 종료 결과다.

| 열 | 의미 |
| --- | --- |
| `step_key` | 중복 삽입 방지 키: `session_id:step_index:objective_index` |
| `received_kst` | Apps Script가 목표 행을 받은 한국 시각 |
| `run_id` | 전체 온보딩 실행 ID |
| `session_id` | 목표가 진행된 씬 세션 ID |
| `stage_id` | 현재 단계 ID `0-1`~`0-7` |
| `stage_attempt_id` | 목표가 속한 단계 도전 키 |
| `step_index` | 단계 인덱스(0~6) |
| `objective_index` | 단계 안 세부 목표 인덱스(0부터 시작) |
| `retry_count` | 사망 후 같은 단계에 재진입한 누계 |
| `active_seconds` | 목표 입력이 허용된 누적 활성 시간(초) |
| `first_input_seconds` | 첫 추적 입력까지 활성 시간. 입력이 없으면 빈 값 |
| `attempts` | 목표가 요구한 입력 시도 수. 이동은 멈췄다가 다시 움직인 횟수 |
| `wrong_attempts` | 목표가 요구하지 않은 전투 입력 시도 수 |
| `movement_attempts` | 이동 목표에서 정지 뒤 다시 이동하기 시작한 횟수 |
| `started_inputs` | 최종 상태가 Started인 고유 입력 수 |
| `rejected_inputs` | 최종 상태가 Rejected인 고유 입력 수 |
| `buffered_inputs` | 최종 상태가 Buffered인 고유 입력 수 |
| `dropped_inputs` | 최종 상태가 BufferDropped인 고유 입력 수 |
| `reason` | 목표가 끝난 이유: completed, death, scene_unloaded, application_quit 등 |

### 전처리로 만드는 보조 CSV

이 파일들은 기본 Apps Script 탭이 아니라 `Export-Telemetry.ps1`이 만드는 파생 자료다.

| 파일·열 | 의미 |
| --- | --- |
| `Patterns.csv`: `stage_attempt_id`, `stage_id` | 스테이지 도전·구간 식별 값 |
| `kind`, `pattern_id` | 공격 방식과 적 공격 패턴 |
| `attempts`, `resolved` | 공격 시도·확정 판정 수 |
| `hit`, `dodge`, `parry`, `perfect_parry`, `guard`, `miss` | 판정 결과별 접촉 수 |
| `damage`, `damage_hits` | 패턴 피해량과 피해 적용 접촉 수 |
| `hit_rate`, `dodge_rate`, `parry_rate`, `perfect_parry_rate` | 각 판정 수 / `resolved` |
| `damage_share`, `damage_hit_share` | 패턴 피해량 / 구간 피해량, 패턴 피격 수 / 구간 피격 수 |
| `Heatmaps.csv`: `session_id`, `stage_id`, `stage_attempt_id` | 위치 자료의 도전 식별 값 |
| `layer` | `movement`(초), `attack_attempt`(시도 수), `damage_hit`(피해 접촉 수) |
| `grid_x`, `grid_z` | 월드 좌표를 내림해 만든 1m 격자 위치 |
| `weight` | 해당 격자에 누적할 시간 또는 횟수 |
| `Diagnostics.csv`: `file`, `line`, `problem` | 전처리에서 제외된 JSONL 원본 파일·행 번호·오류 |

## 웹사이트 지표를 읽는 방법

### 전체 요약

- **구간 도전**은 Sessions 행 수다. 참가자 수나 온보딩 전체 완료 횟수가 아니다. 한 플레이 run은 온보딩 단계마다 행이 생기고 전투에서도 `1-1`, `1-2` 행이 생긴다.
- **사망률**은 선택된 Sessions에서 사망 수 합계 ÷ 도전 행 수다. 단계별로 보고, 같은 run의 사망 재시도 행이 분모에 들어간다는 점을 고려한다. 표본이 적으면 백분율보다 사망 수와 도전 수를 함께 본다.
- **평균 활성 플레이**는 stage attempt별 `active_play_seconds`의 단순 평균이다. 여러 구간 시간을 더한 전체 플레이 시간이나 플레이어당 평균이 아니다.
- **온보딩 목표 완료**는 완료된 목표 행 ÷ 모든 목표 행이다. 플레이어가 온보딩을 끝낸 비율과 다르므로 마지막 단계 `0-7`의 완료/이탈과 `run_id`를 함께 확인한다.
- **피해 원인** 막대는 선택 데이터에서 원인별 누적 피해량을 비교한다. 막대 길이는 가장 큰 원인 대비 값이지 100% 누적 막대가 아니다. 피해 비중을 직접 비교하려면 `DamageShare`를 사용한다.

### 온보딩

- `active_seconds`가 길고 `attempts`가 적으면 안내를 이해하지 못해 머뭇거리는지 살핀다. `first_input_seconds`가 길면 목표 문구·입력 연결이 불명확할 가능성을 확인한다.
- `wrong_attempts`와 `rejected_inputs`가 많으면 조작 규칙이 혼동되거나 입력 시점·행동 조건을 학습하기 어렵다는 신호일 수 있다. 두 지표는 원인이 같지 않으므로 Events의 입력 종류와 거부 사유를 함께 본다.
- 높은 `attempts`가 곧 실패를 뜻하지 않는다. 필요한 행동을 반복해 성공할 수 있으므로 `reason`, 실제 방어 결과, `retry_count`를 함께 본다.
- 각 단계의 목표 인덱스 의미가 다르므로 여러 단계의 평균 시간만 비교하지 않는다. 같은 `stage_id`와 `objective_index`끼리 run별로 비교한다.

### 전투 판정·방어

- **적 공격 적중률**은 결과가 확정된 공격 중 `Hit` 비율이다. 공격 패턴별로 나누고 판정 표본 수도 같이 본다. 방어·회피 능력 전체를 한 숫자로 단정하지 않는다.
- **반응 시간**은 전조 시작부터 연결 입력까지다. 작은 값은 빠른 반응을 뜻하지만 성공한 방어를 보장하지 않는다. `input_to_contact_seconds` 및 Hit/Dodge/Parry/Guard 결과와 함께 본다.
- **피해 접촉**은 피해량이 0보다 큰 실제 적용 횟수다. 공격 판정 총수나 피해량 크기와 다르다.
- **회피·방어 시도**는 입력 수이고, Guard/Parry/Hit 계열은 적 공격 접촉 결과다. 한 번의 가드 입력으로 여러 공격을 막거나 한 번도 막지 못할 수 있어 둘을 같은 분모로 계산하지 않는다.
- 일반 적과 보스, 원거리와 근거리의 적중률을 비교할 때 비율 평균을 구하지 않는다. `합계 Hits ÷ 합계 Resolved`로 합쳐 작은 표본의 비율이 과도한 영향을 주지 않게 한다.

### 전장 위치

- **이동 시간**은 이동 표본 간격을 1m 격자에 누적한 체류 시간이다. 이동 거리나 이동 속도가 아니다. 길게 머무는 지점과 거의 지나지 않는 길을 본다.
- **공격 시도**는 공격 입력 시점의 위치별 횟수다. 피해 성공 위치와 혼동하지 않는다.
- **피해 접촉**은 실제 피해가 적용된 적 공격 사건의 기록 위치별 횟수다. 한 번의 접촉은 여러 히트맵 셀에 분산되지 않는다.
- 지도 중앙 `(0,0)`은 `BambooStage1`의 `Arena` 원점이다. 대나무 벽은 씬의 약 25~33m 반경 배치를 원형 띠로 근사했으므로 개별 줄기·충돌 범위로 해석하지 않는다. 벽 근처의 밀집은 경로 선택·막힘의 후보를 보여 줄 뿐, 원인 확정에는 플레이 영상과 함께 확인한다.
- 원거리 공격 선은 발사점→접촉/종료점을 잇는 직선이다. 실제 유도 궤적, 이동 속도, 공격자의 연속 경로를 표현하지 않는다.

### 데이터 상태·분석 주의

- 데이터 상태의 행 수는 현재 가져온 파일/시트 행 수다. 플레이어 수가 아니며, Diagnostics 행은 로그 실패나 변환 제외 원인을 읽는 데 쓴다.
- build, 날짜, Editor 제외, 구 온보딩 포함 상태를 고정하고 회차 간 비교한다. 새로운 7단계 stageId와 과거 2구간 stageId는 `config_version`으로 분리한다.
- 비율은 원시 이벤트나 합계 분자·분모로 다시 계산한다. 반올림된 구간별 비율을 단순 평균하지 않는다. 작은 표본에서는 결론 대신 다음 테스트에서 확인할 가설로 둔다.

## 전송량 정리와 현재 배포

- Stage1의 이동 표본 0.5→1초, 진행 요약 5→15초. 5분 동안 계속 이동했다면 정기 표본 약 600→300건, 요약 약 60→20건이다. 실제 건수는 플레이 행동에 따라 달라진다.
- 반복된 전체 Inspector 설정 덤프, 위치 표본의 체력과 Y 좌표, 중복 반응 후보·회피 종료·상태 변경 사건은 새 로그에서 보내지 않는다. 반응 시간은 실제 적 공격 접촉 결과에서 확인한다.
- Apps Script의 `setupSheets`는 기존 Events·Sessions 행을 보존하면서 새 열을 추가하고 OnboardingSteps를 생성한다. 웹 앱은 동일 URL의 **버전 7**, 대시보드는 소유자 전용 **Sites 버전 4**로 갱신됐다. `resetTelemetrySheets`는 세 탭 데이터를 비우므로 초기화가 필요할 때만 실행한다.
- CSV 전처리는 Events.csv, Sessions.csv, OnboardingSteps.csv, Patterns.csv, Heatmaps.csv, Diagnostics.csv를 만든다. 기존 0.3.0 JSONL도 같은 변환기에 남겨 두었고 새 봉투 버전은 도입하지 않았다.
- 구간 ID가 Google Sheets에서 날짜로 바뀐 과거 Events·Sessions 행은 stage_attempt_id에서 복원했으며, 세 탭의 stage_id 열은 텍스트 형식이다. 

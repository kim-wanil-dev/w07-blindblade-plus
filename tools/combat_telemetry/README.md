# 온보딩·BambooStage1 분석 로그 v0.3

## 바로 사용하기

1. 새 빌드 또는 갱신된 Unity 프로젝트에서 BambooStage1을 플레이한다. Intro 시작 버튼은 Onboarding을 거쳐 이 씬을 연다.
2. 로컬 경로: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\w07-blindblade+\CombatTelemetry`.
3. `*.jsonl` 한 줄이 Events의 한 행이다. 온보딩은 stageId=0-1/0-2, 일반 구간은 1-1, 보스 구간은 1-2다. 보스 재도전은 1-2만 기록한다.
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
| 피해량 원인 비중 | rangedDamage/meleeDamage, bossRangedDamage/bossChargeDamage 및 DamageShare 열 |
| 피격 횟수 원인 비중 | Patterns.csv의 damage_hits / 구간의 damageHitCount |
| 회피 시도 | dodgeAttempts, 거부=dodgeRejected, 실행=dodgeStarted |
| 방어 시도 | guardAttempts, guardRejected, guardAttemptsWithContact/WithoutContact |
| 방어 중 접촉 결과 | guardNormalParryCount, guardPerfectParryCount, guardGuardCount, guardHitCount |
| 반응 시간 | Events의 enemy_attack_resolved에서 reaction_seconds / input_to_contact_seconds |
| 이동 히트맵 | movement_sample의 x,z에 duration_seconds를 가중치로 사용 |
| 공격 시도 히트맵 | player_input, kind=Attack, is_attempt=true의 x,z |
| 적 공격 결과·적중률 | enemy_attack_resolved의 outcome; Sessions의 ranged/melee/bossRanged/bossCharge 열 |
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
- 사이트 프로젝트 소스는 `tools/combat_telemetry/dashboard_site`에 있다. 사이트는 소유자 전용 Sites Worker 버전 2로 배포했다. 2026-10-16에 기존 Google Apps Script 웹 앱 URL을 유지한 채 읽기 전용 `dashboard_snapshot` 핸들러를 버전 6으로 배포했다. 실행 계정은 소유자, 액세스 권한은 기존 `Anyone` 설정 그대로다. 별도 읽기 토큰은 아직 설정하지 않아 자동 읽기는 대기 중이며, 페이지에서 CSV 대체 입력을 사용할 수 있다.
- Google Sheets에서 Events, Sessions, OnboardingSteps 탭을 각각 CSV로 다운로드하고, 사이트에서 파일을 한 번에 선택한다. Patterns, Heatmaps, Diagnostics 탭도 선택 입력할 수 있다.
- CSV 파일은 브라우저 안에서만 처리되며 사이트 서버로 업로드되지 않는다. 한 파일만 가져와도 보이지만 전체 분석에는 Events·Sessions·OnboardingSteps를 함께 불러온다.
- 로컬 JSONL을 분석하려면 위의 `Export-Telemetry.ps1` 명령으로 동일한 CSV 묶음을 생성한다.
- 자동 연결을 켜려면 Apps Script의 Script Properties와 Sites 런타임 secret에 **동일한** 임의 토큰을 설정한다. 스프레드시트 공유는 **제한됨/소유자만**으로 유지한다.
  1. 배포된 `dashboard_snapshot` 경로는 `DASHBOARD_READ_TOKEN` Script Property를 확인하고 분석에 필요한 Events·Sessions·OnboardingSteps 열만 반환한다. Apps Script 웹 앱 버전 6에 반영됐다. 브라우저에서 URL을 직접 열지 않고 Sites Worker가 POST한다.
  2. 32자 이상의 임의 토큰을 만들고 Apps Script 프로젝트 설정 → 스크립트 속성에 `DASHBOARD_READ_TOKEN`으로 저장한다. 기존 게임 업로드 `UPLOAD_TOKEN`은 재사용하지 않는다.
  3. 같은 토큰을 Sites 프로젝트의 런타임 환경 변수 `DASHBOARD_READ_TOKEN`에 **secret**으로 저장하고 소유자 전용 사이트 버전 2를 다시 배포한다. `.openai/hosting.json`, `src/dashboard.html`, 브라우저 코드, 문서에는 토큰을 넣지 않는다.
  4. 웹 앱은 소유자 계정으로 실행되고 액세스 권한은 기존 `Anyone` 설정을 유지한다. 요청 본문의 별도 토큰 검증 전에는 시트 데이터를 반환하지 않는다. Workspace 정책에서 Anyone을 허용하지 않으면 관리자 제한을 풀거나 Google 인증 API 경로를 별도로 써야 한다. `/exec`를 브라우저에서 직접 열면 doGet 없음 메시지가 나오는 것은 정상이며, 사이트는 POST를 사용한다.
- 현재 두 위치의 `DASHBOARD_READ_TOKEN`은 설정 전이다. Sites secret 저장과 재배포가 끝나기 전까지 자동 연결 대기 상태가 보이며 CSV 가져오기는 계속 쓸 수 있다.
- 상세 데이터 흐름과 보안 경계는 [대시보드 기능 명세](../../docs/features/전투_텔레메트리_분석_대시보드.md)를 따른다.

## 온보딩 0-1·0-2와 목표별 분석

- **Events**: 온보딩 입력 한 번의 최종 처리 결과는 `onboarding_attempt`, 목표 종료 결과는 `onboarding_step`이다. 두 목표가 있는 방어 단계는 `objective_index=0/1`로 구분한다.
- **Sessions**: 0-1은 이동·회피·처치·수리검 극한 회피(단계 0~3), 0-2는 근접 방어부터 궁극기(단계 4~8)다. `run_id`로 사망 전후 실행을 묶고, `exit_step_index`와 `exit_objective_index`로 이탈 지점을 본다. 단계 경계는 OnboardingFlow의 Inspector 설정으로 바꿀 수 있다.
- **OnboardingSteps / OnboardingSteps.csv**: 목표당 1행. `active_seconds`는 입력이 열린 시간, `first_input_seconds`는 첫 유효 입력까지 그 활성 시간, `attempts`는 필요한 행동의 고유 시도, `wrong_attempts`는 다른 전투 입력이다. `retry_count`는 해당 단계에서 사망 후 재진입 횟수이고 `reason`은 completed/death/scene_unloaded/application_quit이다.
- 이동 목표는 이동을 멈춘 뒤 다시 시작할 때 새 시도로 센다. 공격·방어·회피 입력은 고유 InputId로 세고 누르고 있기·놓기·버퍼 후 Started는 중복 계산하지 않는다. 입력 결과는 Started/Rejected/BufferDropped/Buffered이며 실제 목표 성공은 step의 `reason=completed`로 확인한다.
- 0-1·0-2 구간에 사망하면 현재 구간을 death로 닫고 재시작은 새 `session_id`의 같은 구간 행으로 기록한다. Intro에서 새로 시작하면 `run_id`가 바뀌고 재시도 횟수는 0이다.

## 전체 플레이에서 확인할 수 있는 것

| 구간 | 핵심 지표 | 데이터 위치 |
| --- | --- | --- |
| 0-1 이동·회피·처치·극한 회피 | 목표별 활성 시간, 첫 입력 지연, 필요한 행동 시도·오입력·입력 거부, 사망 재시도, 이탈 목표 | OnboardingSteps, Events의 onboarding_attempt, Sessions |
| 0-2 방어·검기·궁극기 | 가드/퍼펙트 패리 목표별 시도와 실제 방어 결과, 완료까지 시간, 재시도·이탈 | OnboardingSteps, Events의 onboarding_attempt, Sessions |
| 1-1 일반 전투 | 사망·처치, 수리검/근접 피해 비중, 회피·방어 시도와 실제 판정, 반응 시간, 이동/공격/피격 히트맵 | Sessions, Events, Patterns.csv, Heatmaps.csv |
| 1-2 보스전 | 보스 수리검/돌진의 적중률·피해 비중, 방어·회피 결과, 보스전 시간과 사망 | Sessions, Events, Patterns.csv, Heatmaps.csv |

온보딩을 완료하고 BambooStage1에 진입하면 전투 Sessions에도 같은 `run_id`를 남긴다. 보스 재도전은 새 `session_id`지만 같은 `run_id`로 묶인다. Stage1을 직접 실행하면 `run_id`는 비어 있다. 같은 PC에서 여러 참가자가 플레이하면 익명 PC ID도 같으므로 참가자 구분은 별도로 관리한다. Editor 확인 행은 `config_version`이 `-editor`로 끝나며 본 분석에서 제외한다.

## 전송량 정리와 현재 배포

- Stage1의 이동 표본 0.5→1초, 진행 요약 5→15초. 5분 동안 계속 이동했다면 정기 표본 약 600→300건, 요약 약 60→20건이다. 실제 건수는 플레이 행동에 따라 달라진다.
- 반복된 전체 Inspector 설정 덤프, 위치 표본의 체력과 Y 좌표, 중복 반응 후보·회피 종료·상태 변경 사건은 새 로그에서 보내지 않는다. 반응 시간은 실제 적 공격 접촉 결과에서 확인한다.
- Apps Script의 `setupSheets`는 기존 Events·Sessions 행을 보존하면서 새 열을 추가하고 OnboardingSteps를 생성한다. 웹 앱은 동일 URL의 **버전 5**로 갱신됐다. `resetTelemetrySheets`는 세 탭 데이터를 비우므로 현재 데이터에 실행하지 않는다.
- CSV 전처리는 Events.csv, Sessions.csv, OnboardingSteps.csv, Patterns.csv, Heatmaps.csv, Diagnostics.csv를 만든다. 기존 0.3.0 JSONL도 같은 변환기에 남겨 두었고 새 봉투 버전은 도입하지 않았다.
- 구간 ID가 Google Sheets에서 날짜로 바뀐 과거 Events·Sessions 행은 stage_attempt_id에서 복원했으며, 세 탭의 stage_id 열은 텍스트 형식이다. 

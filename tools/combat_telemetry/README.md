# BambooStage1 분석 로그 v0.3

## 바로 사용하기

1. 새 빌드 또는 갱신된 Unity 프로젝트에서 BambooStage1을 플레이한다. Intro 시작 버튼도 이 씬을 연다.
2. 로컬 경로: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\w07-blindblade+\CombatTelemetry`.
3. `*.jsonl` 한 줄이 Events의 한 행이다. 일반 구간은 stageId=1-1, 보스 구간은 1-2다. 보스 재도전은 1-2만 기록한다.
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
- reaction_candidate는 입력 당시 진행 중인 공격마다 남기는 후보다. candidateCount>1일 때 어느 공격에 반응했는지는 확정할 수 없다. 후보를 모두 평균내 실제 반응 시간으로 보고하지 않는다.
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

- Events.csv / Sessions.csv: 시트와 같은 구조.
- Patterns.csv: 패턴별 분모·적중/회피/패리/퍼펙트 패리 비율과 피해량·피격 횟수 비중.
- Heatmaps.csv: 1m 월드 XZ 격자별 movement(초), attack_attempt(횟수), damage_hit(횟수).
- Diagnostics.csv: 잘린 JSONL, 지원하지 않는 스키마 등 제외된 줄. 파일이 정상이어도 강제 종료 마지막 줄은 제외될 수 있다.

Google Sheets에 Heatmaps.csv를 가져와 stage_id와 layer로 필터링하고 grid_z를 행, grid_x를 열, weight 합계를 값으로 피벗 테이블을 만든 뒤 색상 척도를 적용한다. 원거리 직선은 Events.csv의 시작·끝 좌표를 이용해 별도 선분 그래프로 그린다.

## Apps Script 재배포

1. 기존 시트의 확장 프로그램 → Apps Script에서 `google_apps_script/Code.gs`를 반영한다.
2. Script Properties의 SPREADSHEET_ID와 UPLOAD_TOKEN은 기존 값을 유지한다. 코드나 로그에 token을 출력하지 않는다.
3. 기존 데이터를 버리기로 결정한 경우에만 `resetTelemetrySheets`를 한 번 실행한다. Events·Sessions만 비운다. 정상 운영 시에는 setupSheets만 사용한다.
4. 배포 → 배포 관리 → 기존 웹 앱 편집 → **새 버전** → 배포한다. 새 배포를 만들지 않으면 기존 URL이 유지된다.
5. 이전 0.2 빌드는 새 수신기로 보낼 수 없다. 새 게임 빌드를 배포한다. 다른 PC에 남은 0.2 JSONL은 업로더가 건너뛴다.

Events/Sessions 헤더를 수동으로 바꾸면 header_mismatch로 저장을 거부한다. 전송 실패에서도 로컬 원본은 남고 재시도하며, ACK되지 않은 데이터를 성공으로 표시하지 않는다. 저장 한도 기본100MB와 Apps Script 처리량 때문에 장기 서비스 전체 수집에 대한 규모 검증은 별도 필요하다.

## 이번 변경의 확인 범위

- C# 빌드·Unity Editor 컴파일, 씬 연결 및 빌드 순서 확인.
- 기존 로컬 57세션과 커서 삭제. 익명 PC 식별자는 유지.
- 보스 재도전 Play에서 보스 수리검 3회와 돌진 1회의 시작·실행·적중, 피해 70, KST 시각, 자동 전송 64건 ACK를 확인했다. Google Events·Sessions 행과 로컬 CSV 전처리(65건, 오류 0건)를 확인했다. 확인용 로컬·시트 데이터는 삭제했다.
- 실제 회피·가드 입력과 1-1 웨이브의 플레이 검증은 아직 수행하지 않았다.
- 같은 PC에서 여러 참가자가 플레이하면 anonymous_player_id가 같으므로 참가자 구분은 별도로 관리한다.

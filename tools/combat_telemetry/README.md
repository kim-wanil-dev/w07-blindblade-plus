# 전투 텔레메트리 로그 v0.4

## 게임 빌드 0.1.3 / 스키마 0.4.0

- 0.1.3은 기존 1-1·1-2에 2-1 일반 전투와 2-2 쌍둥이 보스전을 추가한다. `run_id`로 실행을 연결하고 `entry_mode`로 새 게임, 이어 하기, 체크포인트 재도전, 에디터 직접 진입 및 자연 전환을 구분한다.
- 0.4.0은 기존 0.3.0 로그를 보존하며 Apps Script에서 두 스키마를 함께 수신한다. Events·Sessions의 기존 열 순서는 유지하고 새 열은 오른쪽에 추가한다. 0.3.0 행에서 0.4.0 전용 값은 빈 칸이며 0으로 해석하지 않는다.
- 적 생명주기 이벤트는 `enemy_spawned`, `enemy_defeated`, `enemy_despawned`다. Melee·Beast·Thrower 종류, 스폰 순번과 당시 근접/Beast 확률로 실제 Beast 출현 비율을 설정 기대 비율과 비교한다.
- 일반 Beast 도약 공격은 `DefenseBeastLeap`으로 기록한다. 쌍둥이 보스 부모 패턴은 `Cooperation`, `IaiCharge`, `AlternatingMelee`, `Countering`; 개별 공격은 `TwinBossRanged`, `TwinHumanSlash`, `TwinHumanCounterSlash`, `TwinHumanIai`, `TwinBeastBite`다. 시작·확정·최종 판정은 AttackId 및 부모 패턴 ID로 연결한다.
- `stage_config`는 스폰 설정과 전장 중심·반경을 도전당 한 번 기록한다. 전장 히트맵은 이 중심을 원점으로 정규화하고 2m 격자를 사용한다. 대나무 벽 근사는 1막에서만 표시하고 2막은 대나무 오버레이 없이 전장 범위를 표시한다.
- CSV 내보내기는 Events, Sessions, OnboardingSteps, Patterns, Heatmaps, EnemyLifecycle, BossPatterns, Diagnostics를 만든다. Events와 Sessions 외 두 CSV는 원본 Events에서 파생한 로컬 분석 보조 자료이며 게임이 별도 전송하지 않는다.
- 대시보드는 0.1.2와 0.1.3의 표본 수·평균 지표, run_id 스테이지 퍼널, 2-1 적 구성과 Beast 기대 비율, 2-2 부모/개별 공격 결과와 보스별 피해·처치 시각, 데이터 품질 경고를 제공한다. 자료가 없는 구간은 0 대신 `수집 안 됨`으로 표시한다.
- 봉투 형식은 [0.4.0 JSON Schema](../../docs/contracts/combat-telemetry-v0.4.schema.json), 이전 형식은 [0.3.0 JSON Schema](../../docs/contracts/combat-telemetry-v0.3.schema.json)를 따른다.

## 2026-10-19 업로드 지연 개선 (배포 대기)

- `google_apps_script/Code.gs`에서 업로드마다 Events 전체 `event_key`를 읽던 코드를 제거했다. 숨김 시트 `_TelemetryUploadIndex`에 세션별로 실제 저장된 사건 순번 구간을 기록하고 마지막 색인 위치 뒤의 행만 추가로 확인한다. 정상 운영에서는 직전 배치의 최대 100행만 추가로 읽는다. 전체 Events 재조회 대신 세션당 한 행의 작은 인덱스를 읽는다.
- 최대 순번만 저장하지 않고 `[1,100]`, `[151,156]` 같은 구간을 보존한다. 나중에 101~150이 도착해도 누락된 기록을 새로 저장하고 같은 사건의 재전송은 건너뛴다. 같은 배치 안의 중복도 제거한다.
- 인덱스에는 이미 Events에 있는 키만 넣는다. 색인 구간 저장·flush 후 checkpoint를 변경하므로 중간 실패 시 checkpoint 뒤의 행을 다시 확인할 수 있다. 현재 요청에서 추가한 Events는 다음 요청에서 색인한다. checkpoint가 직전 배치만큼 뒤에 있는 것은 의도된 동작이다. 현재 배치의 ACK를 늦추는 대기열은 아니다.
- 재전송에서도 Sessions·OnboardingSteps 처리는 다시 실행한다. 따라서 Events 저장 후 요약 갱신이 실패했던 배치도 복구할 수 있다. ACK는 최종 저장 확인 후에만 보낸다.
- 스테이지 요약이 없는 배치는 Sessions 조회를 생략한다. 여러 요약 행이 변경되면 연속 행을 묶어 기록한다. `received_kst`는 요청 처리가 시작될 때 기록하고 잠금·색인 확인에 쓴 시간을 포함하지 않는다.
- 웹 앱 오류는 `busy`, `header_mismatch`, `upload_index_invalid` 등을 구분해 반환하고 Apps Script 실행 로그에 실패 단계와 예외를 남긴다. 요청 본문과 토큰은 출력하지 않는다.
- 로컬 구현과 JavaScript 구문 확인 완료. 운영 Apps Script 저장·인덱스 준비·새 버전 배포와 개선 후 전송 시간 확인은 아직 하지 않았다. 아래 **Apps Script 재배포** 절을 따른다. 기존 0.3.0·0.4.0 빌드를 그대로 사용할 수 있다.

### 기존 배포 검증 기록 (업로드 인덱스 적용 전)

- Apps Script 웹 앱 v11은 기존 전송/읽기 URL을 유지한다. 0.3.0·0.4.0 이벤트 수신과 시트 필드 호환을 유지하고, 대시보드 Snapshot은 분석에 필요한 열만 그룹 범위로 읽는다. Sessions는 기존 열 순서를 보존하면서 알려진 헤더 이름으로 값을 매핑한다.
- Sites v10과 Apps Script 최신 배포본이 https://blindblade-combat-analysis.wan1k.chatgpt.site 에서 활성 상태다. Sites 접근 모드는 public이며 Google Spreadsheet 자체는 비공개다. Worker는 서버의 DASHBOARD_READ_TOKEN을 사용하고 브라우저에 토큰을 전달하지 않는다.
- v10 배포 당시 자동 연결 화면에서 Events 51,405행, Sessions 533행, OnboardingSteps 335행을 확인했다. v11 이후 build `0.1.3`의 `2-1`, `1-1`, `1-2` Sessions 행이 추가됐다. 2-2 패턴 결과가 충분히 적재됐는지는 계속 수집한다.
- 2026-10-19 추가 확인: 운영 대시보드의 자동 연결 API가 29~70초 뒤 502를 반환하는 경우가 있어 사이트가 533 Sessions 스냅샷을 유지했다. 실제 시트에는 row 535 `0.1.3 / 2-1 / application_quit / bamboo-2026-10-14-editor`, row 536 `0.1.3 / 1-1 / cleared`가 있다. Editor 로그 제외 필터가 기본 적용되므로 row 535는 체크 해제 전까지 분석에서 빠진다.
- Apps Script의 연속 범위 읽기 최적화 배포 후 자동 연결에 성공했다. 확인 시 Events 54,087건, Sessions 543건, OnboardingSteps 335건이 표시됐고 0.1.3 선택 및 2-1 1행·2-2 5행 집계를 확인했다. 이전에는 Events 조회가 70~80초 뒤 502로 실패했다. 최적화는 지정 열 사이의 개별 Sheets 범위 호출을 단일 연속 범위 읽기로 합치며 기존 API URL을 유지한다.
- `dotnet build Assembly-CSharp.csproj --no-restore` 성공(경고 0, 오류 0). Apps Script JavaScript 구문, 0.4.0 Schema JSON, CSV 전처리기 구문 확인도 통과했다.
- Windows `StandaloneWindows64` Player 빌드가 성공했다. 실행 파일은 `C:\Users\JUNGLE\Desktop\Blindblade-0.1.3-Telemetry\w07-blindblade+.exe`이며 BuildReport 오류 0건·경고 27건이다. 포함된 `Assembly-CSharp.dll`에서 `combat-analysis-0.1.3`과 스키마 `0.4.0`을 확인했다. 경고에는 `Shader Graphs/Trail`의 `pow(f,e)` 관련 메시지가 포함된다.
- Unity Editor 강제 재컴파일 뒤 로컬 JSONL에서 빌드 0.1.3·스키마 0.4.0·stage `2-1`·`stage_config`를 확인했다. 이 세션은 `bamboo-2026-10-14-editor` 설정이라 정식 플레이 표본과 분리한다. Windows Player에서 2-1·2-2를 실제 플레이한 로그와 Google Sheets ACK는 아직 확인하지 않았다.
- 재컴파일 전에 생성된 스키마 0.3.0·`unassigned` 로컬 실행은 전송 확인 없이 남아 있다. 이 세션은 정식 0.1.3 자료로 사용하지 않는다.

## 바로 사용하기

1. 새 빌드 또는 갱신된 Unity 프로젝트에서 BambooStage1을 플레이한다. Intro 시작 버튼은 Onboarding을 거쳐 이 씬을 연다.
2. 로컬 경로: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\w07-blindblade+\CombatTelemetry`.
3. 빌드 실행 중 CSV 파일은 만들지 않는다. 원본은 세션별 JSONL이며, 아래 Export 명령으로 Events·Sessions·OnboardingSteps 등의 CSV를 만든다.
4. `*.jsonl` 한 줄이 Events의 한 행이다. 온보딩은 `0-1~0-5`, 1막은 `1-1`·`1-2`, 2막은 `2-1`·`2-2`다. 이전 7단계 기록을 위한 `0-6`·`0-7` 값도 호환용으로 남아 있다.
5. 기본 전송 설정은 Resources에 포함된다. 기존 URL·token은 유지했으므로 새 PC의 별도 CONFIG는 필요 없다. 로컬 `upload-config.json`이 있으면 그 설정이 우선한다.
6. 일반 로그는 기본 20초마다 전송을 시작하고, 온보딩 목표 결과는 목표가 닫힐 때 즉시 업로드를 요청한다. 밀린 배치는 ACK 후 이어서 보낸다. 시트가 비어 있으면 최신 로컬 JSONL, ACK 커서, Player.log의 전송 오류를 확인한다.

## 다른 PC에서 시트가 비어 있을 때

- `로컬 로그 용량 한도` 오류가 있으면 네트워크 전송 이전에 저장이 실패한 것이다. 2026-10-16 수정본부터 구버전 스키마의 보관 파일은 현재 스키마의 100MiB 저장 예산에서 제외한다. 기존 파일은 삭제하지 않는다. 현재 스키마 자체의 용량 한도는 유지한다.

1. 배포한 빌드 폴더의 `Export-CombatTelemetryDiagnostics.cmd`를 실행하고 `Y`로 진단 파일 생성에 동의한다. 바탕화면에 `Blindblade-Telemetry-Diagnostics-*.zip`이 생긴다. 이 파일을 담당자에게 전달한다. 실행한 PC의 로그만 수집하며, 전송 토큰이 들어갈 수 있는 `upload-config.json`은 제외한다.
2. `Player.log`의 `CombatTelemetry upload acknowledged`가 있으면 서버가 최소 한 배치를 확인했다. `Google Sheets upload was not acknowledged`가 있으면 HTTP 상태, transport, server, client 값을 확인한다. 게임은 실패한 배치를 로컬에 남기고 다음 실행에서 다시 시도한다.
3. `CombatTelemetry`의 `*.jsonl`은 원본이다. 같은 이름에 목적지 해시가 붙은 `*.cursor`의 숫자가 JSONL 바이트 길이와 같으면 그 파일의 모든 사건이 ACK된 것이다. 커서가 없거나 더 작으면 아직 미확인 사건이 남아 있다.
4. 빌드에 포함된 주소와 토큰만 사용하므로 참가자 PC마다 CONFIG 파일이나 Google 계정 로그인은 필요 없다. 이 PC에서 검증한 빌드 폴더 전체를 전달한다. 앱은 Windows의 사용자별 `LocalLow`에 쓰므로 빌드 시 폴더 접근 권한 요청은 필요하지 않다. 진단 ZIP 생성은 사용자가 직접 동의해야 한다.
5. 정상 종료에서는 마지막 사건을 로컬에 저장하고 네트워크 ACK를 기다리지 않는다. 진행 중인 요청은 취소하며 로컬 저장은 최대 1.5초 기다린다. 미확인 로그는 다음 게임 실행에서 재전송한다. 게임이 종료된 동안에는 업로더가 실행되지 않는다.

## Events와 Sessions

- 전송 완료된 **종료 세션**은 서버 ACK 확인 후 JSONL·JSON·커서를 자동 삭제한다. 플레이 중인 세션이나 미전송 기록은 보존한다. 이전 실행의 완전 ACK 기록도 다음 업로드 점검 때 정리한다. 따라서 로컬 파일이 없다는 사실만으로 수집 실패로 판단하지 말고 시트와 Player.log를 함께 확인한다. 전송 완료 자료의 CSV·백업은 시트에서 내려받는다.
- Events는 기록되는데 Sessions가 비어 있으면 `Player.log`에서 `Google Sheets upload was not acknowledged`를 확인한다. Apps Script는 Events를 먼저 저장한 뒤 Sessions를 갱신하므로 Sessions 단계의 오류가 나면 Events만 보일 수 있다. 기존 Sessions 헤더 순서가 달라도 헤더 이름으로 매핑하며, 현재 지원 필드의 누락 열은 오른쪽에 추가한다. 알 수 없는 헤더나 중복 헤더는 열 데이터 오배치를 막기 위해 거부한다. ACK되지 않은 JSONL은 보존되고 다음 게임 실행에서 재전송된다.
- 2026-10-19 확인: Apps Script v11 배포 후 Player에서 업로드 ACK가 기록됐고 Sessions 행이 533개에서 536개로 늘었다. 추가된 build `0.1.3` 행의 stage는 `2-1`, `1-1`, `1-2`다.

- **Events**: 사건별 원본. stage_id, stage_attempt_id, input_id, attack_id와 함께 종류·패턴·결과·피해량·좌표·반응 시간을 열로 제공한다. 기타 정보는 payload_json에 남긴다.
- **Sessions**: 세션 전체가 아니라 **스테이지 도전당 1행**. 한 플레이에서 웨이브와 보스를 하면 2행, 보스 재도전은 1행이다. status=started/in_progress/cleared/death/application_quit/scene_unloaded.
- 같은 사건 재전송은 event_key로 중복 제거한다. Sessions는 최신 event_sequence의 누적값으로 갱신한다.
- 정상 종료 신호 없이 프로세스가 강제 종료되면 in_progress 행을 완료/사망으로 바꾸지 않는다.

### 웹사이트에서 중간 종료 확인

- 분석 사이트의 **이탈 분석** 탭에서 구간별 중도 종료율·종료 건수, 가장 많이 종료한 구간, 구간 진입 후 활성 플레이 시간대, KST 종료 상세를 확인한다. 상단 구간·빌드·날짜·Editor 필터가 적용된다.
- 중도 종료율은 **중도 종료가 확인된 도전 ÷ 완료·사망·종료가 확정된 도전**이다. 사람 수 기준 비율이 아니며, 진행 중·종료 미확정 기록은 분모에서 제외한다. 사망 기록만으로 종료를 추정하지 않는다.
- `Sessions.status=application_quit`는 플레이 중 종료다. `Events.event_type=session_ended`이고 `reason=application_quit`이면 `stage_attempt_id`로 Sessions와 연결해 사망 후 종료도 확인한다. 구간 완료 후 종료는 상세에 표시하되 중도 종료율에서는 제외한다.
- 종료까지 시간은 해당 구간의 `active_play_seconds`이며 일시정지를 제외한다. 정확한 종료 KST는 Events의 `occurred_at_kst`다. 이벤트가 없으면 시각 미수집으로 표시한다. 강제 종료·전송 누락은 확인된 종료에 포함하지 않는다.
- 같은 `stage_attempt_id`의 중복 행은 최신 `last_event_sequence`만 반영한다. `run_id`는 이어 하기에도 유지될 수 있으므로, 뒤에 플레이를 재개했더라도 이전 종료 기록을 삭제하지 않는다.
- 기존 스프레드시트 열만 사용한다. 이번 대시보드 추가를 위해 게임을 다시 빌드하거나 Apps Script를 재배포할 필요는 없다.

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

- 반응 시작→입력 처리 프레임: 엔진 realtime을 사용한다. 대부분 패턴은 전조 시작부터 계산하고, 2막 짐승 물기(`TwinBeastBite`)는 돌진 중 눈 섬광(`PounceGlint`) 시각을 기준으로 삼는다. 원시 deviceTime은 따로 저장하며 엔진 시간과 직접 빼지 않는다.
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

1. 기존 시트의 확장 프로그램 → Apps Script에서 이 프로젝트의 `google_apps_script/Code.gs` 전체를 반영하고 저장한다.
2. Script Properties의 SPREADSHEET_ID와 UPLOAD_TOKEN은 기존 값을 유지한다. 코드나 로그에 token을 출력하지 않는다.
3. 상단 함수 목록에서 **`prepareTelemetryUploadIndex` → 실행**한다. 기존 Events를 한 번 읽어 숨김 인덱스를 만든다. 실행 로그의 **업로드 인덱스 준비 완료**, `complete:true`를 확인한다. `complete:false`면 같은 함수를 다시 실행해 이어 만든다. 한 번에 약 4분까지 처리하며 원본은 삭제하지 않는다. 준비 중 업로드가 잠금 때문에 재시도될 수 있다.
4. **배포 → 배포 관리 → 기존 웹 앱의 연필 버튼 → 버전에서 새 버전 → 배포**한다. 새 배포를 별도로 만들지 않아야 기존 `/exec` URL을 유지할 수 있다. 함수 실행이나 저장만으로는 운영 웹 앱의 `doPost`가 바뀌지 않는다.
5. 기존 게임 빌드를 실행해 밀린 기록을 재전송한다. 업로드 ACK와 Events·Sessions를 확인하고 Apps Script의 실행 내역에서 배포 전후 `doPost` 소요 시간을 비교한다. 이번 최적화는 0.3.0·0.4.0 전송 규약을 유지하므로 Unity 재빌드는 필요 없다. 0.2 빌드는 기존처럼 수신 대상이 아니다.

인덱스를 준비하지 않고 배포해도 업로드 요청이 나눠서 준비하지만, 완료 전에는 `upload_index_rebuilding`으로 재시도를 요청한다. 기존 게임의 재시도 대기 때문에 오래 걸릴 수 있으므로 3번을 먼저 실행한다. `_TelemetryUploadIndex`는 분석용 시트가 아니며 CSV·대시보드로 보내지 않는다.

스프레드시트를 새로고침하면 **전투 로그 → 업로드 인덱스 준비/이어 만들기**로 같은 작업을 할 수 있다. `upload_index_invalid`가 나오거나 Events를 수동으로 정렬·수정했다면 **업로드 인덱스 다시 만들기**를 실행한다. 이 기능은 파생 인덱스만 비우고 원본 Events에서 복구한다. 중단 뒤 이어갈 때는 다시 만들기 대신 준비/이어 만들기를 선택한다.

`upload_index_invalid_event_key`는 기존 Events의 키 형식이 손상됐다는 뜻이다. Apps Script 실행 로그의 `Invalid event_key at Events!A행번호`로 위치를 확인하고 원본 JSONL·백업과 비교한다. 인덱스만 재생성해도 손상된 키 자체가 복원되지는 않는다. `busy`는 다른 업로드나 인덱스 준비가 잠금을 사용 중인 상태이므로 게임의 자동 재시도를 기다린다.

로그 자체를 비울 때만 **전투 로그 → 현재 데이터 초기화…**를 사용한다. 확인 후 Events·Sessions·OnboardingSteps와 파생 인덱스가 함께 초기화된다. 이 데이터 삭제는 되돌릴 수 없으며 다른 사용자 시트, Script Properties, 로컬 JSONL은 건드리지 않는다. 미전송 로컬 로그는 이후 다시 올라올 수 있다. 이번 업로드 지연 개선에 데이터 초기화는 필요 없다.

Events와 OnboardingSteps는 헤더 순서가 다르면 `header_mismatch`로 거부한다. Sessions는 기존 열 순서를 보존하고 알려진 헤더 이름으로 매핑하며, 알려지지 않은 헤더와 중복 헤더를 거부한다. 전송 실패에서도 로컬 원본은 남고 재시도하며, ACK되지 않은 데이터를 성공으로 표시하지 않는다. 저장 한도 기본100MB와 Apps Script 처리량 때문에 장기 서비스 전체 수집에 대한 규모 검증은 별도 필요하다.

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

## 온보딩 0-1~0-5 단계 로그

- **Events**: 온보딩 입력 한 번의 최종 처리 결과는 `onboarding_attempt`, 목표 종료 결과는 `onboarding_step`이다. 두 목표가 있는 방어 단계는 `objective_index=0/1`로 구분한다.
- **stageId 대응**: 0→`0-1` 이동·발도 처치, 1→`0-2` 수리검 극한 회피, 2→`0-3` 근접 막기·쳐내기, 3→`0-4` 원거리 막기·쳐내기·일섬, 4→`0-5` 수리검 쳐내기·거합.
- **Sessions**: 온보딩 단계마다 도전 행 하나다. `run_id`로 5단계와 이후 Stage1 전투를 묶고, `exit_step_index`와 `exit_objective_index`로 마지막 진행 지점을 본다. 같은 PC에서 같은 온보딩을 재시작해도 `run_id`가 달라지므로 별도 실행이다.
- **OnboardingSteps / OnboardingSteps.csv**: 목표당 1행. 목표가 진행 중일 때는 아직 행이 없고, 완료·사망·이탈로 닫힐 때 로컬에 기록하고 즉시 업로드를 요청한다. `active_seconds`는 목표 입력이 열린 시간, `first_input_seconds`는 처음 추적된 이동/전투 입력까지 그 활성 시간이다. 이 입력은 기대 행동이거나 성공 판정일 필요가 없다. `attempts`는 필요한 행동 시도, `wrong_attempts`는 다른 전투 입력이다. `retry_count`는 해당 단계의 사망 후 재진입 횟수이고 `reason`은 completed/death/scene_unloaded/application_quit이다.
- 이동 목표는 이동을 멈춘 뒤 다시 시작할 때 새 시도로 센다. 공격·방어·회피 입력은 고유 InputId로 세고 누르고 있기·놓기·버퍼 후 Started는 중복 계산하지 않는다. 입력 결과는 Started/Rejected/BufferDropped/Buffered이며 실제 목표 성공은 step의 `reason=completed`로 확인한다.
- 사망 후 다시 진입하면 새 `session_id`와 `stage_attempt_id` 행이 생기지만 `run_id`는 유지된다. Intro에서 새로 시작하면 `run_id`와 단계별 재시도 횟수가 초기화된다.
- 이전 설정(`config_version=onboarding-2026-10-16`)의 `0-1`·`0-2`는 각각 여러 단계가 합쳐진 과거 구간이다. 행은 보존하며 새 설정(`onboarding-2026-10-17-stage-ids-5`)의 현재 5단계 데이터나 2026-10-16의 7단계 데이터와 합쳐 비율을 계산하지 않는다. 대시보드는 기본으로 과거 행을 제외하고, `이전 2구간 온보딩 로그 포함`을 켜면 이전 기록임을 표시한다. OnboardingSteps CSV만 있으면 구버전 설정을 판별할 수 없으므로 Sessions도 함께 불러온다.

## 전체 플레이에서 확인할 수 있는 것

| 구간 | 핵심 지표 | 데이터 위치 |
| --- | --- | --- |
| 0-1~0-5 온보딩 각 단계 | 해당 단계의 목표별 활성 시간, 첫 추적 입력까지 시간, 요구 행동 시도·오입력·결과, 사망 재시도, 이탈 목표 | OnboardingSteps, Events의 onboarding_attempt, Sessions |
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
| `received_kst` | Apps Script 요청 처리 시작 한국 시각. 잠금·인덱싱 전에 기록한다. 인덱스 개선 전 행은 기존 키 조회가 끝난 뒤의 시각이다. |
| `schema_version` | JSON 로그 자료 구조 버전(현재 `0.4.0`; `0.3.0`도 호환) |
| `build_version` | Unity `Application.version` |
| `config_version` | 기록 설정 버전. Editor 기록은 끝에 `-editor`가 붙음 |
| `session_id` | 씬 세션을 식별하는 임의 ID |
| `stage_id` | 현재 구간 ID: 온보딩 `0-1`~`0-5`; 이전 7단계 데이터는 `0-6`·`0-7`을 포함할 수 있음, 일반 전투 `1-1`, 보스전 `1-2` |
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
| `reaction_seconds` | 공격별 반응 기준 신호부터 연결 입력까지의 시간(초). 일반 패턴은 전조 시작, 2막 짐승 물기는 돌진 중 눈 섬광 시점부터 계산 |
| `input_to_contact_seconds` | 연결된 입력부터 실제 공격 접촉까지의 시간(초) |
| `reaction_association` | 반응 시간 연결 근거. 예: `resolved_contact`, `held_guard_intent` |
| `payload_json` | 위 열로 펼치지 않은 원본 사건 데이터 전체 |
| `run_id` | 온보딩 단계들과 이후 전투를 묶는 실행 ID |
| `step_index` | 온보딩 단계의 0부터 시작하는 인덱스(현재 0~4) |
| `objective_index` | 한 단계 안의 세부 목표 인덱스(0부터 시작) |
| `retry_count` | 해당 온보딩 단계의 사망 후 재진입 횟수 |
| `first_input_seconds` | 목표 입력 활성화 후 첫 추적 입력까지 걸린 시간(초) |
| `expected` | 온보딩 목표가 요구한 입력 종류인지 여부 |
| `defense_outcome` | 입력과 연결된 실제 방어 접촉 결과 |

수리검 패턴은 `DefenseShuriken`으로 집계한다. BambooThrower의 자동 발사는 전조 시점에 Begin을 기록하고 같은 `attack_id`로 실제 발사 Commit과 종료 결과를 연결한다. 온보딩 발사도 같은 패턴명으로 기록된다. 전조 뒤 발사 전에 취소된 경우 시작·취소 사건은 있지만 Commit이 없으므로 실제 발사 시도에는 포함되지 않는다. 이전 로그의 `BambooShuriken`은 변경 전 패턴명이며, 같은 공격의 시작/Commit과 접촉 결과가 서로 다른 이름으로 저장됐을 수 있으므로 이벤트 행을 패턴별 공격 횟수로 직접 합산하지 않는다. `attempts`는 Patterns.csv 또는 Sessions의 패턴 집계를 사용한다.

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
| `received_kst` | 해당 업로드의 Apps Script 요청 처리 시작 한국 시각. 목표가 발생한 게임 시각과 다를 수 있다. |
| `run_id` | 전체 온보딩 실행 ID |
| `session_id` | 목표가 진행된 씬 세션 ID |
| `stage_id` | 현재 단계 ID `0-1`~`0-5`; 과거 7단계 기록은 `0-6`·`0-7`을 포함할 수 있음 |
| `stage_attempt_id` | 목표가 속한 단계 도전 키 |
| `step_index` | 단계 인덱스(현재 0~4) |
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
- **온보딩 목표 완료**는 완료된 목표 행 ÷ 모든 목표 행이다. 플레이어가 온보딩을 끝낸 비율과 다르므로 마지막 단계 `0-5`의 완료/이탈과 `run_id`를 함께 확인한다.
- **피해 원인** 막대는 선택 데이터에서 원인별 누적 피해량을 비교한다. 막대 길이는 가장 큰 원인 대비 값이지 100% 누적 막대가 아니다. 피해 비중을 직접 비교하려면 `DamageShare`를 사용한다.

### 온보딩

- `active_seconds`가 길고 `attempts`가 적으면 안내를 이해하지 못해 머뭇거리는지 살핀다. `first_input_seconds`가 길면 목표 문구·입력 연결이 불명확할 가능성을 확인한다.
- `wrong_attempts`와 `rejected_inputs`가 많으면 조작 규칙이 혼동되거나 입력 시점·행동 조건을 학습하기 어렵다는 신호일 수 있다. 두 지표는 원인이 같지 않으므로 Events의 입력 종류와 거부 사유를 함께 본다.
- 높은 `attempts`가 곧 실패를 뜻하지 않는다. 필요한 행동을 반복해 성공할 수 있으므로 `reason`, 실제 방어 결과, `retry_count`를 함께 본다.
- 각 단계의 목표 인덱스 의미가 다르므로 여러 단계의 평균 시간만 비교하지 않는다. 같은 `stage_id`와 `objective_index`끼리 run별로 비교한다.

### 전투 판정·방어

- **적 공격 적중률**은 결과가 확정된 공격 중 `Hit` 비율이다. 공격 패턴별로 나누고 판정 표본 수도 같이 본다. 방어·회피 능력 전체를 한 숫자로 단정하지 않는다.
- **반응 시간**은 패턴별 기준 신호부터 연결 입력까지다. 대부분은 전조 시작이며, 2막 짐승 물기는 돌진 중 눈 섬광부터 센다. 작은 값은 빠른 반응을 뜻하지만 성공한 방어를 보장하지 않는다. `input_to_contact_seconds` 및 Hit/Dodge/Parry/Guard 결과와 함께 본다.
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
- build, 날짜, Editor 제외, 구 온보딩 포함 상태를 고정하고 회차 간 비교한다. 현재 5단계, 이전 7단계, 과거 2구간 stageId는 `config_version`으로 분리한다.
- 비율은 원시 이벤트나 합계 분자·분모로 다시 계산한다. 반올림된 구간별 비율을 단순 평균하지 않는다. 작은 표본에서는 결론 대신 다음 테스트에서 확인할 가설로 둔다.

## 전송량 정리와 현재 배포

- Stage1의 이동 표본 0.5→1초, 진행 요약 5→15초. 5분 동안 계속 이동했다면 정기 표본 약 600→300건, 요약 약 60→20건이다. 실제 건수는 플레이 행동에 따라 달라진다.
- 반복된 전체 Inspector 설정 덤프, 위치 표본의 체력과 Y 좌표, 중복 반응 후보·회피 종료·상태 변경 사건은 새 로그에서 보내지 않는다. 반응 시간은 실제 적 공격 접촉 결과에서 확인한다.
- Apps Script의 `setupSheets`는 기존 Events·Sessions 행을 보존하면서 새 열을 추가하고 OnboardingSteps를 생성한다. 웹 앱은 동일 URL의 **버전 7**, 대시보드는 소유자 전용 **Sites 버전 4**로 갱신됐다. `resetTelemetrySheets`는 세 탭 데이터를 비우므로 초기화가 필요할 때만 실행한다.
- CSV 전처리는 Events.csv, Sessions.csv, OnboardingSteps.csv, Patterns.csv, Heatmaps.csv, EnemyLifecycle.csv, BossPatterns.csv, Diagnostics.csv를 만든다. 기존 0.3.0 JSONL도 같은 변환기가 수신하며 신규 빌드 봉투는 0.4.0이다.
- 구간 ID가 Google Sheets에서 날짜로 바뀐 과거 Events·Sessions 행은 stage_attempt_id에서 복원했으며, 세 탭의 stage_id 열은 텍스트 형식이다. 

# D5 로컬 로그와 Google Sheets 자동 전송

## 현재 설치 상태

- 정식 `BambooDefense` 씬의 `GameSceneInitializer`에 `CombatTelemetryRecorder`를 연결한다.
- 설치/참조 복구 메뉴: `BlindBlade > 대숲 방어 > 로그 > D5 로컬 로그 연결`. 열린 정식 씬에서만 동작하고 기존 수치·참조를 재생성하지 않는다.
- 기본 로컬 기록 ON, 원격 전송은 로컬 설정의 `enabled`가 false여서 OFF다. 추가 SDK·서비스 계정·Python은 필요 없다.
- JSON은 스키마 `0.2.0`, 계약은 `0.2.0`이다. 이전 `0.1.0` 파일을 이 전처리기에 섞지 않는다.

## 저장 위치와 파일

Unity `Application.persistentDataPath/CombatTelemetry`를 사용한다. 현재 프로젝트 설정에서 Windows 경로는 다음과 같다. Company/Product Name을 변경하면 경로도 달라진다.

```text
C:\Users\JUNGLE\AppData\LocalLow\DefaultCompany\week06-blindblade\CombatTelemetry
  upload-config.json                     접속 설정, 원본 사건과 별도
  anonymous-player-id.txt                 이 PC 설치의 익명 ID, 실명·Steam ID 아님
  <sessionId>.json                        시작 사건·환경·설정 메타데이터
  <sessionId>.jsonl                       한 줄당 사건 한 건인 원본
  <sessionId>.jsonl.<destination>.cursor   해당 수신 URL에서 확인된 파일 바이트 위치
```

원본 파일은 전송 후에도 삭제하지 않는다. 커서·설정 파일은 분석 입력이 아니다. 같은 PC를 여러 참가자가 쓰면 익명 ID도 같으므로 참가자 ID로 해석하지 않는다. 참가자 교체 시 게임 종료 후 ID 파일을 따로 보관하고 제거하여 새 ID를 발급하거나 별도 참가자 매핑을 관리한다. 재도전은 새 SessionId로 구분한다.

## Google 쪽 준비 순서

1. 새 스프레드시트를 만든다. URL의 `/d/`와 `/edit` 사이가 ID다. 시트를 공개 공유하지 않는다.
2. `확장 프로그램 > Apps Script`를 열어 [Code.gs](google_apps_script/Code.gs) 전체를 붙여 넣는다.
3. `프로젝트 설정 > 스크립트 속성`에 `SPREADSHEET_ID`와 `UPLOAD_TOKEN`을 저장한다. token은 32자 이상의 임의 문자열을 사용한다.
4. 함수 목록에서 `setupSheets`를 선택해 실행하고 본인 계정의 Sheets 접근 권한을 승인한다. 빈 `Events`, `Sessions` 탭과 헤더가 생성된다. 기존 다른 탭은 삭제하지 않는다.
5. `배포 > 새 배포 > 웹 앱`: 실행 사용자 `나`, 액세스 권한 `모든 사용자`로 배포한다. 학교 Workspace 정책에서 막히면 개인 계정 또는 관리자 허용이 필요하다.
6. `/exec`로 끝나는 배포 URL을 복사한다. `/dev`는 편집자용이므로 게임에 사용하지 않는다.
7. 수신 코드를 수정한 뒤에는 `배포 관리 > 수정 > 새 버전`으로 갱신한다. 같은 배포 URL을 유지할 수 있다.

`setupSheets`는 헤더가 다르면 기존 데이터를 덮어쓰지 않고 오류를 낸다. 다른 스키마의 탭을 재사용하지 말고 새 스프레드시트로 시작한다.

## Unity/테스터 PC 설정

로컬 `upload-config.json`에 아래 값을 입력한다. 설치 메뉴가 비활성 기본 파일을 생성하며 기존 파일은 덮어쓰지 않는다. 다른 PC에서는 첫 플레이 후 같은 위치에 생긴 파일을 수정한다.

```json
{
  "enabled": true,
  "endpoint": "https://script.google.com/macros/s/실제배포ID/exec",
  "token": "Google의_UPLOAD_TOKEN과_동일한_32자이상_임의문자열"
}
```

- 실행 중에도 다음 전송 주기에 설정을 다시 읽는다. `enabled:false`는 이후 요청을 멈추지만 진행 중인 요청 취소나 이미 전송한 데이터 삭제를 의미하지 않는다.
- Inspector 위치: `GameSceneInitializer > CombatTelemetryRecorder`. Recording Enabled, Config Version, Buffer Limit, Flush Interval Seconds, Maximum Storage Megabytes, Sample Interval Seconds, Allow Automatic Upload, Upload Interval Seconds, Batch Event Limit, Request Timeout Seconds, Maximum Retry Seconds를 조절한다.
- 기본값: 저장 2초, 표본 5초, 전송 확인 주기 20초, 배치 100건/약192KiB, 타임아웃 30초, 재시도 최대300초, 메모리4096건, 원본100MiB.
- 미확인 기록이 많으면 성공 후 0.5초 간격으로 다음 배치를 보낸다. 수신 오류 시 지수 지연으로 재시도한다. 과제 참가자 수가 늘면 배치 간격·Apps Script 할당량을 관찰한다.
- 수신 URL별 커서를 사용한다. URL이 바뀌면 그 URL에 원본을 처음부터 보낸다. 동일 스프레드시트에 새 URL을 배포해도 사건 키로 중복을 제거한다.
- 원격 전송 설정을 `Assets`, Resources, StreamingAssets나 Git에 넣지 않는다. Google 계정 비밀번호·OAuth 토큰·서비스 계정 키는 게임에 필요 없다.
- 이 설정은 제한된 플레이테스트용이다. 클라이언트의 공유 token은 추출 가능하므로 Steam 공개 배포에서는 인증·요청 제한을 갖춘 수집 서버로 교체한다. 수집 내용을 참가자에게 알리고 필요한 기록만 받는다.

## 기록 가능한 사실

| 사건 | 의미와 한계 |
| --- | --- |
| session_started / session_ended | 세션·버전·환경·시계 기준·설정 스냅샷, 정상 종료 요약. 강제 종료면 종료 사건이 없을 수 있음 |
| state_changed / combat_started / run_ended | 게임 상태, 준비 종료, 치명타 프레임의 모든 사건 수신 후 결과. GameOver에서 구독을 즉시 끊지 않음 |
| resource_changed / damage_applied / player_died | HP 변경·실제 감소량·사망. 피해 합계는 damage_applied만 사용하며 방어 결과 피해를 다시 더하지 않음 |
| action_phase / katana_phase | 실제 행동 단계·결과·ActionId, 카타나 단계. 실제 입력 횟수와 동일하지 않음 |
| defense_resolved / dodge_changed / reveal_pulse | 공급자가 확정한 방어 결과·회피 단계·파동 요청과 제공된 ID |
| projectile_lifecycle / throw_telegraph | 기존 수리검 발사 ID와 실제 전조. 수명 종료·반사·원인 연결이 모두 제공된다고 가정하지 않음 |
| enemy_lifecycle | 스포너의 확정 처치 수. 현재 이벤트에 적/공격 ID가 없으므로 생성하지 않음 |
| threat_presentation_changed / footstep | C 표시 플래그 전환과 원본 걸음당1회. HUD가 실제로 눈에 보였거나 인지됐다는 증거가 아님 |
| progress_sample / time_scale_changed | 5초 간격 진행·위치·HP·누적 결과, 시간 배율 변경 |

`InputResolved`, `StaminaChanged`, `EmpowerChanged`도 구독하지만 현재 실제 입력/스태미너/강화 공급자가 없으면 해당 기록은 생성되지 않는다. 입력 시간 보정·실제 입력 시도 수가 없으므로 입력 성공률, early/late 판정을 산출하지 않는다. `parryCount/defenseCount`는 접촉 결과 중 패링 비율이며 패링 시도 성공률이 아니다.

현재 PlayerCombatEvents는 `BlindBlade.Combat.Contracts.DefenseResolution`을 사용한다. Recorder는 공통 계약 별칭을 명시해 소비한다. 공통 방어 결과에 DodgeId가 없어 defense_resolved의 correlation.dodgeId는 0이며, 별도 dodge_changed의 원본 DodgeId는 보존한다. 시각이나 현재 회피 상태로 빠진 연결을 추정하지 않는다. 공통 계약과 공급자 코드는 수정하지 않았다.

`runRealtimeSeconds`는 세션 시작부터 GameOver까지 실제 시간이고 준비·플레이어 일시정지·포커스 이탈이 포함된다. GameOver 화면 대기는 제외한다. `combatRealtimeSeconds`는 준비 종료 뒤 실제 시간이며, `survivalGameSeconds`는 기존 director의 배율 적용 시간이다. `activeUnscaledSeconds`는 Runtime의 명시적 frozen만 제외한다. 5~10분 달성 기준에서 어떤 시간을 쓸지 팀이 고정하고 대기/재도전 포함 총 체험 시간과 구별한다.

## 전처리 / CSV

Windows PowerShell 5.1 이상에서 플레이를 마친 원본의 사본을 대상으로 실행한다. 테스트 코드는 포함하지 않는다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\combat_telemetry\Export-Telemetry.ps1 -LogDirectory "C:\Users\JUNGLE\AppData\LocalLow\DefaultCompany\week06-blindblade\CombatTelemetry" -OutputDirectory ".\UserSettings\CombatTelemetryAnalysis"
```

- `events.csv`: 사건 평탄화, 기존 ID·시계·payload 보존.
- `sessions.csv`: 요약과 정상 종료 여부. 종료 사건 없는 세션을 클리어/사망/정상 플레이시간으로 단정하지 않는다.
- `diagnostics.csv`: 스키마 미지원, 파싱 실패/잘린 끝줄, 중복 사건 키, 순번 누락·역전, 종료 누락.
- `explicit-links.csv`: 공급자의 InputId가 있는 사건만 보존한다. 후보 매칭이나 성공 판정을 생성하지 않는다.
- `analysis-manifest.json`: 분석 버전·시각·입력 SHA256·건수.

Google Sheets에는 원본 업로드 기준 `Events`와 최신 요약 `Sessions`가 쌓인다. CSV와 같은 지표 정의로 피벗/차트를 만든다. 전처리 도구는 원본을 수정하지 않는다. 빈 자료의 CSV가 비어 있을 수 있다. 전처리는 분석 분모를 대신 결정하지 않는다.

## 오류·운영

- 인터넷/서버 오류: 원본 보존, 서버의 `ok`, 배치 ID와 모든 사건 키 확인 뒤에만 커서 이동. HTTP200만으로 성공 처리하지 않는다. Apps Script JSON 응답의 리다이렉트를 허용한다.
- 서버는 script lock 안에서 Events의 사건 키로 중복 확인 후 저장한다. 확인 응답 유실이나 커서 저장 실패에도 재전송으로 같은 행을 추가하지 않는다. Sessions 집계는 합계 덧셈이 아니라 최신 요약으로 갱신한다.
- 메모리/디스크 한도: 전투는 계속되고 누락 수·LastError를 남긴다. 서비스 누락 수는 여러 재도전의 누적값이다. 공간이 없어 저장 자체가 실패하면 누락 진단까지 파일에 남는다고 보장할 수 없다.
- 백그라운드 저장은 Unity 객체를 사용하지 않는다. 매 프레임 동기 파일 쓰기를 하지 않는다. 시작 시 익명 ID·설정 파일 접근은 작은 동기 작업이다.
- 종료 시 네트워크를 기다리지 않는다. 남은 저장은 최대 약3초 기다리며, 강제 종료면 미저장 메모리·끝줄이 손실될 수 있다. 다음 실행에서 디스크에 있는 미확인 기록을 재전송한다.
- 손상/잘린 줄은 자동 삭제·확인하지 않고 해당 파일의 나머지 전송을 보류한다. 앞의 정상 줄과 다른 정상 세션 파일은 전송할 수 있다. 원본을 백업하고 전처리 보고서를 확인한 뒤 해당 파일을 전송 폴더 밖으로 보관한다. 정상 기록은 CSV로도 구할 수 있다.
- 100MiB 한도는 원본 JSONL 기준이다. 자동 삭제하지 않는다. 테스팅 회차가 끝나면 원본·메타데이터·커서를 함께 백업하고 게임 종료 상태에서 정리한다.
- `CombatTelemetryService.Active`의 LogDirectory / LastError / DroppedEvents / AcknowledgedEvents / BufferedEvents가 개발용 상태다. 접속 token·본문을 Console에 출력하지 않는다.
- Recorder 비활성화는 해당 관찰 세션을 닫는다. 새 세션 기록은 씬 재시작으로 시작한다. 영속 서비스의 미전송 원본 전송은 별개로 계속된다.

## 연결 후 수동 확인 항목 (미실행)

1. 짧은 정상 플레이 후 로컬 JSONL에 시작·피격·최종 판정·run_ended가 순서대로 있는지 확인한다.
2. 재도전 후 새 SessionId와 같은 익명 설치 ID인지 확인한다.
3. Sheets Events의 사건 키가 유일하고 Sessions의 피해 합계가 damage_applied 합계와 맞는지 확인한다.
4. 인터넷을 끊고 플레이한 뒤 복구/재실행하여 전송·중복 제거를 확인한다.
5. 잘못된 token, HTML 응답, 타임아웃, 저장 경로 접근 실패에서 게임이 지속되고 실패가 진단되는지 확인한다.
6. 실제 전송/전처리 자료로 5~10분 지표·손상 로그·누락 분모를 확인한다.

현재 C# 컴파일·씬 연결까지 수행했다. 실제 Play 로그·원격 Sheets·CSV 실행 결과 검증은 아직 수행하지 않았다. Google URL/token이 제공되지 않은 상태에서 원격 성공으로 표시하지 않는다.

## 참고

- [Apps Script 웹 앱 배포·doPost](https://developers.google.com/apps-script/guides/web)
- [Content Service 리다이렉트](https://developers.google.com/apps-script/guides/content)
- [Apps Script LockService](https://developers.google.com/apps-script/reference/lock/lock-service)
- [Apps Script 할당량](https://developers.google.com/apps-script/guides/services/quotas)

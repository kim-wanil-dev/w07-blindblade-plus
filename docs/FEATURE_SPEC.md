# Feature Specifications

## 계획 문서

- [특수 스킬·일섬게이지·거합게이지 리메이크 계획](plans/특수스킬_일섬_거합_리메이크_계획.md): float 3칸 일섬게이지·Shift 즉시 시전/첫 0.2초 집중과 독립 100% 거합게이지·Q 자동 차징 0.75초를 구현한다. 거합은 패링당 정확히 1/3, 보스 일반 공격 유효 적중당 3%p를 적립한다. M1 자원 모델, M2 입력·전투·보상 연결, M3 HUD·검 구슬 표시를 완료했고 M4 회귀 검증이 남아 있다. 온보딩 수정은 제외한다.
- [핵심 전투 로그 개편 실행 계획](plans/핵심_전투로그_개편_실행계획.md): 구간별 도전 세션·입력별 방어 결과·극한 회피·적 공격 결과·피격 반경 내 적대 개체·스킬·사망·플레이어 이탈을 전체요약에 통합한다. 선택 구간 전체 합계와 공격 패턴별 결과를 기본 표시하고 세션·플레이어별 상세는 선택적으로 연다. Sites v26과 Apps Script 웹 앱 v17 운영 배포를 완료했으며 실제 시트 저장/ACK는 미확인이다.
- [전투 텔레메트리 분석 대시보드](features/전투_텔레메트리_분석_대시보드.md): Sites v26과 기존 공개 URL·접근 범위를 유지한다. 구간별 합산 지표·공격 패턴별 결과·플레이어 누적 시간 및 이탈 통계를 운영 배포하고, 세션·플레이어별 상세를 접었다. Code.gs core API에는 익명 ID와 도전 이탈·최종 클리어 필드가 포함되며 웹 앱 v17 배포는 완료, 실제 시트 저장/ACK는 미확인이다.
- [전투 로그 대시보드 정확성 검토 및 수정 계획](plans/전투_로그_대시보드_정확성_검토_및_수정계획.md): 빌드 0.2.0 이상 범위의 집계와 스냅샷 계약을 수정했다. Sites v15는 Google 리다이렉트와 페이지 실패 복구를 처리한다. Chrome에서 Events 61,631행·Sessions 362행·OnboardingSteps 218행의 전체 자동 수신 및 빌드 복수 선택에 따른 패턴/반응 표 갱신을 확인했다.
- [전투 텔레메트리 0.1.3 업데이트 계획](plans/전투_텔레메트리_0.1.3_업데이트_계획.md): 0.1.2 대비 2-1·2-2, 일반 Beast, Twin Boss 부모 패턴 및 개별 공격, Google Sheets와 분석 사이트 확장. S0–S7, Apps Script와 Sites v10 배포를 확인했다. Apps Script 범위 읽기 최적화 배포 후 Events 54,087건·Sessions 543건·OnboardingSteps 335건이 자동 연결됐고, 0.1.3 빌드와 2-1·2-2 세션 집계를 확인했다.

## 관리 규칙

- 기능별 명세는 `docs/features/기능명.md`에 작성한다.
- 모든 명세는 아래 공통 양식을 사용한다.
- 작업 전 관련 명세를 확인하고, 없으면 새로 작성하여 기능 목록에 등록한다.
- 관련 코드의 파일 경로, 클래스, 주요 메서드와 역할을 기록한다.
- 코드 변경 후 관련 명세를 갱신한다.
- 기존 기능 또는 다른 작업자의 작업과 중복되거나 충돌하면 수정 전에 사용자 승인을 요청한다.
- 확인하지 못한 내용은 `미확인`으로 표시하고, 관련 없는 명세는 수정하지 않는다.

## 읽기 시작점

- 정식 게임은 **Intro(빌드 0) 시작 버튼 → 온보딩 5단계 → BambooStage1 → RainyTwinBoss → 게임 클리어**다. 이어 하기는 저장된 진행에 따라 1막 웨이브·보스전 또는 2막 RainyTwinBoss를 연다. 온보딩 씬은 BambooDefense를 복제해 사용하며, 직접 확인이 필요한 기존 방어 동작은 [대숲 방어](features/대숲_방어.md)와 [플레이어 시스템 인수인계](features/플레이어_시스템_인수인계.md)부터 읽는다.
- 2026-10-14 BambooDefense 리팩토링 S7에서 기존 모드 씬(Build의 Stage·Boss, Map, LevelDesign_HP, TestScene, TutoScene, EndingScene)을 삭제했다. 각 명세에 남은 해당 씬 이름은 그 시점의 검증 기록이며 현재 씬이 아니다. 진행 기록과 복구 방법은 [진행 문서](plans/BambooDefense_refactoring_progress.md)의 S7 절을 따른다.

## 게임 내 표시 용어

| 기존 표현 | 현재 게임 화면과 문서의 표현 |
| --- | --- |
| 플레이어의 돌진 공격 | 발도 |
| 수비·가드 | 막기 |
| 패리·패링·퍼펙트 패링 | 쳐내기 |
| 플레이어의 돌진 이동 | 회피 |
| 쳐내기 후 검기 발사·강화 공격 | 일섬 |
| 궁극기 | 거합 |

- 위 표현은 플레이어에게 보여 주는 기술과 현행 설명에 적용한다. 적의 돌진 패턴, 일반 검기 투사체, 과거 검증 기록 및 C# 식별자·이벤트 이름은 원래 의미와 추적 가능성을 유지한다.

## 기능 목록

### 현재 정식 게임에서 쓰는 기능

| 기능 | 명세 파일 |
| --- | --- |
| 일섬·거합 독립 게이지와 전용 입력 | [명세](features/특수스킬_게이지.md) |
| 온보딩 | [명세](features/온보딩.md) |
| 공용 TMP 폰트 | [명세](features/공용_TMP_폰트.md) |
| 대숲 방어 | [명세](features/대숲_방어.md) |
| 대숲 스테이지 1 | [명세](features/대숲_스테이지1.md) |
| 비 내리는 스테이지 2 | [명세](features/비_스테이지2.md) |
| 인트로 메뉴 | [명세](features/인트로_메뉴.md) |
| 게임오버 재도전 | [명세](features/게임오버_재도전.md) |
| 게임 클리어 화면 | [명세](features/게임_클리어_화면.md) |
| 매니저 싱글턴 | [명세](features/매니저_싱글턴.md) |
| 게임 씬 공통 구성 | [명세](features/게임_씬_공통_구성.md) |
| 플레이어 시스템 | [명세](features/플레이어_시스템.md) |
| 플레이어 시스템 인수인계(읽기 안내) | [명세](features/플레이어_시스템_인수인계.md) |
| 플레이어 피격 및 체력 | [명세](features/플레이어_피격_체력.md) |
| 전투 HUD | [명세](features/전투_HUD.md) |
| 전투 로컬 로그·Google Sheets 자동 전송 | [명세](features/전투_로컬로그_자동전송.md) |
| 전투 텔레메트리 분석 대시보드 | [명세](features/전투_텔레메트리_분석_대시보드.md) |
| 플레이어 걸음 보행 | [명세](features/플레이어_걸음_보행.md) |
| 카타나 전투 V2 | [명세](features/카타나_전투_V2.md) |
| 전투 판정 계약 | [명세](features/전투_판정_계약.md) |
| 쳐내기 토큰(일섬·거합 차징·토큰 실 구체) | [명세](features/퍼펙트_패링_토큰.md) |
| 칼 구슬 토큰 표시(핸드가드에 매단 구슬 세 개) | [명세](features/칼_구슬_토큰_표시.md) |
| 쳐내기 모션(막기에서 이어지는 흘리기·튕겨내기) | [명세](features/퍼펙트_패링_모션.md) |
| 대나무 베기(일섬·거합, 숲 뒤쪽 확장) | [명세](features/대나무_베기.md) |
| 검 상시 표시와 휘두르기 파동 | [명세](features/검_상시_표시와_휘두르기_파동.md) |
| 발도 대상 실 표시(칼집 먹실·발걸음 파동) | [명세](features/돌진_대상_실_표시.md) |
| 1인칭 근접 교전 | [명세](features/1인칭_근접_교전.md) |
| 카타나 애니메이션 좌표계 보정 | [명세](features/카타나_애니메이션_좌표계_보정.md) |
| 패드 입력 및 장치별 안내 | [명세](features/패드_입력_안내.md) |
| 적 추적 | [명세](features/적_추적.md) |
| 적 공격 패턴 | [명세](features/적_공격_패턴.md) |
| 근접 공격 차례 제한(일반 웨이브 순차 공격) | [명세](features/근접_공격_차례_제한.md) |
| 일반 Beast 적 | [명세](features/DefenseBeast.md) |
| 적 전투 및 체력 | [명세](features/적_전투_체력.md) |
| 감각 표현 | [명세](features/감각_표현.md) |
| 발자국 시스템 | [감각 표현](features/감각_표현.md)에서 관리 (FootstepEmitter·발자국·파동) |
| 화면 공간 윤곽선 | [명세](features/화면_공간_윤곽선.md) |
| 위협 표시 판정 | [명세](features/위협_표시_판정.md) |
| 이펙트 시스템 | [명세](features/이펙트_시스템.md) |
| FMOD 사운드 매니저 | [명세](features/FMOD_사운드_매니저.md) |
| 사운드 중앙 편집 | [명세](features/사운드_중앙_편집.md) |
| FMOD 에디터 초기화 | [명세](features/FMOD_에디터_초기화.md) |
| FMOD 사운드 수동 검증 | [명세](features/FMOD_사운드_검증.md) |

### 개발 중인 기능과 공통 계약

| 기능 | 명세 파일 | 상태 |
| --- | --- | --- |
| 전투·인지·HUD 공통 계약 | [명세](features/전투_인지_HUD_공통_계약.md) | v0.2.0 수리검·HP·발소리 HUD 연결, 스태미너 HUD 공급자·로그·팀 검토 대기 |
| 발소리 후보 | [명세](features/발소리_후보.md) | 적별 최신 후보·청각 범위·게임 시간 수명 구현, C 전용 핵심 확인 완료·정식 연결 대기 |
| 보상 인지 파동 | [명세](features/보상_인지_파동.md) | C/D 공개·제거 및 회피 시간 핵심 Play 확인 완료, 커밋 인계·정식 연결 대기 |
| 전투·인지·HUD 공통 계약 | [명세](features/전투_인지_HUD_공통_계약.md) | v0.2.0 HUD 연결, D5 로컬 저장/전송 코드 구현·실제 수집 확인 대기, 스태미너 공급자·팀 검토 대기 |
| RainyStage 비 연출 | [명세](features/RainyStage_비_연출.md) | 시험 씬 구현, 실제 Play 연출·성능 확인 대기 |
| 비 내리는 쌍둥이 보스전 | [명세](features/비_쌍둥이_보스전.md) | RainyTwinBoss 씬에 50명 웨이브·비 사운드·쌍둥이 보스를 연결. 보스 전투 패턴·클리어·재도전 Play 확인 대기 |

### 기존 모드 기록 (씬은 S7에서 삭제, 일부 코드·Resources 프리팹은 남음)

| 기능 | 명세 파일 | 남은 것 |
| --- | --- | --- |
| 독백 UI | [명세](features/독백_UI.md) | UIManager의 MonologueUI, ExampleMonologue |
| 사운드 중심 보스전 | [명세](features/사운드_중심_보스전.md) | BossController 등 보스 코드, Resources/Enemies 보스 프리팹 |
| 쌍둥이 보스 | [명세](features/쌍둥이_보스.md) | TwinBossController, Resources/Enemies/TwinBoss |
| 튜토리얼 안내 | [명세](features/튜토리얼_안내.md) | TutorialGuide 코드 |
| 미닫이문 내비게이션 | [명세](features/미닫이문_내비게이션.md) | 문 관련 코드·프리팹 |
| 메인맵 | [명세](features/메인맵.md) | 없음(씬 삭제) |
| 직선 복도 씬 | [명세](features/직선_복도_씬.md) | CorridorRoundController(GameSceneInitializer 옵션) |
| 스테이지 이동 | [명세](features/스테이지_이동.md) | 없음(StageDoor·SceneTransitionFade 삭제) |
| 보스 사망 씬 전환 | [명세](features/보스_사망_씬_전환.md) | 없음(BossSceneExit 삭제) |
| 엔딩 연출 | [명세](features/엔딩_연출.md) | 없음(EndingScene·EndingSceneController 삭제) |
| 게임 공통 구조 검증 씬 | [명세](features/게임_공통_구조_검증_씬.md) | 없음(Stairwell 삭제) |

## 공통 양식

### 기능 이름

- 기능 설명:
- 동작 조건 및 처리 흐름:
- 구현 상태:

#### 관련 코드

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |

#### 연결 및 협업

- 연결된 기능:
- 공유하는 코드 및 상태:
- 중복 또는 충돌 사항:
- 승인 필요 사항 및 결과:


### 2026-10-17 Settings and gameplay pause

| Feature | Specification | Current behavior |
| --- | --- | --- |
| Intro settings popup and gameplay pause | [Feature specification](features/intro_settings_and_gameplay_pause.md) | Persistent settings UI, volume preview, mouse sensitivity, and Escape/Start pause including active effect sounds |

### 2026-10-21 Combat telemetry 0.5 implementation

| Feature | Specification | Current behavior |
| --- | --- | --- |
| Combat attempt telemetry and overview contract | [Execution plan](plans/핵심_전투로그_개편_실행계획.md), [Local telemetry](features/전투_로컬로그_자동전송.md), [Dashboard](features/전투_텔레메트리_분석_대시보드.md) | Unity producer, v0.5 schema, Apps Script core Sessions fields, CSV mapping, and local overview implementation are complete. A successful PerfectDodge links its triggering `AttackId` to that attack's final outcome, with `dodgeId` retained in event correlation; pending attack contacts and ends wait for an active dodge to settle. The overview also derives the linked result from existing `dodge_changed` events for previously collected data without rewriting stored rows. Manual Empower alone increments the manual skill metric; Buffered→Started refreshes the current input link without increasing unique attempts. Death and clear events update Sessions status independently of their summaries; pause requests an immediate nonblocking flush, local write failures retain events for retry, and a later launch reconciles open JSONL sessions as `unclean_exit`. Apps Script v17 was deployed to the existing web app URL with owner execution and all-user access retained; actual Sheets ACK, Player build, and force-termination scenarios remain unverified. |


#### 2026-10-21 telemetry follow-up review

- Final review fixes make onboarding death durable by emitting `player_died` and closing/flushing a `stage_ended(death)` summary in the death callback. Repeated death notifications do not duplicate the result.
- Apps Script Sessions tracks `last_summary_sequence`, `last_exit_sequence`, `last_completion_sequence`, and `status_sequence` independently. Status precedence is death/final_clear/cleared, then scene_unloaded/application_quit, then in_progress/started; event sequence breaks ties. Delayed death summaries can merge after quit facts while preserving maximum deaths; `last_event_sequence` remains monotonic.
- The local CSV exporter retains terminal events until all input files have been read, then merges the newest quit and completion facts into the newest stage summary without changing its summary sequence.
- Combat telemetry upload retry delay is fixed at 20 seconds for retry-path errors; retry eligibility remains code-defined rather than an Inspector setting.
- Final bounded validation after the status-ordering correction: Unity C# build passed with 0 errors and 0 warnings; `node --check`, Apps Script syntax parsing, and v0.5 schema JSON parsing passed. Synthetic Apps Script and JSONL-to-CSV checks covered summary 29, quit 30, completion 32, status precedence/tie-breaking, monotonic deaths, timestamps, and CSV header alignment. No test files were added.

#### 2026-10-22 end-of-session reliability review

- `CombatTelemetryRecorder.HandleDeath` requests a nonblocking flush for `player_died`; the normal LateUpdate still closes the final stage summary after contact callbacks. The Apps Script receiver also updates Sessions directly from `player_died` and `game_completed`, so a terminal fact survives a missing or delayed summary.
- In-game pause transitions request a flush in both combat and onboarding recorders. A Player quit waits asynchronously for local drain and exact ACK; Editor Play exit closes recorders and persists the local buffer before teardown.
- `TelemetryDiskStore` recovers unclosed supported JSONL sessions at the next launch, repairs only an incomplete tail, and appends `session_ended(unclean_exit)`. Failed local appends are requeued in order and retried; terminal events pass the normal buffer cap.
- Apps Script and local CSV status reducers now merge every `session_ended` reason and preserve death/final-clear precedence. `last_event_sequence` includes the selected status event.
- Verification: `dotnet build Assembly-CSharp.csproj --no-restore --verbosity quiet` passed with 0 errors and 4 warnings; `node --check Export-Telemetry.cjs` and Apps Script syntax parsing passed. Apps Script web app version 17 was deployed on 2026-10-22 to the existing URL; real Sheets ACK and Unity Player/Editor force-termination scenarios remain unverified. An unclean exit cannot be sent while the process is already killed and is reconciled only on a later launch.

#### 2026-10-22 Events·Sessions reconciliation and exit evidence

- The latest live dashboard snapshot showed three Events/Sessions attack-result count differences. All three corresponding Sessions rows were `in_progress`; one observed example was Events 49 versus Sessions 41 with one pending attack. No completed/closed mismatch was confirmed, so the snapshot points to Sessions summaries lagging Events rather than proving a lost final upload. The dashboard now labels open-attempt differences as provisional and counts a mismatch as final only after a terminal event or retry linkage closes the attempt.
- The overview now labels `application_quit` as a normal-exit callback, `unclean_exit` as next-launch recovery with unknown actual exit time, `scene_unloaded` as scene exit only, and `in_progress` as having no exit event. A Sessions exit timestamp alone does not prove a normal app quit because `scene_unloaded` also has an event time. Player completion/exit ordering uses `event_sequence` only when both events share the same attempt and session, then linked retry ancestry, then distinct timestamps. This avoids comparing recorder sequence values across attempts; unresolved legacy ordering is excluded from inferred dropout and location counts with an explicit evidence note.
- The persistent service supplies a missing `application_quit` event if gameplay recorder components were already unloaded. A durable process marker retains the last stage attempt through `scene_unloaded`; next launch appends `unclean_exit` even when the JSONL already has a `scene_unloaded` record. Stage start requests immediate nonblocking persistence. Recovery still depends on the start event and marker having reached local disk and a subsequent launch occurring; actual forced-exit time and server ACK cannot be guaranteed.
- Other attack outcomes now show totals and reason breakdowns in overall and per-pattern summaries. Schema diagnostics accept v0.5.0, zero-valued grouping keys remain zero, `repairStageIds()` preserves existing GUID-era stage IDs, CSV snapshots show their file modification time and stale-snapshot status, and awareness labels refer to the configured radius.
- These are local source changes. Sites and Apps Script were not deployed as part of this review.

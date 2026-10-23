### FMOD 사운드 수동 검증

- 2026-10-10: Event Browser 초기화 오류의 실패 처리·해제·재시도와 실제 계단 BGM을 검증했다. [FMOD 에디터 초기화](FMOD_에디터_초기화.md) 참고. 실패한 시스템에서는 매 프레임 생성을 반복하지 않으며 필요 시 FMOD/Retry Editor Audio를 사용한다.

- 기능 설명: 전용 검증 스크립트 없이 실제 게임의 행동을 통해 사운드 재생, 방향, 거리, 반복, 음량 및 정리를 확인한다.
- 동작 조건 및 처리 흐름: 실제 게임 코드가 Managers.Sound를 호출하고 청취 위치에 StudioListener가 있어야 한다. 아래 절차를 실행해 통과/실패/미확인을 기록한다.
- 구현 상태: 전용 코드와 씬 제거 후 사용할 검증 절차를 문서로 보존했다. Stairwell, StraightCorridor, TCorridor의 실제 게임 호출부와 청취 컴포넌트 연결 및 재생 상태는 과거 검증 기록이다. 이 씬들은 현재 프로젝트에서 삭제됐으며 아래의 입력 절차도 현재 입력 체계와 맞지 않는다.

#### 관련 코드

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| Assets/Scripts/Managers/SoundManager.cs | Play, PlayAttached, PlayAttachedWithLabel, PlayBgm, StopBgm, StopFor, StopEffects | 실제 게임에서 호출하는 재생 및 정리 API |
| Assets/Scripts/Managers/Managers.cs | Update, ClearServices | 기존 서비스 수명 정리 |
| Assets/Scripts/Enums/SoundId.cs | SoundId | 등록한 사운드 식별자 |
| Assets/Scripts/SoundSystem/SoundLibrary.cs | SoundLibrary | 사운드 등록 규칙 |
| Assets/Resources/Sound/SoundLibrary.asset | SoundLibrary | 실제 이벤트 및 Bus 연결 |

#### 연결 및 협업

- 연결된 기능: FMOD 사운드 매니저, 실제 게임의 이동/공격/BGM 실행 시점.
- 공유하는 코드 및 상태: [매니저 명세와 등록 방법](FMOD_사운드_매니저.md)을 사용한다. 별도 검증용 스크립트나 씬을 다시 만들지 않는다.
- 중복 또는 충돌 사항: 검증을 위해 공통 Managers를 임의로 수정하지 않는다. 입력과 애니메이션의 실제 사운드 호출 시점은 해당 기능 담당자와 합의한다.
- 승인 필요 사항 및 결과: 사용자가 검증 절차의 문서 보존과 잔재 확인을 요청했다. 이번 작업은 문서만 변경하며 기존 코드/씬/설정 에셋/Bank는 유지한다.

#### 실행 전 준비

1. 실제 플레이어와 소리를 내는 캐릭터가 있는 게임 씬을 연다. 아직 없다면 실제 행동 연결 이후 검증한다.
2. 3D 소리의 청취 위치와 방향에 FMOD StudioListener가 있는지 확인한다. 1인칭 게임은 일반적으로 실제 플레이어 카메라를 사용한다.
3. SoundLibrary.asset의 Id, Type, Event 및 Bus 경로를 확인한다. Studio 변경 후에는 Bank를 재빌드하고 Unity Event Browser의 갱신을 확인한다.
4. 게임 코드의 발 접촉 시점에서 PlayAttached를 한 번 호출한다. 이동 정지 후에는 다음 발걸음 호출을 중단한다. 매니저 자체는 발걸음 시점이나 일반 효과음의 중복 호출을 판정하지 않는다.
5. BGM을 재생하고 정지하는 실제 게임 흐름 또는 UI가 연결되어 있는지 확인한다.
6. 헤드폰을 사용하고, 발소리 방향 검증 중에는 BGM을 정지해 소리 하나씩 비교한다. Play Mode에 진입하고 Console 오류를 확인한다.

#### 검증 절차와 통과 기준

| 항목 | 실제 게임에서 수행할 절차 | 통과 기준 |
| --- | --- | --- |
| 등록과 재생 | 등록한 행동을 실행하고 반환값/Console을 확인한다. | 재생이 성공하고 등록 누락, 종류 불일치, Bank/Bus 오류가 없다. |
| 발걸음 한 번 | 한 번의 발 접촉 행동을 실행한다. | 한 번의 호출에 한 발소리가 들린다. 원본에 여러 발소리가 있으면 Studio에서 편집한다. |
| 이동 정지와 공격 | 걷다가 멈추고 공격 준비 행동을 실행한다. | 이미 시작한 발소리는 자연 종료하고 다음 발걸음은 발생하지 않는다. 공격음 시작 때문에 발소리를 억지로 끊지 않는다. |
| 정면/좌/우 | 소리를 내는 캐릭터를 청취자 정면, 왼쪽, 오른쪽에 배치해 같은 행동을 실행한다. | 소리 방향이 실제 배치와 맞고, 좌우를 구분할 수 있다. 정면 인지 가능성은 직접 청취로 판단한다. |
| 거리 감쇠 | 청취자로부터 가까운 거리와 먼 거리에서 동일한 발소리를 발생시킨다. | 현재 Studio 거리 및 감쇠 곡선과 맞는 음량 차이가 들린다. 거리 비교는 Listener와 발음체의 실제 3D 위치를 기준으로 한다. |
| 발음체 이동 | 캐릭터가 움직이는 동안 PlayAttached로 발소리를 호출한다. | 이동 경로에 따라 소리 위치가 변한다. Play(position)은 재생 당시 위치에 고정된다. |
| 청취자 회전 | 발음체를 고정하고 플레이어 카메라를 회전한다. | 현재 청취 방향에 따라 소리의 좌우 관계가 바뀐다. |
| BGM 반복 | 대상 음악의 끝 지점까지 재생한다. 기존 Ambience는 27.552초, 계단 StairWell은 145.894초 구간이다. | 루프를 의도한 이벤트는 Studio의 Loop Region으로 반복되어야 한다. 현재 StairWell은 단발이며 반복 통과로 기록하지 않는다. |
| BGM 중복 | 같은 BGM을 반복해서 재생 요청한다. | 음악이 겹치거나 음량이 누적되지 않는다. 필요하면 FMOD Profiler에서 인스턴스를 확인한다. |
| BGM 정지 | StopBgm을 실행하고 다시 재생한다. | 정지 후 재생이 가능하다. 페이드 길이는 Studio Release 설정과 맞으며, 즉시 정지는 false 인자로 확인한다. |
| 음량 | BgmVolume/EffectVolume을 1, 0.5, 0으로 바꾸고 다시 1로 복구한다. | 해당 Bus의 음량이 바뀌고 0에서 무음이 된다. 한 호출의 volume 배율은 공통 옵션 음량과 별개다. |
| 오브젝트별 정지 | 서로 다른 두 오브젝트가 효과음을 재생할 때 한 소유자에만 StopFor를 호출한다. | 해당 소유자만 정지하고 다른 오브젝트의 소리는 유지된다. |
| 비활성화/파괴 | 소유자 있는 재생을 시작한 뒤 그 오브젝트를 비활성화 또는 파괴한다. | 다음 매니저 Update에서 재생이 정리되고 Console 오류가 없다. 활성화 이후 다시 재생할 수 있다. |
| 전역 정리 | StopEffects 또는 Clear를 실행한다. | StopEffects는 BGM을 유지하며 효과음만 정지한다. Clear는 둘 다 정지하고 이후 다시 재생할 수 있다. |
| 장시간 플레이 | 이동/공격/음악 전환을 반복한 뒤 움직임을 멈춘다. | 종료된 재생이 누적되지 않고 불필요한 재생이 남지 않는다. 실제 메모리/CPU는 필요할 때 Profiler로 관찰한다. |

음량 0.5는 선형 배율이며 사람이 느끼는 소리 크기의 절반을 보장하지 않는다. 스테레오 방향 인지는 청취 검증이 필요하고, Console 오류가 없다는 사실만으로 공간음향 검증을 통과 처리하지 않는다.

#### 결과 기록 양식

게임 씬 또는 사운드 설정을 변경한 뒤 아래 항목을 기록한다. 실행하지 못한 항목은 미확인으로 남긴다.

| 항목 | 기록할 내용 |
| --- | --- |
| 실행 환경 | 날짜, Unity 버전, 게임 씬 경로, 사용한 출력 장치 |
| 대상 | SoundId, FMOD Event, 변경한 Bank |
| 절차별 결과 | 위 검증 항목별 통과/실패/미확인 |
| 문제 | 재현 행동, 실제 결과, 기대 결과, Console 메시지 |
| 후속 작업 | Studio 수정 또는 게임 호출 시점 수정의 담당자 |

#### 이전 잔재 및 통합 상태 확인 기록

아래 항목은 실제 게임 씬 연결 전의 기록이다. 현재 통합 상태는 다음 게임 씬 적용 검증을 따른다.

- Assets/ProjectSettings/Packages에서 삭제한 전용 코드, 씬 이름 및 Git index에 보관된 이전 스크립트/씬 GUID의 참조가 없는 것을 확인했다.
- Build Settings에는 SampleScene만 등록되어 있고, 현재 열린 씬에도 Missing Script가 없다. Console 오류와 경고는 0건이었다.
- 기존 문서의 삭제된 씬 경로, 컴포넌트, 단축키 및 Material 설명을 본 수동 절차로 교체했다.
- 현재 Assets/Scripts에는 Managers.Sound를 호출하는 실제 게임의 재생 호출부가 발견되지 않았다. 현재 열린 SampleScene의 StudioListener는 0개다. 따라서 실제 게임에서의 재검증은 위 실행 준비를 완료한 후 진행해야 한다.
- Library가 연결하는 event:/Test/TestFootsteps와 event:/Test/TestAmbience는 현재 사용 중인 FMOD 이벤트다. 테스트라는 이름만을 이유로 삭제하지 않으며, 이번 작업에서 변경하지 않았다.

#### 게임 씬 적용 검증 (2026-10-10)

- 환경: Unity 6000.0.55f1, FMOD Studio/Unity 2.03.15, Windows의 실행 중인 Unity Editor. Unity CLI MCP/Pipeline에서 씬 구성을 확인하고 Play Mode의 실제 게임 공개 명령과 판정 콜백을 실행했다. 스피커/헤드폰의 사람이 듣는 검증은 하지 않았다.
- 대상: `Assets/Scenes/Map/Stairwell.unity`, `Assets/Scenes/Map/StraightCorridor.unity`, `Assets/Scenes/Map/TCorridor.unity`. Library의 실제 EventReference와 GUID를 Event Browser에서 확인했다. Desktop SFX/BGM/Master Bank 빌드와 Unity Bank 갱신을 수행했다.
- 승인: A 자동 구현, 일반 1타 A / 2타 B / 발도 C, 일반 충돌에 Enemy_Damaged 임시 사용, EnemyCombatService에 접촉 판정 완료 알림 추가. 전투 피해/사망 로직과 공통 Managers는 변경하지 않았다.

| 항목 | Stairwell | StraightCorridor | TCorridor |
| --- | --- | --- | --- |
| Listener / Missing Script | 1개 / 0개 | 1개 / 0개 | 1개 / 0개 |
| 실제 플레이어 이동 발 접촉 | 2회, Footstep 재생 확인 | 2회, Footstep 재생 확인 | 2회, Footstep 재생 확인 |
| 실제 일반 1/2타 미충돌 | A=0 / B=1, PLAYING | A=0 / B=1, PLAYING | A=0 / B=1, PLAYING |
| 일반 장애물 접촉 | 충돌음 1, No_Impact 0 | 충돌음 1, No_Impact 0 | 충돌음 1, No_Impact 0 |
| 실제 적 피해·사망 | 배치된 적 없음 | 실제 적 사망 후 충돌음 1, No_Impact 0 | 실제 적 사망 후 충돌음 1, No_Impact 0 |
| 적 발 접촉 재생 경로 | 배치된 적 없음 | FootstepEmitter 접촉 경로 1회 → EnemyFootstep 1 | FootstepEmitter 접촉 경로 1회 → EnemyFootstep 1 |
| 발도음 | 납도 후 일반 공격 시작에서 1개 | 납도 후 일반 공격 시작에서 1개 | 기존 등장 시각화 IsPlayingIntro를 감지한 재생 플래그 확인 |
| C Label 선택 API | C=2, PLAYING | C=2, PLAYING | C=2, PLAYING |
| 이동 제한 중 플레이어 발도 요청 | 성공 | 성공 | 성공 |

- 추가로 Stairwell에서 실제 BeginCharge → 충전 완료 → RequestIaidoAttack → IaidoSlash → NotifyIaidoSlashImpact 흐름을 실행했다. 요청 성공, 발도음 1개, SwingType=2(C), PLAYING을 확인했다. StraightCorridor/TCorridor의 실제 전체 발도 동작은 별도 미확인이며 Label API 확인과 구분한다.
- 일반 공격은 실제 RequestNormalAttack 및 NotifyNormalAttackImpact/AnimationEnded로 기존 판정·오디오 알림을 실행했다. 입력 장치로 처음부터 끝까지 조작하는 검증과 애니메이션의 체감 타이밍 검증은 별도다.
- 적 발소리는 기존 FootstepEmitter의 접촉 경로를 호출해 연결을 확인했다. 실제 순찰/돌진 전체의 보폭·타이밍을 직접 듣는 검증은 미확인이다. 실제 적 피해는 배치된 EnemyHealth에 기존 전투 판정을 적용했으며 즉시 비활성화된 후에도 피격음이 남았다.
- 계단의 기존 StairwellMusic 인스턴스는 PLAYING이었다. 현재 Studio 이벤트가 IsOneShot=true이므로 계단 BGM 반복은 통과하지 않았다. 반복이 필요하면 Studio 담당자가 Loop Region을 설정하고 다시 빌드한다.
- Unity 재컴파일 성공, 최종 Editor Console 오류 0건. 검증 도중 계단의 기존 누락된 전투 서비스/Player Root 참조를 발견하여 씬 참조를 보완한 뒤 장애물 판정을 다시 통과했다.
- FMOD Live Update 9264 포트 충돌 경고가 반복 발생했다. 새 사운드 재생은 성공했지만 Live Update 연결은 정상으로 판정하지 않는다. 승인 없이 포트/활성 여부 설정을 변경하지 않았다.
- FMOD Bank 빌드는 동일 에셋의 compression/streaming 설정 충돌 경고로 기본 실행이 막혀 `-ignore-warnings`로 빌드했다. 기존 압축/스트리밍 설정은 유지했으며 경고의 원인은 해결하지 않았다. 새 Bank 로드와 대상 이벤트 재생은 확인했다.
- 검증용 위치 이동/장애물/입력 중지는 Play Mode에서만 사용하고 씬에 저장하지 않았다. 종료 후 Play Mode를 나왔으며 일회성 관찰/적용 스크립트는 제거했다. SoundTest 또는 영구 Editor 도구는 추가하지 않았다.
- Bank 검색으로 자동 추가됐던 FMOD Settings의 Test Bank 목록 항목은 이번 작업 전 상태와 대조해 그 추가분만 복구했다. 기존 사용자 수정이 있던 FMODStudioSettings.asset, Packages, ProjectSettings, Managers.cs는 작업 시작 시점과 동일하다. 캐시와 로그 및 csproj는 Editor가 자동 갱신한 결과이며 사운드 소스 변경과 구분한다.

##### 사용자가 직접 확인할 최종 청취 절차

1. 현재 게임 씬(BambooStage1 또는 RainyTwinBoss)을 Play한다. 일반 공격은 좌클릭 또는 RB, 막기는 우클릭 또는 LB, 일섬은 Shift 또는 RT, 거합은 Q 또는 Y를 사용한다. 씬을 처음 클릭할 때는 커서 잠금만 발생할 수 있다. 입력 전체표는 [패드 입력 안내](패드_입력_안내.md)를 따른다.
2. 걷고 멈추며 플레이어 발 접촉마다 한 번 들리는지 확인한다. 플레이어를 정지하고 적의 순찰을 관찰하여 적 발소리의 좌우/거리 감쇠와 보폭을 확인한다. 기존 정책상 플레이어 이동 중 일반 적 발소리는 제한된다.
3. 빈 공간에 일반 공격 두 번과 발도를 실행한다. 첫 스윙=A, 둘째=B, 발도=C의 음색을 Studio Preview와 비교한다. 첫 검 뽑기와 베기의 체감 타이밍도 확인한다.
4. 벽/물체를 치면 승인된 임시 Enemy_Damaged가 한 번 들리고 No_Impact는 재생되지 않아야 한다. 적을 베면 Multi Instrument가 샘플을 선택하고 적 사망 직후에도 꼬리가 갑자기 끊기지 않아야 한다. 쳐내기도 같은 임시 충돌음을 사용한다.
5. PlayerCombatAudio/EnemyDamageAudio의 Volume과 Studio 이벤트·SFX_Bus 음량을 비교 조절한다. 음량을 바꾼 뒤 실제 헤드폰으로 과도한 증폭/작은 소리를 확인한다. Studio를 수정하면 Bank를 다시 빌드한다.
6. 씬 전환/재시작 후 소리가 겹치거나 이전 씬 재생이 남는지 확인한다. 긴 플레이, 실제 입력 전체 흐름, 쳐내기 타이밍, 공간음향 및 BGM 반복 청취는 이번 자동 관찰 결과로 대체하지 않는다.

설정/컴포넌트와 팀 연결 계약은 [매니저 명세의 게임 씬 적용](FMOD_사운드_매니저.md#게임-씬-적용-2026-10-10-현재)을 참조한다.

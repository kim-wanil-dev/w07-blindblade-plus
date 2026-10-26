# DefenseBeast 일반 적 구현 명세

## 현재 공격 취소 시 NavMeshAgent 복원

- `EnemyHealth.TryTakeDamage`가 치명타 뒤 적 오브젝트를 비활성화하면 `EnemyController.OnDisable`이 진행 중인 물기 돌진을 취소한다. `DefenseBeastLeapAttack.OnAttackEnded`는 이동 설정을 복원하되, `NavMeshAgent.isStopped`는 에이전트가 활성 상태이고 NavMesh 위에 있을 때만 복원한다.
- 비활성 상태에서 취소됐다면 이전 정지 상태를 보류하고 `OnEnable` 또는 `Update`에서 NavMesh 복귀를 확인해 적용한다. 다음 공격 전에 복원 상태도 확인한다. 이 경로는 적 재활성화 후 이동 상태를 유지하기 위한 것이다.
- 관련 코드: `Assets/Scripts/Enemies/Attack/DefenseBeastLeapAttack.cs` / `OnAttackEnded`, `RestoreStoppedWhenReady`, `OnEnable`, `Update`, `PerformAttack`; `Assets/Scripts/Enemies/EnemyController.cs` / `OnDisable`; `Assets/Scripts/Enemies/EnemyHealth.cs` / `TryTakeDamage`.
- 구현 상태: 코드 경로 확인을 마쳤다. .NET 빌드는 `project.assets.json`이 없어 컴파일 전에 중단됐으며 Unity Play에서 치명타 중 도약 취소와 재활성화 후 이동은 미확인이다.

- 작성일: 2026-10-17
- 상태: **2026-10-18 관절 기반 애니메이션·원 그리기·물기 돌진·월드 공간 연기·몸체 숨김·눈 줄기·발 디딤 소리 연결. C# 빌드 확인, 실제 전투 Play 확인 대기**
- 최신 전투 규칙은 아래 "2026-10-18 관절 애니메이션·원 그리기·물기 돌진" 절이며, 교전 시작 순서와 배회·물러나기 거리는 "2026-10-22 배회 후 공격 복귀·교전 거리 축소" 절, 돌진 직전 눈 섬광은 "2026-10-19 돌진 확정 눈 섬광" 절, 덮치기 궤적과 전조 시간은 "2026-10-19 로그 곡선 덮치기·전조 연장" 절이 우선한다. 눈 줄기와 발소리 연결은 아래 최신 보완 절을 따른다. 그 아래 도약(포물선·10m 추월·반대편 6m 이동) 기록은 이전 동작이며 현재 코드와 다르다.

## 2026-10-26 작은 늑대 공격 전조 0.8초와 방향 표시 (최신 규칙)

- `DefenseBeast.prefab`의 첫 박동 대기를 0.4→0.3초로 줄이고 박동 뒤 돌진 대기 0.5초는 유지했다. 실제 웅크림·붉은 경고의 `TelegraphDuration`은 0.8초다. 아래 2026-10-19 절의 0.9초는 변경 전 기록이다.
- `DefenseBeastWarning(52)` 음원도 0.8초로 줄이고 마지막 0.1초를 페이드했다. 전조 시작부터 공격 실행까지의 소리 길이를 일치시켰다. FMOD Desktop SFX 뱅크와 Unity `StreamingAssets` 복사본을 갱신했다. 실제 Play 청취는 미확인이다.
- 정식 1·2스테이지에서 화면 밖 늑대가 물기 돌진을 시도하는 동안 발걸음 아이콘 자리에는 이빨 방향 아이콘이 표시된다. 마지막 0.2초는 붉게 강조하고 돌진음 `DefenseBeastLeap(48)`은 실행 순간 유지한다. [전투 HUD](전투_HUD.md)의 최신 절을 따른다.
- 전체 C# Roslyn 컴파일 오류 0건. Unity 배치 실행의 LicensingClient/WMI 오류 때문에 실제 Play에서 경고음 길이·돌진 타이밍·아이콘은 미확인이다.

## 2026-10-24 돌진 경로 검사 재사용·눈 섬광 텍스처 공유 (최신 규칙)

- `DefenseBeastLeapAttack.HasClearLunge`는 같은 대상의 NavMesh·직선 장애물 검사 결과를 같은 프레임 또는 `Lunge Check Interval`(기본 0.1초, 게임 시간) 동안 재사용한다. `DefenseBeastProwl.IsReadyToAttack`이 Ready 단계에서 매 프레임 부르던 `NavMesh.SamplePosition`·`CapsuleCastNonAlloc`이 줄었다. 0이면 같은 프레임 안에서만 재사용한다.
- 실제 돌진 시작(`PerformAttack`)의 `TryFindLungeDestination`은 재사용하지 않고 항상 다시 검사하므로 막힌 경로로 뛰어들지 않는다. 경로가 열린 것을 알아채는 시점은 최대 0.1초 늦을 수 있다. 다시 활성화되면 저장 결과를 버린다.
- `DefenseBeastEyes.CreateGlints`는 1024×1024 섬광 모양 텍스처(`TwinBeastEyes.CreateGlintShape`)를 static `_sharedGlintShape`에 처음 한 번만 만들고 모든 늑대가 함께 쓴다. 기존에는 풀에 새 늑대가 생길 때마다 약 35ms·4MB 할당과 늑대별 4MB 텍스처가 생겼다. 모양·색·크기·윤곽선 처리는 같다. 씬 전환으로 텍스처가 해제되면 다음 늑대가 다시 만든다.
- 검증: C# 빌드 오류 0개. 텍스처 공유 전 프로파일러에서 `DefenseBeastEyes.Start` 35.55ms·GC 4.0MB를 확인했다. 공유 후 늑대 생성 스파이크 제거와 섬광 외형은 Play 미확인이다.

## 2026-10-24 연기 방출 메시 경량화 (최신 규칙)

- `DefenseBeastSmokeShape.asset`은 본 36개마다 Unity 기본 도형 하나(구 13개: 정점 515·삼각형 768, 캡슐 21개: 550·832, 상자 2개: 24·12)를 가중치 1로 고정한 메시였다. 같은 GUID로 다시 만들어 프리팹 참조는 바꾸지 않았다.
- 구(반지름 0.5)와 캡슐(반지름 0.5·높이 2)은 같은 본 공간 형상을 12분할 × 8띠로 다시 생성하고, 상자 2개는 그대로 복사했다. 정점 18,293 → 3,224, 삼각형 27,480 → 6,240이며 단일 본 가중치·바깥 법선·삼각형 감기 방향을 유지한다. 방출 표면적은 원본의 97.5%이고 경계 상자는 같다. 표면이 평균 반지름 1% 정도 안쪽이라 가닥 시작점이 아주 조금 안쪽이다.
- `SmokeShape`의 `updateWhenOffscreen`, 방출 설정, `TwinBeastSmokeTuning` 배율은 바꾸지 않았다. 늑대마다 매 프레임 방출용으로 스킨하는 정점이 줄어든다. 아래 2026-10-18 절의 18,293개 정점 설명은 이 절이 우선한다.
- 검증: 생성 메시를 디코딩해 본별 정점·삼각형 수, 가중치, 본 공간 반지름·높이 범위를 확인했다. Unity 임포트와 Play 외형·성능은 미확인이다.

## 2026-10-24 충돌체 없는 피격 캡슐 (최신 규칙)

- 원본 `Enemy_SingleMelee`의 루트 Rigidbody와 `Placeholder` CapsuleCollider를 없애고 같은 fileID의 `EnemyHitCapsule`로 바꿨다. Variant는 기존 캡슐 오버라이드를 `_radius` 0.45·`_height` 2.6·`_direction` 2(Z)로 옮겼다.
- 기존 Exclude Layers(모든 레이어)의 "물리 접촉만 끄고 Raycast·Overlap은 맞는다" 의도를 `_blocksPlayer` 0으로 유지한다. 플레이어 몸을 막지 않지만 플레이어 공격·발도 경로 검사에는 걸린다. 아래 "2026-10-18 관절 애니메이션" 절의 충돌 항목과 Inspector 표의 이전 값은 이 절이 우선한다. 상세는 [적 피격 캡슐](적_피격_캡슐.md)을 따른다.

## 2026-10-22 배회 후 공격 복귀·교전 거리 축소 (사용자 요청, 최신 규칙)

- 교전 시작 순서를 "공격 후 배회"에서 다시 "배회 후 공격"으로 바꿨다. 추적 끝에 9m 안으로 처음 들어올 때도 공격 뒤와 같이 원 그리기 → 물러나기 → 돌진 대기 → 물기 돌진 순서로 진행한다. 호출이 0.25초(`_resetGap`) 넘게 끊겼다가 다시 시작되면 항상 새 원 그리기(`BeginCycle`)부터 시작한다.
- 아래 "2026-10-19 선공격 후 배회" 절의 `BeginReady`, `HandleTelegraphStarted`, `_attackedSinceReset`, `TelegraphStarted` 구독은 지웠다. `DefenseBeastLeapAttack.TelegraphStarted` 이벤트 자체는 그대로다.
- 거리(사용자 요청): 원 반지름을 4.5~6.5m에서 2.25~4m로, 돌진 전에 물러나는 거리(`_retreatRadius`)를 8m에서 4m로 줄였다. 물러나기 완료 판정(목표 거리 − 0.5m)과 돌진 대기 중 도는 반지름도 이 4m를 따른다. 교전 사거리 9m, 원 시간 1.6~3초, 물러나기 제한 1.2초, 돌진 대기 제한 2초, 감지 거리 10m는 바꾸지 않았다.
- 관련 코드·프리팹: `Assets/Scripts/Enemies/DefenseBeastProwl.cs`의 `UpdateEngagement`, `OnEnable`, 필드 기본값; `Assets/Resources/Defense/DefenseBeast.prefab`(`_orbitRadiusMin` 2.25, `_orbitRadiusMax` 4, `_retreatRadius` 4 직접 기록). 씬 덮어쓰기는 없다.
- 확인: `dotnet build Assembly-CSharp.csproj --no-restore` 오류 0개, 경고 16개(이번 변경과 무관). Play에서 첫 진입 배회, 가까워진 원 그리기와 4m 돌진 체감, 다수 늑대가 가까이 돌 때의 서로 막힘은 미확인이다. 테스트 코드는 작성하지 않았다.

## 2026-10-19 로그 곡선 덮치기·전조 연장 (사용자 요청, 최신 규칙)

- 궤적: 가운데가 0.35m 뜨는 포물선 대신 로그 곡선처럼 땅을 박차자마자 빠르게 솟고 뒤로 갈수록 평평해지는 경로로 뛴다. 높이 = `Lunge Peak Height`(0.6m) × log(1 + `Lunge Curve Steepness`(9) × 수평 진행도) / log(10)이며, 수평 진행도는 기존처럼 감속(1 − (1 − t)²)한다. 곡선은 계속 오르므로 물기 위치에서 몸 루트가 최고 높이 0.6m에 닿는다. 기존 `_lungeHop` 필드는 `_lungePeakHeight`·`_lungeCurveSteepness`로 바꿨다.
- 속도: 기존 16m/s(`Lunge Speed`)를 유지하되, 비행 시간을 수평 거리 대신 높이를 포함한 실제 경로 길이(16구간 표본, `GetLungePathLength`)로 나눠 정한다. 8m에서 뛰면 약 0.43초로 기존과 거의 같다. 다리 뻗기(`LungeAir`)는 높이 ÷ 최고 높이다.
- 물기: 이전에는 도착 즉시 NavMesh로 Warp한 뒤 물었다. 이제 공중 위치에서 접촉을 판정하고, 물어뜯는 0.25초 동안 바로 아래 땅으로 점점 빠르게 떨어진 뒤 NavMesh에 맞추고 이탈한다(맞추지 못하면 취소). 막기·쳐내기 반동은 공중 위치에서 땅 위 반동 지점으로 이동한다. 공중에서 취소되면 시작 NavMesh 높이에서 바로 아래 땅을 찾아 Warp한다.
- 전조(사용자 요청): 점프 준비(웅크림·붉은 경고)를 0.2초 늘려 0.9초로 했다. `DefenseBeast.prefab`의 `_firstHeartbeatDelay`를 0.2초에서 0.4초로 바꿔 박동음부터 돌진까지 0.5초(`_postHeartbeatAttackDelay`)는 유지한다.
- 관련 코드·프리팹: `Assets/Scripts/Enemies/Attack/DefenseBeastLeapAttack.cs`의 `PerformAttack`, `UpdateLunge`, `GetLungeHeight`, `GetLungePathLength`, `UpdateBite`, `OnAttackEnded`, `ActiveDuration`; `Assets/Resources/Defense/DefenseBeast.prefab`(값 직접 기록).
- 확인: `dotnet build Assembly-CSharp.csproj` 오류 0개, 기존 경고 4개. Play에서 궤적 체감, 0.6m 높이 물기 판정, 공중 낙하 자세, 낮은 천장 근처 돌진 취소 여부, 늘어난 전조 체감은 미확인이다. 테스트 코드는 작성하지 않았다.

## 2026-10-19 돌진 확정 눈 섬광 (사용자 요청, 최신 규칙)

- 쌍둥이 늑대 보스의 덮치기 전 눈 섬광(`TwinBeastEyes`)과 같은 하늘색 + 모양 섬광을 일반 Beast에도 넣었다. `DefenseBeastEyes`가 `DefenseBeastLeapAttack.AttackCommitted`를 구독해, 박동 전조가 끝나고 물기 돌진이 확정되는 순간 두 눈에서 한 번 터뜨린다. 섬광 모양 텍스처는 `TwinBeastEyes.CreateGlintShape`(접근 제한자만 `internal`로 변경)를 같이 쓰고, 재질은 몸 연기(EnemySmokeVisual) 재질을 쓴다.
- 크기(사용자 요청): 기본 0.9m, 가로 배율 1(0.9m)·세로 배율 1.5(1.35m), 눈 좌표보다 머리 로컬 0.5m 아래. 처음 0.03초 동안 원래 크기의 2.25배(`Glint Emphasis Scale`)로 터져 강조하고 0.2초(`Glint Emphasis Duration`)에 원래 크기로 돌아온 뒤 0.4초(`Glint Duration`) 동안 사라진다(전체 0.6초).
- 윤곽선 두께(사용자 요청): 트윈 보스 섬광보다 2.5배 두꺼운 하늘색 경계로 그린다. `TwinBossWarningVisual.AddGlintRenderers`에 `thickOutline` 인자(기본 false)를 더해, 참이면 경고 마스크(`OutlineMeleeWarningMask`)가 섬광을 B 대신 A 채널에 기록한다(`ColorMask RGBA`, 다른 대상의 A는 0). `ScreenSpaceOutline`은 A 채널을 기존 섬광 표본 간격의 `_BeastThickGlintWidthScale`(2.5)배로 읽어 경계를 그린다. 트윈 보스 섬광과 다른 경고 윤곽선은 그대로다.
- 관련 코드·프리팹: `Assets/Scripts/Enemies/DefenseBeastEyes.cs`의 `CreateGlints`, `HandleAttackCommitted`; `TwinBeastEyes.cs`의 `CreateGlintShape`; `TwinBossWarningVisual.cs`의 `AddRedFlashRenderers`, `AddGlintRenderers`; `Assets/Resources/Defense/OutlineMeleeWarningMask.shader`; `Assets/Shaders/ScreenSpaceOutline.shader`; `Assets/Resources/Defense/DefenseBeast.prefab`(새 섬광 값 직접 기록).
- 확인: `dotnet build Assembly-CSharp.csproj` 오류 0개, 기존 경고 4개. 셰이더 컴파일, Play에서 섬광 크기·강조·두꺼운 경계 모양(표본 간격이 넓어 경계가 이중으로 보이는지)은 미확인이다. 돌진 위치를 찾지 못해 공격이 바로 끝나면 붉은 전조가 0.15초 안에 꺼지면서 섬광 윤곽도 함께 사라질 수 있다(미확인). 테스트 코드는 작성하지 않았다.

## 2026-10-19 선공격 후 배회 (이전 동작, 2026-10-22 절로 대체)

> 2026-10-22에 "배회 후 공격"으로 되돌렸다. 아래는 이전 기록이며 현재 코드와 다르다.

- 교전 시작 순서를 "배회 후 공격"에서 "공격 후 배회"로 바꿨다. 추적 끝에 9m 안으로 처음 들어오면 원 그리기·물러나기 없이 바로 돌진 대기(`Ready`)가 되고, 돌진 경로가 열려 있으면 즉시 물기 돌진을 시도한다.
- `DefenseBeastProwl`은 `DefenseBeastLeapAttack.TelegraphStarted`를 구독해 공격 전조 시작을 기록한다. 호출이 0.25초 넘게 끊겼다가 다시 시작될 때 그 사이 전조가 있었으면 기존처럼 새 원 그리기(`BeginCycle`)부터, 없었으면 `BeginReady`로 돌진 대기부터 시작한다. 이후 원 그리기 → 물러나기 → 돌진 순환과 돌진 대기 2초 초과 시 원 그리기로 넘어가는 규칙은 그대로다.
- 관련 코드: `Assets/Scripts/Enemies/DefenseBeastProwl.cs`의 `UpdateEngagement`, `BeginReady`, `HandleTelegraphStarted`. 프리팹·다른 적은 바꾸지 않았다.
- 확인: `dotnet build Assembly-CSharp.csproj` 오류 0개, 기존 경고 4개(CS0414·CS0618, 이번 변경과 무관). Play에서 첫 진입 즉시 돌진과 공격 뒤 배회 전환은 미확인이다. 테스트 코드는 작성하지 않았다.

## 2026-10-18 TwinBoss 눈 줄기·발 디딤 소리

- `DefenseBeastEyes`의 붉은 눈 줄기를 `TwinBoss.prefab`의 Beast `Wolf`에도 적용했다. TwinBoss의 기존 머리 로컬 눈 위치를 유지하고 같은 발광 재질, 꼬리 길이, 휘날림, 연기 바람 연결을 사용한다. `TwinBeastEyes`는 덮치기 전 순간 섬광에 계속 사용한다.
- 일반 DefenseBeast는 `DefenseBeastWolfVisual.PawPlanted`가 실제 발 접촉 위치를 알리고 `EnemyFootsteps`가 그 위치에서 발소리를 재생한다. 같은 적의 이전 발소리를 새 소리 전에 강제 정지하고 재생 간격을 최소 0.18초로 제한한다. `DefenseBeast.prefab`에서는 `_useAnimationEvents`를 켰으며, `FootstepEmitter`와 발소리 C# 이벤트는 감각 전달을 위해 유지한다.
- 관련 코드·프리팹: `Assets/Scripts/Enemies/DefenseBeastEyes.cs`, `DefenseBeastWolfVisual.cs`, `EnemyFootsteps.cs`; `Assets/Resources/Defense/DefenseBeast.prefab`; `Assets/Resources/Enemies/TwinBoss.prefab`.
- 확인: `dotnet build Assembly-CSharp.csproj --no-restore` 성공. Unity Play에서 눈 위치·줄기와 실제 발소리 타이밍은 미확인이다.

## 2026-10-18 관절 애니메이션·원 그리기·물기 돌진 (사용자 요청, 최신 규칙)

- 적용 범위: `DefenseBeast.prefab`을 직접 바꿨으므로 이 프리팹을 쓰는 모든 스테이지(`BambooStage1`, `RainyStage_JH`, `RainyTwinBoss`, `TestStage`)의 일반 Beast에 적용된다(사용자 결정). 크기는 기존 1배 리그(짐승 보스의 절반)를 유지한다.
- 교전 흐름: 수평 9m(`Attack Range`) 안에 들면 `DefenseBeastProwl`이 플레이어 주변을 달리며 반지름 4.5~6.5m 원을 1.6~3초 그린다(현재 방향보다 40도 앞선 원 위 지점을 0.1초마다 목표로 갱신, 도는 방향 무작위, NavMesh 밖이면 반대로 돈다). 이어 8m까지 원을 따라 비스듬히(20도) 물러나고(최대 1.2초), 돌진 경로가 열려 있으면 공격을 허용한다. 2초 안에 공격이 시작되지 않으면 원 그리기부터 다시 한다. 공격·추적으로 호출이 0.25초 넘게 끊기면 새 원으로 시작하며, 공격 후딜 중에도 원을 그린다.
- 물기 돌진(`DefenseBeastLeapAttack`, 클래스·PatternId `DefenseBeastLeap` 유지): 기존 박동 전조(0.2초 + 0.5초)와 붉은 경고 동안 웅크린 채 플레이어 쪽으로 초당 540도 몸을 돌린다. 확정 순간 플레이어 앞 1.1m(`Bite Stop Distance`) 지점까지 16m/s로 감속하며 낮게(최고 0.35m) 뛰어든다. 플레이어가 돌진 축을 따라 물러나면 처음 거리보다 최대 3m(`Max Track Distance`) 더 따라가고, 옆으로 피하는 것은 따라가지 않는다. 경로 반경 1.6m(`Bite Radius`) 안에서 플레이어와 처음 닿으면 한 번 방어 판정을 하고 10 피해를 준다. 닿거나 도착하면 0.25초 동안 물어뜯은 뒤, 플레이어에서 돌진 반대쪽으로 좌우 무작위 50도 비껴 6m 지점으로 9m/s로 빠져나간다(최대 1.5초).
- 방어 결과: Guard·NormalParry·PerfectParry는 기존처럼 플레이어 시야 전방 4m로 0.25초 반동한다. Hit·DodgeAvoided·Invulnerable·빗나감은 물기 뒤 이탈한다. 돌진 중 장애물에 막히거나 대상·자신이 사라지면 취소하고 현재 위치를 NavMesh에 맞춘다.
- 교전 판정: `CanAttack`은 생존과 9m 사거리만 본다. 돌진 경로(NavMesh·직선 장애물)는 `HasClearLunge`로 원 그리기의 돌진 허용 시점과 돌진 확정 때 따로 검사해, 경로가 잠깐 막혀도 추적 상태로 바뀌어 플레이어에게 곧장 달려오지 않게 했다.
- 관절 애니메이션(`DefenseBeastWolfVisual`): 짐승 보스 `TwinBeastWolfVisual`의 방식으로 발 위치를 먼저 정하고 두 마디 다리를 풀어 엉덩이·무릎·발목을 돌린다. 엇갈림 질주, 발 디딤 스프링 반발, 질주 솟구침, 몸·고개 회전 지연, 플레이어를 노려보는 고개 돌림, 대기 동작(살피기·냄새·무게 옮기기·으르렁), 웅크림, 아래턱·귀를 쓴다. 잠수·울음 자세는 쓰지 않는다. 뛰어드는 동안 입을 벌리고 몸이 뜬 만큼 다리를 뻗으며, 물어뜯는 동안 머리를 좌우로 흔들고 턱을 다문다. 방어 반동은 움찔 자세로 보인다.
- 삐걱임 보완(사용자 요청): 보폭을 질주 4.8m로 넓혀 이동 속도 7.2m/s에서 초당 1.5걸음이 되게 하고, 디딤 비율 0.2·솟구침 0.32m·발 들기 0.36m로 공중 구간을 늘렸다. 솟구침 구간은 디딤 비율로 계산한다. 다리 길이의 85%(`Soft Reach Start`)부터는 지수 곡선으로 천천히 펴 무릎이 끝까지 펴지며 튀지 않게 했다. 뛰어들기·물기·반동 중 몸 회전 지연은 한 번에 지우지 않고 4배 센 스프링으로 따라잡는다. 속도 반영 6, 발 디딤 충격 0.3, 출렁임 감쇠 0.6.
- 형상과 연기: Play 시작 시 36개 몸체 렌더러를 꺼 짐승 보스처럼 연기만 보인다(Scene 뷰에는 `SceneShapeGizmo`). 연기는 기존 스킨 메시 방출 파티클 두 개를 유지하고 Wolf에 짐승 보스의 `TwinBeastSmokeTuning`을 추가해 월드 공간으로 계산한다. 늑대가 돌아도 남은 연기가 따라 돌지 않는다. 현재 프리팹 값은 사용자가 Play에서 조정한 Density 7, Size 0.5, Length 0.4, Lifetime 0.3, Flow Speed 1, Rise Force 0.5, Surface Push 0.5, Flutter 0.15/1/0.6, Sway 1.5·최대 3·2.5회·감쇠 0.6이다. 눈은 아래 "2026-10-18 눈 줄기" 절로 바꿨다. 붉은 전조는 `TwinBossWarningVisual`이 연기 렌더러에 그린다.
- 충돌: 피격 캡슐(`Placeholder`)과 루트 키네마틱 Rigidbody의 Exclude Layers를 모든 레이어로 설정해 물리 접촉만 끈다. Raycast·Overlap 검사는 그대로라 플레이어의 베기는 맞는다. NavMeshAgent 회피는 유지한다(사용자 결정).
- 감지 거리: 8m 원과 물러나기 거리를 감지 안에 두려고 `EnemySensor` Detection Range를 8m에서 10m로 늘렸다.
- 공용 코드: `EnemyController`에 선택 인터페이스 `IEnemyEngagementMovement` 연결을 추가했다. 구현 컴포넌트가 있으면 공격 범위 안에서 멈추는 대신 그 이동을 호출하고, 준비될 때만 공격을 시도하며, 후딜 중에도 이동을 호출한다. 구현이 없는 다른 적은 기존과 같다.
- 확인: `dotnet build Assembly-CSharp.csproj` 오류 0개, 기존 경고 2개(CS0414). 프리팹 파일 ID 중복 없음과 새 스크립트 GUID 연결을 확인했다. 사용자가 Play에서 걸음·연기를 보고 조정을 요청해 반영했다. 최신 물기 피해·충돌 통과(특히 CharacterController가 Exclude Layers를 따르는지)·원 그리기 체감·다수 늑대 성능은 미확인이다. 테스트 코드는 작성하지 않았다.

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| Assets/Scripts/Enemies/DefenseBeastProwl.cs (신규) | `UpdateEngagement`, `BeginCycle`, `MoveAround` | 원 그리기·물러나기·돌진 허용 |
| Assets/Scripts/Enemies/IEnemyEngagementMovement.cs (신규) | `IsReadyToAttack`, `UpdateEngagement` | 공격 범위 안 교전 이동 연결 계약 |
| Assets/Scripts/Enemies/EnemyController.cs | `Update`, `UpdateRecoveryPursuit` | 교전 이동 호출과 공격 시도 조건 |
| Assets/Scripts/Enemies/Attack/DefenseBeastLeapAttack.cs | `CanAttack`, `HasClearLunge`, `PerformAttack`, `UpdateLunge`, `TrackLungeDestination`, `UpdateBite`, `GetPeelDestination` | 물기 돌진·축 추적·물어뜯기·이탈 |
| Assets/Scripts/Enemies/DefenseBeastWolfVisual.cs | `ApplyPose`, `SolveLeg`, `SoftenReach`, `UpdateTurnLag`, `UpdateHeadLook` | 관절 기반 걸음·공격 자세, 몸체 숨김 |
| Assets/Resources/Defense/DefenseBeast.prefab | 위 컴포넌트, `TwinBeastSmokeTuning`, 센서·충돌 덮어쓰기 | 직렬화 값 직접 기록 |

## 2026-10-18 눈 줄기 (사용자 요청, 최신 규칙)

- 아래 "늑대형 연기 상승과 발광 눈" 절의 푸른 `EyeGlow_Left`·`EyeGlow_Right` Quad는 프리팹에서 지웠다. `DefenseBeastEyeGlow` 셰이더·재질 파일은 남아 있지만 쓰지 않는다.
- 목표 모습: 사용자가 준 참고 이미지처럼 두 눈에서 붉은 줄기 하나씩이 머리 뒤로 길게 끌린다. 다른 화면처럼 안을 비우고 가장자리 선만 그리며, 눈알(점)은 그리지 않는다.
- 위치: 루트의 `DefenseBeastEyes`가 머리 로컬 (±0.09, 0.3, 0.09)(귀와 입 사이, 사용자 조정값)에서 줄기를 낸다.
- 줄기: 눈마다 `LineRenderer` 하나가 0.02초마다 눈 위치에 점을 내고, 점은 월드 공간에 남아 0.45초 동안 머리 뒤로 0.8m/s, 바깥 옆으로 0.35m/s, 위로 0.05m/s 흘러간다. 첫 점은 늘 현재 눈 위치라 줄기가 눈에 붙어 있다. 오래된 점일수록 머리 오른쪽·위로 최대 0.06m 펄럭이며(초당 3회), 이은 길이가 1.5m를 넘으면 자른다. 굵기는 0.06m에서 꼬리로 0까지, 밝기는 1→0.75(30%)→0.3(70%)→0으로 옅어진다.
- 밀림: 점마다 몸 연기 `TwinBeastSmokeTuning`이 그 프레임 월드 연기에 더하는 바람 속도(`WorldDrift`)를 같이 더하고, 머리 움직임 반대쪽으로 1배(최대 8m/s, 사용자 조정값)를 더 밀어 몸 연기와 함께 뒤로 밀리게 했다.
- 그리기: 새 `DefenseBeastEyeStreak` 셰이더(`Assets/Resources/Defense/DefenseBeastEyeStreak.mat`)가 윤곽선 합성 뒤(Transparent+10, 가산 합성, 깊이 검사)에 그린다. 윤곽선 모드(`Outline Only` 1)는 띠의 양옆만 1픽셀(`Outline Width`) 붉은 선(1, 0.1, 0.06)으로 그리고 끝을 막는 선은 없다. 발광 모드(0)는 뜨거운 심과 붉은 번짐으로 그린다.
- 인지: `Use Perception` 1이면 윤곽선과 같은 전역 인지 값(시야원·소리·보상 파동·걸음 잔상·충격·구형 파동·빗방울)으로 줄기 위치의 공개 정도를 구해 몸처럼 드러날 때만 보인다. 계산은 `RainStage.shader`가 쓰는 방식과 같다.
- 전조: 몸 연기가 붉어지는 세기(`TwinBossWarningVisual.Intensity`)에 따라 밝기 최대 2.2배, 굵기 최대 1.5배가 된다.
- 이 과정에서 연기 입자 트레일과 별도 윤곽선 채널을 시도했으나 사용자 결정으로 되돌렸다. `OutlineVFX`·`OutlineVFXRouting`·`ScreenSpaceOutlineFeature`·`ScreenSpaceOutline.shader`·`ThreatPresentationRouting`은 원래대로다.
- 공용 코드: 짐승 보스 `TwinBeastSmokeTuning`에 읽기 전용 `WorldDrift` 프로퍼티만 추가했다. 보스 동작은 바뀌지 않는다.
- 확인: `dotnet build Assembly-CSharp.csproj` 오류 0개, 기존 경고 2개. 사용자가 Play에서 줄기를 보고 모양·밀림을 조정 요청해 반영했다. 최신 셰이더(윤곽선 모드)의 Unity 컴파일과 화면은 미확인이다.

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| Assets/Scripts/Enemies/DefenseBeastEyes.cs (신규) | `UpdatePoints`, `DrawLine`, `GetDrift`, `CreateLine` | 월드 공간 눈 줄기 점 진행·잇기·밀림 |
| Assets/Shaders/DefenseBeastEyeStreak.shader (신규) | `OutlineStrength`, `PerceptionVisibility` | 가장자리 선·발광 그리기와 인지 공개 |
| Assets/Resources/Defense/DefenseBeastEyeStreak.mat (신규) | 재질 | 윤곽선 모드·선 색·굵기 |
| Assets/Scripts/Enemies/TwinBeastSmokeTuning.cs | `WorldDrift` | 몸 연기 바람 속도 공유 |
| Assets/Resources/Defense/DefenseBeast.prefab | `DefenseBeastEyes`, `EyeGlow` 제거 | 직렬화 값 직접 기록 |

## 기능과 행동 규칙 (2026-10-17 도약 기록, 현재는 위 절이 우선)

Assets/Resources/Defense/DefenseBeast.prefab은 Enemy_SingleMelee.prefab에서 직접 상속한 Prefab Variant다. TwinBoss.prefab의 Beast/Wolf 시각 계층을 사용하고 `EnemySmokeVisual` 파티클이 결합된 스킨 메시 표면에서 연기를 방출한다. 일반 적의 이동·체력·공격 컴포넌트를 유지하며 TwinBoss의 Beast 공격·컨트롤러 동작은 변경하지 않았다.

DefenseBeast는 플레이어를 추적하고, 수평 거리 6m 이내에서 완전한 NavMesh 경로와 장애물 없는 도약선이 있으면 0.55초 동안 최고 1.5m 높이의 포물선 도약을 한다. 플레이어의 CharacterController 높이는 2m다. 막기 또는 쳐내기에 막히면 접촉 위치에서 플레이어 시야 전방 4m 지점으로 0.25초 동안 반동한다. 그 지점에 유효한 NavMesh가 없거나 경로가 막혀 있으면 도약 시작 위치로 돌아온다. 착지 반경은 2m이고 피해는 10이다. 전조가 시작되면 플레이어가 감지 거리 밖으로 이동하거나 시야가 가려져도 도약을 취소하지 않는다. 전조 시작 시 고정한 수평 도약 축에 현재 플레이어 위치를 투영해 도약 중 앞뒤 위치만 따라가며, 좌우 위치 변화는 착지점에 반영하지 않는다. 기본 착지 목표는 현재 플레이어 위치에서 도약 축을 따라 10m 뒤이며, NavMesh가 그 지점까지 이어지지 않으면 플레이어 위치를 후보로 사용한다. 도약 중 경로와 플레이어 충돌체를 검사해 뒤로 물러나도 경로에 닿으면 피해를 판정한다. 좌우 회피는 도약 경로 밖으로 벗어나야 피할 수 있다. 도약 후 행동은 다음과 같다.

| 착지 결과 | 이동 목표 |
| --- | --- |
| Hit이며 실제 피해 적용 | 플레이어 반대편 최소 6m, 이미 10m 추월했다면 착지점 유지 |
| DodgeAvoided | 플레이어 반대편 최소 6m, 이미 10m 추월했다면 착지점 유지 |
| 플레이어와 접촉하지 않은 빗나감 | 플레이어 반대편 최소 6m, 이미 10m 추월했다면 착지점 유지 |
| Guard, NormalParry, PerfectParry | 플레이어 시야 전방 4m 지점으로 반동 |
| Invulnerable | 도약 후 기존 이동으로 도약 시작 위치 |

적중·회피·빗나감 후 이동은 NavMesh 완전 경로와 장애물 검사를 거친다. 방어 반동은 플레이어 전방 목적지의 NavMesh 샘플과 직선 장애물 검사를 거치며, 목적지가 유효하지 않으면 시작 위치를 사용한다. 제한 시간 안에 목적지에 닿지 못하면 현재 위치에서 후딜로 넘어간다. 벽을 관통하는 순간이동은 사용하지 않는다. 피격 취소·사망·비활성화가 발생하면 도약 이동을 중단하고 위치 및 NavMeshAgent 설정을 복원한다. 비활성화 상태에서는 Warp를 시도하지 않지만 저장한 Agent 설정은 복원한다.

## 프리팹 구성

- 직접 부모: Assets/Resources/Enemies/Enemy_SingleMelee.prefab
- TwinBoss의 Beast/Wolf 시각 계층을 복제하되, 일반 적 Wolf에는 전용 `DefenseBeastWolfVisual`을 연결한다. 이 스크립트는 `DefenseBeastLeapAttack`의 전조·도약 상태를 표시하며 TwinBoss 전용 `BeastBossAttack`이나 `TwinBeastWolfVisual`을 사용하지 않는다.
- 일반 적 루트는 NavMeshAgent, EnemyMotor, EnemyHealth를 하나씩 사용한다. 보스 공격·보스 오디오·TwinBossController·RiderSeat는 복제하지 않았다.
- 원본의 베기·돌진 공격과 더 이상 쓰지 않는 공격 시각 계층을 Variant에서 제거했다. 공격 선택기에는 DefenseBeastLeapAttack 하나만 지정하고 Melee Only는 끈다.
- Wolf 피격 판정은 기존 일반 적 체력과 연결했다. 프리팹에는 누락된 스크립트가 없고 중복 NavMeshAgent 또는 EnemyHealth가 없다.
- `DefenseEnemySpawner`의 일반 적/Beast 가중치 선택에 연결되어 있다. `BambooStage1`, `RainyStage_JH`, `RainyTwinBoss`, `TestStage` 씬의 스포너가 `DefenseBeast.prefab`을 참조한다.

## Inspector 초깃값 (2026-10-18 현재)

| 컴포넌트 / 설정 | 값 |
| --- | ---: |
| EnemyMotor 이동 속도 / 가속도 | 7.2m/s / 12m/s² |
| EnemySensor 감지 거리 | 10m |
| NavMeshAgent 반경 / 높이 / 속도 | 0.6m / 1.6m / 7.2m/s |
| DefenseBeastLeapAttack 교전 사거리 | 9m |
| 돌진 속도 / 뜨는 높이 / 멈춤 거리 / 접촉 반경 / 축 추적 | 16m/s / 0.35m / 1.1m / 1.6m / 3m |
| 물기 시간 / 피해 / 전조 회전 | 0.25초 / 10 / 540도/s |
| 이탈 거리 / 각도 / 속도 / 제한 시간 / NavMesh 샘플 반경 | 6m / 50도 / 9m/s / 1.5초 / 1m |
| 방어 반동 복귀 시간 / 플레이어 전방 거리 | 0.25초 / 4m |
| DefenseBeastProwl 원 반지름 / 앞선 각도 / 시간 | 2.25~4m (2026-10-22) / 40도 / 1.6~3초 |
| 물러나기 거리 / 각도 / 제한 / 돌진 대기 제한 / 재시작 간격 | 4m (2026-10-22) / 20도 / 1.2초 / 2초 / 0.25초 |
| DefenseBeastWolfVisual 질주 보폭 / 디딤 / 솟구침 / 발 들기 / 부드러운 펴짐 | 4.8m / 0.2 / 0.32m / 0.36m / 0.85 |
| 피격 캡슐(`EnemyHitCapsule`) 반경 / 높이 / 축 / 몸 막힘 | 0.45m / 2.6m / Z / 끔 (2026-10-24, Rigidbody·CapsuleCollider 없음) |
| 장애물 마스크 / 착지 마스크 | 51 / 모든 레이어 |
| 공통 전조 / 후딜 / 쿨타임 | 1초 / 0.75초 / 2초 |
| 박동·도약 소리 | EnemyHeartbeat / DefenseBeastLeap (보스 공격음보다 +2반음) |
| EnemyHealth 최대 체력 | 10 |

도약 수치와 공통 전조·쿨타임 설정은 프리팹 Inspector에서 조정할 수 있다. FootstepRipple은 DefenseMelee의 리플 설정을 사용하고 DefenseBeast 프리팹의 걸음 간격 설정은 2다.

## 전투·감각 연결

DefenseBeastLeapAttack은 EnemyAttackBehaviour를 상속하고 공격 로그 종류 및 DamageContext.Kind를 Melee로 분류한다. 도약 시작과 접촉에는 같은 AttackId와 PatternId를 사용한다. KatanaDamageReceiver는 저장된 진입 방향으로 도약 공격의 전방 방어를 판정한다. Guard는 기존 플레이어 밀림을 적용하고 패링은 DefenseBeast의 일반 적 자동 경직을 생략해 패턴이 시작점 복귀를 마칠 수 있게 한다.

`DefenseBeastWolfVisual`은 일반 적의 이동 속도와 `DefenseBeastLeapAttack` 상태를 사용해 걸음·웅크림·도약 자세를 표시한다. `TwinBeastWolfVisual`은 TwinBoss의 `BeastBossAttack` 상태와 잠수·물기 연출만 담당한다. FootstepRipple은 첫 박동과 공격 확정 사건에 기존 리플을 내며, 새 공격의 박동음과 도약음은 Inspector SoundId를 사용한다.

## 2026-10-17 도약 전조 붉은 실루엣

- `DefenseBeast.prefab`의 루트에 `TwinBossWarningVisual`을 연결했다. 현재 `_smokeRoot`는 Wolf를 가리키며 이 컴포넌트의 전조 강도를 `DefenseBeastWolfVisual`이 몸체 메시로 전달한다. 일반 근접 적 전조와 TwinBoss가 공유하는 `_MeleeWarningIntensity` 및 `ScreenSpaceOutlineFeature` 근접 경고 마스크를 사용한다.
- `DefenseBeastLeapAttack.PrepareTelegraph`가 실제 공격 문맥의 `TelegraphDuration` 동안 붉은 강도를 올린다. 착지 또는 공격 취소·종료 시 경고를 끝내고 0.15초 동안 기본 실루엣으로 돌아온다.
- TwinBoss 프리팹과 `TwinBeastWolfVisual`은 수정하지 않았다.

## 2026-10-17 Spine 형상에 밀착한 연기와 몸체 선 제거

- `DefenseBeast.prefab`의 Wolf 아래에 `SmokeShape` 스킨 메시 방출원을 추가했다. 기존 36개 Spine 몸체 메시의 정점 18,293개를 36개 본에 연결한 단일 메시이므로 다리·몸통·머리의 애니메이션과 함께 방출 형태가 움직인다.
- `EnemySmokeVisual.prefab`의 기존 `BodySmoke`와 `TrailingWisps` 두 시스템을 Wolf에 하나씩 배치하고 Shape 모듈을 `SkinnedMeshRenderer`로 설정했다. 몸통 연기는 입자 크기 0.68~0.86m, 수명 0.70~0.95초, 방출량 120개/s로 겹침을 늘리고, 속도 0.01~0.04m/s와 노이즈 0.04로 실루엣에서 크게 흩어지지 않게 했다. 보조 wisps는 크기 0.50~0.68m, 방출량 10개/s, 로컬 시뮬레이션으로 설정해 Beast 주변에 머물게 하고, 0.25m/s로 완만하게 상승시킨다. 색상 변화와 기존 연기 재질은 유지한다. Beast 한 마리당 ParticleSystem은 2개이며 메시 조각마다 이미터를 만들지 않는다.
- `DefenseBeastSmokeShapeSource`는 Shape 모듈이 읽는 스킨 메시를 계속 갱신하고 렌더링은 끈다. 화면 공간 연기 합성, `OutlineBeastSmokeMask.shader`, Beast 전용 화면 연기 수치는 사용하지 않는다.
- `ScreenSpaceOutlineFeature`가 보이는 Beast 몸체를 깊이 검사하는 R8 제외 마스크에 그린다. 최종 셰이더는 마스크에 닿는 Beast의 깊이·노멀 경계선을 숨기고, `EnemySmokeVisual` 파티클의 투명 윤곽선은 계속 그린다. 다른 적의 선과 전조 마스크는 기존 방식을 유지한다.
- 도약 전조의 붉은 연기 표시는 기존 근접 경고 R8 마스크를 사용한다. `DefenseBeastWolfVisual.WarningIntensity`가 전조 강도를 노출하며 경고 중인 몸체 렌더러만 경고 마스크에 포함한다.
- 확인: Unity에서 두 파티클 시스템의 스킨 메시 Shape, 눈의 본 연결·공유 머티리얼, 셰이더 메시지 0건을 확인했다. 실제 게임 카메라의 최신 외형과 교전 중 성능은 별도 확인이 필요하다.

## 2026-10-18 늑대형 연기 상승과 발광 눈

> 푸른 발광 눈은 "2026-10-18 눈 줄기" 절에서 지웠다. 아래 눈 기록은 이전 동작이다.

- `BodySmoke`의 위쪽 속도를 0.20m/s로 올려 몸체 실루엣에서 많이 벗어나지 않으면서 연기가 천천히 피어오르게 했다. 기존 `TrailingWisps`는 0.25m/s로 상승하며 Beast의 로컬 좌표에 남는다.
- `Head` 본 앞에 `EyeGlow_Left`와 `EyeGlow_Right` Quad를 배치해 머리 자세를 따라가게 했다. `DefenseBeastEyeGlow` 셰이더가 청록색 코어와 푸른 additive 후광을 그리고 Screen Space Outline 합성 뒤에 렌더한다. 두 눈은 `DefenseBeastEyeGlow.mat` 하나를 공유하며 별도 조명이나 충돌체는 사용하지 않는다.
- 확인: Unity에서 눈의 머리 본 연결·머티리얼과 셰이더 메시지 0건을 확인했다. 현재 게임 화면 캡처에는 Beast가 나오지 않아 실제 전투 중 외형과 성능은 확인 대기다.

## 2026-10-18 도약 준비 확정과 축 방향 추적

- `DefenseBeast.prefab`의 `DefenseBeastLeapAttack._cancelWhenHidden`을 끄고, 전조가 시작된 뒤 플레이어가 감지 거리 밖으로 이동하거나 가려져도 준비된 도약을 실행하게 했다. 대상이 비활성화되거나 다른 대상으로 바뀌는 경우의 취소는 유지한다.
- `DefenseBeastLeapAttack.CanAttack`은 공격 시작 시 `_attackRange`를 확인하고, 도약 목표 계산은 전조 시작 방향의 수평 축을 기준으로 한다. 전조 중과 도약 중 뒤로 물러난 거리만 추적하며 옆 방향 위치 변화는 따라가지 않는다.
- 도약 중 현재 플레이어 위치보다 기본 10m 더 나아간 지점을 착지 목표로 삼는다. 뒤로 계속 이동해도 축을 따라 목표를 갱신하고, 유효한 NavMesh 목표가 없을 때는 플레이어 위치의 축 투영점을 후보로 사용한다.
- 도약 매 프레임 비행 경로를 캡슐 검사한다. 플레이어와 접촉하면 그 시점에 방어 판정을 한 번 처리하고, 착지 시 중복 피해를 막는다. Guard·NormalParry·PerfectParry는 접촉 위치에서 0.25초 반동해 시작 위치로 돌아가고, 적중·회피·빗나감은 최소 6m 반대편으로 이동한다. 이미 10m 추월해 착지했다면 다시 플레이어 쪽으로 되돌아가지 않는다.
- 도약 포물선의 최고 높이를 3m에서 1.5m로 낮췄다. `Assets/Resources/Prefabs/Player.prefab`의 CharacterController 높이 2m보다 낮게 설정했다.
- Guard·NormalParry·PerfectParry 접촉 시 전진 도약을 즉시 멈추고, 플레이어의 `PlayerMotor.ViewForward` 방향 4m 지점으로 0.25초 동안 반동 이동한다. 목적지 NavMesh 샘플 또는 직선 경로 검사가 실패하면 도약 시작 위치로 복귀한다. Invulnerable과 DodgeAvoided는 기존 결과 동작을 유지한다.
- 확인: 스크립트·프리팹·명세 변경을 반영했다. Unity 컴파일 및 실제 전투 Play 검증은 미확인이다.

## 2026-10-18 RainyTwinBoss 일반 Beast 사망 으르렁거림

- 기존 깨갱 세 파일을 `123090__stephensaldanha__monster_growl.wav`와 `416862__tbsounddesigns__beargrowl_05.wav`로 교체했다. 각 원본의 전체 2.117초·1.645초를 사용하고 양끝에 0.01초 페이드를 적용한 모노 24비트 WAV를 `FMODProject/week06-blindblade/Assets/DefenseBeast/Death/`에 저장했다.
- 두 파일의 A 가중 평균 음량을 각각 -32dBFS로 맞췄다. 원본 대비 Monster는 -2.43dB, Bear는 +12.42dB이며 출력 피크는 각각 -2.63dBFS, -1.90dBFS로 클리핑하지 않는다. FMOD `event:/Enemy/DefenseBeastDie`의 Multi Instrument가 두 파일 중 하나를 셔플 재생한다.
- 으르렁거림 원본이 이미 낮은 음역이므로 깨갱용 FMOD -6반음 피치를 해제해 0반음으로 돌렸다. 이벤트 길이는 2.2초이며, `SoundLibrary.asset`의 `SoundId.DefenseBeastDie=47` 음량 배율은 1이다. 이전 0.1990536 배율은 깨갱 원본에 맞춘 설정이므로 사용하지 않는다.
- 사망음은 `EnemyDie`와 같은 FMOD 3D Spatializer 기본 최소·최대 거리 1~20m를 사용하며, 별도 2D 거리 보정은 꺼져 있다. SFX 버스·뱅크와 RainyTwinBoss의 비 속 적 소리 필터를 따른다.
- `RainyTwinBoss`의 `DefenseEnemySpawner._beastDeathSound`만 47로 지정했다. 스폰 시 `EnemyDamageAudio.ConfigureDeathSound`가 해당 Beast에 전용 사망음을 설정한다. 다른 씬의 일반 적, 일반 Beast 및 TwinBoss의 사망음은 기존 연결을 사용한다.
- FMOD Desktop 뱅크 빌드·프로젝트 진단과 이벤트 GUID·씬·Library 연결을 확인했다. Unity 배치 실행은 로컬 라이선스 서비스 연결 단계에서 종료되어 실제 전투의 재생 선택·음량·거리감은 미확인이다.

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| `Assets/Scripts/Defense/DefenseEnemySpawner.cs` | `DefenseEnemySpawner.CreateMelee` | 이 씬의 Beast 인스턴스에 사망음 ID를 설정 |
| `Assets/Scripts/SoundSystem/EnemyDamageAudio.cs` | `ConfigureDeathSound`, `HandleDeath` | 전용 사망음 저장 및 사망 위치 재생 |
| `Assets/Scripts/Managers/SoundManager.cs` | `IsEnemyOrProjectileSound` | 새 사망음을 비 속 적 사운드 필터 대상으로 분류 |
| `Assets/Resources/Sound/SoundLibrary.asset` | `SoundId.DefenseBeastDie` | FMOD 이벤트·거리 감쇠 설정 |
| `Assets/Scenes/Defense/RainyTwinBoss.unity` | `DefenseEnemySpawner` | 이 씬에만 ID 47 지정 |

## 2026-10-18 일반 Beast 도약음 분리

- 일반 Beast의 `DefenseBeastLeapAttack` 실행음은 기존 보스 `BeastBossAttack=29` 이벤트를 공유하되, 별도 `SoundId.DefenseBeastLeap=48`로 재생한다. `SoundLibrary.asset`에서 도약음의 피치 배율을 1.122462(+2반음)로 지정해 보스보다 조금 높인다. 보스의 29번 ID·피치·이벤트는 변경하지 않는다.
- `DefenseBeast.prefab`의 `_leapSound`만 29에서 48로 바꿨다. 기존 `EnemyAttackBehaviour.CommitAttack`이 도약 실행 직전에 `EnemySoundStage.Action`을 재생하므로 공격 시점과 취소·자연 종료 처리는 그대로다. 이 프리팹을 사용하는 씬의 일반 Beast에 적용된다.
- 48번의 보스 공격 이벤트 참조·피치 배율과 프리팹 연결을 확인했고 FMOD 프로젝트 진단이 통과했다. Unity 라이선스 서비스 연결 실패로 실제 전투에서의 도약음은 확인하지 못했다.
- 관련 코드와 설정: `Assets/Scripts/Enemies/Attack/DefenseBeastLeapAttack.cs` / `ResolveActionSound`, `Assets/Scripts/Enemies/Attack/EnemyAttackBehaviour.cs` / `CommitAttack`, `Assets/Scripts/Enums/SoundId.cs`, `Assets/Resources/Sound/SoundLibrary.asset`, `Assets/Resources/Defense/DefenseBeast.prefab`.

## 구현 및 확인 상태

- 2026-10-18 이후 구현 코드 목록은 최신 절의 표를 따른다.
- 구현 코드: Assets/Scripts/Enemies/Attack/DefenseBeastLeapAttack.cs
- 전투 연결: Assets/Scripts/Enemies/Attack/EnemyAttackBehaviour.cs, Assets/Scripts/Enemies/Attack/EnemyAttackContext.cs, Assets/Scripts/Player/KatanaV2/KatanaDamageReceiver.cs, Assets/Scripts/Player/KatanaV2/KatanaCombatCoordinator.cs
- 시각·감각 연결: Assets/Scripts/Enemies/DefenseBeastWolfVisual.cs, Assets/Scripts/Enemies/TwinBossWarningVisual.cs, Assets/Scripts/Footsteps/FootstepRipple.cs
- 연기·눈·몸체 선 처리: Assets/Scripts/Enemies/DefenseBeastSmokeShapeSource.cs, Assets/Scripts/Rendering/ScreenSpaceOutlineFeature.cs, Assets/Shaders/ScreenSpaceOutline.shader, Assets/Shaders/DefenseBeastEyeGlow.shader, Assets/Resources/Defense/DefenseBeastEyeGlow.mat, Assets/Resources/Defense/DefenseBeastSmokeShape.asset, Assets/Resources/Defense/DefenseBeast.prefab, Assets/Resources/Enemies/Visuals/EnemySmokeVisual.prefab
- `DefenseBeast.prefab`은 `DefenseBeastWolfVisual`과 `TwinBossWarningVisual`을 GUID로 연결했고 기존 Wolf 리그 참조와 Inspector 수치는 YAML 직렬화 데이터에서 보존했다.
- Unity Editor 재컴파일 후 스크립트 오류 0건과 ParticleSystem 두 개의 스킨 메시 방출 설정을 확인했다. 실제 게임 카메라의 최신 외형과 교전 중 프레임 비용은 확인하지 않았다.
- TwinBoss 프리팹은 계속 `TwinBeastWolfVisual`을 사용하며 일반 Beast 프리팹은 해당 컴포넌트를 사용하지 않는다.
- 실제 전투의 모든 도약 상태와 다양한 카메라 거리·해상도·성능은 미확인이다.
- 테스트 코드는 작성하지 않았다.

## 관련 명세

- 적 추적: 적 이동과 공격 접근 거리
- 적 공격 패턴: DefenseBeast 도약 공격의 준비·실행·복귀
- 전투 판정 계약: 근접 전방 방어와 도약 결과 처리
- 감각 표현: 도약 박동·확정 리플과 Wolf 시각 상태
- 쌍둥이 보스: 기존 Beast 공격 및 보스 동작은 범위 밖이며 변경하지 않음
## 2026-10-26 작은 늑대 공격 전조음 (최초 제작 기록, 위 0.8초 규칙 우선)

- 붉은 전조 시작에 `DefenseBeastWarning(52)`을 재생한다. `BossMonsterDog/Idle`의 1.655초 GrowlBreathAndRoar 음원만 1.2배속으로 사용하고 0.9초에서 끝낸다. 마지막 0.14초는 페이드한다. 보스의 기존 Idle 이벤트와 4.25초 선택지는 유지한다.
- 첫 박동 `EnemyHeartbeat(26)`은 공격 0.5초 전, 돌진 `DefenseBeastLeap(48)`은 공격 실행 시점이다. 전조 총 0.9초와 붉은 표시 타이밍은 그대로다. 설정은 `DefenseBeast.prefab`의 `DefenseBeastLeapAttack._telegraphStartSound`에 저장한다.
- FMOD 진단·Desktop 뱅크 빌드 성공. Unity Play 청취는 미확인이다.


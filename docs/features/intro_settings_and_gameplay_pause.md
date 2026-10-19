# 인트로 설정 팝업과 게임 일시정지

## 기능 설명

인트로 씬의 음량 버튼을 설정 버튼으로 표시하고, 누르면 화면 중앙에 설정 팝업을 연다. 팝업은 불투명도 60%의 검은 배경, 기존 음량 슬라이더, 마우스 감도 슬라이더, 조작 안내를 포함한다. 팝업 캔버스는 씬이 바뀌어도 유지되어 게임 중에도 같은 창을 연다.

음량을 바꾸면 선택한 값을 BGM 버스, 효과음 버스, Unity AudioListener에 적용한다. 효과음 버스를 통해 `ShurikenFlight` 소리를 짧게 미리 재생하므로 선택한 음량을 확인할 수 있다. 미리듣기가 재생되는 동안 현재 BGM을 일시정지하고, 미리듣기가 끝나거나 팝업이 닫히면 BGM을 이어서 재생한다. 마우스 감도는 PlayerPrefs에 저장하고 현재 `PlayerMotor`에 즉시 적용하며, 새로 생성된 플레이어도 저장값을 불러온다.

게임 중 ESC 또는 게임패드 Start를 누르면 `GameplayState.Paused`로 전환하고 전투 세션 시계·플레이어 행동·기존 효과음 재생을 멈춘 뒤 설정 팝업을 연다. 팝업에서 새로 재생하는 수리검 음량 미리듣기는 사용할 수 있고, 이때 BGM은 미리듣기가 끝날 때까지 멈춘다. 다시 ESC/Start를 누르거나 팝업의 닫기 버튼을 누르면 미리듣기와 BGM 일시정지를 정리하고 기존 효과음과 플레이어 행동·커서 상태를 복원한다. 게임 씬에 UI EventSystem이 없으면 New Input System UI 모듈을 포함한 EventSystem을 해당 씬에 생성한다.

게임 중 일시정지 설정 팝업에는 대나무 모양의 `게임 종료` 버튼을 표시한다. 이 버튼은 절단 연출이 끝난 뒤 미리듣기와 설정 저장을 정리하고 앱을 종료하며, Unity Editor에서는 Play Mode를 종료한다. 인트로에서 여는 설정 팝업에서는 종료 버튼을 숨긴다.

스테이지 씬을 직접 실행해 인트로의 팝업 초기화를 거치지 않은 경우에도 첫 ESC/Start 입력에서 Resources의 팝업 프리팹을 생성한다. 저장된 음량·감도와 슬라이더 입력을 연결한 뒤 일시정지하고, 닫기와 게임 종료 버튼을 표시한다. 인트로 메뉴 참조가 없는 실행에서는 빈 버튼 배열을 사용한다.

## 설정값

| 설정 | 저장 키 | 기본값 / 범위 |
| --- | --- | --- |
| 전체 음량 | `IntroMenu.Volume` | 1.0, 0–1 |
| 마우스 감도 | `GameSettings.MouseSensitivity` | 0.12, 0.02–0.5 |

## 관련 코드

| 파일 | 역할 |
| --- | --- |
| `Assets/Scripts/UI/VolumeControlUI.cs` | 인트로 버튼과 기존 음량 슬라이더를 공용 팝업에 연결한다. |
| `Assets/Scripts/UI/GameSettingsPopupUI.cs` | 영속 팝업, 설정 적용, 수리검 미리듣기, 재개 처리를 담당한다. |
| `Assets/Resources/Prefabs/UI/Intro/GameSettingsPopup.prefab` | 닫기 버튼과 게임 중에만 표시하는 대나무 게임 종료 버튼을 팝업에 연결한다. |
| `Assets/Scripts/GameManagers/InputManager.cs` | ESC/Start 입력을 일시정지와 재개로 전달한다. |
| `Assets/Scripts/GameManagers/GameStateManager.cs` | Paused 상태와 게임 시간·커서 복구를 관리한다. |
| `Assets/Scripts/Managers/SoundManager.cs` | 재생 중인 효과음·파일 스트림과 BGM의 일시정지·재개를 관리한다. |
| `Assets/Scripts/UI/IntroTitleBgm.cs` | IntroScene에서 타이틀 BGM을 시작하고 씬 이탈 시 정지한다. |
| `Assets/Scripts/Player/PlayerMotor.cs` | 저장된 마우스 감도를 불러와 적용한다. |

## Unity 연결

인트로 씬 `VolumeControlUI`에서 설정 버튼과 음량 슬라이더를 공용 팝업에 연결한다. `GameSettingsPopup.prefab`은 팝업 배경, 카드, 설정 항목, 조작 안내, 닫기 버튼과 게임 종료 버튼을 포함한다. `GameSettingsPopupUI`는 `SettingsQuitButton`을 전투 중에만 활성화하고, 버튼을 누르면 `IntroBambooButtonSliceEffect`의 완료 콜백 뒤 종료 처리를 실행한다. 기본 진입에서는 인트로가 팝업을 준비하고, 스테이지 직접 실행에서는 `TryOpenForGameplay`가 `Resources/Prefabs/UI/Intro/GameSettingsPopup.prefab`을 생성해 `Configure`로 초기화한다. 이미 존재하는 영속 팝업은 재사용한다. `IntroTitleBgm`은 FMOD RuntimeManager 초기화를 기다린 다음 `Music/TitleBgm.mp3`를 반복 재생하고 IntroScene을 벗어날 때 정지한다.

## 검증

2026-10-19 BambooStage1 직접 실행에서 영속 팝업이 없고 `TryOpenForGameplay=false`여서 ESC가 커서만 해제하는 문제를 재현했다. 팝업 자동 생성과 빈 메뉴 배열 초기화를 추가했다.

- Unity Editor 재컴파일 완료: `isCompiling=false`, `scriptCompilationFailed=false`.
- BambooStage1 Play Mode에서 팝업 자동 생성, `Paused` 전환, 활성화된 `게임 종료` 라벨과 버튼을 확인했다. 오버레이를 포함한 화면 캡처에서도 카드 하단의 닫기·게임 종료 표시를 확인했다.
- Input System에 ESC 누름·해제를 전달해 `Playing / 팝업 숨김`과 `Paused / 팝업 표시` 전환을 확인했다.
- 인트로 방식으로 열 때 종료 버튼 숨김, 게임 중 다시 열 때 종료 버튼 표시, 팝업 인스턴스 1개 유지를 확인했다.
- 종료 버튼 클릭 직후 절단 연출 중 Play Mode 유지와 버튼 비활성화를 확인했고, 연출 후 Play Mode 종료를 확인했다. 원래 BambooStage1 편집 상태로 복귀했다.
- 실제 빌드 프로세스 종료는 미확인이다.

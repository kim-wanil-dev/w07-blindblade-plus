# 인트로 설정 팝업과 게임 일시정지

## 기능 설명

인트로 씬의 음량 버튼을 설정 버튼으로 표시하고, 누르면 화면 중앙에 설정 팝업을 연다. 팝업은 불투명도 60%의 검은 배경, 기존 음량 슬라이더, 마우스 감도 슬라이더, 조작 안내를 포함한다. 팝업 캔버스는 씬이 바뀌어도 유지되어 게임 중에도 같은 창을 연다.

음량을 바꾸면 선택한 값을 BGM 버스, 효과음 버스, Unity AudioListener에 적용한다. 효과음 버스를 통해 `ShurikenFlight` 소리를 짧게 미리 재생하므로 선택한 음량을 확인할 수 있다. 미리듣기가 재생되는 동안 현재 BGM을 일시정지하고, 미리듣기가 끝나거나 팝업이 닫히면 BGM을 이어서 재생한다. 마우스 감도는 PlayerPrefs에 저장하고 현재 `PlayerMotor`에 즉시 적용하며, 새로 생성된 플레이어도 저장값을 불러온다.

게임 중 ESC 또는 게임패드 Start를 누르면 `GameplayState.Paused`로 전환하고 전투 세션 시계·플레이어 행동·기존 효과음 재생을 멈춘 뒤 설정 팝업을 연다. 팝업에서 새로 재생하는 수리검 음량 미리듣기는 사용할 수 있고, 이때 BGM은 미리듣기가 끝날 때까지 멈춘다. 다시 ESC/Start를 누르거나 팝업의 닫기 버튼을 누르면 미리듣기와 BGM 일시정지를 정리하고 기존 효과음과 플레이어 행동·커서 상태를 복원한다. 게임 씬에 UI EventSystem이 없으면 New Input System UI 모듈을 포함한 EventSystem을 해당 씬에 생성한다.

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
| `Assets/Scripts/GameManagers/InputManager.cs` | ESC/Start 입력을 일시정지와 재개로 전달한다. |
| `Assets/Scripts/GameManagers/GameStateManager.cs` | Paused 상태와 게임 시간·커서 복구를 관리한다. |
| `Assets/Scripts/Managers/SoundManager.cs` | 재생 중인 효과음·파일 스트림과 BGM의 일시정지·재개를 관리한다. |
| `Assets/Scripts/UI/IntroTitleBgm.cs` | IntroScene에서 타이틀 BGM을 시작하고 씬 이탈 시 정지한다. |
| `Assets/Scripts/Player/PlayerMotor.cs` | 저장된 마우스 감도를 불러와 적용한다. |

## Unity 연결

인트로 씬 `VolumeControlUI`의 기존 직렬화 참조에서 설정 버튼과 음량 슬라이더를 가져온다. 팝업 캔버스, 배경, 카드, 복제한 감도 슬라이더, 조작 안내, 닫기 버튼은 런타임에 생성한다. `IntroTitleBgm`은 FMOD RuntimeManager 초기화를 기다린 다음 `Music/TitleBgm.mp3`를 반복 재생하고 IntroScene을 벗어날 때 정지한다. 기본 게임 진입은 인트로 씬이므로 게임 씬으로 넘어가기 전에 영속 팝업이 준비된다.

## 검증

Unity Editor 재컴파일이 `completed`이며 `failed=false`, `errors=[]`다. Console에서 FMOD 초기화 이전에 타이틀 BGM 요청이 거부된 원인을 확인하고 대기 처리를 추가했다. 수정 후 실제 팝업 입력, BGM 반복, 미리듣기 중 정지·재개는 Play Mode에서 확인하지 않았다.

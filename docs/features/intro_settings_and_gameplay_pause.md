# 인트로 설정 팝업과 게임 일시정지

## 기능 설명

인트로 씬의 음량 버튼을 설정 버튼으로 표시하고, 누르면 화면 중앙에 설정 팝업을 연다. 팝업은 불투명도 60%의 검은 배경, 기존 음량 슬라이더, 마우스 감도 슬라이더, 조작 안내를 포함한다. 팝업 캔버스는 씬이 바뀌어도 유지되어 게임 중에도 같은 창을 연다.

음량을 바꾸면 선택한 값을 BGM 버스, 효과음 버스, Unity AudioListener에 적용한다. 효과음 버스를 통해 `ShurikenFlight` 소리를 짧게 미리 재생하므로 선택한 음량을 확인할 수 있다. 마우스 감도는 PlayerPrefs에 저장하고 현재 `PlayerMotor`에 즉시 적용하며, 새로 생성된 플레이어도 저장값을 불러온다.

게임 중 ESC 또는 게임패드 Start를 누르면 `GameplayState.Paused`로 전환하고 전투 세션 시계와 플레이어 행동을 멈춘 뒤 설정 팝업을 연다. 다시 ESC/Start를 누르거나 팝업의 닫기 버튼을 누르면 플레이어 행동 상태와 커서 상태를 복원한다. 게임 씬에 UI EventSystem이 없으면 New Input System UI 모듈을 포함한 EventSystem을 해당 씬에 생성한다.

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
| `Assets/Scripts/Player/PlayerMotor.cs` | 저장된 마우스 감도를 불러와 적용한다. |

## Unity 연결

인트로 씬 `VolumeControlUI`의 기존 직렬화 참조에서 설정 버튼과 음량 슬라이더를 가져온다. 팝업 캔버스, 배경, 카드, 복제한 감도 슬라이더, 조작 안내, 닫기 버튼은 런타임에 생성한다. 기본 게임 진입은 인트로 씬이므로 게임 씬으로 넘어가기 전에 영속 팝업이 준비된다.

## 검증

Unity Editor 스크립트 재컴파일이 완료됐고 현재 컴파일 실패가 없다. Play Mode에서 실제 팝업 입력과 최종 화면 배치는 아직 시각적으로 확인하지 않았다.

# 공용 TMP 폰트

### 공용 TMP 폰트

- 기능 설명: UI에서 함께 쓰는 조선100년체 TMP SDF 에셋의 글자 간격을 원본 OTF와 일치시킨다.
- 동작 조건 및 처리 흐름: `Assets/Fonts/ChosunCentennial_otf SDF.asset`을 쓰는 TMP 텍스트는 에셋의 글리프 쌍 간격을 따른다. 에셋 글리프는 21pt인데 커닝 쌍 값은 90pt로 환산돼 있었으므로, 각 값을 원본 디자인 단위에서 21pt로 다시 계산했다. 폰트 파일, 글리프 아틀라스, 글리프 메트릭과 에셋 GUID는 유지한다.
- 구현 상태: 커닝 550쌍의 첫 번째 `m_XAdvance`를 21/90 배율로 교정했다. 550쌍 모두 90pt 기준의 정수 디자인 단위와 일치함을 확인한 뒤 수정했다. 온보딩의 임시 커닝 해제를 제거해 공용 폰트의 정상 커닝을 사용한다.

#### 관련 코드

| 파일 경로 | 클래스 / 메서드 | 역할 |
| --- | --- | --- |
| `Assets/Fonts/ChosunCentennial_otf.otf` | 원본 OTF / GPOS | 커닝 디자인 값의 기준 |
| `Assets/Fonts/ChosunCentennial_otf SDF.asset` | `TMP_FontAsset` / `m_GlyphPairAdjustmentRecords` | 21pt 글리프와 일치하도록 교정한 공용 커닝 쌍 |
| `Assets/Scripts/Onboarding/OnboardingHud.cs` | `OnboardingHud.CreateText` | 별도 커닝 해제 없이 공용 폰트 사용 |

#### 연결 및 협업

- 연결된 기능: [온보딩](온보딩.md), 공용 에셋을 참조하는 다른 TMP UI.
- 공유하는 코드 및 상태: 같은 GUID의 TMP SDF 에셋을 참조하는 모든 화면에 교정값이 적용된다.
- 중복 또는 충돌 사항: 에셋의 커닝 쌍 외 데이터와 원본 OTF는 수정하지 않았다. 기존 씬의 텍스트 위치는 폰트 간격 변화로 달라질 수 있다.
- 승인 필요 사항 및 결과: 사용자가 공용 폰트의 문제 구간도 수정하도록 요청했다.

#### 확인 결과

- 원본 OTF와 공용 SDF 에셋에 함께 있는 커닝 530쌍은 모두 90pt 환산값과 일치했다. SDF 에셋에만 있는 20쌍을 포함한 550쌍 모두 90pt 환산 정수 단위에 맞아 21pt 값으로 교정했다.
- `WA` 쌍의 XAdvance는 −10.35에서 −2.415로 바뀐다. 글리프 쌍 구역 외의 에셋 바이트와 원본 OTF는 보존했다.
- Unity 6000.3.22f1 격리 프로젝트에서 교정한 에셋의 NativeFormatImporter 임포트와 스크립트 컴파일이 정상 종료됐다. C# 오류 0건이며 기존 `KatanaDebugOverlay._showCombatFeedback` 미사용 경고만 남았다.
- 실제 게임 화면에서 `WASD`와 다른 글자 쌍의 시각적 간격 확인: 미확인.
- 테스트 코드는 작성하지 않았다.

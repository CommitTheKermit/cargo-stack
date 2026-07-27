# cargo-stack

짐을 트럭 짐칸에 쌓고 시작 버튼을 누르면, 차가 정해진 경로를 자동 주행한다.
짐을 떨어뜨리지 않고 목적지에 도달하면 승리하는 물리 기반 적재 퍼즐이다.

폴리브릿지처럼 **3D 로우폴리 그래픽 + 측면 카메라**를 쓰되, 게임플레이는 한 평면에서만 일어난다.
플레이어는 운전하지 않는다. 실력 요소는 전부 적재 단계에 있다.

기획서: [`docs/game-design.md`](docs/game-design.md)

## 개발 환경

- Unity `6000.5.4f1` (Built-in 렌더 파이프라인, 레거시 Input)
- 플랫폼: PC (itch.io / Steam)
- 팀: 3명, 전원 Unity 초급

Unity Hub에서 저장소 루트를 프로젝트로 추가하고 위 버전으로 연다. 다른 Editor 버전으로 업그레이드하지 않는다.

## 실행 방법

1. Unity Hub에서 저장소 루트를 Unity `6000.5.4f1`로 연다.
2. `Assets/Scenes/Prototype.unity` 씬을 연다.
3. Play 버튼을 누른다.

### 조작

| 입력 | 동작 |
|---|---|
| 좌클릭 | 짐 집기 / 놓기 |
| `R` | 들고 있는 짐 90도 회전 |
| `Space` | 적재 완료, 출발 |
| `Backspace` | 재시작 |

들고 있는 짐이 다른 물체와 겹치면 빨갛게 표시된다.
화면 왼쪽 위 HUD의 슬라이더로 짐칸/짐 마찰을 주행 중에도 바꿀 수 있다.

## 현재 범위 (MVP 검증판)

핵심 두 가지, **짐 쌓기**와 **트럭 이동**이 재미있는지만 확인한다.

- 짐: 일반 상자 3개
- 경로: 평지 → 오르막 → 평지, 중간에 급제동 구간 1회
- 고정 장비(로프·쐐기 등), 원통·유리 등 나머지 짐 속성, 스테이지 선택, 점수 UI는 아직 없다

검증 질문 세 가지:

1. 적재 단계에서 고민이 생기는가? (배치 정답이 하나뿐이면 실패)
2. 주행 관전이 긴장되는가? (결과가 뻔하면 실패)
3. 실패했을 때 다시 하고 싶은가?

### 현재 난이도 기준값

PlayMode 테스트가 남기는 로그 기준이다. 마찰이나 속도 프로필을 바꾸면 이 값이 움직인다.

| 배치 | 생존 |
|---|---|
| 나란히 (무게중심 낮게) | 3 / 3 |
| 높이 쌓기 (탑) | 2 / 3 |

배치가 결과를 바꾼다는 전제는 성립한다. 다만 "나란히"가 항상 안전하므로,
안전한 배치에도 긴장이 생기게 하려면 경로를 험하게 하거나 마찰을 낮춰야 한다.

## 테스트

```bash
/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath . -runTests -testPlatform PlayMode \
  -testResults /tmp/cargo-stack-tests.xml -logFile /tmp/cargo-stack-tests.log
```

`Assets/Tests/PlayMode/CoreLoopTests.cs`가 "마찰만으로 짐이 실려 간다"는 전제를 회귀 테스트로 고정한다.
이 테스트가 깨지면 물리 파라미터가 게임을 성립하지 않게 바꾼 것이다.

## 구조

```
Assets/
  Scripts/
    Core/     GameFlow(상태 머신), GameState
    Vehicle/  TruckMover(자동 주행 + 지면 추종)
    Cargo/    Cargo(짐), CargoPlacer(핵심 조작), CargoTracker(낙하 판정)
    View/     CameraRig(짐칸 확대 ↔ 측면 와이드 전환)
    Debug/    PrototypeHud(임시 HUD + 마찰 튜닝)
  Editor/
    PrototypeSceneBuilder.cs
```

`GameFlow`가 등뼈다. 적재 → 주행 → 결과 전환이 모두 여기를 거치고,
다른 시스템끼리는 서로를 직접 참조하지 않는다.

### 씬을 손으로 고치지 않는다

씬 파일은 git 자동 병합이 되지 않아 두 사람이 같은 씬을 고치면 한쪽 작업이 통째로 날아간다.
그래서 프로토타입 씬은 코드로 생성한다.

- 레벨/배치를 바꾸려면 `Assets/Editor/PrototypeSceneBuilder.cs`를 고친다
- Unity 메뉴 `CargoStack > 프로토타입 씬 다시 만들기`를 실행하면 씬이 새로 만들어진다

## 협업 규칙

1. **씬마다 주인 한 명**. 다른 사람은 씬을 직접 고치지 않고 프리팹으로 기여한다.
2. **시스템 경계 지키기**. 시스템끼리 직접 참조하는 대신 `GameFlow`를 거친다.
3. **주 2회 정기 통합**. 마지막 주에 몰아서 합치지 않는다.
4. 새 작업은 새 브랜치(`feat/*`, `fix/*`)에서 시작한다.

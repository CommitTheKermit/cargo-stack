# 에이전트 작업 규칙

이 저장소에서 AI 코딩 도구(Claude Code, Codex 등)가 지켜야 할 운영 규칙이다.
사람 팀원의 협업 규칙은 [`README.md`](README.md)의 "협업 규칙" 절에 있다.

**여러 도구가 동시에 이 저장소를 만진다.** 원격에 `codex/*` 브랜치와 `feat/*` 브랜치가
함께 있는 것이 그 흔적이다. 아래 규칙은 대부분 그 동시성 때문에 필요하다.

## 1. 새 작업은 worktree 에서 시작한다 (필수)

작업 트리를 공유하면 사고가 난다. 실제로 한 세션에서 세 번 섞였다.
오디오 작업, 짐칸 뒷문 작업, 트럭 FBX 편집이 로프 작업 브랜치로 딸려 들어왔다.

원인은 두 가지다.

- `git checkout -b` 는 **미커밋 변경을 그대로 데리고** 브랜치를 옮긴다. 남이 작업 중인
  파일이 내 브랜치에 얹힌다.
- 이 프로젝트의 씬은 재생성물이다. `PrototypeSceneBuilder` 가 씬을 다시 만들면 그 시점
  작업 트리에 있는 **모든** 변경이 씬 파일에 함께 구워진다. 분리할 방법이 없다.

그래서 브랜치만 나누는 것으로는 부족하고, 디렉토리를 나눠야 한다.

```bash
git worktree add ../cargo-stack-<작업이름> -b feat/<작업이름> origin/main

# Unity Library 는 .gitignore 대상이라 worktree 마다 새로 만들어진다(현재 약 184MB).
# 기존 것을 복사해 두면 첫 임포트가 짧아진다.
cp -R Library ../cargo-stack-<작업이름>/Library
```

Claude Code 는 `EnterWorktree` 도구로 `.claude/worktrees/` 아래에 만든다.

작업을 마치면 정리한다.

```bash
git worktree remove ../cargo-stack-<작업이름>
```

## 2. 착수 전에 작업 트리가 깨끗한지 확인한다

worktree 를 쓰지 못하는 상황이라면 최소한 이것만은 지킨다.

```bash
git status --short
```

**남의 미커밋 변경이 보이면 거기서 멈추고 사용자에게 알린다.** 그대로 진행해서 커밋에
섞은 뒤에 분리하려면 `git stash push -- <파일>` 로 내 변경만 빼내고 씬을 다시 만들어야
하는데, 이 작업은 번거롭고 실수하면 남의 작업이 사라진다.

## 3. 씬 파일은 병합하지 말고 재생성한다

`Assets/Scenes/*.unity` 는 git 자동 병합이 되지 않는다. 두 worktree 가 각자 씬을 만들면
반드시 충돌한다. 그때는 **씬 빌더 코드만 병합하고 씬은 버린 뒤 다시 만든다.**

```bash
git checkout --ours Assets/Scenes   # 또는 --theirs. 어느 쪽이든 버려질 파일이다
# 코드 병합을 마친 뒤
# Unity 메뉴: CargoStack > 스테이지 > 모든 씬 다시 만들기
```

씬을 손으로 고치지 않는다는 README 원칙의 연장이다. 씬은 코드의 산출물이므로
분쟁의 대상이 아니다.

## 4. 커밋은 작업 단위로 가른다

한 커밋에 두 작업이 섞이면 롤백할 때 하나만 되돌릴 수 없다.
어쩔 수 없이 섞였다면 남의 작업을 **먼저 별도 커밋으로** 남기고 그 위에 내 작업을 올린다.
README 같은 문서 파일은 hunk 단위로 갈라 각자의 커밋에 넣는다.

커밋 메시지 형식은 Conventional Commits 를 따르고 요약과 본문은 한글로 쓴다.

## 5. Unity 는 한 프로젝트를 한 인스턴스만 연다

에디터가 열려 있으면 배치모드 테스트가 이렇게 막힌다.

```
Aborting batchmode due to fatal error:
It looks like another Unity instance is running with this project open.
```

worktree 는 경로가 다르므로 이 제약을 함께 해소한다. 같은 경로에서 작업해야 한다면
테스트를 돌리기 전에 에디터를 닫아야 하고, 그건 사용자에게 요청할 일이다.

## 6. 물리를 만졌으면 테스트로 재고 수치를 남긴다

이 게임은 물리 튜닝이 재미의 대부분이라, 눈으로 본 인상만으로 판단하면 퇴행을 놓친다.

```bash
/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath . -runTests -testPlatform PlayMode \
  -testResults /tmp/cargo-stack-tests.xml -logFile /tmp/cargo-stack-tests.log
grep -a "\[CargoStack\]" /tmp/cargo-stack-tests.log
```

`[CargoStack]` 로그가 난이도 기준값이다. 값이 움직였으면 README 의 기준값 표도 함께 고친다.

**정지 상태에서만 재지 않는다.** 로프가 그 함정에 빠졌다. 정지 상태에서 안정을 돕던
설정이 주행 중에는 정반대로 작용해, 테스트를 다 통과하고도 실제 플레이에서 짐이
전부 쏟아졌다. 주행 중에 재는 테스트를 반드시 함께 둔다.

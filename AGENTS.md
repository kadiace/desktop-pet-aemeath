# Aemeath Companion 작업 지침 (AGENTS.md)

이 저장소는 **Windows 데스크탑 컴패니언 앱**(`src/AemeathCompanion`, C# .NET 10 WPF)과 **내부 LAN 배포 서버**(`server/release-server.mjs`, Node.js)를 함께 관리한다.

---

## 1. 릴리즈 · 태그 불변성 규칙 (절대 준수)

1. **이미 배포된 버전/태그는 절대 덮어쓰거나 이동하지 않는다.**
   - 이미 생성·배포된 Git 태그(`vX.Y.Z`)를 삭제 후 재생성(`git tag -d` / `git tag -f`)하거나, 기존 `releases/AemeathCompanion-vX.Y.Z.zip`을 덮어쓰지 않는다.
   - 이미 해당 버전을 설치한 팀원은 `CurrentVersion == LatestVersion`으로 판정되어 수정본을 영원히 업데이트받지 못하게 된다.
2. **컴패니언 앱(`src/AemeathCompanion/`, `Assets/`) 변경 시 반드시 새 버전으로 올린다.**
   - 앱 코드나 에셋이 1줄이라도 바뀌었다면 `<Version>`을 다음 패치/마이너 버전(예: `0.0.4` → `0.0.5`)으로 올리고, 테스트의 버전 검증도 함께 갱신한 뒤 새 태그로 배포한다.
3. **서버/스크립트/문서만 변경된 경우 앱 태그를 올리지 않는다.**
   - `server/`, `scripts/`, `README.md`, `AGENTS.md` 등 컴패니언 앱 바이너리에 영향이 없는 변경은 새 앱 릴리즈 태그를 생성하지 않는다.
4. **반드시 커밋 완료 후 태그를 생성한다.**
   - 워킹 트리에 커밋되지 않은 변경 사항이 남아 있는 상태에서 태그를 생성하지 않는다(`git status --porcelain`이 완전히 비어 있어야 함).
   - 릴리즈 태그는 반드시 annotated tag(`git tag -a vX.Y.Z -m "릴리즈 노트"`)로 생성한다.
   - 원격 저장소(`origin`) 푸시는 사용자가 명시적으로 요청할 때만 수행한다.

---

## 2. 핵심 UX · 상태 머신 원칙

1. **유저 액션 최우선 원칙**
   - 언제나 유저의 직접 조작(클릭, 드래그, 우클릭 메뉴 선택)이 자동 상태 전환(SMTC 음악 감지 등)보다 최우선이다.
   - **Platform** 모드 중에는 음악이 시작되어도 자동으로 **Music** 모드로 전환하지 않고 상단에 가로형 미니 플레이어(`PlatformMiniPlayerWindow`)만 노출한다(더블클릭 또는 우클릭 `♪ MUSIC` 등 유저 명시 선택 시에만 풀 Music 모드 진입).
   - 음악 재생 중 우클릭 메뉴(또는 트레이)에서 유저가 직접 **Roam** 모드를 선택하면 풀 Music 모드로 덮어쓰지 않고 **Roam** 모드를 유지하면서 상단에 가로형 미니 플레이어(`PlatformMiniPlayerWindow`)를 노출한다.
   - 재생 중인 **Music** 모드에서 좌클릭은 1회 탄성 애니메이션만 재생하고 모드를 유지하며, **드래그**해도 음악 모드와 말풍선을 유지한 채 이동한다. **더블클릭** 시 **Platform** 모드로 전환(미니 플레이어 유지)하고, 메뉴에서 **Roam / Platform**을 선택하거나 일시정지 홀드 상태에서 클릭·드래그하면 즉시 재생 모드를 해제하며 같은 곡이 계속 재생 중이어도 자동으로 다시 뮤직 모드로 덮어쓰지 않는다.
   - 드래그 도중에는 자동 이벤트가 드래그를 중단시키지 않는다.
2. **표현 및 모드 규칙**
   - UI·트레이·문서에서 `"Mode A"` / `"Mode B"` 표기를 사용하지 않고 **`ROAM` / `PLATFORM` / `MUSIC`**으로 통일한다.
   - Platform 모드에서는 시간 경과에 따른 자동 랜덤 스프라이트 교체를 하지 않는다(직접 클릭·메뉴 선택으로만 변경).
   - Platform 모드에서 드래그할 때는 `drag.gif`로 바꾸지 않고 현재 `screen` 스프라이트를 그대로 유지한다.
   - 더블클릭 판정은 Windows 표준(`GetDoubleClickTime()`, `SM_CXDOUBLECLK × SM_CYDOUBLECLK`, 1번째 Down → 2번째 Down)을 따른다.
   - 클릭 피드백(`BoingAnimator`)은 접지면 피벗 기반의 부피 보존 스쿼시 앤 스트레치 **1회 반동**으로 유지한다.
3. **스프라이트 크기 및 착지 기준선 (`CompanionController.GetSpriteMetrics`)**
   - `screen2`, `screen4` (`200×200`): `BottomPadDip = 0`
   - `screen1`, `screen3`, `screen5`, `screen6`, `screen7` (`200×200`): `BottomPadDip = 4` (양손이 창 모서리에 4px 걸침)
   - `screen8` (`300×200`): `BottomPadDip = 15` (물범 중앙~우측 몸통 하단 라인 기준)
   - `idle1~4`, `move`, `drag`, `aemeath` (`200×200`): `BottomPadDip = 0`

---

## 3. 프로젝트 구조 및 내부 배포 서버

- `src/AemeathCompanion/`: .NET 10 WPF 데스크탑 컴패니언 앱 (`net10.0-windows10.0.19041.0`)
  - `Native/`: Win32/DWM 최상단·창 플랫폼 스캐너, WASAPI Loopback FFT, `HKCU\...\Run` 자동 시작, 로컬 개발 전용 종료 제어(`DevLifecycleControl`, `#if DEBUG` + 로컬 저장소 `bin/` 경로 이중 가드)
  - `Media/`: WinRT SMTC 구독 및 음악 전용 세션 필터(`MusicSessionFilter`)
  - `Engine/`: GIF 프레임 디코더, 상태 머신, 1회 클릭 탄성 애니메이터(`BoingAnimator`)
  - `UI/`: 도트 말풍선(`MusicBubbleControl`), Platform 가로형 미니 플레이어(`PlatformMiniPlayerControl`), 우클릭 컨트롤 메뉴, 트레이 아이콘
  - `Update/`: 내부 배포 서버 버전 조회 및 SHA-256 검증 자동 업데이터(`AppUpdateService`)
- `server/release-server.mjs`: LAN 배포 포털 및 버전 API (기본 `http://172.21.10.15:17515/`)
  - Lightweight 태그(`objecttype !== 'tag'`)는 커밋 메시지를 노출하지 않고 릴리즈 노트를 `"-"`로 표시한다.
  - SSE(`/api/events`) + 웹훅(`POST /api/webhook/refresh`) + 클라이언트 3초 폴백으로 새 태그/패키지 감지 시 자동 새로고침한다.
- `scripts/publish-release.ps1`: 버전 패키징 및 태그 생성 스크립트 (`releases/`는 `.gitignore` 대상)
- `scripts/dev-control.ps1`: 로컬 개발용 실행 중 앱 종료 / 빌드 / 재실행 요청 스크립트 (`-Action Status|Stop|Build|Start|RequestStart|Restart`)
- `scripts/dev-watch.ps1`: 사용자 데스크탑 터미널(`WinSta0\Default`)에서 1회 실행해 두는 로컬 개발 런처 워처 (`.dev-control/start.signal` 수신 시 최신 Debug 앱을 실제 사용자 데스크탑에 자동 실행)

---

## 4. 검증 및 로컬 개발 실행 절차

- C# 앱 코드(`src/AemeathCompanion/`, `Assets/`) 수정 시:
  1. 실행 중인 로컬 Debug 앱이 있으면 `.\scripts\dev-control.ps1 -Action Stop`으로 정상 종료한다.
  2. 반드시 빌드(경고 0개 / 오류 0개) 및 단위 테스트 전체 통과를 확인한다:
     ```powershell
     dotnet build --no-restore tests/AemeathCompanion.Tests/AemeathCompanion.Tests.csproj
     .\tests\AemeathCompanion.Tests\bin\Debug\net10.0-windows10.0.19041.0\AemeathCompanion.Tests.exe
     ```
  3. **작업이 끝나면 항상 최신 Debug 앱이 사용자 데스크탑에 켜져 있는 상태로 마무리한다**:
     - `.dev-control/watcher.json`이 존재하면 `.\scripts\dev-control.ps1 -Action RequestStart`로 데스크탑 워처(`dev-watch.ps1`)를 통해 실제 화면에 자동 재실행한다.
     - 에이전트 터미널(`exebox-*` 숨겨진 데스크탑)에서 직접 `AemeathCompanion.exe`를 띄우지 않는다(숨겨진 데스크탑에서 단일 인스턴스 뮤텍스를 선점해 사용자 화면 실행을 막기 때문). 워처가 꺼져 있다면 종료·빌드·테스트 완료 후 사용자에게 실행 안내한다.
- `server/release-server.mjs` 수정 시 `/api/version`, `/api/releases`, `/api/events`, `POST /api/webhook/refresh`, `/` HTML 렌더링 동작을 검증한다.

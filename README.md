# Aemeath, the Digital Ghost — Windows Desktop Companion

도트 GIF 스프라이트 15종(`aemeath`, `drag`, `idle1~4`, `move`, `screen1~8`)과 Windows 네이티브 API(Win32/DWM, WinRT SMTC, WASAPI Loopback)로 동작하는 **.NET 10 WPF Windows 데스크탑 컴패니언 앱**입니다.

---

## 1. 3가지 동작 모드

| 모드 | 설명 | 스프라이트 | 특징 |
| :--- | :--- | :--- | :--- |
| **ROAM** | 화면과 멀티 모니터 위를 자유롭게 돌아다니고 쉬는 기본 모드 | `idle1~4`, `move`, `drag` | • 일정 간격으로 배회·휴식·근처 커서 따라가기<br>• 음악 재생이 새로 시작되면 자동으로 **MUSIC** 모드 전환<br>• 음악 재생 중 우클릭 메뉴/트레이에서 **ROAM**을 직접 선택하면 **ROAM** 모드를 유지하면서 상단에 **가로형 미니 뮤직 플레이어** 노출(일시정지 시 클릭·드래그 즉시 숨김 / 5분 후 자동 숨김)<br>• 드래그 시 `drag` 스프라이트 표시 |
| **PLATFORM** | 중력이 적용되어 열려 있는 창 상단 테두리나 작업 표시줄에 올라앉는 모드 | `screen1~8` | • 일반 앱 창·작업 관리자(`Taskmgr`) 등의 **실제 보이는 상단 테두리**에 착지(앞 창에 가려진 구간 자동 제외)<br>• 착지한 창을 옮기면 함께 따라 이동, 창이 닫히거나 가려지면 아래 창/작업 표시줄로 낙하<br>• 시간 경과에 따른 자동 스프라이트 교체 없음(직접 클릭·메뉴로만 변경)<br>• 드래그 중에도 현재 `screen` 스프라이트 유지<br>• **음악 재생 시**: 풀 MUSIC 모드로 자동 전환되지 않고 머리 위(상단 공간 부족 시 좌/우 자동 회피)에 **가로형 미니 뮤직 플레이어**(좌측 1:1 앨범 자켓 + 우측 하단 반투명 오디오 스펙트럼 + 곡 정보/진행 바/재생 컨트롤) 노출. 재생 중에는 드래그 중에도 상시 유지되며, 일시정지 시 클릭·드래그 즉시 숨김 또는 5분 후 자동 숨김 |
| **MUSIC** | 현재 재생 중인 음악에 맞춰 도트 말풍선과 실시간 이퀄라이저를 띄우는 모드 | `aemeath` | • 전용 음악 앱(Spotify, 멜론, Apple Music, foobar2000 등)과 브라우저 음악 서비스(YouTube Music, Spotify Web, SoundCloud 등)만 인식하고 **게임·일반 유튜브 영상·방송·메신저 제외**<br>• WASAPI Loopback 24밴드 도트 블록 이퀄라이저, 앨범 아트(미제공 시 도트 디스크), 픽셀 폭 정밀 마퀴 스크롤, 진행률 바<br>• 말풍선 하단 **`⏮` · `▶/❚❚` · `⏭` 도트 버튼**으로 포커스를 뺏지 않고 바로 곡 제어(버튼 외 영역은 클릭 투과)<br>• 음악 일시정지 시 5분간 유지 후 자동 복귀 |

> **유저 액션 최우선 원칙**
> - 언제나 사용자의 직접 조작(클릭, 드래그, 우클릭 메뉴/트레이 선택)이 자동 상태 전환보다 최우선입니다.
> - **MUSIC** 모드 중 메뉴에서 **ROAM / PLATFORM**을 선택(또는 더블클릭으로 **PLATFORM** 전환)하면 즉시 재생 모드가 해제되며, 같은 곡이 계속 재생 중이어도 자동으로 다시 덮어쓰지 않습니다.
> - 음악이 재생 중인 상태에서 **PLATFORM → ROAM**으로 직접 전환해도 **ROAM** 모드가 우선 유지됩니다.
> - 드래그 도중에는 자동 이벤트가 드래그를 끊지 않습니다.

---

## 2. 마우스 조작 및 모드 전환 방법

| 입력 | 동작 |
| :--- | :--- |
| **좌클릭** | • 부피 보존 **1회 "뽀잉~" 탄성 애니메이션** 재생<br>• **ROAM**: `idle1~4` 변경 / **PLATFORM**: `screen1~8` 변경 (일시정지 상태 미니 플레이어 즉시 숨김)<br>• **MUSIC (재생 중)**: 탄성 반응만 재생하고 **MUSIC** 모드 유지 / **MUSIC (일시정지 유지 중)**: 일시정지 홀드 해제 후 **ROAM** 복귀 |
| **좌클릭 드래그** | • 화면 및 멀티 모니터 간 자유 이동<br>• **PLATFORM**: 놓는 순간 아래 창 상단/작업 표시줄로 중력 낙하 (음악 재생 중 미니 플레이어 상시 유지, 일시정지 중에는 드래그 시작 시 숨김)<br>• **MUSIC (재생 중)**: **MUSIC** 모드와 말풍선을 그대로 유지한 채 이동 / **MUSIC (일시정지 유지 중)**: 즉시 홀드 해제 후 **ROAM** 드래그 |
| **더블클릭** | • **ROAM ↔ PLATFORM** 전환 (Windows 표준 `GetDoubleClickTime()` 및 `SM_CXDOUBLECLK × SM_CYDOUBLECLK` 기준)<br>• **PLATFORM (음악 재생 중) → MUSIC** 전환 / **MUSIC → PLATFORM** 전환 (재생 중이면 미니 플레이어 자동 노출) |
| **우클릭** *(또는 650ms 길게 누르기)* | • Aemeath 도트 스타일 **컨트롤 메뉴** 열기 |

---

## 3. 우클릭 컨트롤 메뉴 구성

캐릭터를 우클릭하면 위에서 아래 순서로 다음 항목이 표시됩니다.

1. **상단 모드 전환 바 (`ROAM` / `PLATFORM` / `♪ MUSIC`)**
   - 현재 활성 모드가 강조 표시되며, 어느 상태에서든 원하는 모드로 즉시 전환할 수 있습니다.
   - **PLATFORM** 모드나 재생 모드 수동 해제 상태에서도 **`♪ MUSIC`** 버튼을 누르면 즉시 음악 모드로 진입합니다.
2. **음악 재생 컨트롤 패널**
   - 현재 감지된 음악의 **곡 제목**, **아티스트**, 실시간 **현재 재생 시간 / 전체 길이(`1:34 / 3:38`)** 표시
   - **`⏮ PREV`** · **`▶ PLAY / ❚❚ PAUSE`** · **`NEXT ⏭`** 버튼으로 미디어 제어
3. **애니메이션 / 포즈 선택 (`ANIMATION / POSE`)**
   - `idle1`~`idle4`, `screen1`~`screen8` 12종을 3×4 그리드로 직접 지정
   - 우측 **`AUTO`** 버튼으로 수동 고정 해제
4. **크기 조절 (`SCALE 0.25x ~ 2.00x`)**
   - 슬라이더로 `0.05x` 단위 실시간 크기 조절, **`1.00x`** 버튼으로 기본 크기 즉시 복귀
5. **창 제어 바 (`TOP: ON/OFF` / `CENTER` / `EXIT`)**
   - **`TOP: ON/OFF`**: 항상 최상단 유지(`WS_EX_TOPMOST` + 포그라운드 훅) 토글
   - **`CENTER`**: 주 모니터 중앙으로 캐릭터 위치 초기화
   - **`EXIT`**: 앱 종료 (현재 위치·모드·크기·스프라이트는 `%AppData%\AemeathCompanion\state.json`에 자동 저장)
6. **PC 시작 시 자동 켜기 (`ON` / `OFF`)**
   - 관리자 권한 없이 현재 사용자 Windows 시작 프로그램(`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`)에 자동 실행을 등록·해제합니다.
   - 앱 폴더를 옮기거나 업데이트해도 실행 시 경로를 자동 동기화합니다.
7. **버전 및 업데이트 (`vX.Y.Z` / `최신 확인` / `⬆ vX.Y.Z 업데이트`)**
   - 우클릭 메뉴를 열 때마다 내부 배포 서버(`http://172.21.10.15:17515/api/version`)에서 새 버전 여부를 자동으로 확인하며, 필요 시 **`최신 확인`** 버튼으로 수동 재조회할 수도 있습니다.
   - 더 새로운 버전이 있을 때만 **`⬆ vX.Y.Z 업데이트`** 버튼이 노출되며, 클릭 시 `.zip` 다운로드 → SHA-256 무결성 검증 → 파일 자동 교체 및 새 버전 재실행까지 한 번에 진행합니다.

### 시스템 트레이 아이콘 메뉴
작업 표시줄 트레이의 Aemeath 아이콘에서도 빠른 제어가 가능합니다.
- `Open Control Menu` (또는 트레이 아이콘 더블클릭)
- `Roam` / `Platform` / `Music`
- `Center on Primary Monitor` / `Reset Scale (1.00x)`
- `Toggle Always On Top` / `PC 시작 시 자동 켜기`
- `Exit Aemeath`

---

## 4. 빌드 및 테스트

```powershell
# 실행 (Release)
dotnet run -c Release --project src/AemeathCompanion/AemeathCompanion.csproj

# 또는 빌드된 실행 파일 직접 실행
.\src\AemeathCompanion\bin\Release\net10.0-windows10.0.19041.0\AemeathCompanion.exe

# 빌드 및 단위 테스트 실행
dotnet build --no-restore tests/AemeathCompanion.Tests/AemeathCompanion.Tests.csproj
.\tests\AemeathCompanion.Tests\bin\Debug\net10.0-windows10.0.19041.0\AemeathCompanion.Tests.exe

# 로컬 개발용 앱 종료 / 재빌드 / 재실행 (Debug + 로컬 저장소 bin/ 실행 시에만 동작, Release 배포본에는 미포함)
.\scripts\dev-control.ps1 -Action Status
.\scripts\dev-control.ps1 -Action Stop
.\scripts\dev-control.ps1 -Action Restart
# 또는 로컬 Debug 실행 파일로 직접 종료 신호 전송
.\src\AemeathCompanion\bin\Debug\net10.0-windows10.0.19041.0\AemeathCompanion.exe --shutdown
```

---

## 5. 내부 네트워크(LAN) 배포 서버 (`172.21.10.15:17515`)

### 배포 서버 실행
```powershell
# 기본 포트 17515로 내부 배포 포털 및 API 실행
.\scripts\start-release-server.ps1

# Windows 방화벽 인바운드 포트 허용 (최초 1회, 관리자 PowerShell)
New-NetFirewallRule -DisplayName "Aemeath Release Server" -Direction Inbound -Protocol TCP -LocalPort 17515 -Action Allow
```

- **웹 다운로드 포털 (`GET /`)**: 최상단 대형 최신 버전 다운로드 버튼 + 전체 Git 태그별 버전 목록·릴리즈 노트·다운로드 링크 제공. SSE(`/api/events`) + 3초 폴백으로 새 태그/패키지 배포 시 페이지가 자동 새로고침됩니다.
- **최신 버전 API (`GET /api/version`)**: 최신 Git 태그명, 버전, 릴리즈 노트, 다운로드 URL, SHA-256 JSON 반환
- **전체 목록 API (`GET /api/releases`)**: 전체 Git 태그 및 시그니처 JSON 반환
- **패키지 다운로드 (`GET /download/latest`, `GET /download/<tag>`)**: 릴리즈 `.zip` 스트리밍 다운로드
- **수동 새로고침 훅 (`POST /api/webhook/refresh`)**: 연결된 웹 포털에 즉시 새로고침 이벤트 전송

### 새 버전 배포 절차
> **주의**: 이미 배포된 버전/태그는 덮어쓰지 않고, 컴패니언 앱 변경 시 항상 새 버전으로 올려 커밋한 뒤 배포합니다(상세 규칙은 [AGENTS.md](AGENTS.md) 참고).

```powershell
# 1. src/AemeathCompanion/AemeathCompanion.csproj 의 <Version> 및 테스트 버전 갱신 후 커밋
# 2. 새 태그 생성 + releases/AemeathCompanion-vX.Y.Z.zip 패키징 + 웹 포털 자동 새로고침 알림
.\scripts\publish-release.ps1 -Version 0.0.5 -Notes "변경 사항 요약" -CreateTag

# (.NET 10 런타임 미설치 PC용 독립 실행 패키지가 필요할 때)
.\scripts\publish-release.ps1 -Version 0.0.5 -Notes "변경 사항 요약" -CreateTag -SelfContained
```

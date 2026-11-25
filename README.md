# Aemeath, Digital Ghost — Windows Desktop Companion

Wallpaper Engine 프로젝트(`aemeath,_digital`)의 동일한 GIF 스프라이트 14종을 사용하는 네이티브 Windows 데스크탑 컴패니언 앱(.NET 10 WPF)입니다.

## 주요 기능

- **항상 최상단 유지 (Topmost)**: `WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` + `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`로 포커스를 빼앗지 않으면서 항상 최상단에 표시됩니다.
- **State B 창 플랫폼 착지**: `EnumWindows` + `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` + Z-order 가시성 클리핑으로 일반 앱 창·작업 관리자(`Taskmgr`) 등의 보이는 상단 테두리에 착지하며, 창을 이동하면 함께 따라가고 닫히면 아래 플랫폼/작업 표시줄로 낙하합니다.
- **음악 전용 SMTC 필터링**: WinRT `GlobalSystemMediaTransportControlsSessionManager`를 직접 구독하여 전용 음악 앱 및 브라우저 음악 서비스(YouTube Music, Spotify Web, SoundCloud 등)만 음악 재생 모드로 전환하고, 게임·동영상·방송·메신저 세션은 무시합니다.
- **WASAPI Loopback 픽셀 이퀄라이저 & 도트 말풍선 UI**: GIF 스프라이트에서 추출한 공통 팔레트(사쿠라 핑크·베리 로즈·디지털 시안·라벤더·딥 플럼)와 2px 도트 프레임, 동적 픽셀 꼬리, 24밴드 도트 블록 스펙트럼, 정밀 마퀴 스크롤, 그리고 **이전곡(`⏮`) · 재생/일시정지(`▶`/`❚❚`) · 다음곡(`⏭`) 도트 컨트롤 버튼**(버튼 영역 외 클릭 투과 및 포커스 비탈취)을 제공합니다.

## 조작 방법

- **좌클릭**: 애니메이션 포즈 변경 (`idle1~4` / `screen1~7`), 음악 재생 모드 중 클릭 시 재생 모드 즉시 해제
- **좌클릭 드래그**: 화면 및 멀티 모니터 자유 이동 (음악 재생 모드 중 드래그 시 재생 모드 즉시 해제, Platform 모드에서 놓으면 아래 창 상단/작업 표시줄로 낙하)
- **더블클릭**: Roam ↔ Platform 전환
- **우클릭** (또는 **650ms 길게 누르기**): 픽셀 컨트롤 메뉴 열기 (Roam/Platform 전환, 곡 제목·재생 시간 및 이전곡/재생·일시정지/다음곡 제어, 포즈 미리보기, 0.25×~2.00× 크기 조절, 최상단 토글, 종료)
- **시스템 트레이 아이콘**: 빠른 Roam/Platform 전환, 중앙 복귀, 크기 초기화, 종료

## 빌드 및 실행

```powershell
# 실행 (Release)
dotnet run -c Release --project src/AemeathCompanion/AemeathCompanion.csproj

# 또는 빌드된 실행 파일 직접 실행
.\src\AemeathCompanion\bin\Release\net10.0-windows10.0.19041.0\AemeathCompanion.exe

# 핵심 로직 단위 테스트 실행
dotnet run --project tests/AemeathCompanion.Tests/AemeathCompanion.Tests.csproj
```

## 내부 네트워크(LAN) 배포 및 업데이트 (`172.21.10.15:17515`)

### 1. 배포 서버 실행
```powershell
# 기본 포트 17515로 배포 포털 및 버전 API 실행
.\scripts\start-release-server.ps1

# Windows 방화벽 포트 허용 (최초 1회, 관리자 PowerShell)
New-NetFirewallRule -DisplayName "Aemeath Release Server" -Direction Inbound -Protocol TCP -LocalPort 17515 -Action Allow
```

- **다운로드 포털**: `http://172.21.10.15:17515/` (최상단 최신 버전 대형 다운로드 버튼 + 전체 Git 태그별 버전 목록/다운로드 링크)
- **최신 버전 API**: `http://172.21.10.15:17515/api/version` (Git 태그 기준 최신 버전/다운로드 정보 JSON)
- **전체 목록 API**: `http://172.21.10.15:17515/api/releases`
- **패키지 다운로드**: `http://172.21.10.15:17515/download/latest` 또는 `/download/<tag>`

### 2. 새 버전 태그 및 릴리즈 패키지 생성
```powershell
# 예: v0.1.0 태그 생성 + releases/AemeathCompanion-v0.1.0.zip 자동 패키징
.\scripts\publish-release.ps1 -Version 0.1.0 -Notes "새 기능 및 버그 수정" -CreateTag

# .NET 10 런타임 미설치 PC용 독립 실행 패키지(Self-Contained) 생성 시
.\scripts\publish-release.ps1 -Version 0.1.0 -Notes "독립 실행 패키지" -CreateTag -SelfContained
```

### 3. 앱 내 자동 업데이트 확인
- 앱 실행 시 및 우클릭 메뉴 최하단의 **`최신 확인`** 버튼 클릭 시 `http://172.21.10.15:17515/api/version`을 조회합니다.
- 현재 버전보다 새로운 Git 태그가 존재할 때만 **`⬆ vX.Y.Z 업데이트`** 버튼이 노출되며, 클릭 시 패키지를 받아 무결성(SHA-256) 검증 후 자동으로 교체·재실행합니다.


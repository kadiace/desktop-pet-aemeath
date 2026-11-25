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

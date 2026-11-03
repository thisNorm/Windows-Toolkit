# Windows Toolkit

Small Windows utilities for everyday development.

## NetworkPeek

현재 로컬 IPv4와 기본 게이트웨이를 작업표시줄 근처에서 확인하는 도구입니다.

- 시계 옆 시스템 트레이의 IP 아이콘을 클릭해 확인
- 작은 팝업에 연결 이름·IP·게이트웨이만 표시
- IP 또는 GW 줄을 클릭하면 해당 주소 복사
- 네트워크 변경 감지 및 3초 간격 자동 갱신
- Wi-Fi / Ethernet을 VPN·WSL 가상 어댑터보다 우선 표시
- IP·게이트웨이·전체 정보 복사, 표시줄 드래그 이동
- Windows 시작 시 실행은 트레이 메뉴에서 선택

### Run

폴더의 `NetworkPeek` 바로가기 또는 `NetworkPeek/bin/NetworkPeek.exe`를 실행하세요. 설치나 관리자 권한이 필요하지 않습니다.
트레이 아이콘이 숨겨져 있으면 작업표시줄 오른쪽의 `^` 안에서 찾을 수 있습니다.
숨겨진 IP 아이콘을 시계 옆으로 드래그하거나 Windows 설정의 **개인 설정 → 작업 표시줄 → 기타 시스템 트레이 아이콘**에서 NetworkPeek를 켜면 항상 보입니다.
기본은 트레이만 표시됩니다. 우클릭 메뉴의 **IP 표시줄 보이기**를 켜면 클릭 없이 IP·게이트웨이를 볼 수 있습니다.
완전히 종료하려면 트레이 메뉴의 **종료**를 누르세요.

팝업은 바깥을 누르거나 `Esc`로 닫습니다. `Tab`으로 IP/GW를 선택하고 `Enter` 또는 `Space`로 복사할 수 있습니다.
`Ctrl+C`는 선택한 주소(기본 IP), `Ctrl+Shift+C`는 어댑터별 IP·서브넷 마스크·DNS를 포함한 전체 정보를 복사합니다.
우클릭 메뉴에서도 전체 정보 복사·새로 고침·시작 프로그램 설정을 사용할 수 있습니다.

Windows 11의 기본 작업표시줄은 예전 사용자 도구모음을 제공하지 않으므로,
항상 표시되는 창은 작업표시줄 **바로 위에 놓이는 작은 독립 표시줄**입니다.

### Build

Windows 10/11과 .NET Framework 4.8 이상이 필요합니다. 외부 패키지는 없습니다.

```powershell
cd NetworkPeek
.\build.ps1
.\test.ps1
```

PowerShell 스크립트 실행이 제한된 환경에서는 `build.cmd` / `test.cmd`를 실행하세요.
컴파일러는 Windows에 포함된 .NET Framework의 `csc.exe`를 사용합니다.

### Scope

로컬 네트워크 주소를 표시합니다. 공인 IP나 인터넷 접속 여부를 확인하기 위한 외부 요청은 하지 않습니다.
기본 게이트웨이가 있는 물리 네트워크를 우선 선택하며, 다른 활성 어댑터는 전체 정보 복사에 포함됩니다.
네트워크 전환 중에는 잠시 연결 없음으로 표시될 수 있습니다.

표시줄 위치와 표시 여부는 `%LOCALAPPDATA%/WindowsToolkit/NetworkPeek/settings.txt`에 저장됩니다.
시작 프로그램 옵션을 켜면 현재 사용자의 `HKCU/Software/Microsoft/Windows/CurrentVersion/Run/NetworkPeek` 값만 추가합니다.
실행 파일을 옮긴 경우 새 위치에서 해당 옵션을 껐다가 켜세요.
네트워크 설정·방화벽·라우팅·서비스는 변경하지 않습니다. 관리자 권한이 필요하지 않습니다.

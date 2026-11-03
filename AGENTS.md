# Windows Toolkit requirements

- Every utility must support a compact view or quick actions from its system tray icon, alongside its full window when needed.
- Every tray context menu must include a checkable **Windows 시작 시 실행** option. Show the actual registration state, support enabling and disabling, and do not enable it without the user choosing it.
- Register startup for the current user only, quote executable paths, and start quietly in the tray when Windows signs in.
- Keep the interface easy to scan: few controls, restrained color, and no unnecessary borders. Preserve keyboard access and copy feedback.
- Keep utilities independent and avoid external packages when the Windows/.NET standard library is sufficient.

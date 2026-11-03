using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace PortPeek
{
    internal static class PortActions
    {
        internal static void Terminate(PortEntry selected)
        {
            if (selected == null || !selected.CanTerminate)
                throw new InvalidOperationException("이 프로세스는 종료할 수 없습니다.");

            IntPtr handle = OpenProcess(0x1000 | 0x0001, false, (uint)selected.ProcessId);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                long created, exited, kernel, user;
                if (!GetProcessTimes(handle, out created, out exited, out kernel, out user))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (DateTime.FromFileTimeUtc(created) != selected.ProcessStartedAtUtc.Value)
                    throw new InvalidOperationException("프로세스가 변경되었습니다. 목록을 새로 고친 뒤 다시 선택하세요.");
                bool critical;
                if (!IsProcessCritical(handle, out critical)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (critical) throw new InvalidOperationException("Windows 필수 프로세스는 종료할 수 없습니다.");

                var current = PortReader.Read();
                if (!current.Entries.Any(e => e.Key == selected.Key && e.ProcessStartedAtUtc == selected.ProcessStartedAtUtc))
                    throw new InvalidOperationException("이 포트의 사용 상태가 변경되었습니다. 목록을 새로 고쳐주세요.");
                // Keep the same process handle through validation and termination to avoid PID reuse.
                if (!TerminateProcess(handle, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { CloseHandle(handle); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsProcessCritical(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

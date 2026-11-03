using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using PortPeek;

internal static class PortActionsTests
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--owned-child")
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Console.WriteLine(((IPEndPoint)listener.LocalEndpoint).Port);
            Console.Out.Flush();
            Thread.Sleep(30000);
            listener.Stop();
            return 0;
        }

        using (var own = Process.GetCurrentProcess())
        {
            try {
                PortActions.Terminate(new PortEntry { ProcessId = own.Id, ProcessStartedAtUtc = own.StartTime.ToUniversalTime() });
                throw new Exception("Self termination was allowed.");
            }
            catch (InvalidOperationException) { Console.WriteLine("PASS: own process cannot be terminated."); }
        }

        using (var child = Process.Start(new ProcessStartInfo(typeof(PortActionsTests).Assembly.Location, "--owned-child") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        }))
        {
            try
            {
                int port = Int32.Parse(child.StandardOutput.ReadLine());
                var selected = PortReader.Read().Entries.FirstOrDefault(e => e.ProcessId == child.Id && e.Port == port && e.Protocol == "TCP");
                if (selected == null || !selected.CanTerminate) throw new Exception("Owned test listener was not associated with its process.");
                var stale = new PortEntry { ProcessId = selected.ProcessId, Port = selected.Port, Protocol = selected.Protocol, ProcessStartedAtUtc = selected.ProcessStartedAtUtc.Value.AddSeconds(-1) };
                bool rejected = false;
                try { PortActions.Terminate(stale); } catch (InvalidOperationException) { rejected = true; }
                if (!rejected || child.HasExited) throw new Exception("Stale process identity was not rejected safely.");
                Console.WriteLine("PASS: stale creation time rejected; owned child remains alive.");

                PortActions.Terminate(selected);
                if (!child.WaitForExit(5000)) throw new Exception("Owned child did not terminate after request.");
                if (PortReader.Read().Entries.Any(e => e.ProcessId == child.Id && e.Port == port)) throw new Exception("Owned child port remained bound.");
                Console.WriteLine("PASS: matching owned process terminated and its port released.");
                return 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL: " + error.Message); return 1; }
            finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
        }
    }
}

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace MarkLens.Npp
{
    /// <summary>
    /// Starts Notepad++ again once the current instance has exited. A hidden PowerShell waits
    /// for this process to end (so the new instance is not handed over to the closing one) and
    /// then launches the same executable with the same switches, such as -settingsDir.
    /// </summary>
    internal static class Restarter
    {
        public static void StartAfterExit()
        {
            try
            {
                var self = Process.GetCurrentProcess();
                string exe = self.MainModule.FileName;
                // Keep launch switches; leave out file names, the restored session reopens files.
                var switches = Environment.GetCommandLineArgs().Skip(1)
                    .Where(a => a.StartsWith("-", StringComparison.Ordinal))
                    .Select(QuoteSwitch)
                    .ToArray();

                var script = new StringBuilder();
                script.Append("Wait-Process -Id ").Append(self.Id).Append(" -ErrorAction SilentlyContinue; ");
                script.Append("Start-Process -FilePath ").Append(PsQuote(exe));
                if (switches.Length > 0)
                    script.Append(" -ArgumentList ").Append(string.Join(",", switches.Select(PsQuote)));

                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception ex)
            {
                // Notepad++ is closing anyway; the user can start it by hand.
                Main.ReportError(ex);
            }
        }

        /// <summary>-settingsDir=C:\My Settings becomes -settingsDir="C:\My Settings".</summary>
        private static string QuoteSwitch(string arg)
        {
            int eq = arg.IndexOf('=');
            if (eq < 0 || arg.IndexOf(' ') < 0)
                return arg;
            return arg.Substring(0, eq + 1) + "\"" + arg.Substring(eq + 1) + "\"";
        }

        private static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";
    }
}

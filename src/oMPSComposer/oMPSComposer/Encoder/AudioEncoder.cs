using System;
using System.Diagnostics;
using System.IO;

namespace oMPSComposer
{

    internal static class AudioEncoder
    {
        public const int DefaultBitrate = 128;

        public static void Encode(string srcWav, string dstAt3, int bitrateKbps = DefaultBitrate)
        {
            string at3tool = FindAt3Tool();

            string args = $"-e -br {bitrateKbps} \"{srcWav}\" \"{dstAt3}\"";

            Run(at3tool, args, "audio encoding failed");
        }

        private static string FindAt3Tool()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "at3tool.exe");

            return path;
        }

        private static void Run(string exe, string arguments, string label)
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                string _ = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode != 0)
                {
                    string msg = string.IsNullOrWhiteSpace(err) ? "No output." : err.Trim();
                    throw new InvalidOperationException($"{label} (exit {p.ExitCode}):\n{msg}");
                }
            }
        }
    }
}

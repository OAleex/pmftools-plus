using System;
using System.Diagnostics;
using System.IO;

namespace oMPSComposer
{
    internal static class VideoEncoder
    {
        private const int Width = 480;
        private const int Height = 272;
        private const double Fps = 30_000.0 / 1_001.0;

        public const int PspVideoBitrateCapKbps = 4800;

        public static string PasslogBase(string dst)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(dst)) ?? ".";
            string stem = Path.GetFileNameWithoutExtension(dst);
            return Path.Combine(dir, stem + "_passlog");
        }

        public static void EncodeOnePass(string src, string dst, VideoOptions opts)
        {
            string args = BaseArgs(src, opts.AvgBitrate, opts.MaxBitrate, opts) + $"-bsf:v h264_mp4toannexb -an " + $"\"{dst}\"";

            Run(FindFfmpeg(), args, "ffmpeg video encoding failed");
        }

        public static void EncodePass1(string src, VideoOptions opts, string passlogBase)
        {
            string nullOut = IsWindows() ? "NUL" : "/dev/null";
            string args = BaseArgs(src, opts.AvgBitrate, opts.MaxBitrate, opts) + $"-pass 1 -passlogfile \"{passlogBase}\" " + $"-an -f null \"{nullOut}\"";

            Run(FindFfmpeg(), args, "ffmpeg video encoding pass 1 failed");
        }

        public static void EncodePass2(string src, string dst, VideoOptions opts, string passlogBase)
        {
            string args = BaseArgs(src, opts.AvgBitrate, opts.MaxBitrate, opts) + $"-pass 2 -passlogfile \"{passlogBase}\" " + $"-bsf:v h264_mp4toannexb -an " + $"\"{dst}\"";

            Run(FindFfmpeg(), args, "ffmpeg video encoding pass 2 failed");
        }

        public static void CleanPasslog(string passlogBase)
        {
            string dir = Path.GetDirectoryName(passlogBase) ?? ".";
            string name = Path.GetFileName(passlogBase);
            try
            {
                foreach (string f in Directory.GetFiles(dir, name + "*"))
                    File.Delete(f);
            }
            catch { }
        }

        private static string BaseArgs(string src, int avgKbps, int maxKbps, VideoOptions opts)
        {
            int gopSize = Math.Max(2, (int)Math.Floor(opts.IdrDurationMs * Fps / 1000.0));
            int minKeyint = opts.SceneChangeDetect ? Math.Max(1, gopSize / 2) : gopSize;
            int scThreshold = opts.SceneChangeDetect ? 40 : 0;

            string vf = BuildVf(opts.OverScan);
            string x264params = BuildX264Params(maxKbps, opts);

            return
                $"-y -i \"{src}\" " + $"-c:v libx264 -preset slow " + $"-profile:v main -level 2.1 " + $"-b:v {avgKbps}k -maxrate {maxKbps}k -bufsize {maxKbps}k " +
                $"-refs 1 -bf 0 " + $"-g {gopSize} -keyint_min {minKeyint} -sc_threshold {scThreshold} " + $"-vf \"{vf}\" -pix_fmt yuv420p -vsync cfr " +
                $"-x264-params \"{x264params}\" ";
        }

        private static string BuildVf(bool overScan)
        {
            if (overScan)
                return
                    $"scale={Width}:{Height}:force_original_aspect_ratio=increase," + $"crop={Width}:{Height}," + $"fps=30000/1001";

            return
                $"scale={Width}:{Height}:force_original_aspect_ratio=decrease," + $"pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2," + $"fps=30000/1001";
        }

        private static string BuildX264Params(int maxKbps, VideoOptions opts)
        {
            int aqMode = opts.GradationImprove ? 1 : 0;
            string deblock = opts.DeblockFilter ? "" : "no-deblock=1:";

            return
                $"weightp=1:" + $"weightb=0:" + $"8x8dct=0:" + $"cabac=1:" + deblock + $"nal-hrd=vbr:" + $"vbv-maxrate={maxKbps}:" +
                $"vbv-bufsize={maxKbps}:" + $"aq-mode={aqMode}:" + $"aq-strength=0.7:" + $"psy-rd=0.8:" + $"rc-lookahead=10:" + $"repeat-headers=1:" + $"aud=1";
        }

        private static string FindFfmpeg()
        {
            return Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        }

        private static bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT || Environment.OSVersion.Platform == PlatformID.Win32Windows;
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

                if (err.IndexOf("Error parsing option", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException($"{label}: libx264 rejected one or more encoder options.\n{err.Trim()}");

                if (p.ExitCode != 0)
                {
                    string msg = string.IsNullOrWhiteSpace(err) ? "No output." : err.Trim();
                    throw new InvalidOperationException($"{label} (exit {p.ExitCode}):\n{msg}");
                }
            }
        }
    }
}

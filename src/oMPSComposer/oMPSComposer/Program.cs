using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace oMPSComposer
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (!CheckDependencies())
                    return 1;

                if (args.Length == 0)
                {
                    PrintUsage();
                    return 0;
                }

                ParsedArgs parsed = Parse(args);

                Build(parsed.Video, parsed.AudioFiles, parsed.Output, parsed.AudioBitrate, parsed.Opts);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ERROR] " + ex.Message);
                return 1;
            }
        }

        private static bool CheckDependencies()
        {
            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? ".");

            bool ffmpeg = FindTool(exeDir, "ffmpeg.exe");
            bool at3 = FindTool(exeDir, "at3tool.exe");

            if (ffmpeg && at3) return true;

            Console.WriteLine();
            Console.WriteLine("  Missing Dependencies:");
            Console.WriteLine();

            if (!ffmpeg) Console.WriteLine("    ffmpeg.exe not found");
            if (!at3) Console.WriteLine("    at3tool.exe not found");
            Console.WriteLine();

            return false;
        }

        private static bool FindTool(string exeDir, string name)
        {
            string local = Path.Combine(exeDir ?? ".", name);
            return File.Exists(local);
        }

        private sealed class ParsedArgs
        {
            public string Video = null;
            public List<string> AudioFiles = new List<string>();
            public string Output = null;
            public int AudioBitrate = AudioEncoder.DefaultBitrate;
            public VideoOptions Opts = new VideoOptions();
        }

        private static ParsedArgs Parse(string[] args)
        {
            var parsed = new ParsedArgs();
            int positional = 0;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg == "-h" || arg == "--help" || arg == "/?")
                {
                    PrintUsage();
                    Environment.Exit(0);
                }

                if (arg == "--avg-bitrate") { parsed.Opts.AvgBitrate = ReadInt(args, ref i, arg); continue; }
                if (arg == "--max-bitrate") { parsed.Opts.MaxBitrate = ReadInt(args, ref i, arg); continue; }
                if (arg == "--audio-bitrate") { parsed.AudioBitrate = ReadInt(args, ref i, arg); continue; }
                if (arg == "--idr-duration") { parsed.Opts.IdrDurationMs = ReadInt(args, ref i, arg); continue; }
                if (arg == "--m-frames") { parsed.Opts.MFrames = ReadInt(args, ref i, arg); continue; }

                if (arg == "--encode-mode")
                {
                    string mode = ReadStr(args, ref i, arg).ToLowerInvariant();
                    switch (mode)
                    {
                        case "1pass": case "1passvbr": parsed.Opts.Mode = EncodeMode.OnePassVbr; break;
                        case "2pass": case "2passvbr": parsed.Opts.Mode = EncodeMode.TwoPassVbr; break;
                        default: throw new ArgumentException($"Unknown encode mode '{mode}'. Use 1pass or 2pass.");
                    }
                    continue;
                }

                if (arg == "--no-gradation") { parsed.Opts.GradationImprove = false; continue; }
                if (arg == "--overscan") { parsed.Opts.OverScan = true; continue; }
                if (arg == "--no-deblock") { parsed.Opts.DeblockFilter = false; continue; }
                if (arg == "--no-scene-change") { parsed.Opts.SceneChangeDetect = false; continue; }

                if (arg.StartsWith("--"))
                    throw new ArgumentException("Unknown option: " + arg);

                switch (positional++)
                {
                    case 0:
                        parsed.Video = arg;
                        break;

                    case 1:
                        foreach (string f in arg.Split(','))
                        {
                            string t = f.Trim();
                            if (!string.IsNullOrEmpty(t)) parsed.AudioFiles.Add(t);
                        }
                        break;

                    case 2:
                        parsed.Output = arg;
                        break;

                    default:
                        throw new ArgumentException("Unexpected argument: " + arg);
                }
            }

            if (string.IsNullOrWhiteSpace(parsed.Video))
                throw new ArgumentException("Missing argument: video\n" + UsageLine());

            if (parsed.AudioFiles.Count == 0)
                throw new ArgumentException("Missing argument: at least one audio file\n" + UsageLine());

            if (string.IsNullOrWhiteSpace(parsed.Output))
                throw new ArgumentException("Missing argument: output\n" + UsageLine());

            if (parsed.AudioFiles.Count > MpsMuxer.MaxAudioTracks)
                throw new ArgumentException($"Too many audio tracks! Maximum allowed is {MpsMuxer.MaxAudioTracks}.");

            if (parsed.Opts.MaxBitrate >= VideoEncoder.PspVideoBitrateCapKbps)
                throw new ArgumentException($"Please make the max bitrate value smaller than 4800 kbps.");

            if (parsed.Opts.AvgBitrate >= parsed.Opts.MaxBitrate)
                throw new ArgumentException($"Please make the max bitrate value bigger than that of average. " + $"(avg {parsed.Opts.AvgBitrate} kbps, max {parsed.Opts.MaxBitrate} kbps)");

            string videoExt = Path.GetExtension(parsed.Video).ToLowerInvariant();
            if (videoExt != ".mov" && videoExt != ".avi" && videoExt != ".mp4")
                throw new ArgumentException($"Video: unsupported format '{videoExt}'. Use .mov, .avi, or .mp4.");

            for (int t = 0; t < parsed.AudioFiles.Count; t++)
            {
                string audioExt = Path.GetExtension(parsed.AudioFiles[t]).ToLowerInvariant();
                if (audioExt != ".wav")
                    throw new ArgumentException($"Audio[{t}]: unsupported format '{audioExt}'. Use .wav.");
            }

            if (!File.Exists(parsed.Video))
                throw new FileNotFoundException("Video not found: " + parsed.Video);

            for (int t = 0; t < parsed.AudioFiles.Count; t++)
                if (!File.Exists(parsed.AudioFiles[t]))
                    throw new FileNotFoundException($"Audio[{t}] not found: " + parsed.AudioFiles[t]);

            return parsed;
        }

        private static int ReadInt(string[] args, ref int i, string opt)
        {
            if (++i >= args.Length)
                throw new ArgumentException("Missing value for " + opt);

            int v;

            if (!int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v <= 0)
                throw new ArgumentException($"Invalid integer for {opt}: {args[i]}");
            return v;
        }

        private static string ReadStr(string[] args, ref int i, string opt)
        {
            if (++i >= args.Length)
                throw new ArgumentException("Missing value for " + opt);
            return args[i];
        }

        private static void Build(string videoPath, List<string> audioPaths, string outputPath, int audioBitrate, VideoOptions opts)
        {
            var sw = Stopwatch.StartNew();

            Console.WriteLine();
            Console.WriteLine("  oMPSComposer v1.0  |  by Alex \"OAleex\" Felix");
            Console.WriteLine();
            Console.WriteLine("  Video   " + Path.GetFileName(videoPath));
            Console.WriteLine(audioPaths.Count == 1 ? "  Audio   " + Path.GetFileName(audioPaths[0]) : "  Audio   " + string.Join(", ", audioPaths.ConvertAll(Path.GetFileName)));
            Console.WriteLine("  Output  " + Path.GetFileName(outputPath));
            Console.WriteLine("  Mode    " + ModeLabel(opts.Mode) + $"  |  avg {opts.AvgBitrate} kbps  max {opts.MaxBitrate} kbps");
            Console.WriteLine();

            string outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);

            string tempDir = string.IsNullOrEmpty(outDir) ? Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? "." : outDir;
            string tempStem = Path.GetFileNameWithoutExtension(outputPath);
            string h264Path = Path.Combine(tempDir, tempStem + ".tmp.264");
            string[] atracPaths = new string[audioPaths.Count];
            bool twoPass = opts.Mode == EncodeMode.TwoPassVbr;
            int baseStep = 3;
            int step = 0;

            try
            {
                if (twoPass)
                {
                    string passlog = VideoEncoder.PasslogBase(h264Path);
                    try
                    {
                        PrintStepStart(++step, baseStep, "Encoding video");
                        VideoEncoder.EncodePass1(videoPath, opts, passlog);
                        VideoEncoder.EncodePass2(videoPath, h264Path, opts, passlog);
                        PrintStepDone();
                    }
                    finally { VideoEncoder.CleanPasslog(passlog); }
                }

                else
                {
                    PrintStepStart(++step, baseStep, "Encoding video");
                    VideoEncoder.EncodeOnePass(videoPath, h264Path, opts);
                    PrintStepDone();
                }

                PrintStepStart(++step, baseStep, "Encoding audio");
                for (int t = 0; t < audioPaths.Count; t++)
                {
                    atracPaths[t] = Path.Combine(tempDir, tempStem + $".audio{t}.tmp.atx");
                    AudioEncoder.Encode(audioPaths[t], atracPaths[t], audioBitrate);
                }
                PrintStepDone();

                PrintStepStart(++step, baseStep, "Muxing");

                var atracReaders = new AtracReader[audioPaths.Count];
                for (int t = 0; t < audioPaths.Count; t++)
                    atracReaders[t] = AtracReader.Open(atracPaths[t]);

                MpsMuxer.Mux(h264Path, atracReaders, outputPath);
                PrintStepDone();
            }
            finally
            {
                for (int t = 0; t < atracPaths.Length; t++)
                    DeleteAtracTempFiles(atracPaths[t]);

                if (File.Exists(h264Path))
                    File.Delete(h264Path);
            }

            sw.Stop();
            long outSize = new FileInfo(outputPath).Length;
            double mb = outSize / 1_048_576.0;
            double secs = sw.Elapsed.TotalSeconds;

            Console.WriteLine();
            Console.WriteLine("  Output  " + Path.GetFileName(outputPath) + "  |  " + mb.ToString("F2", CultureInfo.InvariantCulture) + " MB" + "  |  " + secs.ToString("F1", CultureInfo.InvariantCulture) + "s");
            Console.WriteLine();
        }

        private static Thread _spinnerThread;
        private static volatile bool _spinnerRunning;
        private static string _spinnerPrefix;

        private static void PrintStepStart(int step, int total, string label)
        {
            _spinnerPrefix = $"  [{step}/{total}] {label} ";
            _spinnerRunning = true;

            _spinnerThread = new Thread(() =>
            {
                int count = 0;
                while (_spinnerRunning)
                {
                    Console.Write("\r" + _spinnerPrefix + new string('.', count % 20 + 1));
                    count++;
                    Thread.Sleep(120);
                }

            })
            { IsBackground = true };

            _spinnerThread.Start();
        }

        private static void PrintStepDone(string extra = null)
        {
            _spinnerRunning = false;
            _spinnerThread?.Join();

            string line = _spinnerPrefix + new string('.', 20) + "  OK";
            if (extra != null) line += "  " + extra;
            Console.WriteLine("\r" + line);
        }

        private static void DeleteAtracTempFiles(string atxPath)
        {
            if (string.IsNullOrEmpty(atxPath))
                return;

            if (File.Exists(atxPath))
                File.Delete(atxPath);

            string stem = Path.Combine(
                Path.GetDirectoryName(atxPath) ?? ".",
                Path.GetFileNameWithoutExtension(atxPath));

            string euiPath = stem + ".eui";
            string esiaPath = stem + ".esia";

            if (File.Exists(euiPath))
                File.Delete(euiPath);

            if (File.Exists(esiaPath))
                File.Delete(esiaPath);
        }

        private static string UsageLine() => "Usage: oMPSComposer <video.mov|video.avi|video.mp4> <audio0.wav[,audio1.wav,...]> <output.mps> [options]";

        private static string ModeLabel(EncodeMode m) => m == EncodeMode.TwoPassVbr ? "2-pass VBR" : "1-pass VBR";

        private static void PrintUsage()
        {
            int cap = VideoEncoder.PspVideoBitrateCapKbps;
            Console.WriteLine();
            Console.WriteLine("  oMPSComposer v1.0  |  by Alex \"OAleex\" Felix");
            Console.WriteLine();
            Console.WriteLine("  usage: oMPSComposer <video.mov|video.avi|video.mp4> <audio.wav[,audio2.wav,...]> <output.mps> [options]");
            Console.WriteLine();
            Console.WriteLine("  Bitrate");
            Console.WriteLine($"    --avg-bitrate <n>      video average kbps     (default: 1000, must be < max)");
            Console.WriteLine($"    --max-bitrate <n>      video max kbps          (default: 2000, must be < {cap})");
            Console.WriteLine("    --audio-bitrate <n>    ATRAC3+ kbps            (default:  128)");
            Console.WriteLine();
            Console.WriteLine("  Encode mode");
            Console.WriteLine("    --encode-mode <mode>   1pass or 2pass           (default: 2pass)");
            Console.WriteLine();
            Console.WriteLine("  Advanced");
            Console.WriteLine("    --idr-duration <ms>    IDR interval ms          (default: 2000)");
            Console.WriteLine("    --m-frames <n>         I/P frame duration       (default:    1)");
            Console.WriteLine("    --no-gradation         disable gradation improve");
            Console.WriteLine("    --overscan             crop to fill (no bars)");
            Console.WriteLine("    --no-deblock           disable de-blocking filter");
            Console.WriteLine("    --no-scene-change      disable scene change detection");
            Console.WriteLine();
        }
    }
}

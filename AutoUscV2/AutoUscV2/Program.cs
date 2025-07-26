using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoUscV2
{
    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(opts =>
                   {
                       UscProject proj = null;
                       var robot = new UscRobot(opts.Executable);
                       try
                       {
                           robot.Launch();
                           robot.CloseDirectDrawSupportMessage();
                           proj = robot.CreateNewProject(opts.ClipName, opts.ClipDescription, opts.ProjectName, opts.ProjectDescription, opts.Output);
                           robot.AddVideoStream(Path.GetFullPath(opts.Video));
                           robot.AddAudioStream(Path.GetFullPath(opts.Audio));
                           robot.OpenVideoEncodeSettings();
                           robot.SetVideoBitrates(opts.averageBitrate, opts.maxBitrate);
                           robot.Compose(proj);

                       }
                       finally
                       {
                           robot.Close();
                           proj?.Delete();

                       }
                   })
                   .WithNotParsed(errors =>
                   {

                   });
        }
    }

    public class Options
    {
        public const string DefaultExecutablePath = ".\\Umd Stream Composer\\bin\\";

        private string _executable;

        [Option('x', "executable", HelpText = "Path to UmdStreamComposer.exe or UmdStream.exe", Default = DefaultExecutablePath, Required = false)]
        public string Executable
        {
            get
            {
                if (_executable != null)
                    return _executable;

                string basePath = DefaultExecutablePath;


                if (!string.IsNullOrEmpty(_executable) && _executable != DefaultExecutablePath)
                {
                    return _executable;
                }

                string renamedPath = Path.Combine(basePath, "UmdStream.exe");
                if (File.Exists(renamedPath))
                {
                    _executable = renamedPath;
                    return _executable;
                }

                string originalPath = Path.Combine(basePath, "UmdStreamComposer.exe");
                if (File.Exists(originalPath))
                {
                    _executable = originalPath;
                    return _executable;
                }

                _executable = renamedPath;
                return _executable;
            }
            set
            {
                _executable = value;
            }
        }

        [Option("cn", HelpText = "Clip name", Required = true)]
        public string ClipName { get; set; }

        [Option("cd", HelpText = "Clip description", Default = "", Required = false)]
        public string ClipDescription { get; set; }

        [Option("pn", HelpText = "Project name", Required = true)]
        public string ProjectName { get; set; }

        [Option("pd", HelpText = "Project description", Default = "", Required = false)]
        public string ProjectDescription { get; set; }

        [Option('a', "audio", HelpText = "Input audio", Required = true)]
        public string Audio { get; set; }

        [Option('v', "video", HelpText = "Input video", Required = true)]
        public string Video { get; set; }

        [Option("averagebitrate", HelpText = "Average Bitrate", Required = false, Default = 1000)]
        public int averageBitrate { get; set; }

        [Option("maxbitrate", HelpText = "Max Bitrate", Required = false, Default = 2000)]
        public int maxBitrate { get; set; }

        [Option('o', "output", HelpText = "Output file", Required = false)]
        public string Output { get; set; }
    }
}

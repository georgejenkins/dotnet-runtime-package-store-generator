using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace layers.Library
{
    static class CLIWrapper
    {
        /// <summary>
        /// A collection of known paths for common utilities that are usually not found in the path
        /// </summary>
        static readonly IDictionary<string, string> KNOWN_LOCATIONS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe" },
            {"chmod", @"/bin/chmod" },
            {"zip", @"/usr/bin/zip" },
            {"docker.exe", @"C:\Program Files\Docker\Docker\Resources\bin\docker.exe" }
        };

        /// <summary>
        /// Search the path environment variable for the command given.
        /// </summary>
        /// <param name="command">The command to search for in the path</param>
        /// <returns>The full path to the command if found otherwise it will return null</returns>
        public static string FindExecutableInPath(string command)
        {

            if (File.Exists(command))
                return Path.GetFullPath(command);

#if NETCORE
            if (string.Equals(command, "dotnet.exe"))
            {
                if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    command = "dotnet";
                }

                var mainModule = Process.GetCurrentProcess().MainModule;
                if (!string.IsNullOrEmpty(mainModule?.FileName)
                    && Path.GetFileName(mainModule.FileName).Equals(command, StringComparison.OrdinalIgnoreCase))
                {
                    return mainModule.FileName;
                }
            }
#endif

            Func<string, string> quoteRemover = x =>
            {
                if (x.StartsWith("\""))
                    x = x.Substring(1);
                if (x.EndsWith("\""))
                    x = x.Substring(0, x.Length - 1);
                return x;
            };

            var envPath = Environment.GetEnvironmentVariable("PATH");
            foreach (var path in envPath.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(quoteRemover(path), command);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch (Exception)
                {
                    // Catch exceptions and continue if there are invalid characters in the user's path.
                }
            }

            if (KNOWN_LOCATIONS.ContainsKey(command) && File.Exists(KNOWN_LOCATIONS[command]))
                return KNOWN_LOCATIONS[command];

            return null;
        }

        public class ExecuteShellCommandResult
        {
            public int ExitCode { get; }
            public string Stdout { get; }

            public ExecuteShellCommandResult(int exitCode, string stdout)
            {
                this.ExitCode = exitCode;
                this.Stdout = stdout;
            }
        }

        public static ExecuteShellCommandResult ExecuteShellCommand(string workingDirectory, string process, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = process,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };


            StringBuilder capturedOutput = new StringBuilder();
            var handler = (DataReceivedEventHandler)((o, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;
                capturedOutput.AppendLine(e.Data);
            });

            using (var proc = new Process())
            {
                proc.StartInfo = startInfo;
                proc.Start();

                if (startInfo.RedirectStandardOutput)
                {
                    proc.ErrorDataReceived += handler;
                    proc.OutputDataReceived += handler;
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    proc.EnableRaisingEvents = true;
                }

                proc.WaitForExit();
                return new ExecuteShellCommandResult(proc.ExitCode, capturedOutput.ToString());
            }
        }
    }
}

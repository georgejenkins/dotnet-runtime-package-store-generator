﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;

/// <summary>
/// Modified and unmodified code sourced from https://github.com/aws/aws-extensions-for-dotnet-cli 
/// Code used under the provisions of the Apache 2.0 Licence.
/// </summary>
namespace layers.Library
{
    /// <summary>
    /// Wrapper around the dotnet cli used to execute the publish command.
    /// </summary>
    public class LambdaDotNetCLIWrapper
    {
        string _workingDirectory;

        public LambdaDotNetCLIWrapper(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
        }

        public class StoreResult
        {
            public int exitCode;
            public string filePath;
        }

        /// <summary>
        /// Execute the dotnet store command on the provided package manifest
        /// </summary>
        /// <param name="defaults"></param>
        /// <param name="projectLocation"></param>
        /// <param name="outputLocation"></param>
        /// <param name="targetFramework"></param>
        /// <param name="packageManifest"></param>
        /// <param name="enableOptimization"></param>
        /// <returns></returns>
        public StoreResult Store(string projectLocation, string outputLocation, string targetFramework, string packageManifest, bool enableOptimization)
        {
            if (outputLocation == null)
                throw new ArgumentNullException(nameof(outputLocation));

            if (Directory.Exists(outputLocation))
            {
                try
                {
                    Directory.Delete(outputLocation, true);
                    Console.WriteLine("Deleted previous publish folder");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Warning unable to delete previous publish folder: {e.Message}");
                }
            }


            var dotnetCLI = FindExecutableInPath("dotnet.exe");
            if (dotnetCLI == null)
                dotnetCLI = FindExecutableInPath("dotnet");
            if (string.IsNullOrEmpty(dotnetCLI))
                throw new Exception("Failed to locate dotnet CLI executable. Make sure the dotnet CLI is installed in the environment PATH.");

            var fullProjectLocation = this._workingDirectory;
            if (!string.IsNullOrEmpty(projectLocation))
            {
                fullProjectLocation = ProjectUtilities.DetermineProjectLocation(this._workingDirectory, projectLocation);
            }

            var fullPackageManifest = Path.Combine(fullProjectLocation, packageManifest);

            Console.WriteLine($"... invoking 'dotnet store' for manifest {fullPackageManifest} into output directory {outputLocation}");


            StringBuilder arguments = new StringBuilder("store");
            if (!string.IsNullOrEmpty(outputLocation))
            {
                arguments.Append($" --output \"{outputLocation}\"");
            }

            if (!string.IsNullOrEmpty(targetFramework))
            {
                arguments.Append($" --framework \"{targetFramework}\"");
            }

            arguments.Append($" --manifest \"{fullPackageManifest}\"");
            arguments.Append($" --runtime {Common.Constants.RUNTIME_HIERARCHY_STARTING_POINT}");

            if (!enableOptimization)
            {
                arguments.Append(" --skip-optimization");
            }

            var psi = new ProcessStartInfo
            {
                FileName = dotnetCLI,
                Arguments = arguments.ToString(),
                WorkingDirectory = this._workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var handler = (DataReceivedEventHandler)((o, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;

                // Skip outputting this warning message as it adds a lot of noise to the output and is not actionable.
                // Full warning message being skipped: message NETSDK1062:
                // Unable to use package assets cache due to I/O error. This can occur when the same project is built
                // more than once in parallel. Performance may be degraded, but the build result will not be impacted.
                if (e.Data.Contains("message NETSDK1062"))
                    return;

                Console.WriteLine("... store: " + e.Data);
            });

            int exitCode;
            using (var proc = new Process())
            {
                proc.StartInfo = psi;
                proc.Start();


                proc.ErrorDataReceived += handler;
                proc.OutputDataReceived += handler;
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                proc.EnableRaisingEvents = true;

                proc.WaitForExit();

                exitCode = proc.ExitCode;
            }

            return new StoreResult()
            {
                exitCode = exitCode,
                filePath = outputLocation
            };
        }

        /// <summary>
        /// Executes the dotnet publish command for the provided project
        /// </summary>
        /// <param name="defaults"></param>
        /// <param name="projectLocation"></param>
        /// <param name="outputLocation"></param>
        /// <param name="targetFramework"></param>
        /// <param name="configuration"></param>
        /// <param name="msbuildParameters"></param>
        /// <param name="deploymentTargetPackageStoreManifestContent"></param>
        public int Publish(string projectLocation, string outputLocation, string targetFramework, string configuration, string msbuildParameters, IList<string> publishManifests)
        {
            if (outputLocation == null)
                throw new ArgumentNullException(nameof(outputLocation));

            if (Directory.Exists(outputLocation))
            {
                try
                {
                    Directory.Delete(outputLocation, true);
                    Console.WriteLine("Deleted previous publish folder");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Warning unable to delete previous publish folder: {e.Message}");
                }
            }

            Console.WriteLine($"... invoking 'dotnet publish', working folder '{outputLocation}'");

            var dotnetCLI = FindExecutableInPath("dotnet.exe");
            if (dotnetCLI == null)
                dotnetCLI = FindExecutableInPath("dotnet");
            if (string.IsNullOrEmpty(dotnetCLI))
                throw new Exception("Failed to locate dotnet CLI executable. Make sure the dotnet CLI is installed in the environment PATH.");

            var fullProjectLocation = this._workingDirectory;
            if (!string.IsNullOrEmpty(projectLocation))
            {
                fullProjectLocation = ProjectUtilities.DetermineProjectLocation(this._workingDirectory, projectLocation);
            }

            StringBuilder arguments = new StringBuilder("publish");
            if (!string.IsNullOrEmpty(projectLocation))
            {
                arguments.Append($" \"{fullProjectLocation}\"");
            }
            if (!string.IsNullOrEmpty(outputLocation))
            {
                arguments.Append($" --output \"{outputLocation}\"");
            }

            if (!string.IsNullOrEmpty(configuration))
            {
                arguments.Append($" --configuration \"{configuration}\"");
            }

            if (!string.IsNullOrEmpty(targetFramework))
            {
                arguments.Append($" --framework \"{targetFramework}\"");
            }

            if (!string.IsNullOrEmpty(msbuildParameters))
            {
                arguments.Append($" {msbuildParameters}");
            }

            if (!string.Equals("netcoreapp1.0", targetFramework, StringComparison.OrdinalIgnoreCase))
            {
                arguments.Append(" /p:GenerateRuntimeConfigurationFiles=true");

                // If you set the runtime to RUNTIME_HIERARCHY_STARTING_POINT it will trim out the Windows and Mac OS specific dependencies but Razor view precompilation
                // will not run. So only do this packaging optimization if there are no Razor views.
                if (Directory.GetFiles(fullProjectLocation, "*.cshtml", SearchOption.AllDirectories).Length == 0)
                {
                    arguments.Append($" -r {Common.Constants.RUNTIME_HIERARCHY_STARTING_POINT}");

                    if (msbuildParameters == null ||
                        msbuildParameters.IndexOf("--self-contained", StringComparison.InvariantCultureIgnoreCase) == -1)
                    {
                        arguments.Append(" --self-contained false ");
                    }

                    if (string.IsNullOrEmpty(msbuildParameters) ||
                        !msbuildParameters.Contains("PreserveCompilationContext"))
                    {
                        Console.WriteLine("... Disabling compilation context to reduce package size. If compilation context is needed pass in the \"/p:PreserveCompilationContext=false\" switch.");
                        arguments.Append(" /p:PreserveCompilationContext=false");
                    }
                }

                // If we have a manifest of packages already deploy in target deployment environment then write it to disk and add the 
                // command line switch
                if (publishManifests != null && publishManifests.Count > 0)
                {
                    foreach (var manifest in publishManifests)
                    {
                        arguments.Append($" --manifest \"{manifest}\"");
                    }
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = dotnetCLI,
                Arguments = arguments.ToString(),
                WorkingDirectory = this._workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var handler = (DataReceivedEventHandler)((o, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;
                Console.WriteLine("... publish: " + e.Data);
            });

            int exitCode;
            using (var proc = new Process())
            {
                proc.StartInfo = psi;
                proc.Start();


                proc.ErrorDataReceived += handler;
                proc.OutputDataReceived += handler;
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                proc.EnableRaisingEvents = true;

                proc.WaitForExit();

                exitCode = proc.ExitCode;
            }

            if (exitCode == 0)
            {
                ProcessAdditionalFiles(outputLocation);

                var chmodPath = FindExecutableInPath("chmod");
                if (!string.IsNullOrEmpty(chmodPath) && File.Exists(chmodPath))
                {
                    // as we are not invoking through a shell, which would handle
                    // wildcard expansion for us, we need to invoke per-file
                    var files = Directory.GetFiles(outputLocation, "*", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        var filename = Path.GetFileName(file);
                        var psiChmod = new ProcessStartInfo
                        {
                            FileName = chmodPath,
                            Arguments = "+rx \"" + filename + "\"",
                            WorkingDirectory = outputLocation,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using (var proc = new Process())
                        {
                            proc.StartInfo = psiChmod;
                            proc.Start();

                            proc.ErrorDataReceived += handler;
                            proc.OutputDataReceived += handler;
                            proc.BeginOutputReadLine();
                            proc.BeginErrorReadLine();

                            proc.EnableRaisingEvents = true;
                            proc.WaitForExit();

                            if (proc.ExitCode == 0)
                            {
                                Console.WriteLine($"Changed permissions on published file (chmod +rx {filename}).");
                            }
                        }
                    }
                }
            }

            return exitCode;
        }

        private void ProcessAdditionalFiles(string publishLocation)
        {
            var listOfDependencies = new List<string>();

            //var extraDependences = defaults["additional-files"] as string[];
            //if (extraDependences != null)
            //{
            //    foreach (var item in extraDependences)
            //        listOfDependencies.Add(item);
            //}

            foreach (var relativePath in listOfDependencies)
            {
                var fileName = Path.GetFileName(relativePath);
                string source;
                if (Path.IsPathRooted(relativePath))
                    source = relativePath;
                else
                    source = Path.Combine(publishLocation, relativePath);
                var target = Path.Combine(publishLocation, fileName);
                if (File.Exists(source) && !File.Exists(target))
                {
                    File.Copy(source, target);
                    Console.WriteLine($"... publish: Adding additional file {relativePath}");
                }
            }
        }

        /// <summary>
        /// A collection of known paths for common utilities that are usually not found in the path
        /// </summary>
        static readonly IDictionary<string, string> KNOWN_LOCATIONS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe" },
            {"chmod", @"/bin/chmod" },
            {"zip", @"/usr/bin/zip" }
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
            if (envPath != null)
            {
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
            }

            if (KNOWN_LOCATIONS.ContainsKey(command) && File.Exists(KNOWN_LOCATIONS[command]))
                return KNOWN_LOCATIONS[command];

            return null;
        }
    }
}

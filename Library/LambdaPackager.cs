using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

/// <summary>
/// Modified and unmodified code sourced from https://github.com/aws/aws-extensions-for-dotnet-cli 
/// Code used under the provisions of the Apache 2.0 Licence.
/// </summary>
namespace layers.Library
{
    /// <summary>
    /// This class will create the lambda zip package that can be upload to Lambda for deployment.
    /// </summary>
    public static class LambdaPackager
    {
        private const string Shebang = "#!";
        private const char LinuxLineEnding = '\n';
        private const string BootstrapFilename = "bootstrap";
        private static readonly string BuildLambdaZipCliPath = Path.Combine(
            Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath),
            "Resources\\build-lambda-zip.exe");

        static IDictionary<string, Version> NETSTANDARD_LIBRARY_VERSIONS = new Dictionary<string, Version>
        {
            { "netcoreapp1.0", Version.Parse("1.6.0") },
            { "netcoreapp1.1", Version.Parse("1.6.1") }
        };

        public static void BundleDirectory(string zipArchivePath, string sourceDirectory, bool flattenRuntime)
        {
#if NETCORE
            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                BundleWithBuildLambdaZip(zipArchivePath, sourceDirectory, flattenRuntime);
            }
            else
            {
                // Use the native zip utility if it exist which will maintain linux/osx file permissions
                var zipCLI = LambdaDotNetCLIWrapper.FindExecutableInPath("zip");
                if (!string.IsNullOrEmpty(zipCLI))
                {
                    BundleWithZipCLI(zipCLI, zipArchivePath, sourceDirectory, flattenRuntime);
                }
                else
                {
                    throw new LambdaToolsException("Failed to find the \"zip\" utility program in path. This program is required to maintain Linux file permissions in the zip archive.", LambdaToolsException.LambdaErrorCode.FailedToFindZipProgram);
                }
            }
#else
            BundleWithBuildLambdaZip(zipArchivePath, sourceDirectory, flattenRuntime);
#endif            
        }

        public static IDictionary<string, string> ConvertToMapOfFiles(string rootDirectory, string[] files)
        {
            rootDirectory = rootDirectory.Replace("\\", "/");
            if (!rootDirectory.EndsWith("/"))
                rootDirectory += "/";

            var includedFiles = new Dictionary<string, string>(files.Length);
            foreach (var file in files)
            {
                var normalizedFile = file.Replace("\\", "/");
                if (Path.IsPathRooted(file))
                {
                    var relativePath = file.Substring(rootDirectory.Length);
                    includedFiles[relativePath] = normalizedFile;
                }
                else
                {
                    includedFiles[normalizedFile] = Path.Combine(rootDirectory, normalizedFile).Replace("\\", "/");
                }
            }

            return includedFiles;
        }

        /// <summary>
        /// Get the list of files from the publish folder that should be added to the zip archive.
        /// This will skip all files in the runtimes folder because they have already been flatten to the root.
        /// </summary>
        /// <param name="publishLocation"></param>
        /// <param name="flattenRuntime">If true the runtimes folder will be flatten</param>
        /// <returns></returns>
        private static IDictionary<string, string> GetFilesToIncludeInArchive(string publishLocation, bool flattenRuntime)
        {
            string RUNTIME_FOLDER_PREFIX = "runtimes" + Path.DirectorySeparatorChar;

            var includedFiles = new Dictionary<string, string>();
            var allFiles = Directory.GetFiles(publishLocation, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                var relativePath = file.Substring(publishLocation.Length);
                if (relativePath[0] == Path.DirectorySeparatorChar)
                    relativePath = relativePath.Substring(1);

                if (flattenRuntime && relativePath.StartsWith(RUNTIME_FOLDER_PREFIX))
                    continue;

                includedFiles[relativePath] = file;
            }

            return includedFiles;
        }

        /// <summary>
        /// Zip up the publish folder using the build-lambda-zip utility which will maintain linux/osx file permissions.
        /// This is what is used when run on Windows.
        /// <param name="zipArchivePath">The path and name of the zip archive to create.</param>
        /// <param name="publishLocation">The location to be bundled.</param>
        /// <param name="flattenRuntime">If true the runtimes folder will be flatten</param>
        private static void BundleWithBuildLambdaZip(string zipArchivePath, string publishLocation, bool flattenRuntime)
        {
            var includedFiles = GetFilesToIncludeInArchive(publishLocation, flattenRuntime);
            BundleWithBuildLambdaZip(zipArchivePath, publishLocation, includedFiles);
        }

        /// <summary>
        /// Zip up the publish folder using the build-lambda-zip utility which will maintain linux/osx file permissions.
        /// This is what is used when run on Windows.
        /// </summary>
        /// <param name="zipArchivePath">The path and name of the zip archive to create.</param>
        /// <param name="rootDirectory">The root directory where all of the relative paths in includedFiles is pointing to.</param>
        /// <param name="includedFiles">Map of relative to absolute path of files to include in bundle.</param>
        private static void BundleWithBuildLambdaZip(string zipArchivePath, string rootDirectory, IDictionary<string, string> includedFiles)
        {
            if (!File.Exists(BuildLambdaZipCliPath))
            {
                throw new Exception("Failed to find the \"build-lambda-zip\" utility. This program is required to maintain Linux file permissions in the zip archive.");
            }

            EnsureBootstrapLinuxLineEndings(rootDirectory, includedFiles);

            //Write the files to disk to avoid the command line size limit when we have a large number of files to zip.            
            var inputFilename = zipArchivePath + ".txt";
            using (var writer = new StreamWriter(inputFilename))
            {
                foreach (var kvp in includedFiles)
                {
                    writer.WriteLine(kvp.Key);
                }
            }

            var args = new StringBuilder($"-o \"{zipArchivePath}\" -i \"{inputFilename}\"");

            var psiZip = new ProcessStartInfo
            {
                FileName = BuildLambdaZipCliPath,
                Arguments = args.ToString(),
                WorkingDirectory = rootDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var handler = (DataReceivedEventHandler)((o, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;
                Console.WriteLine("... zipping: " + e.Data);
            });

            try
            {
                using (var proc = new Process())
                {
                    proc.StartInfo = psiZip;
                    proc.Start();

                    proc.ErrorDataReceived += handler;
                    proc.OutputDataReceived += handler;
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    proc.EnableRaisingEvents = true;
                    proc.WaitForExit();

                    if (proc.ExitCode == 0)
                    {
                        Console.WriteLine(string.Format("Created publish archive ({0}).", zipArchivePath));
                    }
                }
            }
            finally
            {
                try
                {
                    File.Delete(inputFilename);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Warning: Unable to delete temporary input file, {inputFilename}, after zipping files: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Detects if there is a bootstrap file, and if it's a script (as opposed to an actual executable),
        /// and corrects the line endings so it can be run in Linux.
        /// 
        /// TODO: possibly expand to allow files other than bootstrap to be corrected
        /// </summary>
        /// <param name="rootDirectory"></param>
        /// <param name="includedFiles"></param>
        private static void EnsureBootstrapLinuxLineEndings(string rootDirectory, IDictionary<string, string> includedFiles)
        {
            if (includedFiles.ContainsKey(BootstrapFilename))
            {
                var bootstrapPath = Path.Combine(rootDirectory, BootstrapFilename);
                if (FileIsLinuxShellScript(bootstrapPath))
                {
                    var lines = File.ReadAllLines(bootstrapPath);
                    using (var sw = File.CreateText(bootstrapPath))
                    {
                        foreach (var line in lines)
                        {
                            sw.Write(line);
                            sw.Write(LinuxLineEnding);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the first characters of the file are #!, false otherwise.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private static bool FileIsLinuxShellScript(string filePath)
        {
            using (var sr = File.OpenText(filePath))
            {
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine().Trim();
                    if (line.Length > 0)
                    {
                        return line.StartsWith(Shebang);
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Creates the deployment bundle using the native zip tool installed
        /// on the system (default /usr/bin/zip). This is what is typically used on Linux and OSX
        /// </summary>
        /// <param name="zipCLI">The path to the located zip binary.</param>
        /// <param name="zipArchivePath">The path and name of the zip archive to create.</param>
        /// <param name="rootDirectory">The root directory where all of the relative paths in includedFiles is pointing to.</param>
        /// <param name="includedFiles">Map of relative to absolute path of files to include in bundle.</param>
        private static void BundleWithZipCLI(string zipCLI, string zipArchivePath, string rootDirectory, IDictionary<string, string> includedFiles)
        {
            EnsureBootstrapLinuxLineEndings(rootDirectory, includedFiles);

            var args = new StringBuilder("\"" + zipArchivePath + "\"");

            foreach (var kvp in includedFiles)
            {
                args.AppendFormat(" \"{0}\"", kvp.Key);
            }

            var psiZip = new ProcessStartInfo
            {
                FileName = zipCLI,
                Arguments = args.ToString(),
                WorkingDirectory = rootDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var handler = (DataReceivedEventHandler)((o, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;
                Console.WriteLine("... zipping: " + e.Data);
            });

            using (var proc = new Process())
            {
                proc.StartInfo = psiZip;
                proc.Start();

                proc.ErrorDataReceived += handler;
                proc.OutputDataReceived += handler;
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                proc.EnableRaisingEvents = true;
                proc.WaitForExit();

                if (proc.ExitCode == 0)
                {
                    Console.WriteLine(string.Format("Created publish archive ({0}).", zipArchivePath));
                }
            }
        }
    }
}

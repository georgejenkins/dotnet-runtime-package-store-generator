using CommandLine;
using layers.Library;
using System;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// Modified and unmodified code sourced from https://github.com/aws/aws-extensions-for-dotnet-cli 
/// Code used under the provisions of the Apache 2.0 Licence.
/// </summary>
namespace layers.Commands
{
    [Verb("create-local-layer", HelpText = "Create a lambda layer compatible runtime package store locally.")]
    class CreateLocalLayerOptions
    {
        [Option('m', "manifest", Required = true, HelpText = "Path to the project manifest to create the runtime package store for.")]
        public string Manifest { get; set; }

        [Option('l', "project-location", Required = false, HelpText = "The location of the project, if not set the current directory will be assumed.")]
        public string ProjectLocation { get; set; }

        [Option('f', "framework", Required = true, HelpText = "Target framework to compile, for example: netcoreapp2.1")]
        public string TargetFramework { get; set; }

        [Option('n', "storename", Required = true, HelpText = "The name to use for the generated runtime package store zip.")]
        public string StoreName { get; set; }

        [Option('p', "prejit", Required = false, HelpText = "If true the packages will be pre-jitted to improve cold start performance. This must done on an Amazon Linux environment.")]
        public bool EnableOptimization { get; set; }
    }

    static class CreateLocalLayer
    {
        public static int Execute(CreateLocalLayerOptions opts)
        {
            Console.WriteLine($"Creating runtime package store from manifest: {opts.Manifest}");

            if (opts.EnableOptimization)
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    throw new Exception($"Package optimization is only possible on Amazon Linux. To use this feature execute the command in an Amazon Linux environment.");
                }
                else
                {
                    Console.WriteLine("Warning: Package optimization has been enabled. Be sure to run this on an Amazon Linux environment or the optimization might not be compatbile with the Lambda runtime.");
                }
            }

            if (!File.Exists(opts.Manifest))
            {
                throw new Exception($"Can not find package manifest {opts.Manifest}. Make sure to point to a file not a directory.");
            }

            var tempDirectoryName = $"{opts.StoreName}-{DateTime.UtcNow.Ticks}".ToLower();

            var tempRootPath = Path.Combine(Path.GetTempPath(), tempDirectoryName);
            var storeOutputDirectory = Path.Combine(tempRootPath, Common.Constants.DEFAULT_LAYER_OPT_DIRECTORY);

            var convertResult = ManifestUtilities.ConvertManifestToSdkManifest(opts.Manifest);

            if (convertResult.ShouldDelete)
            {
                Console.WriteLine("Converted ASP.NET Core project file to temporary package manifest file.");
            }

            var cliWrapper = new LambdaDotNetCLIWrapper(Directory.GetCurrentDirectory());
            var storeResult = cliWrapper.Store(
                !string.IsNullOrEmpty(opts.ProjectLocation) ? opts.ProjectLocation : Directory.GetCurrentDirectory(),
                storeOutputDirectory,
                opts.TargetFramework,
                convertResult.PackageManifest,
                opts.EnableOptimization);

            if (storeResult.exitCode != 0)
            {
                throw new Exception($"Error executing the 'dotnet store' command");
            }

            var manifest = ProjectUtilities.FindArtifactOutput(storeResult.filePath);

            //string[] files = Directory.GetFiles(storeResult.filePath, "artifact.xml", SearchOption.AllDirectories);

            var updatedContent = File.ReadAllText(manifest);
            var manifestPath = Path.Combine(Directory.GetCurrentDirectory(), $"{opts.StoreName}.xml");
            File.WriteAllText(manifestPath, updatedContent);

            Console.WriteLine($"Created package manifest file ({manifestPath})");

            if (convertResult.ShouldDelete)
            {
                File.Delete(convertResult.PackageManifest);
            }

            var zipPath = Path.Combine(Directory.GetCurrentDirectory(), $"{opts.StoreName}.zip");
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            LambdaPackager.BundleDirectory(zipPath, tempRootPath, false);

            return 0;
        }
    }
}

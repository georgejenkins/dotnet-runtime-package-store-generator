using System;
using System.IO;
using System.Xml.Linq;
using System.Xml.XPath;

/// <summary>
/// Modified and unmodified code sourced from https://github.com/aws/aws-extensions-for-dotnet-cli 
/// Code used under the provisions of the Apache 2.0 Licence.
/// </summary>
namespace layers.Library
{
    public static class ManifestUtilities
    {
        public static readonly Version MINIMUM_DOTNET_SDK_VERSION_FOR_ASPNET_LAYERS = new Version("2.2.100");

        public class ConvertManifestToSdkManifestResult
        {
            public bool ShouldDelete { get; }
            public string PackageManifest { get; }

            public ConvertManifestToSdkManifestResult(bool shouldDelete, string packageManifest)
            {
                ShouldDelete = shouldDelete;
                PackageManifest = packageManifest;
            }
        }

        public static ConvertManifestToSdkManifestResult ConvertManifestToSdkManifest(string packageManifest)
        {
            var content = File.ReadAllText(packageManifest);

            var result = ConvertManifestContentToSdkManifest(content);

            if (!result.Updated)
            {
                return new ConvertManifestToSdkManifestResult(false, packageManifest);
            }

            var newPath = Path.GetTempFileName();
            File.WriteAllText(newPath, result.UpdatedContent);
            return new ConvertManifestToSdkManifestResult(true, newPath);

        }

        public class ConvertManifestContentToSdkManifestResult
        {
            public bool Updated { get; }
            public string UpdatedContent { get; }

            public ConvertManifestContentToSdkManifestResult(bool updated, string updatedContent)
            {
                this.Updated = updated;
                this.UpdatedContent = updatedContent;
            }
        }

        public static ConvertManifestContentToSdkManifestResult ConvertManifestContentToSdkManifest(string packageManifestContent)
        {
            var originalDoc = XDocument.Parse(packageManifestContent);

            var attr = originalDoc.Root.Attribute("Sdk");
            if (string.Equals(attr?.Value, "Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase))
                return new ConvertManifestContentToSdkManifestResult(false, packageManifestContent);


            var root = new XElement("Project");
            root.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");

            var itemGroup = new XElement("ItemGroup");
            root.Add(itemGroup);


            Version dotnetSdkVersion;
            try
            {
                dotnetSdkVersion = GetSdkVersion();
            }
            catch (Exception e)
            {
                throw new Exception("Error detecting .NET SDK version: \n\t" + e.Message, e);
            }

            if (dotnetSdkVersion < MINIMUM_DOTNET_SDK_VERSION_FOR_ASPNET_LAYERS)
            {
                throw new Exception($"To create a runtime package store layer for an ASP.NET Core project " +
                                               $"version {MINIMUM_DOTNET_SDK_VERSION_FOR_ASPNET_LAYERS} " +
                                               "or above of the .NET Core SDK must be installed. " +
                                               "If a 2.1.X SDK is used then the \"dotnet store\" command will include all " +
                                               "of the ASP.NET Core dependencies that are already available in Lambda.");
            }

            // These were added to make sure the ASP.NET Core dependencies are filter if any of the packages
            // depend on them.
            // See issue for more info: https://github.com/dotnet/cli/issues/10784
            var aspNerCorePackageReference = new XElement("PackageReference");
            aspNerCorePackageReference.SetAttributeValue("Include", "Microsoft.AspNetCore.App");
            itemGroup.Add(aspNerCorePackageReference);

            var aspNerCoreUpdatePackageReference = new XElement("PackageReference");
            aspNerCoreUpdatePackageReference.SetAttributeValue("Update", "Microsoft.NETCore.App");
            aspNerCoreUpdatePackageReference.SetAttributeValue("Publish", "false");
            itemGroup.Add(aspNerCoreUpdatePackageReference);

            foreach (var packageReference in originalDoc.XPathSelectElements("//ItemGroup/PackageReference"))
            {
                var packageName = packageReference.Attribute("Include")?.Value;
                var version = packageReference.Attribute("Version")?.Value;

                if (string.Equals(packageName, "Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(packageName, "Microsoft.AspNetCore.All", StringComparison.OrdinalIgnoreCase))
                    continue;

                var newRef = new XElement("PackageReference");
                newRef.SetAttributeValue("Include", packageName);
                newRef.SetAttributeValue("Version", version);
                itemGroup.Add(newRef);
            }

            var updatedDoc = new XDocument(root);
            var updatedContent = updatedDoc.ToString();

            return new ConvertManifestContentToSdkManifestResult(true, updatedContent);
        }

        public static Version GetSdkVersion()
        {
            var dotnetCLI = CLIWrapper.FindExecutableInPath("dotnet.exe");
            if (dotnetCLI == null)
                dotnetCLI = CLIWrapper.FindExecutableInPath("dotnet");
            if (string.IsNullOrEmpty(dotnetCLI))
                throw new Exception("Failed to locate dotnet CLI executable. Make sure the dotnet CLI is installed in the environment PATH.");

            var results = CLIWrapper.ExecuteShellCommand(null, dotnetCLI, "--list-sdks");
            if (results.ExitCode != 0)
                throw new Exception("Command \"dotnet --list-sdks\" failed, captured output: \n" + results.Stdout);


            var maxSdkVersion = ParseListSdkOutput(results.Stdout);
            if (maxSdkVersion == null)
            {
                throw new Exception("Failed to parse latest SDK version from captured output:\n" + results.Stdout);
            }

            return maxSdkVersion;
        }

        public static Version ParseListSdkOutput(string listSdkOutput)
        {
            var outputLines = listSdkOutput.Split('\n');
            for (int i = outputLines.Length - 1; i >= 0; i--)
            {
                var line = outputLines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var tokens = line.Split(' ');
                // There should be at least 2 tokens, the version and the path to the SDK. There might be more than 2 tokens if the path to the SDK contained spaces.
                if (tokens.Length < 2)
                    continue;

                if (Version.TryParse(tokens[0], out var version))
                {
                    return version;
                }
            }

            return null;
        }
    }
}

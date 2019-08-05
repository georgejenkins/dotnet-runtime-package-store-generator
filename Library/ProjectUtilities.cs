using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Modified and unmodified code sourced from https://github.com/aws/aws-extensions-for-dotnet-cli 
/// Code used under the provisions of the Apache 2.0 Licence.
/// </summary>
namespace layers.Library
{
    public static class ProjectUtilities
    {
        /// <summary>
        /// Determines the location of the project depending on how the workingDirectory and projectLocation
        /// fields are set. This location is root of the project.
        /// </summary>
        /// <param name="workingDirectory"></param>
        /// <param name="projectLocation"></param>
        /// <returns></returns>
        public static string DetermineProjectLocation(string workingDirectory, string projectLocation)
        {
            string location;
            if (string.IsNullOrEmpty(projectLocation))
            {
                location = workingDirectory;
            }
            else if (string.IsNullOrEmpty(workingDirectory))
            {
                location = projectLocation;
            }
            else
            {
                location = Path.IsPathRooted(projectLocation) ? projectLocation : Path.Combine(workingDirectory, projectLocation);
            }

            if (location.EndsWith(@"\") || location.EndsWith(@"/"))
                location = location.Substring(0, location.Length - 1);

            return location;
        }

        public static string FindArtifactOutput(string parentDirectory)
        {
            foreach (var arch in Directory.GetDirectories(parentDirectory))
            {
                foreach (var framework in Directory.GetDirectories(arch))
                {
                    string[] files = Directory.GetFiles(framework, "artifact.xml", SearchOption.TopDirectoryOnly);

                    if (files.Any())
                    {
                        return files.FirstOrDefault();
                    }
                }
            }

            return null;
        }
    }
}

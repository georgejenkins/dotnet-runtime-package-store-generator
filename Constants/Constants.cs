/// <summary>
/// Modified and unmodified code sourced from https://github.com/aws/aws-extensions-for-dotnet-cli 
/// Code used under the provisions of the Apache 2.0 Licence.
/// </summary>
namespace layers.Common
{
    public static class Constants
    {
        // The closest match to Amazon Linux
        public const string RUNTIME_HIERARCHY_STARTING_POINT = "rhel.7.2-x64";

        // The directory under the /opt directory the contents of the layer will be placed. 
        public const string DEFAULT_LAYER_OPT_DIRECTORY = "dotnetcore/store";

        // The .NET Core 1.0 version of the runtime hierarchies for .NET Core taken from the corefx repository
        // https://github.com/dotnet/corefx/blob/release/1.0.0/pkg/Microsoft.NETCore.Platforms/runtime.json
        public const string RUNTIME_HIERARCHY = "netcore.runtime.hierarchy.json";
    }
}

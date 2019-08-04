using CommandLine;
using layers.Commands;

namespace layers
{
    class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<CreateLocalLayerOptions, object>(args) 
                .MapResult(
                (CreateLocalLayerOptions opts) => CreateLocalLayer.Execute(opts),
                errs => 1);
        }
    }
}

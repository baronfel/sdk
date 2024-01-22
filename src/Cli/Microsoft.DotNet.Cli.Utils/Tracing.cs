namespace Microsoft.DotNet.Cli.Utils;

#if NET8_0_OR_GREATER
using System.Diagnostics;
#endif

public static class Tracing
{

#if NET8_0_OR_GREATER
    public static string SourceName = "dotnetcli";
    public static ActivitySource Source = new ActivitySource(SourceName, Product.Version);
#endif
}

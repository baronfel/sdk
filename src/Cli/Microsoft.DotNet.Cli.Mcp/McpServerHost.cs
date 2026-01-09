// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DotNet.Cli.Mcp;

/// <summary>
/// Hosts the MCP server for the dotnet CLI.
/// This mode exposes all CLI commands as MCP tools for AI assistants.
/// </summary>
public static class McpServerHost
{
    /// <summary>
    /// Starts the MCP server with stdio transport.
    /// </summary>
    /// <param name="rootCommand">The root command of the CLI</param>
    /// <param name="invokeCommand">Function to invoke CLI commands</param>
    /// <param name="productVersion">The version of the dotnet CLI</param>
    /// <param name="cancellationToken">Cancellation token for shutdown</param>
    /// <returns>Exit code (0 for success)</returns>
    public static async Task<int> RunAsync(
        System.CommandLine.Command rootCommand,
        string productVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder();

            // Register the tool provider as a singleton
            builder.Services.AddSingleton(sp => new DotnetCliToolProvider(rootCommand));

            // Configure MCP server with stdio transport
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new()
                    {
                        Name = "dotnet-cli",
                        Version = productVersion
                    };
                })
                .WithStdioServerTransport()
                .WithTools(builder.Services.BuildServiceProvider().GetRequiredService<DotnetCliToolProvider>().GetTools());

            var host = builder.Build();

            // Run the server until cancellation
            await host.RunAsync(cancellationToken);

            return 0;
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MCP Server error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("DOTNET_CLI_MCP_DEBUG") == "1")
            {
                Console.Error.WriteLine(ex.ToString());
            }
            return 1;
        }
    }
}

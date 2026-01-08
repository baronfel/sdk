// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Cli.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DotNet.Cli.Commands.Mcp;

/// <summary>
/// Entry point for running the dotnet CLI in MCP server mode.
/// This mode exposes all CLI commands as MCP tools for AI assistants.
/// </summary>
public static class McpServerMode
{
    /// <summary>
    /// Starts the MCP server with stdio transport.
    /// </summary>
    /// <param name="args">Additional arguments for server configuration</param>
    /// <returns>Exit code (0 for success)</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Register the tool provider as a singleton
            builder.Services.AddSingleton<DotnetCliToolProvider>();

            // Configure MCP server with stdio transport
            // Tools are automatically registered from the DotnetCliToolProvider
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new()
                    {
                        Name = "dotnet-cli",
                        Version = Product.Version
                    };
                })
                .WithStdioServerTransport()
                .WithTools(new DotnetCliToolProvider().GetTools());

            var host = builder.Build();

            // Run the server until cancellation
            await host.RunAsync();

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

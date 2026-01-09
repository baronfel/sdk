// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace Microsoft.DotNet.Cli.Mcp;

/// <summary>
/// Provides the --mcp option for starting the dotnet CLI in MCP server mode.
/// </summary>
public class McpOption : Option<bool>
{
    private readonly string _productVersion;

    /// <summary>
    /// Creates the --mcp option that starts the MCP server.
    /// </summary>
    /// <param name="rootCommand">The root command of the CLI</param>
    /// <param name="invokeCommand">Function to invoke CLI commands</param>
    /// <param name="productVersion">The version of the dotnet CLI</param>
    /// <returns>An option configured to start the MCP server</returns>


    public McpOption(string productVersion) : base("--mcp")
    {
        Description = "Starts the dotnet CLI in MCP server mode for AI assistants.";
        Hidden = false;
        Recursive = false;
        Arity = ArgumentArity.Zero;

        _productVersion = productVersion;
    }

    public override CommandLineAction? Action => new McpServerAction(_productVersion);


    /// <summary>
    /// Action that starts the MCP server when the --mcp option is used.
    /// This is a terminating action, so System.CommandLine won't continue processing after this runs.
    /// </summary>
    private sealed class McpServerAction : AsynchronousCommandLineAction
    {
        private readonly string _productVersion;

        public McpServerAction(string productVersion)
        {
            _productVersion = productVersion;
        }

        public override bool Terminating => true;

        public override async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken)
        {
            // Start the MCP server
            return await McpServerHost.RunAsync(
                parseResult.CommandResult.Command,
                _productVersion,
                cancellationToken);
        }
    }
}


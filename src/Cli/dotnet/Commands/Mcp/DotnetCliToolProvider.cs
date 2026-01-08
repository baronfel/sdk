// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ModelContextProtocol.Server;
using SysCommand = System.CommandLine.Command;

namespace Microsoft.DotNet.Cli.Commands.Mcp;

/// <summary>
/// Provides MCP tools for all dotnet CLI commands.
/// </summary>
public class DotnetCliToolProvider
{
    private readonly IEnumerable<SysCommand> _leafCommands;

    public DotnetCliToolProvider()
    {
        // Build the command map during initialization
        _leafCommands = BuildLeafCommands();
    }

    /// <summary>
    /// Gets all MCP tools from the dotnet CLI commands.
    /// </summary>
    public IEnumerable<McpServerTool> GetTools()
    {
        foreach (var command in _leafCommands)
        {
            yield return new CommandLineMcpTool(command);
        }
    }

    private IEnumerable<SysCommand> BuildLeafCommands() => Parser.Subcommands.SelectMany(ProcessCommand);

    private IEnumerable<SysCommand> ProcessCommand(SysCommand command)
    {

        // Check if this is a leaf command (has a handler or no subcommands)
        bool isLeaf = command.Subcommands.Count == 0 || command.Action != null;

        if (isLeaf && !command.Hidden)
        {
            yield return command;
        }

        // Process subcommands
        foreach (var subcommand in command.Subcommands)
        {
            foreach (var leaf in ProcessCommand(subcommand))
            {
                yield return leaf;
            }
        }
    }
}

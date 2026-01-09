// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using McpTool = ModelContextProtocol.Protocol.Tool;
using SysArgument = System.CommandLine.Argument;
using SysOption = System.CommandLine.Option;

namespace Microsoft.DotNet.Cli.Mcp;

/// <summary>
/// MCP tool implementation backed by a System.CommandLine Command.
/// Handles parameter translation, command invocation, and response transformation.
/// </summary>
public class CommandLineMcpTool : McpServerTool
{
    private readonly Command _command;
    private readonly string _toolName;
    private readonly string[] _commandTokens;
    private readonly IReadOnlyList<SysArgument> _arguments;
    private readonly IReadOnlyList<SysOption> _options;

    private static ParserConfiguration s_parserConfiguration = new()
    {
        EnablePosixBundling = false,
    };

    private static InvocationConfiguration s_invocationConfiguration = new()
    {
        EnableDefaultExceptionHandler = false
    };

    public CommandLineMcpTool(Command command)
    {
        _command = command;
        _commandTokens = GetCommandPath(command);
        _toolName = string.Join("_", _commandTokens);

        // Collect arguments and options once during initialization
        _arguments = CollectArguments(command);
        _options = CollectOptions(command);
    }

    public override IReadOnlyList<object> Metadata => Array.Empty<object>();

    public override McpTool ProtocolTool => new()
    {
        Name = _toolName,
        Description = _command.Description ?? $"Executes: {string.Join(" ", _commandTokens)}",
        InputSchema = CommandSchemaBuilder.BuildSchema(_arguments, _options)
    };

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Convert JSON parameters to command-line arguments
            string[] args;
            if (context?.Params?.Arguments is IDictionary<string, JsonElement> parameterDict)
            {
                args = ParameterConverter.ConvertToArgs(_arguments, _options, parameterDict);
            }
            else
            {
                args = [];
            }

            string[] fullArgs = [.._commandTokens, ..args];

            // Invoke the command and capture the exit code
            int exitCode = await _command.Parse(fullArgs, s_parserConfiguration).InvokeAsync(s_invocationConfiguration, cancellationToken);

            // Build response text
            var responseText = $"Command completed with exit code: {exitCode}";

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = responseText }
                },
                IsError = exitCode != 0
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock
                    {
                        Text = $"Exception executing command: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}"
                    }
                },
                IsError = true
            };
        }
    }

    private static string[] GetCommandPath(Command command)
    {
        var path = new List<string>();
        var current = command;

        while (current != null && !string.IsNullOrEmpty(current.Name) && current.Name != "dotnet")
        {
            path.Insert(0, current.Name);
            current = current.Parents.OfType<Command>().FirstOrDefault();
        }

        return ["dotnet", ..path];
    }

    /// <summary>
    /// Collects all arguments from the command and its parent commands, excluding hidden ones.
    /// </summary>
    private static IReadOnlyList<SysArgument> CollectArguments(Command command)
    {
        var arguments = new List<SysArgument>();
        var current = command;

        // Collect all arguments from the command and its parents
        while (current != null)
        {
            // Add arguments from this level (in reverse order since we're walking up)
            arguments.InsertRange(0, current.Arguments);
            current = current.Parents.OfType<Command>().FirstOrDefault();
        }

        // Filter out hidden arguments
        return arguments.Where(arg => !arg.Hidden).ToList();
    }

    /// <summary>
    /// Collects all options from the command and recursive options from parent commands,
    /// excluding hidden options and filtered global options.
    /// </summary>
    private static IReadOnlyList<SysOption> CollectOptions(Command command)
    {
        var allOptions = new List<SysOption>(command.Options);
        var current = command.Parents.OfType<Command>().FirstOrDefault();

        // Collect recursive options from parent commands
        while (current != null)
        {
            allOptions.AddRange(current.Options.Where(o => o.Recursive));
            current = current.Parents.OfType<Command>().FirstOrDefault();
        }

        // Filter out hidden options and global options
        var filteredOptionNames = new[] { "--help", "-h", "--version", "--verbosity", "--diagnostics", "-d" };
        return allOptions
            .Where(opt => !opt.Hidden)
            .Where(opt => !filteredOptionNames.Any(name => opt.Name == name || opt.Aliases.Contains(name)))
            .ToList();
    }
}

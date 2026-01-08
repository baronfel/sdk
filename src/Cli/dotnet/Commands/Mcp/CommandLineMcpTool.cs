// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using McpTool = ModelContextProtocol.Protocol.Tool;

namespace Microsoft.DotNet.Cli.Commands.Mcp;

/// <summary>
/// MCP tool implementation backed by a System.CommandLine Command.
/// Handles parameter translation, command invocation, and response transformation.
/// </summary>
public class CommandLineMcpTool : McpServerTool
{
    private readonly Command _command;
    private readonly string _toolName;
    private readonly string[] _commandTokens;

    public CommandLineMcpTool(Command command)
    {
        _command = command;
        _commandTokens = GetCommandPath(command);
        _toolName = string.Join("_", _commandTokens);
    }

    public override IReadOnlyList<object> Metadata => Array.Empty<object>();

    public override McpTool ProtocolTool => new()
    {
        Name = _toolName,
        Description = _command.Description ?? $"Executes: {string.Join(" ", _commandTokens)}",
        InputSchema = CommandSchemaBuilder.BuildSchema(_command)
    };

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Convert JSON parameters to command-line arguments
            // Convert the arguments dictionary to JsonNode for ParameterConverter
            JsonNode? parametersNode = null;
            if (context?.Params?.Arguments != null)
            {
                var parametersJson = JsonSerializer.SerializeToElement(context.Params.Arguments);
                parametersNode = JsonNode.Parse(parametersJson.GetRawText());
            }

            var args = ParameterConverter.ConvertToArgs(_command, parametersNode);
            string[] fullArgs = [.._commandTokens, ..args];

            // Capture output using StringWriter
            using var outputWriter = new StringWriter();
            using var errorWriter = new StringWriter();

            var originalOut = Console.Out;
            var originalError = Console.Error;

            int exitCode;
            try
            {
                Console.SetOut(outputWriter);
                Console.SetError(errorWriter);

                // Parse and invoke the command.
                // Set up S.CL output channels to align with our per-tool writers to prevent clobbering - though
                // commands will often use the Reporter infrastructure too :(
                InvocationConfiguration invocationConfiguration = new ()
                {
                    EnableDefaultExceptionHandler = Parser.InvocationConfiguration.EnableDefaultExceptionHandler,
                    Output = outputWriter,
                    Error = errorWriter
                };
                exitCode = await Parser.Parse(fullArgs).InvokeAsync(invocationConfiguration, cancellationToken);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            var output = outputWriter.ToString();
            var error = errorWriter.ToString();

            // Build response text
            var responseText = new System.Text.StringBuilder();
            responseText.AppendLine($"Exit Code: {exitCode}");

            if (!string.IsNullOrEmpty(output))
            {
                responseText.AppendLine();
                responseText.AppendLine("Output:");
                responseText.AppendLine(output);
            }

            if (!string.IsNullOrEmpty(error))
            {
                responseText.AppendLine();
                responseText.AppendLine("Errors:");
                responseText.AppendLine(error);
            }

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = responseText.ToString() }
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
}

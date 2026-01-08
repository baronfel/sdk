// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Text.Json.Nodes;

namespace Microsoft.DotNet.Cli.Commands.Mcp;

/// <summary>
/// Converts JSON parameters from MCP tool calls into command-line arguments
/// for System.CommandLine.
/// </summary>
public static class ParameterConverter
{
    /// <summary>
    /// Converts JSON parameters to an args array for a System.CommandLine command.
    /// </summary>
    /// <param name="command">The command to build args for</param>
    /// <param name="parameters">JSON parameters from MCP tool call</param>
    /// <returns>Array of command-line arguments</returns>
    public static string[] ConvertToArgs(Command command, JsonNode? parameters)
    {
        var args = new List<string>();

        if (parameters is not JsonObject paramObj)
        {
            return args.ToArray();
        }

        // Add arguments (positional)
        foreach (var argument in command.Arguments)
        {
            var argName = ToSnakeCase(argument.Name);
            if (paramObj.TryGetPropertyValue(argName, out var value) && value != null)
            {
                AddArgumentValue(args, value, argument);
            }
        }

        // Add options (flags)
        foreach (var option in command.Options)
        {
            if (IsGlobalOption(option))
            {
                continue;
            }

            var optName = ToSnakeCase(option.Name.TrimStart('-'));
            if (paramObj.TryGetPropertyValue(optName, out var value) && value != null)
            {
                AddOptionValue(args, option, value);
            }
        }

        return args.ToArray();
    }

    private static void AddArgumentValue(List<string> args, JsonNode value, Argument argument)
    {
        if (value is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item != null)
                {
                    args.Add(item.ToString());
                }
            }
        }
        else
        {
            var stringValue = value.ToString();
            if (!string.IsNullOrEmpty(stringValue))
            {
                args.Add(stringValue);
            }
        }
    }

    private static void AddOptionValue(List<string> args, Option option, JsonNode value)
    {
        var optionName = GetPreferredOptionName(option);

        // Handle boolean flags
        if (option.ValueType == typeof(bool))
        {
            if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var boolValue) && boolValue)
            {
                args.Add(optionName);
            }
            return;
        }

        // Handle array values
        if (value is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item != null)
                {
                    args.Add(optionName);
                    args.Add(item.ToString());
                }
            }
            return;
        }

        // Handle single values
        var stringValue = value.ToString();
        if (!string.IsNullOrEmpty(stringValue) && stringValue != "null")
        {
            args.Add(optionName);
            args.Add(stringValue);
        }
    }

    private static string GetPreferredOptionName(Option option)
    {
        // Prefer long form (--name) over short form (-n)
        if (!string.IsNullOrEmpty(option.Name))
        {
            return option.Name.StartsWith("--") ? option.Name : $"--{option.Name.TrimStart('-')}";
        }

        // Fall back to first alias
        var alias = option.Aliases.FirstOrDefault() ?? "--unknown";
        return alias.StartsWith("--") ? alias : $"--{alias.TrimStart('-')}";
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = new System.Text.StringBuilder();
        var previousWasUpper = false;

        for (int i = 0; i < input.Length; i++)
        {
            var current = input[i];

            if (current == '-')
            {
                result.Append('_');
                previousWasUpper = false;
                continue;
            }

            if (char.IsUpper(current))
            {
                if (i > 0 && !previousWasUpper && result.Length > 0 && result[result.Length - 1] != '_')
                {
                    result.Append('_');
                }
                result.Append(char.ToLowerInvariant(current));
                previousWasUpper = true;
            }
            else
            {
                result.Append(current);
                previousWasUpper = false;
            }
        }

        return result.ToString();
    }

    private static bool IsGlobalOption(Option option)
    {
        var globalOptionNames = new[] { "--help", "-h", "--version", "-v", "--verbosity", "--diagnostics", "-d" };
        return globalOptionNames.Any(name => option.Name == name || option.Aliases.Contains(name));
    }
}

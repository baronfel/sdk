// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.DotNet.Cli.Commands.Mcp;

/// <summary>
/// Builds JSON schemas for MCP tools from System.CommandLine commands.
/// </summary>
public static class CommandSchemaBuilder
{
    /// <summary>
    /// Builds a JSON schema for an MCP tool from a System.CommandLine command.
    /// </summary>
    public static JsonElement BuildSchema(Command command)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        // Add arguments
        foreach (var argument in command.Arguments)
        {
            var argName = ToSnakeCase(argument.Name);
            properties[argName] = BuildArgumentSchema(argument);

            if (argument.Arity.MinimumNumberOfValues > 0)
            {
                required.Add(argName);
            }
        }

        // Add options
        foreach (var option in command.Options)
        {
            // Skip global options that are handled separately
            if (IsGlobalOption(option))
            {
                continue;
            }

            var optName = ToSnakeCase(option.Name.TrimStart('-'));
            properties[optName] = BuildOptionSchema(option);
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        // Convert JsonObject to JsonElement
        return JsonSerializer.SerializeToElement(schema);
    }

    private static JsonObject BuildArgumentSchema(Argument argument)
    {
        var schema = new JsonObject
        {
            ["description"] = argument.Description ?? $"Argument: {argument.Name}"
        };

        var jsonType = GetJsonType(argument.ValueType);
        schema["type"] = jsonType;

        // Handle array types
        if (argument.Arity.MaximumNumberOfValues > 1 || argument.Arity.MaximumNumberOfValues == int.MaxValue)
        {
            schema["type"] = "array";
            schema["items"] = new JsonObject { ["type"] = jsonType };
        }

        return schema;
    }

    private static JsonObject BuildOptionSchema(Option option)
    {
        var schema = new JsonObject
        {
            ["description"] = option.Description ?? $"Option: {option.Name}"
        };

        var jsonType = GetJsonType(option.ValueType);

        // Handle boolean flags
        if (option.ValueType == typeof(bool))
        {
            schema["type"] = "boolean";
            schema["default"] = false;
        }
        // Handle array options
        else if (option.Arity.MaximumNumberOfValues > 1 || option.Arity.MaximumNumberOfValues == int.MaxValue)
        {
            schema["type"] = "array";
            schema["items"] = new JsonObject { ["type"] = jsonType };
        }
        else
        {
            schema["type"] = jsonType;
        }

        return schema;
    }

    private static string GetJsonType(Type type)
    {
        // Unwrap nullable types
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        // Handle common types
        if (underlyingType == typeof(bool))
            return "boolean";
        if (underlyingType == typeof(int) || underlyingType == typeof(long) ||
            underlyingType == typeof(short) || underlyingType == typeof(byte) ||
            underlyingType == typeof(uint) || underlyingType == typeof(ulong) ||
            underlyingType == typeof(ushort) || underlyingType == typeof(sbyte))
            return "integer";
        if (underlyingType == typeof(float) || underlyingType == typeof(double) || underlyingType == typeof(decimal))
            return "number";

        // Handle enums
        if (underlyingType.IsEnum)
            return "string";

        // Handle arrays and collections
        if (underlyingType.IsArray ||
            (underlyingType.IsGenericType &&
             (underlyingType.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
              underlyingType.GetGenericTypeDefinition() == typeof(List<>) ||
              underlyingType.GetGenericTypeDefinition() == typeof(IList<>))))
        {
            return GetJsonType(underlyingType.GetElementType() ?? underlyingType.GetGenericArguments()[0]);
        }

        // Default to string
        return "string";
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

            // Skip hyphens and convert to underscore
            if (current == '-')
            {
                result.Append('_');
                previousWasUpper = false;
                continue;
            }

            // Add underscore before uppercase letters (except at start)
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
        // Skip common global options that appear on all commands
        var globalOptionNames = new[] { "--help", "-h", "--version", "-v", "--verbosity", "--diagnostics", "-d" };
        return globalOptionNames.Any(name => option.Name == name || option.Aliases.Contains(name));
    }
}

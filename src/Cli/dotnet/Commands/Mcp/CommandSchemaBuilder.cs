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
    /// Builds a JSON schema for an MCP tool from pre-collected arguments and options.
    /// </summary>
    public static JsonElement BuildSchema(IEnumerable<Argument> arguments, IEnumerable<Option> options)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        // Add arguments
        foreach (var argument in arguments)
        {
            var argName = ToSnakeCase(argument.Name);
            properties[argName] = BuildArgumentSchema(argument);

            if (argument.Arity.MinimumNumberOfValues > 0)
            {
                required.Add(argName);
            }
        }

        // Add options (deduplicating by name)
        var addedOptions = new HashSet<string>();
        foreach (var option in options)
        {
            var optName = ToSnakeCase(option.Name.TrimStart('-'));

            // Skip duplicates (same option name from multiple levels)
            if (addedOptions.Contains(optName))
            {
                continue;
            }

            addedOptions.Add(optName);
            properties[optName] = BuildOptionSchema(option);

            // Mark option as required if it's required
            if (option.Required)
            {
                required.Add(optName);
            }
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
        var schema = new JsonObject();

        // Build description with arity information
        var descriptionParts = new List<string>();
        if (!string.IsNullOrEmpty(argument.Description))
        {
            descriptionParts.Add(argument.Description);
        }

        // Add arity information to description
        if (argument.Arity.MinimumNumberOfValues > 0 || argument.Arity.MaximumNumberOfValues < int.MaxValue)
        {
            var arityDesc = argument.Arity.MinimumNumberOfValues == argument.Arity.MaximumNumberOfValues
                ? $"Exactly {argument.Arity.MinimumNumberOfValues} value(s)"
                : argument.Arity.MaximumNumberOfValues == int.MaxValue
                    ? $"At least {argument.Arity.MinimumNumberOfValues} value(s)"
                    : $"Between {argument.Arity.MinimumNumberOfValues} and {argument.Arity.MaximumNumberOfValues} value(s)";
            descriptionParts.Add($"[{arityDesc}]");
        }

        schema["description"] = string.Join(" ", descriptionParts);

        var jsonType = GetJsonType(argument.ValueType);

        // Handle array types
        if (argument.Arity.MaximumNumberOfValues > 1 || argument.Arity.MaximumNumberOfValues == int.MaxValue)
        {
            schema["type"] = "array";
            var itemSchema = new JsonObject { ["type"] = jsonType };

            // Add enum values if applicable
            AddEnumValues(itemSchema, argument.ValueType);

            schema["items"] = itemSchema;

            // Add array constraints based on arity
            if (argument.Arity.MinimumNumberOfValues > 0)
            {
                schema["minItems"] = argument.Arity.MinimumNumberOfValues;
            }
            if (argument.Arity.MaximumNumberOfValues < int.MaxValue)
            {
                schema["maxItems"] = argument.Arity.MaximumNumberOfValues;
            }
        }
        else
        {
            schema["type"] = jsonType;
            AddEnumValues(schema, argument.ValueType);
        }

        return schema;
    }

    private static JsonObject BuildOptionSchema(Option option)
    {
        var schema = new JsonObject();

        // Build description with additional metadata
        var descriptionParts = new List<string>();
        if (!string.IsNullOrEmpty(option.Description))
        {
            descriptionParts.Add(option.Description);
        }

        // Add aliases to description if present
        if (option.Aliases.Count > 1)
        {
            var aliases = string.Join(", ", option.Aliases.Where(a => a != option.Name));
            if (!string.IsNullOrEmpty(aliases))
            {
                descriptionParts.Add($"[Aliases: {aliases}]");
            }
        }

        schema["description"] = string.Join(" ", descriptionParts);

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
            var itemSchema = new JsonObject { ["type"] = jsonType };

            // Add enum values if applicable
            AddEnumValues(itemSchema, option.ValueType);

            schema["items"] = itemSchema;

            // Add array constraints based on arity
            if (option.Arity.MinimumNumberOfValues > 0)
            {
                schema["minItems"] = option.Arity.MinimumNumberOfValues;
            }
            if (option.Arity.MaximumNumberOfValues < int.MaxValue)
            {
                schema["maxItems"] = option.Arity.MaximumNumberOfValues;
            }
        }
        else
        {
            schema["type"] = jsonType;
            AddEnumValues(schema, option.ValueType);
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

    private static void AddEnumValues(JsonObject schema, Type type)
    {
        // Unwrap nullable types
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        // Check if it's an enum type
        if (underlyingType.IsEnum)
        {
            var enumValues = new JsonArray();
            foreach (var value in Enum.GetNames(underlyingType))
            {
                enumValues.Add(value);
            }

            if (enumValues.Count > 0)
            {
                schema["enum"] = enumValues;
            }
        }
    }
}

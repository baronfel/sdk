// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Text.Json;

namespace Microsoft.DotNet.Cli.Commands.Mcp;

/// <summary>
/// Converts JSON parameters from MCP tool calls into command-line arguments
/// for System.CommandLine.
/// </summary>
public static class ParameterConverter
{
    /// <summary>
    /// Converts JSON parameters to an args array using pre-collected arguments and options.
    /// </summary>
    /// <param name="arguments">The arguments to process</param>
    /// <param name="options">The options to process</param>
    /// <param name="parameters">JSON parameters from MCP tool call</param>
    /// <returns>Array of command-line arguments</returns>
    public static string[] ConvertToArgs(
        IEnumerable<Argument> arguments,
        IEnumerable<Option> options,
        IDictionary<string, JsonElement> parameters)
    {
        var args = new List<string>();

        // Add arguments (positional)
        foreach (var argument in arguments)
        {
            var argName = ToSnakeCase(argument.Name);
            if (parameters.TryGetValue(argName, out JsonElement value) && value.ValueKind != JsonValueKind.Null)
            {
                AddArgumentValue(args, value, argument);
            }
        }

        // Add options (flags)
        foreach (var option in options)
        {
            var optName = ToSnakeCase(option.Name.TrimStart('-'));
            if (parameters.TryGetValue(optName, out JsonElement value) && value.ValueKind != JsonValueKind.Null)
            {
                AddOptionValue(args, option, value);
            }
        }

        return args.ToArray();
    }

    private static void AddArgumentValue(List<string> args, JsonElement value, Argument argument)
    {
        // Handle array types based on arity
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Null)
                {
                    var stringValue = DeserializeValue(item, argument.ValueType);
                    if (stringValue != null)
                    {
                        args.Add(stringValue);
                    }
                }
            }
        }
        else
        {
            var stringValue = DeserializeValue(value, argument.ValueType);
            if (stringValue != null)
            {
                args.Add(stringValue);
            }
        }
    }

    private static void AddOptionValue(List<string> args, Option option, JsonElement value)
    {
        var optionName = GetPreferredOptionName(option);

        // Handle boolean flags - only add if true
        if (option.ValueType == typeof(bool))
        {
            if (value.ValueKind == JsonValueKind.True)
            {
                args.Add(optionName);
            }
            return;
        }

        // Handle array values based on arity
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Null)
                {
                    var stringValue = DeserializeValue(item, option.ValueType);
                    if (stringValue != null)
                    {
                        args.Add(optionName);
                        args.Add(stringValue);
                    }
                }
            }
            return;
        }

        // Handle single values
        var singleValue = DeserializeValue(value, option.ValueType);
        if (singleValue != null)
        {
            args.Add(optionName);
            args.Add(singleValue);
        }
    }

    private static string GetPreferredOptionName(Option option) => option.Name;

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

    /// <summary>
    /// Deserializes a JSON value to a string representation appropriate for command-line arguments,
    /// handling type conversions based on the target type.
    /// </summary>
    private static string? DeserializeValue(JsonElement value, Type targetType)
    {
        // Unwrap nullable types
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Handle null values
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        // Handle string types - use raw string value
        if (underlyingType == typeof(string))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        // Handle boolean types
        if (underlyingType == typeof(bool))
        {
            if (value.ValueKind == JsonValueKind.True) return "true";
            if (value.ValueKind == JsonValueKind.False) return "false";
            // Try to parse string as boolean
            if (value.ValueKind == JsonValueKind.String)
            {
                var stringValue = value.GetString();
                if (bool.TryParse(stringValue, out var boolValue))
                {
                    return boolValue ? "true" : "false";
                }
            }
            return null;
        }

        // Handle numeric types
        if (underlyingType == typeof(int) || underlyingType == typeof(long) ||
            underlyingType == typeof(short) || underlyingType == typeof(byte) ||
            underlyingType == typeof(uint) || underlyingType == typeof(ulong) ||
            underlyingType == typeof(ushort) || underlyingType == typeof(sbyte))
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetRawText();
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }

        // Handle floating-point types
        if (underlyingType == typeof(float) || underlyingType == typeof(double) || underlyingType == typeof(decimal))
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetRawText();
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }

        // Handle enum types - expect string values matching enum names
        if (underlyingType.IsEnum)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var stringValue = value.GetString();
                // Validate that it's a valid enum value
                if (!string.IsNullOrEmpty(stringValue) && Enum.IsDefined(underlyingType, stringValue))
                {
                    return stringValue;
                }
            }
            return null;
        }

        // Handle arrays and collections - get element type
        if (underlyingType.IsArray)
        {
            var elementType = underlyingType.GetElementType();
            if (elementType != null)
            {
                return DeserializeValue(value, elementType);
            }
        }

        if (underlyingType.IsGenericType)
        {
            var genericTypeDef = underlyingType.GetGenericTypeDefinition();
            if (genericTypeDef == typeof(IEnumerable<>) ||
                genericTypeDef == typeof(List<>) ||
                genericTypeDef == typeof(IList<>))
            {
                var elementType = underlyingType.GetGenericArguments()[0];
                return DeserializeValue(value, elementType);
            }
        }

        // Default: convert to string
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}

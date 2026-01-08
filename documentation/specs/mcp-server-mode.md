# MCP Server Mode for dotnet CLI

**Status**: Proposal
**Author**: N/A
**Date**: 2026-01-08
**Version**: 1.0

## Executive Summary

This specification outlines how to add Model Context Protocol (MCP) server mode to the dotnet CLI, enabling AI assistants to invoke dotnet commands programmatically. The implementation involves adding the ModelContextProtocol NuGet package, creating a new `--mcp` flag for server mode, and mapping ~60+ System.CommandLine commands to MCP tools.

---

## 1. Architecture Overview

### Current State
- **Entry Point**: [Program.cs:34](../src/Cli/dotnet/Program.cs#L34) - Main() processes args via ProcessArgs()
- **Command Structure**: [Parser.cs:59-93](../src/Cli/dotnet/Parser.cs#L59-L93) - 32 top-level subcommands using System.CommandLine
- **Execution Flow**: Parse → CanBeInvoked() → InvokeBuiltInCommand() OR resolve extensible command
- **Total Leaf Commands**: ~63 executable commands

### Proposed Architecture
```
dotnet --mcp
    ↓
MCP Server Mode (stdio transport)
    ↓
MCP Tool Registration (one tool per leaf command)
    ↓
Tool Invocation → System.CommandLine ParseResult → Execute existing command handler
```

---

## 2. Package Dependencies

### Add to [dotnet.csproj](../src/Cli/dotnet/dotnet.csproj)

```xml
<PackageReference Include="ModelContextProtocol" Version="0.5.0-preview.1" />
<PackageReference Include="Microsoft.Extensions.Hosting" />
```

**Package Details**:
- **ModelContextProtocol**: Main package with hosting and DI extensions
- **ModelContextProtocol.Core**: Alternative minimal dependencies option
- **ModelContextProtocol.AspNetCore**: Not needed (we'll use stdio transport)

**Sources**:
- [NuGet: ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)
- [Official C# SDK GitHub](https://github.com/modelcontextprotocol/csharp-sdk)
- [Microsoft Learn: Build MCP Server](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-server)

---

## 3. Entry Point Modification

### Modify [Program.cs:132](../src/Cli/dotnet/Program.cs#L132) ProcessArgs()

**Option A: Early Exit Approach (Recommended)**
```csharp
internal static int ProcessArgs(string[] args, TimeSpan startupTime)
{
    // Add MCP mode detection BEFORE parsing
    if (args.Length > 0 && args[0] == "--mcp")
    {
        // Skip telemetry, first-time setup for MCP mode
        return RunMcpServerMode(args.Skip(1).ToArray());
    }

    // Existing parsing logic continues...
    ParseResult parseResult = Parser.Parse(args);
    // ...
}
```

**Option B: Add MCP Command to Parser**
```csharp
// In Parser.cs Subcommands array
McpCommandParser.GetCommand(),  // Hidden command
```

**Recommendation**: Use Option A for cleaner separation and avoiding first-time-use overhead in server mode.

---

## 4. MCP Server Implementation

### Create New File: `Commands/Mcp/McpServerMode.cs`

```csharp
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;

namespace Microsoft.DotNet.Cli.Commands.Mcp;

public class McpServerMode
{
    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithManualToolRegistration();  // Not using attribute scanning

        // Register all dotnet CLI tools
        builder.Services.AddSingleton<IMcpToolProvider, DotnetCliToolProvider>();

        var host = builder.Build();
        await host.RunAsync();

        return 0;
    }
}
```

### Why Manual Registration?
- System.CommandLine commands already exist with rich metadata
- Avoid attribute-based approach which requires static methods
- Leverage existing CommandDefinition/CommandParser infrastructure
- Full control over parameter mapping and validation

---

## 5. Tool Provider Implementation

### Create `Commands/Mcp/DotnetCliToolProvider.cs`

This class will:
1. **Enumerate all leaf commands** from Parser.Subcommands
2. **Extract metadata** (name, description, arguments, options)
3. **Register MCP tools** with appropriate schemas
4. **Handle invocations** by constructing args and calling Parser.Parse/Invoke

```csharp
public class DotnetCliToolProvider : IMcpToolProvider
{
    public IEnumerable<McpTool> GetTools()
    {
        foreach (var command in Parser.Subcommands)
        {
            foreach (var leafCommand in GetLeafCommands(command))
            {
                yield return CreateMcpTool(leafCommand);
            }
        }
    }

    private McpTool CreateMcpTool(Command command)
    {
        var toolName = GetCommandPath(command).Replace(" ", "_");
        var schema = BuildJsonSchema(command);

        return new McpTool(
            name: toolName,
            description: command.Description,
            inputSchema: schema,
            handler: async (parameters) => await HandleToolInvocation(command, parameters)
        );
    }

    private async Task<CallToolResult> HandleToolInvocation(Command command, JsonNode parameters)
    {
        // 1. Extract parameters from JSON
        // 2. Build args array for System.CommandLine
        // 3. Call Parser.Parse(args)
        // 4. Call Parser.Invoke(parseResult)
        // 5. Capture output and return as TextContentBlock
    }
}
```

---

## 6. Command-to-MCP Tool Mapping Strategy

### Tool Naming Convention
```
dotnet build           → dotnet_build
dotnet tool install    → dotnet_tool_install
dotnet nuget push      → dotnet_nuget_push
```

### Parameter Mapping

#### Arguments
```csharp
// System.CommandLine
public Argument<string[]> SlnOrProjectArgument { get; } = new("PROJECT | SOLUTION")

// MCP Tool Schema
{
  "type": "object",
  "properties": {
    "project_or_solution": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Project or solution file(s)"
    }
  }
}
```

#### Options
```csharp
// System.CommandLine
public Option<string> Configuration { get; } = new("--configuration", "-c")

// MCP Tool Schema
{
  "configuration": {
    "type": "string",
    "description": "Configuration (Debug/Release)",
    "default": null
  }
}
```

#### Boolean Flags
```csharp
// System.CommandLine
public Option<bool> NoRestore { get; } = new("--no-restore")

// MCP Tool Schema
{
  "no_restore": {
    "type": "boolean",
    "description": "Skip package restore",
    "default": false
  }
}
```

---

## 7. Schema Generation

### Create `Commands/Mcp/CommandSchemaBuilder.cs`

```csharp
public static class CommandSchemaBuilder
{
    public static JsonNode BuildSchema(Command command)
    {
        var properties = new JsonObject();

        // Add arguments
        foreach (var arg in command.Arguments)
        {
            properties[ToSnakeCase(arg.Name)] = new JsonObject
            {
                ["type"] = GetJsonType(arg.ValueType),
                ["description"] = arg.Description ?? "",
                ["required"] = arg.Arity.MinimumNumberOfValues > 0
            };
        }

        // Add options
        foreach (var opt in command.Options)
        {
            properties[ToSnakeCase(opt.Name)] = new JsonObject
            {
                ["type"] = GetJsonType(opt.ValueType),
                ["description"] = opt.Description ?? "",
                ["default"] = opt.GetDefaultValue()
            };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
    }
}
```

---

## 8. Output Capture

### Challenge
MCP tools need to return results as strings, but dotnet commands write to Console.Out/Error.

### Solution: Stream Redirection

```csharp
private async Task<CallToolResult> HandleToolInvocation(Command command, JsonNode parameters)
{
    using var outputWriter = new StringWriter();
    using var errorWriter = new StringWriter();

    // Redirect Console output
    var originalOut = Console.Out;
    var originalError = Console.Error;
    Console.SetOut(outputWriter);
    Console.SetError(errorWriter);

    try
    {
        var args = BuildArgsFromParameters(command, parameters);
        var parseResult = Parser.Parse(args);
        var exitCode = Parser.Invoke(parseResult);

        var output = outputWriter.ToString();
        var error = errorWriter.ToString();

        return new CallToolResult
        {
            Content = new[]
            {
                new TextContentBlock
                {
                    Text = $"Exit Code: {exitCode}\n\nOutput:\n{output}\n\nErrors:\n{error}"
                }
            }
        };
    }
    finally
    {
        Console.SetOut(originalOut);
        Console.SetError(originalError);
    }
}
```

### Alternative: Reporter Pattern
The CLI uses `Reporter.Output` and `Reporter.Error`. Consider hooking into this system instead of Console redirection.

---

## 9. Command Mapping - Detailed Breakdown

### High Priority Commands (15 commands)
Essential for AI development workflows:

1. **dotnet_build** - Build projects
2. **dotnet_run** - Run applications
3. **dotnet_test** - Run tests
4. **dotnet_restore** - Restore dependencies
5. **dotnet_clean** - Clean build outputs
6. **dotnet_publish** - Publish applications
7. **dotnet_new** - Create from templates
8. **dotnet_add_package** - Add NuGet packages
9. **dotnet_remove_package** - Remove packages
10. **dotnet_list_package** - List packages
11. **dotnet_tool_install** - Install tools
12. **dotnet_tool_list** - List tools
13. **dotnet_format** - Format code
14. **dotnet_nuget_push** - Push packages
15. **dotnet_workload_list** - List workloads

### Medium Priority (20 commands)
Reference management, advanced build operations:

16-35. tool update/uninstall, reference add/remove/list, package search/list, project convert, workload install/update/search, nuget commands, etc.

### Low Priority (28 commands)
Hidden, diagnostic, or specialized commands:

36-63. build-server shutdown, vstest, store, parse, complete, fsi, msbuild direct, etc.

---

## 10. Implementation Phases

### Phase 1: Foundation (Week 1-2)
- [ ] Add ModelContextProtocol package to dotnet.csproj
- [ ] Create `Commands/Mcp/` directory structure
- [ ] Implement basic MCP server mode detection in Program.cs
- [ ] Create McpServerMode.cs with stdio transport
- [ ] Test basic server startup/shutdown

### Phase 2: Core Tool Provider (Week 2-3)
- [ ] Implement DotnetCliToolProvider.cs
- [ ] Create CommandSchemaBuilder.cs for JSON schema generation
- [ ] Implement command tree traversal to find leaf commands
- [ ] Create tool naming and registration logic
- [ ] Test tool enumeration (verify all 60+ tools appear)

### Phase 3: Tool Invocation (Week 3-4)
- [ ] Implement parameter extraction from JSON
- [ ] Build args array reconstruction
- [ ] Implement output/error capture mechanism
- [ ] Create CallToolResult formatting
- [ ] Test with 5 high-priority commands (build, run, test, restore, clean)

### Phase 4: High Priority Commands (Week 4-5)
- [ ] Validate all 15 high-priority command mappings
- [ ] Test each command with various parameter combinations
- [ ] Handle edge cases (missing required params, invalid values)
- [ ] Implement proper error messages in MCP format

### Phase 5: Remaining Commands (Week 5-6)
- [ ] Map medium and low priority commands
- [ ] Test subcommand hierarchies (tool, workload, nuget, package)
- [ ] Validate complex commands (new with templates, format)
- [ ] Test forwarding commands (msbuild, fsi, vstest)

### Phase 6: Polish & Documentation (Week 6-7)
- [ ] Add comprehensive tool descriptions
- [ ] Document all parameter schemas
- [ ] Create example `.vscode/mcp.json` configuration
- [ ] Write integration tests
- [ ] Performance testing and optimization
- [ ] Update CLI help text to mention MCP mode

---

## 11. Configuration Example

### `.vscode/mcp.json`
```json
{
  "servers": {
    "dotnet-cli": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["--mcp"],
      "env": {
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1"
      }
    }
  }
}
```

---

## 12. Technical Challenges & Solutions

### Challenge 1: Complex Option Types
**Problem**: System.CommandLine supports complex types (enums, collections, custom parsers)
**Solution**: Convert to JSON primitive types in schema; use ToString()/Parse in conversion layer

### Challenge 2: Interactive Commands
**Problem**: Some commands may prompt for input (e.g., certificate generation)
**Solution**: Disable interactive features in MCP mode via environment variable

### Challenge 3: MSBuild Forwarding
**Problem**: Many commands forward to MSBuild with custom property formatting
**Solution**: Preserve existing forwarding logic; invoke through normal command path

### Challenge 4: Telemetry in Server Mode
**Problem**: Telemetry initialization adds overhead
**Solution**: Skip telemetry setup when `--mcp` flag detected (see Program.cs modification)

### Challenge 5: First-Time Configuration
**Problem**: First-run experience (certificate generation, PATH setup) inappropriate for server
**Solution**: Skip ConfigureDotNetForFirstTimeUse() in MCP mode

---

## 13. Alternative Approaches Considered

### ❌ Separate Executable (`dotnet-mcp`)
**Pros**: Clean separation, extensible command pattern
**Cons**: Duplicate command definitions, maintenance burden, larger installation

### ❌ Attribute-Based Tool Registration
**Pros**: Idiomatic MCP pattern, automatic discovery
**Cons**: Requires rewriting all 60+ commands, loses System.CommandLine benefits

### ✅ Hybrid Approach (RECOMMENDED)
**Pros**: Reuses existing infrastructure, minimal code duplication, maintains single source of truth
**Cons**: Requires bridging layer between System.CommandLine and MCP

---

## 14. Testing Strategy

### Unit Tests
```csharp
[Fact]
public void CommandSchemaBuilder_GeneratesCorrectSchema_ForBuildCommand()
{
    var command = BuildCommandParser.GetCommand();
    var schema = CommandSchemaBuilder.BuildSchema(command);

    Assert.Equal("object", schema["type"]);
    Assert.Contains("configuration", schema["properties"]);
    Assert.Contains("framework", schema["properties"]);
}
```

### Integration Tests
```csharp
[Fact]
public async Task McpServer_ExecutesBuildCommand_Successfully()
{
    var parameters = new JsonObject
    {
        ["configuration"] = "Debug",
        ["no_restore"] = true
    };

    var result = await provider.InvokeTool("dotnet_build", parameters);

    Assert.Contains("Build succeeded", result.Content[0].Text);
}
```

### E2E Tests
- Start MCP server via stdio
- Send JSON-RPC tool invocation requests
- Verify responses match expected format
- Test with real AI client (Claude Desktop, VS Code)

---

## 15. File Structure

```
src/Cli/dotnet/
├── Commands/
│   └── Mcp/
│       ├── McpServerMode.cs              # Server entry point
│       ├── DotnetCliToolProvider.cs      # Tool registration
│       ├── CommandSchemaBuilder.cs       # JSON schema generation
│       ├── ParameterConverter.cs         # JSON → args conversion
│       ├── OutputCapture.cs              # Console redirection
│       └── McpCommandParser.cs           # Optional: if using Option B
├── Program.cs                            # Modified: Add --mcp detection
├── Parser.cs                             # Unchanged (or add McpCommand)
└── dotnet.csproj                         # Modified: Add packages
```

---

## 16. Command Mapping Reference

### Complete List of Leaf Commands (63 total)

#### Build & Compilation (7)
- dotnet_build
- dotnet_clean
- dotnet_publish
- dotnet_pack
- dotnet_restore
- dotnet_msbuild
- dotnet_build_server_shutdown

#### Execution (4)
- dotnet_run
- dotnet_run_api
- dotnet_test
- dotnet_vstest

#### Package Management (13)
- dotnet_package_add
- dotnet_package_remove
- dotnet_package_list
- dotnet_package_search
- dotnet_add_package (hidden)
- dotnet_remove_package (hidden)
- dotnet_list_package (hidden)
- dotnet_nuget_push
- dotnet_nuget_delete
- dotnet_nuget_verify
- dotnet_nuget_sign
- dotnet_nuget_locals
- dotnet_nuget_why

#### Tool Management (8)
- dotnet_tool_install
- dotnet_tool_uninstall
- dotnet_tool_update
- dotnet_tool_list
- dotnet_tool_run
- dotnet_tool_search
- dotnet_tool_restore
- dotnet_tool_execute

#### Workload Management (11)
- dotnet_workload_install
- dotnet_workload_update
- dotnet_workload_list
- dotnet_workload_search
- dotnet_workload_search_versions
- dotnet_workload_uninstall
- dotnet_workload_repair
- dotnet_workload_restore
- dotnet_workload_clean
- dotnet_workload_elevate
- dotnet_workload_config
- dotnet_workload_history

#### Project & Solution (7)
- dotnet_new
- dotnet_sln_add
- dotnet_sln_remove
- dotnet_sln_list
- dotnet_reference_add
- dotnet_reference_remove
- dotnet_reference_list
- dotnet_project_convert

#### Specialized (13)
- dotnet_format
- dotnet_fsi
- dotnet_sdk_check
- dotnet_store
- dotnet_help
- dotnet_complete (hidden)
- dotnet_parse (hidden)
- dotnet_dnx (hidden)
- dotnet_nuget_trust_*... (6 subcommands)

---

## 17. Risk Assessment

### High Risk
- **Output capture reliability**: Console redirection may not catch all output sources
- **State management**: Some commands modify global state (workloads, tools)
- **Concurrency**: Multiple MCP clients invoking commands simultaneously

### Medium Risk
- **Performance**: Repeated parsing overhead vs. caching parsed commands
- **Breaking changes**: MCP SDK is in preview (0.5.0-preview.1)
- **Compatibility**: Different .NET SDK versions may have different commands

### Low Risk
- **Schema accuracy**: Automated generation from System.CommandLine metadata
- **Testing coverage**: Existing CLI tests validate command behavior

---

## 18. Success Criteria

1. ✅ All 60+ leaf commands exposed as MCP tools
2. ✅ AI assistants can successfully invoke high-priority commands (build, run, test, etc.)
3. ✅ Parameter validation matches existing CLI behavior
4. ✅ Output format suitable for LLM consumption
5. ✅ Performance: Tool invocation adds <100ms overhead vs. direct CLI
6. ✅ No breaking changes to existing CLI functionality
7. ✅ Comprehensive test coverage (unit + integration + E2E)

---

## 19. Open Questions

1. **Should hidden commands be exposed as MCP tools?**
   - Recommendation: Yes, but mark as experimental/internal in description

2. **How to handle commands that require user interaction?**
   - Recommendation: Return error indicating command not supported in MCP mode

3. **Should we version MCP tools separately from CLI version?**
   - Recommendation: Use CLI version, document in tool metadata

4. **How to handle breaking changes in command structure?**
   - Recommendation: MCP tool version in name (dotnet_build_v1, dotnet_build_v2)

5. **Should MCP mode support HTTP/SSE transport?**
   - Recommendation: Start with stdio only; add HTTP in future iteration

---

## 20. Next Steps

After approval of this plan:

1. **Create feature branch**: `feature/mcp-server-mode`
2. **Set up project structure**: Create Commands/Mcp directory
3. **Add dependencies**: Update dotnet.csproj
4. **Prototype Phase 1**: Basic server with 1-2 test commands
5. **Validate approach**: Test with Claude Desktop or VS Code
6. **Iterate through phases**: Following timeline above

---

## References

### Documentation
- [Official C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [ModelContextProtocol NuGet Package](https://www.nuget.org/packages/ModelContextProtocol)
- [Build MCP Server - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-server)
- [MCP Server in .NET Blog Post](https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/)
- [NuGet MCP Server Preview](https://devblogs.microsoft.com/dotnet/nuget-mcp-server-preview/)

### Codebase Files
- [Program.cs](../src/Cli/dotnet/Program.cs) - Entry point and ProcessArgs
- [Parser.cs](../src/Cli/dotnet/Parser.cs) - Command registration
- [dotnet.csproj](../src/Cli/dotnet/dotnet.csproj) - Project dependencies

---

**End of Specification**

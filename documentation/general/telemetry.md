# Telemetry in the .NET SDK

> All of this is publicly documented [here](https://docs.microsoft.com/dotnet/core/tools/telemetry#data-points), this is mostly a design/process document.

## Sources of Telemetry

* Dotnet CLI commands directly calling `TelemetryEventEntry.TrackEvent`
* The default MSBuild logger forwards along specific telemetry events
* The (currently external to this repo) `dotnet new` command produces telemetry specific to template instanciation that is not covered here

## A note on privacy

String values are hashed before being sent off of the device. The text is uppercased and then the UTF8 bytes are hashed using SHA256.

## Data Collected

### Common Properties

For each event, a set of machine-common properties are collected. These can all be seen at [TelemetryCommonProperties](..\..\src\Cli\dotnet\Telemetry\TelemetryCommonProperties.cs).

One note here is that there are two classes of property:

* SDK-variant
* OS-variant

### SDK Telemetry

#### Unhandled exceptions

Unhandled exceptions are tracked by the `mainCatchException/exception` event with the following properties:

* `exceptionType` (GetType().ToString())
* `detail` - custom ToString that drops the message and unwraps any inner exceptions

#### Installations

New installations of the SDK are reported via the `install/reportSuccess` event.

#### SDK Commands

SDK top-level commands are tracked via the `toplevelparser/command` event with the following properties:

* `verb` - the name of the command to be executed
* there are a subset of properties that are tracked for each verb:
  * new - sends the first argument (aka the template name)
  * help - sends the first argument
  * add/remove/list/sln/nuget - send the first subcommand (aka `package` in `dotnet add package`)
  * build/publish/run/clean/test - log the following option values if present
    * Framework, Runtime, Configuration
  * pack - log the following option values if present
    * Configuration
  * vstest - log the following option values if present
    * TestPlatform, TestFramework, TestLogger
  * workload/tool - logs the workload subcommand and subcommand argument, if present (ie `dotnet workload install <workload>` would log `install` and `workload` )

In addition, SDK top level commands track their verbosities if one is specified via the `sublevelparser/command` event, which sends:

* verb
* verbosity

### MSBuild Telemetry

* the `msbuild/targetframeworkeval` event logs the following properties
  * TargetFrameworkVersion
  * RuntimeIdentifier
  * SelfContained
  * UseApphost
  * OutputType

> Note: the following events are in the code as logged, but I have yet to find any of them in the telemetry backend.

* the following events are passed through entirely:
  * `taskBaseCatchException`
  * `PublishProperties`
  * `ReadyToRun`

### Template Engine Telemetry

#### Instanciation

The following properties are logged on template instanciation as part of the `new-create-template` event

* Language (the chosen template language, MS-authored templates only)
* ArgError (if there was a parse error)
* Framework (the chosen TFM)
* TemplateName
* IsTemplateThirdParty (true if template author isn't Microsoft)
* Success (boolean)
* Auth (the value of the --auth flag, MS-authored templates only)

#### Installation

The following properties are logged on template install as part of the `new-install` event:

* CountOfThingsToInstall (number of template packages in the installable package)

## Controlling data collection

If the `DOTNET_CLI_TELEMETRY_OPTOUT` environment variable is set to `true`, data collection will be disabled.  What this means is that the events will still be sent to the `Microsoft.DotNet.Cli.Telemetry.Telemetry` class, but all write operations will be no ops.

## Submitting Telemetry Data

When these events are sent, they are buffered to disk (by default inside the dotnet home path), and a separate set of Senders reads these events from storage on a periodic basis, sending them to the backend.

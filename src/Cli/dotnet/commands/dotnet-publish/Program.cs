﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Build.Execution;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Cli.Sln.Internal;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;
using Microsoft.VisualBasic.CompilerServices;
using Parser = Microsoft.DotNet.Cli.Parser;


namespace Microsoft.DotNet.Tools.Publish
{
    public class PublishCommand : RestoringCommand
    {

        /// <summary>
        /// The list of properties that should be forwarded from the publish profile to the publish invocation.
        /// </summary>
        /// <remarks>
        /// While we could forward along every property, the intent of this array is to mimic the behavior of VS,
        /// whose Publish operation only forwards along a few properties. Of particular interest are properties that
        /// are set very early on in the build (RID, Configuration, etc).  The remainder will be imported during the
        /// build via the Microsoft.Net.Sdk.Publish props and targets.
        /// </remarks>
        private static string[] PropertiesToForwardFromProfile = new [] {
            "Configuration",
            "LastUsedBuildConfiguration",
            "LastUsedPlatform",
            "Platform",
            "RuntimeIdentifier",
            "RuntimeIdentifiers",
            "TargetFramework",
            "TargetFrameworks",
        };

        private PublishCommand(
            IEnumerable<string> msbuildArgs,
            bool noRestore,
            string msbuildPath = null)
            : base(msbuildArgs, noRestore, msbuildPath)
        {
        }

        public static PublishCommand FromArgs(string[] args, string msbuildPath = null)
        {
            var parser = Parser.Instance;
            var parseResult = parser.ParseFrom("dotnet publish", args);
            return FromParseResult(parseResult);
        }

        public static PublishCommand FromParseResult(ParseResult parseResult, string msbuildPath = null)
        {
            parseResult.HandleDebugSwitch();
            parseResult.ShowHelpOrErrorIfAppropriate();

            bool noRestore = parseResult.HasOption(PublishCommandParser.NoRestoreOption)
                          || parseResult.HasOption(PublishCommandParser.NoBuildOption);


            var publishProfileProperties = DiscoverPropertiesFromPublishProfile(parseResult);
            Console.WriteLine($"Read publish properties: {string.Join(",", publishProfileProperties)}");
            var standardMSbuildProperties = CreatePropertyListForPublishInvocation(parseResult);
            return new PublishCommand(
                // properties defined by the selected publish profile should override any other properties,
                // so they should be added after the other properties.
                standardMSbuildProperties.Concat(publishProfileProperties),
                noRestore,
                msbuildPath
            );

            List<string> CreatePropertyListForPublishInvocation(ParseResult parseResult) {
                var msbuildArgs = new List<string>()
                    {
                        "-target:Publish",
                        "--property:_IsPublishing=true" // This property will not hold true for MSBuild /t:Publish. VS should also inject this property when publishing in the future.
                    };

                IEnumerable<string> slnOrProjectArgs = parseResult.GetValueForArgument(PublishCommandParser.SlnOrProjectArgument);

                CommonOptions.ValidateSelfContainedOptions(parseResult.HasOption(PublishCommandParser.SelfContainedOption),
                    parseResult.HasOption(PublishCommandParser.NoSelfContainedOption));

                msbuildArgs.AddRange(parseResult.OptionValuesToBeForwarded(PublishCommandParser.GetCommand()));
                ReleasePropertyProjectLocator projectLocator = new ReleasePropertyProjectLocator(Environment.GetEnvironmentVariable(EnvironmentVariableNames.ENABLE_PUBLISH_RELEASE_FOR_SOLUTIONS) != null);
                msbuildArgs.AddRange(projectLocator.GetCustomDefaultConfigurationValueIfSpecified(parseResult, MSBuildPropertyNames.PUBLISH_RELEASE, slnOrProjectArgs, PublishCommandParser.ConfigurationOption) ?? Array.Empty<string>());
                msbuildArgs.AddRange(slnOrProjectArgs ?? Array.Empty<string>());

                return msbuildArgs;
            }

            List<string> DiscoverPropertiesFromPublishProfile(ParseResult parseResult)
            {
                ReleasePropertyProjectLocator projectLocator = new ReleasePropertyProjectLocator(Environment.GetEnvironmentVariable(EnvironmentVariableNames.ENABLE_PUBLISH_RELEASE_FOR_SOLUTIONS) != null);
                var cliProps = projectLocator.GetGlobalPropertiesFromUserArgs(parseResult);
                var projectInstance = projectLocator.GetTargetedProject(parseResult.GetValueForArgument(PublishCommandParser.SlnOrProjectArgument), cliProps, MSBuildPropertyNames.PUBLISH_RELEASE);
                // this can happen if the project wasn't loadable, or if we're at a solution context
                if (projectInstance == null)
                {
                    return new List<string>();
                }
                var importedPropValue = projectInstance.GetPropertyValue(MSBuildPropertyNames.PUBLISH_PROFILE_IMPORTED);
                if (!String.IsNullOrEmpty(importedPropValue) && bool.TryParse(importedPropValue, out var wasImported) && wasImported) {
                    try {
                        if (projectInstance.GetPropertyValue(MSBuildPropertyNames.PUBLISH_PROFILE_FULL_PATH) is {} fullPathPropValue) {
                            var properties = new ProjectInstance(fullPathPropValue).ToProjectRootElement().PropertyGroups.First().Properties;
                            var propertiesToForward = properties.Where(p => PropertiesToForwardFromProfile.Contains(p.Name));
                            return propertiesToForward.Select(p => $"--property:{p.Name}=\"{p.Value}\"").ToList();
                        }
                    } catch (IOException) {
                        return new List<string>();
                    }
                }
                return new List<string>();
            };
        }

        public static int Run(ParseResult parseResult)
        {
            parseResult.HandleDebugSwitch();

            return FromParseResult(parseResult).Execute();
        }
    }
}

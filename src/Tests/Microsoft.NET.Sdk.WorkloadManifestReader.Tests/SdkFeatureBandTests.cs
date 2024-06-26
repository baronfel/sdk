// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.DotNet.Cli;
using Microsoft.NET.Sdk.WorkloadManifestReader;
using Microsoft.NET.TestFramework;

using Xunit;
using Xunit.Abstractions;

namespace ManifestReaderTests
{

    public class SdkFeatureBandTests : SdkTest
    {
        public SdkFeatureBandTests(ITestOutputHelper logger) : base(logger)
        {
        }

        [Theory]
        [InlineData("6.0.100", "6.0.100")]
        [InlineData("10.0.512", "10.0.500")]
        [InlineData("7.0.100-preview.1.12345", "7.0.100-preview.1")]
        [InlineData("7.0.100-dev", "7.0.100")]
        [InlineData("7.0.100-ci", "7.0.100")]
        [InlineData("6.0.100-rc.2.21505.57", "6.0.100-rc.2")]
        [InlineData("7.0.100-alpha.1.21558.2", "7.0.100-alpha.1")]
        public void ItParsesVersionsCorrectly(string version, string expectedParsedVersion)
        {
            var parsedVersion = new SdkFeatureBand(version).ToString();
            parsedVersion.Should().Be(expectedParsedVersion);
        }
    }
}

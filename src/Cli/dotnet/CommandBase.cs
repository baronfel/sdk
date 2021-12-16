﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine.Parsing;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Cli
{
    public abstract class CommandBase
    {
        public abstract Task<int> Execute();
    }
}

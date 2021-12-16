// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Evaluation;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;
using NuGet.Frameworks;

namespace Microsoft.DotNet.Tools.Add.ProjectToProjectReference
{
    record class AddProjectToProjectReferenceOptions(string fileOrDirectory, bool interactive, string framework, string[] projectsToAdd);
    
    internal class AddProjectToProjectReferenceCommand : CommandBase
    {
        private readonly AddProjectToProjectReferenceOptions _options;

        public AddProjectToProjectReferenceCommand(AddProjectToProjectReferenceOptions options)
        {
            _options = options;
        }

        public override Task<int> Execute()
        {
            var projects = new ProjectCollection();
            MsbuildProject msbuildProj = MsbuildProject.FromFileOrDirectory(
                projects,
                _options.fileOrDirectory,
                _options.interactive);

            PathUtility.EnsureAllPathsExist(_options.projectsToAdd, CommonLocalizableStrings.CouldNotFindProjectOrDirectory, true);
            
            List<MsbuildProject> refs =
                _options.projectsToAdd
                    .Select((r) => MsbuildProject.FromFileOrDirectory(projects, r, _options.interactive))
                    .ToList();

            if (string.IsNullOrEmpty(_options.framework))
            {
                foreach (var tfm in msbuildProj.GetTargetFrameworks())
                {
                    foreach (var @ref in refs)
                    {
                        if (!@ref.CanWorkOnFramework(tfm))
                        {
                            Reporter.Error.Write(GetProjectNotCompatibleWithFrameworksDisplayString(
                                                     @ref,
                                                     msbuildProj.GetTargetFrameworks().Select((fx) => fx.GetShortFolderName())));
                            return Task.FromResult(1);
                        }
                    }
                }
            }
            else
            {
                var framework = NuGetFramework.Parse(_options.framework);
                if (!msbuildProj.IsTargetingFramework(framework))
                {
                    Reporter.Error.WriteLine(string.Format(
                                                 CommonLocalizableStrings.ProjectDoesNotTargetFramework,
                                                 msbuildProj.ProjectRootElement.FullPath,
                                                 _options.framework));
                    return Task.FromResult(1);
                }

                foreach (var @ref in refs)
                {
                    if (!@ref.CanWorkOnFramework(framework))
                    {
                        Reporter.Error.Write(GetProjectNotCompatibleWithFrameworksDisplayString(
                                                 @ref,
                                                 new string[] { _options.framework }));
                        return Task.FromResult(1);
                    }
                }
            }

            var relativePathReferences = refs.Select((r) =>
                                                        Path.GetRelativePath(
                                                            msbuildProj.ProjectDirectory,
                                                            r.ProjectRootElement.FullPath)).ToList();

            int numberOfAddedReferences = msbuildProj.AddProjectToProjectReferences(
                _options.framework,
                relativePathReferences);

            if (numberOfAddedReferences != 0)
            {
                msbuildProj.ProjectRootElement.Save();
            }

            return Task.FromResult(0);
        }

        private static string GetProjectNotCompatibleWithFrameworksDisplayString(MsbuildProject project, IEnumerable<string> frameworksDisplayStrings)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(CommonLocalizableStrings.ProjectNotCompatibleWithFrameworks, project.ProjectRootElement.FullPath));
            foreach (var tfm in frameworksDisplayStrings)
            {
                sb.AppendLine($"    - {tfm}");
            }

            return sb.ToString();
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Text.Json;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Cli.NuGetPackageDownloader;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Configurer;
using Microsoft.DotNet.Workloads.Workload.Install;
using Microsoft.DotNet.Workloads.Workload.Install.InstallRecord;
using Microsoft.NET.Sdk.WorkloadManifestReader;
using Microsoft.TemplateEngine.Cli.Commands;
using InformationStrings = Microsoft.DotNet.Workloads.Workload.LocalizableStrings;

namespace Microsoft.DotNet.Workloads.Workload.List
{
    internal class WorkloadListCommand : WorkloadCommandBase
    {
        private readonly bool _includePreviews;
        private readonly IWorkloadManifestUpdater _workloadManifestUpdater;
        private readonly IWorkloadInfoHelper _workloadListHelper;
        private readonly ListFormat _format;

        public WorkloadListCommand(
            ParseResult parseResult,
            IReporter reporter = null,
            IWorkloadInstallationRecordRepository workloadRecordRepo = null,
            string currentSdkVersion = null,
            string dotnetDir = null,
            string userProfileDir = null,
            string tempDirPath = null,
            INuGetPackageDownloader nugetPackageDownloader = null,
            IWorkloadManifestUpdater workloadManifestUpdater = null,
            IWorkloadResolver workloadResolver = null
        ) : base(parseResult, CommonOptions.HiddenVerbosityOption, reporter, tempDirPath, nugetPackageDownloader)
        {
            _workloadListHelper = new WorkloadInfoHelper(
                parseResult.HasOption(SharedOptions.InteractiveOption),
                Verbosity,
                parseResult?.GetValue(WorkloadListCommandParser.VersionOption) ?? null,
                VerifySignatures,
                Reporter,
                workloadRecordRepo,
                currentSdkVersion,
                dotnetDir,
                userProfileDir,
                workloadResolver
            );

            _format = parseResult.GetValue(WorkloadListCommandParser.MachineReadableOption) ? ListFormat.Json : parseResult.GetValue(WorkloadListCommandParser.FormatOption);

            _includePreviews = parseResult.GetValue(WorkloadListCommandParser.IncludePreviewsOption);
            string userProfileDir1 = userProfileDir ?? CliFolderPathCalculator.DotnetUserProfileFolderPath;

            _workloadManifestUpdater = workloadManifestUpdater ?? new WorkloadManifestUpdater(Reporter,
                _workloadListHelper.WorkloadResolver, PackageDownloader, userProfileDir1, _workloadListHelper.WorkloadRecordRepo, _workloadListHelper.Installer);
        }

        public override int Execute()
        {
            IEnumerable<WorkloadId> installedList = _workloadListHelper.InstalledSdkWorkloadIds;

            var result = _format switch
            {
                ListFormat.Json => PrintMachineReadable(),
                ListFormat.Table => PrintTable(),
                ListFormat.Mermaid => GenerateMermaidGraph(),
                _ => 1 // this won't be hit - System.CommandLine validation will throw here
            };

            return result;

            int PrintMachineReadable()
            {
                _workloadListHelper.CheckTargetSdkVersionIsValid();

                var updateAvailable = GetUpdateAvailable(installedList);
                var installed = installedList.Select(id => id.ToString()).ToArray();
                ListOutput listOutput = new(installed, updateAvailable.ToArray());

                Reporter.WriteLine("==workloadListJsonOutputStart==");
                Reporter.WriteLine(
                    JsonSerializer.Serialize(listOutput,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                Reporter.WriteLine("==workloadListJsonOutputEnd==");
                return 0;
            }

            int PrintTable()
            {
                var manifestInfoDict = _workloadListHelper.WorkloadResolver.GetInstalledManifests().ToDictionary(info => info.Id, StringComparer.OrdinalIgnoreCase);

                InstalledWorkloadsCollection installedWorkloads = _workloadListHelper.AddInstalledVsWorkloads(installedList);
                Reporter.WriteLine();
                PrintableTable<KeyValuePair<string, string>> table = new();
                table.AddColumn(InformationStrings.WorkloadIdColumn, workload => workload.Key);
                table.AddColumn(InformationStrings.WorkloadManfiestVersionColumn, workload =>
                {
                    var m = _workloadListHelper.WorkloadResolver.GetManifestFromWorkload(new WorkloadId(workload.Key));
                    var manifestInfo = manifestInfoDict[m.Id];
                    return m.Version + "/" + manifestInfo.ManifestFeatureBand;
                });
                table.AddColumn(InformationStrings.WorkloadSourceColumn, workload => workload.Value);

                table.PrintRows(installedWorkloads.AsEnumerable(), l => Reporter.WriteLine(l));

                Reporter.WriteLine();
                Reporter.WriteLine(LocalizableStrings.WorkloadListFooter);
                Reporter.WriteLine();

                var updatableWorkloads = _workloadManifestUpdater.GetUpdatableWorkloadsToAdvertise(installedList).Select(workloadId => workloadId.ToString());
                if (updatableWorkloads.Any())
                {
                    Reporter.WriteLine(string.Format(LocalizableStrings.WorkloadUpdatesAvailable, string.Join(" ", updatableWorkloads)));
                    Reporter.WriteLine();
                }
                return 0;
            }

            int GenerateMermaidGraph()
            {
                var manifestInfoDict = _workloadListHelper.WorkloadResolver.GetInstalledManifests().ToDictionary(info => info.Id, StringComparer.OrdinalIgnoreCase);
                HashSet<WorkloadId> workloadIds = new();
                HashSet<WorkloadPackId> packIds = new();
                HashSet<(WorkloadId left, WorkloadPackId right)> edges = new();

                workloadIds.AddRange(installedList);
                var packsInWorkloads = installedList.ToDictionary(m => m, m => _workloadListHelper.WorkloadResolver.GetPacksInWorkload(m));
                var packsDict = packsInWorkloads.Values.SelectMany(p => p).ToDictionary(p => p, p => _workloadListHelper.WorkloadResolver.TryGetPackInfo(p));
                foreach (var (workloadId, packIdsInWorkload) in packsInWorkloads)
                {
                    packIds.AddRange(packIdsInWorkload);
                    foreach (var packId in packIdsInWorkload)
                    {
                        edges.Add((workloadId, packId));
                    }
                }
                Reporter.WriteLine("```mermaid");
                Reporter.WriteLine("---");
                Reporter.WriteLine("title: workloads");
                Reporter.WriteLine("---");
                Reporter.WriteLine("graph TD");

                foreach (var workloadId in workloadIds)
                {
                    var manifest = _workloadListHelper.WorkloadResolver.GetManifestFromWorkload(workloadId);
                    Reporter.WriteLine($"{workloadId}[{workloadId}/{manifest.Version}]");
                }

                foreach (var packId in packIds)
                {
                    var packInfo = packsDict[packId];
                    if (packInfo == null)
                    {
                        continue;
                    }
                    (string prefix, string suffix) = packInfo.Kind switch
                    {
                        WorkloadPackKind.Sdk => ("([", "])"),
                        WorkloadPackKind.Library => ("[/", "\\]"),
                        WorkloadPackKind.Framework => ("{", "}"),
                        WorkloadPackKind.Template => ("[[", "]]"),
                        WorkloadPackKind.Tool => ("[/", "/]"),
                        _ => ("([", "])")
                    };
                    Reporter.WriteLine($"{packId}{prefix}{packId}/{packInfo.Version}{suffix}");
                }

                foreach (var edge in edges)
                {
                    Reporter.WriteLine($"{edge.left} --> {edge.right}");
                }

                Reporter.WriteLine("```");
                return 0;
            }
        }

        internal class StringTupleEqualityComparer : IEqualityComparer<(string, string)>
        {
            public bool Equals((string, string) x, (string, string) y) => StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);
            public int GetHashCode([DisallowNull] (string, string) obj) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
        }

        internal IEnumerable<UpdateAvailableEntry> GetUpdateAvailable(IEnumerable<WorkloadId> installedList)
        {
            _workloadManifestUpdater.UpdateAdvertisingManifestsAsync(_includePreviews).Wait();
            var manifestsToUpdate = _workloadManifestUpdater.CalculateManifestUpdates();

            foreach ((ManifestVersionUpdate manifestUpdate, WorkloadCollection workloads) in manifestsToUpdate)
            {
                foreach ((WorkloadId workloadId, WorkloadDefinition workloadDefinition) in workloads)
                {
                    if (installedList.Contains(workloadId))
                    {
                        yield return new UpdateAvailableEntry(manifestUpdate.ExistingVersion.ToString(),
                            manifestUpdate.NewVersion.ToString(),
                            workloadDefinition.Description, workloadId.ToString());
                    }
                }
            }
        }

        internal record ListOutput(string[] Installed, UpdateAvailableEntry[] UpdateAvailable);

        internal record UpdateAvailableEntry(string ExistingManifestVersion, string AvailableUpdateManifestVersion,
            string Description, string WorkloadId);
    }
}

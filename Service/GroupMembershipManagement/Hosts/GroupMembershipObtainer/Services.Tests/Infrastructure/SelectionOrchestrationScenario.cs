// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Moq;
using Repositories.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Tests.Services.Infrastructure
{
    /// <summary>
    /// A one-type part read by the real <see cref="SubOrchestratorFunction"/>: the read-path activities are
    /// simple storage-backed fakes (page readers, merge, saved-delta processing, upload deletion), while the whole
    /// selection pipeline runs the real activities through <see cref="SelectionPipelineHarness"/>.
    /// The fallback full read writes the members Graph's selected query would return (by true type).
    /// </summary>
    internal sealed class SelectionOrchestrationScenario
    {
        public SelectionOrchestrationScenario(
            IReadOnlyCollection<string> identityTypes,
            bool savedDeltaLink,
            InMemoryBlobStorageRepository storage = null,
            SelectionPipelineHarness pipeline = null,
            Guid? sourceGroupId = null,
            Guid? targetGroupId = null,
            Guid? runId = null,
            int currentPart = 1)
        {
            Storage = storage ?? new InMemoryBlobStorageRepository();
            Pipeline = pipeline ?? new SelectionPipelineHarness(Storage);
            SavedDeltaLink = savedDeltaLink;
            Request = new GroupMembershipRequest
            {
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = runId ?? Guid.NewGuid(),
                    Query = "[{\"type\":\"GroupMembership\"}]"
                },
                SourceGroup = new AzureADGroup { ObjectId = sourceGroupId ?? Guid.NewGuid() },
                GroupId = targetGroupId ?? Guid.NewGuid(),
                CurrentPart = currentPart,
                TotalParts = Math.Max(currentPart, 2),
                IdentityTypes = identityTypes?.ToList()
            };
            Context = new Mock<TaskOrchestrationContext>();
            Function = new SubOrchestratorFunction(
                new MockDeltaCachingConfig { DeltaCacheEnabled = true },
                new TelemetryClient(new TelemetryConfiguration()));
            Configure();
        }

        public InMemoryBlobStorageRepository Storage { get; }
        public SelectionPipelineHarness Pipeline { get; }
        public bool SavedDeltaLink { get; }
        public GroupMembershipRequest Request { get; }
        public Mock<TaskOrchestrationContext> Context { get; }
        public SubOrchestratorFunction Function { get; }
        public RecordingLogger<SubOrchestratorFunction> Logger { get; } = new();
        public List<string> Sequence { get; } = new();

        /// <summary>The source group's members (people and agent users) as the incremental read stages them.</summary>
        public List<AzureADUser> Members { get; } = new();

        /// <summary>Upload pages present under the merge prefix each time the merge ran.</summary>
        public List<int> MergedPageCounts { get; } = new();

        /// <summary>Throws (wrapped) from the named read-path activity on the given 1-based call.</summary>
        public Dictionary<string, int> FailReadActivityOnCall { get; } = new();

        /// <summary>When set, the fallback full read also fails.</summary>
        public bool FullReadFails { get; set; }

        /// <summary>Uploads one page under the merge prefix whenever the incremental reader runs.</summary>
        public bool IncrementalReaderUploadsPage { get; set; }

        public Guid RunId => Request.SyncJob.RunId.GetValueOrDefault();
        public string StagingPath => GroupPartStoragePaths.BuildStagingPath(Request.GroupId, RunId, Request.CurrentPart);
        public string SelectionFolder => GroupPartStoragePaths.BuildSelectionFolder(Request.GroupId, RunId, Request.CurrentPart);
        public string MergePrefix => GroupPartStoragePaths.BuildMergePrefix(Request.GroupId, RunId, Request.CurrentPart);

        public void AddMember(string type, Guid? id = null, bool knownToGraph = true)
        {
            var member = new AzureADUser
            {
                ObjectId = id ?? Guid.NewGuid(),
                SourceGroup = Request.SourceGroup.ObjectId
            };
            Members.Add(member);
            if (knownToGraph)
            {
                Pipeline.DirectoryTypes[member.ObjectId] = type;
            }

            TrueTypes[member.ObjectId] = type;
        }

        public Dictionary<Guid, string> TrueTypes { get; } = new();

        public Task<SubOrchestratorResponse> RunAsync() => Function.RunSubOrchestratorAsync(Context.Object);

        /// <summary>Every published part file of this run and part.</summary>
        public IReadOnlyList<string> PartFiles => Storage.Paths
            .Where(path => path.StartsWith($"{Request.GroupId}/", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains("/userUploads/", StringComparison.OrdinalIgnoreCase) &&
                           path.EndsWith($"_{RunId}_GroupMembership_{Request.CurrentPart}.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        public IReadOnlyList<string> SelectionArtifacts => Storage.Paths
            .Where(path => path.StartsWith(SelectionFolder, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(path, StagingPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        public IReadOnlyList<Guid> SelectedTrueMembers =>
            Members.Where(member => Selects(TrueTypes[member.ObjectId])).Select(member => member.ObjectId).ToList();

        public bool Selects(string type)
        {
            var names = GroupIdentitySelection.Resolve(Request.IdentityTypes).ToTypeNames();
            return type == "user" ? names.Contains("users") : names.Contains("agents");
        }

        public async Task<List<Guid>> ReadPartMemberIdsAsync(string path)
        {
            var ids = new List<Guid>();
            await foreach (var member in Storage.StreamMembershipAsync(path))
            {
                ids.Add(member.ObjectId);
            }

            return ids;
        }

        public IEnumerable<RecordingLogger<SubOrchestratorFunction>.LogEntry> FallbackEntries =>
            Logger.Entries.Where(entry => entry.EventId.Id == 150049);

        public IEnumerable<RecordingLogger<SubOrchestratorFunction>.LogEntry> CompletionEntries =>
            Logger.Entries.Where(entry => entry.EventId.Id == 150202);

        private int _readCalls;

        private Task Track(string name)
        {
            Sequence.Add(name);
            var calls = Sequence.Count(entry => entry == name);
            if (FailReadActivityOnCall.TryGetValue(name, out var failing) && failing == calls)
            {
                return ActivityCallWrapper.InvokeAsync(name, () => Task.FromException(new KeyNotFoundException($"Injected {name} failure.")));
            }

            return Task.CompletedTask;
        }

        private void Configure()
        {
            Context.Setup(c => c.GetInput<GroupMembershipRequest>()).Returns(Request);
            Context.Setup(c => c.CreateReplaySafeLogger(It.IsAny<string>())).Returns(Logger);
            Context.SetupGet(c => c.IsReplaying).Returns(false);
            Pipeline.Wire(Context, Sequence);

            Returns(nameof(GroupValidatorFunction), () => Task.FromResult(true));
            Returns(nameof(GetTransitiveGroupCountFunction), () => Task.FromResult(0));
            Returns(nameof(FileDownloaderFunction), () => Task.FromResult(
                SavedDeltaLink ? TextCompressor.Compress("saved-delta") : string.Empty));
            Returns(nameof(BlobCheckerFunction), () => Task.FromResult(SavedDeltaLink
                ? new BlobResult { BlobStatus = BlobStatus.Found, Path = "cache/source" }
                : new BlobResult { BlobStatus = BlobStatus.NotFound }));
            Returns(nameof(DeltaUserReaderFunction), async () =>
            {
                await UploadIncrementalPageAsync();
                return new DeltaUrls { DeltaUrl = "new-delta" };
            });
            Returns(nameof(DeltaLinkUserReaderFunction), async () =>
            {
                await UploadIncrementalPageAsync();
                return new DeltaUrls { DeltaUrl = "new-delta" };
            });
            Returns(nameof(GetUserCountFunction), () => Task.FromResult(Members.Count));
            Returns(nameof(GetAgentUserCountFunction), () => Task.FromResult<int?>(0));
            Returns(nameof(MembersReaderFunction), async () =>
            {
                if (FullReadFails)
                {
                    throw new InvalidOperationException("The selected full read failed.");
                }

                // The selected Graph query returns only the selected type.
                await Storage.UploadFileAsync(
                    MergePrefix + Guid.NewGuid() + ".json",
                    JsonSerializer.Serialize(Members.Where(member => Selects(TrueTypes[member.ObjectId])).ToList()));
                return (string)null;
            });

            Context
                .Setup(c => c.CallActivityAsync<GroupMembershipFileResult>(
                    It.Is<TaskName>(name => name.Name == nameof(TransitiveAndDeltaUsersSenderFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>(async (_, input, _) =>
                {
                    var request = (TransitiveAndDeltaUsersSenderRequest)input;
                    await Track(nameof(TransitiveAndDeltaUsersSenderFunction));
                    MergedPageCounts.Add(Storage.Paths.Count(path => path.StartsWith(MergePrefix, StringComparison.Ordinal)));
                    var partPath = PartPath(request.FileTimestamp);
                    var target = request.WriteStaging ? StagingPath : partPath;
                    var merged = await Storage.MergeAndStreamUserBlobsWriteOnceAsync(
                        MergePrefix,
                        target,
                        new AzureADGroup { ObjectId = Request.GroupId },
                        RunId,
                        Request.SyncJob.Id,
                        Request.Exclusionary,
                        false,
                        Request.SyncJob.Query);
                    return new GroupMembershipFileResult { FilePath = partPath, MemberCount = merged.Count };
                });
            Context
                .Setup(c => c.CallActivityAsync<ProcessCachedAndDeltaUsersResponse>(
                    It.Is<TaskName>(name => name.Name == nameof(ProcessCachedAndDeltaUsersFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>(async (_, input, _) =>
                {
                    var request = (ProcessCachedAndDeltaUsersRequest)input;
                    await Track(nameof(ProcessCachedAndDeltaUsersFunction));
                    await SelectionPipelineHarness.WriteDeltaLinkStagingAsync(
                        Storage,
                        StagingPath,
                        Request.GroupId,
                        Request.SyncJob,
                        Members.Select(member => member.ObjectId).ToList(),
                        Request.Exclusionary);
                    return new ProcessCachedAndDeltaUsersResponse
                    {
                        CacheMatchesGroupCount = true,
                        CacheCount = Members.Count,
                        MembershipFilePath = PartPath(request.FileTimestamp),
                        StagingPath = StagingPath
                    };
                });
            Context
                .Setup(c => c.CallActivityAsync(
                    It.Is<TaskName>(name => name.Name == nameof(DeleteBlobFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>(async (_, input, _) =>
                {
                    var request = (DeleteBlobRequest)input;
                    await Track(nameof(DeleteBlobFunction));
                    var prefixes = request.Prefixes is { Count: > 0 } ? request.Prefixes : new List<string> { MergePrefix };
                    await Storage.DeleteListedBlobsAsync(Array.Empty<string>(), prefixes);
                });
            foreach (var name in new[]
                     {
                         nameof(CacheUploaderFunction), nameof(DeltaLinkUploaderFunction), nameof(FileDeleterFunction),
                         nameof(JobStatusUpdaterFunction)
                     })
            {
                Context
                    .Setup(c => c.CallActivityAsync(
                        It.Is<TaskName>(taskName => taskName.Name == name),
                        It.IsAny<object>(),
                        It.IsAny<TaskOptions>()))
                    .Returns(() => Track(name));
            }
        }

        private async Task UploadIncrementalPageAsync()
        {
            _readCalls++;
            if (IncrementalReaderUploadsPage || !SavedDeltaLink)
            {
                // The initial read's unfiltered pages, merged into staging.
                await Storage.UploadFileAsync(
                    MergePrefix + Guid.NewGuid() + ".json",
                    JsonSerializer.Serialize(Members));
            }
        }

        private string PartPath(DateTime? fileTimestamp) =>
            $"{Request.GroupId}/{(fileTimestamp ?? DateTime.UtcNow):MMddyyyy-HHmm}_{RunId}_GroupMembership_{Request.CurrentPart}.json";

        private void Returns<T>(string activityName, Func<Task<T>> result)
        {
            Context
                .Setup(c => c.CallActivityAsync<T>(
                    It.Is<TaskName>(name => name.Name == activityName),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns(async () =>
                {
                    await Track(activityName);
                    return await ActivityCallWrapper.InvokeAsync(activityName, result);
                });
        }
    }
}

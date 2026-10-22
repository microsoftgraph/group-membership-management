// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.Services.Infrastructure
{
    /// <summary>A clock that tests move by hand. Timers stay on the system clock.</summary>
    internal sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset? start = null)
        {
            // Starts at the real time so blob LastModified values (set from the real clock) line up with it.
            _now = start ?? DateTimeOffset.UtcNow;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;

        public void Set(DateTimeOffset now) => _now = now;
    }

    /// <summary>
    /// Runs the real selection-pipeline activities (capability, prepare, classify, finalize, record types, cleanup)
    /// and the real child orchestration behind a mocked <see cref="TaskOrchestrationContext"/>, with the in-memory
    /// blob double and a configurable <c>getByIds</c> stub. Activity failures reach the orchestrator as
    /// <see cref="TaskFailedException"/>, as in the isolated worker (no mocked activity results: V6.12).
    /// </summary>
    internal sealed class SelectionPipelineHarness
    {
        public SelectionPipelineHarness(InMemoryBlobStorageRepository storage, ManualTimeProvider clock = null)
        {
            Storage = storage;
            Clock = clock ?? new ManualTimeProvider();
            GraphRepository = new Mock<IGraphGroupRepository>();
            GraphRepository
                .Setup(repository => repository.GetDirectoryObjectTypesAsync(
                    It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IReadOnlyCollection<Guid>, Guid?, CancellationToken>(
                    (ids, runId, token) =>
                    {
                        LookupBatches.Add(ids.ToArray());
                        return Lookup(ids, runId, token);
                    });
            OrchestrationNow = Clock.GetUtcNow().UtcDateTime;
            Rebuild();
        }

        public InMemoryBlobStorageRepository Storage { get; }
        public ManualTimeProvider Clock { get; }
        public Mock<IGraphGroupRepository> GraphRepository { get; }

        /// <summary>The type of each member Graph knows; members absent here are omitted by getByIds.</summary>
        public Dictionary<Guid, string> DirectoryTypes { get; } = new();

        /// <summary>Overrides the lookup; by default it answers from <see cref="DirectoryTypes"/>.</summary>
        public Func<IReadOnlyCollection<Guid>, Guid?, CancellationToken, Task<IReadOnlyDictionary<Guid, string>>> LookupOverride { get; set; }

        public List<Guid[]> LookupBatches { get; } = new();

        /// <summary>Throws (wrapped as TaskFailedException) before the named activity runs.</summary>
        public Func<string, object, Exception> FailBefore { get; set; }

        /// <summary>Throws (wrapped as TaskFailedException) after the named activity ran and its writes succeeded.</summary>
        public Func<string, object, Exception> FailAfter { get; set; }

        /// <summary>The orchestration's replay-safe time; timers advance it and the activities' clock together.</summary>
        public DateTime OrchestrationNow { get; set; }
        public List<TimeSpan> TimerWaits { get; } = new();
        public List<int> ChildOrchestrationActivityCounts { get; } = new();

        /// <summary>
        /// Fails a child orchestration that throws as a <see cref="TaskFailedException"/> carrying only the child's own
        /// failure, with no inner failure details: the least the parent can rely on from Durable.
        /// </summary>
        public bool ChildFailuresHideInnerFailure { get; set; }
        public List<object> ActivityRequests { get; } = new();

        public RecordingLogger<GroupPartSelectionPrepareFunction> PrepareLogger { get; } = new();
        public RecordingLogger<GroupPartSegmentClassifyFunction> ClassifyLogger { get; } = new();
        public RecordingLogger<GroupPartSelectionFinalizeFunction> FinalizeLogger { get; } = new();
        public RecordingLogger<GroupPartRecordTypesFunction> RecordTypesLogger { get; } = new();
        public RecordingLogger<GroupPartCleanupFunction> CleanupLogger { get; } = new();

        public GroupPartSelectionPrepareFunction Prepare { get; private set; }
        public GroupPartSegmentClassifyFunction Classify { get; private set; }
        public GroupPartSelectionFinalizeFunction Finalize { get; private set; }
        public GroupPartRecordTypesFunction RecordTypes { get; private set; }
        public GroupPartCleanupFunction Cleanup { get; private set; }

        /// <summary>Recreates the activities.</summary>
        public void Rebuild()
        {
            Prepare = new GroupPartSelectionPrepareFunction(Storage, PrepareLogger);
            Classify = new GroupPartSegmentClassifyFunction(Storage, GraphRepository.Object, ClassifyLogger);
            Finalize = new GroupPartSelectionFinalizeFunction(Storage, FinalizeLogger);
            RecordTypes = new GroupPartRecordTypesFunction(Storage, RecordTypesLogger, Clock)
            {
                RetryDelay = _ => TimeSpan.FromMilliseconds(1)
            };
            Cleanup = new GroupPartCleanupFunction(Storage, CleanupLogger);
        }

        public int LookupCallCount => LookupBatches.Count;

        public IReadOnlyList<(int EventId, LogLevel Level, string Message)> AllSelectionLogEntries =>
            PrepareLogger.Entries.Select(e => (e.EventId.Id, e.Level, e.Message))
                .Concat(ClassifyLogger.Entries.Select(e => (e.EventId.Id, e.Level, e.Message)))
                .Concat(FinalizeLogger.Entries.Select(e => (e.EventId.Id, e.Level, e.Message)))
                .Concat(RecordTypesLogger.Entries.Select(e => (e.EventId.Id, e.Level, e.Message)))
                .Concat(CleanupLogger.Entries.Select(e => (e.EventId.Id, e.Level, e.Message)))
                .ToList();

        private Task<IReadOnlyDictionary<Guid, string>> Lookup(
            IReadOnlyCollection<Guid> ids,
            Guid? runId,
            CancellationToken token)
        {
            if (LookupOverride != null)
            {
                return LookupOverride(ids, runId, token);
            }

            IReadOnlyDictionary<Guid, string> result = ids
                .Where(DirectoryTypes.ContainsKey)
                .ToDictionary(id => id, id => DirectoryTypes[id]);
            return Task.FromResult(result);
        }

        /// <summary>Wires the pipeline activities, timers and child orchestrations into <paramref name="context"/>.</summary>
        public void Wire(Mock<TaskOrchestrationContext> context, List<string> sequence)
        {
            context.SetupGet(c => c.CurrentUtcDateTime).Returns(() => OrchestrationNow);
            context
                .Setup(c => c.CreateTimer(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns<DateTime, CancellationToken>((fireAt, _) =>
                {
                    sequence.Add("Timer");
                    TimerWaits.Add(fireAt - OrchestrationNow);
                    OrchestrationNow = fireAt;
                    if (new DateTimeOffset(fireAt) > Clock.GetUtcNow())
                    {
                        Clock.Set(new DateTimeOffset(fireAt));
                    }

                    return Task.CompletedTask;
                });

            Route<GroupPartPrepareResult, GroupPartSelectionRequest>(
                context, sequence, nameof(GroupPartSelectionPrepareFunction), r => Prepare.RunAsync(Clone(r), CancellationToken.None));
            Route<GroupPartSegmentResult, GroupPartSelectionRequest>(
                context, sequence, nameof(GroupPartSegmentClassifyFunction), r => Classify.RunAsync(Clone(r), CancellationToken.None));
            Route<GroupPartSelectionResult, GroupPartSelectionRequest>(
                context, sequence, nameof(GroupPartSelectionFinalizeFunction), r => Finalize.RunAsync(Clone(r), CancellationToken.None));
            Route<GroupPartRecordTypesResult, GroupPartSelectionRequest>(
                context, sequence, nameof(GroupPartRecordTypesFunction), r => RecordTypes.RunAsync(Clone(r), CancellationToken.None));
            // Cleanup before a replacement read uses its result; the final cleanup ignores it.
            Route<GroupPartCleanupResult, GroupPartCleanupRequest>(
                context, sequence, nameof(GroupPartCleanupFunction), r => Cleanup.RunAsync(r));
            context
                .Setup(c => c.CallActivityAsync(
                    It.Is<TaskName>(name => name.Name == nameof(GroupPartCleanupFunction)),
                    It.IsAny<GroupPartCleanupRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, GroupPartCleanupRequest, TaskOptions>(async (_, request, _) =>
                {
                    sequence.Add(nameof(GroupPartCleanupFunction));
                    ActivityRequests.Add(request);
                    await ActivityCallWrapper.InvokeAsync(nameof(GroupPartCleanupFunction), async () =>
                    {
                        ThrowIfInjected(FailBefore, nameof(GroupPartCleanupFunction), request);
                        await Cleanup.RunAsync(request);
                        ThrowIfInjected(FailAfter, nameof(GroupPartCleanupFunction), request);
                    });
                });
            context
                .Setup(c => c.CallSubOrchestratorAsync<GroupPartSegmentBatchResult>(
                    It.Is<TaskName>(name => name.Name == nameof(GroupPartSegmentBatchOrchestratorFunction)),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>(async (_, input, _) =>
                {
                    sequence.Add(nameof(GroupPartSegmentBatchOrchestratorFunction));
                    var childSequence = new List<string>();
                    var child = new Mock<TaskOrchestrationContext>();
                    child.Setup(c => c.GetInput<GroupPartSegmentBatchRequest>())
                        .Returns(JsonSerializer.Deserialize<GroupPartSegmentBatchRequest>(JsonSerializer.Serialize(input)));
                    child.Setup(c => c.CreateReplaySafeLogger(It.IsAny<string>()))
                        .Returns(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
                    child.SetupGet(c => c.IsReplaying).Returns(false);
                    Wire(child, childSequence);
                    GroupPartSegmentBatchResult result;
                    try
                    {
                        result = await new GroupPartSegmentBatchOrchestratorFunction().RunAsync(child.Object);
                    }
                    catch (Exception exception) when (ChildFailuresHideInnerFailure)
                    {
                        throw new TaskFailedException(
                            nameof(GroupPartSegmentBatchOrchestratorFunction),
                            -1,
                            new TaskFailureDetails(exception.GetType().FullName, exception.Message, null, null, null));
                    }
                    ChildOrchestrationActivityCounts.Add(childSequence.Count);
                    sequence.AddRange(childSequence.Select(name => "child:" + name));
                    // Round-trip as Durable does, so the result is the serialized history value.
                    return JsonSerializer.Deserialize<GroupPartSegmentBatchResult>(JsonSerializer.Serialize(result));
                });
        }

        private void Route<TResult, TRequest>(
            Mock<TaskOrchestrationContext> context,
            List<string> sequence,
            string activityName,
            Func<TRequest, Task<TResult>> run)
        {
            context
                .Setup(c => c.CallActivityAsync<TResult>(
                    It.Is<TaskName>(name => name.Name == activityName),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .Returns<TaskName, object, TaskOptions>(async (_, input, _) =>
                {
                    sequence.Add(activityName);
                    var request = (TRequest)input;
                    ActivityRequests.Add(request);
                    var result = await ActivityCallWrapper.InvokeAsync(activityName, async () =>
                    {
                        ThrowIfInjected(FailBefore, activityName, request);
                        var value = await run(request);
                        ThrowIfInjected(FailAfter, activityName, request);
                        return value;
                    });

                    // Durable records the serialized result in history.
                    return JsonSerializer.Deserialize<TResult>(JsonSerializer.Serialize(result));
                });
        }

        private static void ThrowIfInjected(Func<string, object, Exception> injector, string activityName, object request)
        {
            var exception = injector?.Invoke(activityName, request);
            if (exception != null)
            {
                throw exception;
            }
        }

        private static GroupPartSelectionRequest Clone(GroupPartSelectionRequest request) =>
            JsonSerializer.Deserialize<GroupPartSelectionRequest>(JsonSerializer.Serialize(request));

        /// <summary>Writes an initial-path staging blob (full records, as the merge writes it).</summary>
        public static Task WriteInitialStagingAsync(
            InMemoryBlobStorageRepository storage,
            string stagingPath,
            Guid targetGroupId,
            SyncJob syncJob,
            IReadOnlyCollection<AzureADUser> members,
            bool exclusionary = false) =>
            storage.UploadFileAsync(
                stagingPath,
                JsonSerializer.Serialize(new GroupMembership
                {
                    Destination = new AzureADGroup { ObjectId = targetGroupId },
                    RunId = syncJob.RunId.GetValueOrDefault(),
                    SyncJobId = syncJob.Id,
                    Exclusionary = exclusionary,
                    Query = syncJob.Query,
                    // The merge writes staging in canonical (ascending object identifier) order.
                    SourceMembers = members.OrderBy(member => member.ObjectId).ToList()
                }));

        /// <summary>Writes a saved-delta staging blob (identifier only, as saved-delta processing writes it).</summary>
        public static Task WriteDeltaLinkStagingAsync(
            InMemoryBlobStorageRepository storage,
            string stagingPath,
            Guid targetGroupId,
            SyncJob syncJob,
            IReadOnlyCollection<Guid> memberIds,
            bool exclusionary = false) =>
            storage.UploadGroupMembershipFromGuidsWriteOnceAsync(
                stagingPath,
                memberIds,
                new AzureADGroup { ObjectId = targetGroupId },
                syncJob.RunId.GetValueOrDefault(),
                syncJob.Id,
                exclusionary,
                syncJob.IsDryRunEnabled,
                syncJob.Query);
    }
}

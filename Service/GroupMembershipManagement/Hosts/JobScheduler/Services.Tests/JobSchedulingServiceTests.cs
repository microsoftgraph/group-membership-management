// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MockDatabaseSyncJobRepository = Repositories.SyncJobs.Tests.MockDatabaseSyncJobRepository;

namespace Services.Tests
{
    [TestClass]
    public class JobSchedulingServiceTests
    {
        public int DEFAULT_RUNTIME_SECONDS = 60;
        public int START_TIME_DELAY_MINUTES = 60;
        public int BUFFER_SECONDS = 10;

        private JobSchedulingService _jobSchedulingService = null;
        private MockDatabaseSyncJobRepository _mockDatabaseSyncJobRepository = null;
        private DefaultRuntimeRetrievalService _defaultRuntimeRetrievalService = null;
        private LogsRuntimeRetrievalService _logsRuntimeRetrievalService = null;
        private Mock<IJobSchedulerConfig> _jobSchedulerConfig = new Mock<IJobSchedulerConfig>();
        private Mock<LogsQueryClient> _logsQueryClient = new Mock<LogsQueryClient>();

        [TestInitialize]
        public void InitializeTest()
        {
            _jobSchedulerConfig.Setup(x => x.DefaultRuntimeSeconds).Returns(DEFAULT_RUNTIME_SECONDS);
            _mockDatabaseSyncJobRepository = new MockDatabaseSyncJobRepository();
            _defaultRuntimeRetrievalService = new DefaultRuntimeRetrievalService(_jobSchedulerConfig.Object.DefaultRuntimeSeconds);
            _logsRuntimeRetrievalService = new LogsRuntimeRetrievalService(_jobSchedulerConfig.Object, _logsQueryClient.Object);

            _jobSchedulingService = new JobSchedulingService(
                _mockDatabaseSyncJobRepository,
                _defaultRuntimeRetrievalService,
                NullLogger<JobSchedulingService>.Instance
            );
        }

        [TestMethod]
        public void ResetAllScheduledDates()
        {
            List<DistributionSyncJob> jobs = CreateSampleSyncJobs(10, 1);
            DateTime newStartTime = DateTime.UtcNow;

            List<DistributionSyncJob> updatedJobs = _jobSchedulingService.ResetJobStartTimes(jobs, newStartTime);

            Assert.AreEqual(jobs.Count, updatedJobs.Count);

            foreach (DistributionSyncJob job in updatedJobs)
            {
                Assert.AreEqual(job.ScheduledDate, newStartTime);
            }
        }

        [TestMethod]
        public async Task ScheduleJobsNone()
        {
            List<DistributionSyncJob> jobs = new List<DistributionSyncJob>();

            List<DistributionSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(jobs, START_TIME_DELAY_MINUTES, BUFFER_SECONDS);

            Assert.AreEqual(updatedJobs.Count, 0);
        }

        [TestMethod]
        public async Task ScheduleJobsOne()
        {
            DateTime dateTimeNow = DateTime.UtcNow;
            List<DistributionSyncJob> jobs = CreateSampleSyncJobs(1, 1);
            List<DistributionSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(jobs, START_TIME_DELAY_MINUTES, BUFFER_SECONDS);

            Assert.AreEqual(updatedJobs.Count, 1);
            Assert.IsTrue(updatedJobs[0].ScheduledDate > dateTimeNow);
        }

        [TestMethod]
        public async Task ScheduleJobsMultipleWithPriority()
        {
            DateTime dateTimeNow = DateTime.UtcNow;
            List<DistributionSyncJob> jobs = CreateSampleSyncJobs(10, 1, dateTimeNow.Date.AddDays(-20), dateTimeNow.Date);

            List<DistributionSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(jobs, START_TIME_DELAY_MINUTES, BUFFER_SECONDS);

            jobs.Sort();
            updatedJobs.Sort();

            Assert.AreEqual(jobs.Count, updatedJobs.Count);

            for (int i = 0; i < jobs.Count; i++)
            {
                Assert.AreEqual(jobs[i].Id, updatedJobs[i].Id);
                Assert.IsTrue(jobs[i].ScheduledDate < dateTimeNow);
                Assert.IsTrue(updatedJobs[i].ScheduledDate >= dateTimeNow.AddSeconds(60 * START_TIME_DELAY_MINUTES +
                    i * (DEFAULT_RUNTIME_SECONDS + BUFFER_SECONDS)));
            }
        }

        [TestMethod]
        public async Task ScheduleJobsWithConcurrency()
        {
            int defaultTenMinuteRuntime = 600;
            _jobSchedulerConfig.Setup(x => x.DefaultRuntimeSeconds).Returns(defaultTenMinuteRuntime);
            var longerDefaultRuntimeService = new DefaultRuntimeRetrievalService(_jobSchedulerConfig.Object.DefaultRuntimeSeconds);

            JobSchedulerConfig jobSchedulerConfig = new JobSchedulerConfig(
                                                        true, 0, true,
                                                        START_TIME_DELAY_MINUTES,
                                                        BUFFER_SECONDS,
                                                        DEFAULT_RUNTIME_SECONDS,
                                                        false,
                                                        "max",
                                                        "query",
                                                        7,
                                                        "workspace-id");
            JobSchedulingService jobSchedulingService = new JobSchedulingService(
                _mockDatabaseSyncJobRepository,
                longerDefaultRuntimeService,
                NullLogger<JobSchedulingService>.Instance
            );

            DateTime dateTimeNow = DateTime.UtcNow.Date;
            List<DistributionSyncJob> jobs = CreateSampleSyncJobs(10, 1, dateTimeNow.Date.AddDays(-20), dateTimeNow.Date);

            List<DistributionSyncJob> updatedJobs = await jobSchedulingService.DistributeJobStartTimesAsync(jobs, START_TIME_DELAY_MINUTES, BUFFER_SECONDS);

            jobs.Sort();
            updatedJobs.Sort();

            Assert.AreEqual(jobs.Count, updatedJobs.Count);
            Assert.AreEqual(jobs.Count, 10);

            // Check that times are sorted like this with concurrency of 2:
            // 0  2  4  6  8  9
            // 1  3  5  7
            for (int i = 0; i < jobs.Count; i++)
            {
                Assert.AreEqual(jobs[i].Id, updatedJobs[i].Id);
                Assert.IsTrue(jobs[i].ScheduledDate < dateTimeNow);
                if (i < 8)
                {
                    Assert.IsTrue(updatedJobs[i].ScheduledDate >= dateTimeNow.AddSeconds(60 * START_TIME_DELAY_MINUTES +
                        i / 2 * (defaultTenMinuteRuntime + BUFFER_SECONDS)));
                }
                else
                {
                    Assert.IsTrue(updatedJobs[i].ScheduledDate >= dateTimeNow.AddSeconds(60 * START_TIME_DELAY_MINUTES +
                        (i - 5) * (defaultTenMinuteRuntime + BUFFER_SECONDS)));
                }
            }
        }

        [TestMethod]
        public async Task ScheduleJobsWithTwoDifferentPeriods()
        {
            DateTime dateTimeNow = DateTime.UtcNow;
            List<DistributionSyncJob> jobs = CreateSampleSyncJobs(3, 1, dateTimeNow.Date.AddDays(-20), dateTimeNow.Date);
            jobs.AddRange(CreateSampleSyncJobs(3, 24, dateTimeNow.Date.AddDays(-20), dateTimeNow.Date));

            List<DistributionSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(jobs, START_TIME_DELAY_MINUTES, BUFFER_SECONDS);

            jobs.Sort(new PeriodComparer());
            updatedJobs.Sort(new PeriodComparer());

            for (int i = 0; i < jobs.Count; i++)
            {
                Assert.AreEqual(jobs[i].Id, updatedJobs[i].Id);
                Assert.IsTrue(jobs[i].ScheduledDate < dateTimeNow);

                if (i < 3)
                {
                    Assert.IsTrue(updatedJobs[i].ScheduledDate >= dateTimeNow.AddSeconds(60 * START_TIME_DELAY_MINUTES +
                        i * (DEFAULT_RUNTIME_SECONDS + BUFFER_SECONDS)));
                }
                else
                {
                    Assert.IsTrue(updatedJobs[i].ScheduledDate >= dateTimeNow.AddSeconds(60 * START_TIME_DELAY_MINUTES +
                        (i - 3) * (DEFAULT_RUNTIME_SECONDS + BUFFER_SECONDS)));
                }
            }
        }

        [TestMethod]
        public async Task ScheduleJobsWithThresholdPrioritization()
        {
            // Create jobs with and without thresholds
            DateTime dateTimeNow = DateTime.UtcNow;
            var jobs = new List<DistributionSyncJob>();
            
            // Jobs without thresholds (-1 means no threshold)
            for (int i = 0; i < 3; i++)
            {
                jobs.Add(new DistributionSyncJob
                {
                    Id = Guid.NewGuid(),
                    Period = 1,
                    ScheduledDate = dateTimeNow.AddDays(-1),
                    Status = SyncStatus.Idle.ToString(),
                    LastRunTime = dateTimeNow.AddDays(-1),
                    ThresholdPercentageForAdditions = -1,
                    ThresholdPercentageForRemovals = -1
                });
            }

            // Jobs with thresholds
            for (int i = 0; i < 2; i++)
            {
                jobs.Add(new DistributionSyncJob
                {
                    Id = Guid.NewGuid(),
                    Period = 1,
                    ScheduledDate = dateTimeNow.AddDays(-1),
                    Status = SyncStatus.Idle.ToString(),
                    LastRunTime = dateTimeNow.AddDays(-1),
                    ThresholdPercentageForAdditions = 50,
                    ThresholdPercentageForRemovals = 50
                });
            }

            // Distribute jobs WITH prioritization enabled
            List<DistributionSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(
                jobs, START_TIME_DELAY_MINUTES, BUFFER_SECONDS, prioritizeThresholdJobs: true);

            Assert.AreEqual(5, updatedJobs.Count);

            // Sort by scheduled date to see the order
            updatedJobs.Sort((a, b) => a.ScheduledDate.CompareTo(b.ScheduledDate));

            // First 2 jobs should have thresholds (scheduled first)
            Assert.IsTrue(updatedJobs[0].ThresholdPercentageForAdditions != -1 && updatedJobs[0].ThresholdPercentageForRemovals != -1);
            Assert.IsTrue(updatedJobs[1].ThresholdPercentageForAdditions != -1 && updatedJobs[1].ThresholdPercentageForRemovals != -1);

            // Last 3 jobs should NOT have thresholds (scheduled after)
            Assert.IsTrue(updatedJobs[2].ThresholdPercentageForAdditions == -1 || updatedJobs[2].ThresholdPercentageForRemovals == -1);
            Assert.IsTrue(updatedJobs[3].ThresholdPercentageForAdditions == -1 || updatedJobs[3].ThresholdPercentageForRemovals == -1);
            Assert.IsTrue(updatedJobs[4].ThresholdPercentageForAdditions == -1 || updatedJobs[4].ThresholdPercentageForRemovals == -1);
        }

        [TestMethod]
        public async Task ScheduleJobsWithoutThresholdPrioritization()
        {
            // Same jobs but WITHOUT prioritization - should use default sorting (Status, LastRunTime)
            DateTime dateTimeNow = DateTime.UtcNow;
            var jobs = new List<DistributionSyncJob>();

            // Jobs without thresholds, but with earlier LastRunTime (should normally be scheduled first)
            for (int i = 0; i < 2; i++)
            {
                jobs.Add(new DistributionSyncJob
                {
                    Id = Guid.NewGuid(),
                    Period = 1,
                    ScheduledDate = dateTimeNow.AddDays(-1),
                    Status = SyncStatus.Idle.ToString(),
                    LastRunTime = dateTimeNow.AddDays(-10 - i), // Earlier last run time
                    ThresholdPercentageForAdditions = -1,
                    ThresholdPercentageForRemovals = -1
                });
            }

            // Jobs with thresholds, but with more recent LastRunTime
            for (int i = 0; i < 2; i++)
            {
                jobs.Add(new DistributionSyncJob
                {
                    Id = Guid.NewGuid(),
                    Period = 1,
                    ScheduledDate = dateTimeNow.AddDays(-1),
                    Status = SyncStatus.Idle.ToString(),
                    LastRunTime = dateTimeNow.AddDays(-1 - i), // More recent last run time
                    ThresholdPercentageForAdditions = 50,
                    ThresholdPercentageForRemovals = 50
                });
            }

            // Distribute jobs WITHOUT prioritization (default behavior)
            List<DistributionSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(
                jobs, START_TIME_DELAY_MINUTES, BUFFER_SECONDS, prioritizeThresholdJobs: false);

            Assert.AreEqual(4, updatedJobs.Count);

            // Sort by scheduled date to see the order
            updatedJobs.Sort((a, b) => a.ScheduledDate.CompareTo(b.ScheduledDate));

            // Jobs with earlier LastRunTime should be scheduled first (regardless of threshold)
            // The first two jobs should be the ones without thresholds (earlier LastRunTime)
            Assert.IsTrue(updatedJobs[0].LastRunTime < updatedJobs[2].LastRunTime);
            Assert.IsTrue(updatedJobs[1].LastRunTime < updatedJobs[2].LastRunTime);
        }

        [TestMethod]
        public async Task ScheduleJobsOneFromLogs_MaxMetric()
        {
            _jobSchedulerConfig.Setup(x => x.GetRunTimeFromLogs).Returns(true);
            _jobSchedulerConfig.Setup(x => x.RunTimeMetric).Returns("MaxProcessingTime");
            _jobSchedulingService = new JobSchedulingService(
                                        _mockDatabaseSyncJobRepository,
                                        _logsRuntimeRetrievalService,
                                        NullLogger<JobSchedulingService>.Instance);

            var numberOfJobs = 5;
            var periodInHours = 1;
            var jobs = CreateSampleSyncJobs(numberOfJobs, periodInHours);
            var groupRuntimes = new List<(string Id, double Max, double Average)>();
            var max = 100.0;
            var avg = 5.0;
            foreach (var job in jobs)
            {
                groupRuntimes.Add((job.Id.ToString(), max++, avg++));
            }

            var queryResult = CreateLogsQueryResult(groupRuntimes);
            _logsQueryClient.Setup(x => x.QueryWorkspaceAsync(
                                            It.IsAny<string>(),
                                            It.IsAny<string>(),
                                            It.IsAny<QueryTimeRange>(),
                                            It.IsAny<LogsQueryOptions>(),
                                            It.IsAny<CancellationToken>()
                                            )
                                  ).ReturnsAsync(queryResult);

            DateTime dateTimeNow = DateTime.UtcNow;
            List<DistributionSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(jobs, START_TIME_DELAY_MINUTES, BUFFER_SECONDS);

            double totalTimeInSeconds = groupRuntimes.Select(x => x.Max).Sum() + (jobs.Count - groupRuntimes.Count) * DEFAULT_RUNTIME_SECONDS
                                      + jobs.Count * BUFFER_SECONDS;
            int concurrencyNumber = (int)Math.Ceiling(totalTimeInSeconds / (periodInHours * 3600));

            Assert.AreEqual(concurrencyNumber, 1);
            Assert.AreEqual(updatedJobs.Count, numberOfJobs);
            Assert.IsTrue(updatedJobs[0].ScheduledDate > dateTimeNow);

            var baseStartDate = updatedJobs.First().ScheduledDate;
            var currentJobIndex = 0;
            foreach (var updateJob in updatedJobs)
            {
                if (currentJobIndex > 0)
                {
                    var previousJobRunTime = groupRuntimes.First(x => x.Id.ToString() == updatedJobs[currentJobIndex - 1].Id.ToString());
                    baseStartDate = baseStartDate.AddSeconds(BUFFER_SECONDS + previousJobRunTime.Max);
                }

                Assert.AreEqual(updateJob.ScheduledDate, baseStartDate);
                currentJobIndex++;
            }
        }

        private List<DistributionSyncJob> CreateSampleSyncJobs(int numberOfJobs, int period, DateTime? scheduledDateBase = null, DateTime? lastRunTimeBase = null)
        {
            var jobs = new List<DistributionSyncJob>();
            DateTime ScheduledDateBase = scheduledDateBase ?? DateTime.UtcNow.AddDays(-1);
            DateTime LastRunTimeBase = lastRunTimeBase ?? DateTime.UtcNow.AddDays(-1);

            for (int i = 0; i < numberOfJobs; i++)
            {
                var job = new DistributionSyncJob
                {
                    Id = Guid.NewGuid(),
                    Period = period,
                    ScheduledDate = ScheduledDateBase.AddDays(-1 * i),
                    Status = SyncStatus.Idle.ToString(),
                    LastRunTime = LastRunTimeBase.AddDays(-1 * i)
                };

                jobs.Add(job);
            }

            return jobs;
        }

        private Response<LogsQueryResult> CreateLogsQueryResult(List<(string Destination, double Max, double Avg)> groupRuntimes)
        {
            var columns = new List<LogsTableColumn>();
            var columnNames = new[] { "Destination", "MaxProcessingTime", "AvgProcessingTime" };
            var logsTableColumnConstructor = typeof(LogsTableColumn).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                                                                                    new[] { typeof(string), typeof(LogsColumnType) });

            foreach (var name in columnNames)
            {
                var columnType = name == "Destination" ? LogsColumnType.Guid : LogsColumnType.Real;
                columns.Add(logsTableColumnConstructor.Invoke(new object[] { name, columnType }) as LogsTableColumn);
            }

            var rowsList = new List<string>();
            foreach (var group in groupRuntimes)
            {
                var destinationJson = JsonSerializer.Serialize(group.Destination);
                rowsList.Add($"[{destinationJson},{group.Max},{group.Avg}]");
            }

            var tableJSON = $"{{\"name\":\"PrimaryResult\"," +
                            $"\"columns\":[{{\"name\":\"Destination\",\"type\":\"string\"}},{{\"name\":\"MaxProcessingTime\",\"type\":\"real\"}},{{\"name\":\"AvgProcessingTime\",\"type\":\"real\"}}]," +
                            $"\"rows\":[{string.Join(",", rowsList)}]}}";

            var jsonDocument = JsonDocument.Parse(tableJSON);
            var jsonElementConstructor = typeof(JsonElement).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                                                                        new[] { typeof(JsonDocument), typeof(int) });

            var tableJsonElement = (JsonElement)jsonElementConstructor.Invoke(new object[] { jsonDocument, 0 });
            var logsTableConstructor = typeof(LogsTable).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                                                                        new[] { typeof(string), typeof(IEnumerable<LogsTableColumn>), typeof(JsonElement) });

            JsonElement rows = default;
            foreach (var property in tableJsonElement.EnumerateObject())
            {
                if (property.NameEquals("rows"))
                {
                    rows = property.Value.Clone();
                    break;
                }
            }

            var logsTable = logsTableConstructor.Invoke(new object[] { "table", columns, rows }) as LogsTable;
            var logsQueryResultConstructor = typeof(LogsQueryResult).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, new[] { typeof(IEnumerable<LogsTable>) });
            var logsQueryResult = logsQueryResultConstructor.Invoke(new object[] { new List<LogsTable> { logsTable } }) as LogsQueryResult;

            return Response.FromValue(logsQueryResult, new TestResponse());
        }
    }

    public class PeriodComparer : Comparer<DistributionSyncJob>
    {
        public override int Compare(DistributionSyncJob x, DistributionSyncJob y)
        {
            if (x.Period != y.Period)
            {
                return x.Period.CompareTo(y.Period);
            }

            return x.CompareTo(y);
        }
    }

    public class TestResponse : Response
    {
        public TestResponse()
        {
        }

        public override int Status { get; }

        public override string ReasonPhrase { get; }

        public override Stream ContentStream { get; set; }
        public override string ClientRequestId { get; set; }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }

        protected override bool ContainsHeader(string name)
        {
            throw new NotImplementedException();
        }

        protected override IEnumerable<HttpHeader> EnumerateHeaders()
        {
            throw new NotImplementedException();
        }

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string value)
        {
            throw new NotImplementedException();
        }

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string> values)
        {
            throw new NotImplementedException();
        }
    }
}

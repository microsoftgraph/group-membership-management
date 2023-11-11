// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MockDatabaseSyncJobRepository = Repositories.SyncJobs.Tests.MockDatabaseSyncJobRepository;
using Newtonsoft.Json;

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
        private MockLoggingRepository _mockLoggingRepository = null;
        private Mock<IJobSchedulerConfig> _jobSchedulerConfig = new Mock<IJobSchedulerConfig>();
        private Mock<LogsQueryClient> _logsQueryClient = new Mock<LogsQueryClient>();

        [TestInitialize]
        public void InitializeTest()
        {
            _jobSchedulerConfig.Setup(x => x.DefaultRuntimeSeconds).Returns(DEFAULT_RUNTIME_SECONDS);
            _mockDatabaseSyncJobRepository = new MockDatabaseSyncJobRepository();
            _defaultRuntimeRetrievalService = new DefaultRuntimeRetrievalService(_jobSchedulerConfig.Object.DefaultRuntimeSeconds);
            _logsRuntimeRetrievalService = new LogsRuntimeRetrievalService(_jobSchedulerConfig.Object, _logsQueryClient.Object);
            _mockLoggingRepository = new MockLoggingRepository();

            _jobSchedulingService = new JobSchedulingService(
                _mockDatabaseSyncJobRepository,
                _defaultRuntimeRetrievalService,
                _mockLoggingRepository
            );
        }

        [TestMethod]
        public void ResetAllStartTimes()
        {
            List<DistributionSyncJob> jobs = CreateSampleSyncJobs(10, 1);
            DateTime newStartTime = DateTime.UtcNow;

            List<DistributionSyncJob> updatedJobs = _jobSchedulingService.ResetJobStartTimes(jobs, newStartTime, false);

            Assert.AreEqual(jobs.Count, updatedJobs.Count);

            foreach (DistributionSyncJob job in updatedJobs)
            {
                Assert.AreEqual(job.StartDate, newStartTime);
            }
        }

        [TestMethod]
        public void ResetOlderStartTimes()
        {
            DateTime newStartTime = DateTime.UtcNow.Date;
            List<DistributionSyncJob> jobs = CreateSampleSyncJobs(10, 1, newStartTime.AddDays(4));

            List<DistributionSyncJob> updatedJobs = _jobSchedulingService.ResetJobStartTimes(jobs, newStartTime, false);

            Assert.AreEqual(jobs.Count, updatedJobs.Count);

            int startTimeUpdatedCount = 0;
            int startTimeNotUpdatedCount = 0;

            foreach (DistributionSyncJob job in updatedJobs)
            {
                if (job.StartDate == newStartTime)
                {
                    startTimeUpdatedCount++;
                }
                else
                {
                    startTimeNotUpdatedCount++;
                }
            }

            Assert.AreEqual(startTimeUpdatedCount, 6);
            Assert.AreEqual(startTimeNotUpdatedCount, 4);
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
            Assert.IsTrue(updatedJobs[0].StartDate > dateTimeNow);
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
                Assert.AreEqual(jobs[i].TargetOfficeGroupId, updatedJobs[i].TargetOfficeGroupId);
                Assert.IsTrue(jobs[i].StartDate < dateTimeNow);
                Assert.IsTrue(updatedJobs[i].StartDate >= dateTimeNow.AddSeconds(60 * START_TIME_DELAY_MINUTES +
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
                                                        true, 0, true, false,
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
                _mockLoggingRepository
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
                Assert.AreEqual(jobs[i].TargetOfficeGroupId, updatedJobs[i].TargetOfficeGroupId);
                Assert.IsTrue(jobs[i].StartDate < dateTimeNow);
                if (i < 8)
                {
                    Assert.IsTrue(updatedJobs[i].StartDate >= dateTimeNow.AddSeconds(60 * START_TIME_DELAY_MINUTES +
                        i / 2 * (defaultTenMinuteRuntime + BUFFER_SECONDS)));
                }
                else
                {
                    Assert.IsTrue(updatedJobs[i].StartDate >= dateTimeNow.AddSeconds(60 * START_TIME_DELAY_MINUTES +
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
                Assert.AreEqual(jobs[i].TargetOfficeGroupId, updatedJobs[i].TargetOfficeGroupId);
                Assert.IsTrue(jobs[i].StartDate < dateTimeNow);

                if (i < 3)
                {
                    Assert.IsTrue(updatedJobs[i].StartDate >= dateTimeNow.AddSeconds(60 * START_TIME_DELAY_MINUTES +
                        i * (DEFAULT_RUNTIME_SECONDS + BUFFER_SECONDS)));
                }
                else
                {
                    Assert.IsTrue(updatedJobs[i].StartDate >= dateTimeNow.AddSeconds(60 * START_TIME_DELAY_MINUTES +
                        (i - 3) * (DEFAULT_RUNTIME_SECONDS + BUFFER_SECONDS)));
                }
            }
        }

        [TestMethod]
        public async Task ScheduleJobsOneFromLogs_MaxMetric()
        {
            _jobSchedulerConfig.Setup(x => x.GetRunTimeFromLogs).Returns(true);
            _jobSchedulingService = new JobSchedulingService(
                                        _mockDatabaseSyncJobRepository,
                                        _logsRuntimeRetrievalService,
                                        _mockLoggingRepository);

            var numberOfJobs = 5;
            var periodInHours = 1;
            var jobs = CreateSampleSyncJobs(numberOfJobs, periodInHours);
            var groupRuntimes = new List<(string Destination, double Max, double Avg)>();
            var max = 100.0;
            var avg = 5.0;
            foreach (var job in jobs)
            {
                groupRuntimes.Add((job.Destination, max++, avg++));
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

            double totalTimeInSeconds = groupRuntimes.Select(x => x.Max).Sum() + (jobs.Count - groupRuntimes.Count) * DEFAULT_RUNTIME_SECONDS;
            int concurrencyNumber = (int)Math.Ceiling(totalTimeInSeconds / (periodInHours * 3600));

            Assert.AreEqual(concurrencyNumber, 1);
            Assert.AreEqual(updatedJobs.Count, numberOfJobs);
            Assert.IsTrue(updatedJobs[0].StartDate > dateTimeNow);

            var baseStartDate = updatedJobs.First().StartDate;
            var currentJobIndex = 0;
            foreach (var updateJob in updatedJobs)
            {
                if (currentJobIndex > 0)
                {
                    var previousJobRunTime = groupRuntimes.First(x => x.Destination == updatedJobs[currentJobIndex - 1].Destination);
                    baseStartDate = baseStartDate.AddSeconds(BUFFER_SECONDS + previousJobRunTime.Max);
                }

                Assert.AreEqual(updateJob.StartDate, baseStartDate);
                currentJobIndex++;
            }
        }

        private List<DistributionSyncJob> CreateSampleSyncJobs(int numberOfJobs, int period, DateTime? startDateBase = null, DateTime? lastRunTimeBase = null)
        {
            var jobs = new List<DistributionSyncJob>();
            DateTime StartDateBase = startDateBase ?? DateTime.UtcNow.AddDays(-1);
            DateTime LastRunTimeBase = lastRunTimeBase ?? DateTime.UtcNow.AddDays(-1);

            for (int i = 0; i < numberOfJobs; i++)
            {
                var job = new DistributionSyncJob
                {
                    Id = Guid.NewGuid(),
                    Period = period,
                    StartDate = StartDateBase.AddDays(-1 * i),
                    Status = SyncStatus.Idle.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    LastRunTime = LastRunTimeBase.AddDays(-1 * i),
                    Destination = $"[{{\"type\":\"GroupMembership\",\"value\":{{\"objectId\":\"{Guid.NewGuid()}\"}}}}]"
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
                var destinationJson = JsonConvert.ToString(group.Destination);
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

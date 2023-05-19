// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Entities;

namespace Services.Tests
{
    [TestClass]
    public class OwnershipReaderServiceTests
    {
        private Mock<IDryRunValue> _dryRunSettings = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<ISyncJobRepository> _syncJobRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<IBlobStorageRepository> _blobStorageRepository = null!;
        private OwnershipReaderService _ownershipReaderService = null!;

        [TestInitialize]
        public void Setup()
        {
            _dryRunSettings = new Mock<IDryRunValue>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<ISyncJobRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();

            _ownershipReaderService = new OwnershipReaderService(
                _dryRunSettings.Object,
                _loggingRepository.Object,
                _syncJobRepository.Object,
                _graphGroupRepository.Object,
                _blobStorageRepository.Object
                );
        }

        [TestMethod]
        [DataRow("All", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("Hybrid", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("SecurityGroup", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("CustomType1", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("CustomType2", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("SecurityGroup,CustomType1", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("SecurityGroup,CustomType1,CustomType2", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        public void TestExistingFilterSyncJobsBySourceTypes(string requestedSourceTypes, string jobSourceTypes)
        {
            var requestedTypes = new HashSet<string>(requestedSourceTypes.Split(','));
            var jobTypes = new HashSet<string>(jobSourceTypes.Split(','));
            var jobs = GenerateSampleJobs(jobTypes);

            var groupIds = _ownershipReaderService.FilterSyncJobsBySourceTypes(requestedTypes, jobs);

            var expectedGroupIds = new List<Guid>();
            if (requestedSourceTypes.Contains("All"))
            {
                expectedGroupIds = jobs.Select(x => x.TargetOfficeGroupId)
                                       .ToList();
            }
            else if (requestedSourceTypes.Contains("Hybrid"))
            {
                var hybridJobs = jobs.Where(x =>
                {
                    if (x.Query == null) return false;

                    var queryParts = JArray.Parse(x.Query);
                    var queryTypes = queryParts.SelectTokens("$..type")
                        .Select(x => x.Value<string>())
                        .Distinct()
                        .ToList();

                    return queryTypes.Count() > 1;

                }).ToList();

                expectedGroupIds = hybridJobs.Select(x => x.TargetOfficeGroupId)
                                             .ToList();
            }
            else
            {
                var requestedJobs = jobs.Where(x =>
                {
                    if (x.Query == null) return false;

                    var queryParts = JArray.Parse(x.Query);
                    var queryTypes = queryParts.SelectTokens("$..type")
                        .Select(x => x.Value<string>())
                        .Distinct()
                        .ToList();

                    return requestedTypes.All(x => queryTypes.Contains(x, StringComparer.InvariantCultureIgnoreCase))
                    && requestedTypes.Count == queryTypes.Count;

                }).ToList();

                expectedGroupIds = requestedJobs.Select(x => x.TargetOfficeGroupId)
                                                .ToList();
            }

            Assert.AreEqual(expectedGroupIds.Count, groupIds.Count);
            Assert.IsTrue(groupIds.Count > 0);
        }

        [TestMethod]
        [DataRow("SecurityGroup,CustomType2", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("CustomType1,CustomType2", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("type3", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("type3,SecurityGroup", "SecurityGroup,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        public void TestNonExistingFilterSyncJobsBySourceTypes(string requestedSourceTypes, string jobSourceTypes)
        {
            var requestedTypes = new HashSet<string>(requestedSourceTypes.Split(','));
            var jobTypes = new HashSet<string>(jobSourceTypes.Split(','));
            var jobs = GenerateSampleJobs(jobTypes);

            var groupIds = _ownershipReaderService.FilterSyncJobsBySourceTypes(requestedTypes, jobs);

            var expectedGroupIds = new List<Guid>();
            if (requestedSourceTypes.Contains("All"))
            {
                expectedGroupIds = jobs.Select(x => x.TargetOfficeGroupId)
                                       .ToList();
            }
            else if (requestedSourceTypes.Contains("Hybrid"))
            {
                var hybridJobs = jobs.Where(x =>
                {
                    if (x.Query == null) return false;

                    var queryParts = JArray.Parse(x.Query);
                    var queryTypes = queryParts.SelectTokens("$..type")
                        .Select(x => x.Value<string>())
                        .Distinct()
                        .ToList();

                    return queryTypes.Count() > 1;

                }).ToList();

                expectedGroupIds = hybridJobs.Select(x => x.TargetOfficeGroupId)
                                             .ToList();
            }
            else
            {
                var requestedJobs = jobs.Where(x =>
                {
                    if (x.Query == null) return false;

                    var queryParts = JArray.Parse(x.Query);
                    var queryTypes = queryParts.SelectTokens("$..type")
                        .Select(x => x.Value<string>())
                        .Distinct()
                        .ToList();

                    return requestedTypes.All(x => queryTypes.Contains(x, StringComparer.InvariantCultureIgnoreCase))
                    && requestedTypes.Count == queryTypes.Count;

                }).ToList();

                expectedGroupIds = requestedJobs.Select(x => x.TargetOfficeGroupId)
                                                .ToList();
            }

            Assert.AreEqual(expectedGroupIds.Count, groupIds.Count);
            Assert.AreEqual(0, groupIds.Count);
        }

        private List<JobsFilterSyncJob> GenerateSampleJobs(IEnumerable<string> sourceTypes)
        {
            var jobs = new List<JobsFilterSyncJob>();
            var queryParts = new List<string>();

            foreach (var sourceType in sourceTypes)
            {
                var queryPart = $"{{\"type\":\"{sourceType}\",\"source\":\"source-definition\"}}";
                queryParts.Add(queryPart);

                // individual sources
                jobs.Add(new JobsFilterSyncJob
                {
                    TargetOfficeGroupId = Guid.NewGuid(),
                    Query = $"[{queryPart}]"
                });

                // hybrid jobs
                if (queryParts.Count > 1)
                {
                    jobs.Add(new JobsFilterSyncJob
                    {
                        TargetOfficeGroupId = Guid.NewGuid(),
                        Query = $"[{string.Join(",", queryParts)}]"
                    });
                }
            }

            return jobs;
        }
    }
}


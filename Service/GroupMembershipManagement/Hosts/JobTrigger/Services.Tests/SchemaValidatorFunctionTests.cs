// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.JobTrigger;
using JobTrigger.Activity.SchemaValidator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using Services.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SchemaValidatorFunctionTests
    {
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IJobTriggerService> _jobTriggerService;
        private JsonSchemaProvider _jsonSchemaProvider;

        [TestInitialize]
        public void Setup()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _jobTriggerService = new Mock<IJobTriggerService>();
            _jsonSchemaProvider = SchemaProviderFactory.CreateJsonSchemaProvider();
        }

        [TestMethod]
        public async Task TestValidQueriesAsync()
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(_loggingRepository.Object, _jobTriggerService.Object, _jsonSchemaProvider);

            var syncJob = new SyncJob
            {
                RunId = Guid.Empty
            };

            foreach (var query in GetValidQueries())
            {
                syncJob.Query = query;
                var isValidJson = await schemaValidatorFunction.ValidateSchemasAsync(syncJob);
                Assert.IsTrue(isValidJson);
            }
        }

        [TestMethod]
        public async Task TestInvalidQueriesAsync()
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(_loggingRepository.Object, _jobTriggerService.Object, _jsonSchemaProvider);

            var syncJob = new SyncJob
            {
                RunId = Guid.Empty
            };

            foreach (var query in GetInvalidQueries())
            {
                syncJob.Query = query;
                var isValidJson = await schemaValidatorFunction.ValidateSchemasAsync(syncJob);
                Assert.IsFalse(isValidJson);
            }
        }

        public List<string> GetValidQueries()
        {
            return new List<string>
            {
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]" },
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]" },
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\",\"exclusionary\":true}]" },
                { "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]}]" },
                { "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]},{\"type\":\"GroupOwnership\",\"source\":[\"TeamsChannel\"]}]" },
                { "[{\"type\":\"TeamsChannel\",\"source\":{\"group\":\"00000000-0000-0000-0000-000000000000\",\"channel\":\"0:00000000000000000000000000000000@thread.tacv2\"}}]" }
            };
        }

        public List<string> GetInvalidQueries()
        {
            return new List<string>
            {
                { "[{\"types\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]" },
                { "[{\"type\":\"GroupMembership\",\"source\":123,{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]" },
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\",\"exclusionary\":\"abc\"}]" },
                { "[{\"type\":123,\"source\":[\"GroupMembership\"]}]" },
                { "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]},{\"type\":\"GroupOwnership\",\"source\":[\"TeamsChannel\"],\"test\":123}]" },
            };
        }
    }
}
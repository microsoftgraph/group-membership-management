// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.JobTrigger;
using JobTrigger.Activity.SchemaValidator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
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
        private Mock<IJobTriggerService> _jobTriggerService;
        private JsonSchemaProvider _jsonSchemaProvider;

        [TestInitialize]
        public void Setup()
        {
            _jobTriggerService = new Mock<IJobTriggerService>();
            _jsonSchemaProvider = SchemaProviderFactory.CreateJsonSchemaProvider();
        }

        [TestMethod]
        public async Task TestValidQueriesAsync()
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, _jobTriggerService.Object, _jsonSchemaProvider);

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
            var schemaValidatorFunction = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, _jobTriggerService.Object, _jsonSchemaProvider);

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

        [TestMethod]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"}}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"agents\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"users\",\"agents\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"agents\"]},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]")]
        public async Task TestValidSqlIdentityTypesQueriesAsync(string query)
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, _jobTriggerService.Object, _jsonSchemaProvider);
            var syncJob = new SyncJob { RunId = Guid.Empty, Query = query };

            Assert.IsTrue(await schemaValidatorFunction.ValidateSchemasAsync(syncJob));
        }

        [TestMethod]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\",\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"devices\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"users\",\"users\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":\"users\"}]")]
        public async Task TestInvalidSqlIdentityTypesQueriesAsync(string query)
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, _jobTriggerService.Object, _jsonSchemaProvider);
            var syncJob = new SyncJob { RunId = Guid.Empty, Query = query };

            Assert.IsFalse(await schemaValidatorFunction.ValidateSchemasAsync(syncJob));
        }

        public List<string> GetValidQueries()
        {
            return new List<string>
            {
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]" },
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]" },
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\",\"exclusionary\":true}]" },
                { "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]}]" },
                { "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]},{\"type\":\"GroupOwnership\",\"source\":[\"00000000-0000-0000-0000-000000000000\"]}]" },
                { "[{\"type\":\"TeamsChannelMembership\",\"source\":{\"group\":\"00000000-0000-0000-0000-000000000000\",\"channel\":\"0:00000000000000000000000000000000@thread.tacv2\"}}]" }
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
                { "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]},{\"type\":\"GroupOwnership\",\"source\":[\"TeamsChannelMembership\"],\"test\":123}]" },
            };
        }
    }
}
